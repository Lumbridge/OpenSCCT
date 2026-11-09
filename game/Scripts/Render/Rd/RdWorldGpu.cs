using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using RD = Godot.RenderingDevice;

namespace OpenScctGame.Render.Rd;

/// <summary>One frame's draws for the render thread, filled on the main thread (<see cref="RdWorldRenderer"/>).</summary>
internal sealed class RdWorldFrame
{
    /// <summary>Batch indices in draw order, each with the features its shader leaves out and the spot slots that reach it.</summary>
    public int[] Draws = new int[256];
    public byte[] Features = new byte[256];
    public int[] SpotBits = new int[256];
    /// <summary>The rows of the dynamic light texture (below 32) that reach each draw.</summary>
    public int[] DynBits = new int[256];
    public int DrawCount;
    public Color Clear;
    /// <summary>The main camera's cull mask, as the template reads CAMERA_VISIBLE_LAYERS (fog slot, shadow pass).</summary>
    public uint Layers;

    public void Reserve(int draws)
    {
        if (Draws.Length >= draws) return;
        Draws = new int[draws];
        Features = new byte[draws];
        SpotBits = new int[draws];
        DynBits = new int[draws];
    }
}

/// <summary>
/// The world pass's render-thread half: every RenderingDevice object it owns and the draw into Godot's own colour and
/// depth buffers (from the main camera's compositor, between Godot's depth pre-pass and its opaque pass). A plain class:
/// callables queued on the render thread stay valid after the node that queued them is freed. The global shader
/// parameters are read here, at draw time, after every pre-draw handler has set this frame's values (ShaderGlobals);
/// that relies on Godot's default thread model, where the render thread is the main thread.
/// </summary>
internal sealed class RdWorldGpu
{
    private const string Stamp = "RD World Pass";
    private const string EndStamp = "RD World Pass End";

    private sealed class ShaderGpu
    {
        public required RdTranslatedShader Source;
        public Rid Shader;
        public Rid GlobalSet;
        public int GlobalVersion = -1;
        public readonly Dictionary<long, Rid> Pipelines = [];
    }

    /// <summary>A material with one shader: the uniform set made for that shader's layout.</summary>
    private sealed class Variant
    {
        public required ShaderGpu Shader;
        public Rid Set;
    }

    private sealed class MaterialGpu
    {
        public required RdMaterial Source;
        public Rid Values;
        public readonly Variant?[] Variants = new Variant?[(int)RdFeatures.All + 1];
    }

    private readonly RdWorldGeometry _geometry;
    private readonly int _anisotropy;
    private readonly float _mipBias;
    private readonly Lock _queueLock = new();
    private readonly Queue<RdWorldFrame> _pending = new();
    private readonly Stack<RdWorldFrame> _free = new();
    private readonly Color[] _clear = new Color[1];
    private readonly byte[] _frameBlock = new byte[GdShaderTranslator.FrameBlockSize];
    private readonly byte[] _drawBlock = new byte[GdShaderTranslator.DrawBlockSize];
    private byte[] _globalBlock = [];

    private RenderingDevice? _rd;
    private bool _ready, _warmed;
    private volatile bool _failed;
    private Rid _vertexBuffer, _vertexArray, _indexBuffer, _frameBuffer, _globalBuffer;
    private Rid _white, _black, _whiteArray, _white3D;
    private long _vertexFormat;
    private Rid[] _indexArrays = [];
    private readonly Dictionary<(string Code, RdFeatures Features), ShaderGpu> _shaders = [];
    private readonly List<MaterialGpu> _materials = [];
    private readonly Dictionary<(RdFilter, bool), Rid> _samplers = [];
    private readonly Dictionary<(Rid Color, Rid Depth), Rid> _framebuffers = [];
    private readonly Dictionary<long, (Rid OneSided, Rid TwoSided)> _depthPipelines = [];
    private Rid _depthShader, _depthSet;
    private RdWorldFrame? _current;

    // What each draw of the frame binds, resolved before the draw list opens (nothing is made inside one).
    private Variant[] _drawVariants = new Variant[256];
    private Rid[] _drawPipelines = new Rid[256];

    // The global parameters' textures as RenderingDevice textures, by binding place; the version changes with them.
    private ShaderGlobals.Slot[] _slots = [];
    private Rid[] _globalTextures = [];
    private int _globalVersion;

