using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Projectors;

/// <summary>The own-spy's ShadowProjector: a posed 128px silhouette, root tracking and a finite receiver volume.</summary>
internal sealed partial class SpyShadowProjector : Node3D
{
    internal const int TextureSize = 128;
    internal const float Range = 3f, LightDistance = 3f, CullDistance = 6f;
    internal static bool BenchmarkDisabled { get; set; }
    private static readonly bool s_disabled = System.Environment.GetEnvironmentVariable("SVM_PAWN_SHADOW") == "0";
    private readonly Node3D _world;
    private readonly SubViewport _capture;
    private readonly Camera3D _camera;
    private readonly Skeleton3D _skeleton = new();
    private readonly MeshInstance3D _decal;
    private readonly ShaderMaterial _material;
    private readonly List<(MeshInstance3D Source, MeshInstance3D Copy)> _casters = [];
    private readonly Dictionary<Mesh, (Vector3[] Vertices, int[] Indices)[]> _triangles = [];
    private readonly List<(MeshInstance3D Node, Transform3D Transform, Mesh Mesh)> _nearby = [];
    private SkeletalModel? _model;
    private Aabb? _receiverBounds;
    private bool _logged;
    internal Texture2D Mask => _capture.GetTexture();
    internal ShaderMaterial ReceiverMaterial => _material;

    public SpyShadowProjector(Node3D world)
    {
        _world = world;
        Name = "SpyShadowProjector";
        _capture = new SubViewport { Name = "SpyShadowTexture", Size = new Vector2I(TextureSize, TextureSize),
            OwnWorld3D = true, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(_capture); _capture.AddChild(_skeleton);
        _camera = new Camera3D { Current = true, Near = .01f, Far = LightDistance + Range,
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Transparent,
                TonemapMode = Godot.Environment.ToneMapper.Linear, AmbientLightSource = Godot.Environment.AmbientSource.Disabled } };
        _capture.AddChild(_camera);
        _material = new ShaderMaterial { Shader = ResponsiveSmokeShaders.Shared(
            "shader_type spatial;\nrender_mode unshaded, fog_disabled, skip_vertex_transform, depth_draw_never, blend_mul, cull_disabled;\n" +
            "#define SVM_PROJECTOR_DECAL\n#define SVM_PAWN_SHADOW_DECAL\n#define BLEND_MODULATE\n" +
            "#include \"res://Shaders/svm_world.gdshaderinc\"\n", "own spy shadow receiver"), RenderPriority = -126 };
        _material.SetShaderParameter("shadow_mask", _capture.GetTexture());
        _decal = new MeshInstance3D { Name = "SpyShadowReceivers", MaterialOverride = _material,
            Layers = RenderLayers.Effects, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_decal);
    }

