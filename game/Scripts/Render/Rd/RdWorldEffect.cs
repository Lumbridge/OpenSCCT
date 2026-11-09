using Godot;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// The main camera's compositor hook of the world pass: between Godot's depth pre-pass and its opaque pass it hands
/// the frame's colour and depth buffers and scene data to <see cref="RdWorldGpu.Render"/>.
/// </summary>
internal sealed partial class RdWorldEffect : CompositorEffect
{
    private readonly RdWorldGpu _gpu;

    public RdWorldEffect(RdWorldGpu gpu)
    {
        _gpu = gpu;
        EffectCallbackType = EffectCallbackTypeEnum.PreOpaque;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        // The managed wrappers are released here, on the render thread (see ShadowDepthCapture).
        using RenderData? frameData = renderData;
        using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
        using RenderSceneData? sceneData = renderData?.GetRenderSceneData();
        if (sceneBuffers is RenderSceneBuffersRD buffers && sceneData is not null)
        {
            _gpu.Render(buffers, sceneData);
        }
    }
}
