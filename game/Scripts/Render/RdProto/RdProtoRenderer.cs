using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render.RdProto;

/// <summary>
/// SVM_RD_PROTO=1: a measurement prototype of a custom world renderer on Godot's RenderingDevice. Godot's own 3D
/// drawing of the main viewport is switched off (Viewport.Disable3D, and the auxiliary sky/spot/flare SubViewports are
/// deactivated); the merged cell geometry MapBuilder made (<see cref="RdProtoGeometry"/>) is drawn on the render
/// thread (<see cref="RdProtoGpu"/>) into an RGBA8 colour + D32 depth target with the gameplay camera, and shown by
/// a full-screen TextureRect (Texture2DRD) under every canvas layer, so the HUD still draws over it.
/// Per frame on the main thread: frustum culling per batch, sort (opaque front to back, masked, sky, blended back to
/// front), the view-projections. Prints "SVM_RD_PROTO gpuMs=..." once a second.
/// Not drawn: gameplay meshes, scenery, characters, particles, fog, dynamic lights, vision modes.
/// </summary>
public partial class RdProtoRenderer : Node
{
    public static bool Enabled => RdProtoGeometry.Enabled;

    // Depth slice for the sky zone in reverse Z: the sky draws in [0, SkyDepth], the world in [SkyDepth, 1].
    private const float SkyDepth = 1f / 1024f;

    private readonly RenderMap _map;
    private readonly FaithfulView _view;
    private readonly RdProtoGeometry _geometry;
    private readonly RdProtoGpu _gpu;
    private readonly Callable _render;
    private readonly Vector3 _skyOrigin;
    private readonly Plane[] _worldPlanes = new Plane[6];
    private readonly Plane[] _skyPlanes = new Plane[6];
    private long[] _keys = new long[256];
    private readonly Texture2Drd _shown = new();
    private TextureRect? _rect;
    private Rid _shownColor;
    private double _sinceReport;
    private int _framesSinceReport;
    private int _lastDraws, _lastTriangles;
    private RdProtoFrameEffect? _effect;
    private bool _drawSky = true;

    internal RdProtoRenderer(RenderMap map, FaithfulView view)
    {
        Name = "RdProto";
        _map = map;
        _view = view;
        _geometry = map.RdGeometry ?? throw new InvalidOperationException("SVM_RD_PROTO: the map was built without RdProtoGeometry");
        _gpu = new RdProtoGpu(_geometry, CullFromEnvironment());
        RdProtoGpu gpu = _gpu;
        // The target of this callable is a plain object, so it outlives the node (see RdProtoGpu).
        _render = Callable.From(gpu.Render);
        _skyOrigin = map.Lighting.SkyLocation is { } sky ? UeSpace.ToGodot(sky) : Vector3.Zero;
        // After the play camera (PlayRoot, PlayCamera) and FpsBenchmark (1000) placed this frame's view.
        ProcessPriority = 10000;
    }

    /// <summary>SVM_RD_PROTO_CULL=none|front overrides back-face culling (a check that the winding is right).</summary>
    private static RenderingDevice.PolygonCullMode CullFromEnvironment() => System.Environment.GetEnvironmentVariable("SVM_RD_PROTO_CULL") switch
    {
        "none" => RenderingDevice.PolygonCullMode.Disabled,
        "front" => RenderingDevice.PolygonCullMode.Front,
        _ => RenderingDevice.PolygonCullMode.Back,
    };

