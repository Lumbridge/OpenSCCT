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
    private bool _released;
    private UniformSlots? _slots;
    private byte[]? _push;
    // The colour buffer as a storage image (a copy of it with MSAA, rl:gfx.aa).
    private readonly StorageColor _storage = new("remanence");

    public RemanenceEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        RenderingServer.CallOnRenderThread(Callable.From(InitOnRenderThread));
    }

    /// <summary>PlayerController.RemanenceBlendFactor this frame (0..1).</summary>
    public float Factor
    {
        get => _factor;
        set => _factor = value;
    }

    /// <summary>Frames captured so far (render thread writes).</summary>
    public int Captures => _capturesDone;

    /// <summary>Frames the after-image was drawn on so far (render thread writes).</summary>
    public int Draws { get; private set; }

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
        Rid[] rids = [_pipeline, _shader, _capture];
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            _rd = null;
            _storage.Free();
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
            return;
        }

        _shader = ComputeShaderSource.CreateShader(_rd, ShaderPath, "svm_remanence", out string? error);
        if (!_shader.IsValid)
        {
            GD.PushError("svm_remanence: " + error);
            return;
        }

        _pipeline = _rd.ComputePipelineCreate(_shader);
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
        bool capture = DoubleVision ? factor > DrawThreshold : _captureRequests != _capturesDone;
        bool draw = factor > DrawThreshold && _capture.IsValid && _captureSize == size;
        if (!capture && !draw)
        {
            return;
        }

        if (capture && (!_capture.IsValid || _captureSize != size))
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
        }

        Rid color = _storage.Begin(_rd, buffers.GetColorLayer(0), size);
        if (!color.IsValid)
        {
            return;
        }

        if (capture)
        {
            Dispatch(color, 1f, 0f, size);
            _capturesDone = _captureRequests;
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
        Span<float> push = [mode, factor, size.X, size.Y];
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
