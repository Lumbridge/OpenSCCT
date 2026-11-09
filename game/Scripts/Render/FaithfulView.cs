using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Camera;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render;

/// <summary>
/// The Faithful view of a <see cref="RenderMap"/>: a camera placed from a UE camera (location, rotation, FovAngle), an environment that leaves colours alone (Linear tonemap, exposure 1, no glow, AO, fog or auto exposure), the
/// display-space post pass, and the sky zone drawn behind the world as seen from the SkyZoneInfo: in the main view
/// itself, or, for a sky zone fogged solid, once through a small SubViewport.
/// Add it, untransformed, under the node whose viewport it should draw in. docs/RENDERING.md#view.
/// </summary>
[PortOf("eng:Render.View", Spec = "docs/RENDERING.md#view")]
public partial class FaithfulView : Node3D
{
    public const string BackdropShaderPath = "res://Shaders/svm_sky_backdrop.gdshader";


    // The sky view exists only for a sky zone that is fog in every direction (FoggedSkyZone): its picture is drawn
    // once, small, and kept.
    private SubViewport? _skyViewport;
    private Camera3D? _skyCamera;
    // Every other sky zone is drawn by the main camera: its nodes, their places in their cell's surface order and their
    // last sort offsets.
    private bool _skyMerged;

    /// <summary>The main view draws the sky zone's geometry this frame (not a sky fogged solid, not thermal or EMF).</summary>
    internal bool SkyInMainView => _skyMerged && !IsSceneVision(_rendMap);
    private Vector3 _skyOrigin;
    private readonly System.Collections.Generic.List<GeometryInstance3D> _skyNodes = [];
    private readonly System.Collections.Generic.List<int> _skyOrder = [];
    private readonly System.Collections.Generic.List<Vector3> _skyCentre = [];
    private float[] _skySort = [];
    private SurfaceFogRegistry? _surfaceFog;
    private SurfaceFogRegistry? _skySurfaceFog;
    private MeshInstance3D? _backdrop;
    private MeshInstance3D? _smokeBackdrop;
    private FVector _cameraLocation;
    private FRotator _cameraRotation;
    private float _fov = 90f;
    private Vector2I _projectedFor;
    private Coronas? _coronas;
    private RainVolumes? _rain;
    private bool _cut = true;

