using System;
using System.Runtime.InteropServices;
using Godot;
using RD = Godot.RenderingDevice;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// The end of the own frame (the options' Fast renderer), as the original ends its frame (docs/PERFORMANCE-LOWEND-2026-11.md,
/// "The original's frame, traced"): the glow chain on small 8-bit targets (bright pass 512 x 512, eight blur passes
/// from 512 x 256 down to 32 x 32, the weighted sum of the four levels back at 512 x 512), then one full-screen pass
/// that gates the scene and adds the glow into the target the canvas shows. The arithmetic is that of
/// svm_glow_bright.glsl, svm_vision_blur.glsl, svm_glow.glsl and svm_display_normal.glsl. Render thread only.
/// </summary>
internal sealed class RdFramePost
{
    private const string Vertex = """
        #version 450
        layout(location = 0) out vec2 v_uv;

        void main()
        {
            // One triangle over the target; uv 0..1 across it, rows top down.
            vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
            v_uv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }

        """;

    private const string Head = """
        #version 450
        layout(push_constant, std430) uniform Params
        {
            vec4 a;
            vec4 b;
        } p;
        layout(location = 0) in vec2 v_uv;
        layout(location = 0) out vec4 frag;

        """;

    // a: x threshold, y gain; b: rgb luminance weights.
    private const string Bright = Head + """
        layout(set = 0, binding = 0) uniform sampler2D source;

        void main()
        {
            vec3 s = clamp(textureLod(source, v_uv, 0.0).rgb, vec3(0.0), vec3(1.0));
            float l = dot(s, p.b.rgb);
            frag = l > p.a.x ? clamp(vec4(vec3(p.a.y * l), p.a.y * (l - p.a.x)), vec4(0.0), vec4(1.0)) : vec4(0.0);
        }

        """;

    // a: xy one source texel along the pass's axis, z near weight, w far weight.
    private const string Blur = Head + """
        layout(set = 0, binding = 0) uniform sampler2D source;

        void main()
        {
            frag = p.a.z * textureLod(source, v_uv + p.a.xy, 0.0) + p.a.w * textureLod(source, v_uv + 3.0 * p.a.xy, 0.0)
                + p.a.z * textureLod(source, v_uv - p.a.xy, 0.0) + p.a.w * textureLod(source, v_uv - 3.0 * p.a.xy, 0.0);
        }

        """;

    // a: the four level weights.
    private const string Composite = Head + """
        layout(set = 0, binding = 0) uniform sampler2D level1;
        layout(set = 0, binding = 1) uniform sampler2D level2;
        layout(set = 0, binding = 2) uniform sampler2D level3;
        layout(set = 0, binding = 3) uniform sampler2D level4;

        void main()
        {
            frag = clamp(p.a.x * textureLod(level1, v_uv, 0.0) + p.a.y * textureLod(level2, v_uv, 0.0)
                + p.a.z * textureLod(level3, v_uv, 0.0) + p.a.w * textureLod(level4, v_uv, 0.0), vec4(0.0), vec4(1.0));
        }

        """;

    // a: x gate on, y gain, z offset, w glow on; b: rgb gate luminance weights.
    private const string Final = Head + """
        layout(set = 0, binding = 0) uniform sampler2D scene;
        layout(set = 0, binding = 1) uniform sampler2D glow;

        void main()
        {
            vec3 c = texelFetch(scene, ivec2(gl_FragCoord.xy), 0).rgb;
            if (p.a.x > 0.5) {
                c *= clamp(p.a.y * (dot(c, p.b.rgb) - p.a.z), 0.0, 1.0);
                if (p.a.w > 0.5) {
                    c = clamp(c + textureLod(glow, v_uv, 0.0).rgb, vec3(0.0), vec3(1.0));
                }
            }
            frag = vec4(c, 1.0);
        }

        """;

    private static readonly Vector2I[] s_levelSizes =
    [
        new(512, 256), new(256, 256), new(256, 128), new(128, 128), new(128, 64), new(64, 64), new(64, 32), new(32, 32),
    ];

