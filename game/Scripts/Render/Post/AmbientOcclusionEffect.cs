using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Post;

/// <summary>
/// Optional depth-based contact shading of the opaque scene. It runs after the opaque pass and before the sky and
/// every blended surface, as one full-screen draw that multiplies the frame's own colour target (the multisampled one
/// with MSAA, so the later resolve keeps it). Smoke, gas, glass and other blended surfaces are drawn afterwards, over
/// the shaded scene: run after them, it darkened them by the shapes behind them, which showed through a cloud.
/// </summary>
public partial class AmbientOcclusionEffect : CompositorEffect
{
    private const string VertexSource = """
        #version 450
        void main() {
            vec2 corner = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 450
        layout(set = 0, binding = 0) uniform sampler2D depth_texture;
        layout(push_constant, std430) uniform Params {
            vec4 frame; // size, radius in metres, strength
            vec4 projection; // focal scales and asymmetric frustum offsets
            vec4 range; // near, far, near exclusion, distance fade
        } p;
        layout(location = 0) out vec4 shade;

        // Depths at or below this are the sky zone or nothing drawn (svm_sky_depth.gdshaderinc, 2^-40).
        const float SKY_DEPTH_LIMIT = 9.094947017729282e-13;

        vec3 position_at(ivec2 pixel) {
            pixel = clamp(pixel, ivec2(0), ivec2(p.frame.xy) - 1);
            float d = texelFetch(depth_texture, pixel, 0).r;
            float z = p.range.x * p.range.y / (d * (p.range.y - p.range.x) + p.range.x);
            vec2 uv = (vec2(pixel) + .5) / p.frame.xy;
            vec2 ndc = vec2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
            return vec3((ndc + p.projection.zw) / p.projection.xy * z, -z);
        }

        void main() {
            // The frame is multiplied by this: 1 leaves a pixel as it is.
            shade = vec4(1.0);
            ivec2 xy = ivec2(gl_FragCoord.xy), size = ivec2(p.frame.xy);
            if (any(greaterThanEqual(xy, size))) return;
            if (texelFetch(depth_texture, xy, 0).r <= SKY_DEPTH_LIMIT) return; // sky zone or nothing drawn
            vec3 center = position_at(xy);
            float z = -center.z;
            if (z <= p.range.z || z >= p.range.w) return; // held weapon and distant scene
            // Choose the derivative that stays on the same surface to avoid silhouette halos.
            vec3 left = center - position_at(xy - ivec2(1, 0));
            vec3 right = position_at(xy + ivec2(1, 0)) - center;
            vec3 up = center - position_at(xy - ivec2(0, 1));
            vec3 down = position_at(xy + ivec2(0, 1)) - center;
            vec3 cross_normal = cross(abs(left.z) < abs(right.z) ? left : right, abs(up.z) < abs(down.z) ? up : down);
            if (dot(cross_normal, cross_normal) < 1e-12) return;
            vec3 normal = normalize(cross_normal);
            if (dot(normal, -center) < 0.0) normal = -normal;
            float full = p.frame.z * p.projection.y * p.frame.y / (2.0 * z);
            float pixels = min(64.0, full);
            // The radius the capped kernel covers, so the falloff reaches zero at the last sample. With the full radius
            // a near occluder still had most of its weight where sampling stopped, and its darkening ended in a hard edge.
            float radius = p.frame.z * pixels / max(full, 1e-6);
            // Turn the directions and slide the steps per pixel (interleaved gradient noise). A fixed pattern stamped a
            // shifted copy of an occluder's outline onto the floor for every sample offset.
            float jitter = fract(52.9829189 * fract(dot(vec2(xy), vec2(.06711056, .00583715))));
            float total = 0.0;
            for (int dir = 0; dir < 8; dir++) {
                float angle = (float(dir) + jitter) * .7853981634;
                vec2 direction = vec2(cos(angle), sin(angle));
                float horizon = 0.0;
                for (int step = 1; step <= 3; step++) {
                    ivec2 sample_xy = xy + ivec2(round(direction * max(1.0, pixels * (float(step) - jitter) / 3.0)));
                    if (any(lessThan(sample_xy, ivec2(0))) || any(greaterThanEqual(sample_xy, size))) continue;
                    if (texelFetch(depth_texture, sample_xy, 0).r <= SKY_DEPTH_LIMIT) continue;
                    vec3 delta = position_at(sample_xy) - center;
                    float distance = length(delta);
                    if (distance < .001 || distance >= radius) continue;
                    float cosine = max(0.0, dot(normal, delta / distance) - .08);
                    horizon = max(horizon, cosine * (1.0 - distance * distance / (radius * radius)));
                }
                total += horizon;
            }
            float fade = smoothstep(p.range.z, p.range.z * 2.0, z) * (1.0 - smoothstep(p.range.w * .5, p.range.w, z));
            shade = vec4(vec3(1.0 - min(.45, total * p.frame.w / 8.0) * fade), 1.0);
        }
        """;