    public FaithfulView()
    {
        Name = "FaithfulView";
        Camera = new Camera3D
        {
            Name = "Camera",
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = UeSpace.LengthToGodot(RenderK.NearClipUnits),
            Far = UeSpace.LengthToGodot(RenderK.FarClipUnits),
            Environment = CreateEnvironment(),
            Attributes = CreateAttributes(),
            CullMask = RenderLayers.MainView,
        };
        Effect = new DisplaySpaceEffect();
        AmbientOcclusion = new AmbientOcclusionEffect();
        Remanence = new RemanenceEffect();
        DoubleVision = new RemanenceEffect { DoubleVision = true };
        CoronaSamples = new CoronaSampleEffect(Camera);
        Camera.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { CoronaSamples, AmbientOcclusion, Effect, DoubleVision, Remanence } };
        AddChild(Camera);
        _surfaceFog = new SurfaceFogRegistry(Camera, main: true);
    }

    public Camera3D Camera { get; }

    public DisplaySpaceEffect Effect { get; }

    public AmbientOcclusionEffect AmbientOcclusion { get; }

    /// <summary>The flash-bang after-image over the shown frame (spec/equipment/flash-bang.md rule 5).</summary>
    public RemanenceEffect Remanence { get; }

    public RemanenceEffect DoubleVision { get; }

    /// <summary>The scene colour and depth under this frame's coronas, which they read instead of full-screen copies.</summary>
    public CoronaSampleEffect CoronaSamples { get; }

    /// <summary>Prepare the live camera's smoke surface while the loading gate covers the view.</summary>
    public void PreloadSmokeBackground()
    {
        if (_smokeBackdrop is not null) return;
        _smokeBackdrop = ResponsiveSmokeRenderer.CreateBackground(Camera);
        _smokeBackdrop.Visible = false;
    }

    public bool SmokeBackgroundPrepared => _smokeBackdrop is not null;

    public RenderMap? Map { get; private set; }

    /// <summary>The sky zone's SubViewport, when the map has a sky zone fogged solid (else the main view draws it).</summary>
    public SubViewport? SkyViewport => _skyViewport;

    private int _rendMap = RenderK.RendMapNormal;
    private float _squintingFactor;

    /// <summary>The simulation's merc eye adaptation; changes scene lighting without changing vision mode.</summary>
    public float SquintingFactor
    {
        get => _squintingFactor;
        set
        {
            if (_squintingFactor == value) return;
            _squintingFactor = value;
            Materials.MaterialCompiler.SetVision(_rendMap, value);
        }
    }
    private CanvasLayer? _overlayLayer;
    private TextureRect? _overlayRect;
    private bool _binocularsOn;

    /// <summary>
    /// PlayerController.RendMap of the view: 5 normal, 34 night, 36 thermal, 37 motion, 38 EMF
    /// (docs/RENDERING.md#vision-modes). Thermal and EMF change what every Faithful material draws (a process-wide
    /// global), the post pass draws the mode, and thermal and EMF hide coronas and show the vision-only nodes (laser
    /// beams) of the map.
    /// </summary>
    public int RendMap
    {
        get => _rendMap;
        set
        {
            _rendMap = value;
            ApplyVision();
        }
    }

    /// <summary>
    /// The motion tracker's clean view rectangles this frame, as parts of the picture (0..1): the post pass redraws the
    /// scene inside them in clean monochrome (RendMap 37 only).
    /// </summary>
    public Rect2[] MotionCleanRects { get; set; } = [];

    /// <summary>SBasePawn.bBinocularsOn: shows <see cref="BinocularsOverlay"/> over the view (the HUD draws it; mode 50 keeps the vision mode under it).</summary>
    public bool BinocularsOn
    {
        get => _binocularsOn;
        set
        {
            _binocularsOn = value;
            EnsureOverlay();
            _overlayRect!.Visible = value && _overlayRect.Texture is not null;
        }
    }

    /// <summary>The binocular page (the HUD's texture), drawn at 4:3 in the middle of the view while <see cref="BinocularsOn"/>.</summary>
    public Texture2D? BinocularsOverlay
    {
        get => _overlayRect?.Texture;
        set
        {
            EnsureOverlay();
            _overlayRect!.Texture = value;
            _overlayRect.Visible = _binocularsOn && value is not null;
        }
    }

    /// <summary>The glow composite's dark gate.</summary>
    public bool GlowGate { get; set; } = true;

    /// <summary>The zone the camera is in, from the last <see cref="SetUeCamera"/>.</summary>
    public ZoneEntry? CameraZone { get; private set; }

    /// <summary>
    /// Replaces <see cref="VerticalFov"/> for the camera: (FovAngle in degrees, screen width over height) to the
    /// vertical angle in degrees. The options set it from the widescreen settings (Options/ViewFov); null keeps Hor+.
    /// </summary>
    public Func<float, float, float>? VerticalFovPolicy
    {
        get => _verticalFovPolicy;
        set
        {
            _verticalFovPolicy = value;
            _projectedFor = default;
        }
    }

    private Func<float, float, float>? _verticalFovPolicy;

    /// <summary>
    /// An environment that passes colours through: the frame already holds display values and the post pass
    /// linearizes them, so everything Godot would add on top is off.
    /// </summary>
    public static Godot.Environment CreateEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = new Color(0f, 0f, 0f),
        AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
        ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
        TonemapMode = Godot.Environment.ToneMapper.Linear,
        TonemapExposure = 1f,
        GlowEnabled = false,
        SsaoEnabled = false,
        SsilEnabled = false,
        SsrEnabled = false,
        SdfgiEnabled = false,
        FogEnabled = false,
        VolumetricFogEnabled = false,
        AdjustmentEnabled = false,
    };

    public static CameraAttributesPractical CreateAttributes() => new()
    {
        AutoExposureEnabled = false,
        ExposureMultiplier = 1f,
        DofBlurFarEnabled = false,
        DofBlurNearEnabled = false,
    };

    /// <summary>Shows <paramref name="map"/> and sets up its sky zone when it has one.</summary>
    public void Attach(RenderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Map = map;
        // MapBuilder decided this already; the shown map keeps it if another map was built since.
        Materials.ResponsiveSmokeShaders.UsePortalFog(PortalFogFrame.MapUses(map.Lighting));
        if (map.Lighting.OutdoorSky)
        {
            Camera.Environment.BackgroundMode = Godot.Environment.BGMode.Sky;
            Camera.Environment.Sky = new Sky
            {
                SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/hub_sky.gdshader") },
            };
            string panorama = System.IO.Path.Combine(map.Folder, "render", "scenery", "autumn_field_puresky.hdr");
            if (System.IO.File.Exists(panorama))
            {
                using var image = Image.LoadFromFile(panorama);
                var sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/hub_panorama.gdshader") };
                sky.SetShaderParameter("panorama", ImageTexture.CreateFromImage(image));
                Camera.Environment.Sky.SkyMaterial = sky;
            }
        }
        _coronas = new Coronas(map, map.Root, CoronaSamples);
        _rain = new RainVolumes(map, map.Root);
        ApplyVision();
        if (map.Lighting.SkyLocation is not { } skyLocation)
        {
            return;
        }

        _skyOrigin = UeSpace.ToGodot(skyLocation);
        // Every pixel of a sky zone fogged solid is the same value, so the 16 x 16 target drawn once (ApplyVision)
        // holds exactly what each pixel of a full-size view would, through the same pipeline.
        if (!FoggedSkyZone.IsSolid(map, _skyOrigin))
        {
            AttachSkyToMainView(map);
            return;
        }

        _skyViewport = new SubViewport
        {
            Name = "SkyZone",
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Disabled,
            // Sky materials already write display values. Retain them through the offscreen target
            // so the backdrop can sample them without an sRGB decode pass followed by an encode.
            UseHdr2D = true,
            Size = new Vector2I(16, 16),
        };
        _skyCamera = new Camera3D
        {
            Name = "SkyCamera",
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = Camera.Near,
            Far = Camera.Far,
            Environment = CreateEnvironment(),
            Attributes = CreateAttributes(),
            CullMask = RenderLayers.Sky,
            Current = true,
        };
        _skyViewport.AddChild(_skyCamera);
        _skySurfaceFog = new SurfaceFogRegistry(_skyCamera);
        SetSkyGlobals();
        // The camera only turns, so place it now: the one draw may come before the first SetUeCamera.
        _skyCamera.Transform = CameraTransform(skyLocation, _cameraRotation);
        GD.Print($"SVM_SKY {map.MapId}: sky zone {map.Lighting.SkyZone} is fogged solid, its view is drawn once");

        AddChild(_skyViewport);
        Diagnostics.ViewportTimings.Register(_skyViewport, Diagnostics.ViewportTimings.Sky);
        ApplyVision();

        // Lowest render priority: the backdrop never draws (or sorts) after a corona or a particle over the sky.
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(BackdropShaderPath), RenderPriority = (int)Material.RenderPriorityMin };
        material.SetShaderParameter("sky_view", _skyViewport.GetTexture());
        _backdrop = new MeshInstance3D
        {
            Name = "SkyBackdrop",
            Mesh = new QuadMesh { Size = Vector2.One },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384f,
            Layers = RenderLayers.Backdrop,
        };
        Camera.AddChild(_backdrop);
    }

    // A box this much bigger on every side holds every camera of the map, so the main camera never culls a sky
    // surface by its place in the world (metres; the old backdrop's margin).
    private const float SkyCullMargin = 16384f;

    /// <summary>
    /// Node meta (int) on a transparent sky surface split out of a cell mesh (MapBuilder): its place in the cell's
    /// surface order, the order the sky camera drew the cell's surfaces in at their one shared sort distance.
    /// </summary>
    public const string SkySortOrderMeta = "svm_sky_order";

    // Each later surface of a cell sorts this much nearer (metres): far below any distance between two cells, far above
    // the float rounding of the sort distance.
    private const float SkySortStep = 0.001f;

    /// <summary>
    /// The sky zone drawn by the main camera as the sky camera saw it (UE2's sky pass, in the same frame buffer): the
    /// sky materials (MaterialCompiler.GetSky) place each surface from the SkyZoneInfo with this camera's rotation and
    /// projection, behind the whole world, and shade it with the sky's own fog slot. No second scene render.
    /// </summary>
    private void AttachSkyToMainView(RenderMap map)
    {
        _skyMerged = true;
        _skySurfaceFog = new SurfaceFogRegistry();
        SetSkyGlobals();
        CollectSky(map.Root);
        _skySort = new float[_skyNodes.Count];
        Array.Fill(_skySort, float.NaN);
        GD.Print($"SVM_SKY {map.MapId}: sky zone {map.Lighting.SkyZone} is drawn in the main view ({_skyNodes.Count} nodes)");
        ApplyVision();
        UpdateSkySort();
    }

    private void CollectSky(Node node)
    {
        if (node is GeometryInstance3D geometry && (geometry.Layers & RenderLayers.Sky) != 0)
        {
            geometry.ExtraCullMargin = SkyCullMargin;
            geometry.IgnoreOcclusionCulling = true;
            // Transparent surfaces then sort by the distance to the node's origin, which UpdateSkySort turns into the
            // distance the sky camera sorted them by.
            geometry.SortingUseAabbCenter = false;
            _skyNodes.Add(geometry);
            _skyOrder.Add(geometry.HasMeta(SkySortOrderMeta) ? geometry.GetMeta(SkySortOrderMeta).AsInt32() : 0);
            // Sky meshes never change, so their box centres are read once, not on every UpdateSkySort.
            _skyCentre.Add(LocalSortCentre(geometry));
        }

        foreach (Node child in node.GetChildren())
        {
            CollectSky(child);
        }
    }

    /// <summary>
    /// The sky camera sorted transparent sky surfaces far to near by its distance to each node's box centre (Godot
    /// 4.7.2 render_forward_clustered.cpp: depth = |eye - centre| - sorting offset, sort_by_reverse_depth_and_priority),
    /// and the surfaces of one cell at that one distance in surface order. Each node's SortingOffset makes the main
    /// camera's depth that same distance, less a step per place in its cell (Godot's sort keeps the order of equal
    /// depths only up to 16 transparent surfaces), so the sky keeps its order; its lowest render priority draws it
    /// before every other transparent surface.
    /// </summary>
    private void UpdateSkySort()
    {
        if (!_skyMerged || !Camera.IsInsideTree())
        {
            return;
        }

        // The eye the renderer sorts by (Camera3D.get_camera_transform).
        Vector3 eye = Camera.GetCameraTransform().Origin;
        for (int i = 0; i < _skyNodes.Count; i++)
        {
            GeometryInstance3D node = _skyNodes[i];
            if (!IsInstanceValid(node) || !node.IsInsideTree())
            {
                continue;
            }

            Transform3D transform = node.GlobalTransform;
            float offset = eye.DistanceTo(transform.Origin) - _skyOrigin.DistanceTo(transform * _skyCentre[i]) +
                _skyOrder[i] * SkySortStep;
            if (offset != _skySort[i])
            {
                _skySort[i] = offset;
                node.SortingOffset = offset;
            }
        }
    }

    /// <summary>The centre of the box the renderer sorts a node by: its mesh's custom box when it has one.</summary>
    private static Vector3 LocalSortCentre(GeometryInstance3D node) => node switch
    {
        MeshInstance3D { Mesh: ArrayMesh { CustomAabb: var custom } } when custom != default => custom.GetCenter(),
        MeshInstance3D { Mesh: PrimitiveMesh { CustomAabb: var custom } } when custom != default => custom.GetCenter(),
        _ => node.GetAabb().GetCenter(),
    };

    /// <summary>The sky camera's layers: the sky layer and the fog slot of its scene.</summary>
    private uint SkyLayers => _skyCamera?.CullMask ?? (RenderLayers.Sky | (_skySurfaceFog?.Layers ?? 0u));

    private static readonly StringName s_skyOriginName = Materials.MaterialCompiler.SkyOriginParameter;
    private static readonly StringName s_skyLayersName = Materials.MaterialCompiler.SkyLayersParameter;

    /// <summary>Hands the sky materials their camera's eye and layers (process-wide globals, set by each live view).</summary>
    private void SetSkyGlobals()
    {
        if (_skySurfaceFog is null)
        {
            return;
        }

        Materials.MaterialCompiler.EnsureGlobals();
        RenderingServer.GlobalShaderParameterSet(s_skyOriginName, _skyOrigin);
        RenderingServer.GlobalShaderParameterSet(s_skyLayersName, (int)SkyLayers);
    }

    /// <summary>
    /// Places the camera at a UE camera: location in uu, rotation, FovAngle in degrees. <paramref name="cut"/> settles
    /// time-smoothed effects (corona fades) at once, as for a new shot.
    /// </summary>
    public void SetUeCamera(FVector location, FRotator rotation, float fovDegrees, bool cut = false)
    {
        _cut |= cut;
        _cameraLocation = location;
        _cameraRotation = rotation;
        _fov = fovDegrees;
        Camera.Transform = CameraTransform(location, rotation);
        ApplyProjection();
        CameraZone = Map?.ZoneAt(location);
        if (_skyCamera is not null && Map?.Lighting.SkyLocation is { } sky)
        {
            _skyCamera.Transform = CameraTransform(sky, rotation);
        }

        SetSkyGlobals();
        UpdateSkySort();
        UpdateEffect();
        _coronas?.Update(location, 0f, _cut);
        _rain?.Update(location);
    }

    /// <summary>The coronas of the attached map, or null.</summary>
    public Coronas? Coronas => _coronas;

    /// <summary>The rain volumes of the attached map, or null.</summary>
    public RainVolumes? Rain => _rain;

    /// <summary>
    /// The vertical FOV of a UE FOV: FovAngle is horizontal over a 4:3 picture, and wider pictures add width (Hor+),
    /// so the vertical angle stays that of the 4:3 frame whatever the aspect.
    /// </summary>
    public static float VerticalFov(float horizontalDegrees)
    {
        return Characters.FirstPersonFraming.VerticalFov(horizontalDegrees, RenderK.FovReferenceAspect);
    }

    /// <summary>A sphere around the eye enclosing the near plane, including its pixel-centre offset, in uu.</summary>
    public float CameraClearance(float fovDegrees)
    {
        Vector2 size = IsInsideTree() ? GetViewport().GetVisibleRect().Size : new Vector2(1280f, 720f);
        float width = Math.Max(1f, size.X), height = Math.Max(1f, size.Y);
        float vertical = _verticalFovPolicy?.Invoke(fovDegrees, width / height) ?? VerticalFov(fovDegrees);
        float near = Camera.Near * UeSpace.UnitsPerMetre;
        float halfHeight = near * MathF.Tan(Mathf.DegToRad(vertical) * 0.5f);
        float halfWidth = halfHeight * width / height;
        halfWidth *= 1f + 2f * RenderK.PixelCenterOffset / width;
        halfHeight *= 1f + 2f * RenderK.PixelCenterOffset / height;
        return Math.Max(SpyPerspectiveCamera.DefaultClearance, MathF.Sqrt(near * near + halfWidth * halfWidth + halfHeight * halfHeight) + 1f);
    }

    /// <summary>
    /// The Godot transform of a UE view: Godot cameras look down -Z with +Y up and +X right, so the basis columns are
    /// UE right, up and backward, each through <see cref="UeSpace"/>.
    /// </summary>
    public static Transform3D CameraTransform(FVector location, FRotator rotation)
    {
        rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        var basis = new Basis(UeSpace.DirectionToGodot(y), UeSpace.DirectionToGodot(z), -UeSpace.DirectionToGodot(x));
        return new Transform3D(basis, UeSpace.ToGodot(location));
    }

    /// <summary>
    /// A frustum projection with the vertical angle of <see cref="VerticalFov"/>, shifted by half a pixel down and to
    /// the right: the original's D3D renderer puts pixel centres on whole coordinates, half a pixel off Vulkan's.
    /// </summary>
    private void ApplyProjection()
    {
        Vector2I size = IsInsideTree() ? (Vector2I)GetViewport().GetVisibleRect().Size : new Vector2I(1280, 720);
        size = new Vector2I(Math.Max(1, size.X), Math.Max(1, size.Y));
        foreach (Camera3D? camera in new[] { Camera, _skyCamera })
        {
            if (camera is null)
            {
                continue;
            }

            float vertical = _verticalFovPolicy?.Invoke(_fov, (float)size.X / size.Y) ?? VerticalFov(_fov);
            float height = 2f * camera.Near * MathF.Tan(Mathf.DegToRad(vertical) * 0.5f);
            float width = height * size.X / size.Y;
            camera.Projection = Camera3D.ProjectionType.Frustum;
            camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            camera.Size = height;
            camera.FrustumOffset = new Vector2(-RenderK.PixelCenterOffset * width / size.X, RenderK.PixelCenterOffset * height / size.Y);
        }

        _projectedFor = size;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            // Free the post passes with the view: their predelete hands the GPU frees to the render thread, which a
            // leftover wrapper finalized after the renderer shut down could no longer do.
            foreach (Camera3D? camera in new[] { Camera, _skyCamera })
            {
                if (camera?.Compositor is { } compositor)
                {
                    compositor.CompositorEffects = [];
                    camera.Compositor = null;
                    compositor.Dispose();
                }
            }

            _surfaceFog?.Dispose();
            _skySurfaceFog?.Dispose();
            Effect.Release();
            AmbientOcclusion.Release();
            Remanence.Release();
            DoubleVision.Release();
            CoronaSamples.Release();
            Effect.Dispose();
            AmbientOcclusion.Dispose();
            Remanence.Dispose();
            DoubleVision.Dispose();
            CoronaSamples.Dispose();
        }

        base._Notification(what);
    }

    /// <summary>
    /// Reloaded's widescreen limit (rl:gfx.widescreen, Hud/HudLayout): on a window wider than 16:9 the game is drawn at
    /// 16:9 and stretched across it, 3D and HUD alike. True while the widescreen setting is on; only a view drawing into
    /// the main window changes the window.
    /// </summary>
    public bool StretchPastWidest { get; set; } = true;

    private bool _stretching;
    private Window.ContentScaleModeEnum _oldScaleMode;
    private Window.ContentScaleAspectEnum _oldScaleAspect;
    private Vector2I _oldScaleSize;

    /// <summary>Stretches a 16:9 picture over a wider main window, or puts the window's own scaling back.</summary>
    private void FitWindow()
    {
        if (GetViewport() is not Window window || !ReferenceEquals(window, GetTree()?.Root))
        {
            return;
        }

        Vector2I size = window.Size;
        bool wide = StretchPastWidest && size.Y > 0 && size.X > size.Y * Hud.HudLayout.WidestAspect + 1f;
        if (wide)
        {
            if (!_stretching)
            {
                _stretching = true;
                _oldScaleMode = window.ContentScaleMode;
                _oldScaleAspect = window.ContentScaleAspect;
                _oldScaleSize = window.ContentScaleSize;
            }

            var target = new Vector2I((int)MathF.Round(Hud.HudLayout.LayoutWidth(size.X, size.Y)), size.Y);
            if (window.ContentScaleSize != target || window.ContentScaleMode != Window.ContentScaleModeEnum.Viewport)
            {
                window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
                window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
                window.ContentScaleSize = target;
            }
        }
        else
        {
            RestoreWindow(window);
        }
    }

    private void RestoreWindow(Window window)
    {
        if (!_stretching)
        {
            return;
        }

        _stretching = false;
        window.ContentScaleMode = _oldScaleMode;
        window.ContentScaleAspect = _oldScaleAspect;
        window.ContentScaleSize = _oldScaleSize;
    }

    public override void _ExitTree()
    {
        if (GetTree()?.Root is { } root)
        {
            RestoreWindow(root);
        }
    }

    public override void _Process(double delta)
    {
        FitWindow();
        if (IsInsideTree() && (Vector2I)GetViewport().GetVisibleRect().Size != _projectedFor)
        {
            ApplyProjection();
        }

        // Light sprites of the sky can move.
        UpdateSkySort();
        _fog.Tick((float)delta);
        UpdateEffect();
        _coronas?.Update(_cameraLocation, (float)delta, _cut);
        _cut = false;
    }

    private readonly ZoneFog _fog = new();
    private PortalFogFrame? _portalFogFrame;
    private LightingDoc? _portalFogLighting;
    private Transform3D _portalFogTransform;
    private Projection _portalFogProjection;
    private Vector2I _portalFogSize;
    private ViewFog _portalFogView;
    private int _portalFogZone;

    /// <summary>The distance fog the view draws with: on, colour (display value), start and end in uu.</summary>
    private readonly record struct ViewFog(bool On, Color Color, float Start, float End);

    private PortalFogFrame? PortalFog(ViewFog fog, int zone)
    {
        if (Map?.Lighting is not { } lighting || IsSceneVision(_rendMap)) return null;
        // Converted maps can have a portal table even when no zone uses fog. The view's fog (fog.On, also during a
        // transition) only comes from a fogged zone, so this is the same test that decided the shaders' portal fog.
        if (!PortalFogFrame.MapUses(lighting)) return null;
        Transform3D transform = Camera.GlobalTransform;
        Projection projection = Camera.GetCameraProjection();
        Vector2I size = (Vector2I)Camera.GetViewport().GetVisibleRect().Size;
        if (_portalFogFrame is null || !ReferenceEquals(lighting, _portalFogLighting) || transform != _portalFogTransform ||
            projection != _portalFogProjection || size != _portalFogSize || fog != _portalFogView || zone != _portalFogZone)
        {
            _portalFogLighting = lighting; _portalFogTransform = transform; _portalFogProjection = projection;
            _portalFogSize = size; _portalFogView = fog; _portalFogZone = zone;
            _portalFogFrame = PortalFogFrame.Build(lighting, Camera, _cameraLocation, zone, fog.On, fog.Color, fog.Start, fog.End);
        }
        return _portalFogFrame;
    }

    /// <summary>
    /// The view's zone fog with its blend over the new zone's DistanceFogBlendTime (the scene fog setup, game
    /// 0x10a15fb8, with PlayerController's LastZone, LastViewOrigin, TimeSinceLastFogChange and LastDistanceFog*).
    /// A zone without fog blends towards start 32764 and end 32765 in the colour last used; a view that jumped more than
    /// 100 uu, or the first zone, takes the new fog at once; zone 0 (no ZoneInfo) never blends.
    /// </summary>
    private sealed class ZoneFog
    {
        private int _lastZone;
        private FVector _lastOrigin;
        private float _since;
        private float _lastStart;
        private float _lastEnd;
        private (int R, int G, int B) _lastColor;

        /// <summary>PlayerController.Tick: TimeSinceLastFogChange += DeltaTime (0x10a30240).</summary>
        public void Tick(float delta) => _since += delta;

        public ViewFog Update(LightingDoc? lighting, ZoneEntry? zone, FVector origin)
        {
            int index = zone?.ZoneIndex ?? 0;
            if (index < 0)
            {
                index = 0;
            }

            bool fog = zone is { Fog: true };
            if (index != _lastZone)
            {
                if (fog && _lastStart == RenderK.FogOffEnd)
                {
                    _lastColor = Bytes(zone!.FogColor);
                }

                ZoneEntry? old = _lastZone == 0 ? null : lighting?.ZoneByIndex(_lastZone);
                if (_lastZone == 0 || (origin - _lastOrigin).Size() > RenderK.FogTeleportUnits || (!fog && old is not { Fog: true }))
                {
                    _since = BlendTime(zone);
                }
                else
                {
                    // Still blending into the old zone: keep where that blend got to as the new starting point.
                    float oldTime = BlendTime(old);
                    if (_since < oldTime)
                    {
                        float a = oldTime == 0f ? 1f : _since / oldTime;
                        _lastStart += ((old?.FogStart ?? 0f) - _lastStart) * a;
                        _lastEnd += ((old?.FogEnd ?? 0f) - _lastEnd) * a;
                        _lastColor = Lerp(_lastColor, Bytes(old?.FogColor ?? default), a);
                    }

                    _since = 0f;
                }

                _lastZone = index;
            }

            _lastOrigin = origin;
            float time = BlendTime(zone);
            float alpha = time == 0f ? 1f : _since / time;
            (int R, int G, int B) color = fog ? Bytes(zone!.FogColor) : _lastColor;
            float start = fog ? zone!.FogStart : RenderK.FogOffEnd - 1f;
            float end = fog ? zone!.FogEnd : RenderK.FogOffEnd;
            if (!(alpha < 1f) || index == 0)
            {
                _lastColor = color;
                _lastStart = start;
                _lastEnd = end;
                _since = 1e6f;
                alpha = 1f;
            }

            (int R, int G, int B) c = Lerp(_lastColor, color, alpha);
            return new ViewFog(
                fog || alpha < 1f,
                new Color(c.R / 255f, c.G / 255f, c.B / 255f),
                _lastStart + ((start - _lastStart) * alpha),
                _lastEnd + ((end - _lastEnd) * alpha));
        }

        private static float BlendTime(ZoneEntry? zone) => zone?.FogBlendTime ?? 1f;

        private static (int R, int G, int B) Bytes(Color c) =>
            ((int)MathF.Round(c.R * 255f), (int)MathF.Round(c.G * 255f), (int)MathF.Round(c.B * 255f));

        /// <summary>The byte lerp of 0x109fbf20: a + (b - a) x alpha, truncated.</summary>
        private static (int R, int G, int B) Lerp((int R, int G, int B) a, (int R, int G, int B) b, float alpha) =>
            ((int)(a.R + ((b.R - a.R) * alpha)), (int)(a.G + ((b.G - a.G) * alpha)), (int)(a.B + ((b.B - a.B) * alpha)));
    }

    private void UpdateEffect()
    {
        AmbientOcclusion.Configure(OpenScctGame.Options.SettingsStore.Current.AmbientOcclusion, RendMap, Camera);
        ZoneEntry? zone = CameraZone;
        // EMF fogs to its clear colour; thermal has no fog; the other modes take the zone fog, blended on a zone change.
        ViewFog fog = _rendMap switch
        {
            RenderK.RendMapThermal => default,
            RenderK.RendMapEmf => new ViewFog(true, RenderK.EmfClear, RenderK.EmfFogStart, RenderK.EmfFogEnd),
            _ => _fog.Update(Map?.Lighting, zone, _cameraLocation),
        };
        UpdateClear(zone);
        Effect.Settings = new DisplayPostSettings
        {
            SurfaceFogApplied = true,
            Fog = fog.On,
            FogColor = fog.Color,
            FogStart = fog.Start,
            FogEnd = fog.End,
            PortalFog = PortalFog(fog, zone?.ZoneIndex ?? 0),
            GlowGate = GlowGate,
            // Post-Render Quality Normal draws no glow (threshold 255 lets nothing through); the gate still runs.
            GlowThreshold = OpenScctGame.Options.SettingsStore.Current.PostQuality == 0 ? 255 : Map?.Lighting.GlowThreshold ?? 255,
            RendMap = RendMap,
            MtvSlope = Map?.MtvSlope ?? RenderK.DefaultMtvSlope,
            MtvOffset = Map?.MtvOffset ?? RenderK.DefaultMtvOffset,
            Pack = Map?.Pack,
            CleanRects = MotionCleanRects,
            Near = RenderK.NearClipUnits,
            Far = RenderK.FarClipUnits,
        };
        _surfaceFog?.Update(Effect.Settings);
        if (_surfaceFog is not null)
            ResponsiveSmokeRenderer.BindView(_surfaceFog.Slot, Camera, Map, _rendMap);
        bool smoke = ResponsiveSmokeRenderer.HasSmoke(Map);
        if (smoke) PreloadSmokeBackground();
        if (_smokeBackdrop is not null) _smokeBackdrop.Visible = smoke;

        if (_skySurfaceFog is not null)
        {
            // The sky zone's own scene takes its zone's fog as it is, without the view's blend (AquaE's sky zone fogs
            // everything from 0 to 16 uu).
            ZoneEntry? sky = Map is { Lighting.SkyZone: >= 0 } ? Map.Lighting.ZoneByIndex(Map.Lighting.SkyZone) : null;
            bool skyFog = sky is { Fog: true } && !IsSceneVision(_rendMap);
            _skySurfaceFog.Update(skyFog, sky?.FogColor ?? default, sky?.FogStart ?? 0f, sky?.FogEnd ?? 0f);
        }
    }

    /// <summary>True for the modes whose scene is the vision pass of its own (thermal, EMF), not the normal frame.</summary>
    public static bool IsSceneVision(int rendMap) => rendMap is RenderK.RendMapThermal or RenderK.RendMapEmf;

    private Color _clear = new(0f, 0f, 0f);

    /// <summary>
    /// The clear colour: EMF clears to 0xffffda85; a camera zone with bDistanceFog and bClearToFogColor clears to its
    /// DistanceFogColor (not in thermal); else black. The sky view clears the same, so the clear shows wherever neither
    /// the world nor the sky zone drew.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "scene clear 0x10a3e515: Clear(DistanceFogColor) when the view zone has bClearToFogColor and bDistanceFog, RendMap not 36 or 38, main scene only")]
    private void UpdateClear(ZoneEntry? zone)
    {
        Color clear = _rendMap == RenderK.RendMapEmf ? RenderK.EmfClear
            : zone is { Fog: true, ClearToFog: true } && !IsSceneVision(_rendMap) ? zone.FogColor
            : new Color(0f, 0f, 0f);
        if (clear == _clear)
        {
            return;
        }

        _clear = clear;
        // Godot linearizes the background colour (sRGB to linear); the frame holds display values, so hand it the
        // sRGB encoding of the value the buffer must hold.
        foreach (Camera3D? camera in new[] { Camera, _skyCamera })
        {
            if (camera?.Environment is { } env)
            {
                env.BackgroundColor = new Color(clear.R, clear.G, clear.B).LinearToSrgb();
            }
        }
    }

    /// <summary>
    /// The vision mode's scene settings: the materials' mode, the clear colour (<see cref="UpdateClear"/>), coronas off
    /// in thermal and EMF, vision-only nodes on in thermal and EMF.
    /// </summary>
    private void ApplyVision()
    {
        Materials.MaterialCompiler.SetVision(_rendMap, _squintingFactor);
        // Sets both cameras' clear again below (the sky camera may be new).
        _clear = new Color(-1f, -1f, -1f);
        bool sceneVision = IsSceneVision(_rendMap);
        if (Map?.Lighting.OutdoorSky == true)
            Camera.Environment.BackgroundMode = sceneVision ? Godot.Environment.BGMode.Color : Godot.Environment.BGMode.Sky;
        // Thermal and EMF draw no sky zone: the frame keeps its clear colour there (thermal black reads as the far,
        // cold end of the table).
        if (_backdrop is not null)
        {
            _backdrop.Visible = !sceneVision;
        }

        if (_skyMerged)
        {
            Camera.CullMask = sceneVision ? Camera.CullMask & ~RenderLayers.Sky : Camera.CullMask | RenderLayers.Sky;
        }

        if (_skyViewport is not null)
        {
            // The sky view only draws a sky fogged solid: once on each return to a normal mode (the fog and vision
            // globals of this frame are set before the draw) and then kept: the picture cannot change while the mode stays.
            _skyViewport.RenderTargetUpdateMode = sceneVision ? SubViewport.UpdateMode.Disabled : SubViewport.UpdateMode.Once;
        }

        if (_coronas is not null)
        {
            _coronas.Enabled = !sceneVision;
        }

        Map?.ShowVisionOnly(sceneVision);
        UpdateEffect();
    }

    private void EnsureOverlay()
    {
        if (_overlayRect is not null)
        {
            return;
        }

        _overlayLayer = new CanvasLayer { Name = "Overlay", Layer = 10 };
        _overlayRect = new TextureRect
        {
            Name = "Binoculars",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Visible = false,
        };
        _overlayRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlayLayer.AddChild(_overlayRect);
        AddChild(_overlayLayer);
    }

    /// <summary>The UE camera last set.</summary>
    public (FVector Location, FRotator Rotation, float Fov) UeCamera => (_cameraLocation, _cameraRotation, _fov);
}
