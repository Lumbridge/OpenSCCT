using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>The shape and colour of an LE_ESpotShadow light (Echelon spot: the merc torch, window and sun projectors).</summary>
/// <param name="Range">EchelonRange in uu: the light reaches this far along its axis.</param>
/// <param name="SpotWidth">SpotWidth in uu: the lit width at <paramref name="Range"/>.</param>
/// <param name="SpotHeight">SpotHeight in uu.</param>
/// <param name="NearPlaneRatio">NearPlaneRatio p: the frustum's apex sits p x Range / (1 - p) behind the light.</param>
/// <param name="Brightness">LightBrightness.</param>
/// <param name="Hue">LightHue.</param>
/// <param name="Saturation">LightSaturation (255 is white).</param>
/// <param name="NormalInfluence">EchelonNormalInfluence: 0 lights every surface fully, 1 by N.L.</param>
/// <param name="Attenuation">EchelonLightingMaterial.AttenuationType: 0 none, 1 linear, 2 late.</param>
/// <param name="Cookie">Object path of the projected texture in the render pack.</param>
public sealed record SpotShadowShape(float Range, float SpotWidth, float SpotHeight, float NearPlaneRatio, float Brightness, byte Hue,
    byte Saturation, float NormalInfluence, int Attenuation, string Cookie)
{
    /// <summary>The merc torch, SGadTorche (spec/equipment/merc-flashlight.md): Echelon.Torche cookie, linear attenuation, no normal influence.</summary>
    public static SpotShadowShape Torch { get; } = new(RenderK.TorchRange, RenderK.TorchSpotSize, RenderK.TorchSpotSize,
        RenderK.TorchNearPlaneRatio, RenderK.TorchBrightness, RenderK.TorchHue, RenderK.TorchSaturation, 0f, RenderK.TorchAttenuation,
        RenderPack.TorchCookie);

    /// <summary>A map LE_ESpotShadow light's shape (its cookie is a map texture, passed to the light separately).</summary>
    public static SpotShadowShape Of(LightEntry l, EchelonSpot e)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(e);
        return new SpotShadowShape(MathF.Max(e.Range, 1f), MathF.Max(e.Width, 1f), MathF.Max(e.Height, 1f), e.NearPlaneRatio, l.Brightness,
            l.Hue, l.Saturation, e.NormalInfluence, e.Attenuation, e.Texture ?? "");
    }

    /// <summary>The apex distance behind the light in uu (p x Range / (1 - p)), or null for parallel rays.</summary>
    [Provenance(ProvenanceKind.Decoded, "ESpotShadow light-space frustum, editor 0x111a5480 (bridge docs/LIGHTING_BALANCE.md)")]
    public float? ApexDistance()
    {
        float p = Math.Clamp(NearPlaneRatio, 0f, 1f);
        return p >= RenderK.SpotParallelRatio ? null : p * Range / (1f - p);
    }
}

/// <summary>
/// An LE_ESpotShadow light drawn the Faithful way (docs/RENDERING.md#spot-shadow-lights). The frustum is the decoded
/// one: its apex sits NearPlaneRatio x EchelonRange / (1 - NearPlaneRatio) behind the light, the near plane at the
/// light and the far plane, SpotWidth x SpotHeight, EchelonRange further on; geometry between the apex and the light
/// never shadows. A camera at the apex renders the world's depth into the light's tile of a shared shadow atlas (kept
/// as linear distance), and the world template adds, per pixel, to the light stage of every surface it reaches:
/// colour x cookie(uv) x attenuation(d / Range) x shadow. Each light holds one of the template's
/// <see cref="RenderK.SpotShadowSlots"/> slots for its life; <see cref="Place"/> moves it, <see cref="On"/> switches it.
/// </summary>
[PortOf("eng:Render.SpotShadow", Spec = "docs/RENDERING.md#spot-shadow-lights")]
public sealed partial class SpotShadowLight : IDisposable
{
    public const string ParamSlots = "svm_spots";
    public const string ParamCount = "svm_spot_count";
    public const string ParamDepth = "svm_spot_depth";
    public const string ParamCookies = "svm_spot_cookies";
    public const string ParamAttenuation = "svm_spot_atten";

    /// <summary>Texels per slot row of the slot texture (the template's layout, svm_world.gdshaderinc).</summary>
    private const int SlotTexels = 8;

