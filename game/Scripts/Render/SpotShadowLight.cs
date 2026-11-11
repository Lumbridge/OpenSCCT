using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Lighting;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>The shape and colour of an LE_ESpotShadow light (Echelon spot: the merc torch, window and sun projectors).</summary>
/// <param name="Range">EchelonRange in uu: the light reaches this far along its axis.</param>
/// <param name="SpotWidth">SpotWidth in uu: half the lit width at <paramref name="Range"/> (editor 0x111a5480 maps
/// +-SpotWidth to the cookie's edges).</param>
/// <param name="SpotHeight">SpotHeight in uu: half the lit height at <paramref name="Range"/>.</param>
/// <param name="NearPlaneRatio">NearPlaneRatio p: the frustum's apex sits p x Range / (1 - p) behind the light.</param>
/// <param name="Brightness">LightBrightness.</param>
/// <param name="Hue">LightHue.</param>
/// <param name="Saturation">LightSaturation (255 is white).</param>
/// <param name="NormalInfluence">EchelonNormalInfluence: 0 lights every surface fully, 1 by N.L.</param>
/// <param name="Attenuation">EchelonLightingMaterial.AttenuationType: 0 none, 1 linear, 2 late.</param>
/// <param name="Cookie">Object path of the projected texture in the render pack.</param>
/// <param name="Dynamic">bDynamicLight: actors with bIgnoreDynamicEchelonLighting skip it, those with
/// bIgnoreEchelonLighting skip the others (game 0x10a37310).</param>
public sealed record SpotShadowShape(float Range, float SpotWidth, float SpotHeight, float NearPlaneRatio, float Brightness, byte Hue,
    byte Saturation, float NormalInfluence, int Attenuation, string Cookie, bool Dynamic = false)
{
    /// <summary>The merc torch, SGadTorche (spec/equipment/merc-flashlight.md): Echelon.Torche cookie, linear attenuation, no normal influence.</summary>
    public static SpotShadowShape Torch { get; } = new(RenderK.TorchRange, RenderK.TorchSpotSize, RenderK.TorchSpotSize,
        RenderK.TorchNearPlaneRatio, RenderK.TorchBrightness, RenderK.TorchHue, RenderK.TorchSaturation, 0f, RenderK.TorchAttenuation,
        RenderPack.TorchCookie, Dynamic: true);

    /// <summary>
    /// A map LE_ESpotShadow light's shape (its cookie is a map texture, passed to the light separately). A light without
    /// an EchelonLightMaterial takes the level's DefaultEchelonLightMaterial, whose cookie is the soft round
    /// <see cref="RenderPack.SpotCookie"/>.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "the editor exe names Lighting spot_bw next to its built-in packages (DefaultEchelonLightMaterial, not traced); attenuation stays linear")]
    public static SpotShadowShape Of(LightEntry l, EchelonSpot e)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(e);
        return new SpotShadowShape(MathF.Max(e.Range, 1f), MathF.Max(e.Width, 1f), MathF.Max(e.Height, 1f), e.NearPlaneRatio, l.Brightness,
            l.Hue, l.Saturation, e.NormalInfluence, e.Attenuation, e.Texture ?? RenderPack.SpotCookie, l.Dynamic);
    }

    /// <summary>The apex distance behind the light in uu (p x Range / (1 - p)), or null for parallel rays.</summary>
    [Provenance(ProvenanceKind.Decoded, "ESpotShadow light-space frustum, editor 0x111a5480 (bridge docs/LIGHTING_BALANCE.md)")]
    public float? ApexDistance()
    {
        float p = Math.Clamp(NearPlaneRatio, 0f, 1f);
        return p >= RenderK.SpotParallelRatio ? null : p * Range / (1f - p);
    }

    /// <summary>Conservative bounds of the complete lit volume, used before allocating an atlas slot.</summary>
    public Aabb InfluenceBounds(FVector location, FRotator rotation)
    {
        rotation.GetAxes(out FVector forward, out FVector right, out FVector up);
        var bounds = new Aabb(UeSpace.ToGodot(location), Vector3.Zero);
        float nearRatio = ApexDistance() is null ? 1f : Math.Clamp(NearPlaneRatio, 0f, 1f);
        for (int end = 0; end < 2; end++)
        {
            FVector center = location + forward * (end * Range);
            float scale = end == 0 ? nearRatio : 1f;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    bounds = bounds.Expand(UeSpace.ToGodot(center + right * (x * SpotWidth * scale) + up * (y * SpotHeight * scale)));
        }
        return bounds;
    }
}