    private Rid _brightShader, _blurShader, _compositeShader, _finalShader;
    private Rid _brightPipeline, _blurPipeline, _compositePipeline, _finalPipeline;
    private Rid _linear, _nearest;
    private Rid _bright, _brightFb, _glow, _glowFb;
    private readonly Rid[] _levels = new Rid[8];
    private readonly Rid[] _levelFbs = new Rid[8];
    private readonly Rid[] _blurSets = new Rid[8];
    private Rid _compositeSet, _brightSet, _finalSet, _finalFb;
    private Rid _scene, _shown;
    private readonly byte[] _push = new byte[32];
    private readonly Color[] _clear = [new Color(0f, 0f, 0f, 1f)];
    private bool _made;

    /// <summary>Makes the shaders and the glow targets once, and the bindings of this scene and shown target.</summary>
    public bool Ensure(RenderingDevice rd, Rid scene, Rid shown, Func<string, string, string, Rid> compile)
    {
        if (!_made)
        {
            _made = true;
            _brightShader = compile(Vertex, Bright, "svm_rd_post_bright");
            _blurShader = compile(Vertex, Blur, "svm_rd_post_blur");
            _compositeShader = compile(Vertex, Composite, "svm_rd_post_composite");
            _finalShader = compile(Vertex, Final, "svm_rd_post_final");
            if (!_brightShader.IsValid || !_blurShader.IsValid || !_compositeShader.IsValid || !_finalShader.IsValid) return false;
            _linear = rd.SamplerCreate(new RDSamplerState { MagFilter = RD.SamplerFilter.Linear, MinFilter = RD.SamplerFilter.Linear, MaxLod = 0f });
            _nearest = rd.SamplerCreate(new RDSamplerState { MaxLod = 0f });
            _bright = Target(rd, RenderK.GlowBrightTarget);
            _glow = Target(rd, RenderK.GlowBrightTarget);
            _brightFb = rd.FramebufferCreate([_bright], -1, 1);
            _glowFb = rd.FramebufferCreate([_glow], -1, 1);
            long format = rd.FramebufferGetFormat(_brightFb);
            _brightPipeline = Pipeline(rd, _brightShader, format);
            _blurPipeline = Pipeline(rd, _blurShader, format);
            _compositePipeline = Pipeline(rd, _compositeShader, format);
            _finalPipeline = Pipeline(rd, _finalShader, format);
            Rid source = _bright;
            for (int i = 0; i < 8; i++)
            {
                _levels[i] = Target(rd, s_levelSizes[i]);
                _levelFbs[i] = rd.FramebufferCreate([_levels[i]], -1, 1);
                _blurSets[i] = rd.UniformSetCreate([Sampled(0, _linear, source)], _blurShader, 0);
                source = _levels[i];
            }

            _compositeSet = rd.UniformSetCreate(
                [Sampled(0, _linear, _levels[1]), Sampled(1, _linear, _levels[3]), Sampled(2, _linear, _levels[5]), Sampled(3, _linear, _levels[7])], _compositeShader, 0);
        }

        if (!_finalPipeline.IsValid) return false;
        if (scene != _scene || shown != _shown || !rd.FramebufferIsValid(_finalFb))
        {
            foreach (Rid set in new[] { _brightSet, _finalSet })
            {
                if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
            }

            if (rd.FramebufferIsValid(_finalFb)) rd.FreeRid(_finalFb);
            (_scene, _shown) = (scene, shown);
            _brightSet = rd.UniformSetCreate([Sampled(0, _linear, scene)], _brightShader, 0);
            _finalSet = rd.UniformSetCreate([Sampled(0, _nearest, scene), Sampled(1, _linear, _glow)], _finalShader, 0);
            _finalFb = rd.FramebufferCreate([shown], -1, 1);
        }

        return true;
    }

    /// <summary>The glow chain when <paramref name="glowCut"/> is not negative, then the final pass into the shown target.</summary>
    /// <summary>The glow to add to the gated scene (512 x 512), once <see cref="Ensure"/> has made it.</summary>
    public Rid Glow => _glow;

