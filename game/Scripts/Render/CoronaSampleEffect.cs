using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render;

/// <summary>
/// What the coronas read of the scene (docs/RENDERING.md#coronas), without Godot's full-screen copies. A corona reads
/// the opaque colour under each of its pixels and the depth at its centre. Through hint_screen_texture and
/// hint_depth_texture that cost a copy of the whole colour buffer (and its mip chain) and of the whole depth buffer in
/// every frame a corona drew. Before the transparent pass, this effect copies the colour and depth under each corona
/// drawn this frame (its quad's screen rectangle and <see cref="Margin"/> more pixels) into two textures of the
/// frame's size. svm_corona_copies.gdshader samples them where svm_corona.gdshader samples the screen and depth
/// textures, with the same filter and repeat, so it reads the same texels with the same values. Texels outside the
/// rectangles keep older values that no corona fragment reads.
/// </summary>
/// <remarks>
/// The render thread makes the textures at the frame's internal size; before the next frame the main thread binds them
/// to <see cref="Scene"/> and <see cref="Depth"/>. Before each frame (RenderingServer.frame_pre_draw, after all of
/// the frame's main-thread changes) <see cref="UseCopies"/> learns whether the bound textures have the size this frame
/// renders at, which is the size the render thread last saw under the same viewport settings (its size, 3D scale and
/// scaling mode fix the internal size). Until they match (the first frames, a resize) the coronas keep
/// svm_corona.gdshader and its screen copies. Godot renders on the main thread here (thread model Safe), so the
/// coronas published in a frame are the ones that frame draws.
/// </remarks>
public sealed partial class CoronaSampleEffect : CompositorEffect
{
    // Pixels added around each corona's rectangle, for the rasterizer at the quad's edges.
    private const int Margin = 4;

    // Metres added to each quad's half size per metre of world coordinates (centre and camera): the float error of this
    // view-space centre against the GPU's is a few ulps of those coordinates (about 1e-7 each), far below this.
    private const double Slack = 2e-5;

    private const string Source = """
        #version 450
        // CoronaSampleEffect: copies the frame's colour and depth in one corona's rectangle into the corona's copies.
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(set = 0, binding = 0) uniform sampler2D colour_tex;
        layout(set = 0, binding = 1) uniform sampler2D depth_tex;
        layout(rgba16f, set = 0, binding = 2) uniform writeonly image2D colour_copy;
        layout(r32f, set = 0, binding = 3) uniform writeonly image2D depth_copy;
        layout(push_constant, std430) uniform Params {
            ivec4 rect; // xy first pixel (up to the margin past the frame's edges), zw size
            ivec4 size; // xy the frame's size
        } p;
        void main() {
            ivec2 g = ivec2(gl_GlobalInvocationID.xy);
            if (g.x >= p.rect.z || g.y >= p.rect.w) {
                return;
            }
            // A pixel past an edge stands for the one across the frame: the depth read at the light's centre uses a
            // repeating sampler, so a centre on the right or bottom edge can read column or row 0.
            ivec2 xy = p.rect.xy + g;
            xy = ivec2(xy.x < 0 ? xy.x + p.size.x : (xy.x >= p.size.x ? xy.x - p.size.x : xy.x),
                xy.y < 0 ? xy.y + p.size.y : (xy.y >= p.size.y ? xy.y - p.size.y : xy.y));
            imageStore(colour_copy, xy, texelFetch(colour_tex, xy, 0));
            imageStore(depth_copy, xy, vec4(texelFetch(depth_tex, xy, 0).r, 0.0, 0.0, 0.0));
        }
        """;

    /// <summary>A corona drawn this frame: its centre in the world (m) and its quad's half size (m).</summary>
    public readonly record struct Sample(Vector3 Centre, float HalfSize);

    // What fixes the frame's internal size: the viewport, a count of its size changes, its 3D scale and scaling mode.
    private readonly record struct ViewKey(ulong ViewportId, int SizeEpoch, float Scale, Viewport.Scaling3DModeEnum Mode);