/// <summary>
/// An LE_ESpotShadow light drawn the Faithful way (docs/RENDERING.md#spot-shadow-lights). The frustum is the decoded
/// one: its apex sits NearPlaneRatio x EchelonRange / (1 - NearPlaneRatio) behind the light, the near plane at the
/// light and the far plane, 2 SpotWidth x 2 SpotHeight, EchelonRange further on; geometry between the apex and the light
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
    public const string ParamIgnore = "svm_spot_ignore";
    public const string ParamBits = "svm_spot_bits";

    /// <summary>Texels per slot row of the slot texture (the template's layout, svm_world.gdshaderinc).</summary>
    private const int SlotTexels = 9;

    /// <summary>Slots svm_spot_bits can hold (16 on bits, 16 bDynamicLight bits).</summary>
    private const int BitSlots = 16;

    /// <summary>
    /// How far (m) the caster search reaches past the shadow camera's frustum: more than float noise between the
    /// search and Godot's own culling of the same bounds.
    /// </summary>
    private const float CasterMargin = 0.25f;

    /// <summary>
    /// Frames a light with a static cache keeps still before the static world is drawn into it. Until then the live
    /// pass draws the static world too (the same depth): a torch swept every frame would otherwise draw it twice.
    /// </summary>
    private const int StillFrames = 3;

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
    private static readonly StringName s_bitsName = ParamBits;
    private static int s_lastBits;

    private static bool s_globalsAdded;

    // The map's IgnoredEchelonLights groups (RenderMap.EchelonGroups; group 0 is empty) and the slot x group table the
    // template reads (1 where the group ignores the light in that slot), rebuilt when a slot or the groups change.
    private static IReadOnlyList<IReadOnlySet<string>> s_ignoreGroups = [];
    private static Image? s_ignore;
    private static ImageTexture? s_ignoreTexture;
    private static bool s_ignoreDirty = true;

    // The slots (shared by every spot shadow light), their parameter rows and the atlas.
    private static readonly SpotShadowLight?[] s_slots = new SpotShadowLight?[RenderK.SpotShadowSlots];
    private static Image? s_rows;
    private static ImageTexture? s_rowsTexture;
    private static readonly Vector4[] s_rowValues = new Vector4[SlotTexels * RenderK.SpotShadowSlots];
    private static bool s_rowsDirty;
    private static int s_lastCount;
    private static ImageTexture? s_noDepth;
    private static readonly Texture2Drd s_atlasTexture = new();
    private static Rid s_atlas;
    private static Rid s_staticAtlas;
    private static bool s_staticAtlasPending;
    private static bool s_atlasPending;

    // Retain CPU copies: rebuilding the array must not read earlier cookies back from the GPU during map loading.
    // Sources belong to their map/pack; only the normalized copies are owned here. Layer 0 is white (no cookie).
    private static readonly Dictionary<Image, int> s_cookieLayers = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Image> s_cookies = [];
    private static Texture2DArray? s_cookieTexture;

    /// <summary>Shared inputs for volumetric scattering, without querying shader globals back from the GPU.</summary>
    internal static (Texture2D Rows, Texture2D Depth, Texture2DArray Cookies, int Count) VolumeInputs()
    {
        EnsureGlobals();
        return (s_rowsTexture!, s_atlasTexture.TextureRdRid.IsValid ? s_atlasTexture : s_noDepth!, s_cookieTexture!, s_lastCount);
    }

    private readonly SubViewport _viewport;
    private readonly Camera3D _camera;
    private readonly ShadowDepthCapture _capture;
    private readonly Vector3 _colour;
    private readonly int _slot;
    private readonly Vector2I _size;
    private readonly bool _parallel;
    private readonly float _apex;
    private readonly int _cookieLayer;
    private bool _on;
    private bool _disposed;
    private bool _placed;
    private bool _cameraVisible = true;
    private Aabb _influenceBounds;
    private readonly SubViewport? _staticViewport;
    private readonly Camera3D? _staticCamera;
    private readonly ShadowDepthCapture? _staticCapture;
    private bool _staticDirty = true;
    private long _staticScheduledStamp;
    private bool _tileByRd;
    // The last look at the light's casters found one that moves (the tile is then written every frame, or every other).
    private bool _lastMoving;
    private List<Rect2I>? _rdWritten;
    private (bool Live, FVector Location, FRotator Rotation, float Scale, float Visibility, int Zone, float Soft)? _lastPush;

    // The live pass is drawn only when its tile could change (RefreshShadowPass): what the tile holds, the pass asked
    // for this frame, static passes scheduled so far, and the search planes for the camera's frustum.
    private ShadowTile? _tile;
    private ShadowTile? _asked;
    private int _staticRenders;
    private bool _liveDrawn;
    private Transform3D _lastCamera;
    private int _stillFrames;

    // Set just before the frame draws: the live pass takes the cached static depth (read by its capture on the render
    // thread) and leaves the static world out of its camera.
    private volatile bool _combine;
    private Godot.Collections.Array<Plane>? _searchPlanes;
    private Transform3D _searchFor;

    /// <param name="parent">Node the light's shadow viewport goes under.</param>
    /// <param name="pack">The render pack (attenuation ramps; the cookie when <paramref name="cookie"/> is null).</param>
    /// <param name="shape">The light.</param>
    /// <param name="cookie">The projected texture's CPU image, else the pack's <see cref="SpotShadowShape.Cookie"/>
    /// (none: white). The source is retained without changing or disposing it.</param>
    /// <param name="cacheStatic">Keep the static world's depth in a cached tile once the light keeps still; the live
    /// pass then draws only the other casters and takes the nearest of both (the same depth as one pass of everything).</param>
    public SpotShadowLight(Node parent, RenderPack? pack, SpotShadowShape shape, Image? cookie = null, bool cacheStatic = false)
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
            s_ignoreDirty = true;
        }

        EnsureAtlas();
        if (cacheStatic) EnsureStaticAtlas();
        float aspect = shape.SpotWidth / MathF.Max(1f, shape.SpotHeight);
        _size = aspect >= 2f
            ? new Vector2I(TileWidth, Math.Max(1, (int)MathF.Round(TileWidth / aspect)))
            : new Vector2I(Math.Max(1, (int)MathF.Round(TileHeight * aspect)), TileHeight);
        float? apex = shape.ApexDistance();
        _parallel = apex is null;
        _apex = apex ?? 0f;
        float near = UeSpace.LengthToGodot(MathF.Max(1f, _apex));
        float far = UeSpace.LengthToGodot(_apex + shape.Range);
        _capture = new ShadowDepthCapture(_size, near, far, TileOrigin(_slot), _parallel, static () => s_atlas,
            cacheStatic ? () => _combine ? s_staticAtlas : default : null);
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
            CullMask = RenderLayers.SpotShadow,
            Environment = FaithfulView.CreateEnvironment(),
            Attributes = FaithfulView.CreateAttributes(),
            Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _capture } },
            Current = true,
        };
        if (_parallel)
        {
            _camera.Projection = Camera3D.ProjectionType.Orthogonal;
            _camera.Size = UeSpace.LengthToGodot(2f * shape.SpotHeight);
        }
        else
        {
            // Set the near-plane height directly: valid Echelon frusta can exceed Camera3D.Fov's 1..179 degree
            // range (MuseE's Light15 is wider than 179). This keeps the shadow and cookie projection identical.
            _camera.SetFrustum(2f * UeSpace.LengthToGodot(shape.SpotHeight) * near / far, Vector2.Zero, near, far);
        }

        if (cacheStatic)
        {
            _staticCapture = new ShadowDepthCapture(_size, near, far, TileOrigin(_slot), _parallel, static () => s_staticAtlas);
            _staticViewport = new SubViewport
            {
                Name = "StaticShadow", Size = _size, Msaa3D = Godot.Viewport.Msaa.Disabled,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            _staticCamera = new Camera3D
            {
                Name = "StaticLightCamera", KeepAspect = _camera.KeepAspect, Near = near, Far = far,
                Projection = _camera.Projection, Size = _camera.Size,
                CullMask = RenderLayers.StaticWorldAll | RenderLayers.ShadowPass,
                Environment = FaithfulView.CreateEnvironment(), Attributes = FaithfulView.CreateAttributes(),
                Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _staticCapture } },
                Current = true,
            };
            _staticViewport.AddChild(_staticCamera);
            // Child viewports render before their parent: populate the static tile before the dynamic pass reads it.
            // The live camera leaves the static world out only while it takes the cached depth (RefreshShadowPass).
            _viewport.AddChild(_staticViewport);
            Diagnostics.ViewportTimings.Register(_staticViewport, Diagnostics.ViewportTimings.SpotStatic);
        }

        _viewport.AddChild(_camera);
        parent.AddChild(_viewport);
        Diagnostics.ViewportTimings.Register(_viewport, Diagnostics.ViewportTimings.SpotLive);
        cookie ??= string.IsNullOrEmpty(shape.Cookie) ? null : pack?.Image(shape.Cookie);
        _cookieLayer = CookieLayer(cookie);
        _colour = Colour(shape) * RenderK.SpotShadowGain;
    }

    public SpotShadowShape Shape { get; }

    /// <summary>The light actor's name (map lights), matched against IgnoredEchelonLights; null for the torch.</summary>
    public string? LightName { get; init; }

    /// <summary>Where the light is and where it points (UE), as last placed.</summary>
    public (FVector Location, FRotator Rotation) Placement { get; private set; }

    /// <summary>The light's shadow viewport (rendered only while <see cref="On"/>).</summary>
    public SubViewport Viewport => _viewport;

    /// <summary>The zone the light keeps to (bAffectOwnZoneOnly), or -1 for every zone.</summary>
    public int Zone { get; set; } = -1;

    /// <summary>The light type's brightness factor this frame (FDynamicLight::Update); applied at the next <see cref="Push"/>.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Map-budget transition gain, shared by surface lighting and the light beam.</summary>
    public float VisibilityGain { get; set; } = 1f;

    /// <summary>False when every slot was taken: the light draws nothing.</summary>
    public bool HasSlot => _slot >= 0;

    /// <summary>
    /// The template slot of the map light <paramref name="lightName"/> while it is drawn (on, with a slot), else -1. Its
    /// light beam reads the slot's frustum and shadow map (MapLightBeams).
    /// </summary>
    public static int SlotOf(string lightName)
    {
        for (int i = 0; i < s_slots.Length; i++)
        {
            if (s_slots[i] is { _on: true, _disposed: false } l && string.Equals(l.LightName, lightName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public static float VisibilityOf(int slot) => slot >= 0 && slot < s_slots.Length
        ? s_slots[slot]?.VisibilityGain ?? 0f : 0f;

    /// <summary>LightType != LT_None: the shadow pass runs and the template adds the light.</summary>
    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            Push();
        }
    }

    /// <summary>
    /// The light colour of the 2x stage: the light's FDynamicLight colour, FGetHSV(hue, saturation) x brightness / 510
    /// (FDynamicLight::Update, game 0x10a1de70), scaled down so its brightest channel is at most
    /// <see cref="RenderK.SpotShadowColourCap"/> (the hue is kept). Taken as brightness / 255 before, the lights below the
    /// cap (the tutorials' brightness 150 to 200 spots, TermD's, the merc torch) drew twice as bright as the original
    /// (TSP21 spy-0 luma 1.43, MuseD merc-0 1.28). Without the cap AquaD's sun shafts
    /// (brightness 500 to 650) added three to four times the light and clipped the temple floor to white. A cap per
    /// channel turned FactD's orange window patches (hue 14 to 21, brightness 400 to 1200) white; keeping the hue
    /// matched the original's colours better on all of FactD's spy views (mean RGB error 6.5 to 5.6 on spy-0,
    /// 1.26 to 1.07 on spy-180).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0; Color = HSV x brightness / 510, game 0x10a1de70; the cap is ActorLightRules.SpotShadowColour, shared with the bots")]
    public static Vector3 Colour(SpotShadowShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        LightRgb c = ActorLightRules.SpotShadowColour(shape.Hue, shape.Saturation, shape.Brightness);
        return new Vector3(c.R, c.G, c.B);
    }

    /// <summary>Moves the light to a UE location and rotation (its axis is the rotation's forward); the camera sits at the apex.</summary>
    public void Place(FVector location, FRotator rotation)
    {
        if (!_placed || Placement != (location, rotation))
        {
            _placed = true;
            Placement = (location, rotation);
            _camera.Transform = FaithfulView.CameraTransform(location - (rotation.Vector() * _apex), rotation);
            if (_staticCamera is not null)
            {
                _staticCamera.Transform = _camera.Transform;
                _staticDirty = true;
            }
            // Bounds of the complete lit frustum, including the volume used by light beams. A shadow map outside
            // the viewing frustum cannot affect that view; keep its atlas tile and refresh it when visible again.
            _influenceBounds = Shape.InfluenceBounds(location, rotation);
        }

        Push();
    }

    /// <summary>Conservative culling of the shadow pass, keeping the light and its atlas slot intact.</summary>
    public void CullShadow(ReadOnlySpan<Plane> frustum) => _cameraVisible = ViewFrustum.Intersects(_influenceBounds, frustum);

    /// <summary>
    /// What a light's tile of the atlas holds: drawn from <paramref name="Camera"/>, with the cached static depth or
    /// not, after static pass <paramref name="StaticRender"/>, with only the static-world instances
    /// <paramref name="Static"/> (sorted, the first <paramref name="StaticCount"/>) in the frustum; null when other
    /// casters were there. <paramref name="Stamp"/> is the capture's stamp when the pass was asked for.
    /// </summary>
    private sealed record ShadowTile(Transform3D Camera, bool Combined, int StaticRender, long[]? Static, int StaticCount, long Stamp, ulong Rigid = 0)
    {
        public bool Holds(Transform3D camera, bool combined, int staticRender, long[] statics, int count, ulong rigid) =>
            Static is not null && Camera == camera && Combined == combined && StaticRender == staticRender && StaticCount == count
            && Rigid == rigid && Static.AsSpan(0, StaticCount).SequenceEqual(statics.AsSpan(0, count));
    }

    // Meshes that never change after they are built (MapBuilder's gameplay meshes): an instance of one casts the same
    // shadow until it moves, hides or takes another mesh. Held weakly, by the mesh object itself.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mesh, object> s_rigidMeshes = new();
    private static readonly object s_rigid = new();

    /// <summary>
    /// Marks a mesh whose surfaces are never rewritten or animated: its instances (without a skeleton) are rigid casters,
    /// whose shadow the shadow pass redraws only when one of them moves, hides, shows or takes another mesh. A mesh
    /// rebuilt in place (soft bodies, escalator steps), a skinned one or one with an animated cut-out must not be marked.
    /// </summary>
    public static void MarkRigid(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        s_rigidMeshes.AddOrUpdate(mesh, s_rigid);
    }

    /// <summary>SVM_SHADOW_STATS=1 prints, at most once a second per light, the caster that keeps its shadow pass live.</summary>
    private static bool Stats { get; } = System.Environment.GetEnvironmentVariable("SVM_SHADOW_STATS") == "1";
    private ulong _reportedAt;

    private void ReportLive(VisualInstance3D node)
    {
        ulong now = Time.GetTicksMsec();
        if (now - _reportedAt < 1000) return;
        _reportedAt = now;
        GD.Print($"SVM_SHADOW slot {_slot} ({LightName}) live: {node.GetClass()} {node.GetPath()} layers={node.Layers} mesh={(node as MeshInstance3D)?.Mesh?.ResourceName} skin={(node as MeshInstance3D)?.Skin is not null}");
    }

    /// <summary>SVM_SHADOW_RIGID=0 redraws a light's shadow pass whenever anything but the static world is in its frustum, as before.</summary>
    private static bool TrackRigid { get; } = System.Environment.GetEnvironmentVariable("SVM_SHADOW_RIGID") != "0";

    /// <summary>SVM_SHADOW_HALF=0 writes a tile with a moving caster every frame at any frame rate (a measurement switch).</summary>
    private static bool HalfRate { get; } = System.Environment.GetEnvironmentVariable("SVM_SHADOW_HALF") != "0";

    private static void RefreshShadowPasses()
    {
        foreach (SpotShadowLight? light in s_slots)
        {
            light?.RefreshShadowPass();
        }

        // The tiles our own pass writes this frame (Rd/RdShadowPass), before anything draws.
        Rd.RdShadowPass.Flush(s_atlas, s_staticAtlas);
    }

    /// <summary>
    /// Runs just before the frame draws, after every node has moved. The live pass is skipped when the tile already
    /// holds exactly what it would draw: the camera, the static cache and the static-world instances in the frustum are
    /// unchanged, and no other caster (a pawn, mover, animated surface or anything not known to be static) is in the
    /// frustum now or was at the tile's last pass. The atlas keeps a tile until its own capture writes it again.
    /// </summary>
    private void RefreshShadowPass()
    {
        if (_disposed || !GodotObject.IsInstanceValid(_viewport) || !GodotObject.IsInstanceValid(_camera))
        {
            return;
        }

        // Settle last frame's pass: a write replaces the tile (its stamp tells whether it took the static depth).
        if (_asked is { } asked)
        {
            _asked = null;
            long stamp = _capture.Stamp;
            _tile = stamp != asked.Stamp && asked.Static is not null ? asked with { Combined = (stamp & 1) != 0 } : null;
        }

        bool draw = _on && HasSlot && _cameraVisible && _camera.IsInsideTree();
        Transform3D camera = _camera.GlobalTransform;
        _stillFrames = camera == _lastCamera ? _stillFrames + 1 : 0;
        _lastCamera = camera;
        if (_staticViewport is not null)
        {
            _combine = _staticCapture is { Ready: true } && !_staticDirty;
            SetLiveMask();
        }

        if (_tileByRd && Rd.RdShadowPass.Failed)
        {
            // Our own pass failed: its tiles are drawn again by the viewport pass.
            _tileByRd = false;
            _tile = null;
        }

        // Our own pass draws the casters over the cached static tile, once that tile was written (a frame after the
        // static pass was scheduled) and when it can draw every caster in the frustum as Godot would.
        // A tile kept live by a moving caster (a character, a cloth) is written every other frame while the game draws
        // a hundred frames a second or more, half the tiles in each frame: its shadow is then still drawn fifty times a
        // second or more, a frame behind its caster at most. A spared frame does not look for the light's casters.
        bool rdTile = draw && Rd.RdShadowPass.Enabled && !Rd.RdShadowPass.Failed && _combine && s_staticAtlas.IsValid
            && _staticCapture is { } cache && cache.Stamp != _staticScheduledStamp;
        if (rdTile && _lastMoving && HalfRate && _tileByRd && _rdWritten is not null && _stillFrames > 0
            && Engine.GetFramesPerSecond() >= 100.0 && ((Engine.GetProcessFrames() + (ulong)_slot) & 1) == 1)
        {
            _asked = null;
            draw = false;
            rdTile = false;
        }

        if (rdTile)
        {
            object job = Rd.RdShadowPass.Begin(TileOrigin(_slot), _size, _camera);
            long[]? found = StaticCasters(camera, out int found_count, out ulong found_rigid, job, out bool moving);
            _lastMoving = found is not null && moving;
            if (found is not null)
            {
                bool held = !moving && _tile is { } now && now.Holds(camera, true, _staticRenders, found, found_count, found_rigid);
                if (held)
                {
                    Rd.RdShadowPass.Discard(job);
                }
                else
                {
                    // A tile last written by the viewport pass is copied whole.
                    if (!_tileByRd) _rdWritten = null;
                    Rd.RdShadowPass.Commit(job, ref _rdWritten);
                    _tile = moving ? null : new ShadowTile(camera, true, _staticRenders, found, found_count, _capture.Stamp, found_rigid);
                    _tileByRd = true;
                }

                _asked = null;
                draw = false;
            }
            else
            {
                Rd.RdShadowPass.Discard(job);
            }
        }

        if (draw)
        {
            long[]? statics = StaticCasters(camera, out int count, out ulong rigid);
            bool keep = statics is not null && _tile is { } tile && tile.Holds(camera, _combine, _staticRenders, statics, count, rigid);
            // Draw the static world into its cache only when the live pass runs anyway and the light has kept still. The
            // static pass draws first (child viewport); either way the tile gets the same depth: the nearest of both
            // passes, or one pass of everything.
            if (!keep && _staticViewport is not null && _staticDirty && _stillFrames >= StillFrames && _staticCapture is { Ready: true })
            {
                _staticScheduledStamp = _staticCapture.Stamp;
                _staticViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                _staticDirty = false;
                _staticRenders++;
                _combine = true;
                SetLiveMask();
                statics = StaticCasters(camera, out count, out rigid);
            }

            if (keep)
            {
                draw = false;
            }
            else
            {
                _asked = new ShadowTile(camera, _combine, _staticRenders, statics, count, _capture.Stamp, rigid);
                _tileByRd = false;
            }
        }

        // SVM_RD_SHADOWS=verify: now and then our own pass draws the tile the viewport pass draws this frame, to compare.
        if (draw && Rd.RdShadowPass.Verify && _combine && s_staticAtlas.IsValid && _staticCapture is { } cached
            && cached.Stamp != _staticScheduledStamp && Engine.GetProcessFrames() % 150 == 100)
        {
            object job = Rd.RdShadowPass.Begin(TileOrigin(_slot), _size, _camera);
            if (StaticCasters(camera, out _, out _, job, out _) is not null) Rd.RdShadowPass.CommitVerify(job, $"slot {_slot} ({LightName})");
            else
            {
                Rd.RdShadowPass.Discard(job);
                GD.Print($"SVM_RD_SHADOW verify slot {_slot} ({LightName}): a caster our pass cannot draw");
            }
        }

        if (_liveDrawn != draw)
        {
            _liveDrawn = draw;
            _viewport.RenderTargetUpdateMode = draw ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        }
    }

    /// <summary>The live camera leaves the static world out while it takes the cached static depth.</summary>
    private void SetLiveMask()
    {
        uint mask = _combine ? RenderLayers.World | RenderLayers.ShadowPass : RenderLayers.SpotShadow;
        if (_camera.CullMask != mask)
        {
            _camera.CullMask = mask;
        }
    }

    /// <summary>
    /// The static-world instances the live camera draws (sorted ids, the first <paramref name="count"/>) and a
    /// fingerprint of the rigid casters in its frustum (<see cref="MarkRigid"/>: which, where, with which mesh), or null
    /// when anything else it draws is in its frustum. The search uses the instance bounds Godot culls the camera with.
    /// </summary>
    // Instances known to be merged map geometry alone (layers within StaticWorldAll), with those layers: instance ids
    // are not used again within a run of the game.
    private static readonly Dictionary<long, uint> s_staticLayers = [];

    private long[]? StaticCasters(Transform3D camera, out int count, out ulong rigid) =>
        StaticCasters(camera, out count, out rigid, null, out _);

    /// <summary>
    /// As above, for our own shadow pass when <paramref name="job"/> is given (Rd/RdShadowPass): every caster that is not
    /// static world is added to the job, and null is returned only when the pass cannot draw one of them.
    /// <paramref name="moving"/> is set when a caster's shadow can change while this fingerprint stays (a skeleton, a
    /// mesh not marked rigid): the tile is then drawn every frame.
    /// </summary>
    private long[]? StaticCasters(Transform3D camera, out int count, out ulong rigid, object? job, out bool moving)
    {
        count = 0;
        rigid = 0;
        moving = false;
        if (_camera.GetWorld3D() is not { } world)
        {
            return null;
        }

        if (_searchPlanes is null || _searchFor != camera)
        {
            Godot.Collections.Array<Plane> planes = _camera.GetFrustum();
            if (planes.Count != 6)
            {
                return null;
            }

            // Godot's frustum planes face outwards: moving each out widens the search.
            for (int i = 0; i < planes.Count; i++)
            {
                Plane p = planes[i];
                planes[i] = new Plane(p.Normal, p.D + CasterMargin);
            }

            _searchPlanes = planes;
            _searchFor = camera;
        }

        long[] ids = RenderingServer.InstancesCullConvex(_searchPlanes, world.Scenario);
        uint drawn = _camera.CullMask & ~RenderLayers.ShadowPass;
        int kept = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            long id = ids[i];
            // The merged map geometry is most of what a light's frustum holds, and it never changes its layers: once
            // known it is passed over without asking the engine for its node.
            if (s_staticLayers.TryGetValue(id, out uint known))
            {
                if ((known & drawn) != 0) ids[kept++] = id;
                continue;
            }

            // An instance without a node (or one being freed) may change unseen: draw the pass.
            if (GodotObject.InstanceFromId((ulong)id) is not VisualInstance3D node)
            {
                return null;
            }

            // Lights, decals and probes write no depth.
            if (node is not GeometryInstance3D)
            {
                continue;
            }

            uint layers = node.Layers & drawn;
            if (layers == 0)
            {
                continue;
            }

            // Only the merged map geometry is fixed (RenderLayers.StaticWorld, the static cache's own premise). A rigid
            // caster keeps its shadow while it keeps its place, its mesh and its layers; anything else may move, animate
            // or change unseen.
            if ((layers & ~RenderLayers.StaticWorldAll) != 0)
            {
                bool tracked = TrackRigid && node is MeshInstance3D { Mesh: { } marked, Skin: null } && s_rigidMeshes.TryGetValue(marked, out _)
                    && node.IsVisibleInTree();
                if (job is not null)
                {
                    bool moves = !tracked;
                    if (node is not MeshInstance3D caster || !caster.IsVisibleInTree() || !Rd.RdShadowPass.TryAdd(job, caster, ref moves))
                    {
                        if (Stats) ReportLive(node);
                        return null;
                    }

                    moving |= moves;
                    if (!tracked) continue;
                }

                if (!tracked || node is not MeshInstance3D { Mesh: { } mesh } instance)
                {
                    if (Stats) ReportLive(node);
                    return null;
                }

                // Order free (the search returns instances in no fixed order): each caster's own hash, summed.
                Transform3D at = instance.GlobalTransform;
                var hash = new HashCode();
                hash.Add(id);
                hash.Add(layers);
                hash.Add(mesh.GetRid());
                hash.Add(at.Basis.X);
                hash.Add(at.Basis.Y);
                hash.Add(at.Basis.Z);
                hash.Add(at.Origin);
                rigid += (ulong)(uint)hash.ToHashCode() * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
                continue;
            }

            if ((node.Layers & ~RenderLayers.StaticWorldAll) == 0)
            {
                if (s_staticLayers.Count > 200_000) s_staticLayers.Clear();
                s_staticLayers[id] = node.Layers;
            }

            ids[kept++] = id;
        }

        Array.Sort(ids, 0, kept);
        count = kept;
        return ids;
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
        var state = (live, Placement.Location, Placement.Rotation, Scale, VisibilityGain, Zone, SoftRadius);
        if (_lastPush == state)
        {
            return;
        }

        _lastPush = state;
        if (!live)
        {
            SetRow(_slot, 3, Vector4.Zero);
            SetRow(_slot, 8, Vector4.Zero);
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
        SetRow(_slot, 3, new Vector4(Shape.Dynamic ? 2f : 1f, range, Shape.NormalInfluence, Shape.Attenuation));
        Vector3 c = _colour * (Scale * VisibilityGain);
        SetRow(_slot, 4, new Vector4(c.X, c.Y, c.Z, UeSpace.LengthToGodot(_apex)));
        float aspect = _size.X / (float)_size.Y;
        Vector4 frustum;
        if (_parallel)
        {
            // Parallel rays: x / SpotWidth, y / SpotHeight span -1..1 (editor 0x111a5480, scale 1 / SpotWidth).
            float h = UeSpace.LengthToGodot(Shape.SpotHeight);
            frustum = new Vector4(1f / (h * aspect), 1f / h, 1f, _cookieLayer);
        }
        else
        {
            // x scale apex / (p x SpotWidth) over the depth from the apex: +-SpotWidth at the far plane (editor 0x111a5480).
            float tanH = Shape.SpotHeight / (_apex + Shape.Range);
            frustum = new Vector4(1f / (tanH * aspect), 1f / tanH, 0f, _cookieLayer);
        }

        SetRow(_slot, 5, frustum);
        Vector2I origin = TileOrigin(_slot);
        SetRow(_slot, 6, new Vector4(origin.X / (float)AtlasWidth, origin.Y / (float)AtlasHeight, _size.X / (float)AtlasWidth,
            _size.Y / (float)AtlasHeight));
        SetRow(_slot, 7, new Vector4(Zone, UeSpace.LengthToGodot(2f), 0.01f, SoftRadius));
        SetRow(_slot, 8, LitSphere(_camera.GlobalTransform, UeSpace.LengthToGodot(_apex), range, frustum));
        Flush();
    }

    /// <summary>
    /// A sphere (centre, radius squared) around every point the template can light from a slot: depth from the apex
    /// within (apex, apex + range) and the clip position within -1..1, the convex hull of the frustum's eight corners.
    /// The margin is far above the float error of the template's own tests, so the sphere only rejects points they reject.
    /// </summary>
    private static Vector4 LitSphere(Transform3D toWorld, float near, float range, Vector4 frustum)
    {
        float far = near + range;
        bool parallel = frustum.Z > 0.5f;
        float nx = parallel ? 1f / frustum.X : near / frustum.X;
        float ny = parallel ? 1f / frustum.Y : near / frustum.Y;
        float fx = parallel ? 1f / frustum.X : far / frustum.X;
        float fy = parallel ? 1f / frustum.Y : far / frustum.Y;
        // The axis point as far from the near corners as from the far ones (clamped to the span).
        float z = Math.Clamp(((fx * fx) + (fy * fy) - (nx * nx) - (ny * ny) + (far * far) - (near * near)) / (2f * range), near, far);
        Vector3 centre = toWorld * new Vector3(0f, 0f, -z);
        float radius = 0f;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                radius = MathF.Max(radius, centre.DistanceTo(toWorld * new Vector3(x * nx, y * ny, -near)));
                radius = MathF.Max(radius, centre.DistanceTo(toWorld * new Vector3(x * fx, y * fy, -far)));
            }
        }

        radius = (radius * 1.001f) + 0.05f;
        return new Vector4(centre.X, centre.Y, centre.Z, radius * radius);
    }

    /// <summary>
    /// Row 8 of a slot the template visits this frame (on in svm_spot_bits): the sphere (centre, radius squared) outside
    /// which the slot adds exactly nothing. Zero for a slot it skips (<see cref="LightReach"/>).
    /// </summary>
    internal static Vector4 ReachSphere(int slot) =>
        slot < 0 || slot >= Math.Min(s_slots.Length, BitSlots) || s_rowValues[(slot * SlotTexels) + 3].X < 0.5f
            ? Vector4.Zero : s_rowValues[(slot * SlotTexels) + 8];

    /// <summary>
    /// The template's own tests of a slot (svm_spot_slot: the lit span along the light's axis and the sides of its
    /// frustum) against a world box: false only when no point of the box passes them all, so the slot adds exactly
    /// nothing anywhere in it. Tighter than <see cref="ReachSphere"/>, which holds the whole frustum.
    /// </summary>
    internal static bool ReachesBox(int slot, in Aabb box)
    {
        if (slot < 0 || slot >= Math.Min(s_slots.Length, BitSlots)) return false;
        int at = slot * SlotTexels;
        Vector4 r0 = s_rowValues[at], r1 = s_rowValues[at + 1], r2 = s_rowValues[at + 2];
        Vector4 a = s_rowValues[at + 3], c = s_rowValues[at + 4], f = s_rowValues[at + 5];
        float near = c.W, far = c.W + a.Y;
        if (!float.IsFinite(r0.X + r0.Y + r0.Z + r0.W + r1.X + r1.Y + r1.Z + r1.W + r2.X + r2.Y + r2.Z + r2.W + near + far + f.X + f.Y)) return true;
        bool parallel = f.Z > 0.5f;
        // A point is lit only where all six are above zero; each is linear, so its largest value over the box is at a corner.
        Span<float> best = stackalloc float[6];
        best.Fill(float.NegativeInfinity);
        Vector3 min = box.Position, max = box.End;
        for (int corner = 0; corner < 8; corner++)
        {
            var p = new Vector3((corner & 1) != 0 ? max.X : min.X, (corner & 2) != 0 ? max.Y : min.Y, (corner & 4) != 0 ? max.Z : min.Z);
            float x = (r0.X * p.X) + (r0.Y * p.Y) + (r0.Z * p.Z) + r0.W;
            float y = (r1.X * p.X) + (r1.Y * p.Y) + (r1.Z * p.Z) + r1.W;
            float z = -((r2.X * p.X) + (r2.Y * p.Y) + (r2.Z * p.Z) + r2.W);
            float side = parallel ? 1f : z;
            best[0] = MathF.Max(best[0], z - near);
            best[1] = MathF.Max(best[1], far - z);
            best[2] = MathF.Max(best[2], side - (x * f.X));
            best[3] = MathF.Max(best[3], side + (x * f.X));
            best[4] = MathF.Max(best[4], side - (y * f.Y));
            best[5] = MathF.Max(best[5], side + (y * f.Y));
        }

        // A margin for the rounding of the template's own arithmetic on the GPU.
        float margin = 0.01f + (1e-4f * MathF.Abs(far)) * MathF.Max(1f, MathF.Max(MathF.Abs(f.X), MathF.Abs(f.Y)));
        foreach (float value in best)
        {
            if (value < -margin) return false;
        }

        return true;
    }

    /// <summary>The zone a slot's light is limited to (row 7 x), or below 0 when it lights every zone.</summary>
    internal static float OwnZone(int slot) =>
        slot < 0 || slot >= Math.Min(s_slots.Length, BitSlots) ? -1f : s_rowValues[(slot * SlotTexels) + 7].X;

    /// <summary>The slots <see cref="ReachSphere"/> reads.</summary>
    internal const int ReachSlots = RenderK.SpotShadowSlots;

    private static Vector2I TileOrigin(int slot) =>
        slot < 0 ? Vector2I.Zero : new Vector2I(slot % AtlasColumns * TileWidth, slot / AtlasColumns * TileHeight);

    private static void SetRow(int slot, int texel, Vector4 v)
    {
        int index = slot * SlotTexels + texel;
        if (s_rowValues[index] == v)
        {
            return;
        }

        s_rowValues[index] = v;
        s_rowsDirty = true;
        s_rows!.SetPixel(texel, slot, new Color(v.X, v.Y, v.Z, v.W));
    }

    private static void Flush()
    {
        if (s_ignoreDirty)
        {
            RebuildIgnore();
        }

        if (s_rowsDirty)
        {
            s_rowsTexture?.Update(s_rows);
            s_rowsDirty = false;
        }
        int count = 0;
        for (int i = 0; i < s_slots.Length; i++)
        {
            if (s_slots[i] is not null)
            {
                count = i + 1;
            }
        }

        if (count != s_lastCount)
        {
            s_lastCount = count;
            ShaderGlobals.Set(s_countName, count);
        }

        // The template's slot loop visits only the slots below the count that row 3 marks on, as its own tests do.
        int bits = 0;
        for (int i = 0; i < Math.Min(count, BitSlots); i++)
        {
            float on = s_rowValues[(i * SlotTexels) + 3].X;
            if (!(on < 0.5f))
            {
                bits |= 1 << i;
            }

            if (on > 1.5f)
            {
                bits |= 1 << (i + BitSlots);
            }
        }

        if (bits != s_lastBits)
        {
            s_lastBits = bits;
            ShaderGlobals.Set(s_bitsName, bits);
        }
    }

    /// <summary>Registers the template's spot slots (once per process), with the attenuation ramps of the pack.</summary>
    public static void EnsureGlobals(RenderPack? pack = null)
    {
        if (!s_globalsAdded)
        {
            s_rows = Image.CreateEmpty(SlotTexels, RenderK.SpotShadowSlots, false, Image.Format.Rgbaf);
            s_rowsTexture = ImageTexture.CreateFromImage(s_rows);
            s_noDepth = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rf));
            ShaderGlobals.Add(ParamSlots, RenderingServer.GlobalShaderParameterType.Sampler2D, s_rowsTexture);
            ShaderGlobals.Add(ParamCount, RenderingServer.GlobalShaderParameterType.Int, 0);
            ShaderGlobals.Add(ParamDepth, RenderingServer.GlobalShaderParameterType.Sampler2D, s_noDepth);
            ShaderGlobals.Add(ParamCookies, RenderingServer.GlobalShaderParameterType.Sampler2Darray, Cookies());
            ShaderGlobals.Add(ParamAttenuation, RenderingServer.GlobalShaderParameterType.Sampler2D, Ramps(null));
            s_ignore = Image.CreateEmpty(RenderK.SpotShadowSlots, 1, false, Image.Format.Rf);
            s_ignoreTexture = ImageTexture.CreateFromImage(s_ignore);
            ShaderGlobals.Add(ParamIgnore, RenderingServer.GlobalShaderParameterType.Sampler2D, s_ignoreTexture);
            ShaderGlobals.Add(ParamBits, RenderingServer.GlobalShaderParameterType.Int, 0);
            RenderingServer.FramePreDraw += RefreshShadowPasses;
            s_globalsAdded = true;
        }

        if (pack is not null)
        {
            ShaderGlobals.Set(ParamAttenuation, Ramps(pack));
        }
    }

    /// <summary>
    /// Sets the map's IgnoredEchelonLights groups (<see cref="RenderMap.EchelonGroups"/>): surfaces of group g skip the
    /// slots whose light group g names (game 0x10a37310).
    /// </summary>
    public static void SetIgnoreGroups(IReadOnlyList<IReadOnlySet<string>> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        EnsureGlobals();
        s_ignoreGroups = groups;
        RebuildIgnore();
    }

    private static void RebuildIgnore()
    {
        s_ignoreDirty = false;
        if (!s_globalsAdded)
        {
            return;
        }

        int rows = Math.Max(1, s_ignoreGroups.Count);
        var image = Image.CreateEmpty(RenderK.SpotShadowSlots, rows, false, Image.Format.Rf);
        for (int slot = 0; slot < s_slots.Length; slot++)
        {
            if (s_slots[slot]?.LightName is not { } name)
            {
                continue;
            }

            for (int g = 1; g < s_ignoreGroups.Count; g++)
            {
                if (s_ignoreGroups[g].Contains(name))
                {
                    image.SetPixel(slot, g, new Color(1f, 0f, 0f));
                }
            }
        }

        if (s_ignore is not null && s_ignore.GetSize() == image.GetSize())
        {
            s_ignore = image;
            s_ignoreTexture!.Update(image);
        }
        else
        {
            s_ignore = image;
            s_ignoreTexture = ImageTexture.CreateFromImage(image);
            ShaderGlobals.Set(ParamIgnore, s_ignoreTexture);
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
    private static int CookieLayer(Image? cookie)
    {
        if (cookie is null)
        {
            return 0;
        }

        if (s_cookieLayers.TryGetValue(cookie, out int layer))
        {
            return layer;
        }

        if (cookie.IsEmpty()) return 0;
        var image = (Image)cookie.Duplicate();
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
        {
            image.Dispose();
            return 0;
        }

        image.ClearMipmaps();
        image.Convert(Image.Format.Rgba8);
        image.Resize(RenderK.SpotCookieSize, RenderK.SpotCookieSize, Image.Interpolation.Bilinear);
        s_cookies.Add(image);
        s_cookieLayers.Add(cookie, s_cookies.Count);
        ShaderGlobals.Set(s_cookiesName, Cookies());
        return s_cookies.Count;
    }

    /// <summary>The cookies as one array, each resampled to <see cref="RenderK.SpotCookieSize"/> square RGBA8.</summary>
    private static Texture2DArray Cookies()
    {
        int size = RenderK.SpotCookieSize;
        var images = new Godot.Collections.Array<Image>();
        using Image white = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        white.Fill(new Color(1f, 1f, 1f, 1f));
        images.Add(white);
        foreach (Image image in s_cookies)
        {
            images.Add(image);
        }

        var array = new Texture2DArray();
        array.CreateFromImages(images);
        s_cookieTexture = array;
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
                    // Written by the depth copies (storage) and by Rd/RdShadowPass (a copy of the static tile, then draws).
                    UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit
                        | RenderingDevice.TextureUsageBits.ColorAttachmentBit | RenderingDevice.TextureUsageBits.CanCopyToBit
                        | RenderingDevice.TextureUsageBits.CanCopyFromBit,
                }, new RDTextureView());
            }

            s_atlasPending = false;
        }));
    }

    private static void EnsureStaticAtlas()
    {
        if (s_staticAtlas.IsValid || s_staticAtlasPending) return;
        s_staticAtlasPending = true;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            RenderingDevice? rd = RenderingServer.GetRenderingDevice();
            if (rd is not null)
            {
                using var format = new RDTextureFormat
                {
                    Format = RenderingDevice.DataFormat.R32Sfloat,
                    Width = (uint)AtlasWidth, Height = (uint)AtlasHeight,
                    UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit
                        | RenderingDevice.TextureUsageBits.CanCopyFromBit,
                };
                using var view = new RDTextureView();
                s_staticAtlas = rd.TextureCreate(format, view);
            }

            s_staticAtlasPending = false;
        }));
    }

    /// <summary>The atlas, once made, goes to the template's depth slot.</summary>
    private static void BindAtlas()
    {
        Rid atlas = s_atlas;
        if (atlas.IsValid && s_atlasTexture.TextureRdRid != atlas)
        {
            s_atlasTexture.TextureRdRid = atlas;
            ShaderGlobals.Set(s_depthName, s_atlasTexture);
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
            SetRow(_slot, 8, Vector4.Zero);
            s_slots[_slot] = null;
            s_ignoreDirty = true;
            Flush();
        }

        _viewport.QueueFree();
        // The effect is a resource the managed side keeps alive: without the garbage collector running before the
        // process quits its predelete never comes, so its render device objects are freed here.
        _capture.Release();
        _staticCapture?.Release();
        _searchPlanes = null;
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
            ShaderGlobals.Set(s_depthName, s_noDepth);
        }

        s_atlasTexture.TextureRdRid = default;
        Rd.RdShadowPass.Release();
        s_cookieLayers.Clear();
        foreach (Image image in s_cookies) image.Dispose();
        s_cookies.Clear();
        ShaderGlobals.Set(s_cookiesName, Cookies());
        Rid atlas = s_atlas;
        Rid staticAtlas = s_staticAtlas;
        s_atlas = default;
        s_staticAtlas = default;
        if (atlas.IsValid)
        {
            RenderingServer.CallOnRenderThread(Callable.From(() => RenderingServer.GetRenderingDevice()?.FreeRid(atlas)));
        }
        if (staticAtlas.IsValid)
        {
            RenderingServer.CallOnRenderThread(Callable.From(() => RenderingServer.GetRenderingDevice()?.FreeRid(staticAtlas)));
        }
    }
}
