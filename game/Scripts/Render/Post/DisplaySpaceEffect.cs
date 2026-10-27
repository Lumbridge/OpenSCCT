using System;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Post;

/// <summary>What the display-space pass does this frame. Set from the main thread, read on the render thread.</summary>
public sealed record DisplayPostSettings
{
    /// <summary>Zone distance fog (ZoneInfo.bDistanceFog): colour as a display value, start and end in uu.</summary>
    public bool Fog { get; init; }

    public Color FogColor { get; init; }

    public float FogStart { get; init; }

    public float FogEnd { get; init; }

    /// <summary>The glow composite's dark gate (applies whenever PlayerController.DoGlow is on, even at threshold 255).</summary>
    public bool GlowGate { get; init; } = true;

    /// <summary>PlayerController.RendMap: 5 normal, 34 night, 36 thermal, 37 motion, 38 EMF.</summary>
    public int RendMap { get; init; } = RenderK.RendMapNormal;

    /// <summary>LevelInfo.MTVSlope and MTVOffset (motion vision ramp).</summary>
    public int MtvSlope { get; init; } = RenderK.DefaultMtvSlope;

    public int MtvOffset { get; init; } = RenderK.DefaultMtvOffset;

    /// <summary>The shared render pack (thermal table, grain, sonar); without it the grain is a stand-in and thermal has no table.</summary>
    public RenderPack? Pack { get; init; }

    /// <summary>The final sRGB decode. Off only for diagnostics.</summary>
    public bool Linearize { get; init; } = true;

    public float Near { get; init; } = RenderK.NearClipUnits;

    public float Far { get; init; } = RenderK.FarClipUnits;

    /// <summary>
    /// The motion tracker's clean view rectangles (ASMotionTrackingVision::Tick's list, 0x10ac0e30) as parts of the
    /// picture (0..1); in motion vision the scene inside each is redrawn in clean monochrome. At most
    /// <see cref="MaxCleanRects"/> are drawn.
    /// </summary>
    public Rect2[] CleanRects { get; init; } = [];

    public const int MaxCleanRects = 8;

    /// <summary>Screen pixel rectangles to parts of the picture.</summary>
    public static Rect2[] Normalize(System.Collections.Generic.IReadOnlyList<Rect2> rects, Vector2 screen)
    {
        ArgumentNullException.ThrowIfNull(rects);
        if (rects.Count == 0 || screen.X <= 0f || screen.Y <= 0f)
        {
            return [];
        }

        var parts = new Rect2[Math.Min(rects.Count, MaxCleanRects)];
        for (int i = 0; i < parts.Length; i++)
        {
            Rect2 r = rects[i].Abs();
            parts[i] = new Rect2(r.Position / screen, r.Size / screen);
        }

        return parts;
    }
}

/// <summary>
/// The Faithful pipeline's post pass (docs/RENDERING.md#display-space, #vision-modes). Materials write the original's
/// display values into the colour buffer (in thermal its heat buffer); after the transparent pass this compute pass
/// applies zone fog and the glow gate to the normal scene, builds the vision blur pyramid when thermal or motion need
/// it, runs the vision pass of RendMap in display space, then decodes sRGB exactly, so Godot's Linear tonemapper at
/// exposure 1 (whose output encode is the exact sRGB curve) puts the display value back on screen.
/// </summary>
[PortOf("eng:Render.DisplaySpace", Spec = "docs/RENDERING.md#display-space")]
public partial class DisplaySpaceEffect : CompositorEffect
{
    public const string ShaderPath = "res://Shaders/svm_display_post.glsl";
    public const string BlurShaderPath = "res://Shaders/svm_vision_blur.glsl";
    private const int NoiseSize = 128;
    private const int ParamVectors = 17 + DisplayPostSettings.MaxCleanRects;

    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _blurShader;
    private Rid _blurPipeline;
    private Rid _nearest;
    private Rid _linearRepeat;
    private Rid _linearClamp;
    private Rid _noise;
    private Rid _lut;
    private Rid _sonar;
    private readonly Rid[] _params = new Rid[2];
    private readonly Rid[] _levels = new Rid[RenderK.BlurTargets.Length];
    // Uniform sets of the pyramid passes that read a level (made once: they hold only this effect's own resources and
    // are freed before them).
    private readonly Rid[] _levelSets = new Rid[RenderK.BlurTargets.Length];
    private RenderPack? _uploaded;
    private volatile DisplayPostSettings _settings = new();
    private uint _frame;
    private uint _grain = 0x2545F491u;

