using System;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Provenance;

namespace OpenScctGame.Render.Post;

/// <summary>
/// The flash-bang after-image (spec/equipment/flash-bang.md rule 5): the viewport's post-render captures the frame
/// when PlayerController.bCaptureRemanence is set and, while RemanenceBlendFactor is above 0.0039, draws the capture
/// over every frame by that factor. Runs after the display-space pass, so it sees the frame as shown.
/// </summary>
[PortOf("eng:Render.Remanence", Spec = "spec/equipment/flash-bang.md#rules")]
public partial class RemanenceEffect : CompositorEffect
{
    /// <summary>Use a fresh frame each draw for fading double vision instead of a frozen flash capture.</summary>
    public bool DoubleVision { get; init; }

    public const string ShaderPath = "res://Shaders/svm_remanence.glsl";

    /// <summary>Factors at or under this draw nothing.</summary>
    [Provenance(ProvenanceKind.Decoded, "viewport post-render game 0x10a0b050: 0.0039 < RemanenceBlendFactor before render device virtual 0x84")]
    public const float DrawThreshold = 0.0039f;

    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _capture;
    private Vector2I _captureSize;
    private volatile int _captureRequests;
    private int _capturesDone;
    private volatile float _factor;
    private volatile float _spreadA, _spreadB;
    private bool _released;
    private UniformSlots? _slots;
    private byte[]? _push;
    private volatile bool _warmupRequested;
    private volatile bool _warmupComplete;
    // The colour buffer as a storage image (a copy of it with MSAA, rl:gfx.aa).
    private readonly StorageColor _storage = new("remanence");

