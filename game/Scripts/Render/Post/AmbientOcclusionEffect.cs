using System;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.Post;

/// <summary>Optional depth-based contact shading before vision post-processing, using opaque surface depth.</summary>
public partial class AmbientOcclusionEffect : CompositorEffect
{
    public const string ShaderPath = "res://Shaders/svm_ambient_occlusion.glsl";
    public sealed record Frame(bool On, Vector4 Projection, float Near, float Far);
    private volatile Frame _frame = new(false, new(1, 1, 0, 0), .01f, 100f);
    private RenderingDevice? _rd;
    private Rid _shader, _pipeline, _sampler;
    private readonly StorageColor _storage = new("ambient-occlusion");
    private UniformSlots? _slots;
    private readonly byte[] _push = new byte[48];
    private bool _released, _attempted;
    public int Draws { get; private set; }
    public string? Error { get; private set; }

    public AmbientOcclusionEffect()
    {
        Enabled = false;
        // With MSAA, the transparent pass resolves color again; PostOpaque edits to the resolved image are lost.
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = true;
        AccessResolvedDepth = true;
    }

    public static bool SupportsVision(int mode) => mode is RenderK.RendMapNormal or RenderK.RendMapNight;

    public void Configure(bool on, int mode, Camera3D camera)
    {
        Projection p = camera.GetCameraProjection();
        bool active = on && SupportsVision(mode);
        _frame = new(active, new(p.X.X, p.Y.Y, p.Z.X, p.Z.Y), camera.Near, camera.Far);
        // Disabled means the renderer can omit this effect's resolve requests as well as its dispatch.
        if (Enabled != active) Enabled = active;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        using RenderData? frameData = renderData;
        Frame f = _frame;
        if (_released || !f.On) return;
        if (!_attempted) Init();
        using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
        if (_rd is null || !_pipeline.IsValid || sceneBuffers is not RenderSceneBuffersRD buffers) return;
        Vector2I size = buffers.GetInternalSize();
        if (size.X < 2 || size.Y < 2) return;
        Span<float> constants = [size.X, size.Y, .6f, 1.25f,
            f.Projection.X, f.Projection.Y, f.Projection.Z, f.Projection.W,
            f.Near, f.Far, .25f, 60f];
        MemoryMarshal.AsBytes(constants).CopyTo(_push);
        for (uint view = 0; view < buffers.GetViewCount(); view++)
        {
            Rid color = _storage.Begin(_rd, buffers.GetColorLayer(view, false), size);
            if (!color.IsValid) continue;
            UniformSlots slots = _slots ??= new UniformSlots(RenderingDevice.UniformType.Image, RenderingDevice.UniformType.SamplerWithTexture);
            slots.Set(0, color);
            slots.Set(1, _sampler, buffers.GetDepthLayer(view, false));
            Rid set = UniformSetCacheRD.GetCache(_shader, 0, slots.Array);
            long list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, set, 0);
            _rd.ComputeListSetPushConstant(list, _push, (uint)_push.Length);
            _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
            _rd.ComputeListEnd();
            _storage.End();
            Draws++;
        }
    }

    private void Init()
    {
        _attempted = true;
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd is null) { Error = "requires RenderingDevice"; return; }
        _shader = ComputeShaderSource.CreateShader(_rd, ShaderPath, "svm_ambient_occlusion", out string? error);
        if (!_shader.IsValid) { Error = error; GD.PushError("Ambient occlusion: " + error); return; }
        _pipeline = _rd.ComputePipelineCreate(_shader);
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
        Rid[] rids = [_pipeline, _shader, _sampler];
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            _storage.Free();
            foreach (Rid rid in rids) if (rid.IsValid) rd.FreeRid(rid);
            _rd = null;
        }));
    }
}