    public DisplaySpaceEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = true;
        AccessResolvedDepth = true;
        RenderingServer.CallOnRenderThread(Callable.From(InitOnRenderThread));
    }

    public DisplayPostSettings Settings
    {
        get => _settings;
        set => _settings = value ?? new DisplayPostSettings();
    }

    /// <summary>The RendMap of the last frame the pass drew (render thread writes), or -1 before the first.</summary>
    public int LastDrawnRendMap => _lastDrawnRendMap;

    private volatile int _lastDrawnRendMap = -1;

    /// <summary>Clean view rectangles the last drawn frame redrew (render thread writes).</summary>
    public int LastDrawnCleanRects => _lastDrawnCleanRects;

    private volatile int _lastDrawnCleanRects;
    private bool _released;

    /// <summary>True once the compute pipelines exist.</summary>
    public bool Ready => _pipeline.IsValid && _blurPipeline.IsValid;

    /// <summary>Why the pass could not start, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>True once the render pack's thermal table, grain and sonar are on the GPU.</summary>
    public bool HasPackTextures { get; private set; }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            Release();
        }

        base._Notification(what);
    }

    /// <summary>
    /// Frees the pass's GPU resources on the render thread; the pass draws nothing afterwards. The view calls it when it
    /// goes away, so a left play session does not keep them until the process ends.
    /// </summary>
    public void Release()
    {
        if (_released || _rd is not { } rd)
        {
            return;
        }

        _released = true;
        Rid[] rids = [.. _levelSets, _pipeline, _shader, _blurPipeline, _blurShader, _nearest, _linearRepeat, _linearClamp, _noise, _lut, _sonar, .. _params, .. _levels];
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            // On the render thread, between frames: the pass stops drawing before its resources go.
            _rd = null;
            foreach (Rid rid in rids)
            {
                if (rid.IsValid)
                {
                    rd.FreeRid(rid);
                }
            }
        }));
    }

    private void InitOnRenderThread()
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd is null)
        {
            Error = "no RenderingDevice (compatibility renderer or headless)";
            return;
        }

        if (!Compile(ShaderPath, "svm_display_post", out _shader, out _pipeline) ||
            !Compile(BlurShaderPath, "svm_vision_blur", out _blurShader, out _blurPipeline))
        {
            return;
        }

        _nearest = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
        });
        _linearRepeat = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatV = RenderingDevice.SamplerRepeatMode.Repeat,
        });
        _linearClamp = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
        _noise = Upload(GaussNoise(), NoiseSize, NoiseSize);
        _lut = Upload([255, 255, 255, 255], 1, 1);
        _sonar = Upload([255, 255, 255, 255], 1, 1);
        // One parameter block per stage, so the second dispatch of a frame never sees the first one's block rewritten.
        _params[0] = _rd.UniformBufferCreate(ParamVectors * 16);
        _params[1] = _rd.UniformBufferCreate(ParamVectors * 16);
        for (int i = 0; i < _levels.Length; i++)
        {
            Vector2I s = RenderK.BlurTargets[i];
            _levels[i] = _rd.TextureCreate(new RDTextureFormat
            {
                Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                Width = (uint)s.X,
                Height = (uint)s.Y,
                UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
            }, new RDTextureView());
        }
    }

    private bool Compile(string path, string name, out Rid shader, out Rid pipeline)
    {
        shader = default;
        pipeline = default;
        shader = ComputeShaderSource.CreateShader(_rd!, path, name, out string? error);
        if (!shader.IsValid)
        {
            Error = name + ": " + error;
            GD.PushError(Error);
            return false;
        }

        pipeline = _rd!.ComputePipelineCreate(shader);
        return true;
    }

    private Rid Upload(byte[] rgbaOrR8, int width, int height)
    {
        bool single = rgbaOrR8.Length == width * height;
        return _rd!.TextureCreate(new RDTextureFormat
        {
            Format = single ? RenderingDevice.DataFormat.R8Unorm : RenderingDevice.DataFormat.R8G8B8A8Unorm,
            Width = (uint)width,
            Height = (uint)height,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
        }, new RDTextureView(), new Godot.Collections.Array<byte[]> { rgbaOrR8 });
    }

    /// <summary>Replaces a texture with a pack image (raw RGBA8 values).</summary>
    private void Replace(ref Rid texture, Image? image)
    {
        if (image is null || image.IsEmpty())
        {
            return;
        }

        Image copy = (Image)image.Duplicate();
        if (copy.GetFormat() != Image.Format.Rgba8)
        {
            copy.Convert(Image.Format.Rgba8);
        }

        Rid next = Upload(copy.GetData(), copy.GetWidth(), copy.GetHeight());
        if (texture.IsValid)
        {
            _rd!.FreeRid(texture);
        }

        texture = next;
    }

    private void UploadPack(RenderPack pack)
    {
        Replace(ref _noise, pack.Image(RenderPack.Noise));
        Replace(ref _lut, pack.Image(RenderPack.ThermalTable));
        Replace(ref _sonar, pack.Image(RenderPack.Sonar));
        HasPackTextures = pack.Image(RenderPack.ThermalTable) is not null && pack.Image(RenderPack.Noise) is not null;
        _uploaded = pack;
    }

    /// <summary>
    /// A stand-in for Engine.Noise0_gauss (128 x 128, mean 0.5) when the render pack is missing: seeded Gaussian grain,
    /// so every run draws the same.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "mean 0.5 and spread of Engine.Noise0_gauss (std 0.085 measured on the converted texture)")]
    private static byte[] GaussNoise()
    {
        var bytes = new byte[NoiseSize * NoiseSize];
        uint state = 0x9E3779B9u;
        float Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state + 0.5f) / 4294967296f;
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            float u1 = Next();
            float u2 = Next();
            float g = MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
            float v = 0.5f + (g * RenderK.NightVisionNoiseSigma);
            bytes[i] = (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        }

        return bytes;
    }

    /// <summary>The next of the grain's per-frame random numbers in [0, 1): a seeded sequence, so renders repeat.</summary>
    private float NextGrain()
    {
        _grain ^= _grain << 13;
        _grain ^= _grain >> 17;
        _grain ^= _grain << 5;
        return (_grain >> 8) / 16777216f;
    }

    /// <summary>True when the mode reads the blur pyramid (thermal heat, motion and EMF scenes).</summary>
    [Provenance(ProvenanceKind.Decoded, "thermal post 0x10964d30, motion 0x10963dc0 and EMF 0x10965680 each run 0x10967d70 twice")]
    public static bool UsesPyramid(int rendMap) => rendMap is RenderK.RendMapThermal or RenderK.RendMapMotion or RenderK.RendMapEmf;

    /// <summary>True when the mode draws the normal scene, so zone fog applies (thermal and EMF draw their own passes).</summary>
    public static bool DrawsNormalScene(int rendMap) => rendMap is not (RenderK.RendMapThermal or RenderK.RendMapEmf);

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        // Release the scene buffers' managed wrapper here, on the render thread. Left to the garbage collector it can
        // hold the last reference to a freed viewport's buffers, whose destructor then frees GPU resources on the
        // finalizer thread (free_rid errors) and crashes the process at exit after a play session was left.
        // The frame's render data gets a new managed wrapper every call: released here it is not left to the finalizer
        // (a finalizable object and a tracked handle per frame for the garbage collector).
        using RenderData? frameData = renderData;
        using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
        if (_rd is null || !Ready || sceneBuffers is not RenderSceneBuffersRD buffers)
        {
            return;
        }

        Vector2I size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        DisplayPostSettings s = _settings;
        if (s.Pack is not null && !ReferenceEquals(s.Pack, _uploaded))
        {
            UploadPack(s.Pack);
        }

        _frame++;
        float angle = NextGrain() * RenderK.NoiseAngleRange;
        float offU = NextGrain();
        float offV = NextGrain();
        int mode = s.RendMap;
        _lastDrawnRendMap = mode;
        _lastDrawnCleanRects = mode == RenderK.RendMapMotion ? Math.Min(s.CleanRects.Length, DisplayPostSettings.MaxCleanRects) : 0;
        bool normal = DrawsNormalScene(mode);
        bool fog = normal && s.Fog;
        // The glow composite runs on the normal frame (night vision draws its own glow term); vision passes skip it.
        // The gate belongs to the glow composite of the normal frame; the night pass (0x10963090) reads the scene copy
        // before any composite and replaces it, so night vision has no gate.
        bool gate = s.GlowGate && mode is RenderK.RendMapNormal or RenderK.RendMapBinoculars;
        float slope = 1f + (3f * s.MtvSlope / 255f);
        Span<float> values =
        [
            s.FogColor.R, s.FogColor.G, s.FogColor.B, fog ? 1f : 0f,
            UeSpace.LengthToGodot(s.FogStart), UeSpace.LengthToGodot(s.FogEnd), UeSpace.LengthToGodot(s.Near), UeSpace.LengthToGodot(s.Far),
            gate ? 1f : 0f, RenderK.GlowGateGain, RenderK.GlowGateOffset, 0f,
            RenderK.GlowGateLuma.X, RenderK.GlowGateLuma.Y, RenderK.GlowGateLuma.Z, 0f,
            mode, 0f, size.X, size.Y,
            MathF.Cos(angle), MathF.Sin(angle), offU, offV,
            1f / NoiseSize, 1f / NoiseSize, RenderK.SonarTiles.X, RenderK.SonarTiles.Y,
            RenderK.NightVisionTint.X, RenderK.NightVisionTint.Y, RenderK.NightVisionTint.Z, RenderK.NightVisionGain,
            RenderK.NightVisionLuma.X, RenderK.NightVisionLuma.Y, RenderK.NightVisionLuma.Z, s.Linearize ? 1f : 0f,
            RenderK.ThermalBlurAlpha, RenderK.ThermalLevelMix, RenderK.ThermalLutGain, 0f,
            2f * slope, s.MtvOffset / 255f, RenderK.MotionRampScale, RenderK.MotionFoldAt,
            RenderK.MotionLow.R, RenderK.MotionLow.G, RenderK.MotionLow.B, 0f,
            RenderK.MotionHigh.R, RenderK.MotionHigh.G, RenderK.MotionHigh.B, 0f,
            RenderK.MotionFold.X, RenderK.MotionFold.Y, RenderK.MotionFold.Z, 0f,
            RenderK.EmfGain, s.RendMap == RenderK.RendMapMotion ? Math.Min(s.CleanRects.Length, DisplayPostSettings.MaxCleanRects) : 0f, 0f, 0f,
            RenderK.MotionCleanTint.X, RenderK.MotionCleanTint.Y, RenderK.MotionCleanTint.Z, RenderK.MotionCleanGain,
            RenderK.MotionCleanBias.X, RenderK.MotionCleanBias.Y, RenderK.MotionCleanBias.Z, 0f,
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        ];
        for (int i = 0; i < Math.Min(s.CleanRects.Length, DisplayPostSettings.MaxCleanRects); i++)
        {
            Rect2 r = s.CleanRects[i];
            int at = (17 * 4) + (i * 4);
            values[at] = r.Position.X;
            values[at + 1] = r.Position.Y;
            values[at + 2] = r.End.X;
            values[at + 3] = r.End.Y;
        }

        bool pyramid = UsesPyramid(mode);
        uint views = buffers.GetViewCount();
        for (uint view = 0; view < views; view++)
        {
            Rid color = buffers.GetColorLayer(view);
            Rid depth = buffers.GetDepthLayer(view);
            if (pyramid)
            {
                if (fog || gate)
                {
                    Main(values, 1f, color, depth, size);
                }

                Blur(color, size, _levels[0], new Vector2(0f, 1f), fromFrame: true);
                Blur(_levels[0], RenderK.BlurTargets[0], _levels[1], new Vector2(1f, 0f), fromFrame: false);
                Blur(_levels[1], RenderK.BlurTargets[1], _levels[2], new Vector2(0f, 1f), fromFrame: false);
                Blur(_levels[2], RenderK.BlurTargets[2], _levels[3], new Vector2(1f, 0f), fromFrame: false);
                Main(values, 2f, color, depth, size);
            }
            else
            {
                Main(values, 3f, color, depth, size);
            }
        }
    }

    // The uniform lists and constant bytes of the passes, kept from frame to frame (no wrappers made per frame).
    private UniformSlots? _mainSlots;
    private UniformSlots? _blurSlots;
    private byte[]? _mainBytes;
    private byte[]? _blurBytes;

    private void Main(Span<float> values, float stage, Rid color, Rid depth, Vector2I size)
    {
        values[17] = stage;
        Span<byte> raw = MemoryMarshal.AsBytes(values);
        if (_mainBytes?.Length != raw.Length)
        {
            _mainBytes = new byte[raw.Length];
        }

        raw.CopyTo(_mainBytes);
        Rid block = _params[stage == 1f ? 0 : 1];
        _rd!.BufferUpdate(block, 0, (uint)_mainBytes.Length, _mainBytes);
        UniformSlots slots = _mainSlots ??= new UniformSlots(RenderingDevice.UniformType.Image,
            RenderingDevice.UniformType.SamplerWithTexture, RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.UniformBuffer, RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.SamplerWithTexture, RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.SamplerWithTexture);
        slots.Set(0, color);
        slots.Set(1, _nearest, depth);
        slots.Set(2, _linearRepeat, _noise);
        slots.Set(3, block);
        slots.Set(4, _linearClamp, _lut);
        slots.Set(5, _linearRepeat, _sonar);
        slots.Set(6, _linearClamp, _levels[1]);
        slots.Set(7, _linearClamp, _levels[3]);
        Rid set = UniformSetCacheRD.GetCache(_shader, 0, slots.Array);
        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, set, 0);
        _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        _rd.ComputeListEnd();
    }

    /// <summary>
    /// One blur pass into <paramref name="target"/>. The frame (<paramref name="fromFrame"/>) is read as an image and
    /// filtered in the shader; pyramid levels are read through the bilinear sampler. The unused source binding gets a
    /// texture of the pyramid that the pass does not write.
    /// </summary>
    private void Blur(Rid source, Vector2I sourceSize, Rid target, Vector2 axis, bool fromFrame)
    {
        int index = Array.IndexOf(_levels, target);
        Vector2I size = RenderK.BlurTargets[index];
        Rid spare = _levels[index == _levels.Length - 1 ? 0 : _levels.Length - 1];
        UniformSlots slots = _blurSlots ??= new UniformSlots(RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.Image, RenderingDevice.UniformType.Image);
        Rid set;
        if (fromFrame)
        {
            slots.Set(0, _linearClamp, _lut);
            slots.Set(1, target);
            slots.Set(2, source);
            set = UniformSetCacheRD.GetCache(_blurShader, 0, slots.Array);
        }
        else
        {
            if (!_levelSets[index].IsValid || !_rd!.UniformSetIsValid(_levelSets[index]))
            {
                slots.Set(0, _linearClamp, source);
                slots.Set(1, target);
                slots.Set(2, spare);
                _levelSets[index] = _rd!.UniformSetCreate(slots.Array, _blurShader, 0);
            }

            set = _levelSets[index];
        }
        Span<float> push =
        [
            axis.X, axis.Y, sourceSize.X, sourceSize.Y,
            size.X, size.Y, RenderK.BlurNearWeight, RenderK.BlurFarWeight,
            fromFrame ? 1f : 0f, 0f, 0f, 0f,
        ];
        Span<byte> raw = MemoryMarshal.AsBytes(push);
        if (_blurBytes?.Length != raw.Length)
        {
            _blurBytes = new byte[raw.Length];
        }

        raw.CopyTo(_blurBytes);
        byte[] bytes = _blurBytes;
        long list = _rd!.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _blurPipeline);
        _rd.ComputeListBindUniformSet(list, set, 0);
        _rd.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
        _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        _rd.ComputeListEnd();
    }
}
