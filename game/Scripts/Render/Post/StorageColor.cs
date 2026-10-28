using System;
using Godot;

namespace OpenScctGame.Render.Post;

/// <summary>
/// The colour buffer as a storage image for the post passes, which read and write it in place. Without MSAA the
/// scene's colour buffer is one. With MSAA the buffer the passes get is the resolve target, which Godot 4.7.2 makes
/// without storage use (binding it as an image failed and crashed the armoury preview, 5678a73). Then the passes run
/// on a copy: <see cref="Begin"/> copies the resolved frame into a storage texture of this helper's own (a compute
/// copy through a sampler, as the resolve target cannot be copied from), and <see cref="End"/> copies the result back
/// into the resolve target, which Godot's tonemap pass reads next. Render thread only.
/// </summary>
public sealed class StorageColor
{
    private const string CopySource = """
        #version 450
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(set = 0, binding = 0) uniform sampler2D source_tex;
        layout(rgba16f, set = 0, binding = 1) uniform writeonly image2D target_image;
        void main() {
            ivec2 xy = ivec2(gl_GlobalInvocationID.xy);
            ivec2 size = imageSize(target_image);
            if (xy.x >= size.x || xy.y >= size.y) {
                return;
            }
            imageStore(target_image, xy, texelFetch(source_tex, xy, 0));
        }
        """;

    private readonly string _owner;
    private RenderingDevice? _rd;
    private Rid _copyShader;
    private Rid _copyPipeline;
    private Rid _nearest;
    private Rid _work;
    private Vector2I _workSize;
    private RenderingDevice.DataFormat _workFormat;
    private UniformSlots? _slots;

    // The last colour buffer looked at and what it allows (TextureGetFormat makes a wrapper, so once per buffer).
    private Rid _checked;
    private bool _checkedStorage;
    private bool _checkedCopyBack;
    private RenderingDevice.DataFormat _checkedFormat;

    // The buffer of this frame's Begin, when the passes run on the copy.
    private Rid _pending;
    private bool _warned;

    public StorageColor(string owner) => _owner = owner;

    /// <summary>
    /// The image the passes write for <paramref name="color"/> (<paramref name="size"/> pixels): the buffer itself when
    /// it is a storage image, else this helper's copy of it, or an invalid id when neither works (the passes skip).
    /// </summary>
    public Rid Begin(RenderingDevice rd, Rid color, Vector2I size)
    {
        ArgumentNullException.ThrowIfNull(rd);
        _pending = default;
        if (color != _checked)
        {
            using RDTextureFormat format = rd.TextureGetFormat(color);
            _checked = color;
            _checkedStorage = (format.UsageBits & RenderingDevice.TextureUsageBits.StorageBit) != 0;
            _checkedCopyBack = (format.UsageBits & RenderingDevice.TextureUsageBits.CanCopyToBit) != 0
                && (format.UsageBits & RenderingDevice.TextureUsageBits.SamplingBit) != 0
                && format.Samples == RenderingDevice.TextureSamples.Samples1;
            _checkedFormat = format.Format;
            if (!_checkedStorage)
            {
                GD.Print($"SVM_RENDER {_owner}: the colour buffer is not a storage image (MSAA resolve target); "
                    + (_checkedCopyBack ? "the post passes run on a copy" : "it cannot be copied back either, the post passes are skipped"));
            }
        }

        if (_checkedStorage)
        {
            return color;
        }

        if (!_checkedCopyBack || _checkedFormat != RenderingDevice.DataFormat.R16G16B16A16Sfloat || !EnsureCopy(rd, size))
        {
            if (!_warned)
            {
                _warned = true;
                GD.PushWarning($"SVM_RENDER {_owner}: no storage copy of the colour buffer ({_checkedFormat}); post pass skipped");
            }

            return default;
        }

        UniformSlots slots = _slots ??= new UniformSlots(RenderingDevice.UniformType.SamplerWithTexture, RenderingDevice.UniformType.Image);
        slots.Set(0, _nearest, color);
        slots.Set(1, _work);
        Rid set = UniformSetCacheRD.GetCache(_copyShader, 0, slots.Array);
        long list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, _copyPipeline);
        rd.ComputeListBindUniformSet(list, set, 0);
        rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        rd.ComputeListEnd();
        _pending = color;
        return _work;
    }

    /// <summary>Puts the passes' result back into the colour buffer of the last <see cref="Begin"/> when they ran on the copy.</summary>
    public void End()
    {
        if (!_pending.IsValid || _rd is null)
        {
            return;
        }

        _rd.TextureCopy(_work, _pending, Vector3.Zero, Vector3.Zero, new Vector3(_workSize.X, _workSize.Y, 1f), 0, 0, 0, 0);
        _pending = default;
    }

    /// <summary>Frees this helper's GPU resources (render thread).</summary>
    public void Free()
    {
        if (_rd is null)
        {
            return;
        }

        foreach (Rid rid in (Rid[])[_work, _copyPipeline, _copyShader, _nearest])
        {
            if (rid.IsValid)
            {
                _rd.FreeRid(rid);
            }
        }

        _work = _copyPipeline = _copyShader = _nearest = default;
        _rd = null;
        _checked = default;
    }

    private bool EnsureCopy(RenderingDevice rd, Vector2I size)
    {
        if (_rd is null)
        {
            _rd = rd;
            using var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = CopySource };
            using RDShaderSpirV? spirv = rd.ShaderCompileSpirVFromSource(source);
            if (spirv is null || !string.IsNullOrEmpty(spirv.CompileErrorCompute))
            {
                GD.PushError($"SVM_RENDER {_owner}: colour copy shader: {spirv?.CompileErrorCompute ?? "no SPIR-V"}");
                return false;
            }

            _copyShader = rd.ShaderCreateFromSpirV(spirv, "svm_colour_copy");
            _copyPipeline = rd.ComputePipelineCreate(_copyShader);
            using var sampler = new RDSamplerState
            {
                MinFilter = RenderingDevice.SamplerFilter.Nearest,
                MagFilter = RenderingDevice.SamplerFilter.Nearest,
            };
            _nearest = rd.SamplerCreate(sampler);
        }

        if (!_copyPipeline.IsValid)
        {
            return false;
        }

        if (_work.IsValid && _workSize == size && _workFormat == _checkedFormat)
        {
            return true;
        }

        if (_work.IsValid)
        {
            rd.FreeRid(_work);
        }

        using var format = new RDTextureFormat
        {
            Format = _checkedFormat,
            Width = (uint)size.X,
            Height = (uint)size.Y,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit
                | RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanCopyToBit,
        };
        using var view = new RDTextureView();
        _work = rd.TextureCreate(format, view);
        _workSize = size;
        _workFormat = _checkedFormat;
        return _work.IsValid;
    }
}