    public sealed record Frame(bool On, Vector4 Projection, float Near, float Far);
    private static readonly Color[] NoClear = [];
    private volatile Frame _frame = new(false, new(1, 1, 0, 0), .01f, 100f);
    private RenderingDevice? _rd;
    private Rid _shader, _sampler;
    // By framebuffer format: the colour target's format and sample count decide the pipeline.
    private readonly Dictionary<long, Rid> _pipelines = [];
    private readonly Godot.Collections.Array<Rid> _target = [default];
    private readonly Godot.Collections.Array<RDFramebufferPass> _passes = [];
    private UniformSlots? _slots;
    private readonly byte[] _push = new byte[48];
    private bool _released, _attempted;
    public int Draws { get; private set; }
    public string? Error { get; private set; }

    public AmbientOcclusionEffect()
    {
        Enabled = false;
        EffectCallbackType = EffectCallbackTypeEnum.PostOpaque;
        AccessResolvedDepth = true;
    }

    public static bool SupportsVision(int mode) => mode is RenderK.RendMapNormal or RenderK.RendMapNight;

    public void Configure(bool on, int mode, Camera3D camera)
    {
        Projection p = camera.GetCameraProjection();
        bool active = on && SupportsVision(mode);
        _frame = new(active, new(p.X.X, p.Y.Y, p.Z.X, p.Z.Y), camera.Near, camera.Far);
        // Disabled means the renderer can omit this effect's resolve requests as well as its draw.
        if (Enabled != active) Enabled = active;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        using RenderData? frameData = renderData;
        Frame f = _frame;
        if (_released || !f.On) return;
        if (!_attempted) Init();
        using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
        if (_rd is not { } rd || !_shader.IsValid || sceneBuffers is not RenderSceneBuffersRD buffers) return;
        Vector2I size = buffers.GetInternalSize();
        if (size.X < 2 || size.Y < 2) return;
        Span<float> constants = [size.X, size.Y, .6f, 1.25f,
            f.Projection.X, f.Projection.Y, f.Projection.Z, f.Projection.W,
            f.Near, f.Far, .25f, 60f];
        MemoryMarshal.AsBytes(constants).CopyTo(_push);
        // The target the later passes go on drawing into; the depth read is the resolved one either way.
        bool msaa = buffers.GetMsaa3D() != RenderingServer.ViewportMsaa.Disabled;
        for (uint view = 0; view < buffers.GetViewCount(); view++)
        {
            Rid color = buffers.GetColorLayer(view, msaa), depth = buffers.GetDepthLayer(view, false);
            if (!color.IsValid || !depth.IsValid) continue;
            _target[0] = color;
            Rid framebuffer = FramebufferCacheRD.GetCacheMultipass(_target, _passes, 1);
            if (!framebuffer.IsValid || PipelineFor(rd, framebuffer, color) is not { IsValid: true } pipeline) continue;
            UniformSlots slots = _slots ??= new UniformSlots(RenderingDevice.UniformType.SamplerWithTexture);
            slots.Set(0, _sampler, depth);
            Rid set = UniformSetCacheRD.GetCache(_shader, 0, slots.Array);
            long list = rd.DrawListBegin(framebuffer, 0, NoClear, 0f, 0, null, 0);
            rd.DrawListBindRenderPipeline(list, pipeline);
            rd.DrawListBindUniformSet(list, set, 0);
            rd.DrawListSetPushConstant(list, _push, (uint)_push.Length);
            rd.DrawListDraw(list, false, 1, 3);
            rd.DrawListEnd();
            Draws++;
        }
    }

