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

    // The vision pass of PlayerController.RendMap (svm_display_post.glsl, stage 2), into the shown target in display
    // values. p.a: x RendMap, y clean rectangles in use, zw the target's size.
    private const string Vision = Head + """
        layout(set = 0, binding = 0) uniform sampler2D scene;
        layout(set = 0, binding = 1) uniform sampler2D noise_texture;
        layout(set = 0, binding = 2, std140) uniform VisionParams
        {
            vec4 noise;        // x cos, y sin of the grain angle, zw grain offset
            vec4 tiles;        // xy pixels per grain texel, zw sonar tiles over the frame
            vec4 night;        // rgb tint, a gain
            vec4 luma;         // rgb display luminance
            vec4 thermal;      // x composite alpha, y level mix, z table gain, w EMF gain
            vec4 motion;       // x 2 s, y MTVOffset / 255, z ramp scale, w fold threshold
            vec4 motion_low;
            vec4 motion_high;
            vec4 motion_fold;
            vec4 clean_tint;   // rgb tint, a gain of each x2 stage
            vec4 clean_bias;
            vec4 clean_rect[8];
        } v;
        layout(set = 0, binding = 3) uniform sampler2D lut_texture;
        layout(set = 0, binding = 4) uniform sampler2D sonar_texture;
        layout(set = 0, binding = 5) uniform sampler2D blur1_texture;
        layout(set = 0, binding = 6) uniform sampler2D blur2_texture;

        void main()
        {
            vec3 c = clamp(texelFetch(scene, ivec2(gl_FragCoord.xy), 0).rgb, vec3(0.0), vec3(1.0));
            int mode = int(p.a.x + 0.5);
            vec2 pixel = gl_FragCoord.xy;
            vec2 uv = pixel / p.a.zw;
            // One grain texel a pixel, turned and moved by the frame's random angle and offset.
            vec2 g = pixel * v.tiles.xy;
            vec2 q = vec2(g.x * v.noise.x - g.y * v.noise.y, g.x * v.noise.y + g.y * v.noise.x) + v.noise.zw;
            float n = textureLod(noise_texture, q, 0.0).r;
            if (mode == 34) {
                float l = dot(c, v.luma.rgb);
                c = clamp(v.night.a * n * (l * v.night.rgb), 0.0, 1.0);
            } else if (mode == 36) {
                vec3 b = mix(textureLod(blur1_texture, uv, 0.0).rgb, textureLod(blur2_texture, uv, 0.0).rgb, v.thermal.y);
                vec3 h = mix(c, b, v.thermal.x);
                c = clamp(v.thermal.z * textureLod(lut_texture, vec2(h.g, h.b), 0.0).rgb * n, 0.0, 1.0);
            } else if (mode == 37) {
                vec3 s = textureLod(blur2_texture, uv, 0.0).rgb;
                float lum = clamp(v.motion.x * dot(s * n, v.luma.rgb) + v.motion.y, -1.0, 1.0);
                vec3 ramp = mix(v.motion_low.rgb, v.motion_high.rgb, clamp(v.motion.z * lum, 0.0, 1.0));
                vec3 fold = v.motion_fold.rgb * (1.0 - lum);
                vec3 col = lum > v.motion.w ? fold : ramp;
                float sonar = textureLod(sonar_texture, uv * v.tiles.zw, 0.0).r;
                vec3 outc = clamp(col * sonar, 0.0, 1.0);
                int rects = int(p.a.y + 0.5);
                for (int i = 0; i < rects; i++) {
                    vec4 r = v.clean_rect[i];
                    if (uv.x >= r.x && uv.x < r.z && uv.y >= r.y && uv.y < r.w) {
                        float l = dot(c, v.luma.rgb);
                        vec3 m = clamp(v.clean_tint.a * (l * v.clean_tint.rgb + v.clean_bias.rgb), 0.0, 1.0);
                        m = clamp(v.clean_tint.a * m * n, 0.0, 1.0);
                        outc = clamp(m * sonar, 0.0, 1.0);
                        break;
                    }
                }
                c = outc;
            } else if (mode == 38) {
                vec3 b = mix(textureLod(blur1_texture, uv, 0.0).rgb, textureLod(blur2_texture, uv, 0.0).rgb, v.thermal.y);
                vec3 h = mix(c, b, v.thermal.x);
                float sonar = textureLod(sonar_texture, uv * v.tiles.zw, 0.0).r;
                c = clamp(v.thermal.w * (vec3(1.0) - h) * n * sonar, 0.0, 1.0);
            }
            frag = vec4(c, 1.0);
        }

        """;

    // The EMF halo's mask (svm_glow.glsl mode 2): white where the scene's red is at most a.x. Point sampled.
    private const string HaloMask = Head + """
        layout(set = 0, binding = 0) uniform sampler2D scene;

        void main()
        {
            frag = vec4(textureLod(scene, v_uv, 0.0).r <= p.a.x ? 1.0 : 0.0);
        }

        """;

    // The EMF halo (svm_glow.glsl mode 3): the four blurred levels of the mask, kept only outside it. a: the level
    // weights; b.x the mask's threshold.
    private const string HaloComposite = Head + """
        layout(set = 0, binding = 0) uniform sampler2D level1;
        layout(set = 0, binding = 1) uniform sampler2D level2;
        layout(set = 0, binding = 2) uniform sampler2D level3;
        layout(set = 0, binding = 3) uniform sampler2D level4;
        layout(set = 0, binding = 4) uniform sampler2D scene;

        void main()
        {
            vec4 c = clamp(p.a.x * textureLod(level1, v_uv, 0.0) + p.a.y * textureLod(level2, v_uv, 0.0)
                + p.a.z * textureLod(level3, v_uv, 0.0) + p.a.w * textureLod(level4, v_uv, 0.0), vec4(0.0), vec4(1.0));
            float mask = textureLod(scene, v_uv, 0.0).r <= p.b.x ? 1.0 : 0.0;
            frag = vec4(c.rgb * min(c.a, 1.0 - mask), 1.0);
        }

        """;

    // The halo over the scene: drawn with ZERO / INVSRCCOLOR, it darkens the scene by 1 - halo.
    private const string HaloApply = Head + """
        layout(set = 0, binding = 0) uniform sampler2D halo;

        void main()
        {
            frag = vec4(textureLod(halo, v_uv, 0.0).rgb * p.a.x, 1.0);
        }

        """;

    /// <summary>The floats of the vision pass's block (<see cref="Vision"/>'s VisionParams).</summary>
    public const int VisionFloats = 19 * 4;

    private static readonly Vector2I[] s_levelSizes =
    [
        new(512, 256), new(256, 256), new(256, 128), new(128, 128), new(128, 64), new(64, 64), new(64, 32), new(32, 32),
    ];

    private Rid _brightShader, _blurShader, _compositeShader, _finalShader;
    private Rid _brightPipeline, _blurPipeline, _compositePipeline, _finalPipeline;
    private Rid _visionShader, _maskShader, _haloShader, _applyShader;
    private Rid _visionPipeline, _maskPipeline, _haloPipeline, _applyPipeline;
    private Rid _visionBlock, _visionSet, _maskSet, _haloSet, _applySet, _sceneFb, _grey;
    private Rid _noise, _lut, _sonar;
    // The two blur runs the vision passes read (RenderK.BlurTargets), in half floats as DisplaySpaceEffect keeps them:
    // motion vision multiplies the blurred scene several times over, which shows an 8-bit level's rounding.
    private readonly Rid[] _visionLevels = new Rid[4];
    private readonly Rid[] _visionLevelFbs = new Rid[4];
    private readonly Rid[] _visionBlurSets = new Rid[4];
    private readonly Vector2I[] _visionSizes = new Vector2I[4];
    // The EMF halo's own chain (mask, eight blur levels, composite) at the frame's detail scale, as
    // DisplaySpaceEffect sizes its glow targets: the normal view's glow chain keeps the original's fixed sizes.
    private readonly Rid[] _haloLevels = new Rid[8];
    private readonly Rid[] _haloLevelFbs = new Rid[8];
    private readonly Rid[] _haloBlurSets = new Rid[8];
    private readonly Vector2I[] _haloSizes = new Vector2I[8];
    private Rid _haloBright, _haloBrightFb, _haloGlow, _haloGlowFb;
    private Vector2I _haloBrightSize;
    private Rid _visionBlurPipeline;
    private readonly byte[] _visionBytes = new byte[VisionFloats * 4];
    private Rid _linear, _nearest, _linearRepeat;
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
    /// <param name="noise">The grain, the thermal table and the sonar backdrop of the render pack (any may be invalid).</param>
    public bool Ensure(RenderingDevice rd, Rid scene, Rid shown, Func<string, string, string, Rid> compile, Rid noise = default, Rid lut = default, Rid sonar = default)
    {
        if (!_made)
        {
            _made = true;
            _brightShader = compile(Vertex, Bright, "svm_rd_post_bright");
            _blurShader = compile(Vertex, Blur, "svm_rd_post_blur");
            _compositeShader = compile(Vertex, Composite, "svm_rd_post_composite");
            _finalShader = compile(Vertex, Final, "svm_rd_post_final");
            _visionShader = compile(Vertex, Vision, "svm_rd_post_vision");
            _maskShader = compile(Vertex, HaloMask, "svm_rd_post_halo_mask");
            _haloShader = compile(Vertex, HaloComposite, "svm_rd_post_halo");
            _applyShader = compile(Vertex, HaloApply, "svm_rd_post_halo_apply");
            if (!_brightShader.IsValid || !_blurShader.IsValid || !_compositeShader.IsValid || !_finalShader.IsValid
                || !_visionShader.IsValid || !_maskShader.IsValid || !_haloShader.IsValid || !_applyShader.IsValid) return false;
            _linearRepeat = rd.SamplerCreate(new RDSamplerState
            {
                MagFilter = RD.SamplerFilter.Linear, MinFilter = RD.SamplerFilter.Linear, MaxLod = 0f,
                RepeatU = RD.SamplerRepeatMode.Repeat, RepeatV = RD.SamplerRepeatMode.Repeat,
            });
            // In a pack texture's place when the pack has none: mid grey (the grain's mean).
            _grey = rd.TextureCreate(new RDTextureFormat
            {
                Format = RD.DataFormat.R8G8B8A8Unorm, Width = 1, Height = 1, UsageBits = RD.TextureUsageBits.SamplingBit,
            }, new RDTextureView(), new Godot.Collections.Array<byte[]> { new byte[] { 128, 128, 128, 255 } });
            _visionBlock = rd.UniformBufferCreate((uint)_visionBytes.Length, ReadOnlySpan<byte>.Empty, 0);
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
            _visionPipeline = Pipeline(rd, _visionShader, format);
            _maskPipeline = Pipeline(rd, _maskShader, format);
            _haloPipeline = Pipeline(rd, _haloShader, format);
            _applyPipeline = rd.RenderPipelineCreate(_applyShader, format, -1, RD.RenderPrimitive.Triangles, new RDPipelineRasterizationState(),
                new RDPipelineMultisampleState(), new RDPipelineDepthStencilState(), new RDPipelineColorBlendState
                {
                    Attachments =
                    [
                        new RDPipelineColorBlendStateAttachment
                        {
                            EnableBlend = true, SrcColorBlendFactor = RD.BlendFactor.Zero, DstColorBlendFactor = RD.BlendFactor.OneMinusSrcColor,
                            SrcAlphaBlendFactor = RD.BlendFactor.Zero, DstAlphaBlendFactor = RD.BlendFactor.One,
                        },
                    ],
                }, 0, 0, []);
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

        if (!_finalPipeline.IsValid || !_visionPipeline.IsValid || !_maskPipeline.IsValid || !_haloPipeline.IsValid || !_applyPipeline.IsValid) return false;
        if (scene != _scene || shown != _shown || !rd.FramebufferIsValid(_finalFb) || noise != _noise || lut != _lut || sonar != _sonar)
        {
            foreach (Rid set in new[] { _brightSet, _finalSet, _visionSet, _maskSet, _haloSet, _applySet })
            {
                if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
            }

            // The vision blur levels at the frame's detail scale (RenderK.PostDetailScale), as DisplaySpaceEffect sizes them.
            using RDTextureFormat sceneFormat = rd.TextureGetFormat(scene);
            Vector2I detail = RenderK.PostDetailScale((int)sceneFormat.Width);
            FreeVisionLevels(rd);
            for (int i = 0; i < 4; i++)
            {
                _visionSizes[i] = RenderK.BlurTargets[i] * detail;
                _visionLevels[i] = rd.TextureCreate(RdWorldGpu.Shown(new RDTextureFormat
                {
                    Format = RD.DataFormat.R16G16B16A16Sfloat, Width = (uint)_visionSizes[i].X, Height = (uint)_visionSizes[i].Y,
                    UsageBits = RD.TextureUsageBits.ColorAttachmentBit | RD.TextureUsageBits.SamplingBit,
                }), new RDTextureView(), []);
                _visionLevelFbs[i] = rd.FramebufferCreate([_visionLevels[i]], -1, 1);
                if (i > 0) _visionBlurSets[i] = rd.UniformSetCreate([Sampled(0, _linear, _visionLevels[i - 1])], _blurShader, 0);
            }

            if (!_visionBlurPipeline.IsValid) _visionBlurPipeline = Pipeline(rd, _blurShader, rd.FramebufferGetFormat(_visionLevelFbs[0]));
            if (!_visionBlurPipeline.IsValid) return false;
            _haloBrightSize = RenderK.GlowBrightTarget * detail;
            _haloBright = Target(rd, _haloBrightSize);
            _haloGlow = Target(rd, _haloBrightSize);
            _haloBrightFb = rd.FramebufferCreate([_haloBright], -1, 1);
            _haloGlowFb = rd.FramebufferCreate([_haloGlow], -1, 1);
            for (int i = 0; i < 8; i++)
            {
                _haloSizes[i] = s_levelSizes[i] * detail;
                _haloLevels[i] = Target(rd, _haloSizes[i]);
                _haloLevelFbs[i] = rd.FramebufferCreate([_haloLevels[i]], -1, 1);
                _haloBlurSets[i] = rd.UniformSetCreate([Sampled(0, _linear, i == 0 ? _haloBright : _haloLevels[i - 1])], _blurShader, 0);
            }

            if (rd.FramebufferIsValid(_finalFb)) rd.FreeRid(_finalFb);
            if (rd.FramebufferIsValid(_sceneFb)) rd.FreeRid(_sceneFb);
            (_scene, _shown, _noise, _lut, _sonar) = (scene, shown, noise, lut, sonar);
            _brightSet = rd.UniformSetCreate([Sampled(0, _linear, scene)], _brightShader, 0);
            _finalSet = rd.UniformSetCreate([Sampled(0, _nearest, scene), Sampled(1, _linear, _glow)], _finalShader, 0);
            _finalFb = rd.FramebufferCreate([shown], -1, 1);
            _sceneFb = rd.FramebufferCreate([scene], -1, 1);
            var block = new RDUniform { UniformType = RD.UniformType.UniformBuffer, Binding = 2 };
            block.AddId(_visionBlock);
            _visionSet = rd.UniformSetCreate(
            [
                Sampled(0, _nearest, scene), Sampled(1, _linearRepeat, noise.IsValid && rd.TextureIsValid(noise) ? noise : _grey), block,
                Sampled(3, _linear, lut.IsValid && rd.TextureIsValid(lut) ? lut : _grey),
                Sampled(4, _linearRepeat, sonar.IsValid && rd.TextureIsValid(sonar) ? sonar : _grey),
                Sampled(5, _linear, _visionLevels[1]), Sampled(6, _linear, _visionLevels[3]),
            ], _visionShader, 0);
            _visionBlurSets[0] = rd.UniformSetCreate([Sampled(0, _linear, scene)], _blurShader, 0);
            _maskSet = rd.UniformSetCreate([Sampled(0, _nearest, scene)], _maskShader, 0);
            _haloSet = rd.UniformSetCreate(
                [Sampled(0, _linear, _haloLevels[1]), Sampled(1, _linear, _haloLevels[3]), Sampled(2, _linear, _haloLevels[5]), Sampled(3, _linear, _haloLevels[7]), Sampled(4, _nearest, scene)], _haloShader, 0);
            _applySet = rd.UniformSetCreate([Sampled(0, _linear, _haloGlow)], _applyShader, 0);
            if (!_visionSet.IsValid || !_visionBlurSets[0].IsValid || !_maskSet.IsValid || !_haloSet.IsValid || !_applySet.IsValid) return false;
        }

        return true;
    }

    /// <summary>The glow chain when <paramref name="glowCut"/> is not negative, then the final pass into the shown target.</summary>
    /// <summary>The glow to add to the gated scene (512 x 512), once <see cref="Ensure"/> has made it.</summary>
    public Rid Glow => _glow;

    /// <summary>
    /// The end of a vision mode's frame (svm_display_post.glsl and DisplaySpaceEffect's order): EMF's halo darkens the
    /// scene, thermal, motion and EMF blur the scene twice (the two levels they read), then the mode's pass writes the
    /// shown target. <paramref name="values"/> are <see cref="VisionFloats"/> floats of the pass's block.
    /// </summary>
    public void RunVision(RenderingDevice rd, int mode, int cleanRects, Vector2I size, ReadOnlySpan<float> values)
    {
        rd.CaptureTimestamp("RD Own Vision");
        if (mode == RenderK.RendMapEmf)
        {
            const float cut = 228f / 255f;
            Pass(rd, _haloBrightFb, _maskPipeline, _maskSet, [cut, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);
            BlurLevels(rd, _haloBrightSize, _haloLevelFbs, _haloBlurSets, _blurPipeline, _haloSizes);
            Vector4 w = RenderK.GlowLevelWeights;
            Pass(rd, _haloGlowFb, _haloPipeline, _haloSet, [w.X, w.Y, w.Z, w.W, cut, 0f, 0f, 0f]);
            Pass(rd, _sceneFb, _applyPipeline, _applySet, [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);
        }

        if (Post.DisplaySpaceEffect.UsesPyramid(mode)) BlurLevels(rd, size, _visionLevelFbs, _visionBlurSets, _visionBlurPipeline, _visionSizes);
        MemoryMarshal.AsBytes(values[..VisionFloats]).CopyTo(_visionBytes);
        rd.BufferUpdate(_visionBlock, 0, (uint)_visionBytes.Length, _visionBytes);
        Pass(rd, _finalFb, _visionPipeline, _visionSet, [mode, cleanRects, size.X, size.Y, 0f, 0f, 0f, 0f]);
        rd.CaptureTimestamp("RD Own Final");
    }

    // Vertical then horizontal, each halving its axis: taps one and three source texels either side.
    private void BlurLevels(RenderingDevice rd, Vector2I source, Rid[] framebuffers, Rid[] sets, Rid pipeline, Vector2I[] sizes)
    {
        for (int i = 0; i < framebuffers.Length; i++)
        {
            bool vertical = (i & 1) == 0;
            Pass(rd, framebuffers[i], pipeline, sets[i],
                [vertical ? 0f : 1f / source.X, vertical ? 1f / source.Y : 0f, RenderK.BlurNearWeight, RenderK.BlurFarWeight, 0f, 0f, 0f, 0f]);
            source = sizes[i];
        }
    }

    private void FreeVisionLevels(RenderingDevice rd)
    {
        foreach (Rid set in _visionBlurSets)
        {
            if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
        }

        foreach (Rid rid in _visionLevels)
        {
            // Freeing a target frees the framebuffer made of it.
            if (rid.IsValid) rd.FreeRid(rid);
        }

        Array.Clear(_visionBlurSets);
        Array.Clear(_visionLevels);
        Array.Clear(_visionLevelFbs);
        foreach (Rid set in _haloBlurSets)
        {
            if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
        }

        foreach (Rid rid in (Rid[])[.. _haloLevels, _haloBright, _haloGlow])
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        Array.Clear(_haloBlurSets);
        Array.Clear(_haloLevels);
        Array.Clear(_haloLevelFbs);
        _haloBright = _haloGlow = _haloBrightFb = _haloGlowFb = default;
    }

    /// <param name="final">False: the canvas item that shows the scene gates it and adds the glow itself.</param>
    public void Run(RenderingDevice rd, bool gate, float glowCut, bool final = true)
    {
        bool glow = gate && glowCut >= 0f;
        if (glow)
        {
            rd.CaptureTimestamp("RD Own Glow");
            Pass(rd, _brightFb, _brightPipeline, _brightSet, [glowCut, RenderK.GlowBrightGain, 0f, 0f, RenderK.GlowBrightLuma.X, RenderK.GlowBrightLuma.Y, RenderK.GlowBrightLuma.Z, 0f]);
            BlurLevels(rd, RenderK.GlowBrightTarget, _levelFbs, _blurSets, _blurPipeline, s_levelSizes);
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
        long dl = rd.DrawListBegin(framebuffer, RD.DrawFlags.IgnoreAll, _clear, 0f, 0, null, RdWorldGpu.Breadcrumb + 1);
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

    private static Rid Target(RenderingDevice rd, Vector2I size) => rd.TextureCreate(RdWorldGpu.Shown(new RDTextureFormat
    {
        Format = RdWorldGpu.Test.Contains("glow16") ? RD.DataFormat.R16G16B16A16Sfloat : RD.DataFormat.R8G8B8A8Unorm, Width = (uint)size.X, Height = (uint)size.Y,
        UsageBits = RD.TextureUsageBits.ColorAttachmentBit | RD.TextureUsageBits.SamplingBit,
    }), new RDTextureView(), []);

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
        foreach (Rid set in new[] { _brightSet, _finalSet, _compositeSet, _visionSet, _maskSet, _haloSet, _applySet })
        {
            if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
        }

        FreeVisionLevels(rd);
        if (_visionBlurPipeline.IsValid) rd.FreeRid(_visionBlurPipeline);
        _visionBlurPipeline = default;

        foreach (Rid set in _blurSets)
        {
            if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
        }

        if (rd.FramebufferIsValid(_finalFb)) rd.FreeRid(_finalFb);
        if (rd.FramebufferIsValid(_sceneFb)) rd.FreeRid(_sceneFb);
        foreach (Rid rid in new[] { _bright, _glow, _grey, _visionBlock })
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        foreach (Rid rid in _levels)
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        foreach (Rid rid in new[] { _brightPipeline, _blurPipeline, _compositePipeline, _finalPipeline, _brightShader, _blurShader, _compositeShader, _finalShader, _linear, _nearest,
            _visionPipeline, _maskPipeline, _haloPipeline, _applyPipeline, _visionShader, _maskShader, _haloShader, _applyShader, _linearRepeat })
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        _made = false;
        _scene = _shown = _noise = _lut = _sonar = default;
    }
}
