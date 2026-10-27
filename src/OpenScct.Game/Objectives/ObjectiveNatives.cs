using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// The level services the objective port uses, the engine natives OpenScct.Core does not declare yet (SetCollisionSize,
/// SetRotation, bone attachment, run-time Spawn, RadiusActors) and small shared rules (alive, side, team, state
/// changes). The other natives (SetCollision, SetLocation, SetStaticMesh, PlaySound...) are the Actor methods.
/// </summary>
public static class ObjectiveNatives
{
    /// <summary>The level's net hooks, or the local stand-in.</summary>
    public static IObjectiveNet Net(Actor actor) => actor.XLevel.GetService<IObjectiveNet>() ?? ObjectiveNetLocal.Instance;

    /// <summary>The level's hooks into the game rules, or the stand-in over the level's SGameInfo.</summary>
    public static IObjectiveHost Host(Actor actor) => actor.XLevel.GetService<IObjectiveHost>() ?? ObjectiveHostLocal.Instance;

    // ---- collision and placement ---------------------------------------------------------------------------

    /// <summary>
    /// UnrealScript SetCollisionSize: through the level's physics driver when there is one (so touches update),
    /// otherwise the fields only.
    /// </summary>
    [UcNative("AActor::execSetCollisionSize", Exe = 0x10a24d50)]
    public static void SetCollisionSize(Actor actor, float NewRadius, float NewHeight)
    {
        if (actor.XLevel.PhysicsDriver is PhysicsDriver driver)
        {
            driver.SetCollisionSize(actor, NewRadius, NewHeight);
            return;
        }

        actor.CollisionRadius = NewRadius;
        actor.CollisionHeight = NewHeight;
    }

    /// <summary>UnrealScript SetRotation (no encroachment test: the objective code only turns unblocked actors).</summary>
    [UcNative("AActor::execSetRotation")]
    public static void SetRotation(Actor actor, FRotator NewRotation) => actor.Rotation = NewRotation;

    /// <summary>
    /// Attaches <paramref name="attachment"/> to <paramref name="host"/> at <paramref name="BoneName"/> (base and
    /// bone name only: bone placement belongs to the animation system).
    /// </summary>
    [UcNative("AActor::execAttachToBone")]
    [Provenance(ProvenanceKind.Estimated, "Attachment bookkeeping only; the bone transform comes from the animation port")]
    public static bool AttachToBone(Actor host, Actor attachment, Name BoneName)
    {
        attachment.SetBase(null);
        attachment.SetBase(host);
        attachment.AttachmentBone = BoneName;
        return true;
    }

    [UcNative("AActor::execDetachFromBone")]
    [Provenance(ProvenanceKind.Estimated, "Attachment bookkeeping only; the bone transform comes from the animation port")]
    public static bool DetachFromBone(Actor attachment)
    {
        attachment.SetBase(null);
        attachment.AttachmentBone = Name.None;
        return true;
    }

    // ---- spawning ------------------------------------------------------------------------------------------

    /// <summary>
    /// A run-time UnrealScript Spawn: as in UE2 it fails for bStatic and bNoDelete classes, even while the map
    /// loads (the runtime's map loader may place such actors, script cannot). The spawn ignores collision.
    /// </summary>
    [UcNative("AActor::execSpawn")]
    [Provenance(ProvenanceKind.Estimated, "UE2 SpawnActor refuses bStatic/bNoDelete classes outside the editor")]
    public static T? Spawn<T>(Actor spawner, Actor? SpawnOwner, FVector SpawnLocation, FRotator SpawnRotation)
        where T : Actor, new()
    {
        var actor = new T();
        if (actor.bStatic || actor.bNoDelete)
        {
            return null;
        }

        return spawner.XLevel.SpawnActor(actor, SpawnLocation, SpawnRotation, SpawnOwner, spawner.Instigator);
    }

    // ---- searches ------------------------------------------------------------------------------------------