    public RemanenceEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = true;
        RenderingServer.CallOnRenderThread(Callable.From(InitOnRenderThread));
    }

    /// <summary>PlayerController.RemanenceBlendFactor this frame (0..1).</summary>
    public float Factor
    {
        get => _factor;
        set => _factor = value;
    }

    /// <summary>The double vision's two U offsets this frame (<see cref="DoubleVisionSpread"/>).</summary>
    public Vector2 Spread
    {
        get => new(_spreadA, _spreadB);
        set => (_spreadA, _spreadB) = (value.X, value.Y);
    }

    /// <summary>
    /// The U offsets of the two copies of the picture the original draws for PlayerController.CrossEyedFactor
    /// <paramref name="factor"/> at viewport time <paramref name="seconds"/>: the first copy shows U 0 to 1 - X across
    /// the screen, the second U Y to 1, and the two are averaged. Each offset swings between 25/640 and 75/640 of the
    /// picture's width at factor 1.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10966010: (20 cos(1.8849557 t) + 5 cos(4.826743 t) + 50) x CrossEyedFactor x 0.0015625 and (20 cos(2.8274334 t) + 5 cos(4.826743 (t + 0.13)) + 50) x CrossEyedFactor x 0.0015625, t the viewport's time (double at +0x130); quad U 0..1-a on stage 0 and b..1 on stage 1; pixel shader 0x10c4ebd4 tex t0, tex t1, add_d2 r0, t0, t1")]
    [Provenance(ProvenanceKind.Estimated, "The viewport's time is taken as wall-clock seconds")]
    public static Vector2 DoubleVisionSpread(float factor, double seconds)
    {
        float f = Math.Clamp(factor, 0f, 1f);
        double a = (20.0 * Math.Cos(1.8849557195089517 * seconds) + 5.0 * Math.Cos(4.826742963254986 * seconds) + 50.0) * f * 0.0015625;
        double b = (20.0 * Math.Cos(2.8274335792634275 * seconds) + 5.0 * Math.Cos(4.826742963254986 * (seconds + 0.12999999523162842)) + 50.0) * f * 0.0015625;
        return new Vector2((float)a, (float)b);
    }

    /// <summary>Frames captured so far (render thread writes).</summary>
    public int Captures => _capturesDone;

    /// <summary>Frames the after-image was drawn on so far (render thread writes).</summary>
    public int Draws { get; private set; }

    /// <summary>Allocate this view's full-size capture/storage and descriptors while the loading gate is held.</summary>
    public void Preload() => _warmupRequested = true;

    public bool WarmupComplete => _warmupComplete || Error is not null || !Enabled || _released;

    public string? Error { get; private set; }

    /// <summary>Capture allocations, for first-use and resize diagnostics.</summary>
    public int CaptureAllocations { get; private set; }

    /// <summary>Asks for the next frame to be captured (bCaptureRemanence).</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10a0b050: bCaptureRemanence (bit 0x800 of 0x5f0) -> render device virtual 0x80, then the bit clears")]
    public void RequestCapture() => System.Threading.Interlocked.Increment(ref _captureRequests);

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            Release();
        }

        base._Notification(what);
    }

    /// <summary>Frees the GPU resources on the render thread.</summary>
    public void Release()
    {
        if (_released || _rd is not { } rd)
        {
            return;
        }

        _released = true;
        Rid shader = _shader, capture = _capture;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            _rd = null;
            _storage.Free();
            if (capture.IsValid)
            {
                rd.FreeRid(capture);
            }

            // The shader and pipeline are shared with the other remanence views (ComputeShaderSource).
            ComputeShaderSource.ReleaseShared(rd, shader);
        }));
    }

    private void InitOnRenderThread()
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd is null)
        {
            Error = "no RenderingDevice";
            return;
        }

        if (!ComputeShaderSource.AcquireShared(_rd, ShaderPath, "svm_remanence", out _shader, out _pipeline, out string? error))
        {
            Error = error ?? "shader creation failed";
            GD.PushError("svm_remanence: " + error);
        }
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        using RenderData? frameData = renderData;
        using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
        if (_rd is null || !_pipeline.IsValid || sceneBuffers is not RenderSceneBuffersRD buffers)
        {
            return;
        }

        Vector2I size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        float factor = _factor;
        bool warmup = _warmupRequested && !_warmupComplete;
        bool capture = DoubleVision ? factor > DrawThreshold : _captureRequests != _capturesDone;
        bool draw = factor > DrawThreshold && _capture.IsValid && _captureSize == size;
        if (!warmup && !capture && !draw)
        {
            return;
        }

        if ((warmup || capture) && (!_capture.IsValid || _captureSize != size))
        {
            if (_capture.IsValid)
            {
                _rd.FreeRid(_capture);
            }

            _capture = _rd.TextureCreate(new RDTextureFormat
            {
                Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                Width = (uint)size.X,
                Height = (uint)size.Y,
                UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit,
            }, new RDTextureView());
            _captureSize = size;
            CaptureAllocations++;
        }

        Rid color = _storage.Begin(_rd, buffers.GetColorLayer(0, false), size);
        if (!color.IsValid)
        {
            return;
        }

        if (capture || warmup)
        {
            Dispatch(color, 1f, 0f, size);
            if (capture) _capturesDone = _captureRequests;
            if (warmup) _warmupComplete = true;
        }

        if (factor > DrawThreshold && _capture.IsValid && _captureSize == size)
        {
            Dispatch(color, DoubleVision ? 3f : 2f, factor, size);
            Draws++;
        }

        _storage.End();
    }

    private void Dispatch(Rid color, float mode, float factor, Vector2I size)
    {
        UniformSlots slots = _slots ??= new UniformSlots(RenderingDevice.UniformType.Image, RenderingDevice.UniformType.Image);
        slots.Set(0, color);
        slots.Set(1, _capture);
        Rid set = UniformSetCacheRD.GetCache(_shader, 0, slots.Array);
        Span<float> push = [mode, factor, size.X, size.Y, _spreadA, _spreadB, 0f, 0f];
        Span<byte> raw = MemoryMarshal.AsBytes(push);
        _push ??= new byte[raw.Length];
        raw.CopyTo(_push);
        long list = _rd!.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, set, 0);
        _rd.ComputeListSetPushConstant(list, _push, (uint)_push.Length);
        _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        _rd.ComputeListEnd();
    }
}
