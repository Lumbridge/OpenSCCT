using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScctGame.Match;

/// <summary>
/// Draws the walkers a client has replicated: one box per live walker, placed from the client's actor through
/// <see cref="UeSpace"/>. Reads sim state only, never writes it.
/// </summary>
public partial class WalkerViews : Node3D
{
    private static readonly Color LocalColour = new(0.3f, 0.55f, 1f);
    private static readonly Color RemoteColour = new(1f, 0.6f, 0.2f);

    private readonly Dictionary<uint, MeshInstance3D> _views = new();
    private readonly HashSet<uint> _seen = new();
    private readonly List<uint> _stale = new();
    private StandardMaterial3D? _localMaterial;
    private StandardMaterial3D? _remoteMaterial;

    /// <summary>The client whose walkers are drawn.</summary>
    public ClientSession? Session { get; set; }

    public int Count => _views.Count;

    /// <summary>The view of the walker on actor channel <paramref name="netGuid"/>.</summary>
    public MeshInstance3D? ViewOf(uint netGuid) => _views.GetValueOrDefault(netGuid);

    /// <summary>Brings the views in line with the client's walkers. Call after the client ticks.</summary>
    public void Sync()
    {
        _seen.Clear();
        if (Session is { } session)
        {
            foreach (ActorChannel channel in session.ActorChannels)
            {
                if (channel.Actor is not CubeWalker { bDeleteMe: false } walker || channel.Motion is not { HasState: true })
                {
                    continue;
                }

                uint guid = channel.NetGuid;
                if (!_views.TryGetValue(guid, out MeshInstance3D? view))
                {
                    view = CreateView(walker, ReferenceEquals(walker, session.LocalWalker));
                    view.Name = "Walker" + guid.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    AddChild(view);
                    _views.Add(guid, view);
                }

                view.Transform = UeSpace.TransformToGodot(walker.Location, new FRotator(0, walker.Rotation.Yaw, 0));
                _seen.Add(guid);
            }
        }

        _stale.Clear();
        foreach (uint guid in _views.Keys)
        {
            if (!_seen.Contains(guid))
            {
                _stale.Add(guid);
            }
        }

        foreach (uint guid in _stale)
        {
            _views[guid].QueueFree();
            _views.Remove(guid);
        }
    }

    private MeshInstance3D CreateView(CubeWalker walker, bool local)
    {
        StandardMaterial3D material = local
            ? _localMaterial ??= new StandardMaterial3D { AlbedoColor = LocalColour }
            : _remoteMaterial ??= new StandardMaterial3D { AlbedoColor = RemoteColour };
        float width = UeSpace.LengthToGodot(walker.CollisionRadius * 2f);
        float height = UeSpace.LengthToGodot(walker.CollisionHeight * 2f);
        var body = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(width, height, width), Material = material } };

        // A nose on local +X (UE forward) shows the facing.
        body.AddChild(new MeshInstance3D
        {
            Name = "Nose",
            Mesh = new BoxMesh { Size = new Vector3(width * 0.4f, height * 0.2f, width * 0.2f), Material = material },
            Position = new Vector3(width * 0.6f, height * 0.25f, 0f),
        });
        return body;
    }
}