    /// <summary>
    /// The first <typeparamref name="T"/> in actor-list order within <paramref name="Radius"/> of
    /// <paramref name="Center"/> (UnrealScript <c>foreach RadiusActors(class'T', A, Radius)</c> then break).
    /// </summary>
    [UcNative("AActor::execRadiusActors")]
    [Provenance(ProvenanceKind.Estimated, "RadiusActors distance test from the actor centre, strict less-than; SCCT body not checked")]
    public static T? FirstWithin<T>(Level level, FVector Center, float Radius, System.Func<T, bool>? filter = null)
        where T : Actor
    {
        float r2 = Radius * Radius;
        foreach (T a in level.AllActors<T>())
        {
            if ((a.Location - Center).SizeSquared() < r2 && (filter is null || filter(a)))
            {
                return a;
            }
        }

        return null;
    }

    // ---- players -------------------------------------------------------------------------------------------

    /// <summary>True when the controller has a pawn with health left (the script's "is alive" tests).</summary>
    public static bool IsAlive(Controller? c) => c is { bDeleteMe: false, Pawn: { bDeleteMe: false, Health: > 0 } };

    /// <summary>The player's side: the pawn's SPawnType, or the controller's when it has no SBasePawn.</summary>
    public static PawnType PawnTypeOf(Controller c) =>
        c.Pawn is SBasePawn p ? p.SPawnType : c is SPlayerController pc ? pc.SPawnType : PawnType.SPAWNOFFENSE;

    /// <summary>Whether <paramref name="UsableBy"/> lets a player of <paramref name="type"/> use an entry.</summary>
    [Provenance(ProvenanceKind.Estimated, "Usable enum meaning from interaction-system.md; U_DEFAULT_USABILITY treated as both")]
    public static bool UsableByAllows(Usable UsableBy, PawnType type) => UsableBy switch
    {
        Usable.U_BOTH or Usable.U_DEFAULT_USABILITY => true,
        Usable.U_SPAWNDEFENSE => type == PawnType.SPAWNDEFENSE,
        Usable.U_SPAWNOFFENSE => type == PawnType.SPAWNOFFENSE,
        _ => false,
    };

    /// <summary>Team index of a player (PRI.Team.TeamIndex), or -1 without a team.</summary>
    public static int TeamIndexOf(Controller c) => c.PlayerReplicationInfo?.Team?.TeamIndex ?? -1;

    /// <summary>
    /// Server-ordered state change (UnrealScript MirrorState): a player goes through the controller's state sync
    /// (<c>SPlayerController.MirrorState</c>), any other actor has no owning connection and changes state here.
    /// </summary>
    public static void MirrorState(Actor actor, Name State, Name Label = default, bool bDontSaveCurrentState = false)
    {
        if (actor is SPlayerController pc)
        {
            pc.eventMirrorState(State, Label, bDontSaveCurrentState);
        }
        else
        {
            actor.GotoState(State, Label);
        }
    }

    /// <summary>State change on the machine that runs the player's input (<c>SPlayerController.ChangeState</c>).</summary>
    public static void ChangeState(Actor actor, Name State, Name Label = default, bool bDontSaveCurrentState = false)
    {
        if (actor is SPlayerController pc)
        {
            pc.eventChangeState(State, Label, bDontSaveCurrentState);
        }
        else
        {
            actor.GotoState(State, Label);
        }
    }

    /// <summary>
    /// <c>GameReplicationInfo.PreciseElapsedTime()</c>: ElapsedTime + (TimeSeconds - SecondCount) / TimeDilation
    /// (spec/game/replication-info.md rule 5). Zero without a GRI.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "spec/game/replication-info.md rule 5")]
    public static float PreciseElapsedTime(LevelInfo level)
    {
        GameReplicationInfo? gri = level.GRI;
        if (gri is null)
        {
            return 0f;
        }

        return gri.ElapsedTime + (level.TimeSeconds - gri.SecondCount) / level.TimeDilation;
    }

    /// <summary>
    /// An actor's object name for stats (UnrealScript <c>self.Name</c>). The runtime keeps no map export names
    /// yet, so this is the class name and actor id.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Stand-in for the map object name until the loader records export names")]
    public static Name ObjectName(Actor actor) =>
        new(string.Concat(actor.ClassName.ToString(), actor.ActorId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