    // A pair of copy textures and their size.
    private readonly record struct Targets(Rid Scene, Rid Depth, Vector2I Size)
    {
        public bool IsValid => Scene.IsValid && Depth.IsValid;
    }

    private readonly Camera3D _camera;
    private readonly System.Threading.Lock _gate = new();

    // Shared between the threads under _gate.
    private Sample[] _published = new Sample[16];
    private int _publishedCount;
    private ViewKey _frameKey;
    private Targets _current;
    private bool _seen;
    private ViewKey _seenKey;
    private Vector2I _seenSize;
    private readonly List<Targets> _replaced = [];

    // Main thread.
    private Viewport? _viewport;
    private int _sizeEpoch;
    private Targets _bound;
    private readonly List<Targets> _retired = [];
    private Action<bool>? _useCopies;
    private volatile bool _released;

    // Set by Preload and cleared by the render thread once it has tried to make the pipeline and copies: keeps the
    // effect on until then, so that work happens in a loading frame rather than at the first corona in play.
    private volatile bool _warmup;

    // Render thread.
    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _sampler;
    private bool _attempted;
    private Rid _checkedColour;
    private bool _colourSupported;
    private Sample[] _frame = new Sample[16];
    private UniformSlots? _slots;
    private readonly int[] _pushValues = new int[8];
    private readonly byte[] _push = new byte[32];

    /// <param name="camera">The camera whose compositor holds this effect (its viewport fixes the frame's size).</param>
    public CoronaSampleEffect(Camera3D camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        EffectCallbackType = EffectCallbackTypeEnum.PreTransparent;
        // With MSAA the renderer resolves colour and depth before this pass, as it did for the screen and depth textures.
        AccessResolvedColor = true;
        AccessResolvedDepth = true;
        Enabled = false;
        RenderingServer.FramePreDraw += OnPreDraw;
    }

    /// <summary>The colour copy, for svm_corona_copies.gdshader's scene_tex.</summary>
    public Texture2Drd Scene { get; } = new();

    /// <summary>The depth copy, for svm_corona_copies.gdshader's depth_tex.</summary>
    public Texture2Drd Depth { get; } = new();

    /// <summary>
    /// Told before each frame whether the coronas may draw with the copies (true) or must keep the screen-texture
    /// shader (false). A callback replaced by another is told false first: only the newest coronas are copied for.
    /// Main thread.
    /// </summary>
    public Action<bool>? UseCopies
    {
        get => _useCopies;
        set
        {
            if (_useCopies == value)
            {
                return;
            }

            _useCopies?.Invoke(false);
            _useCopies = value;
        }
    }

    /// <summary>
    /// Makes the copy pipeline and the copies in the next frames the view draws (while the map loads), not in the
    /// first frame a corona shows. Main thread.
    /// </summary>
    public void Preload() => _warmup = true;

    /// <summary>The coronas drawn this frame (their nodes visible). Main thread.</summary>
    public void Publish(ReadOnlySpan<Sample> samples)
    {
        lock (_gate)
        {
            if (_published.Length < samples.Length)
            {
                _published = new Sample[Math.Max(samples.Length, _published.Length * 2)];
            }

            samples.CopyTo(_published);
            _publishedCount = samples.Length;
        }
    }

    private void OnPreDraw()
    {
        if (_released)
        {
            return;
        }

        ViewKey key = KeyOf();
        Targets current;
        bool seen;
        ViewKey seenKey;
        Vector2I seenSize;
        int count;
        lock (_gate)
        {
            _frameKey = key;
            current = _current;
            seen = _seen;
            seenKey = _seenKey;
            seenSize = _seenSize;
            count = _publishedCount;
            _retired.AddRange(_replaced);
            _replaced.Clear();
        }

        if (current.IsValid && current != _bound)
        {
            // Texture2Drd hands the change to the render thread; the frees below are queued after it.
            Scene.TextureRdRid = current.Scene;
            Depth.TextureRdRid = current.Depth;
            _bound = current;
        }

        FreeRetired();
        // With no corona drawn the effect dispatches nothing: a warm-up frame leaves the picture as it is.
        bool on = count > 0 || _warmup;
        if (Enabled != on)
        {
            Enabled = on;
        }

        UseCopies?.Invoke(_bound.IsValid && seen && seenKey == key && seenSize == _bound.Size);
    }

