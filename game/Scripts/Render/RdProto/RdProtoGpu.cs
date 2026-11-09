using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using OpenScctGame.Render.Data;
using RD = Godot.RenderingDevice;

namespace OpenScctGame.Render.RdProto;

/// <summary>One frame's work for the render thread, filled on the main thread (<see cref="RdProtoRenderer"/>).</summary>
internal sealed class RdProtoFrame
{
    public const int UboSize = 144;
    public readonly byte[] Ubo = new byte[UboSize];
    public int[] Draws = new int[256];
    public int DrawCount;
    public Color Clear;
    public Vector2I Size;
    /// <summary>SVM_RD_PROTO=frame: where the sky zone is seen from (the SkyZoneInfo).</summary>
    public Vector3 SkyOrigin;
}

/// <summary>
/// The prototype's render-thread half: every RenderingDevice object it owns and the per-frame draw. A plain class, not
/// a Node: the callables queued with <see cref="RenderingServer.CallOnRenderThread"/> stay valid after the node that
/// queued them is freed (a Callable whose target is a freed GodotObject is silently dropped). Every method here runs
/// on the render thread (in Godot's default thread model that is the main thread, inline).
/// </summary>
internal sealed class RdProtoGpu
{
    private const string BeginStamp = "SVM_RD_PROTO begin";
    private const string EndStamp = "SVM_RD_PROTO end";

    private readonly RdProtoGeometry _geometry;
    private readonly RD.PolygonCullMode _cull;
    private readonly Lock _queueLock = new();
    private readonly Queue<RdProtoFrame> _pending = new();
    private readonly Stack<RdProtoFrame> _free = new();
    private readonly Color[] _clear = new Color[1];
    private readonly Stopwatch _record = new();

    private RenderingDevice? _rd;
    private bool _ready, _failed;
    private Rid _shaderSolid, _shaderMasked, _vertexBuffer, _vertexArray, _indexBuffer, _ubo, _frameSet;
    private Rid _samplerDiffuse, _samplerLightmap, _white, _black;
    private long _vertexFormat, _framebufferFormat = -1;
    private Rid[] _indexArrays = [];
    private Rid[] _batchSets = [];
    private int[] _batchPipelines = [];
    private readonly Dictionary<(Rid, Rid), Rid> _sets = [];
    private readonly Rid[] _pipelines = new Rid[12];

    // SVM_RD_PROTO=frame: framebuffers over Godot's own colour and depth layers, the pipelines per framebuffer format
    // (the format changes with the MSAA setting), and the last frame the main thread submitted.
    private readonly Dictionary<(Rid Color, Rid Depth), Rid> _sceneFramebuffers = [];
    private readonly Dictionary<long, Rid[]> _scenePipelines = [];
    private readonly byte[] _sceneUbo = new byte[RdProtoFrame.UboSize];
    // The sky zone's depth scale in the frame: behind the whole world, and under the 2^-40 that readers of the view's
    // depth take as sky or nothing drawn (svm_sky_depth.gdshaderinc).
    private const float SceneSkyDepthScale = 8.8817842e-16f;
    private RdProtoFrame? _current;

    // Targets: the current one, and older ones freed a few frames after a resize (the canvas may still show them).
    private Rid _color, _depth, _framebuffer;
    private Vector2I _size;
    private readonly List<(long Frame, Rid Color, Rid Depth)> _retired = [];
    private long _frame;

    // The colour target the main thread should show (Texture2DRD), handed over under the lock.
    private Rid _latestColor;

    // Statistics, read by the main thread under the lock.
    private static readonly Lock s_statsLock = new();
    private static double s_gpuSum, s_recordSum, s_caseGpuSum;
    private static int s_gpuCount, s_recordCount, s_caseGpuCount;

    public RdProtoGpu(RdProtoGeometry geometry, RD.PolygonCullMode cull)
    {
        _geometry = geometry;
        _cull = cull;
    }

    public bool Failed => _failed;

    public Rid LatestColor
    {
        get { lock (_queueLock) return _latestColor; }
    }

