using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Views;

/// <summary>
/// Draws the wall mounts security cameras spawn (SCamera.PreBeginPlay with bAutoWallSupport: a plain
/// SBasicGamePlayObject owned by the camera, StaticMesh camera_blindee_pied, heat 0.2; spec/devices/security-camera.md
/// rule 1). The mount is a spawned actor, so the map's gameplay meshes do not carry it. One mount is drawn per camera
/// (a client has its own spawn and may also get the server's copy). Reads the simulation and never writes it.
/// </summary>
[Provenance(ProvenanceKind.Decoded, "SCamera.PreBeginPlay: Spawn(class'SBasicGamePlayObject', self) with SetStaticMesh(WallSupportStaticmesh), DT_StaticMesh, bHidden false, WallSupportHeatIntensity")]
public sealed class CameraMountViews
{
    private readonly RenderMap _map;
    private readonly Node3D _parent;
    private readonly Func<string, Mesh?> _meshFor;
    private readonly Dictionary<SCamera, Mount> _mounts = new(ReferenceEqualityComparer.Instance);
    private readonly List<SCamera> _gone = [];
    private int _actorCount = -1;

    internal CameraMountViews(RenderMap map, Node3D parent, Func<string, Mesh?> meshFor)
    {
        _map = map;
        _parent = parent;
        _meshFor = meshFor;
    }

    /// <summary>The mounts drawn.</summary>
    public int Count => _mounts.Count;

    /// <summary>Drops mounts whose actor went and picks up new ones when the level's actors changed.</summary>
    internal void OnTick(Level level)
    {
        _gone.Clear();
        foreach ((SCamera camera, Mount mount) in _mounts)
        {
            if (mount.Actor.bDeleteMe)
            {
                _gone.Add(camera);
            }
        }

        foreach (SCamera camera in _gone)
        {
            _mounts[camera].Node.QueueFree();
            _mounts.Remove(camera);
        }

        if (level.ActorCount == _actorCount && _gone.Count == 0)
        {
            return;
        }

        _actorCount = level.ActorCount;
        foreach (SBasicGamePlayObject a in level.DynamicActors<SBasicGamePlayObject>())
        {
            if (a.bDeleteMe || a.GetType() != typeof(SBasicGamePlayObject) || a.Owner is not SCamera camera
                || _mounts.ContainsKey(camera) || a.StaticMesh.Path is not { } path || _meshFor(path) is not { } mesh)
            {
                continue;
            }

            var node = new MeshInstance3D { Name = "CameraMount", Mesh = mesh };
            _parent.AddChild(node);
            _mounts[camera] = new Mount(a, node);
        }
    }

    internal void Draw()
    {
        foreach (Mount m in _mounts.Values)
        {
            Actor a = m.Actor;
            MeshInstance3D node = m.Node;
            node.Visible = !a.bHidden && a.DrawType == EDrawType.DT_StaticMesh;
            if (!node.Visible)
            {
                continue;
            }

            node.Transform = UeSpace.TransformToGodot(a.Location, a.Rotation, a.DrawScale3D * a.DrawScale, a.PrePivot);
            Vector4 vision = GameplayObjects.VisionOf(a);
            if (vision != m.Vision)
            {
                m.Vision = vision;
                node.SetInstanceShaderParameter(GameplayObjects.VisionActorParameter, vision);
            }

            if (!m.Lit && node.Mesh is { } mesh)
            {
                m.Lit = true;
                Aabb box = node.Transform * mesh.GetAabb();
                float radius = UeSpace.UnitsPerMetre * box.Size.Length() * 0.5f;
                ActorLighting.Apply(node, _map.LightActor(a.Location, radius, a.AmbientGlow));
            }
        }
    }

    private sealed class Mount(Actor actor, MeshInstance3D node)
    {
        public Actor Actor { get; } = actor;

        public MeshInstance3D Node { get; } = node;

        public Vector4 Vision { get; set; } = new(float.NaN, 0f, 0f, 0f);

        public bool Lit { get; set; }
    }
}
