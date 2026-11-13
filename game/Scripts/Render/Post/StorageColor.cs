using System;
using Godot;

namespace OpenScctGame.Render.Post;

/// <summary>
/// The resolved colour buffer as a storage image for in-place post passes. Callers explicitly request the
/// single-sample layer. When that layer lacks storage usage but permits sampling and copying back, <see cref="Begin"/>
/// copies it through a sampler into this helper's storage texture, and <see cref="End"/> copies the result back
/// before tonemapping. Unsupported sample/usage combinations skip the pass rather than bind an invalid image.
/// Render thread only.
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
    private bool _checkedRasterBack;
    private RenderingDevice.DataFormat _checkedFormat;
    private Rid _presentShader, _presentPipeline;
    private long _presentFormat = -1;
    private UniformSlots? _presentSlots;
    private readonly Godot.Collections.Array<Rid> _presentTarget = [default(Rid)];
    private readonly Godot.Collections.Array<RDFramebufferPass> _presentPasses = [new RDFramebufferPass { ColorAttachments = [0] }];
    private static readonly Color[] NoClear = [];

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
            _checkedStorage = (format.UsageBits & RenderingDevice.TextureUsageBits.StorageBit) != 0
                && format.Samples == RenderingDevice.TextureSamples.Samples1
                && format.Format == RenderingDevice.DataFormat.R16G16B16A16Sfloat;
            _checkedCopyBack = (format.UsageBits & RenderingDevice.TextureUsageBits.CanCopyToBit) != 0
                && (format.UsageBits & RenderingDevice.TextureUsageBits.SamplingBit) != 0
                && format.Samples == RenderingDevice.TextureSamples.Samples1;
            _checkedRasterBack = (format.UsageBits & RenderingDevice.TextureUsageBits.ColorAttachmentBit) != 0
                && (format.UsageBits & RenderingDevice.TextureUsageBits.SamplingBit) != 0
                && format.Samples == RenderingDevice.TextureSamples.Samples1;
            _checkedFormat = format.Format;
            if (!_checkedStorage)
            {
                GD.Print($"SVM_RENDER {_owner}: the colour buffer is not a compatible storage image; "
                    + (_checkedCopyBack || _checkedRasterBack ? "the post passes run on a copy" : "it cannot be copied back either, the post passes are skipped")
                    + $"; usage={format.UsageBits}, samples={format.Samples}, type={format.TextureType}");
            }
        }

        if (_checkedStorage)
        {
            return color;
        }

        if ((!_checkedCopyBack && !_checkedRasterBack) || _checkedFormat != RenderingDevice.DataFormat.R16G16B16A16Sfloat || !EnsureCopy(rd, size))
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

        if (_checkedCopyBack)
            _rd.TextureCopy(_work, _pending, Vector3.Zero, Vector3.Zero, new Vector3(_workSize.X, _workSize.Y, 1f), 0, 0, 0, 0);
        else
            Present(_rd, _pending);
        _pending = default;
    }

    /// <summary>Frees this helper's GPU resources (render thread).</summary>
    public void Free()
    {
        if (_rd is null)
        {
            return;
        }

        foreach (Rid rid in (Rid[])[_work, _nearest, _presentPipeline, _presentShader])
        {
            if (rid.IsValid)
            {
                _rd.FreeRid(rid);
            }
        }

        // The copy shader and pipeline are shared with the other helpers (ComputeShaderSource).
        ComputeShaderSource.ReleaseShared(_rd, _copyShader);

        _work = _copyPipeline = _copyShader = _nearest = default;
        _presentPipeline = _presentShader = default;
        _presentFormat = -1;
        _rd = null;
        _checked = default;
    }

    // Mobile/WebGPU scene buffers are render attachments without storage or copy-destination usage.
    // Draw the processed HDR copy back into that attachment before Godot's tone mapping.
    private void Present(RenderingDevice rd, Rid color)
    {
        _presentTarget[0] = color;
        Rid framebuffer = FramebufferCacheRD.GetCacheMultipass(_presentTarget, _presentPasses, 1);
        long format = rd.FramebufferGetFormat(framebuffer);
        if (!_presentShader.IsValid)
        {
            using var source = new RDShaderSource
            {
                Language = RenderingDevice.ShaderLanguage.Glsl,
                SourceVertex = "#version 450\nvoid main() { vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2)); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }",
                SourceFragment = "#version 450\nlayout(set=0,binding=0) uniform sampler2D source_tex; layout(location=0) out vec4 color; void main() { color = texelFetch(source_tex, ivec2(gl_FragCoord.xy), 0); }",
            };
            using RDShaderSpirV spirv = rd.ShaderCompileSpirVFromSource(source);
            _presentShader = rd.ShaderCreateFromSpirV(spirv);
        }
        if (!_presentPipeline.IsValid || _presentFormat != format)
        {
            if (_presentPipeline.IsValid) rd.FreeRid(_presentPipeline);
            using var raster = new RDPipelineRasterizationState { CullMode = RenderingDevice.PolygonCullMode.Disabled };
            using var samples = new RDPipelineMultisampleState();
            using var depth = new RDPipelineDepthStencilState();
            using var blend = new RDPipelineColorBlendState { Attachments = [new RDPipelineColorBlendStateAttachment()] };
            _presentPipeline = rd.RenderPipelineCreate(_presentShader, format, -1, RenderingDevice.RenderPrimitive.Triangles,
                raster, samples, depth, blend);
            _presentFormat = format;
        }
        UniformSlots slots = _presentSlots ??= new UniformSlots(RenderingDevice.UniformType.SamplerWithTexture);
        slots.Set(0, _nearest, _work);
        Rid set = UniformSetCacheRD.GetCache(_presentShader, 0, slots.Array);
        long list = rd.DrawListBegin(framebuffer, RenderingDevice.DrawFlags.IgnoreAll, NoClear);
        rd.DrawListBindRenderPipeline(list, _presentPipeline);
        rd.DrawListBindUniformSet(list, set, 0);
        rd.DrawListDraw(list, false, 1, 3);
        rd.DrawListEnd();
    }

    private bool EnsureCopy(RenderingDevice rd, Vector2I size)
    {
        if (_rd is null)
        {
            _rd = rd;
            if (!ComputeShaderSource.AcquireSharedSource(rd, CopySource, "svm_colour_copy", out _copyShader, out _copyPipeline, out string? error))
            {
                GD.PushError($"SVM_RENDER {_owner}: colour copy shader: {error}");
                return false;
            }

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