    /// <summary>SVM_RD_WORLD_CACHE=0 compiles every shader from source (a measurement switch).</summary>
    private static bool UseCache { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_CACHE") != "0";

    /// <summary>SVM_RD_WORLD_PREPASS=0 draws without the pass's own depth pre-pass (a measurement switch).</summary>
    private static bool DepthPrepass { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_PREPASS") != "0";

    public RdWorldGpu(RdWorldGeometry geometry, int anisotropyLevel, float mipBias)
    {
        _geometry = geometry;
        _anisotropy = anisotropyLevel;
        _mipBias = mipBias;
    }

    public bool Failed => _failed;

    /// <summary>Draw calls and render-thread time of the last frame drawn, and the shaders made so far (statistics).</summary>
    public int LastDraws { get; private set; }
    public double LastRecordMs { get; private set; }
    public int ShaderCount => _shaders.Count;
    /// <summary>Objects made since the pass began: framebuffers, pipelines, global uniform sets (a steady frame makes none).</summary>
    public (int Framebuffers, int Pipelines, int GlobalSets) Made { get; private set; }

    /// <summary>A frame object to fill (main thread).</summary>
    public RdWorldFrame Rent()
    {
        lock (_queueLock) return _free.Count > 0 ? _free.Pop() : new RdWorldFrame();
    }

    /// <summary>Queues a filled frame; the next <see cref="Render"/> draws the newest one (main thread).</summary>
    public void Submit(RdWorldFrame frame)
    {
        lock (_queueLock) _pending.Enqueue(frame);
    }

    /// <summary>Drops what was submitted: a frame the pass sits out draws nothing here.</summary>
    public void Withdraw()
    {
        lock (_queueLock)
        {
            while (_pending.TryDequeue(out RdWorldFrame? frame)) _free.Push(frame);
            if (_current is not null) _free.Push(_current);
            _current = null;
        }
    }

    /// <summary>Draws the newest submitted frame into the scene's buffers (render thread, compositor callback).</summary>
    public void Render(RenderSceneBuffersRD buffers, RenderSceneData scene)
    {
        lock (_queueLock)
        {
            while (_pending.TryDequeue(out RdWorldFrame? next))
            {
                if (_current is not null) _free.Push(_current);
                _current = next;
            }
        }

        if (_current is null || _failed) return;
        try
        {
            Draw(_current, buffers, scene);
        }
        catch (Exception error)
        {
            _failed = true;
            GD.PushError("RD world pass failed: " + error);
        }
    }

    private void Draw(RdWorldFrame frame, RenderSceneBuffersRD buffers, RenderSceneData scene)
    {
        if (!_ready)
        {
            Init();
            if (_failed) return;
        }

        RenderingDevice rd = _rd!;
        bool msaa = buffers.GetMsaa3D() != RenderingServer.ViewportMsaa.Disabled;
        Rid color = buffers.GetColorLayer(0, msaa);
        Rid depth = buffers.GetDepthLayer(0, msaa);
        if (!color.IsValid || !depth.IsValid) return;
        if (!_framebuffers.TryGetValue((color, depth), out Rid framebuffer) || !rd.FramebufferIsValid(framebuffer))
        {
            // A resize frees Godot's textures and, with them, the framebuffers made here.
            foreach ((Rid, Rid) key in new List<(Rid, Rid)>(_framebuffers.Keys))
            {
                if (!rd.FramebufferIsValid(_framebuffers[key])) _framebuffers.Remove(key);
            }

            framebuffer = rd.FramebufferCreate([color, depth], -1, 1);
            Made = (Made.Framebuffers + 1, Made.Pipelines, Made.GlobalSets);
            _framebuffers[(color, depth)] = framebuffer;
        }

        long format = rd.FramebufferGetFormat(framebuffer);
        var clock = Stopwatch.StartNew();
        UpdateGlobals(rd);

        // Godot's corrected projection (y flipped, reverse Z) and its view: the same clip space as its own shaders.
        Transform3D eye = scene.GetCamTransform();
        Vector2I size = buffers.GetInternalSize();
        Span<float> f = MemoryMarshal.Cast<byte, float>(_frameBlock.AsSpan());
        Write(new Projection(eye.AffineInverse()), f[..16]);
        Write(new Projection(eye), f[16..32]);
        Projection projection = scene.GetCamProjection();
        Write(projection, f[32..48]);
        Write(projection * new Projection(eye.AffineInverse()), f[48..64]);
        (f[64], f[65], f[66], f[67]) = (size.X, size.Y, 0f, 0f);
        (f[68], f[69], f[70], f[71]) = (eye.Origin.X, eye.Origin.Y, eye.Origin.Z, 0f);
        MemoryMarshal.Cast<byte, uint>(_frameBlock.AsSpan(288))[0] = frame.Layers;
        rd.BufferUpdate(_frameBuffer, 0, (uint)_frameBlock.Length, _frameBlock);

        if (!_warmed)
        {
            _warmed = true;
            if (!Warm(rd, format, color, depth)) return;
        }

        if (!Resolve(rd, frame, format, color, depth)) return;
        (Rid OneSided, Rid TwoSided) depthPipelines = DepthPrepass ? DepthPipelinesOf(rd, format, color) : default;
        rd.CaptureTimestamp(Stamp);
        _clear[0] = frame.Clear;
        long dl = rd.DrawListBegin(framebuffer, RD.DrawFlags.ClearColor0, _clear, 0f, 0, null, 0);
        try
        {
            Record(rd, dl, frame, depthPipelines);
        }
        finally
        {
            // An open draw list would stop Godot's own passes.
            rd.DrawListEnd();
        }

        rd.CaptureTimestamp(EndStamp);
        LastDraws = frame.DrawCount;
        LastRecordMs = clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>The shader variant, uniform sets and pipeline of every draw of the frame, made where they are new.</summary>
    private bool Resolve(RenderingDevice rd, RdWorldFrame frame, long format, Rid color, Rid depth)
    {
        if (_drawVariants.Length < frame.DrawCount)
        {
            _drawVariants = new Variant[frame.DrawCount];
            _drawPipelines = new Rid[frame.DrawCount];
        }

        List<RdWorldGeometry.Batch> batches = _geometry.Batches;
        for (int k = 0; k < frame.DrawCount; k++)
        {
            MaterialGpu material = _materials[batches[frame.Draws[k]].Material.GpuIndex];
            RdFeatures features = (RdFeatures)frame.Features[k] & material.Source.Applicable;
            Variant? variant = material.Variants[(int)features] ?? VariantOf(rd, material, features);
            if (variant is null) return false;
            ShaderGpu shader = variant.Shader;
            if (shader.GlobalVersion != _globalVersion || !rd.UniformSetIsValid(shader.GlobalSet)) MakeGlobalSet(rd, shader);
            if (!shader.Pipelines.TryGetValue(format, out Rid pipeline)) pipeline = PipelineOf(rd, shader, format, color, depth);
            _drawVariants[k] = variant;
            _drawPipelines[k] = pipeline;
        }

        return true;
    }

    /// <summary>
    /// Makes, in the first frame (behind the loading cover), every shader variant and pipeline a later frame can ask
    /// for as lights switch and the camera moves: each light state with and without portal fog. Play then never waits
    /// for a compile. The first load of a map on a machine compiles them (seconds); later loads read the SPIR-V cache
    /// and the driver's pipeline cache.
    /// </summary>
    private bool Warm(RenderingDevice rd, long format, Rid color, Rid depth)
    {
        var clock = Stopwatch.StartNew();
        ReadOnlySpan<RdFeatures> lights = [RdFeatures.None, RdFeatures.NoSpot, RdFeatures.NoSpot | RdFeatures.NoDynamic];
        ReadOnlySpan<RdFeatures> fogs = [RdFeatures.None, RdFeatures.NoPortalFog];
        foreach (MaterialGpu material in _materials)
        {
            foreach (RdFeatures light in lights)
            {
                foreach (RdFeatures fog in fogs)
                {
                    RdFeatures features = (light | fog) & material.Source.Applicable;
                    Variant? variant = material.Variants[(int)features] ?? VariantOf(rd, material, features);
                    if (variant is null) return false;
                    if (!variant.Shader.Pipelines.ContainsKey(format)) PipelineOf(rd, variant.Shader, format, color, depth);
                }
            }
        }

        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"SVM_RD_WORLD warm: {_shaders.Count} shaders ({_compiled} compiled, {_cached} from the cache) in {clock.Elapsed.TotalMilliseconds:F0} ms"));
        return true;
    }

    private void Record(RenderingDevice rd, long dl, RdWorldFrame frame, (Rid OneSided, Rid TwoSided) depthPipelines)
    {
        rd.DrawListBindVertexArray(dl, _vertexArray);
        List<RdWorldGeometry.Batch> batches = _geometry.Batches;
        // Depth first, for the solid world (masked surfaces need their texture, the sky zone lies behind it all): each
        // pixel then runs the template once, for the nearest surface.
        if (DepthPrepass)
        {
            Rid bound = default;
            for (int k = 0; k < frame.DrawCount; k++)
            {
                int i = frame.Draws[k];
                RdWorldGeometry.Batch b = batches[i];
                if (b.Sky || b.Material.Masked) continue;
                Rid wanted = b.Material.Shader.TwoSided ? depthPipelines.TwoSided : depthPipelines.OneSided;
                if (wanted != bound)
                {
                    bound = wanted;
                    rd.DrawListBindRenderPipeline(dl, bound);
                    rd.DrawListBindUniformSet(dl, _depthSet, 0);
                }

                rd.DrawListBindIndexArray(dl, _indexArrays[i]);
                rd.DrawListDraw(dl, true, 1, 0);
            }
        }

        ShaderGpu? shader = null;
        Variant? variant = null;
        Rid pipeline = default;
        int spotBits = 0, dynBits = 0;
        bool pushed = false;
        for (int k = 0; k < frame.DrawCount; k++)
        {
            Variant next = _drawVariants[k];
            if (!ReferenceEquals(next, variant))
            {
                variant = next;
                if (!ReferenceEquals(variant.Shader, shader))
                {
                    shader = variant.Shader;
                    if (_drawPipelines[k] != pipeline)
                    {
                        pipeline = _drawPipelines[k];
                        rd.DrawListBindRenderPipeline(dl, pipeline);
                        pushed = false;
                    }

                    rd.DrawListBindUniformSet(dl, shader.GlobalSet, 0);
                }

                rd.DrawListBindUniformSet(dl, variant.Set, 1);
            }

            if (!pushed || spotBits != frame.SpotBits[k] || dynBits != frame.DynBits[k])
            {
                spotBits = frame.SpotBits[k];
                dynBits = frame.DynBits[k];
                Span<int> draw = MemoryMarshal.Cast<byte, int>(_drawBlock.AsSpan());
                draw[0] = spotBits;
                draw[1] = dynBits;
                rd.DrawListSetPushConstant(dl, _drawBlock, (uint)_drawBlock.Length);
                pushed = true;
            }

            rd.DrawListBindIndexArray(dl, _indexArrays[frame.Draws[k]]);
            rd.DrawListDraw(dl, true, 1, 0);
        }
    }

    private void Init()
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
        const uint stride = 40;
        _vertexFormat = rd.VertexFormatCreate(
        [
            new RDVertexAttribute { Location = 0, Offset = 0, Format = RD.DataFormat.R32G32B32Sfloat, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 1, Offset = 12, Format = RD.DataFormat.R16G16Unorm, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 2, Offset = 16, Format = RD.DataFormat.R32G32Sfloat, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 3, Offset = 24, Format = RD.DataFormat.R32G32Sfloat, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 4, Offset = 32, Format = RD.DataFormat.R8G8B8A8Unorm, Stride = stride, Binding = 0 },
            new RDVertexAttribute { Location = 5, Offset = 36, Format = RD.DataFormat.R32Sfloat, Stride = stride, Binding = 0 },
        ]);
        ReadOnlySpan<byte> vertices = MemoryMarshal.AsBytes(_geometry.Vertices.AsSpan());
        ReadOnlySpan<byte> indices = MemoryMarshal.AsBytes(_geometry.Indices.AsSpan());
        if (vertices.Length == 0 || indices.Length == 0)
        {
            Fail("the map gave no geometry the pass can draw");
            return;
        }

        _vertexBuffer = rd.VertexBufferCreate((uint)vertices.Length, vertices, 0);
        _vertexArray = rd.VertexArrayCreate((uint)_geometry.Vertices.Length, _vertexFormat, [_vertexBuffer], ReadOnlySpan<long>.Empty);
        _indexBuffer = rd.IndexBufferCreate((uint)_geometry.Indices.Length, RD.IndexBufferFormat.Uint32, indices, false, 0);
        _frameBuffer = rd.UniformBufferCreate((uint)_frameBlock.Length, ReadOnlySpan<byte>.Empty, 0);
        // Room for parameters registered later: a shader compiled now names only those it knows.
        _globalBlock = new byte[Math.Max(1024, (ShaderGlobals.BlockSize + 15) / 16 * 16 * 2)];
        _globalBuffer = rd.UniformBufferCreate((uint)_globalBlock.Length, ReadOnlySpan<byte>.Empty, 0);
        _white = Pixel(rd, RD.TextureType.Type2D, 0xffffffffu);
        _black = Pixel(rd, RD.TextureType.Type2D, 0xff000000u);
        _whiteArray = Pixel(rd, RD.TextureType.Type2DArray, 0xffffffffu);
        _white3D = Pixel(rd, RD.TextureType.Type3D, 0xffffffffu);
        UpdateGlobals(rd);
        _depthShader = Compile(rd, GdShaderTranslator.DepthVertex, GdShaderTranslator.DepthFragment, "svm_rd_world_depth", null);
        if (!_depthShader.IsValid) return;
        var depthFrame = new RDUniform { UniformType = RD.UniformType.UniformBuffer, Binding = 0 };
        depthFrame.AddId(_frameBuffer);
        _depthSet = rd.UniformSetCreate([depthFrame], _depthShader, 0);

        List<RdWorldGeometry.Batch> batches = _geometry.Batches;
        _indexArrays = new Rid[batches.Count];
        for (int i = 0; i < batches.Count; i++)
        {
            RdWorldGeometry.Batch b = batches[i];
            _indexArrays[i] = rd.IndexArrayCreate(_indexBuffer, b.IndexOffset, b.IndexCount);
            RdMaterial material = b.Material;
            if (material.GpuIndex >= 0) continue;
            var gpu = new MaterialGpu { Source = material };
            if (material.Values.Length > 0) gpu.Values = rd.UniformBufferCreate((uint)material.Values.Length, material.Values, 0);
            material.GpuIndex = _materials.Count;
            _materials.Add(gpu);
        }

        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"SVM_RD_WORLD ready: {_geometry.Vertices.Length} vertices, {_geometry.Indices.Length / 3} triangles, {batches.Count} batches, {_materials.Count} materials, {clock.Elapsed.TotalMilliseconds:F0} ms"));
    }

    private void Fail(string message)
    {
        _failed = true;
        GD.PushError("RD world pass: " + message);
    }

    // Compiled SPIR-V kept in the user folder by source hash: a map's shaders compile once per machine, not per load.
    private static readonly string s_cacheFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "rd_shaders");
    private int _compiled, _cached;

    private Rid Compile(RenderingDevice rd, string vertex, string fragment, string name, RdTranslatedShader? dump)
    {
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Engine.GetVersionInfo()["string"].AsString() + "\n" + vertex + "\n\u0001\n" + fragment)))[..40];
        string vertexFile = System.IO.Path.Combine(s_cacheFolder, key + ".vert.spv"), fragmentFile = System.IO.Path.Combine(s_cacheFolder, key + ".frag.spv");
        RDShaderSpirV? spirv = null;
        try
        {
            if (UseCache && System.IO.File.Exists(vertexFile) && System.IO.File.Exists(fragmentFile))
            {
                spirv = new RDShaderSpirV { BytecodeVertex = System.IO.File.ReadAllBytes(vertexFile), BytecodeFragment = System.IO.File.ReadAllBytes(fragmentFile) };
                _cached++;
            }
        }
        catch (System.IO.IOException)
        {
            spirv = null;
        }

        if (spirv is null)
        {
            spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RD.ShaderLanguage.Glsl, SourceVertex = vertex, SourceFragment = fragment }, false);
            if (!string.IsNullOrEmpty(spirv.CompileErrorVertex) || !string.IsNullOrEmpty(spirv.CompileErrorFragment))
            {
                if (dump is not null) Dump(dump);
                Fail(name + " compile: " + spirv.CompileErrorVertex + spirv.CompileErrorFragment);
                return default;
            }

            _compiled++;
            try
            {
                if (UseCache)
                {
                    System.IO.Directory.CreateDirectory(s_cacheFolder);
                    System.IO.File.WriteAllBytes(vertexFile, spirv.BytecodeVertex);
                    System.IO.File.WriteAllBytes(fragmentFile, spirv.BytecodeFragment);
                }
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            {
                // The cache is an optimisation: without it the shader compiles again next time.
            }
        }

        Rid shader = rd.ShaderCreateFromSpirV(spirv, name);
        if (!shader.IsValid)
        {
            if (dump is not null) Dump(dump);
            Fail(name + " create failed");
        }

        return shader;
    }

    /// <summary>A material with some features left out: the shader (shared by code) and the material's set for it.</summary>
    private Variant? VariantOf(RenderingDevice rd, MaterialGpu material, RdFeatures features)
    {
        RdMaterial source = material.Source;
        if (!_shaders.TryGetValue((source.Code, features), out ShaderGpu? shader))
        {
            RdTranslatedShader? translated = RdShaderLibrary.Get(source.Code, features, out string? reason);
            if (translated is null)
            {
                Fail("shader translation: " + reason);
                return null;
            }

            // The material's values and textures were packed for the full shader: the features only leave code out.
            if (translated.ValueBlockSize != source.Shader.ValueBlockSize || translated.Textures.Count != source.Shader.Textures.Count)
            {
                Fail("a feature changed a material's layout");
                return null;
            }

            Rid rid = Compile(rd, translated.Vertex, translated.Fragment, "svm_rd_world", translated);
            if (!rid.IsValid) return null;
            shader = new ShaderGpu { Source = translated, Shader = rid };
            _shaders[(source.Code, features)] = shader;
        }

        var uniforms = new Godot.Collections.Array<RDUniform>();
        if (material.Values.IsValid)
        {
            var block = new RDUniform { UniformType = RD.UniformType.UniformBuffer, Binding = 0 };
            block.AddId(material.Values);
            uniforms.Add(block);
        }

        for (int i = 0; i < source.Shader.Textures.Count; i++)
        {
            RdMaterialTexture slot = source.Shader.Textures[i];
            Rid texture = source.Textures[i] is { } t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default;
            var uniform = new RDUniform { UniformType = RD.UniformType.SamplerWithTexture, Binding = slot.Binding };
            uniform.AddId(SamplerOf(rd, slot.Filter, slot.Repeat));
            uniform.AddId(texture.IsValid ? texture : slot.DefaultBlack ? _black : _white);
            uniforms.Add(uniform);
        }

        var variant = new Variant { Shader = shader, Set = rd.UniformSetCreate(uniforms, shader.Shader, 1) };
        if (!variant.Set.IsValid)
        {
            Dump(shader.Source);
            Fail("material uniform set failed");
            return null;
        }

        material.Variants[(int)features] = variant;
        return variant;
    }

    /// <summary>SVM_RD_DUMP=folder: writes a shader that failed there, to read the compiler's line numbers against.</summary>
    private static void Dump(RdTranslatedShader shader)
    {
        if (System.Environment.GetEnvironmentVariable("SVM_RD_DUMP") is not { Length: > 0 } folder) return;
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "failed.vert"), shader.Vertex);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "failed.frag"), shader.Fragment);
    }

    /// <summary>The sampler of a filter and repeat mode, as Godot makes its material samplers (MaterialStorage).</summary>
    private Rid SamplerOf(RenderingDevice rd, RdFilter filter, bool repeat)
    {
        if (_samplers.TryGetValue((filter, repeat), out Rid sampler)) return sampler;
        bool nearest = filter is RdFilter.Nearest or RdFilter.NearestMipmap;
        bool mipmaps = filter is not (RdFilter.Nearest or RdFilter.Linear);
        var state = new RDSamplerState
        {
            MagFilter = nearest ? RD.SamplerFilter.Nearest : RD.SamplerFilter.Linear,
            MinFilter = nearest ? RD.SamplerFilter.Nearest : RD.SamplerFilter.Linear,
            RepeatU = repeat ? RD.SamplerRepeatMode.Repeat : RD.SamplerRepeatMode.ClampToEdge,
            RepeatV = repeat ? RD.SamplerRepeatMode.Repeat : RD.SamplerRepeatMode.ClampToEdge,
            RepeatW = repeat ? RD.SamplerRepeatMode.Repeat : RD.SamplerRepeatMode.ClampToEdge,
        };
        if (mipmaps)
        {
            state.MipFilter = RD.SamplerFilter.Linear;
            state.LodBias = _mipBias;
        }
        else
        {
            state.MaxLod = 0f;
        }

        if (filter == RdFilter.LinearMipmapAnisotropic)
        {
            state.UseAnisotropy = true;
            state.AnisotropyMax = 1 << _anisotropy;
        }

        sampler = rd.SamplerCreate(state);
        _samplers[(filter, repeat)] = sampler;
        return sampler;
    }

    private static Rid Pixel(RenderingDevice rd, RD.TextureType type, uint abgr)
    {
        var format = new RDTextureFormat
        {
            Format = RD.DataFormat.R8G8B8A8Unorm, Width = 1, Height = 1, Depth = 1, ArrayLayers = 1, TextureType = type,
            UsageBits = RD.TextureUsageBits.SamplingBit | RD.TextureUsageBits.CanUpdateBit,
        };
        return rd.TextureCreate(format, new RDTextureView(), [BitConverter.GetBytes(abgr)]);
    }

    /// <summary>This frame's global parameter values into their block, and their textures as RenderingDevice textures.</summary>
    private void UpdateGlobals(RenderingDevice rd)
    {
        ShaderGlobals.Slot[] slots = ShaderGlobals.Slots();
        int textures = 0;
        foreach (ShaderGlobals.Slot slot in slots)
        {
            if (slot.Size == 0) textures = Math.Max(textures, slot.Place + 1);
        }

        bool changed = slots.Length != _slots.Length;
        if (_globalTextures.Length != textures) Array.Resize(ref _globalTextures, textures);
        _slots = slots;
        Span<byte> block = _globalBlock;
        foreach (ShaderGlobals.Slot slot in slots)
        {
            ShaderGlobals.TryGet(slot.Name, out Godot.Variant value);
            if (slot.Size == 0)
            {
                Rid texture = value.VariantType switch
                {
                    Godot.Variant.Type.Object when value.AsGodotObject() is Texture t => RenderingServer.TextureGetRdTexture(t.GetRid(), false),
                    Godot.Variant.Type.Rid => RenderingServer.TextureGetRdTexture(value.AsRid(), false),
                    _ => default,
                };
                if (!texture.IsValid)
                {
                    texture = slot.Type switch
                    {
                        RenderingServer.GlobalShaderParameterType.Sampler2Darray => _whiteArray,
                        RenderingServer.GlobalShaderParameterType.Sampler3D => _white3D,
                        _ => _white,
                    };
                }

                if (_globalTextures[slot.Place] != texture)
                {
                    _globalTextures[slot.Place] = texture;
                    changed = true;
                }

                continue;
            }

            if (slot.Place + slot.Size > block.Length) continue;
            Span<byte> to = block.Slice(slot.Place, slot.Size);
            switch (slot.Type)
            {
                case RenderingServer.GlobalShaderParameterType.Bool:
                    MemoryMarshal.Cast<byte, int>(to)[0] = value.AsBool() ? 1 : 0;
                    break;
                case RenderingServer.GlobalShaderParameterType.Int:
                case RenderingServer.GlobalShaderParameterType.Uint:
                    MemoryMarshal.Cast<byte, int>(to)[0] = value.AsInt32();
                    break;
                case RenderingServer.GlobalShaderParameterType.Float:
                    MemoryMarshal.Cast<byte, float>(to)[0] = value.AsSingle();
                    break;
                case RenderingServer.GlobalShaderParameterType.Vec2:
                    MemoryMarshal.Cast<byte, Vector2>(to)[0] = value.AsVector2();
                    break;
                case RenderingServer.GlobalShaderParameterType.Vec3:
                    MemoryMarshal.Cast<byte, Vector3>(to)[0] = value.AsVector3();
                    break;
                case RenderingServer.GlobalShaderParameterType.Vec4:
                case RenderingServer.GlobalShaderParameterType.Color:
                    MemoryMarshal.Cast<byte, Vector4>(to)[0] = value.VariantType == Godot.Variant.Type.Color
                        ? new Vector4(value.AsColor().R, value.AsColor().G, value.AsColor().B, value.AsColor().A) : value.AsVector4();
                    break;
                case RenderingServer.GlobalShaderParameterType.Ivec2:
                    MemoryMarshal.Cast<byte, Vector2I>(to)[0] = value.AsVector2I();
                    break;
                case RenderingServer.GlobalShaderParameterType.Ivec3:
                    MemoryMarshal.Cast<byte, Vector3I>(to)[0] = value.AsVector3I();
                    break;
                case RenderingServer.GlobalShaderParameterType.Ivec4:
                    MemoryMarshal.Cast<byte, Vector4I>(to)[0] = value.AsVector4I();
                    break;
            }
        }

        if (changed) _globalVersion++;
        rd.BufferUpdate(_globalBuffer, 0, (uint)_globalBlock.Length, _globalBlock);
    }

    /// <summary>Set 0 of a shader: the frame block, the global values and every global texture (it takes those it names).</summary>
    private void MakeGlobalSet(RenderingDevice rd, ShaderGpu shader)
    {
        if (shader.GlobalSet.IsValid && rd.UniformSetIsValid(shader.GlobalSet)) rd.FreeRid(shader.GlobalSet);
        var uniforms = new Godot.Collections.Array<RDUniform>();
        var frame = new RDUniform { UniformType = RD.UniformType.UniformBuffer, Binding = 0 };
        frame.AddId(_frameBuffer);
        uniforms.Add(frame);
        var values = new RDUniform { UniformType = RD.UniformType.UniformBuffer, Binding = 1 };
        values.AddId(_globalBuffer);
        uniforms.Add(values);
        Rid sampler = SamplerOf(rd, RdFilter.LinearMipmap, repeat: true);
        for (int place = 0; place < _globalTextures.Length; place++)
        {
            var texture = new RDUniform { UniformType = RD.UniformType.SamplerWithTexture, Binding = GdShaderTranslator.GlobalTextureBinding + place };
            texture.AddId(sampler);
            texture.AddId(_globalTextures[place]);
            uniforms.Add(texture);
        }

        shader.GlobalSet = rd.UniformSetCreate(uniforms, shader.Shader, 0);
        Made = (Made.Framebuffers, Made.Pipelines, Made.GlobalSets + 1);
        shader.GlobalVersion = _globalVersion;
        if (!shader.GlobalSet.IsValid) throw new InvalidOperationException("global uniform set failed");
    }

    private Rid PipelineOf(RenderingDevice rd, ShaderGpu shader, long framebufferFormat, Rid color, Rid depth)
    {
        using RDTextureFormat colorFormat = rd.TextureGetFormat(color);
        using RDTextureFormat depthFormat = rd.TextureGetFormat(depth);
        RdTranslatedShader source = shader.Source;
        var raster = new RDPipelineRasterizationState
        {
            CullMode = source.TwoSided ? RD.PolygonCullMode.Disabled : RD.PolygonCullMode.Back,
            // Godot meshes are clockwise from the front; its projection's y flip keeps that in the framebuffer.
            FrontFace = RD.PolygonFrontFace.Clockwise,
        };
        var depthState = new RDPipelineDepthStencilState
        {
            EnableDepthTest = true, EnableDepthWrite = true, DepthCompareOperator = RD.CompareOperator.GreaterOrEqual,
        };
        bool stencil = depthFormat.Format is RD.DataFormat.D24UnormS8Uint or RD.DataFormat.D32SfloatS8Uint or RD.DataFormat.D16UnormS8Uint;
        if (source.StencilWrite is { } reference && stencil)
        {
            depthState.EnableStencil = true;
            depthState.FrontOpCompare = depthState.BackOpCompare = RD.CompareOperator.Always;
            depthState.FrontOpPass = depthState.BackOpPass = RD.StencilOperation.Replace;
            depthState.FrontOpFail = depthState.BackOpFail = RD.StencilOperation.Keep;
            depthState.FrontOpDepthFail = depthState.BackOpDepthFail = RD.StencilOperation.Keep;
            depthState.FrontOpCompareMask = depthState.BackOpCompareMask = 0xff;
            depthState.FrontOpWriteMask = depthState.BackOpWriteMask = 0xff;
            depthState.FrontOpReference = depthState.BackOpReference = (uint)reference;
        }

        var multisample = new RDPipelineMultisampleState
        {
            SampleCount = colorFormat.Samples,
            EnableAlphaToCoverage = colorFormat.Samples != RD.TextureSamples.Samples1 && source.RenderModes.Contains("alpha_to_coverage"),
        };
        var blend = new RDPipelineColorBlendState { Attachments = [new RDPipelineColorBlendStateAttachment()] };
        Rid pipeline = rd.RenderPipelineCreate(shader.Shader, framebufferFormat, _vertexFormat, RD.RenderPrimitive.Triangles,
            raster, multisample, depthState, blend, 0, 0, []);
        if (!pipeline.IsValid) throw new InvalidOperationException("render pipeline failed");
        Made = (Made.Framebuffers, Made.Pipelines + 1, Made.GlobalSets);
        shader.Pipelines[framebufferFormat] = pipeline;
        return pipeline;
    }

    private (Rid OneSided, Rid TwoSided) DepthPipelinesOf(RenderingDevice rd, long framebufferFormat, Rid color)
    {
        if (_depthPipelines.TryGetValue(framebufferFormat, out (Rid, Rid) pipelines)) return pipelines;
        using RDTextureFormat colorFormat = rd.TextureGetFormat(color);
        var depthState = new RDPipelineDepthStencilState
        {
            EnableDepthTest = true, EnableDepthWrite = true, DepthCompareOperator = RD.CompareOperator.GreaterOrEqual,
        };
        var blend = new RDPipelineColorBlendState
        {
            Attachments = [new RDPipelineColorBlendStateAttachment { WriteR = false, WriteG = false, WriteB = false, WriteA = false }],
        };
        var made = new Rid[2];
        for (int twoSided = 0; twoSided < 2; twoSided++)
        {
            var raster = new RDPipelineRasterizationState
            {
                CullMode = twoSided == 1 ? RD.PolygonCullMode.Disabled : RD.PolygonCullMode.Back, FrontFace = RD.PolygonFrontFace.Clockwise,
            };
            made[twoSided] = rd.RenderPipelineCreate(_depthShader, framebufferFormat, _vertexFormat, RD.RenderPrimitive.Triangles, raster,
                new RDPipelineMultisampleState { SampleCount = colorFormat.Samples }, depthState, blend, 0, 0, []);
            if (!made[twoSided].IsValid) throw new InvalidOperationException("depth pipeline failed");
        }

        pipelines = (made[0], made[1]);
        _depthPipelines[framebufferFormat] = pipelines;
        return pipelines;
    }

    /// <summary>Column-major, as GLSL reads a std140 mat4.</summary>
    private static void Write(in Projection m, Span<float> to)
    {
        ReadOnlySpan<Vector4> columns = [m.X, m.Y, m.Z, m.W];
        for (int c = 0; c < 4; c++)
        {
            to[(c * 4) + 0] = columns[c].X;
            to[(c * 4) + 1] = columns[c].Y;
            to[(c * 4) + 2] = columns[c].Z;
            to[(c * 4) + 3] = columns[c].W;
        }
    }

    /// <summary>Frees everything (render thread).</summary>
    public void Free()
    {
        if (_rd is not { } rd) return;
        // Uniform sets go with any texture or buffer they use, and freeing one twice is an error.
        foreach (MaterialGpu material in _materials)
        {
            foreach (Variant? variant in material.Variants)
            {
                if (variant is not null && rd.UniformSetIsValid(variant.Set)) rd.FreeRid(variant.Set);
            }

            if (material.Values.IsValid) rd.FreeRid(material.Values);
        }

        foreach (ShaderGpu shader in _shaders.Values)
        {
            if (rd.UniformSetIsValid(shader.GlobalSet)) rd.FreeRid(shader.GlobalSet);
            foreach (Rid pipeline in shader.Pipelines.Values)
            {
                if (pipeline.IsValid) rd.FreeRid(pipeline);
            }

            if (shader.Shader.IsValid) rd.FreeRid(shader.Shader);
        }

        foreach (Rid framebuffer in _framebuffers.Values)
        {
            if (rd.FramebufferIsValid(framebuffer)) rd.FreeRid(framebuffer);
        }

        foreach ((Rid oneSided, Rid twoSided) in _depthPipelines.Values)
        {
            rd.FreeRid(oneSided);
            rd.FreeRid(twoSided);
        }

        if (rd.UniformSetIsValid(_depthSet)) rd.FreeRid(_depthSet);
        if (_depthShader.IsValid) rd.FreeRid(_depthShader);
        foreach (Rid array in _indexArrays)
        {
            if (array.IsValid) rd.FreeRid(array);
        }

        foreach (Rid sampler in _samplers.Values) rd.FreeRid(sampler);
        foreach (Rid rid in new[] { _vertexBuffer, _indexBuffer, _frameBuffer, _globalBuffer, _white, _black, _whiteArray, _white3D })
        {
            if (rid.IsValid) rd.FreeRid(rid);
        }

        _materials.Clear();
        _shaders.Clear();
        _framebuffers.Clear();
        _depthPipelines.Clear();
        _samplers.Clear();
        _rd = null;
    }
}