    /// <summary>A frame object to fill (main thread).</summary>
    public RdProtoFrame Rent()
    {
        lock (_queueLock) return _free.Count > 0 ? _free.Pop() : new RdProtoFrame();
    }

    /// <summary>Queues a filled frame; the next <see cref="Render"/> on the render thread takes it (main thread).</summary>
    public void Submit(RdProtoFrame frame)
    {
        lock (_queueLock) _pending.Enqueue(frame);
    }

    /// <summary>Mean GPU ms and render-thread record ms since the last call (main thread, once a second).</summary>
    public static (double GpuMs, double RecordMs, int Samples) TakeSecond()
    {
        lock (s_statsLock)
        {
            var r = (s_gpuCount > 0 ? s_gpuSum / s_gpuCount : double.NaN, s_recordCount > 0 ? s_recordSum / s_recordCount : double.NaN, s_gpuCount);
            s_gpuSum = s_recordSum = 0;
            s_gpuCount = s_recordCount = 0;
            return r;
        }
    }

    /// <summary>Starts a new benchmark case (FpsBenchmark).</summary>
    public static void ResetCase()
    {
        lock (s_statsLock)
        {
            s_caseGpuSum = 0;
            s_caseGpuCount = 0;
        }
    }

    /// <summary>The mean GPU ms of the custom pass since <see cref="ResetCase"/>, or null when nothing was timed.</summary>
    public static double? CaseGpuMs
    {
        get
        {
            lock (s_statsLock) return s_caseGpuCount > 0 ? s_caseGpuSum / s_caseGpuCount : null;
        }
    }

    /// <summary>Records one frame: clear, opaque front to back, masked, then blended back to front (render thread).</summary>
    public void Render()
    {
        RdProtoFrame? frame;
        lock (_queueLock)
        {
            if (!_pending.TryDequeue(out frame)) return;
        }

        try
        {
            if (!_failed) RenderFrame(frame);
        }
        catch (Exception error)
        {
            _failed = true;
            GD.PushError("SVM_RD_PROTO render failed: " + error);
        }
        finally
        {
            lock (_queueLock) _free.Push(frame);
        }
    }

    private void RenderFrame(RdProtoFrame frame)
    {
        _frame++;
        if (!_ready)
        {
            Init(frame.Size, ownTargets: true);
            if (_failed) return;
        }

        RenderingDevice rd = _rd!;
        ReadTimestamps(rd);
        EnsureTargets(rd, frame.Size);
        FreeRetired(rd, force: false);

        _record.Restart();
        rd.BufferUpdate(_ubo, 0, RdProtoFrame.UboSize, frame.Ubo);
        rd.CaptureTimestamp(BeginStamp);
        _clear[0] = frame.Clear;
        // Reverse Z: clear depth to 0 (far), test GREATER_OR_EQUAL.
        long dl = rd.DrawListBegin(_framebuffer, RD.DrawFlags.ClearAll, _clear, 0f, 0, null, 0);
        Draw(rd, dl, frame, _pipelines);
        rd.DrawListEnd();
        rd.CaptureTimestamp(EndStamp);
        double recordMs = _record.Elapsed.TotalMilliseconds;
        lock (s_statsLock)
        {
            s_recordSum += recordMs;
            s_recordCount++;
        }
    }

    private void Draw(RenderingDevice rd, long dl, RdProtoFrame frame, Rid[] pipelines)
    {
        int pipeline = -1;
        Rid set = default;
        for (int k = 0; k < frame.DrawCount; k++)
        {
            int i = frame.Draws[k];
            if (_batchPipelines[i] != pipeline)
            {
                bool first = pipeline < 0;
                pipeline = _batchPipelines[i];
                rd.DrawListBindRenderPipeline(dl, pipelines[pipeline]);
                if (first)
                {
                    rd.DrawListBindVertexArray(dl, _vertexArray);
                    rd.DrawListBindUniformSet(dl, _frameSet, 0);
                }
            }

            if (_batchSets[i] != set)
            {
                set = _batchSets[i];
                rd.DrawListBindUniformSet(dl, set, 1);
            }

            rd.DrawListBindIndexArray(dl, _indexArrays[i]);
            rd.DrawListDraw(dl, true, 1, 0);
        }
    }

