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

namespace OpenScctGame.Views;

/// <summary>
/// The map's placed emitters and lights whose simulated actor is attached to another actor follow it on screen: a
/// rider of a mover (AttachTag: ShadD's spotlight movers spot1 and spot2, ShadP's DynamicSpotlights) and an actor hung
/// on an SAnimatedMesh bone (ActorsToAttach: the Club fans' emitters, the Miss and Stat beacons). Emitters move their
/// particle systems (<see cref="Render.Particles.Emitters.SetPlace"/>); lights move their world term, actor lighting
/// and corona (<see cref="RenderMap.MoveLight"/>). The render-only map lights (not in the simulation, such as PanaD's
/// elevator lights) are Engine.Light: bStatic and not bMovable, baked into the lightmaps, so they stay put.
/// Reads the simulation and never writes it.
/// </summary>
[PortOf("eng:Render.AttachedEffects", Spec = "spec/devices/animated-mesh.md#rules")]
public sealed class AttachedEffects
{
    private const float RelightSeconds = 0.1f;

    private readonly RenderMap _map;
    private readonly IReadOnlyList<AnimatedMeshView> _animated;
    private readonly List<Follower> _followers = [];
    private float _relightClock;
    private bool _lightsMoved;

    internal AttachedEffects(RenderMap map, MapLoadResult loaded, IReadOnlyList<AnimatedMeshView> animated)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(loaded);
        _animated = animated ?? throw new ArgumentNullException(nameof(animated));
        var lights = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Render.Data.LightEntry l in map.Lighting.Lights)
        {
            lights.Add(l.Name);
        }

        foreach (Render.Data.CoronaEntry c in map.Lighting.Coronas)
        {
            lights.Add(c.Light);
        }

        foreach (ActorRecord record in loaded.Gameplay.Actors)
        {
            if (loaded.ActorOf(record) is not { } actor)
            {
                continue;
            }

            bool emitter = actor is Emitter && map.Emitters is { } emitters && emitters.Has(record.Id);
            if (emitter || lights.Contains(record.Id))
            {
                _followers.Add(new Follower(record.Id, actor, emitter));
            }
        }
    }

    /// <summary>Emitters and lights bound to their simulated actors.</summary>
    public int Bound => _followers.Count;

    /// <summary>Of those, the ones attached to a mover or a bone now.</summary>
    public int Attached
    {
        get
        {
            int n = 0;
            foreach (Follower f in _followers)
            {
                n += f.Riding ? 1 : 0;
            }

            return n;
        }
    }

    /// <summary>After a simulation tick: each attached actor's place and its base's, for drawing between ticks.</summary>
    internal void OnTick()
    {
        foreach (Follower f in _followers)
        {
            f.OnTick();
        }
    }

    /// <summary>Places every attached emitter and light for a frame <paramref name="alpha"/> of the way between the last two ticks.</summary>
    internal void Draw(float alpha, float dt)
    {
        foreach (Follower f in _followers)
        {
            if (!f.Riding || !Place(f, alpha, out FVector location, out FRotator rotation))
            {
                continue;
            }

            if (location == f.ShownLocation && rotation == f.ShownRotation)
            {
                continue;
            }

            f.ShownLocation = location;
            f.ShownRotation = rotation;
            if (f.IsEmitter)
            {
                _map.Emitters?.SetPlace(f.Id, location, rotation);
            }
            else
            {
                _map.MoveLight(f.Id, location, rotation);
                _lightsMoved = true;
            }
        }

        // The actor-lit meshes take the moved lights at the rate moved devices are relit.
        _relightClock -= dt;
        if (_relightClock <= 0f)
        {
            _relightClock = RelightSeconds;
            if (_lightsMoved)
            {
                _lightsMoved = false;
                _map.RelightGameplay();
            }
        }
    }

    /// <summary>Where an attached actor is drawn: on its bone, hard-attached to its base, or at its own interpolated place.</summary>
    private bool Place(Follower f, float alpha, out FVector location, out FRotator rotation)
    {
        Actor a = f.Actor;
        if (!a.AttachmentBone.IsNone && a.Base is SAnimatedMesh mesh)
        {
            return BonePlace(a, mesh, out location, out rotation) || f.Lerp(alpha, out location, out rotation);
        }

        return f.Lerp(alpha, out location, out rotation);
    }

    /// <summary>
    /// An actor hung on a bone of a drawn SAnimatedMesh: the bone's frame turned by RelativeRotation and moved by
    /// RelativeLocation (both zero for the map's attachments, so they sit on the bone). False while the prop is not
    /// drawn (its pose is not the game's then).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 bone attachment: the attached actor's Location and Rotation are the bone's coords composed with RelativeLocation and RelativeRotation each time the mesh is posed")]
    private bool BonePlace(Actor a, SAnimatedMesh mesh, out FVector location, out FRotator rotation)
    {
        location = default;
        rotation = default;
        AnimatedMeshView? view = null;
        foreach (AnimatedMeshView v in _animated)
        {
            if (ReferenceEquals(v.Actor, mesh))
            {
                view = v;
                break;
            }
        }

        if (view is null || !view.Visible || view.Model.Skeleton.FindBone(a.AttachmentBone.ToString()) is not (var index and >= 0))
        {
            return false;
        }

        Transform3D bone = view.Model.Skeleton.GlobalTransform * view.Model.Skeleton.GetBoneGlobalPose(index);
        Basis boneBasis = bone.Basis.Orthonormalized();
        Basis basis = boneBasis * UeSpace.RotationToGodot(a.RelativeRotation);
        location = UeSpace.ToUe(bone.Origin + (boneBasis * UeSpace.ToGodot(a.RelativeLocation)));
        rotation = RotationOf(basis);
        return true;
    }

    /// <summary>The UE rotator of a Godot rotation basis (columns: UE X, Z and Y).</summary>
    internal static FRotator RotationOf(Basis basis) => PoseCameraBones.OrthoRotation(
        UeSpace.DirectionToUe(basis.Column0), UeSpace.DirectionToUe(basis.Column2), UeSpace.DirectionToUe(basis.Column1));

    private sealed class Follower(string id, Actor actor, bool emitter)
    {
        private FVector _prevLocation = actor.Location;
        private FVector _location = actor.Location;
        private FRotator _prevRotation = actor.Rotation;
        private FRotator _rotation = actor.Rotation;
        private Actor? _hardBase;
        private Transform3D _hardRel;
        private Transform3D _prevBase;
        private Transform3D _base;

        public string Id { get; } = id;

        public Actor Actor { get; } = actor;

        public bool IsEmitter { get; } = emitter;

        /// <summary>True while the actor is based on another actor (a mover, a prop's bone).</summary>
        public bool Riding => Actor is { bDeleteMe: false, Base: { bWorldGeometry: false } };

        public FVector ShownLocation { get; set; } = actor.Location;

        public FRotator ShownRotation { get; set; } = actor.Rotation;

        public void OnTick()
        {
            _prevLocation = _location;
            _prevRotation = _rotation;
            _location = Actor.Location;
            _rotation = Actor.Rotation;
            Actor? b = Actor.bHardAttach && Actor.AttachmentBone.IsNone ? Actor.Base : null;
            if (b is null || b.bWorldGeometry)
            {
                _hardBase = null;
                return;
            }

            Transform3D now = UeSpace.TransformToGodot(b.Location, b.Rotation);
            if (!ReferenceEquals(b, _hardBase))
            {
                // HardRelMatrix: the rider's frame in its base's when it was based.
                _hardBase = b;
                _hardRel = now.AffineInverse() * UeSpace.TransformToGodot(Actor.Location, Actor.Rotation);
                _prevBase = now;
            }
            else
            {
                _prevBase = _base;
            }

            _base = now;
        }

        /// <summary>
        /// The place between the last two ticks. A bHardAttach rider keeps its frame in its base's (UE2 HardRelMatrix),
        /// pitch and roll included; a soft rider is where the simulation moved it.
        /// </summary>
        [Provenance(ProvenanceKind.Decoded, "Engine.Actor: bHardAttach and HardRelMatrix (the rider's matrix relative to its base)")]
        [Provenance(ProvenanceKind.Estimated, "Drawn between the last two ticks (linear location, shortest-way rotation), as movers are")]
        public bool Lerp(float alpha, out FVector location, out FRotator rotation)
        {
            float a = Math.Clamp(alpha, 0f, 1f);
            if (_hardBase is not null)
            {
                Transform3D b = _prevBase.InterpolateWith(_base, a);
                Transform3D world = b * _hardRel;
                location = UeSpace.ToUe(world.Origin);
                rotation = RotationOf(world.Basis.Orthonormalized());
                return true;
            }

            location = _prevLocation + ((_location - _prevLocation) * a);
            rotation = new FRotator(
                _prevRotation.Pitch + (int)(FRotator.NormalizeAxis(_rotation.Pitch - _prevRotation.Pitch) * a),
                _prevRotation.Yaw + (int)(FRotator.NormalizeAxis(_rotation.Yaw - _prevRotation.Yaw) * a),
                _prevRotation.Roll + (int)(FRotator.NormalizeAxis(_rotation.Roll - _prevRotation.Roll) * a));
            return true;
        }
    }
}
