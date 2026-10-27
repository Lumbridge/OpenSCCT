using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Content;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Views;

/// <summary>
/// Draws the client world's devices over the built map: every gameplay mesh node of <see cref="RenderMap.Gameplay"/>
/// follows its simulated actor (movers move, hidden actors hide, a changed StaticMesh swaps the node's mesh), and
/// every SAnimatedMesh (SteeE's crane and blast wall) is a skeletal model that shows and hides with the actor and
/// starts the sequence its replicated SimAnim names. Reads the simulation and never writes it.
/// </summary>
public sealed class DeviceViews
{
    private const float RelightSeconds = 0.1f;

    private readonly RenderMap _map;
    private readonly List<MeshFollower> _meshes = [];
    private readonly List<AnimatedMeshView> _animated = [];
    private readonly List<LightSwitch> _lights = [];
    private readonly Dictionary<string, Mesh?> _meshByAsset = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private float _relightClock;

    /// <summary>Binds the map's nodes to the actors <paramref name="loaded"/> created (the client's map load).</summary>
    public DeviceViews(RenderMap map, MapLoadResult loaded, Node3D parent, string? sharedRoot = null)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(parent);
        foreach (ActorRecord record in loaded.Gameplay.Actors)
        {
            if (loaded.ActorOf(record) is not { } actor)
            {
                continue;
            }

            if (map.Gameplay.TryGetValue(record.Id, out GameplayMesh? entry))
            {
                // The map's bHidden, not the actor's now: replication may have shown it before this first bind.
                bool mapHidden = map.Actors.Find(record.Id) is { } placed ? placed.Bool("bHidden") : actor.bHidden;
                var follower = new MeshFollower(actor, entry, mapHidden);
                _meshes.Add(follower);
                if (follower.Asset is { } asset && entry.Node.Mesh is { } mesh)
                {
                    _meshByAsset.TryAdd(asset, mesh);
                }
            }

            if (actor is STriggerLight light)
            {
                _lights.Add(new LightSwitch(record.Id, light));
            }

            if (actor is SAnimatedMesh animated && !animated.Mesh.IsNone)
            {
                string asset = animated.Mesh.Path!;
                string? glb = SkeletalModel.FindGlb(asset, sharedRoot ?? map.SharedRoot);
                if (glb is null)
                {
                    _missing.Add(asset);
                    continue;
                }

                SkeletalModel model = SkeletalModel.Load(glb);
                model.Root.Name = record.Id;
                parent.AddChild(model.Root);
                _animated.Add(new AnimatedMeshView(record.Id, animated, model, map));
            }
        }
    }

    /// <summary>The followed gameplay mesh nodes.</summary>
    public IReadOnlyList<MeshFollower> Meshes => _meshes;

    /// <summary>The skeletal props.</summary>
    public IReadOnlyList<AnimatedMeshView> Animated => _animated;

    /// <summary>Meshes the render content does not have (a swapped StaticMesh, a skeletal mesh): the node hides.</summary>
    public IReadOnlyCollection<string> MissingMeshes => _missing;

    /// <summary>A one-line count of what the views follow and what changed (for reports).</summary>
    public string Summary()
    {
        int movers = 0;
        int moved = 0;
        int swapped = 0;
        foreach (MeshFollower m in _meshes)
        {
            movers += m.Actor is Mover ? 1 : 0;
            moved += m.Moved ? 1 : 0;
            swapped += string.Equals(m.ShownAsset, m.Asset, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        int playing = 0;
        foreach (AnimatedMeshView a in _animated)
        {
            playing += a.Starts > 0 ? 1 : 0;
        }

        return $"devices={_meshes.Count} movers={movers} moved={moved} swapped={swapped} skeletal={_animated.Count} started={playing}";
    }

    /// <summary>
    /// Takes the actors' state after a simulation tick. With the client world's <paramref name="level"/>, a node whose
    /// map actor the client destroyed at load follows the server's copy once it arrives (<see cref="Rebind"/>).
    /// </summary>
    public void OnTick(Level? level = null)
    {
        if (level is not null)
        {
            Rebind(level);
        }

        foreach (MeshFollower m in _meshes)
        {
            m.OnTick();
        }

        foreach (AnimatedMeshView a in _animated)
        {
            a.OnTick();
        }

        // A trigger light switches the map's light of the same name (spec/devices/trigger-light.md, render).
        for (int i = 0; i < _lights.Count; i++)
        {
            LightSwitch l = _lights[i];
            bool on = !l.Light.bDeleteMe && l.Light.LightType != ELightType.LT_None;
            if (l.Shown != on)
            {
                _lights[i] = l with { Shown = on };
                _map.SetLightOn(l.Id, on);
            }
        }
    }

    /// <summary>
    /// UE2 client map setup destroys every map actor that is neither bStatic nor bNoDelete (SLaserTrigger,
    /// SVolumetricSensor) and the server sends its own copy while it is relevant (spec/net/model.md rule 14,
    /// spec/devices/laser-trigger.md rule 11). The map's node was bound to the destroyed copy and hid with it, so only
    /// the beam (drawn from the live actors) showed. A node whose actor is gone follows the live, unbound actor of the
    /// same class at its map placement; when that copy goes too (out of relevancy) the node hides until the next one.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "NetDriver.PrepareClientMap destroys non-bNoDelete map actors; the server's copy keeps Location and Rotation (bNetInitialRotation)")]
    private void Rebind(Level level)
    {
        bool orphans = false;
        foreach (MeshFollower m in _meshes)
        {
            if (m.Actor.bDeleteMe && m.Replaceable)
            {
                orphans = true;
                break;
            }
        }

        if (!orphans || (level.ActorCount == _rebindActorCount && level.TickCount % RebindRescanTicks != 0))
        {
            return;
        }

        _rebindActorCount = level.ActorCount;
        _bound.Clear();
        foreach (MeshFollower m in _meshes)
        {
            if (!m.Actor.bDeleteMe)
            {
                _bound.Add(m.Actor);
            }
        }

        foreach (Actor a in level.DynamicActors<Actor>())
        {
            if (a.bDeleteMe || a.bNoDelete || _bound.Contains(a))
            {
                continue;
            }

            foreach (MeshFollower m in _meshes)
            {
                if (m.Actor.bDeleteMe && m.Replaceable && m.Actor.GetType() == a.GetType() && m.IsAtPlacement(a.Location))
                {
                    m.Rebind(a);
                    _bound.Add(a);
                    break;
                }
            }
        }
    }

    private const int RebindRescanTicks = 30;

    private readonly HashSet<Actor> _bound = new(ReferenceEqualityComparer.Instance);
    private int _rebindActorCount = -1;

    /// <summary>The trigger lights followed (map light name = actor id).</summary>
    public int TriggerLights => _lights.Count;

    private readonly record struct LightSwitch(string Id, STriggerLight Light, bool? Shown = null);

    /// <summary>Places, shows and swaps for a frame <paramref name="alpha"/> of the way from the previous tick to the last.</summary>
    public void Draw(float alpha, float dt)
    {
        _relightClock -= dt;
        bool relight = _relightClock <= 0f;
        if (relight)
        {
            _relightClock = RelightSeconds;
        }

        foreach (MeshFollower m in _meshes)
        {
            if (m.WantsMeshChange)
            {
                string? wanted = m.Actor.StaticMesh.Path;
                bool own = wanted is not null && string.Equals(wanted, m.Asset, StringComparison.OrdinalIgnoreCase);
                m.ShowMesh(wanted, wanted is null || own ? null : FindMesh(wanted));
            }

            m.Draw(alpha);
            if (relight && m.Moved && m.Entry.Light == LightSource.Actor && m.Entry.Node.Visible)
            {
                ActorLighting.Apply(m.Entry.Node, _map.LightActor(m.Actor.Location, m.Entry.Radius, m.Entry.AmbientGlow));
            }
        }

        foreach (AnimatedMeshView a in _animated)
        {
            a.Draw(dt);
        }
    }

    private Mesh? FindMesh(string asset)
    {
        if (_meshByAsset.TryGetValue(asset, out Mesh? known))
        {
            return known;
        }

        Mesh? mesh = _map.SwapMeshes.TryGetValue(asset, out ArrayMesh? swap) ? swap : GameplayObjects.MeshFor(_map, asset, unlit: false);
        _meshByAsset[asset] = mesh;
        if (mesh is null && _missing.Add(asset))
        {
            GD.PushWarning($"{_map.MapId}: no render mesh for {asset}; the node is hidden while the actor uses it");
        }

        return mesh;
    }
}

/// <summary>One gameplay mesh node following its actor.</summary>
public sealed class MeshFollower
{
    private readonly Transform3D _node0;
    private readonly Transform3D _placed0Inverse;
    private readonly FVector _location0;
    private readonly FRotator _rotation0;
    private readonly bool _hidden0;
    private readonly bool _visible0;
    private FVector _prevLocation;
    private FVector _location;
    private FRotator _prevRotation;
    private FRotator _rotation;
    private bool _meshMissing;
    private readonly Mesh? _mesh0;
    private Vector4 _vision;

    internal MeshFollower(Actor actor, GameplayMesh entry, bool mapHidden)
    {
        Actor = actor;
        Entry = entry;
        _node0 = entry.Node.Transform;
        _location0 = actor.Location;
        _rotation0 = actor.Rotation;
        _placed0Inverse = UeSpace.TransformToGodot(_location0, _rotation0).AffineInverse();
        _hidden0 = mapHidden;
        _visible0 = entry.Node.Visible;
        _prevLocation = _location = actor.Location;
        _prevRotation = _rotation = actor.Rotation;
        Asset = actor.StaticMesh.Path;
        ShownAsset = Asset;
        _mesh0 = entry.Node.Mesh;
        _vision = entry.Node.GetInstanceShaderParameter(GameplayObjects.VisionActorParameter).AsVector4();
        Replaceable = !actor.bStatic && !actor.bNoDelete;
    }

    public Actor Actor { get; private set; }

    /// <summary>True for a map actor a client destroys at load and gets back from the server (neither bStatic nor bNoDelete).</summary>
    internal bool Replaceable { get; }

    /// <summary>True when <paramref name="location"/> is the map placement, within what replication rounds.</summary>
    internal bool IsAtPlacement(FVector location)
    {
        FVector d = location - _location0;
        return MathF.Abs(d.X) <= PlacementSlack && MathF.Abs(d.Y) <= PlacementSlack && MathF.Abs(d.Z) <= PlacementSlack;
    }

    /// <summary>Follows <paramref name="actor"/>, the server's copy of the destroyed map actor, from its current state.</summary>
    internal void Rebind(Actor actor)
    {
        // _hidden0 stays the map's: a copy that arrives hidden hides the node.
        Actor = actor;
        _prevLocation = _location = actor.Location;
        _prevRotation = _rotation = actor.Rotation;
        Moved = LeftPlacement();
    }

    public GameplayMesh Entry { get; }

    /// <summary>The StaticMesh the map gave the actor.</summary>
    public string? Asset { get; }

    /// <summary>The StaticMesh the node shows now (null when the actor has none).</summary>
    public string? ShownAsset { get; private set; }

    /// <summary>True once the actor left its map location or rotation.</summary>
    public bool Moved { get; private set; }

    /// <summary>
    /// True when the actor's StaticMesh is not what the node shows, a change to None included (a broken pane hides;
    /// only a change to another mesh used to count, so broken glass stayed drawn).
    /// </summary>
    internal bool WantsMeshChange => MeshSwap.Changed(Actor.StaticMesh.Path, ShownAsset);

    internal void OnTick()
    {
        _prevLocation = _location;
        _prevRotation = _rotation;
        _location = Actor.Location;
        _rotation = Actor.Rotation;
        if (!Moved && LeftPlacement())
        {
            Moved = true;
        }
    }

    /// <summary>
    /// True when the actor is away from its map placement by more than what replication rounds (locations to whole
    /// units, rotations to 256-unit steps): a replicated static prop keeps the map's exact transform.
    /// </summary>
    private bool LeftPlacement()
    {
        FVector d = _location - _location0;
        return MathF.Abs(d.X) > PlacementSlack || MathF.Abs(d.Y) > PlacementSlack || MathF.Abs(d.Z) > PlacementSlack ||
            Math.Abs(FRotator.NormalizeAxis(_rotation.Pitch - _rotation0.Pitch)) > RotationSlack ||
            Math.Abs(FRotator.NormalizeAxis(_rotation.Yaw - _rotation0.Yaw)) > RotationSlack ||
            Math.Abs(FRotator.NormalizeAxis(_rotation.Roll - _rotation0.Roll)) > RotationSlack;
    }

    private const float PlacementSlack = 1f;

    private const int RotationSlack = 256;

    /// <summary>Shows <paramref name="mesh"/> for <paramref name="asset"/>; no mesh (None, or one the render lacks) hides the node.</summary>
    internal void ShowMesh(string? asset, Mesh? mesh)
    {
        // Back to the map's own mesh (a reset, a second toggle): the node's first mesh.
        if (asset is not null && string.Equals(asset, Asset, StringComparison.OrdinalIgnoreCase) && _mesh0 is not null)
        {
            mesh = _mesh0;
        }

        ShownAsset = asset;
        _meshMissing = mesh is null;
        if (mesh is not null)
        {
            Entry.Node.Mesh = mesh;
        }
    }

    [Provenance(ProvenanceKind.Estimated, "Movers drawn between the last two ticks (linear location, shortest-way rotation)")]
    internal void Draw(float alpha)
    {
        MeshInstance3D node = Entry.Node;
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        // The map's own visibility (mode-dependent props, hidden placements) until the actor changes bHidden.
        bool visible = Actor.bHidden == _hidden0 ? _visible0 : !Actor.bHidden;
        // A stage that removes the mesh (a broken window pane, spec/devices/vitre.md rule 2) hides the node.
        bool meshGone = Asset is not null && Actor.StaticMesh.IsNone && Actor.DrawType == EDrawType.DT_StaticMesh;
        node.Visible = visible && !_meshMissing && !meshGone && !Actor.bDeleteMe && Actor.DrawType != EDrawType.DT_None;

        // The live vision values (a switched-off device stops glowing in EMF); HeatOpacity, not replicated, stays the map's.
        Vector4 vision = GameplayObjects.VisionOf(Actor) with { Y = _vision.Y };
        if (vision != _vision)
        {
            _vision = vision;
            node.SetInstanceShaderParameter(GameplayObjects.VisionActorParameter, vision);
        }

        if (!Moved || !node.Visible)
        {
            return;
        }

        float a = Math.Clamp(alpha, 0f, 1f);
        FVector location = _prevLocation + ((_location - _prevLocation) * a);
        var rotation = new FRotator(
            _prevRotation.Pitch + (int)(FRotator.NormalizeAxis(_rotation.Pitch - _prevRotation.Pitch) * a),
            _prevRotation.Yaw + (int)(FRotator.NormalizeAxis(_rotation.Yaw - _prevRotation.Yaw) * a),
            _prevRotation.Roll + (int)(FRotator.NormalizeAxis(_rotation.Roll - _prevRotation.Roll) * a));
        node.Transform = UeSpace.TransformToGodot(location, rotation) * _placed0Inverse * _node0;
    }
}

/// <summary>
/// An SAnimatedMesh drawn as its skeletal model: shown while the actor is not hidden, and playing the sequence of
/// the actor's replicated SimAnim from its start when the server starts it (a rate above 0 after a frozen or
/// different channel), held at the replicated frame while frozen.
/// </summary>
public sealed class AnimatedMeshView
{
    private readonly SAnimatedMesh _actor;
    private readonly RenderMap _map;
    private FAnimRep _rep;
    private bool _bound;
    private bool _lit;

    internal AnimatedMeshView(string id, SAnimatedMesh actor, SkeletalModel model, RenderMap map)
    {
        Id = id;
        _actor = actor;
        Model = model;
        _map = map;
        Place();
        OnTick();
    }

    public string Id { get; }

    public SkeletalModel Model { get; }

    public SAnimatedMesh Actor => _actor;

    /// <summary>Times the view started a sequence (not counting frozen holds).</summary>
    public int Starts { get; private set; }

    /// <summary>The sequence the model plays or holds, or null.</summary>
    public string? Sequence => Model.Sampler.Current?.Name;

    /// <summary>The model's normalised frame.</summary>
    public float Frame => Model.Sampler.Frame;

    public bool Visible => Model.Root.Visible;

    [Provenance(ProvenanceKind.Estimated, "Client start rule: a SimAnim with rate above 0 that differs from the last one starts the sequence (UE2 client anim replication)")]
    internal void OnTick()
    {
        FAnimRep rep = _actor.SimAnim;
        if (_bound && rep == _rep)
        {
            return;
        }

        bool start = rep.AnimRate > 0 && (!_bound || rep.AnimSequence != _rep.AnimSequence || _rep.AnimRate == 0);
        _rep = rep;
        _bound = true;
        if (rep.AnimSequence.IsNone)
        {
            return;
        }

        string sequence = rep.AnimSequence.ToString();
        if (start)
        {
            if (Model.Sampler.Play(sequence, rep.AnimRate / 64f, rep.AnimLoop != 0, rep.AnimFrame / 255f))
            {
                Starts++;
            }
        }
        else if (rep.AnimRate == 0)
        {
            Model.Sampler.Play(sequence, 0f, false, rep.AnimFrame / 255f);
        }
    }

    internal void Draw(float dt)
    {
        bool visible = !_actor.bHidden && !_actor.bDeleteMe && _actor.DrawType == EDrawType.DT_Mesh;
        Model.Root.Visible = visible;
        if (!visible)
        {
            return;
        }

        Place();
        Model.Sampler.Advance(dt);
        Model.Pose();
        if (!_lit)
        {
            _lit = true;
            (FVector center, float radius) = AnimatedProps.Bounds(Model);
            Model.Light(_map.LightActor(center, radius, _actor.AmbientGlow));
        }
    }

    private void Place() => Model.Place(_actor.Location, _actor.Rotation, _actor.PrePivot, _actor.DrawScale);
}
