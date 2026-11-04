using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Game.Svm;
using OpenScct.Physics.Conveyors;
using OpenScctGame.Render;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Views;

/// <summary>
/// Draws the moving parts of the map's conveyors (spec/additions/conveyors.md#look). The editor builds each look into
/// render/world.glb, lit like the rest of the map; here the belt and roller textures and the map's own scroll targets
/// slide with the belt, escalator steps move along their path, and the items ride the belts (<see cref="ConveyorItemFlow"/>,
/// handed on between linked conveyors). Everything is placed from the conveyors' replicated phases at the displayed
/// server time, so every client shows the same belts. Reads the simulation and never writes it.
/// </summary>
public sealed class ConveyorViews
{
    private const string ScrollParameter = "svm_scroll";
    private const float RelightSeconds = 0.1f;

    // An item without a mesh found still takes this much room along the belt.
    private const float DefaultItemLength = 32f;

    private readonly RenderMap _map;
    private readonly Node3D _itemRoot;
    private readonly List<Belt> _belts = [];
    private readonly Dictionary<Conveyor, Belt> _byConveyor = new(ReferenceEqualityComparer.Instance);
    private readonly ConveyorItemFlow? _flow;
    private readonly List<ConveyorItemPlace> _places = [];
    private readonly List<MeshInstance3D> _itemNodes = [];
    private readonly List<ConveyorStepFace> _faces = [];
    private float _relightClock;
    private double _drawnTick;

    /// <summary>Binds the conveyors <paramref name="loaded"/> created to the map's nodes; adds item nodes under <paramref name="parent"/>.</summary>
    public ConveyorViews(RenderMap map, MapLoadResult loaded, Node3D parent)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(parent);
        var conveyors = new List<Conveyor>();
        foreach (ActorRecord record in loaded.Gameplay.Actors)
        {
            if (loaded.ActorOf(record) is not Conveyor conveyor)
            {
                continue;
            }

            var belt = new Belt(record.Id, conveyor);
            AddScroll(belt, record.Id + ConveyorLayout.BeltPart, ConveyorLayout.BeltTile, 1, null);
            AddScroll(belt, record.Id + ConveyorLayout.RollersPart, 2f * MathF.PI * ConveyorLayout.RollerRadius, 0, null);
            foreach (FConveyorScroll target in conveyor.ScrollTargets)
            {
                if (target.Target is { Length: > 0 } id && target.UnitsPerTile != 0f)
                {
                    AddScroll(belt, id, target.UnitsPerTile, target.Axis, target.Material.Path);
                }
            }

            if (conveyor.Look == EConveyorLook.CL_Escalator && _map.Gameplay.TryGetValue(record.Id + ConveyorLayout.StepsPart, out GameplayMesh? steps))
            {
                belt.Steps = MovingSteps.From(steps.Node, conveyor);
                Steps += belt.Steps is null ? 0 : 1;
            }

            AddItemMeshes(belt);
            _belts.Add(belt);
            _byConveyor[conveyor] = belt;
            conveyors.Add(conveyor);
        }