    /// <summary>
    /// SVM_RD_PROTO=frame (render thread, from the main camera's compositor between Godot's depth pre-pass and its
    /// opaque pass): clears the colour (the environment keeps the background, so Godot's opaque pass loads it) and
    /// draws the newest submitted frame into Godot's own colour and depth buffers with the projection and view
    /// Godot's shaders use this frame. The cells and everything Godot draws share one depth buffer, and Godot's
    /// opaque pass is hidden by the cells where they are nearer.
    /// </summary>
    public void RenderInFrame(RenderSceneBuffersRD buffers, RenderSceneData scene)
    {
        lock (_queueLock)
        {
            while (_pending.TryDequeue(out RdProtoFrame? next))
            {
                if (_current is not null) _free.Push(_current);
                _current = next;
            }
        }

        if (_current is null || _failed) return;
        try
        {
            DrawInFrame(_current, buffers, scene);
        }
        catch (Exception error)
        {
            _failed = true;
            GD.PushError("SVM_RD_PROTO in-frame render failed: " + error);
        }
    }

    private void DrawInFrame(RdProtoFrame frame, RenderSceneBuffersRD buffers, RenderSceneData scene)
    {
        _frame++;
        if (!_ready)
        {
            Init(default, ownTargets: false);
            if (_failed) return;
        }

        RenderingDevice rd = _rd!;
        bool msaa = buffers.GetMsaa3D() != RenderingServer.ViewportMsaa.Disabled;
        Rid color = buffers.GetColorLayer(0, msaa);
        Rid depth = buffers.GetDepthLayer(0, msaa);
        if (!color.IsValid || !depth.IsValid) return;
        if (!_sceneFramebuffers.TryGetValue((color, depth), out Rid framebuffer) || !rd.FramebufferIsValid(framebuffer))
        {
            // A resize frees Godot's textures and, with them, the framebuffers made here.
            foreach ((Rid, Rid) key in new List<(Rid, Rid)>(_sceneFramebuffers.Keys))
            {
                if (!rd.FramebufferIsValid(_sceneFramebuffers[key])) _sceneFramebuffers.Remove(key);
            }

            framebuffer = rd.FramebufferCreate([color, depth], -1, 1);
            _sceneFramebuffers[(color, depth)] = framebuffer;
        }

        long format = rd.FramebufferGetFormat(framebuffer);
        if (!_scenePipelines.TryGetValue(format, out Rid[]? pipelines))
        {
            pipelines = new Rid[12];
            using RDTextureFormat colorFormat = rd.TextureGetFormat(color);
            CreatePipelines(rd, pipelines, format, colorFormat.Samples);
            _scenePipelines[format] = pipelines;
            GD.Print($"SVM_RD_PROTO in-frame pipelines: colour {colorFormat.Format}, samples {colorFormat.Samples}");
        }

        // Godot's corrected projection (y flipped, reverse Z) and its view: the same clip space as its own shaders.
        // The sky zone is seen from the SkyZoneInfo with the camera's rotation, its depth scaled towards the far plane.
        frame.Ubo.CopyTo(_sceneUbo, 0);
        Projection projection = scene.GetCamProjection();
        Transform3D eye = scene.GetCamTransform();
        Projection clip = projection * new Projection(eye.AffineInverse());
        Projection sky = projection * new Projection(new Transform3D(eye.Basis, frame.SkyOrigin).AffineInverse());
        sky = new Projection(
            new Vector4(sky.X.X, sky.X.Y, sky.X.Z * SceneSkyDepthScale, sky.X.W),
            new Vector4(sky.Y.X, sky.Y.Y, sky.Y.Z * SceneSkyDepthScale, sky.Y.W),
            new Vector4(sky.Z.X, sky.Z.Y, sky.Z.Z * SceneSkyDepthScale, sky.Z.W),
            new Vector4(sky.W.X, sky.W.Y, sky.W.Z * SceneSkyDepthScale, sky.W.W));
        Span<float> ubo = MemoryMarshal.Cast<byte, float>(_sceneUbo.AsSpan());
        RdProtoRenderer.Write(clip, ubo[..16]);
        RdProtoRenderer.Write(sky, ubo[16..32]);

        _record.Restart();
        rd.BufferUpdate(_ubo, 0, RdProtoFrame.UboSize, _sceneUbo);
        rd.CaptureTimestamp(BeginStamp);
        _clear[0] = frame.Clear;
        long dl = rd.DrawListBegin(framebuffer, RD.DrawFlags.ClearColor0, _clear, 0f, 0, null, 0);
        Draw(rd, dl, frame, pipelines);
        rd.DrawListEnd();
        rd.CaptureTimestamp(EndStamp);
        double recordMs = _record.Elapsed.TotalMilliseconds;
        lock (s_statsLock)
        {
            s_recordSum += recordMs;
            s_recordCount++;
        }
    }

