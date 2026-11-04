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
/// render/world.glb, lit like the rest of the map; here the belt, step and roller textures and the map's own scroll
/// targets slide with the belt, and the items ride it. Everything is placed from the conveyor's replicated phase at
/// the displayed server time, so every client shows the same belt. Reads the simulation and never writes it.
/// </summary>
public sealed class ConveyorViews
{
    private const string ScrollParameter = "svm_scroll";
    private const float RelightSeconds = 0.1f;

    private readonly RenderMap _map;
    private readonly List<Belt> _belts = [];
    private float _relightClock;

    /// <summary>Binds the conveyors <paramref name="loaded"/> created to the map's nodes; adds item nodes under <paramref name="parent"/>.</summary>
    public ConveyorViews(RenderMap map, MapLoadResult loaded, Node3D parent)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(parent);
        foreach (ActorRecord record in loaded.Gameplay.Actors)
        {
            if (loaded.ActorOf(record) is not Conveyor conveyor)
            {
                continue;
            }

            var belt = new Belt(record.Id, conveyor, conveyor.WorldPath);
            AddScroll(belt, record.Id + ConveyorLayout.BeltPart, ConveyorLayout.BeltTile, 1);
            AddScroll(belt, record.Id + ConveyorLayout.StepsPart, ConveyorLayout.StepLength, 1);
            AddScroll(belt, record.Id + ConveyorLayout.RollersPart, 2f * MathF.PI * ConveyorLayout.RollerRadius, 0);
            foreach (FConveyorScroll target in conveyor.ScrollTargets)
            {
                if (target.Target is { Length: > 0 } id && target.UnitsPerTile != 0f)
                {
                    AddScroll(belt, id, target.UnitsPerTile, target.Axis);
                }
            }

            AddItems(belt, parent);
            _belts.Add(belt);
        }
    }

    /// <summary>Conveyors bound, for reports.</summary>
    public int Count => _belts.Count;

    /// <summary>
    /// Places every moving part at <paramref name="serverTick"/>, the server time this frame shows (fractional ticks).
    /// </summary>
    public void Draw(double serverTick, float dt)
    {
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

            for (int i = 0; i < belt.Items.Count; i++)
            {
                MeshInstance3D node = belt.Items[i];
                ConveyorItemPlace place = ConveyorLayout.PlaceItem(belt.Conveyor, belt.Path, i, belt.Items.Count, phase);
                node.Visible = place.Scale > 0f;
                if (!node.Visible)
                {
                    continue;
                }

                // Tilted with the belt, turned about its up, and lifted so the mesh's lowest point rests on it.
                node.Mesh = belt.Meshes[place.Mesh];
                var scale = new FVector(place.Scale, place.Scale, place.Scale);
                node.Transform = UeSpace.TransformToGodot(place.Location, place.Belt) *
                                 UeSpace.TransformToGodot(default, new FRotator(0, place.Spin, 0), scale, default) *
                                 new Transform3D(Basis.Identity, new Vector3(0f, belt.Lifts[place.Mesh], 0f));
                if (relight)
                {
                    ActorLighting.Apply(node, _map.LightActor(place.Location, belt.ItemRadius, 0));
                }
            }
        }
    }

    /// <summary>
    /// Gives a map node its own copies of its materials and of their later passes (the vision passes sample the same
    /// texture), so this conveyor's scroll does not move the same texture on other meshes.
    /// </summary>
    private void AddScroll(Belt belt, string actorId, float unitsPerTile, byte axis)
    {
        if (!_map.Gameplay.TryGetValue(actorId, out GameplayMesh? entry) || entry.Node.Mesh is not { } mesh)
        {
            return;
        }

        var materials = new List<ShaderMaterial>();
        for (int i = 0; i < mesh.GetSurfaceCount(); i++)
        {
            if ((entry.Node.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i)) is ShaderMaterial shared)
            {
                entry.Node.SetSurfaceOverrideMaterial(i, OwnCopy(shared, materials));
            }
        }

        belt.Scrolls.Add(new Scroll(materials, unitsPerTile, axis));
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

    private void AddItems(Belt belt, Node3D parent)
    {
        int count = ConveyorLayout.ItemCount(belt.Conveyor, belt.Path);
        if (count == 0)
        {
            return;
        }

        foreach (OpenScct.Core.Uc.AssetRef asset in belt.Conveyor.ItemMeshes)
        {
            if (FindMesh(asset.Path) is { } mesh)
            {
                belt.Meshes.Add(mesh);
                Aabb bounds = mesh.GetAabb();
                belt.Lifts.Add(-bounds.Position.Y);
                belt.ItemRadius = MathF.Max(belt.ItemRadius, UeSpace.UnitsPerMetre * bounds.Size.Length() * 0.5f);
            }
            else
            {
                _map.Warnings.Add($"conveyor {belt.Id}: item mesh {asset.Path} is not in the map's render data");
                belt.Meshes.Add(null);
                belt.Lifts.Add(0f);
            }
        }

        if (belt.Meshes.TrueForAll(m => m is null))
        {
            return;
        }

        // A missing mesh draws as the first one found, so items keep their spacing.
        int first = belt.Meshes.FindIndex(m => m is not null);
        Mesh fallback = belt.Meshes[first]!;
        for (int i = 0; i < belt.Meshes.Count; i++)
        {
            if (belt.Meshes[i] is null)
            {
                belt.Meshes[i] = fallback;
                belt.Lifts[i] = belt.Lifts[first];
            }
        }

        var group = new Node3D { Name = belt.Id + ".Items" };
        parent.AddChild(group);
        for (int i = 0; i < count; i++)
        {
            var node = new MeshInstance3D { Name = $"Item{i}", Mesh = fallback };
            node.SetInstanceShaderParameter(GameplayObjects.VisionActorParameter, SurfaceBuilder.DefaultVision);
            group.AddChild(node);
            belt.Items.Add(node);
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

    private sealed class Belt(string id, Conveyor conveyor, ConveyorPath path)
    {
        public string Id { get; } = id;
        public Conveyor Conveyor { get; } = conveyor;
        public ConveyorPath Path { get; } = path;
        public List<Scroll> Scrolls { get; } = [];
        public List<Mesh?> Meshes { get; } = [];

        /// <summary>Per item mesh: how far to raise it (Godot units, before scaling) so its lowest point is at its pivot.</summary>
        public List<float> Lifts { get; } = [];
        public List<MeshInstance3D> Items { get; } = [];
        public float ItemRadius { get; set; }
    }

    private sealed record Scroll(List<ShaderMaterial> Materials, float UnitsPerTile, byte Axis);
}
