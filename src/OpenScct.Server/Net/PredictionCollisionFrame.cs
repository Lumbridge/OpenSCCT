using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Server;

/// <summary>Collision inputs observed by an owner, rather than another pawn's later presentation pose.</summary>
internal sealed class PredictionCollisionFrame
{
    // A gameplay match has four pawns. Keep headroom for hub players and moving bases without an unbounded ring.
    private const int Capacity = 128;
    private readonly Pose[] _poses = new Pose[Capacity];
    private int _count;
    public bool Complete { get; private set; } = true;

    private struct Pose
    {
        public Actor Actor;
        public FVector Location;
        public FRotator Rotation;
        public float Radius, Height;
        public bool Collide, BlockActors, BlockPlayers;

        public static Pose Capture(Actor actor) => new()
        {
            Actor = actor, Location = actor.Location, Rotation = actor.Rotation,
            Radius = actor.CollisionRadius, Height = actor.CollisionHeight,
            Collide = actor.bCollideActors, BlockActors = actor.bBlockActors, BlockPlayers = actor.bBlockPlayers,
        };

        public readonly void Apply()
        {
            if (Actor.bDeleteMe) return;
            // CollisionWorld synchronizes dynamic brush transforms lazily from these fields. Do not use a move
            // native here: it would shove riders, change zones, or dispatch gameplay on a different actor.
            Actor.Location = Location; Actor.Rotation = Rotation;
            Actor.CollisionRadius = Radius; Actor.CollisionHeight = Height;
            Actor.bCollideActors = Collide; Actor.bBlockActors = BlockActors; Actor.bBlockPlayers = BlockPlayers;
        }
    }

    public void Capture(Pawn owner)
    {
        Clear(); Complete = true;
        var actors = owner.XLevel.Actors;
        for (int i = 0; i < actors.Count; i++)
        {
            Actor actor = actors[i];
            if (ReferenceEquals(actor, owner) || actor.bDeleteMe || actor.bWorldGeometry || actor.bStatic ||
                (actor is not Pawn && !actor.bIsAMover && !ReferenceEquals(actor, owner.Base))) continue;
            if (_count == _poses.Length) { Complete = false; return; }
            _poses[_count++] = Pose.Capture(actor);
        }
    }

    public void Apply()
    {
        for (int i = 0; i < _count; i++) _poses[i].Apply();
    }

    public void Clear()
    {
        // Release actors that left relevancy; retaining old frame references is limited to the prediction ring.
        for (int i = 0; i < _count; i++) _poses[i] = default;
        _count = 0;
    }
}