    /// <summary>The pipeline that multiplies a target of this framebuffer's format, made at its first frame.</summary>
    private Rid PipelineFor(RenderingDevice rd, Rid framebuffer, Rid color)
    {
        long format = rd.FramebufferGetFormat(framebuffer);
        if (_pipelines.TryGetValue(format, out Rid pipeline)) return pipeline;
        using RDTextureFormat colorFormat = rd.TextureGetFormat(color);
        // Frame x shade; the frame's alpha is left as it is.
        var attachment = new RDPipelineColorBlendStateAttachment
        {
            EnableBlend = true, WriteA = false,
            ColorBlendOp = RenderingDevice.BlendOperation.Add, AlphaBlendOp = RenderingDevice.BlendOperation.Add,
            SrcColorBlendFactor = RenderingDevice.BlendFactor.DstColor, DstColorBlendFactor = RenderingDevice.BlendFactor.Zero,
            SrcAlphaBlendFactor = RenderingDevice.BlendFactor.Zero, DstAlphaBlendFactor = RenderingDevice.BlendFactor.One,
        };
        pipeline = rd.RenderPipelineCreate(_shader, format, -1, RenderingDevice.RenderPrimitive.Triangles,
            new RDPipelineRasterizationState { CullMode = RenderingDevice.PolygonCullMode.Disabled },
            new RDPipelineMultisampleState { SampleCount = colorFormat.Samples }, new RDPipelineDepthStencilState(),
            new RDPipelineColorBlendState { Attachments = [attachment] }, 0, 0, []);
        if (!pipeline.IsValid)
        {
            Error = $"no pipeline for {colorFormat.Format} with {colorFormat.Samples}";
            GD.PushError("Ambient occlusion: " + Error);
        }

        _pipelines[format] = pipeline;
        return pipeline;
    }

    private void Init()
    {
        _attempted = true;
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd is null) { Error = "requires RenderingDevice"; return; }
        using var source = new RDShaderSource
        {
            Language = RenderingDevice.ShaderLanguage.Glsl, SourceVertex = VertexSource, SourceFragment = FragmentSource,
        };
        using RDShaderSpirV spirv = _rd.ShaderCompileSpirVFromSource(source, false);
        string error = spirv.CompileErrorVertex + spirv.CompileErrorFragment;
        if (error.Length == 0)
        {
            _shader = _rd.ShaderCreateFromSpirV(spirv, "svm_ambient_occlusion");
            if (!_shader.IsValid) error = "the shader could not be made";
        }

        if (error.Length != 0) { Error = error; GD.PushError("Ambient occlusion: " + error); return; }
        using var sampler = new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Nearest, MagFilter = RenderingDevice.SamplerFilter.Nearest };
        _sampler = _rd.SamplerCreate(sampler);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) Release();
        base._Notification(what);
    }

    public void Release()
    {
        if (_released) return;
        _released = true;
        if (_rd is not { } rd) return;
        Rid shader = _shader, sampler = _sampler;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            if (sampler.IsValid) rd.FreeRid(sampler);
            // Freeing the shader frees the pipelines made from it.
            if (shader.IsValid) rd.FreeRid(shader);
            _pipelines.Clear();
            _rd = null;
        }));
    }
}