    private ViewKey KeyOf()
    {
        Viewport? viewport = IsInstanceValid(_camera) && _camera.IsInsideTree() ? _camera.GetViewport() : null;
        if (viewport != _viewport)
        {
            if (_viewport is not null && IsInstanceValid(_viewport))
            {
                _viewport.SizeChanged -= OnSizeChanged;
            }

            _viewport = viewport;
            if (viewport is not null)
            {
                viewport.SizeChanged += OnSizeChanged;
            }

            _sizeEpoch++;
        }

        return viewport is null ? default
            : new ViewKey(viewport.GetInstanceId(), _sizeEpoch, viewport.Scaling3DScale, viewport.Scaling3DMode);
    }

    // Viewport.size_changed comes with every change of the size the renderer is given.
    private void OnSizeChanged() => _sizeEpoch++;

    // Frees the replaced copies the materials no longer read.
    private void FreeRetired()
    {
        if (_retired.Count == 0)
        {
            return;
        }

        var free = new List<Rid>();
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            Targets t = _retired[i];
            if (t == _bound)
            {
                continue;
            }

            free.Add(t.Scene);
            free.Add(t.Depth);
            _retired.RemoveAt(i);
        }

        if (free.Count > 0)
        {
            FreeOnRenderThread([.. free]);
        }
    }

    private static void FreeOnRenderThread(Rid[] rids) =>
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            RenderingDevice? rd = RenderingServer.GetRenderingDevice();
            if (rd is null)
            {
                return;
            }

            foreach (Rid rid in rids)
            {
                if (rid.IsValid && rd.TextureIsValid(rid))
                {
                    rd.FreeRid(rid);
                }
            }
        }));

    /// <summary>
    /// Stops the effect and frees its textures (once, main thread): the coronas go back to the screen-texture shader
    /// first, then the textures' bindings, then the textures on the render thread.
    /// </summary>
    public void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        RenderingServer.FramePreDraw -= OnPreDraw;
        if (_viewport is not null && IsInstanceValid(_viewport))
        {
            _viewport.SizeChanged -= OnSizeChanged;
        }

        _viewport = null;
        UseCopies = null;
        Scene.TextureRdRid = default;
        Depth.TextureRdRid = default;
        _bound = default;
        var textures = new List<Rid>();
        lock (_gate)
        {
            _retired.AddRange(_replaced);
            _replaced.Clear();
            _retired.Add(_current);
            _current = default;
            _seen = false;
        }

        foreach (Targets t in _retired)
        {
            textures.Add(t.Scene);
            textures.Add(t.Depth);
        }

        _retired.Clear();
        Rid[] rids = [.. textures];
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            if (_rd is not { } rd)
            {
                return;
            }

            foreach (Rid rid in rids)
            {
                if (rid.IsValid && rd.TextureIsValid(rid))
                {
                    rd.FreeRid(rid);
                }
            }

            if (_sampler.IsValid)
            {
                rd.FreeRid(_sampler);
            }

            // The copy shader and pipeline are shared through ComputeShaderSource.
            ComputeShaderSource.ReleaseShared(rd, _shader);
            _sampler = _shader = _pipeline = default;
            _rd = null;
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

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        // The frame's wrappers are released here, on the render thread, as in the other effects.
        using RenderData? frameData = renderData;
        using RenderSceneBuffers? sceneBuffers = renderData?.GetRenderSceneBuffers();
        using RenderSceneData? sceneData = renderData?.GetRenderSceneData();
        if (_released || sceneBuffers is not RenderSceneBuffersRD buffers || sceneData is null)
        {
            return;
        }

        if (!_attempted)
        {
            Init();
        }

        // This frame makes the copies too (below), so the warm-up is done.
        _warmup = false;
        if (_rd is not { } rd || !_pipeline.IsValid)
        {
            return;
        }

        Vector2I size = buffers.GetInternalSize();
        Rid colour = buffers.GetColorLayer(0, false);
        Rid depth = buffers.GetDepthLayer(0, false);
        bool supported = buffers.GetViewCount() == 1 && size.X > 0 && size.Y > 0 && depth.IsValid && ColourSupported(rd, colour);
        Targets made = default;
        bool remake = supported && (!_current.IsValid || _current.Size != size);
        if (remake)
        {
            made = Make(rd, size);
        }

        Targets targets;
        int count;
        lock (_gate)
        {
            if (!supported)
            {
                // The copies cannot stand in for this frame's textures: the coronas keep the screen-texture shader.
                _seen = false;
                return;
            }

            if (remake)
            {
                if (_current.IsValid)
                {
                    _replaced.Add(_current);
                }

                _current = made;
            }

            _seen = true;
            _seenKey = _frameKey;
            _seenSize = size;
            targets = _current;
            count = _publishedCount;
            if (_frame.Length < count)
            {
                _frame = new Sample[_published.Length];
            }

            Array.Copy(_published, _frame, count);
        }

        if (!targets.IsValid || count == 0)
        {
            return;
        }

        // The scene data's projection is the camera's: the renderer adds its y flip (and jitter) to the shader's
        // PROJECTION_MATRIX, so a corona's rows are mirrored from this projection's. Each rectangle is copied flipped
        // and as it is, so the copies hold the rows the shader reads either way.
        Transform3D camera = sceneData.GetCamTransform();
        Transform3D view = camera.AffineInverse();
        Projection projection = sceneData.GetCamProjection();
        float cameraReach = camera.Origin.Length();
        UniformSlots slots = _slots ??= new UniformSlots(RenderingDevice.UniformType.SamplerWithTexture,
            RenderingDevice.UniformType.SamplerWithTexture, RenderingDevice.UniformType.Image, RenderingDevice.UniformType.Image);
        long list = 0;
        bool open = false;
        for (int n = 0; n < count * 2; n++)
        {
            if (!RectOf(_frame[n / 2], view, cameraReach, projection, size, n % 2 == 0, out Vector4I rect))
            {
                continue;
            }

            if (!open)
            {
                slots.Set(0, _sampler, colour);
                slots.Set(1, _sampler, depth);
                slots.Set(2, targets.Scene);
                slots.Set(3, targets.Depth);
                Rid set = UniformSetCacheRD.GetCache(_shader, 0, slots.Array);
                list = rd.ComputeListBegin();
                rd.ComputeListBindComputePipeline(list, _pipeline);
                rd.ComputeListBindUniformSet(list, set, 0);
                open = true;
            }

            int[] values = _pushValues;
            (values[0], values[1], values[2], values[3]) = (rect.X, rect.Y, rect.Z, rect.W);
            (values[4], values[5]) = (size.X, size.Y);
            MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(_push);
            rd.ComputeListSetPushConstant(list, _push, (uint)_push.Length);
            rd.ComputeListDispatch(list, (uint)((rect.Z + 7) / 8), (uint)((rect.W + 7) / 8), 1);
        }

        if (open)
        {
            rd.ComputeListEnd();
        }
    }

    /// <summary>
    /// The pixels <paramref name="sample"/>'s quad can cover (x, y, width, height), or false when it covers none of the
    /// frame. The quad is camera-facing at the centre's view depth, its corners at most half size x sqrt 2 from the
    /// centre on each axis at any roll. <paramref name="cameraReach"/> is the camera's distance from the origin (m).
    /// <paramref name="flipY"/> mirrors the rows (the renderer's y flip).
    /// </summary>
    private static bool RectOf(Sample sample, Transform3D view, float cameraReach, Projection p, Vector2I size, bool flipY, out Vector4I rect)
    {
        rect = default;
        Vector3 v = view * sample.Centre;
        Vector4 clip = p * new Vector4(v.X, v.Y, v.Z, 1f);
        double w = clip.W;
        // Behind the eye (or on its plane) the whole quad is, as all its corners share the centre's depth: nothing draws.
        if (!(w > 0.0))
        {
            return false;
        }

        double cx = ((clip.X / w * 0.5) + 0.5) * size.X;
        double cy = (((flipY ? -clip.Y : clip.Y) / w * 0.5) + 0.5) * size.Y;
        double r = (sample.HalfSize * Math.Sqrt(2.0)) + (Slack * (sample.Centre.Length() + cameraReach + 1.0));
        double ex = (Math.Abs(p.X.X) + Math.Abs(p.Y.X)) * r / w * 0.5 * size.X;
        double ey = (Math.Abs(p.X.Y) + Math.Abs(p.Y.Y)) * r / w * 0.5 * size.Y;
        double x0 = Math.Floor(cx - ex) - Margin;
        double x1 = Math.Ceiling(cx + ex) + Margin;
        double y0 = Math.Floor(cy - ey) - Margin;
        double y1 = Math.Ceiling(cy + ey) + Margin;
        // A projection whose w changes across the view plane would change w across the quad too: take the whole frame.
        if (p.X.W != 0f || p.Y.W != 0f || !double.IsFinite(x0 + x1 + y0 + y1))
        {
            x0 = y0 = double.NegativeInfinity;
            x1 = y1 = double.PositiveInfinity;
        }

        // Only pixels in the frame draw; past its edges the margin stays for the depth read's wrap (the shader's wrap
        // needs it no wider than the frame).
        int mx = Math.Min(Margin, size.X);
        int my = Math.Min(Margin, size.Y);
        x0 = Math.Max(x0, -mx);
        y0 = Math.Max(y0, -my);
        x1 = Math.Min(x1, size.X - 1 + mx);
        y1 = Math.Min(y1, size.Y - 1 + my);
        if (x0 > x1 || y0 > y1)
        {
            return false;
        }

        rect = new Vector4I((int)x0, (int)y0, (int)(x1 - x0) + 1, (int)(y1 - y0) + 1);
        return true;
    }

    private void Init()
    {
        _attempted = true;
        RenderingDevice? rd = RenderingServer.GetRenderingDevice();
        if (rd is null)
        {
            return;
        }

        if (!ComputeShaderSource.AcquireSharedSource(rd, Source, "svm_corona_copy", out _shader, out _pipeline, out string? error))
        {
            GD.PushError("svm_corona_copy: " + error);
            return;
        }

        using var sampler = new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
        };
        _sampler = rd.SamplerCreate(sampler);
        _rd = rd;
    }

    // The colour buffer can be copied into the RGBA16F copy (once per buffer: TextureGetFormat makes a wrapper).
    private bool ColourSupported(RenderingDevice rd, Rid colour)
    {
        if (!colour.IsValid)
        {
            return false;
        }

        if (colour != _checkedColour)
        {
            using RDTextureFormat format = rd.TextureGetFormat(colour);
            _checkedColour = colour;
            _colourSupported = format.Format == RenderingDevice.DataFormat.R16G16B16A16Sfloat
                && format.Samples == RenderingDevice.TextureSamples.Samples1
                && (format.UsageBits & RenderingDevice.TextureUsageBits.SamplingBit) != 0;
        }

        return _colourSupported;
    }

    private static Targets Make(RenderingDevice rd, Vector2I size)
    {
        Rid scene = Create(rd, RenderingDevice.DataFormat.R16G16B16A16Sfloat, size);
        Rid depth = Create(rd, RenderingDevice.DataFormat.R32Sfloat, size);
        if (scene.IsValid && depth.IsValid)
        {
            return new Targets(scene, depth, size);
        }

        if (scene.IsValid)
        {
            rd.FreeRid(scene);
        }

        if (depth.IsValid)
        {
            rd.FreeRid(depth);
        }

        return default;
    }

    private static Rid Create(RenderingDevice rd, RenderingDevice.DataFormat format, Vector2I size)
    {
        using var textureFormat = new RDTextureFormat
        {
            Format = format,
            Width = (uint)size.X,
            Height = (uint)size.Y,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
        };
        using var textureView = new RDTextureView();
        return rd.TextureCreate(textureFormat, textureView);
    }
}