    /// <summary>
    /// The shadow map height the next set of lights uses (Reloaded's shadow_quality, <see cref="MapSizeFor"/>): read
    /// when the first light of a map takes the shared atlas, so a change applies from the next map.
    /// </summary>
    public static int MapSize { get; set; } = RenderK.SpotShadowMapSize;

    /// <summary>The disc radius in texels that softens shadow edges (Reloaded's shadow_edge_smoothing); 0 for one lookup.</summary>
    public static float SoftRadius { get; set; } = RenderK.SpotShadowSoftRadius;

    /// <summary>
    /// The map height for a shadow_quality: 256 texels x the quality (2, the documented default, is the stock 512),
    /// a power of two from 128 to 2048 (an 8192 x 8192 atlas at the top).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "rl:gfx.shadows maps shadow_quality to the atlas (1024 x value, 8 = 8K): the atlas is four map heights across, so the map is 256 x value")]
    public static int MapSizeFor(float quality)
    {
        int size = (int)MathF.Round(256f * Math.Clamp(quality, 0.5f, 16f));
        return Math.Clamp((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, size)), 128, 2048);
    }

    private static int s_mapSize = RenderK.SpotShadowMapSize;

    /// <summary>Atlas tiles are two shadow map heights wide; the atlas is two tiles across.</summary>
    private static int TileWidth => s_mapSize * 2;

    private static int TileHeight => s_mapSize;
    private const int AtlasColumns = 2;
    private static int AtlasWidth => TileWidth * AtlasColumns;
    private static int AtlasHeight => TileHeight * ((RenderK.SpotShadowSlots + AtlasColumns - 1) / AtlasColumns);

    private static readonly StringName s_slotsName = ParamSlots;
    private static readonly StringName s_countName = ParamCount;
    private static readonly StringName s_depthName = ParamDepth;
    private static readonly StringName s_cookiesName = ParamCookies;

    private static bool s_globalsAdded;

    // The slots (shared by every spot shadow light), their parameter rows and the atlas.
    private static readonly SpotShadowLight?[] s_slots = new SpotShadowLight?[RenderK.SpotShadowSlots];
    private static Image? s_rows;
    private static ImageTexture? s_rowsTexture;
    private static ImageTexture? s_noDepth;
    private static readonly Texture2Drd s_atlasTexture = new();
    private static Rid s_atlas;
    private static bool s_atlasPending;

    // The cookies in the template's texture array: layer 0 is white (no cookie).
    private static readonly List<Texture2D> s_cookies = [];

    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly SpotDepthCapture _capture;
    private readonly Vector3 _colour;
    private readonly int _slot;
    private readonly Vector2I _size;
    private readonly bool _parallel;
    private readonly float _apex;
    private readonly int _cookieLayer;
    private bool _on;
    private bool _disposed;

    /// <param name="parent">Node the light's shadow viewport goes under.</param>
    /// <param name="pack">The render pack (attenuation ramps; the cookie when <paramref name="cookie"/> is null).</param>
    /// <param name="shape">The light.</param>
    /// <param name="cookie">The projected texture, else the pack's <see cref="SpotShadowShape.Cookie"/> (none: white).</param>
    public SpotShadowLight(Node parent, RenderPack? pack, SpotShadowShape shape, Texture2D? cookie = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(shape);
        Shape = shape;
        EnsureGlobals(pack);
        if (!s_atlas.IsValid && !s_atlasPending && Array.TrueForAll(s_slots, x => x is null))
        {
            s_mapSize = MapSize;
        }

        _slot = Array.IndexOf(s_slots, null);
        if (_slot >= 0)
        {
            s_slots[_slot] = this;
        }

        EnsureAtlas();
        float aspect = shape.SpotWidth / MathF.Max(1f, shape.SpotHeight);
        _size = aspect >= 2f
            ? new Vector2I(TileWidth, Math.Max(1, (int)MathF.Round(TileWidth / aspect)))
            : new Vector2I(Math.Max(1, (int)MathF.Round(TileHeight * aspect)), TileHeight);
        float? apex = shape.ApexDistance();
        _parallel = apex is null;
        _apex = apex ?? 0f;
        float near = UeSpace.LengthToGodot(MathF.Max(1f, _apex));
        float far = UeSpace.LengthToGodot(_apex + shape.Range);
        _capture = new SpotDepthCapture(_size, near, far, TileOrigin(_slot), _parallel);
        _viewport = new SubViewport
        {
            Name = "SpotShadow",
            Size = _size,
            Msaa3D = Godot.Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        _camera = new Camera3D
        {
            Name = "LightCamera",
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = near,
            Far = far,
            CullMask = RenderLayers.World,
            Environment = FaithfulView.CreateEnvironment(),
            Attributes = FaithfulView.CreateAttributes(),
            Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _capture } },
            Current = true,
        };
        if (_parallel)
        {
            _camera.Projection = Camera3D.ProjectionType.Orthogonal;
            _camera.Size = UeSpace.LengthToGodot(shape.SpotHeight);
        }
        else
        {
            _camera.Projection = Camera3D.ProjectionType.Perspective;
            _camera.Fov = Mathf.RadToDeg(2f * MathF.Atan(shape.SpotHeight * 0.5f / (_apex + shape.Range)));
        }

        _viewport.AddChild(_camera);
        parent.AddChild(_viewport);
        cookie ??= string.IsNullOrEmpty(shape.Cookie) ? null : pack?.Texture(shape.Cookie);
        _cookieLayer = CookieLayer(cookie);
        _colour = Colour(shape) * RenderK.SpotShadowGain;
    }

    public SpotShadowShape Shape { get; }

    /// <summary>Where the light is and where it points (UE), as last placed.</summary>
    public (FVector Location, FRotator Rotation) Placement { get; private set; }

    /// <summary>The light's shadow viewport (rendered only while <see cref="On"/>).</summary>
    public SubViewport Viewport => _viewport;

    /// <summary>The zone the light keeps to (bAffectOwnZoneOnly), or -1 for every zone.</summary>
    public int Zone { get; set; } = -1;

    /// <summary>The light type's brightness factor this frame (FDynamicLight::Update); applied at the next <see cref="Push"/>.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>False when every slot was taken: the light draws nothing.</summary>
    public bool HasSlot => _slot >= 0;

    /// <summary>LightType != LT_None: the shadow pass runs and the template adds the light.</summary>
    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            _viewport.RenderTargetUpdateMode = value && HasSlot ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            Push();
        }
    }

    /// <summary>The light colour of the 2x stage: FGetHSV(hue, saturation) x brightness / 255.</summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0 (as actor lighting)")]
    public static Vector3 Colour(SpotShadowShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var entry = new LightEntry("spot", default, default, shape.Brightness, shape.Hue, shape.Saturation, 0f, 0, RenderK.LE_ESpotShadow,
            1, true, false, true, true, null, 32f, 0f, false);
        return ActorLighting.Colour(entry);
    }

    /// <summary>Moves the light to a UE location and rotation (its axis is the rotation's forward); the camera sits at the apex.</summary>
    public void Place(FVector location, FRotator rotation)
    {
        Placement = (location, rotation);
        _camera.Transform = FaithfulView.CameraTransform(location - (rotation.Vector() * _apex), rotation);
        Push();
    }

    /// <summary>Writes the light into its slot of the template (or clears the slot when off).</summary>
    public void Push()
    {
        BindAtlas();
        if (!HasSlot || s_rows is null)
        {
            return;
        }

        bool live = _on && !_disposed && _camera.IsInsideTree() && _capture.Ready;
        if (!live)
        {
            SetRow(_slot, 3, Vector4.Zero);
            Flush();
            return;
        }

        // Rows 0-2: world to the light camera's view (3 x 4).
        Transform3D view = _camera.GlobalTransform.AffineInverse();
        Basis b = view.Basis;
        SetRow(_slot, 0, new Vector4(b.Row0.X, b.Row0.Y, b.Row0.Z, view.Origin.X));
        SetRow(_slot, 1, new Vector4(b.Row1.X, b.Row1.Y, b.Row1.Z, view.Origin.Y));
        SetRow(_slot, 2, new Vector4(b.Row2.X, b.Row2.Y, b.Row2.Z, view.Origin.Z));
        float range = UeSpace.LengthToGodot(Shape.Range);
        SetRow(_slot, 3, new Vector4(1f, range, Shape.NormalInfluence, Shape.Attenuation));
        Vector3 c = _colour * Scale;
        SetRow(_slot, 4, new Vector4(c.X, c.Y, c.Z, UeSpace.LengthToGodot(_apex)));
        float aspect = _size.X / (float)_size.Y;
        Vector4 frustum;
        if (_parallel)
        {
            float h = UeSpace.LengthToGodot(Shape.SpotHeight);
            frustum = new Vector4(2f / (h * aspect), 2f / h, 1f, _cookieLayer);
        }
        else
        {
            float tanH = Shape.SpotHeight * 0.5f / (_apex + Shape.Range);
            frustum = new Vector4(1f / (tanH * aspect), 1f / tanH, 0f, _cookieLayer);
        }

        SetRow(_slot, 5, frustum);
        Vector2I origin = TileOrigin(_slot);
        SetRow(_slot, 6, new Vector4(origin.X / (float)AtlasWidth, origin.Y / (float)AtlasHeight, _size.X / (float)AtlasWidth,
            _size.Y / (float)AtlasHeight));
        SetRow(_slot, 7, new Vector4(Zone, UeSpace.LengthToGodot(2f), 0.01f, SoftRadius));
        Flush();
    }

    private static Vector2I TileOrigin(int slot) =>
        slot < 0 ? Vector2I.Zero : new Vector2I(slot % AtlasColumns * TileWidth, slot / AtlasColumns * TileHeight);

    private static void SetRow(int slot, int texel, Vector4 v) => s_rows!.SetPixel(texel, slot, new Color(v.X, v.Y, v.Z, v.W));

    private static void Flush()
    {
        s_rowsTexture?.Update(s_rows);
        int count = 0;
        for (int i = 0; i < s_slots.Length; i++)
        {
            if (s_slots[i] is not null)
            {
                count = i + 1;
            }
        }

        RenderingServer.GlobalShaderParameterSet(s_countName, count);
    }

    /// <summary>Registers the template's spot slots (once per process), with the attenuation ramps of the pack.</summary>
    public static void EnsureGlobals(RenderPack? pack = null)
    {
        if (!s_globalsAdded)
        {
            s_rows = Image.CreateEmpty(SlotTexels, RenderK.SpotShadowSlots, false, Image.Format.Rgbaf);
            s_rowsTexture = ImageTexture.CreateFromImage(s_rows);
            s_noDepth = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rf));
            RenderingServer.GlobalShaderParameterAdd(ParamSlots, RenderingServer.GlobalShaderParameterType.Sampler2D, s_rowsTexture);
            RenderingServer.GlobalShaderParameterAdd(ParamCount, RenderingServer.GlobalShaderParameterType.Int, 0);
            RenderingServer.GlobalShaderParameterAdd(ParamDepth, RenderingServer.GlobalShaderParameterType.Sampler2D, s_noDepth);
            RenderingServer.GlobalShaderParameterAdd(ParamCookies, RenderingServer.GlobalShaderParameterType.Sampler2Darray, Cookies());
            RenderingServer.GlobalShaderParameterAdd(ParamAttenuation, RenderingServer.GlobalShaderParameterType.Sampler2D, Ramps(null));
            s_globalsAdded = true;
        }

        if (pack is not null)
        {
            RenderingServer.GlobalShaderParameterSet(ParamAttenuation, Ramps(pack));
        }
    }

    /// <summary>
    /// The Echelon attenuation ramps as one 16 x 2 texture: row 0 Engine.linear_attenuation, row 1
    /// Engine.late_attenuation (16 texels along the light's range), or analytic stand-ins without the pack.
    /// </summary>
    private static ImageTexture Ramps(RenderPack? pack)
    {
        Image ramps = Image.CreateEmpty(16, 2, false, Image.Format.Rgba8);
        Image? linear = pack?.Image(RenderPack.LinearAttenuation);
        Image? late = pack?.Image(RenderPack.LateAttenuation);
        for (int x = 0; x < 16; x++)
        {
            float t = x / 15f;
            float l = linear is not null && linear.GetWidth() == 16 ? linear.GetPixel(x, 0).R : 1f - t;
            float k = late is not null && late.GetWidth() == 16 ? late.GetPixel(x, 0).R : (t < 0.9f ? 1f : 0f);
            ramps.SetPixel(x, 0, new Color(l, l, l, 1f));
            ramps.SetPixel(x, 1, new Color(k, k, k, 1f));
        }

        return ImageTexture.CreateFromImage(ramps);
    }

    /// <summary>The cookie's layer in the template's texture array (0, white, without one); a new cookie rebuilds the array.</summary>
    private static int CookieLayer(Texture2D? cookie)
    {
        if (cookie is null)
        {
            return 0;
        }

        int i = s_cookies.IndexOf(cookie);
        if (i >= 0)
        {
            return i + 1;
        }

        s_cookies.Add(cookie);
        RenderingServer.GlobalShaderParameterSet(s_cookiesName, Cookies());
        return s_cookies.Count;
    }

    /// <summary>The cookies as one array, each resampled to <see cref="RenderK.SpotCookieSize"/> square RGBA8.</summary>
    private static Texture2DArray Cookies()
    {
        int size = RenderK.SpotCookieSize;
        var images = new Godot.Collections.Array<Image>();
        Image white = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        white.Fill(new Color(1f, 1f, 1f, 1f));
        images.Add(white);
        foreach (Texture2D t in s_cookies)
        {
            Image? img = t.GetImage();
            if (img is null || img.IsEmpty())
            {
                images.Add(white);
                continue;
            }

            if (img.IsCompressed())
            {
                img.Decompress();
            }

            img.ClearMipmaps();
            img.Convert(Image.Format.Rgba8);
            img.Resize(size, size, Image.Interpolation.Bilinear);
            images.Add(img);
        }

        var array = new Texture2DArray();
        array.CreateFromImages(images);
        return array;
    }

    /// <summary>Asks the render thread for the shared shadow atlas, when no light holds it.</summary>
    private static void EnsureAtlas()
    {
        if (s_atlas.IsValid || s_atlasPending)
        {
            return;
        }

        s_atlasPending = true;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            RenderingDevice? rd = RenderingServer.GetRenderingDevice();
            if (rd is not null)
            {
                s_atlas = rd.TextureCreate(new RDTextureFormat
                {
                    Format = RenderingDevice.DataFormat.R32Sfloat,
                    Width = (uint)AtlasWidth,
                    Height = (uint)AtlasHeight,
                    UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
                }, new RDTextureView());
            }

            s_atlasPending = false;
        }));
    }

    /// <summary>The atlas, once made, goes to the template's depth slot.</summary>
    private static void BindAtlas()
    {
        Rid atlas = s_atlas;
        if (atlas.IsValid && s_atlasTexture.TextureRdRid != atlas)
        {
            s_atlasTexture.TextureRdRid = atlas;
            RenderingServer.GlobalShaderParameterSet(s_depthName, s_atlasTexture);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        On = false;
        _disposed = true;
        if (HasSlot)
        {
            SetRow(_slot, 3, Vector4.Zero);
            s_slots[_slot] = null;
            Flush();
        }

        _viewport.QueueFree();
        // The effect is a resource the managed side keeps alive: without the garbage collector running before the
        // process quits its predelete never comes, so its render device objects are freed here.
        _capture.Release();
        if (Array.TrueForAll(s_slots, s => s is null))
        {
            ReleaseShared();
        }
    }

    /// <summary>The last light is gone: the template stops sampling the atlas and the cookies, and both are freed.</summary>
    private static void ReleaseShared()
    {
        if (s_noDepth is not null)
        {
            RenderingServer.GlobalShaderParameterSet(s_depthName, s_noDepth);
        }

        s_atlasTexture.TextureRdRid = default;
        s_cookies.Clear();
        RenderingServer.GlobalShaderParameterSet(s_cookiesName, Cookies());
        Rid atlas = s_atlas;
        s_atlas = default;
        if (atlas.IsValid)
        {
            RenderingServer.CallOnRenderThread(Callable.From(() => RenderingServer.GetRenderingDevice()?.FreeRid(atlas)));
        }
    }

    /// <summary>
    /// The light's depth copy: after its camera's opaque pass, the depth buffer becomes linear distance along the axis in
    /// the light's tile of the shared R32F atlas the template samples.
    /// </summary>
    private sealed partial class SpotDepthCapture : CompositorEffect
    {
        public const string ShaderPath = "res://Shaders/svm_spot_depth.glsl";

        private readonly Vector2I _size;
        private readonly float _near;
        private readonly float _far;
        private readonly Vector2I _origin;
        private readonly bool _parallel;
        private RenderingDevice? _rd;
        private Rid _shader;
        private Rid _pipeline;
        private Rid _sampler;

        public SpotDepthCapture(Vector2I size, float near, float far, Vector2I origin, bool parallel)
        {
            _size = size;
            _near = near;
            _far = far;
            _origin = origin;
            _parallel = parallel;
            EffectCallbackType = EffectCallbackTypeEnum.PostOpaque;
            AccessResolvedDepth = true;
            RenderingServer.CallOnRenderThread(Callable.From(Init));
        }

        /// <summary>The pipeline is made and the atlas exists: the light's tile is written each frame it renders.</summary>
        public bool Ready => _pipeline.IsValid && s_atlas.IsValid;

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
            Rid atlas = s_atlas;
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
}