    /// <summary>The GPU time between this pass's two timestamps, from the frame RD last read back (a few frames old).</summary>
    private static void ReadTimestamps(RenderingDevice rd)
    {
        uint count = rd.GetCapturedTimestampsCount();
        ulong begin = 0, end = 0;
        for (uint i = 0; i < count; i++)
        {
            string name = rd.GetCapturedTimestampName(i);
            if (name == BeginStamp) begin = rd.GetCapturedTimestampGpuTime(i);
            else if (name == EndStamp) end = rd.GetCapturedTimestampGpuTime(i);
        }

        if (begin == 0 || end < begin) return;
        // GetCapturedTimestampGpuTime is in microseconds.
        double ms = (end - begin) / 1000.0;
        lock (s_statsLock)
        {
            s_gpuSum += ms;
            s_gpuCount++;
            s_caseGpuSum += ms;
            s_caseGpuCount++;
        }
    }

    private void Init(Vector2I size, bool ownTargets)
    {
        _ready = true;
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd is null)
        {
            Fail("no RenderingDevice (Compatibility renderer or headless)");
            return;
        }

        RenderingDevice rd = _rd;
        var clock = Stopwatch.StartNew();
        _shaderSolid = CompileShader(rd, masked: false);
        _shaderMasked = CompileShader(rd, masked: true);
        if (!_shaderSolid.IsValid || !_shaderMasked.IsValid)
        {
            Fail("shader compile failed");
            return;
        }

        // One interleaved stream, binding 0: position, UV, UV2, RGBA8 colour (RdProtoGeometry.Vertex, 32 bytes).
        const uint stride = 32;
        _vertexFormat = rd.VertexFormatCreate(
        [
            new RDVertexAttribute { Location = 0, Offset = 0, Format = RD.DataFormat.R32G32B32Sfloat, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 1, Offset = 12, Format = RD.DataFormat.R32G32Sfloat, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 2, Offset = 20, Format = RD.DataFormat.R32G32Sfloat, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 3, Offset = 28, Format = RD.DataFormat.R8G8B8A8Unorm, Stride = stride, Binding = 0 },
        ]);
        ReadOnlySpan<byte> vertices = MemoryMarshal.AsBytes(_geometry.Vertices.AsSpan());
        ReadOnlySpan<byte> indices = MemoryMarshal.AsBytes(_geometry.Indices.AsSpan());
        if (vertices.Length == 0 || indices.Length == 0)
        {
            Fail("the map gave no merged geometry");
            return;
        }

        _vertexBuffer = rd.VertexBufferCreate((uint)vertices.Length, vertices, 0);
        _vertexArray = rd.VertexArrayCreate((uint)_geometry.Vertices.Length, _vertexFormat, [_vertexBuffer], ReadOnlySpan<long>.Empty);
        _indexBuffer = rd.IndexBufferCreate((uint)_geometry.Indices.Length, RD.IndexBufferFormat.Uint32, indices, false, 0);

