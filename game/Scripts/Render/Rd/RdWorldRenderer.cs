using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// SVM_RD_WORLD=1: the world pass. The merged map cells and the sky zone's solid surfaces are drawn by our own code on
/// Godot's RenderingDevice, inside Godot's frame: from the main camera's compositor, between Godot's depth pre-pass
/// and its opaque pass, into Godot's own colour and depth buffers (<see cref="RdWorldGpu"/>). Godot draws everything
/// else as before, over the same depth. The shaders are the Faithful template itself, translated
/// (<see cref="GdShaderTranslator"/>), so the pixels are the template's.
/// Each frame before the draw (after the lights, fog and vision of the frame are set): frustum culling per batch, the
/// light terms each batch needs (no spot slot's sphere reaches it: no spot code; no dynamic light either: neither),
/// the spot slots that reach it, and a front to back order.
/// The pass sits out, and Godot draws those meshes itself, in thermal and EMF, while responsive smoke is compiled in,
/// on maps with an outdoor sky, and after any failure.
/// </summary>
public partial class RdWorldRenderer : Node
{
    public static bool Enabled => RdWorldGeometry.Enabled;

    // LightReach's margins: a light reaches a box a little before the template's own test would light a pixel of it.
    private const float Margin = 0.01f;
    private const float RelativeMargin = 1e-4f;

    private readonly RenderMap _map;
    private readonly FaithfulView _view;
    private readonly RdWorldGeometry _geometry;
    private readonly RdWorldGpu _gpu;
    private readonly Vector3 _skyOrigin;
    private readonly Plane[] _worldPlanes = new Plane[6];
    private readonly Plane[] _skyPlanes = new Plane[6];
    private readonly Vector4[] _spots = new Vector4[SpotShadowLight.ReachSlots];
    private readonly List<DynamicRow> _lights = [];
    private long[] _keys = new long[256];
    private int[] _order = new int[256];
    private RdWorldEffect? _effect;
    private bool _subscribed, _active, _reportedFailure;
    private double _sinceReport;
    private int _frames, _full, _noSpot, _neither;

    internal RdWorldRenderer(RenderMap map, FaithfulView view)
    {
        Name = "RdWorld";
        _map = map;
        _view = view;
        _geometry = map.RdWorld ?? throw new InvalidOperationException("SVM_RD_WORLD: the map was built without RdWorldGeometry");
        int anisotropy = ProjectSettings.GetSetting("rendering/textures/default_filters/anisotropic_filtering_level", 2).AsInt32();
        float mipBias = (float)ProjectSettings.GetSetting("rendering/textures/default_filters/texture_mipmap_bias", 0.0).AsDouble();
        _gpu = new RdWorldGpu(_geometry, Math.Clamp(anisotropy, 0, 4), mipBias);
        _skyOrigin = map.Lighting.SkyLocation is { } sky ? UeSpace.ToGodot(sky) : Vector3.Zero;
    }

