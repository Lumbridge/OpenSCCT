using System;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame.Render;

/// <summary>
/// A shadow camera's depth copy: after the camera's opaque pass, its depth buffer becomes linear distance along the
/// camera's axis in the camera's tile of a shared R32F atlas (svm_spot_depth.glsl). Used by the spot shadow lights
/// (<see cref="SpotShadowLight"/>) and the flares' cube shadows (<see cref="FlareLights"/>).
/// </summary>
internal sealed partial class ShadowDepthCapture : CompositorEffect
{
    public const string ShaderPath = "res://Shaders/svm_spot_depth.glsl";

    private readonly Vector2I _size;
    private readonly float _near;
    private readonly float _far;
    private readonly Vector2I _origin;
    private readonly bool _parallel;
    private readonly Func<Rid> _atlas;
    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _sampler;

    /// <param name="size">The camera's viewport size (a frame of another size is skipped).</param>
    /// <param name="near">The camera's near plane (m).</param>
    /// <param name="far">The camera's far plane (m).</param>
    /// <param name="origin">The tile's first texel in the atlas.</param>
    /// <param name="parallel">True for an orthographic camera.</param>
    /// <param name="atlas">The atlas texture, read each frame (invalid while it is not made yet).</param>
    public ShadowDepthCapture(Vector2I size, float near, float far, Vector2I origin, bool parallel, Func<Rid> atlas)
    {
        _size = size;
        _near = near;
        _far = far;
        _origin = origin;
        _parallel = parallel;
        _atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        EffectCallbackType = EffectCallbackTypeEnum.PostOpaque;
        AccessResolvedDepth = true;
        RenderingServer.CallOnRenderThread(Callable.From(Init));
    }

    /// <summary>The pipeline is made and the atlas exists: the tile is written each frame the camera renders.</summary>
    public bool Ready => _pipeline.IsValid && _atlas().IsValid;

    private volatile bool _released;

    private void Init()
    {
        if (_released)
        {
            return;
        }

        _rd = RenderingServer.GetRenderingDevice();
        if (_rd is null)
        {
            return;
        }

        _shader = ComputeShaderSource.CreateShader(_rd, ShaderPath, "svm_spot_depth", out string? error);
        if (!_shader.IsValid)
        {
            GD.PushError("svm_spot_depth: " + error);
            return;
        }

        _pipeline = _rd.ComputePipelineCreate(_shader);
        _sampler = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
        });
    }

    /// <summary>Frees the render device objects on the render thread (once); the effect draws nothing after.</summary>
    public void Release()
    {
        _released = true;
        if (_rd is not { } rd)
        {
            return;
        }

        Rid[] rids = [_pipeline, _shader, _sampler];
        _rd = null;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            foreach (Rid rid in rids)
            {
                if (rid.IsValid)
                {
                    rd.FreeRid(rid);
                }
            }
        }));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            Release();
        }

        base._Notification(what);
    }

    private Post.UniformSlots? _slots;
    private readonly byte[] _push = new byte[32];

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        // Release the scene buffers' managed wrapper here, on the render thread. Left to the garbage collector it can
        // hold the last reference to a freed viewport's buffers, whose destructor then frees GPU resources on the
        // finalizer thread (free_rid errors) and crashes the process at exit after a play session was left.
        // The frame's render data gets a new managed wrapper every call: released here it is not left to the finalizer
        // (a finalizable object and a tracked handle per frame for the garbage collector).
        using RenderData? frameData = renderData;
        using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
        Rid atlas = _atlas();
        if (_rd is null || !_pipeline.IsValid || !atlas.IsValid || sceneBuffers is not RenderSceneBuffersRD buffers)
        {
            return;
        }

        Vector2I size = buffers.GetInternalSize();
        if (size != _size)
        {
            return;
        }

        // The uniform list and the constant bytes are kept from frame to frame (no wrappers made per frame).
        Post.UniformSlots slots = _slots ??= new Post.UniformSlots(RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.Image);
        slots.Set(0, _sampler, buffers.GetDepthLayer(0));
        slots.Set(1, atlas);
        Rid set = UniformSetCacheRD.GetCache(_shader, 0, slots.Array);
        Span<float> push = [_near, _far, size.X, size.Y, _origin.X, _origin.Y, _parallel ? 1f : 0f, 0f];
        MemoryMarshal.AsBytes(push).CopyTo(_push);
        byte[] bytes = _push;
        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, set, 0);
        _rd.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
        _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        _rd.ComputeListEnd();
    }
}