        _ubo = rd.UniformBufferCreate(RdProtoFrame.UboSize, ReadOnlySpan<byte>.Empty, 0);
        var frameUniform = new RDUniform { UniformType = RD.UniformType.UniformBuffer, Binding = 0 };
        frameUniform.AddId(_ubo);
        _frameSet = rd.UniformSetCreate([frameUniform], _shaderSolid, 0);

        int anisotropy = ProjectSettings.GetSetting("rendering/textures/default_filters/anisotropic_filtering_level", 2).AsInt32();
        _samplerDiffuse = rd.SamplerCreate(new RDSamplerState
        {
            MagFilter = RD.SamplerFilter.Linear, MinFilter = RD.SamplerFilter.Linear, MipFilter = RD.SamplerFilter.Linear,
            RepeatU = RD.SamplerRepeatMode.Repeat, RepeatV = RD.SamplerRepeatMode.Repeat,
            // The project setting is a level (0 off, 1 2x, 2 4x, 3 8x, 4 16x), as Godot's own world materials use it.
            UseAnisotropy = anisotropy > 0, AnisotropyMax = 1 << Math.Clamp(anisotropy, 0, 4),
        });
        _samplerLightmap = rd.SamplerCreate(new RDSamplerState
        {
            MagFilter = RD.SamplerFilter.Linear, MinFilter = RD.SamplerFilter.Linear, MipFilter = RD.SamplerFilter.Nearest,
            RepeatU = RD.SamplerRepeatMode.ClampToEdge, RepeatV = RD.SamplerRepeatMode.ClampToEdge,
        });
        _white = Pixel(rd, 0xffffffffu);
        _black = Pixel(rd, 0xff000000u);

        List<RdProtoGeometry.Batch> batches = _geometry.Batches;
        _indexArrays = new Rid[batches.Count];
        _batchSets = new Rid[batches.Count];
        _batchPipelines = new int[batches.Count];
        for (int i = 0; i < batches.Count; i++)
        {
            RdProtoGeometry.Batch b = batches[i];
            _indexArrays[i] = rd.IndexArrayCreate(_indexBuffer, b.IndexOffset, b.IndexCount);
            _batchSets[i] = TextureSet(rd, b.Diffuse, b.Lightmap);
            _batchPipelines[i] = PipelineIndex(b.Blend, b.TwoSided);
        }