    /// <param name="final">False: the canvas item that shows the scene gates it and adds the glow itself.</param>
    public void Run(RenderingDevice rd, bool gate, float glowCut, bool final = true)
    {
        bool glow = gate && glowCut >= 0f;
        if (glow)
        {
            rd.CaptureTimestamp("RD Own Glow");
            Pass(rd, _brightFb, _brightPipeline, _brightSet, [glowCut, RenderK.GlowBrightGain, 0f, 0f, RenderK.GlowBrightLuma.X, RenderK.GlowBrightLuma.Y, RenderK.GlowBrightLuma.Z, 0f]);
            Vector2I source = RenderK.GlowBrightTarget;
            for (int i = 0; i < 8; i++)
            {
                // Vertical then horizontal, each halving its axis: taps one and three source texels either side.
                bool vertical = (i & 1) == 0;
                Pass(rd, _levelFbs[i], _blurPipeline, _blurSets[i],
                    [vertical ? 0f : 1f / source.X, vertical ? 1f / source.Y : 0f, RenderK.BlurNearWeight, RenderK.BlurFarWeight, 0f, 0f, 0f, 0f]);
                source = s_levelSizes[i];
            }

            Vector4 w = RenderK.GlowLevelWeights;
            Pass(rd, _glowFb, _compositePipeline, _compositeSet, [w.X, w.Y, w.Z, w.W, 0f, 0f, 0f, 0f]);
        }

        rd.CaptureTimestamp("RD Own Final");
        if (!final) return;
        Pass(rd, _finalFb, _finalPipeline, _finalSet,
            [gate ? 1f : 0f, RenderK.GlowGateGain, RenderK.GlowGateOffset, glow ? 1f : 0f, RenderK.GlowGateLuma.X, RenderK.GlowGateLuma.Y, RenderK.GlowGateLuma.Z, 0f]);
    }

    private void Pass(RenderingDevice rd, Rid framebuffer, Rid pipeline, Rid set, ReadOnlySpan<float> values)
    {
        MemoryMarshal.AsBytes(values).CopyTo(_push);
        long dl = rd.DrawListBegin(framebuffer, RD.DrawFlags.IgnoreAll, _clear, 0f, 0, null, 0);
        try
        {
            rd.DrawListBindRenderPipeline(dl, pipeline);
            rd.DrawListBindUniformSet(dl, set, 0);
            rd.DrawListSetPushConstant(dl, _push, (uint)_push.Length);
            rd.DrawListDraw(dl, false, 1, 3);
        }
        finally
        {
            rd.DrawListEnd();
        }
    }

    private static Rid Target(RenderingDevice rd, Vector2I size) => rd.TextureCreate(new RDTextureFormat
    {
        Format = RdWorldGpu.Test.Contains("glow16") ? RD.DataFormat.R16G16B16A16Sfloat : RD.DataFormat.R8G8B8A8Unorm, Width = (uint)size.X, Height = (uint)size.Y,
        UsageBits = RD.TextureUsageBits.ColorAttachmentBit | RD.TextureUsageBits.SamplingBit,
    }, new RDTextureView(), []);

    private static Rid Pipeline(RenderingDevice rd, Rid shader, long format) => rd.RenderPipelineCreate(shader, format, -1,
        RD.RenderPrimitive.Triangles, new RDPipelineRasterizationState(), new RDPipelineMultisampleState(), new RDPipelineDepthStencilState(),
        new RDPipelineColorBlendState { Attachments = [new RDPipelineColorBlendStateAttachment()] }, 0, 0, []);

    private static RDUniform Sampled(int binding, Rid sampler, Rid texture)
    {
        var uniform = new RDUniform { UniformType = RD.UniformType.SamplerWithTexture, Binding = binding };
        uniform.AddId(sampler);
        uniform.AddId(texture);
        return uniform;
    }

    public void Free(RenderingDevice rd)
    {
        foreach (Rid set in new[] { _brightSet, _finalSet, _compositeSet })
        {
            if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
        }

        foreach (Rid set in _blurSets)
        {
            if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
        }

        if (rd.FramebufferIsValid(_finalFb)) rd.FreeRid(_finalFb);
        foreach (Rid rid in new[] { _bright, _glow })
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        foreach (Rid rid in _levels)
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        foreach (Rid rid in new[] { _brightPipeline, _blurPipeline, _compositePipeline, _finalPipeline, _brightShader, _blurShader, _compositeShader, _finalShader, _linear, _nearest })
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        _made = false;
        _scene = _shown = default;
    }
}