    public override void _Ready()
    {
        if (RdProtoGeometry.InFrame)
        {
            if (_map.Lighting.OutdoorSky)
            {
                // Godot's sky pass needs its own background mode: such a map stays with Godot.
                GD.Print("SVM_RD_PROTO in-frame: off (outdoor sky), " + _map.MapId);
                SetProcess(false);
                return;
            }

            // Godot keeps drawing everything but the merged cells: they leave the main view (the shadow cameras still
            // draw them) and this renderer draws them from the camera's compositor between Godot's depth pre-pass and
            // its opaque pass. The environment keeps the background, so that pass loads what was drawn here.
            _effect = new RdProtoFrameEffect(_gpu);
            Compositor compositor = _view.Camera.Compositor ??= new Compositor();
            Godot.Collections.Array<CompositorEffect> effects = compositor.CompositorEffects;
            effects.Insert(0, _effect);
            compositor.CompositorEffects = effects;
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"SVM_RD_PROTO in-frame: {_map.MapId}, {_geometry.Batches.Count} batches, {_geometry.Vertices.Length} vertices, {_geometry.Indices.Length / 3} triangles"));
            return;
        }

        // Under every other canvas layer (the HUD is at 10, the camera occlusion mask at 9).
        var layer = new CanvasLayer { Name = "RdProtoLayer", Layer = -100 };
        _rect = new TextureRect
        {
            Name = "RdProtoView",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_rect);
        AddChild(layer);
        // Godot draws nothing 3D in the main viewport: no pre-pass, no HDR target, no tonemap, no compositor effects.
        GetViewport().Disable3D = true;
        DisableAuxViewports();
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_RD_PROTO on: {_map.MapId}, {_geometry.Batches.Count} batches, {_geometry.Vertices.Length} vertices, {_geometry.Indices.Length / 3} triangles ({_geometry.Skipped} buckets without a material entry skipped)"));
    }

    public override void _ExitTree()
    {
        if (_effect is not null)
        {
            if (GodotObject.IsInstanceValid(_view.Camera) && _view.Camera.Compositor is { } compositor)
            {
                Godot.Collections.Array<CompositorEffect> effects = compositor.CompositorEffects;
                effects.Remove(_effect);
                compositor.CompositorEffects = effects;
                RestoreGodotWorld();
            }

            RdProtoGpu inFrameGpu = _gpu;
            RenderingServer.CallOnRenderThread(Callable.From(inFrameGpu.Free));
            return;
        }

        GetViewport().Disable3D = false;
        if (_rect is not null) _rect.Texture = null;
        _shown.TextureRdRid = default;
        // Queued after the Texture2DRD let go of the target (its own change is queued on the render thread too).
        RdProtoGpu gpu = _gpu;
        RenderingServer.CallOnRenderThread(Callable.From(gpu.Free));
    }

    public override void _Process(double delta)
    {
        if (_gpu.Failed)
        {
            // In-frame: Godot draws the cells again.
            if (_effect is not null) RestoreGodotWorld();
            return;
        }

        bool inFrame = _effect is not null;
        if (!inFrame) ShowLatest();
        Camera3D camera = _view.Camera;
        if (!camera.IsInsideTree()) return;
        _drawSky = !inFrame || _view.SkyInMainView;
        if (inFrame)
        {
            uint hidden = RenderLayers.StaticWorld | (_drawSky ? RenderLayers.Sky : 0u);
            if ((camera.CullMask & hidden) != 0) camera.CullMask &= ~hidden;
            if (camera.Environment is { BackgroundMode: Godot.Environment.BGMode.Color } env) env.BackgroundMode = Godot.Environment.BGMode.Keep;
        }

        Viewport viewport = GetViewport();
        Vector2 visible = viewport.GetVisibleRect().Size;
        // The 3D resolution Godot would render at (Scaling3DScale, as the benchmark's render scale).
        float scale = viewport.Scaling3DScale;
        var size = new Vector2I(Math.Max(1, (int)(visible.X * scale)), Math.Max(1, (int)(visible.Y * scale)));

        // The same view and projection Godot's camera would use (OpenGL clip space; converted for Vulkan below).
        Transform3D eye = camera.GetCameraTransform();
        Projection projection = camera.GetCameraProjection();
        Projection world = projection * new Projection(eye.AffineInverse());
        Projection sky = projection * new Projection(new Transform3D(eye.Basis, _skyOrigin).AffineInverse());
        Planes(world, _worldPlanes);
        Planes(sky, _skyPlanes);

        RdProtoFrame frame = _gpu.Rent();
        frame.Size = size;
        Color clear = camera.Environment?.BackgroundColor.SrgbToLinear() ?? new Color(0f, 0f, 0f);
        frame.Clear = new Color(clear.R, clear.G, clear.B, 1f);
        frame.SkyOrigin = _skyOrigin;
        Span<float> ubo = MemoryMarshal.Cast<byte, float>(frame.Ubo.AsSpan());
        Write(ToVulkan(world, 1f - SkyDepth, SkyDepth), ubo[..16]);
        Write(ToVulkan(sky, SkyDepth, 0f), ubo[16..32]);
        ubo[32] = RenderK.LightmapScale;
        ubo[33] = ubo[34] = ubo[35] = 0f;
        CullAndSort(frame, eye.Origin);
        _gpu.Submit(frame);
        // In-frame, the compositor effect draws the frame inside Godot's own render of the main view.
        if (!inFrame) RenderingServer.CallOnRenderThread(_render);

        _framesSinceReport++;
        _sinceReport += delta;
        if (_sinceReport >= 1.0)
        {
            Report();
            if (!inFrame) DisableAuxViewports();
        }
    }

    /// <summary>In-frame: Godot's main view draws the cells and the sky zone itself again, over its own clear.</summary>
    private void RestoreGodotWorld()
    {
        Camera3D camera = _view.Camera;
        camera.CullMask |= RenderLayers.StaticWorld | (_view.SkyInMainView ? RenderLayers.Sky : 0u);
        if (camera.Environment is { BackgroundMode: Godot.Environment.BGMode.Keep } env) env.BackgroundMode = Godot.Environment.BGMode.Color;
    }

    /// <summary>Points the Texture2DRD at the render thread's newest colour target (new after a resize).</summary>
    private void ShowLatest()
    {
        Rid latest = _gpu.LatestColor;
        if (!latest.IsValid || latest == _shownColor) return;
        _shown.TextureRdRid = latest;
        _shownColor = latest;
        if (_rect is not null) _rect.Texture = _shown;
    }

    private void Report()
    {
        (double gpuMs, double recordMs, int samples) = RdProtoGpu.TakeSecond();
        double fps = _framesSinceReport / _sinceReport;
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_RD_PROTO gpuMs={gpuMs:F3} recordMs={recordMs:F3} frameMs={1000.0 / fps:F3} fps={fps:F1} draws={_lastDraws} tris={_lastTriangles} samples={samples}"));
        _sinceReport = 0;
        _framesSinceReport = 0;
    }

    /// <summary>The sky, spot shadow and flare SubViewports draw 3D for Godot's renderer only: off while measuring.</summary>
    private static void DisableAuxViewports()
    {
        foreach (SubViewport vp in Diagnostics.ViewportTimings.Alive())
        {
            RenderingServer.ViewportSetActive(vp.GetViewportRid(), false);
        }
    }

    /// <summary>
    /// The visible batches in draw order: world opaque and masked front to back, then the sky zone (its own eye,
    /// depth behind the world, so the world's depth rejects it early), then blended sky and world back to front.
    /// </summary>
    private void CullAndSort(RdProtoFrame frame, Vector3 eye)
    {
        List<RdProtoGeometry.Batch> batches = _geometry.Batches;
        if (frame.Draws.Length < batches.Count) frame.Draws = new int[batches.Count];
        if (_keys.Length < batches.Count) _keys = new long[batches.Count];
        int n = 0, triangles = 0;
        for (int i = 0; i < batches.Count; i++)
        {
            RdProtoGeometry.Batch b = batches[i];
            if (b.Sky && !_drawSky) continue;
            if (!Inside(b.Sky ? _skyPlanes : _worldPlanes, b.Bounds)) continue;
            float d = (b.Sky ? _skyOrigin : eye).DistanceSquaredTo(b.Bounds.GetCenter());
            bool blended = b.Blend is not (MaterialBlend.Opaque or MaterialBlend.Masked);
            long group = b.Sky
                ? blended ? 4 : b.Blend == MaterialBlend.Masked ? 3 : 2
                : blended ? 5 : b.Blend == MaterialBlend.Masked ? 1 : 0;
            uint bits = BitConverter.SingleToUInt32Bits(d);
            _keys[n] = (group << 32) | (blended ? ~bits : bits);
            frame.Draws[n] = i;
            triangles += (int)(b.IndexCount / 3);
            n++;
        }

        Array.Sort(_keys, frame.Draws, 0, n);
        frame.DrawCount = n;
        _lastDraws = n;
        _lastTriangles = triangles;
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

    /// <summary>
    /// An OpenGL-style clip transform made Vulkan's: y flipped (framebuffer rows top down, as Godot shows textures),
    /// reverse Z (near 1, far 0), then squeezed into [bias, bias + scale] of the depth range.
    /// </summary>
    private static Projection ToVulkan(in Projection m, float scale, float bias)
    {
        static Vector4 Fix(Vector4 c, float scale, float bias) =>
            new(c.X, -c.Y, (scale * 0.5f * (c.W - c.Z)) + (bias * c.W), c.W);
        return new Projection(Fix(m.X, scale, bias), Fix(m.Y, scale, bias), Fix(m.Z, scale, bias), Fix(m.W, scale, bias));
    }

    /// <summary>Column-major, as GLSL reads a std140 mat4.</summary>
    internal static void Write(in Projection m, Span<float> to)
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
}
