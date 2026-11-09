using Godot;

namespace OpenScctGame.Render.RdProto;

/// <summary>
/// SVM_RD_PROTO=frame: the main camera's compositor hook. Before Godot's opaque pass it hands the frame's colour and
/// depth buffers and scene data to <see cref="RdProtoGpu.RenderInFrame"/>, which draws the merged cells into them.
/// </summary>
internal sealed partial class RdProtoFrameEffect : CompositorEffect
{
    private readonly RdProtoGpu _gpu;

    public RdProtoFrameEffect(RdProtoGpu gpu)
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
            _gpu.RenderInFrame(buffers, sceneData);
        }
    }
}