    public void Draw(SkeletalModel? model, byte darkness, Vector3 lightDirection, int vision = 5, Camera3D? mainCamera = null)
    {
        if (s_disabled || BenchmarkDisabled || model is null || darkness == 0 || vision is 36 or 38 || !model.Root.IsVisibleInTree()) { HideShadow(); return; }
        bool changed = !ReferenceEquals(_model, model) || _casters.Count != model.Meshes.Count;
        if (!changed)
            for (int i = 0; i < _casters.Count; i++) changed |= _casters[i].Source != model.Meshes[i] || _casters[i].Copy.Mesh != model.Meshes[i].Mesh;
        if (changed) Bind(model);
        _skeleton.GlobalTransform = model.Skeleton.GlobalTransform;
        for (int bone = 0; bone < _skeleton.GetBoneCount(); bone++) _skeleton.SetBonePose(bone, model.Skeleton.GetBonePose(bone));
        foreach (var (source, copy) in _casters)
        {
            copy.GlobalTransform = source.GlobalTransform;
            copy.Visible = source.Visible;
        }
        Vector3 origin = model.Root.GlobalPosition + Vector3.Up * .05f;
        if (model.PackageBones.Count > 0 && model.Skeleton.FindBone(model.PackageBones[0]) is int root && root >= 0)
            origin = (model.Skeleton.GlobalTransform * model.CurrentBonePose(root)).Origin;
        if (mainCamera is not null && mainCamera.GlobalPosition.DistanceSquaredTo(origin) > CullDistance * CullDistance)
        { HideShadow(); return; }
        Vector3 up = lightDirection.LengthSquared() > .001f ? lightDirection.Normalized() : Vector3.Up;
        Aabb bounds = default; bool first = true;
        foreach (MeshInstance3D mesh in model.Meshes)
        {
            Aabb box = mesh.GlobalTransform * mesh.GetAabb();
            bounds = first ? box : bounds.Merge(box); first = false;
        }
        float radius = MathF.Max(.15f, bounds.Size.Length() * .5f);
        float half = radius * TextureSize / (TextureSize - 3f);
        _camera.GlobalPosition = origin + up * LightDistance;
        _camera.LookAt(origin, MathF.Abs(up.Dot(Vector3.Forward)) < .99f ? Vector3.Forward : Vector3.Right);
        _camera.Fov = 2f * Mathf.RadToDeg(MathF.Atan2(half, LightDistance));
        Vector3 right = _camera.GlobalBasis.X, down = -_camera.GlobalBasis.Y;
        _material.SetShaderParameter("shadow_origin", origin);
        _material.SetShaderParameter("shadow_forward", -up);
        _material.SetShaderParameter("shadow_u", right / (2f * half));
        _material.SetShaderParameter("shadow_v", down / (2f * half));
        _material.SetShaderParameter("shadow_darkness", darkness / 255f);
        _material.SetShaderParameter("shadow_range", Range);
        _material.SetShaderParameter("shadow_cull", CullDistance);
        bool movedReceiver = false;
        foreach (var (node, transform, mesh) in _nearby)
            if (!GodotObject.IsInstanceValid(node) || node.GlobalTransform != transform || node.Mesh != mesh || !node.IsVisibleInTree()) { movedReceiver = true; break; }
        Aabb wanted = new(origin, Vector3.Zero);
        for (int end = 0; end < 2; end++) for (int u = -1; u <= 1; u += 2) for (int v = -1; v <= 1; v += 2)
            wanted = wanted.Expand(origin - up * (end * Range) + right * (u * half) + down * (v * half));
        if (_receiverBounds is not { } cached || !cached.Encloses(wanted) || movedReceiver)
            Receivers(wanted.Grow(.5f));
        _decal.Visible = _decal.Mesh is not null;
        _capture.RenderTargetUpdateMode = _decal.Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (!_logged && _decal.Visible)
        {
            GD.Print($"SVM_PAWN_SHADOW darkness={darkness} origin={origin} half={half:F3} texture={TextureSize}"); _logged = true;
        }
    }

