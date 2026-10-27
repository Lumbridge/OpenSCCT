using System;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render;

/// <summary>
/// The Faithful view of a <see cref="RenderMap"/>: a camera placed from a UE camera (location, rotation, FovAngle), an environment that leaves colours alone (Linear tonemap, exposure 1, no glow, AO, fog or auto exposure), the
/// display-space post pass, and the sky zone drawn behind the world from the SkyZoneInfo through a SubViewport.
/// Add it, untransformed, under the node whose viewport it should draw in. docs/RENDERING.md#view.
/// </summary>
[PortOf("eng:Render.View", Spec = "docs/RENDERING.md#view")]
public partial class FaithfulView : Node3D
{
    public const string BackdropShaderPath = "res://Shaders/svm_sky_backdrop.gdshader";


    private SubViewport? _skyViewport;
    private Camera3D? _skyCamera;
    private DisplaySpaceEffect? _skyEffect;
    private MeshInstance3D? _backdrop;
    private FVector _cameraLocation;
    private FRotator _cameraRotation;
    private float _fov = 90f;
    private Vector2I _projectedFor;
    private Coronas? _coronas;
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
        Remanence = new RemanenceEffect();
        Camera.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { Effect, Remanence } };
        AddChild(Camera);
    }

    public Camera3D Camera { get; }

    public DisplaySpaceEffect Effect { get; }

    /// <summary>The flash-bang after-image over the shown frame (spec/equipment/flash-bang.md rule 5).</summary>
    public RemanenceEffect Remanence { get; }

    public RenderMap? Map { get; private set; }

    /// <summary>The sky zone's SubViewport, when the map has a sky zone.</summary>
    public SubViewport? SkyViewport => _skyViewport;

    private int _rendMap = RenderK.RendMapNormal;
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

    /// <summary>Shows <paramref name="map"/> and sets up its sky zone view when it has one.</summary>
    public void Attach(RenderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Map = map;
        _coronas = new Coronas(map, map.Root);
        ApplyVision();
        if (map.Lighting.SkyLocation is null)
        {
            return;
        }

        _skyViewport = new SubViewport
        {
            Name = "SkyZone",
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Disabled,
            Size = new Vector2I(16, 16),
        };
        _skyEffect = new DisplaySpaceEffect { Settings = new DisplayPostSettings { GlowGate = false } };
        _skyCamera = new Camera3D
        {
            Name = "SkyCamera",
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = Camera.Near,
            Far = Camera.Far,
            Environment = CreateEnvironment(),
            Attributes = CreateAttributes(),
            Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _skyEffect } },
            CullMask = RenderLayers.Sky,
            Current = true,
        };
        _skyViewport.AddChild(_skyCamera);
        AddChild(_skyViewport);
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

        UpdateEffect();
        _coronas?.Update(location, 0f, _cut);
    }

    /// <summary>The coronas of the attached map, or null.</summary>
    public Coronas? Coronas => _coronas;

    /// <summary>
    /// The vertical FOV of a UE FOV: FovAngle is horizontal over a 4:3 picture, and wider pictures add width (Hor+),
    /// so the vertical angle stays that of the 4:3 frame whatever the aspect.
    /// </summary>
    public static float VerticalFov(float horizontalDegrees)
    {
        float half = Mathf.DegToRad(horizontalDegrees) * 0.5f;
        return Mathf.RadToDeg(2f * MathF.Atan(MathF.Tan(half) / RenderK.FovReferenceAspect));
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

            Effect.Release();
            Remanence.Release();
            _skyEffect?.Release();
            Effect.Dispose();
            Remanence.Dispose();
            _skyEffect?.Dispose();
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

        if (_skyViewport is not null)
        {
            Vector2I size = (Vector2I)GetViewport().GetVisibleRect().Size;
            if (size.X > 0 && size.Y > 0 && _skyViewport.Size != size)
            {
                _skyViewport.Size = size;
            }
        }

        _fog.Tick((float)delta);
        UpdateEffect();
        _coronas?.Update(_cameraLocation, (float)delta, _cut);
        _cut = false;
    }

    private readonly ZoneFog _fog = new();

    /// <summary>The distance fog the view draws with: on, colour (display value), start and end in uu.</summary>
    private readonly record struct ViewFog(bool On, Color Color, float Start, float End);

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
            Fog = fog.On,
            FogColor = fog.Color,
            FogStart = fog.Start,
            FogEnd = fog.End,
            GlowGate = GlowGate,
            GlowThreshold = Map?.Lighting.GlowThreshold ?? 255,
            RendMap = RendMap,
            MtvSlope = Map?.MtvSlope ?? RenderK.DefaultMtvSlope,
            MtvOffset = Map?.MtvOffset ?? RenderK.DefaultMtvOffset,
            Pack = Map?.Pack,
            CleanRects = MotionCleanRects,
            Near = RenderK.NearClipUnits,
            Far = RenderK.FarClipUnits,
        };

        if (_skyEffect is not null)
        {
            // The sky zone's own scene takes its zone's fog as it is, without the view's blend (AquaE's sky zone fogs
            // everything from 0 to 16 uu).
            ZoneEntry? sky = Map is { Lighting.SkyZone: >= 0 } ? Map.Lighting.ZoneByIndex(Map.Lighting.SkyZone) : null;
            bool skyFog = sky is { Fog: true } && !IsSceneVision(_rendMap);
            _skyEffect.Settings = new DisplayPostSettings
            {
                GlowGate = false,
                Fog = skyFog,
                FogColor = sky?.FogColor ?? default,
                FogStart = sky?.FogStart ?? 0f,
                FogEnd = sky?.FogEnd ?? 0f,
                Near = RenderK.NearClipUnits,
                Far = RenderK.FarClipUnits,
            };
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
        Materials.MaterialCompiler.SetVision(_rendMap);
        // Sets both cameras' clear again below (the sky camera may be new).
        _clear = new Color(-1f, -1f, -1f);
        bool sceneVision = IsSceneVision(_rendMap);
        // Thermal and EMF draw no sky zone: the frame keeps its clear colour there (thermal black reads as the far,
        // cold end of the table).
        if (_backdrop is not null)
        {
            _backdrop.Visible = !sceneVision;
        }

        if (_skyViewport is not null)
        {
            _skyViewport.RenderTargetUpdateMode = sceneVision ? SubViewport.UpdateMode.Disabled : SubViewport.UpdateMode.Always;
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