        // The pipelines need the framebuffer format: made with the first target, all twelve up front (creating one
        // inside a draw list would also work, but would stall the first frame that needs it).
        if (ownTargets) EnsureTargets(rd, size);
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"SVM_RD_PROTO ready: {_geometry.Vertices.Length} vertices, {_geometry.Indices.Length / 3} triangles, {batches.Count} batches, {_sets.Count} texture sets, upload {clock.Elapsed.TotalMilliseconds:F0} ms"));
    }

    private void Fail(string message)
    {
        _failed = true;
        GD.PushError("SVM_RD_PROTO " + message);
    }

    private static Rid CompileShader(RenderingDevice rd, bool masked)
    {
        var source = new RDShaderSource
        {
            Language = RD.ShaderLanguage.Glsl,
            SourceVertex = RdProtoShaders.Vertex,
            SourceFragment = RdProtoShaders.Fragment(masked),
        };
        RDShaderSpirV spirv = rd.ShaderCompileSpirVFromSource(source, false);
        if (!string.IsNullOrEmpty(spirv.CompileErrorVertex) || !string.IsNullOrEmpty(spirv.CompileErrorFragment))
        {
            GD.PushError("SVM_RD_PROTO shader: " + spirv.CompileErrorVertex + spirv.CompileErrorFragment);
            return default;
        }

        return rd.ShaderCreateFromSpirV(spirv, masked ? "svm_rd_proto_masked" : "svm_rd_proto_solid");
    }

    private static Rid Pixel(RenderingDevice rd, uint abgr)
    {
        var format = new RDTextureFormat
        {
            Format = RD.DataFormat.R8G8B8A8Unorm, Width = 1, Height = 1,
            UsageBits = RD.TextureUsageBits.SamplingBit | RD.TextureUsageBits.CanUpdateBit,
        };
        return rd.TextureCreate(format, new RDTextureView(), [BitConverter.GetBytes(abgr)]);
    }

    /// <summary>The set-1 uniform set of a texture pair; Godot textures are shared through their RD textures.</summary>
    private Rid TextureSet(RenderingDevice rd, Rid diffuse, Rid lightmap)
    {
        if (_sets.TryGetValue((diffuse, lightmap), out Rid set)) return set;
        // texture_get_rd_texture is a RenderingServer call: on the render thread it runs directly, from the main
        // thread under the separate thread model it would sync with the render thread.
        Rid d = diffuse.IsValid ? RenderingServer.TextureGetRdTexture(diffuse, false) : default;
        Rid l = lightmap.IsValid ? RenderingServer.TextureGetRdTexture(lightmap, false) : default;
        var u0 = new RDUniform { UniformType = RD.UniformType.SamplerWithTexture, Binding = 0 };
        u0.AddId(_samplerDiffuse);
        u0.AddId(d.IsValid ? d : _white);
        var u1 = new RDUniform { UniformType = RD.UniformType.SamplerWithTexture, Binding = 1 };
        u1.AddId(_samplerLightmap);
        u1.AddId(l.IsValid ? l : _black);
        set = rd.UniformSetCreate([u0, u1], _shaderSolid, 1);
        _sets[(diffuse, lightmap)] = set;
        return set;
    }

    private static int PipelineIndex(MaterialBlend blend, bool twoSided) => ((int)blend * 2) + (twoSided ? 1 : 0);

    private void CreatePipelines(RenderingDevice rd, Rid[] pipelines, long framebufferFormat, RD.TextureSamples samples)
    {
        foreach (MaterialBlend blend in Enum.GetValues<MaterialBlend>())
        {
            bool solid = blend is MaterialBlend.Opaque or MaterialBlend.Masked;
            var attachment = new RDPipelineColorBlendStateAttachment
            {
                // Alpha is never written: the target keeps the clear's 1, so the canvas draws the picture opaque.
                WriteR = true, WriteG = true, WriteB = true, WriteA = false,
                EnableBlend = !solid,
                ColorBlendOp = RD.BlendOperation.Add,
                AlphaBlendOp = RD.BlendOperation.Add,
                SrcAlphaBlendFactor = RD.BlendFactor.Zero,
                DstAlphaBlendFactor = RD.BlendFactor.One,
            };
            (attachment.SrcColorBlendFactor, attachment.DstColorBlendFactor) = blend switch
            {
                MaterialBlend.Translucent => (RD.BlendFactor.SrcAlpha, RD.BlendFactor.OneMinusSrcAlpha),
                MaterialBlend.Additive => (RD.BlendFactor.One, RD.BlendFactor.One),
                MaterialBlend.Modulate => (RD.BlendFactor.DstColor, RD.BlendFactor.SrcColor),
                MaterialBlend.Darken => (RD.BlendFactor.Zero, RD.BlendFactor.OneMinusSrcColor),
                _ => (RD.BlendFactor.One, RD.BlendFactor.Zero),
            };
            // Assigned whole: the Attachments getter hands out a copy.
            var colorBlend = new RDPipelineColorBlendState { Attachments = [attachment] };
            var depth = new RDPipelineDepthStencilState
            {
                EnableDepthTest = true,
                EnableDepthWrite = solid,
                DepthCompareOperator = RD.CompareOperator.GreaterOrEqual,
            };
            for (int twoSided = 0; twoSided < 2; twoSided++)
            {
                var raster = new RDPipelineRasterizationState
                {
                    CullMode = twoSided == 1 ? RD.PolygonCullMode.Disabled : _cull,
                    // Godot meshes are clockwise from the front as seen on screen; the y flip in the projection keeps
                    // that in Vulkan's framebuffer space.
                    FrontFace = RD.PolygonFrontFace.Clockwise,
                };
                pipelines[PipelineIndex(blend, twoSided == 1)] = rd.RenderPipelineCreate(
                    blend == MaterialBlend.Masked ? _shaderMasked : _shaderSolid, framebufferFormat, _vertexFormat,
                    RD.RenderPrimitive.Triangles, raster, new RDPipelineMultisampleState { SampleCount = samples }, depth, colorBlend, 0, 0, []);
            }
        }
    }

    /// <summary>(Re)creates the RGBA8 colour and D32 depth targets at <paramref name="size"/>.</summary>
    private void EnsureTargets(RenderingDevice rd, Vector2I size)
    {
        size = new Vector2I(Math.Max(1, size.X), Math.Max(1, size.Y));
        if (_framebuffer.IsValid && size == _size) return;
        if (_color.IsValid) _retired.Add((_frame, _color, _depth));
        _size = size;
        _color = rd.TextureCreate(new RDTextureFormat
        {
            Format = RD.DataFormat.R8G8B8A8Unorm, Width = (uint)size.X, Height = (uint)size.Y,
            UsageBits = RD.TextureUsageBits.ColorAttachmentBit | RD.TextureUsageBits.SamplingBit | RD.TextureUsageBits.CanCopyFromBit,
        }, new RDTextureView(), []);
        _depth = rd.TextureCreate(new RDTextureFormat
        {
            Format = RD.DataFormat.D32Sfloat, Width = (uint)size.X, Height = (uint)size.Y,
            UsageBits = RD.TextureUsageBits.DepthStencilAttachmentBit,
            IsDiscardable = true,
        }, new RDTextureView(), []);
        _framebuffer = rd.FramebufferCreate([_color, _depth], -1, 1);
        if (_framebufferFormat < 0)
        {
            _framebufferFormat = rd.FramebufferGetFormat(_framebuffer);
            CreatePipelines(rd, _pipelines, _framebufferFormat, RD.TextureSamples.Samples1);
        }

        lock (_queueLock) _latestColor = _color;
    }

    private void FreeRetired(RenderingDevice rd, bool force)
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            // The main thread swaps the Texture2DRD the next frame; four frames covers the frames in flight.
            if (!force && _frame - _retired[i].Frame < 4) continue;
            // Freeing a texture frees the framebuffers made from it.
            rd.FreeRid(_retired[i].Color);
            rd.FreeRid(_retired[i].Depth);
            _retired.RemoveAt(i);
        }
    }

    /// <summary>Frees everything (render thread, after the main thread let go of the Texture2DRD).</summary>
    public void Free()
    {
        if (_rd is not { } rd) return;
        // Uniform sets first: they are freed with any texture or buffer they use, and freeing one twice is an error
        // (a Godot texture the map freed already took its sets with it).
        foreach (Rid set in _sets.Values)
        {
            if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
        }

        if (rd.UniformSetIsValid(_frameSet)) rd.FreeRid(_frameSet);
        foreach (Rid pipeline in _pipelines)
        {
            if (pipeline.IsValid) rd.FreeRid(pipeline);
        }

        foreach (Rid[] pipelines in _scenePipelines.Values)
        {
            foreach (Rid pipeline in pipelines)
            {
                if (pipeline.IsValid) rd.FreeRid(pipeline);
            }
        }

        foreach (Rid framebuffer in _sceneFramebuffers.Values)
        {
            if (rd.FramebufferIsValid(framebuffer)) rd.FreeRid(framebuffer);
        }

        _scenePipelines.Clear();
        _sceneFramebuffers.Clear();

        foreach (Rid array in _indexArrays)
        {
            if (array.IsValid) rd.FreeRid(array);
        }
        FreeRetired(rd, force: true);
        // The vertex array goes with its buffer; the framebuffer with its textures.
        foreach (Rid rid in new[] { _vertexBuffer, _indexBuffer, _ubo, _color, _depth, _white, _black, _samplerDiffuse, _samplerLightmap, _shaderSolid, _shaderMasked })
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        _sets.Clear();
        _rd = null;
    }
}