    public override void _Ready()
    {
        var reasons = new List<string>();
        foreach ((string reason, int count) in _geometry.Reasons) reasons.Add($"{count} {reason}");
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_RD_WORLD {_map.MapId}: {_geometry.Batches.Count} batches from {_geometry.Taken} buckets, {_geometry.Left} buckets left to Godot ({string.Join("; ", reasons)})"));
        if (Stats)
        {
            foreach (string name in _geometry.TakenNames()) GD.Print("SVM_RD_WORLD material " + name);
        }

        if (_map.Lighting.OutdoorSky || _geometry.Batches.Count == 0)
        {
            // Godot's sky pass needs its own background mode: such a map stays with Godot.
            GD.Print("SVM_RD_WORLD off for " + _map.MapId);
            return;
        }

        if (OwnFrame)
        {
            // Under every other canvas layer (the HUD is at 10, the camera occlusion mask at 9).
            var layer = new CanvasLayer { Name = "RdFrameLayer", Layer = -100 };
            _ownRect = new TextureRect
            {
                Name = "RdFrameView", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _ownRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(_ownRect);
            AddChild(layer);
            // Godot draws nothing 3D in the main viewport: no pre-pass, no float target, no tonemap copy.
            GetViewport().Disable3D = true;
            _renderOwn = Callable.From(_gpu.RenderOwn);
            RenderingServer.FramePreDraw += OnPreDraw;
            _subscribed = true;
            GD.Print("SVM_RD_FRAME on for " + _map.MapId);
            return;
        }

        _effect = new RdWorldEffect(_gpu);
        Compositor compositor = _view.Camera.Compositor ??= new Compositor();
        Godot.Collections.Array<CompositorEffect> effects = compositor.CompositorEffects;
        effects.Insert(0, _effect);
        compositor.CompositorEffects = effects;
        // After the lights, the fog and LightReach, which subscribed while the map was built and attached.
        RenderingServer.FramePreDraw += OnPreDraw;
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (_subscribed)
        {
            RenderingServer.FramePreDraw -= OnPreDraw;
            _subscribed = false;
        }

        if (OwnFrame && _ownRect is not null)
        {
            _ownRect.Texture = null;
            GetViewport().Disable3D = false;
            // Queued after the Texture2DRD let go of the target.
            RenderingServer.CallOnRenderThread(Callable.From(_gpu.Free));
            return;
        }

        if (_effect is null) return;
        if (GodotObject.IsInstanceValid(_view.Camera))
        {
            if (_view.Camera.Compositor is { } compositor)
            {
                Godot.Collections.Array<CompositorEffect> effects = compositor.CompositorEffects;
                effects.Remove(_effect);
                compositor.CompositorEffects = effects;
            }

            SetActive(_view.Camera, false);
        }

        _effect = null;
        RdWorldGpu gpu = _gpu;
        RenderingServer.CallOnRenderThread(Callable.From(gpu.Free));
    }

    public override void _Process(double delta)
    {
        _sinceReport += delta;
        _frames++;
        Survey(delta);
        if (_sinceReport < 1.0 || !Stats) return;
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_RD_WORLD active={_active} draws={_gpu.LastDraws} full={_full} noSpot={_noSpot} neither={_neither} shaders={_gpu.ShaderCount} made={_gpu.Made} recordMs={_gpu.LastRecordMs:F3} fps={_frames / _sinceReport:F1}"));
        var spots = new System.Text.StringBuilder();
        for (int i = 0; i < _spots.Length; i++)
        {
            if (_spots[i].W > 0f) spots.Append(CultureInfo.InvariantCulture, $" {i}:r{MathF.Sqrt(_spots[i].W):F1}");
        }

        GD.Print($"SVM_RD_WORLD spots{spots} dynamic={_lights.Count} soft={SpotShadowLight.SoftRadius}");
        _sinceReport = 0;
        _frames = 0;
    }

    /// <summary>SVM_RD_WORLD_STATS=1 prints the draws by variant once a second.</summary>
    private static bool Stats { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_STATS") == "1";

    /// <summary>
    /// SVM_RD_WORLD_OUT / SVM_RD_WORLD_IN = a sum of RdFeatures: code every world draw leaves out, or keeps, whatever
    /// reaches it (measurement switches; OUT draws wrong light).
    /// </summary>
    private static RdFeatures ForcedOut { get; } = Forced("SVM_RD_WORLD_OUT");
    private static RdFeatures ForcedIn { get; } = Forced("SVM_RD_WORLD_IN");

    private static RdFeatures Forced(string name) =>
        int.TryParse(System.Environment.GetEnvironmentVariable(name), out int v) ? (RdFeatures)v & RdFeatures.All : RdFeatures.None;

    /// <summary>SVM_RD_WORLD_TIGHT=0 reaches with the slots' spheres alone, as LightReach (a measurement switch).</summary>
    private static bool TightSpots { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_TIGHT") != "0";

    /// <summary>SVM_RD_WORLD_DYNBITS=0 hands every draw every dynamic light row (a measurement switch).</summary>
    private static bool TightDynamic { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_DYNBITS") != "0";

    private static readonly StringName s_fogMainSlot = "svm_fog_main_slot";
    private static readonly StringName s_fogMainView = "svm_fog_main_view";

    /// <summary>
    /// The code no draw of this frame needs: portal fog while the main view's fog is none or one zone's ramp (the
    /// template reads mode 2 as portal fog; svm_fog.gdshaderinc).
    /// </summary>
    private static RdFeatures FrameFeatures(Camera3D camera)
    {
        RdFeatures features = RdFeatures.None;
        int slot = (int)((camera.CullMask >> RenderLayers.FogViewShift) & 127u);
        if (slot == 0) features |= RdFeatures.NoPortalFog;
        else if (ShaderGlobals.TryGet(s_fogMainSlot, out Variant mainSlot) && mainSlot.AsInt32() == slot
            && ShaderGlobals.TryGet(s_fogMainView, out Variant view) && view.AsVector4().Y < 1.5f) features |= RdFeatures.NoPortalFog;
        return features;
    }

    /// <summary>
    /// SVM_RD_FRAME=1 (in development): the pass draws the scene into a target of its own, as the original draws its
    /// frame, and Godot draws no 3D in the main viewport. So far the world's solid and masked surfaces only.
    /// </summary>
    internal static bool OwnFrame { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_FRAME") == "1";

    // The share of the depth range the sky zone is squeezed into, behind the world.
    private const float SkyDepth = 0.001f;
    private TextureRect? _ownRect;
    private readonly RdObjects _objects = new();
    private readonly List<RdObjects.Upload> _objectUploads = [];
    private readonly Texture2Drd _ownShown = new();
    private Rid _ownShownColor;
    private Callable _renderOwn;

    private void PreDrawOwn(Camera3D camera)
    {
        Viewport viewport = GetViewport();
        Vector2 visible = viewport.GetVisibleRect().Size;
        float scale = viewport.Scaling3DScale;
        Transform3D eye = camera.GetCameraTransform();
        Projection projection = camera.GetCameraProjection();
        Projection world = projection * new Projection(eye.AffineInverse());
        Projection sky = projection * new Projection(new Transform3D(eye.Basis, _skyOrigin).AffineInverse());
        Planes(world, _worldPlanes);
        Planes(sky, _skyPlanes);
        RdWorldFrame frame = _gpu.Rent();
        frame.Size = new Vector2I(Math.Max(1, (int)(visible.X * scale)), Math.Max(1, (int)(visible.Y * scale)));
        Color clear = camera.Environment?.BackgroundColor.SrgbToLinear() ?? new Color(0f, 0f, 0f);
        frame.Clear = new Color(clear.R, clear.G, clear.B, 1f);
        WriteClip(ToTarget(world, 1f - SkyDepth, SkyDepth), frame.Clip.AsSpan(0, 16));
        WriteClip(ToTarget(sky, SkyDepth, 0f), frame.Clip.AsSpan(16, 16));
        frame.Gate = _view.GlowGate;
        // The camera zone's own distance fog, as the surfaces' shaders read it (svm_fog.gdshaderinc): mode 1 holds it
        // in the view row, mode 2 (portal fog) in the camera zone's ramp. The original fogs a frame with one such ramp.
        Span<float> fogBlock = frame.FogBlock;
        fogBlock.Clear();
        (fogBlock[0], fogBlock[1], fogBlock[2]) = (eye.Origin.X, eye.Origin.Y, eye.Origin.Z);
        if (ShaderGlobals.TryGet(s_fogMainView, out Variant fogView))
        {
            Vector4 view = fogView.AsVector4();
            if (view.Y > 1.5f && ShaderGlobals.TryGet("svm_fog_main_ramp", out Variant rampValue) && ShaderGlobals.TryGet("svm_fog_main_ramp_color", out Variant rampColor)
                && ShaderGlobals.TryGet("svm_fog_main_forward", out Variant forward))
            {
                Vector4 ramp = rampValue.AsVector4(), color = rampColor.AsVector4();
                (fogBlock[4], fogBlock[5], fogBlock[6], fogBlock[7]) = (color.X, color.Y, color.Z, ramp.W > 0.5f ? 1f : 0f);
                (fogBlock[8], fogBlock[9], fogBlock[10]) = (forward.AsVector4().W, ramp.X, ramp.Z);
            }
            else if (view.Y > 0.5f && view.Y < 1.5f && ShaderGlobals.TryGet("svm_fog_main_color", out Variant fogColor))
            {
                Vector4 color = fogColor.AsVector4();
                (fogBlock[4], fogBlock[5], fogBlock[6], fogBlock[7]) = (color.X, color.Y, color.Z, 1f);
                (fogBlock[8], fogBlock[9], fogBlock[10]) = (100f, view.Z, view.W > view.Z ? 1f / (view.W - view.Z) : 1e6f);
            }
        }

        int threshold = OpenScctGame.Options.SettingsStore.Current.PostQuality == 0 ? 255 : _map.Lighting.GlowThreshold;
        frame.GlowCut = Post.DisplaySpaceEffect.GlowCut(threshold) ?? -1f;
        frame.Eye = eye;
        frame.Proj = projection;
        frame.Layers = camera.CullMask;
        ReadLights();
        // Portal fog stays out of the template's draws: the frame has the camera zone's one ramp (RdWorldGpu.OverrideFog).
        Select(frame, eye.Origin, true, RdFeatures.NoPortalFog);
        // The map's gameplay meshes and the characters.
        _objects.Begin(frame.Objects, frame.ObjectData, frame.Palette, _objectUploads);
        if (_map.Root.GetNodeOrNull("Gameplay") is { } gameplay)
        {
            _objects.Gather(gameplay, _worldPlanes, static (planes, box) => Inside(planes, box), frame.Objects, frame.ObjectData, frame.Palette, _objectUploads);
        }

        if ((GetParent() as OpenScctGame.Play.PlayRoot)?.Pawns is { } pawns)
        {
            _objects.Gather(pawns, _worldPlanes, static (planes, box) => Inside(planes, box), frame.Objects, frame.ObjectData, frame.Palette, _objectUploads);
        }

        foreach (RdObjects.Upload upload in _objectUploads) _gpu.QueueObjectUpload(upload);

        _gpu.Submit(frame);
        RenderingServer.CallOnRenderThread(_renderOwn);
        Rid latest = _gpu.LatestOwnColor;
        if (latest.IsValid && latest != _ownShownColor && _ownRect is not null)
        {
            _ownShown.TextureRdRid = latest;
            _ownShownColor = latest;
            _ownRect.Texture = _ownShown;
        }
    }

    /// <summary>
    /// An OpenGL-style clip transform for the pass's own target: y flipped (rows top down, as Godot shows textures),
    /// reverse Z (near 1, far 0), squeezed into [bias, bias + scale] of the depth range.
    /// </summary>
    private static Projection ToTarget(in Projection m, float scale, float bias)
    {
        static Vector4 Fix(Vector4 c, float scale, float bias) => new(c.X, -c.Y, (scale * 0.5f * (c.W - c.Z)) + (bias * c.W), c.W);
        return new Projection(Fix(m.X, scale, bias), Fix(m.Y, scale, bias), Fix(m.Z, scale, bias), Fix(m.W, scale, bias));
    }

    private static void WriteClip(in Projection m, Span<float> to)
    {
        ReadOnlySpan<Vector4> columns = [m.X, m.Y, m.Z, m.W];
        for (int c = 0; c < 4; c++) (to[c * 4], to[(c * 4) + 1], to[(c * 4) + 2], to[(c * 4) + 3]) = (columns[c].X, columns[c].Y, columns[c].Z, columns[c].W);
    }

    private double _surveyAt;
    private bool _surveyed;

    /// <summary>
    /// SVM_PERF_SURVEY=1 (measurement only): six seconds into the round, lists what Godot's main view can still draw
    /// that is not on the World layer alone; SVM_PERF_HIDE=a,b hides the nodes whose path holds one of the words.
    /// </summary>
    private void Survey(double delta)
    {
        if (_surveyed) return;
        string? hide = System.Environment.GetEnvironmentVariable("SVM_PERF_HIDE");
        bool list = System.Environment.GetEnvironmentVariable("SVM_PERF_SURVEY") == "1";
        if (hide is null && !list)
        {
            _surveyed = true;
            return;
        }

        _surveyAt += delta;
        if (_surveyAt < 6.0) return;
        _surveyed = true;
        string[] words = hide?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var stack = new Stack<Node>();
        stack.Push(GetTree().Root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            foreach (Node child in node.GetChildren()) stack.Push(child);
            if (node is not GeometryInstance3D g || !g.IsVisibleInTree()) continue;
            string path = node.GetPath().ToString();
            bool hidden = false;
            foreach (string word in words) hidden |= path.Contains(word, StringComparison.OrdinalIgnoreCase);
            if (hidden) g.Visible = false;
            if (list && (g.Layers & (RenderLayers.Effects | RenderLayers.FirstPerson | RenderLayers.Backdrop)) != 0)
            {
                string material = g is MeshInstance3D { Mesh: { } mesh } && mesh.GetSurfaceCount() > 0
                    ? (g.MaterialOverride ?? ((MeshInstance3D)g).GetActiveMaterial(0)) is ShaderMaterial { Shader: { } sh } ? sh.ResourceName : "?"
                    : "";
                GD.Print($"SVM_PERF node {node.GetClass()} layers={g.Layers:X} hidden={hidden} {path} [{material}]");
            }
        }
    }

    private void OnPreDraw()
    {
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !GodotObject.IsInstanceValid(_view.Camera))
        {
            return;
        }

        Camera3D camera = _view.Camera;
        if (OwnFrame)
        {
            PreDrawOwn(camera);
            return;
        }

        if (_gpu.Failed && !_reportedFailure)
        {
            _reportedFailure = true;
            GD.PushWarning("SVM_RD_WORLD: the world pass failed; Godot draws the map");
        }

        // Thermal and EMF draw other materials (LightReach swaps them in on Godot's meshes); responsive smoke changes
        // the template's code while a match runs.
        bool active = !_gpu.Failed && camera.IsInsideTree() && camera.Current && camera.Environment is not null
            && !FaithfulView.IsSceneVision(MaterialCompiler.Vision) && !ResponsiveSmokeShaders.Enabled;
        SetActive(camera, active);
        if (!active)
        {
            _gpu.Withdraw();
            return;
        }

        Transform3D eye = camera.GetCameraTransform();
        Projection projection = camera.GetCameraProjection();
        Planes(projection * new Projection(eye.AffineInverse()), _worldPlanes);
        Planes(projection * new Projection(new Transform3D(eye.Basis, _skyOrigin).AffineInverse()), _skyPlanes);
        ReadLights();

        RdWorldFrame frame = _gpu.Rent();
        Color clear = camera.Environment!.BackgroundColor.SrgbToLinear();
        frame.Clear = new Color(clear.R, clear.G, clear.B, 1f);
        frame.Layers = camera.CullMask;
        Select(frame, eye.Origin, _view.SkyInMainView, FrameFeatures(camera));
        _gpu.Submit(frame);
    }

    /// <summary>
    /// Who draws the pass's meshes in the main view: this pass (they leave the camera's mask, and the environment keeps
    /// the background so Godot's opaque pass loads what the pass drew), or Godot (the sky meshes while it draws the sky).
    /// </summary>
    private void SetActive(Camera3D camera, bool active)
    {
        _active = active;
        uint mask = camera.CullMask;
        uint wanted = active
            ? mask & ~(RenderLayers.StaticWorldRd | RenderLayers.SkyRd)
            : (mask | RenderLayers.StaticWorldRd) & ~RenderLayers.SkyRd | ((mask & RenderLayers.Sky) != 0 ? RenderLayers.SkyRd : 0u);
        if (active && RdWorldGpu.Test.Contains("only")) wanted = 0;
        if (active && System.Environment.GetEnvironmentVariable("SVM_RD_WORLD_MASK") is { Length: > 0 } keep)
        {
            wanted &= Convert.ToUInt32(keep, 16) | RenderLayers.FogViewMask;
        }
        if (wanted != mask) camera.CullMask = wanted;
        if (camera.Environment is { } env)
        {
            if (active && env.BackgroundMode == Godot.Environment.BGMode.Color) env.BackgroundMode = Godot.Environment.BGMode.Keep;
            else if (!active && env.BackgroundMode == Godot.Environment.BGMode.Keep) env.BackgroundMode = Godot.Environment.BGMode.Color;
        }
    }

    /// <summary>The spot slots' spheres and the rows of the dynamic light texture as the template reads them this frame.</summary>
    private void ReadLights()
    {
        for (int i = 0; i < _spots.Length; i++) _spots[i] = SpotShadowLight.ReachSphere(i);
        WorldLights lights = _map.WorldLights;
        ReadOnlySpan<PackedLight> rows = lights.Rows;
        ReadOnlySpan<float> zones = lights.RowZones;
        Vector4 tune = WorldLights.Tune;
        _lights.Clear();
        for (int i = 0; i < rows.Length; i++)
        {
            PackedLight row = rows[i];
            // The template's own radius scale, and its own-zone limit only while the calibration honours it.
            _lights.Add(new DynamicRow(new Vector4(row.Pos.X, row.Pos.Y, row.Pos.Z, row.Pos.W * tune.Y), row.Col, row.Spot,
                tune.W > 0.5f ? zones[i] : -1f));
        }
    }

    private readonly record struct DynamicRow(Vector4 Pos, Vector4 Col, Vector4 Spot, float Zone);

    /// <summary>
    /// The visible batches in draw order (world solid front to back, world masked, then the sky zone, behind the world
    /// in depth), each with the code its shader can leave out this frame and the spot slots whose sphere reaches its box.
    /// </summary>
    private void Select(RdWorldFrame frame, Vector3 eye, bool drawSky, RdFeatures frameFeatures)
    {
        List<RdWorldGeometry.Batch> batches = _geometry.Batches;
        frame.Reserve(batches.Count);
        if (_keys.Length < batches.Count)
        {
            _keys = new long[batches.Count];
            _order = new int[batches.Count];
        }

        int n = 0;
        for (int i = 0; i < batches.Count; i++)
        {
            RdWorldGeometry.Batch b = batches[i];
            if (b.Sky && !drawSky) continue;
            if (!Inside(b.Sky ? _skyPlanes : _worldPlanes, b.Bounds)) continue;
            float d = (b.Sky ? _skyOrigin : eye).DistanceSquaredTo(b.Bounds.GetCenter());
            long group = (b.Sky ? 2 : 0) + (b.Material.Masked ? 1 : 0);
            _keys[n] = (group << 32) | BitConverter.SingleToUInt32Bits(d);
            _order[n] = i;
            n++;
        }

        Array.Sort(_keys, _order, 0, n);
        _full = _noSpot = _neither = 0;
        for (int k = 0; k < n; k++)
        {
            RdWorldGeometry.Batch b = batches[_order[k]];
            // The sky zone keeps its whole material and every slot.
            int spots = -1, dynamicBits = -1;
            RdFeatures features = RdFeatures.None;
            if (!b.Sky)
            {
                spots = SpotsReaching(b);
                bool dynamic = DynamicReaching(b, out dynamicBits);
                if (!TightDynamic) dynamicBits = -1;
                // A draw a spot reaches keeps the dynamic term too (its mask is empty then): one shader fewer per material.
                features = frameFeatures | (spots == 0 ? RdFeatures.NoSpot : 0) | (dynamic || spots != 0 ? 0 : RdFeatures.NoDynamic);
                features = (features | ForcedOut) & ~ForcedIn;
                if ((features & RdFeatures.NoSpot) != 0) spots = 0;
            }

            if ((features & RdFeatures.NoSpot) == 0) _full++;
            else if ((features & RdFeatures.NoDynamic) == 0) _noSpot++;
            else _neither++;
            frame.Draws[k] = _order[k];
            frame.Features[k] = (byte)features;
            frame.SpotBits[k] = spots;
            frame.DynBits[k] = dynamicBits;
        }

        frame.DrawCount = n;
    }

    /// <summary>
    /// The slots that can light some point of the batch: on, its sphere and its frustum reach the batch's box, and its
    /// own zone (when it has one) is among the batch's. Every other slot adds exactly nothing to every pixel of it.
    /// </summary>
    private int SpotsReaching(RdWorldGeometry.Batch batch)
    {
        Aabb box = batch.Bounds;
        int bits = 0;
        for (int i = 0; i < _spots.Length && i < 16; i++)
        {
            Vector4 s = _spots[i];
            // Radius squared 0: the slot is off. A row that is not finite reaches everything, as the template's test.
            if (s.W <= 0f) continue;
            float radius = MathF.Sqrt(s.W);
            float reach = (radius * (1f + RelativeMargin)) + Margin;
            if (!float.IsFinite(s.X + s.Y + s.Z + s.W))
            {
                bits |= 1 << i;
                continue;
            }

            if (DistanceSquared(box, new Vector3(s.X, s.Y, s.Z)) >= reach * reach || (TightSpots && !SpotShadowLight.ReachesBox(i, box))) continue;
            float zone = SpotShadowLight.OwnZone(i);
            if (TightSpots && zone >= 0f && !batch.HasZone((int)MathF.Round(zone))) continue;
            bits |= 1 << i;
        }

        return bits;
    }

    /// <summary>
    /// The rows that can light some point of the batch (svm_one_light): bit i for row i below 32, and whether any row
    /// can at all. A row is left out when its colour is black, its radius stops short of the batch's box, its cone
    /// (a spot) misses the box's sphere, or its own zone is not among the batch's: it adds exactly nothing then.
    /// </summary>
    private bool DynamicReaching(RdWorldGeometry.Batch batch, out int bits)
    {
        bits = 0;
        bool any = false;
        Aabb box = batch.Bounds;
        Vector3 centre = box.GetCenter();
        float radius = box.Size.Length() * 0.5f;
        for (int i = 0; i < _lights.Count; i++)
        {
            DynamicRow l = _lights[i];
            bool reaches = true;
            if (float.IsFinite(l.Pos.X + l.Pos.Y + l.Pos.Z + l.Pos.W + l.Col.X + l.Col.Y + l.Col.Z + l.Spot.X + l.Spot.Y + l.Spot.Z + l.Spot.W))
            {
                if ((l.Col.X * l.Col.X) + (l.Col.Y * l.Col.Y) + (l.Col.Z * l.Col.Z) <= 0f) reaches = false;
                else if (l.Zone >= 0f && !batch.HasZone((int)MathF.Round(l.Zone))) reaches = false;
                else if (l.Pos.W >= 0f)
                {
                    var at = new Vector3(l.Pos.X, l.Pos.Y, l.Pos.Z);
                    float reach = (MathF.Max(l.Pos.W, 1e-5f) * (1f + RelativeMargin)) + Margin;
                    if (DistanceSquared(box, at) >= reach * reach) reaches = false;
                    else if (l.Spot.W > -1.5f && l.Spot.W > 0f && l.Spot.W < 1f)
                    {
                        // A spot lights only inside its cone (half angle acos(spot.w), under 90 degrees here): the box's
                        // sphere lies outside when its centre is further than its radius from the cone's side.
                        var axis = new Vector3(l.Spot.X, l.Spot.Y, l.Spot.Z);
                        Vector3 v = centre - at;
                        float along = v.Dot(axis);
                        float beside = (v - (axis * along)).Length();
                        float sin = MathF.Sqrt(1f - (l.Spot.W * l.Spot.W));
                        if ((beside * l.Spot.W) - (along * sin) > radius + Margin + (RelativeMargin * l.Pos.W)) reaches = false;
                    }
                }
            }

            if (!reaches) continue;
            any = true;
            if (i < 32) bits |= 1 << i;
        }

        return any;
    }

    private static float DistanceSquared(in Aabb box, Vector3 p)
    {
        Vector3 end = box.End;
        float dx = p.X < box.Position.X ? box.Position.X - p.X : p.X > end.X ? p.X - end.X : 0f;
        float dy = p.Y < box.Position.Y ? box.Position.Y - p.Y : p.Y > end.Y ? p.Y - end.Y : 0f;
        float dz = p.Z < box.Position.Z ? box.Position.Z - p.Z : p.Z > end.Z ? p.Z - end.Z : 0f;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    /// <summary>The six frustum planes of an OpenGL-style view-projection (inside: normal . p + d >= 0).</summary>
    private static void Planes(in Projection m, Plane[] planes)
    {
        Vector4 r0 = Row(m, 0), r1 = Row(m, 1), r2 = Row(m, 2), r3 = Row(m, 3);
        planes[0] = PlaneOf(r3 + r0);
        planes[1] = PlaneOf(r3 - r0);
        planes[2] = PlaneOf(r3 + r1);
        planes[3] = PlaneOf(r3 - r1);
        planes[4] = PlaneOf(r3 + r2);
        planes[5] = PlaneOf(r3 - r2);
    }

    private static Vector4 Row(in Projection m, int r) => new(m.X[r], m.Y[r], m.Z[r], m.W[r]);

    // Stored as (normal, -w) so that Godot's Plane.D convention (normal . p = D on the plane) holds.
    private static Plane PlaneOf(Vector4 v) => new(new Vector3(v.X, v.Y, v.Z), -v.W);

    private static bool Inside(Plane[] planes, in Aabb box)
    {
        Vector3 min = box.Position, max = box.End;
        foreach (Plane p in planes)
        {
            Vector3 n = p.Normal;
            var corner = new Vector3(n.X >= 0 ? max.X : min.X, n.Y >= 0 ? max.Y : min.Y, n.Z >= 0 ? max.Z : min.Z);
            if (n.Dot(corner) - p.D < 0f) return false;
        }

        return true;
    }
}