    public void HideShadow()
    {
        _decal.Visible = false;
        _capture.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    public override void _Ready()
    {
        GetTree().NodeAdded += ReceiverChanged; GetTree().NodeRemoved += ReceiverChanged;
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= ReceiverChanged; GetTree().NodeRemoved -= ReceiverChanged;
    }

    private void ReceiverChanged(Node node)
    {
        if (node is MeshInstance3D && GodotObject.IsInstanceValid(_world) && _world.IsAncestorOf(node)) _receiverBounds = null;
    }

    private void Bind(SkeletalModel model)
    {
        foreach (var (_, copy) in _casters) { _capture.RemoveChild(copy); copy.QueueFree(); }
        _casters.Clear(); _skeleton.ClearBones(); _model = model;
        for (int bone = 0; bone < model.Skeleton.GetBoneCount(); bone++) _skeleton.AddBone(model.Skeleton.GetBoneName(bone));
        for (int bone = 0; bone < model.Skeleton.GetBoneCount(); bone++)
        {
            _skeleton.SetBoneParent(bone, model.Skeleton.GetBoneParent(bone));
            _skeleton.SetBoneRest(bone, model.Skeleton.GetBoneRest(bone));
        }
        foreach (MeshInstance3D source in model.Meshes)
        {
            var copy = new MeshInstance3D { Mesh = source.Mesh, Skin = source.Skin, ExtraCullMargin = 4,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _capture.AddChild(copy); copy.Skeleton = copy.GetPathTo(_skeleton);
            if (source.Mesh is { } mesh)
                for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                    copy.SetSurfaceOverrideMaterial(surface, Silhouette(source.GetActiveMaterial(surface)));
            _casters.Add((source, copy));
        }
        _receiverBounds = null; _logged = false;
    }

    private static ShaderMaterial Silhouette(Material? source)
    {
        string code = source is ShaderMaterial { Shader: { } shader } ? ResponsiveSmokeShaders.BaseCode(shader) : "";
        bool transparent = code.Contains("#define BLEND_TRANSLUCENT", StringComparison.Ordinal) || code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal) ||
            code.Contains("#define BLEND_MODULATE", StringComparison.Ordinal) || code.Contains("#define BLEND_DARKEN", StringComparison.Ordinal);
        bool masked = code.Contains("#define BLEND_MASKED", StringComparison.Ordinal);
        var material = new ShaderMaterial { Shader = ResponsiveSmokeShaders.Shared(
            "shader_type spatial;\nrender_mode unshaded, fog_disabled, cull_disabled;\nuniform sampler2D tex_diffuse;\nvoid fragment() {\n" +
            (transparent ? "discard;\n" : masked ? "if (texture(tex_diffuse, UV).a < 0.5) discard;\n" : "") +
            "ALBEDO = vec3(1.0);\n}\n", "spy shadow silhouette") };
        if (masked && source is ShaderMaterial original) material.SetShaderParameter("tex_diffuse", original.GetShaderParameter("tex_diffuse"));
        return material;
    }

    private void Receivers(Aabb reach)
    {
        _receiverBounds = reach; _nearby.Clear();
        var positions = new List<Vector3>();
        var polygon = new List<Vector3>(10); var clipped = new List<Vector3>(10);
        float Distance(Vector3 p, int plane) => plane switch
        { 0 => p.X - reach.Position.X, 1 => reach.End.X - p.X, 2 => p.Y - reach.Position.Y,
            3 => reach.End.Y - p.Y, 4 => p.Z - reach.Position.Z, _ => reach.End.Z - p.Z };
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
            for (int plane = 0; plane < 6 && polygon.Count > 0; plane++)
            {
                clipped.Clear(); Vector3 from = polygon[^1]; float fd = Distance(from, plane);
                foreach (Vector3 to in polygon)
                {
                    float td = Distance(to, plane);
                    if ((fd >= 0) != (td >= 0)) clipped.Add(from.Lerp(to, fd / (fd - td)));
                    if (td >= 0) clipped.Add(to);
                    from = to; fd = td;
                }
                (polygon, clipped) = (clipped, polygon);
            }
            for (int i = 1; i + 1 < polygon.Count; i++)
            { positions.Add(polygon[0]); positions.Add(polygon[i]); positions.Add(polygon[i+1]); }
        }
        void Visit(Node node)
        {
            if (node is MeshInstance3D { Mesh: { } mesh, Skin: null } receiver && receiver.IsVisibleInTree() &&
                (receiver.Layers & (RenderLayers.World | RenderLayers.StaticWorldAll)) != 0 && reach.Intersects(receiver.GlobalTransform * receiver.GetAabb()))
            {
                Transform3D transform = receiver.GlobalTransform;
                _nearby.Add((receiver, transform, mesh));
                if (!_triangles.TryGetValue(mesh, out var surfaces))
                {
                    surfaces = new (Vector3[], int[])[mesh.GetSurfaceCount()];
                    for (int surface = 0; surface < surfaces.Length; surface++)
                    {
                        if (mesh is ArrayMesh arrayMesh && arrayMesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles) continue;
                        var arrays = mesh.SurfaceGetArrays(surface);
                        surfaces[surface] = (arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(), arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
                    }
                    _triangles[mesh] = surfaces;
                }
                for (int surface = 0; surface < surfaces.Length; surface++)
                {
                    if (receiver.GetActiveMaterial(surface) is { } active && active.HasMeta("authored_displacement")) continue;
                    if (receiver.GetActiveMaterial(surface) is ShaderMaterial { Shader: { } shader })
                    {
                        string code = ResponsiveSmokeShaders.BaseCode(shader);
                        if (code.Contains("#define BLEND_", StringComparison.Ordinal) || code.Contains("#define BILLBOARD", StringComparison.Ordinal)) continue;
                    }
                    var (vertices, indices) = surfaces[surface];
                    if (vertices is null) continue;
                    int count = indices.Length > 0 ? indices.Length : vertices.Length;
                    for (int i = 0; i + 2 < count; i += 3)
                    {
                        Vector3 a = transform * vertices[indices.Length > 0 ? indices[i] : i],
                            b = transform * vertices[indices.Length > 0 ? indices[i+1] : i+1],
                            c = transform * vertices[indices.Length > 0 ? indices[i+2] : i+2];
                        if (reach.Intersects(new Aabb(a, Vector3.Zero).Expand(b).Expand(c).Grow(.001f)))
                            Triangle(a, b, c);
                    }
                }
            }
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(_world);
        if (positions.Count == 0) { _decal.Mesh = null; return; }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        var result = new ArrayMesh(); result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _decal.Mesh = result;
    }
}