        _itemRoot = new Node3D { Name = "ConveyorItems" };
        parent.AddChild(_itemRoot);
        if (conveyors.Count > 0)
        {
            _flow = new ConveyorItemFlow(conveyors, c => _byConveyor[c].ItemLength * c.ItemScale);
        }
    }

    /// <summary>Conveyors bound, for reports.</summary>
    public int Count => _belts.Count;

    /// <summary>Escalators whose steps move.</summary>
    public int Steps { get; private set; }

    /// <summary>Items riding at the last draw, for reports.</summary>
    public int Items => _flow?.Count ?? 0;

    /// <summary>
    /// Places every moving part at <paramref name="serverTick"/>, the server time this frame shows (fractional ticks).
    /// </summary>
    public void Draw(double serverTick, float dt)
    {
        _drawnTick = serverTick;
        _relightClock -= dt;
        bool relight = _relightClock <= 0f;
        if (relight)
        {
            _relightClock = RelightSeconds;
        }

        foreach (Belt belt in _belts)
        {
            double phase = belt.Conveyor.PhaseAt(serverTick);
            foreach (Scroll s in belt.Scrolls)
            {
                // The texture moves with the belt: its offset runs against the direction of travel.
                float offset = -(float)(phase / s.UnitsPerTile - Math.Floor(phase / s.UnitsPerTile));
                var value = s.Axis == 0 ? new Vector2(offset, 0f) : new Vector2(0f, offset);
                foreach (ShaderMaterial m in s.Materials)
                {
                    m.SetShaderParameter(ScrollParameter, value);
                }
            }

            belt.Steps?.Place(belt.Conveyor, phase, _faces);
        }

        DrawItems(serverTick, relight);
    }

    /// <summary>
    /// How far to raise a walking pawn's mesh so its feet rest on the escalator tread under it (negative lowers it): a
    /// pawn walks on the slope through the steps' inner corners. 0 off escalators. Uses the time of the last
    /// <see cref="Draw"/>, so the feet match the steps drawn.
    /// </summary>
    public float FeetLift(FVector feet, float radius)
    {
        foreach (Belt belt in _belts)
        {
            if (belt.Steps is not null && belt.Conveyor.TryTreadUnder(feet, radius, _drawnTick, out float tread))
            {
                return tread - feet.Z;
            }
        }

        return 0f;
    }

    private void DrawItems(double serverTick, bool relight)
    {
        if (_flow is null)
        {
            return;
        }

        _flow.AdvanceTo((int)Math.Ceiling(serverTick));
        _flow.Place(serverTick, _places);
        int used = 0;
        foreach (ConveyorItemPlace place in _places)
        {
            Belt origin = _byConveyor[place.Origin];
            if (place.Scale <= 0f || origin.Meshes.Count == 0)
            {
                continue;
            }

            MeshInstance3D node = ItemNode(used++);
            node.Visible = true;
            // Tilted with the belt, turned about its up, and lifted so the mesh's lowest point rests on it.
            int mesh = place.Mesh;
            node.Mesh = origin.Meshes[mesh];
            var scale = new FVector(place.Scale, place.Scale, place.Scale);
            node.Transform = UeSpace.TransformToGodot(place.Location, place.Belt) *
                             UeSpace.TransformToGodot(default, new FRotator(0, place.Spin, 0), scale, default) *
                             new Transform3D(Basis.Identity, new Vector3(0f, origin.Lifts[mesh], 0f));
            if (relight)
            {
                ActorLighting.Apply(node, _map.LightActor(place.Location, origin.ItemRadius, 0));
            }
        }

        for (int i = used; i < _itemNodes.Count; i++)
        {
            _itemNodes[i].Visible = false;
        }
    }

    /// <summary>The <paramref name="index"/>th item node, made when the belts first carry that many items.</summary>
    private MeshInstance3D ItemNode(int index)
    {
        while (_itemNodes.Count <= index)
        {
            var node = new MeshInstance3D { Name = $"Item{_itemNodes.Count}" };
            node.SetInstanceShaderParameter(GameplayObjects.VisionActorParameter, SurfaceBuilder.DefaultVision);
            _itemRoot.AddChild(node);
            _itemNodes.Add(node);
        }

        return _itemNodes[index];
    }

    /// <summary>
    /// Gives a map node its own copies of its materials and of their later passes (the vision passes sample the same
    /// texture), so this conveyor's scroll does not move the same texture on other meshes. With
    /// <paramref name="material"/> (an asset path) only the surfaces drawn with that material scroll.
    /// </summary>
    private void AddScroll(Belt belt, string actorId, float unitsPerTile, byte axis, string? material)
    {
        if (!_map.Gameplay.TryGetValue(actorId, out GameplayMesh? entry) || entry.Node.Mesh is not { } mesh)
        {
            return;
        }

        var materials = new List<ShaderMaterial>();
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            if ((entry.Node.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i)) is ShaderMaterial shared &&
                (material is null || DrawsMaterial(shared, material)))
            {
                entry.Node.SetSurfaceOverrideMaterial(i, OwnCopy(shared, materials));
            }
        }

        if (materials.Count == 0 && material is not null)
        {
            _map.Warnings.Add($"conveyor {belt.Id}: {actorId} has no surface drawn with {material} to scroll");
        }

        belt.Scrolls.Add(new Scroll(materials, unitsPerTile, axis));
    }

    /// <summary>
    /// Whether a compiled material is the map's material <paramref name="asset"/> (Package.Group.Name): its name is the
    /// render material's, Package.Name with the exporter's variant suffixes after "__".
    /// </summary>
    private static bool DrawsMaterial(ShaderMaterial shared, string asset)
    {
        string name = shared.ResourceName;
        int cut = name.IndexOf("__", StringComparison.Ordinal);
        string root = cut >= 0 ? name[..cut] : name;
        int dot = asset.IndexOf('.', StringComparison.Ordinal);
        string shortPath = dot < 0 ? asset : asset[..dot] + asset[asset.LastIndexOf('.')..];
        return string.Equals(root, asset, StringComparison.OrdinalIgnoreCase) || string.Equals(root, shortPath, StringComparison.OrdinalIgnoreCase);
    }

    private static ShaderMaterial OwnCopy(ShaderMaterial shared, List<ShaderMaterial> copies)
    {
        var own = (ShaderMaterial)shared.Duplicate();
        copies.Add(own);
        if (own.NextPass is ShaderMaterial next)
        {
            own.NextPass = OwnCopy(next, copies);
        }

        return own;
    }

    /// <summary>The conveyor's item meshes, how far each is lifted to rest on the belt, and the room an item takes.</summary>
    private void AddItemMeshes(Belt belt)
    {
        var found = new List<(Mesh? Mesh, float Lift)>();
        float length = 0f;
        foreach (OpenScct.Core.Uc.AssetRef asset in belt.Conveyor.ItemMeshes)
        {
            Mesh? mesh = FindMesh(asset.Path);
            if (mesh is null)
            {
                _map.Warnings.Add($"conveyor {belt.Id}: item mesh {asset.Path} is not in the map's render data");
                found.Add((null, 0f));
                continue;
            }

            Aabb bounds = mesh.GetAabb();
            found.Add((mesh, -bounds.Position.Y));
            belt.ItemRadius = MathF.Max(belt.ItemRadius, UeSpace.UnitsPerMetre * bounds.Size.Length() * 0.5f);
            // Items turn about their up, so the room is the widest flat extent.
            length = MathF.Max(length, UeSpace.UnitsPerMetre * MathF.Max(bounds.Size.X, bounds.Size.Z));
        }

        belt.ItemLength = length > 0f ? length : DefaultItemLength;
        // A missing mesh draws as the first one found, so the seed picks the same items everywhere; none found draws none.
        int first = found.FindIndex(m => m.Mesh is not null);
        if (first < 0)
        {
            return;
        }

        foreach ((Mesh? mesh, float lift) in found)
        {
            belt.Meshes.Add(mesh ?? found[first].Mesh!);
            belt.Lifts.Add(mesh is null ? found[first].Lift : lift);
        }
    }

    /// <summary>An item mesh: built for actors when no placed mesh uses it, else the mesh of a placed gameplay node.</summary>
    private Mesh? FindMesh(string? asset)
    {
        if (asset is null)
        {
            return null;
        }

        if (_map.SwapMeshes.TryGetValue(asset, out ArrayMesh? swap))
        {
            return swap;
        }

        foreach (GameplayMesh entry in _map.Gameplay.Values)
        {
            if (entry.Light == LightSource.Actor && entry.Node.Mesh is { } mesh &&
                string.Equals(mesh.ResourceName, asset, StringComparison.OrdinalIgnoreCase))
            {
                return mesh;
            }
        }

        return null;
    }

    private sealed class Belt(string id, Conveyor conveyor)
    {
        public string Id { get; } = id;
        public Conveyor Conveyor { get; } = conveyor;
        public List<Scroll> Scrolls { get; } = [];

        /// <summary>The item meshes, one per <see cref="Conveyor.ItemMeshes"/> entry; empty when none was found.</summary>
        public List<Mesh> Meshes { get; } = [];

        /// <summary>Per item mesh: how far to raise it (Godot units, before scaling) so its lowest point is at its pivot.</summary>
        public List<float> Lifts { get; } = [];
        public float ItemRadius { get; set; }

        /// <summary>The room one item takes along the belt at scale 1 (uu).</summary>
        public float ItemLength { get; set; }

        public MovingSteps? Steps { get; set; }
    }

    private sealed record Scroll(List<ShaderMaterial> Materials, float UnitsPerTile, byte Axis);

    /// <summary>
    /// An escalator's steps, rebuilt each frame where the phase puts them (<see cref="ConveyorSteps"/>) on the map's
    /// <c>.Steps</c> node, with its material. The baked light stays where it was baked: each new vertex takes the light
    /// the baked steps had at the same place along the path, on the same side and facing the same way, blended between
    /// the samples either side of it. The vertex alpha keeps the zone the baked steps had there and the Echelon stream
    /// is copied, so zone-limited, dynamic and spot lights, flares and the vision ambient light the moving steps as they
    /// lit the baked ones.
    /// </summary>
    private sealed class MovingSteps
    {
        // Light samples per this many uu along the path.
        private const float Bin = 8f;

        private readonly Material? _material;
        private readonly Transform3D _toLocal;
        private readonly Sample[,]? _light;
        private readonly float? _echelon;
        private readonly ArrayMesh _mesh = new();
        private readonly List<Vector3> _points = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<Vector2> _uvs = [];
        private readonly List<Color> _colours = [];
        private readonly List<int> _indices = [];
        private readonly float _u;

        private MovingSteps(MeshInstance3D node, Material? material, Sample[,]? light, float? echelon, float u)
        {
            _material = material;
            _toLocal = World(node).AffineInverse();
            _light = light;
            _echelon = echelon;
            _u = u;
            node.Mesh = _mesh;
        }

        /// <summary>Takes over the node: reads the light baked into its steps, then draws them moving.</summary>
        public static MovingSteps? From(MeshInstance3D node, Conveyor conveyor)
        {
            if (node.Mesh is not ArrayMesh baked || baked.GetSurfaceCount() == 0)
            {
                return null;
            }

            ConveyorPath path = conveyor.WorldPath;
            int bins = Math.Max(1, (int)MathF.Ceiling(path.Length / Bin) + 1);
            // [bin, side * 2 + facing]: left or right half, tread or riser.
            var sum = new Vector4[bins, 4];
            var zones = new Dictionary<byte, int>?[bins, 4];
            Transform3D toWorld = World(node);
            bool vertexLit = false;
            float? echelon = null;
            for (int s = 0; s < baked.GetSurfaceCount(); s++)
            {
                Godot.Collections.Array arrays = baked.SurfaceGetArrays(s);
                Vector3[] points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                Color[] colours = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                // The map builder writes one Echelon code for the whole actor (SurfaceBuilder.Echelon).
                if (echelon is null && arrays[(int)Mesh.ArrayType.Custom1] is { VariantType: Variant.Type.PackedFloat32Array } custom1 &&
                    custom1.AsFloat32Array() is { Length: > 0 } codes)
                {
                    echelon = codes[0];
                }

                vertexLit |= colours.Length > 0;
                for (int i = 0; i < points.Length && i < colours.Length; i++)
                {
                    FVector at = UeSpace.ToUe(toWorld * points[i]);
                    if (!path.Project(at, float.MaxValue, out ConveyorProjection p))
                    {
                        continue;
                    }

                    bool up = i < normals.Length && (toWorld.Basis * normals[i]).Y > 0.5f;
                    int slot = (p.Lateral > 0f ? 2 : 0) + (up ? 0 : 1);
                    int bin = BinOf(p.Along, bins);
                    Color c = colours[i];
                    sum[bin, slot] += new Vector4(c.R, c.G, c.B, 1f);
                    // The alpha is the vertex's zone / 255 (SurfaceBuilder.Add).
                    byte zone = (byte)Math.Clamp((int)MathF.Round(c.A * 255f), 0, 255);
                    Dictionary<byte, int> counts = zones[bin, slot] ??= [];
                    counts[zone] = counts.GetValueOrDefault(zone) + 1;
                }
            }

            Material? material = node.GetSurfaceOverrideMaterial(0) ?? baked.SurfaceGetMaterial(0);
            // Actor-lit steps (no baked colours) take their light from the actor lighting, as the baked node did.
            return new MovingSteps(node, material, vertexLit ? Samples(sum, zones) : null, echelon, conveyor.Width / ConveyorLayout.BeltTile);
        }

        /// <summary>
        /// The mean light and the most common zone per bin and slot. A bin no vertex fell in takes the last sampled bin
        /// before it; leading ones take the first sampled.
        /// </summary>
        private static Sample[,] Samples(Vector4[,] sum, Dictionary<byte, int>?[,] zones)
        {
            int bins = sum.GetLength(0);
            var light = new Sample[bins, 4];
            for (int slot = 0; slot < 4; slot++)
            {
                int first = -1;
                for (int b = 0; b < bins; b++)
                {
                    if (zones[b, slot] is { } counts)
                    {
                        Vector4 mean = sum[b, slot] / sum[b, slot].W;
                        light[b, slot] = new Sample(new Vector3(mean.X, mean.Y, mean.Z), MostCommon(counts));
                        first = first < 0 ? b : first;
                    }
                    else if (b > 0)
                    {
                        light[b, slot] = light[b - 1, slot];
                    }
                }

                for (int b = 0; b < first; b++)
                {
                    light[b, slot] = light[first, slot];
                }
            }

            return light;
        }

        private static byte MostCommon(Dictionary<byte, int> counts)
        {
            byte best = 0;
            int most = 0;
            foreach ((byte zone, int n) in counts)
            {
                if (n > most || (n == most && zone < best))
                {
                    (best, most) = (zone, n);
                }
            }

            return best;
        }

        /// <summary>Rebuilds the steps at belt <paramref name="phase"/>.</summary>
        public void Place(Conveyor conveyor, double phase, List<ConveyorStepFace> faces)
        {
            ConveyorPath path = conveyor.WorldPath;
            ConveyorSteps.Build(path, conveyor.HalfWidth, phase, faces);
            _points.Clear();
            _normals.Clear();
            _uvs.Clear();
            _colours.Clear();
            _indices.Clear();
            foreach (ConveyorStepFace f in faces)
            {
                bool up = f.Normal.Z > 0.5f;
                int i = _points.Count;
                Vertex(path, f.A, f.Normal, new Vector2(0f, f.V0), up);
                Vertex(path, f.B, f.Normal, new Vector2(_u, f.V0), up);
                Vertex(path, f.C, f.Normal, new Vector2(_u, f.V1), up);
                Vertex(path, f.D, f.Normal, new Vector2(0f, f.V1), up);
                // Godot fronts wind clockwise seen from the front.
                Vector3 n = UeSpace.DirectionToGodot(f.Normal);
                bool clockwise = (_points[i + 1] - _points[i]).Cross(_points[i + 2] - _points[i]).Dot(n) < 0f;
                _indices.AddRange(clockwise ? [i, i + 1, i + 2, i, i + 2, i + 3] : [i, i + 2, i + 1, i, i + 3, i + 2]);
            }

            _mesh.ClearSurfaces();
            if (_indices.Count == 0)
            {
                return;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _points.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
            if (_light is not null)
            {
                arrays[(int)Mesh.ArrayType.Color] = _colours.ToArray();
            }

            Mesh.ArrayFormat flags = 0;
            if (_echelon is { } code)
            {
                float[] codes = new float[_points.Count];
                Array.Fill(codes, code);
                arrays[(int)Mesh.ArrayType.Custom1] = codes;
                flags = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
            }

            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
            _mesh.SurfaceSetMaterial(0, _material);
        }

        private static Transform3D World(Node3D node) => node.IsInsideTree() ? node.GlobalTransform : node.Transform;

        private void Vertex(ConveyorPath path, FVector at, FVector normal, Vector2 uv, bool up)
        {
            _points.Add(_toLocal * UeSpace.ToGodot(at));
            _normals.Add((_toLocal.Basis * UeSpace.DirectionToGodot(normal)).Normalized());
            _uvs.Add(uv);
            if (_light is null)
            {
                return;
            }

            path.Project(at, float.MaxValue, out ConveyorProjection p);
            int slot = (p.Lateral > 0f ? 2 : 0) + (up ? 0 : 1);
            _colours.Add(LightAt(_light, p.Along, slot));
        }

        /// <summary>
        /// The light blended between the centres of the two bins either side of <paramref name="along"/>, in the zone of
        /// the nearer one (a zone is never blended).
        /// </summary>
        private static Color LightAt(Sample[,] light, float along, int slot)
        {
            int bins = light.GetLength(0);
            float x = Math.Clamp(along / Bin - 0.5f, 0f, bins - 1);
            int b0 = (int)x, b1 = Math.Min(b0 + 1, bins - 1);
            float t = x - b0;
            Sample s0 = light[b0, slot], s1 = light[b1, slot];
            Vector3 rgb = s0.Light.Lerp(s1.Light, t);
            return new Color(rgb.X, rgb.Y, rgb.Z, (t < 0.5f ? s0.Zone : s1.Zone) / 255f);
        }

        private static int BinOf(float along, int bins) => Math.Clamp((int)(along / Bin), 0, bins - 1);

        /// <summary>The baked light at one place on the steps (half the light term, as the map builder writes it) and its zone.</summary>
        private readonly record struct Sample(Vector3 Light, byte Zone);
    }
}
