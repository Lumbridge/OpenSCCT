using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Base of every simulated object (Engine.Actor). Field names are the original ones. Defaults below are the stock
/// UE2 Actor defaults until the class-defaults pack decoded from Engine.u replaces them.
/// <para>
/// <see cref="Level"/> is the LevelInfo actor, as in UnrealScript; <see cref="XLevel"/> is the runtime
/// <see cref="OpenScct.Core.Level"/> that owns the actor list and the clock.
/// </para>
/// </summary>
public abstract partial class Actor
{
    protected Actor()
    {
        ClassName = new Name(GetType().Name);
    }

    // ---- identity ------------------------------------------------------------------------------------------

    /// <summary>Runtime id, unique within the level, assigned at spawn in spawn order (LevelInfo is 1).</summary>
    public int ActorId { get; internal set; }

    /// <summary>UnrealScript class name (the C# type name).</summary>
    public Name ClassName { get; }

    /// <summary>The level's LevelInfo actor (UnrealScript Level).</summary>
    public LevelInfo Level { get; internal set; } = null!;

    /// <summary>The runtime level that owns this actor (UnrealScript XLevel).</summary>
    public Level XLevel { get; internal set; } = null!;

    /// <summary>True once the actor has been destroyed. It stays in lists until the end of the tick.</summary>
    public bool bDeleteMe { get; internal set; }

    /// <summary>True when the actor has already been ticked in the current tick.</summary>
    public bool bTicked => XLevel is not null && TickedStamp == XLevel.TickCount;

    // ---- movement ------------------------------------------------------------------------------------------

    public FVector Location;
    public FRotator Rotation;
    public FVector Velocity;
    public FVector Acceleration;
    public EPhysics Physics;

    /// <summary>The actor this one is based on (standing on or attached to). Change it with <see cref="SetBase"/>.</summary>
    public Actor? Base { get; private set; }

    /// <summary>Actors based on this one (UE2 Attached).</summary>
    public readonly List<Actor> Attached = new();

    public bool bInterpolating;
    public bool bCollideWorld;

    /// <summary>World geometry (level BSP, terrain) does not track attached actors.</summary>
    public bool bWorldGeometry;

    // ---- collision -----------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Actor default")]
    public float CollisionRadius = 22f;

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Actor default")]
    public float CollisionHeight = 22f;

    public bool bCollideActors;
    public bool bBlockActors;
    public bool bBlockPlayers;

    /// <summary>Actors currently overlapping this one. Managed by <see cref="BeginTouch"/> and <see cref="EndTouch"/>.</summary>
    public readonly List<Actor> Touching = new();

    // ---- ownership -----------------------------------------------------------------------------------------

    /// <summary>Owning actor. Change it with <see cref="SetOwner"/>.</summary>
    public Actor? Owner { get; private set; }

    public Pawn? Instigator;

    // ---- events and display --------------------------------------------------------------------------------

    private Name _tag;

    /// <summary>Event tag this actor answers to. Defaults to the class name at spawn. Changing it updates the level's tag index.</summary>
    public Name Tag
    {
        get => _tag;
        set
        {
            Name old = _tag;
            if (old == value)
            {
                return;
            }

            _tag = value;
            XLevel?.OnTagChanged(this, old);
        }
    }

    /// <summary>Event this actor fires (TriggerEvent(Event, ...)).</summary>
    public Name Event;

    public bool bHidden;

    // ---- lifetime ------------------------------------------------------------------------------------------

    /// <summary>Seconds until the actor destroys itself; 0 means forever.</summary>
    public float LifeSpan;

    public bool bStatic;
    public bool bNoDelete;
    public bool bMovable = true;

    public bool bIsAPawn;
    public bool bIsAPlayerController;

    // ---- networking ----------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Actor default")]
    public ENetRole Role = ENetRole.ROLE_Authority;

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Actor default")]
    public ENetRole RemoteRole = ENetRole.ROLE_DumbProxy;

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Actor default")]
    public float NetPriority = 1f;

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 Actor default")]
    public float NetUpdateFrequency = 100f;

    public bool bAlwaysRelevant;
    public bool bNetTemporary;

    // ---- timers and script state ---------------------------------------------------------------------------

    public float TimerRate;
    public float TimerCounter;
    public bool bTimerLoop;

    /// <summary>Seconds argument of the last Sleep (UE2 keeps it here; the tick-based wake uses the frame).</summary>
    public float LatentFloat;

    /// <summary>State entered by SetInitialState; None means the auto state.</summary>
    public Name InitialState;

    public bool bScriptInitialized;

    // ---- level bookkeeping (owned by Level) ----------------------------------------------------------------

    internal long TickedStamp = -1;
    internal long ListOrder;
    internal bool InList;
    internal bool PendingInsert;
    internal bool DeferredBeginPlay;
    internal bool Destroying;

    // ---- natives -------------------------------------------------------------------------------------------

    /// <summary>Destroys this actor (UnrealScript Destroy). False for bStatic or bNoDelete actors.</summary>
    public bool Destroy() => XLevel is not null && XLevel.Destroy(this);

    /// <summary>
    /// Spawns an actor next to this one (UnrealScript Spawn). Location and rotation default to this actor's,
    /// the instigator is this actor's instigator, and the tag is applied after the spawn events, as in UE2.
    /// </summary>
    [UcNative("AActor::execSpawn")]
    public T? Spawn<T>(Actor? SpawnOwner = null, Name SpawnTag = default, FVector? SpawnLocation = null,
        FRotator? SpawnRotation = null)
        where T : Actor, new()
    {
        T? spawned = XLevel.SpawnActor(new T(), SpawnLocation ?? Location, SpawnRotation ?? Rotation, SpawnOwner, Instigator);
        if (spawned is not null && !SpawnTag.IsNone)
        {
            spawned.Tag = SpawnTag;
        }

        return spawned;
    }

    /// <summary>Starts or stops the actor timer (UE2 SetTimer). A rate of 0 stops it.</summary>
    [UcNative("AActor::execSetTimer")]
    public void SetTimer(float NewTimerRate, bool bLoop)
    {
        TimerCounter = 0f;
        TimerRate = NewTimerRate;
        bTimerLoop = bLoop;
    }

    /// <summary>Changes the owner, sending LostChild to the old owner and GainedChild to the new one.</summary>
    [UcNative("AActor::SetOwner")]
    public void SetOwner(Actor? NewOwner)
    {
        if (Owner is not null)
        {
            Owner.eventLostChild(this);
        }

        Owner = NewOwner;
        if (Owner is not null)
        {
            Owner.eventGainedChild(this);
        }
    }

    /// <summary>
    /// Changes the base. Refuses loops. The old base gets Detach and the new one Attach (unless world geometry);
    /// this actor gets BaseChange when <paramref name="bNotifyActor"/> is set.
    /// </summary>
    [UcNative("AActor::SetBase")]
    public void SetBase(Actor? NewBase, bool bNotifyActor = true)
    {
        for (Actor? loop = NewBase; loop is not null; loop = loop.Base)
        {
            if (ReferenceEquals(loop, this))
            {
                return;
            }
        }

        if (ReferenceEquals(NewBase, Base))
        {
            return;
        }

        Actor? oldBase = Base;
        if (oldBase is not null && !oldBase.bWorldGeometry)
        {
            oldBase.Attached.Remove(this);
            if (XLevel?.World.IsReplaying != true) oldBase.eventDetach(this);
        }

        Base = NewBase;
        if (NewBase is not null && !NewBase.bWorldGeometry)
        {
            NewBase.Attached.Add(this);
            if (XLevel?.World.IsReplaying != true) NewBase.eventAttach(this);
        }

        if (bNotifyActor && XLevel?.World.IsReplaying != true)
        {
            eventBaseChange();
        }
    }

    /// <summary>Starts a touch between this actor and <paramref name="Other"/>: both lists, then Touch on each.</summary>
    [UcNative("AActor::BeginTouch")]
    [Provenance(ProvenanceKind.Estimated, "UE2 AActor::BeginTouch order; to be confirmed in the game exe")]
    public void BeginTouch(Actor Other)
    {
        if (XLevel?.World.IsReplaying == true) return;
        if (ReferenceEquals(Other, this) || Touching.Contains(Other))
        {
            return;
        }

        Touching.Add(Other);
        Other.Touching.Add(this);
        eventTouch(Other);
        if (!Other.bDeleteMe && Other.Touching.Contains(this))
        {
            Other.eventTouch(this);
        }
    }

    /// <summary>Ends a touch. UnTouch goes to this actor (unless <paramref name="NoNotifySelf"/>) and to the other.</summary>
    [UcNative("AActor::EndTouch")]
    public void EndTouch(Actor Other, bool NoNotifySelf = false)
    {
        if (XLevel?.World.IsReplaying == true) return;
        if (!NoNotifySelf)
        {
            eventUnTouch(Other);
        }

        Touching.Remove(Other);
        Other.eventUnTouch(this);
        Other.Touching.Remove(this);
    }

    /// <summary>Fires an event at every dynamic actor tagged <paramref name="EventName"/> (calls their Trigger).</summary>
    public virtual void TriggerEvent(Name EventName, Actor? Other, Pawn? EventInstigator) =>
        XLevel.TriggerEvent(EventName, Other, EventInstigator);

    /// <summary>Calls UnTrigger on every dynamic actor tagged <paramref name="EventName"/>.</summary>
    public virtual void UntriggerEvent(Name EventName, Actor? Other, Pawn? EventInstigator) =>
        XLevel.UntriggerEvent(EventName, Other, EventInstigator);

    // ---- random numbers (UnrealScript FRand, Rand, RandRange, VRand) ---------------------------------------

    protected float FRand() => XLevel.World.Random.FRand();

    protected int Rand(int Max) => XLevel.World.Random.Rand(Max);

    protected float RandRange(float Min, float Max) => XLevel.World.Random.RandRange(Min, Max);

    protected FVector VRand() => XLevel.World.Random.VRand();

    // ---- per-tick update -----------------------------------------------------------------------------------

    /// <summary>
    /// Native per-tick update (UE2 AActor::Tick after the owner-first check): Tick event, state code, timer,
    /// LifeSpan, then physics. The role gates of spec/native/net-replication.md rule 26 decide what runs: a dumb
    /// proxy or ROLE_None actor gets no script, timers or state code, only falling or rotating physics; an
    /// autonomous proxy skips physics (its moves run it); on the server, a remote player's autonomous actor runs no
    /// physics of its own and only catches up when its moves stall (<see cref="CatchUpRemoteAutonomous"/>).
    /// </summary>
    [UcNative("AActor::Tick", Editor = 0x11183b50)]
    [PortOf("eng:AActor::Tick", Spec = "spec/native/net-replication.md#receive-and-apply-client")]
    [Provenance(ProvenanceKind.Estimated, "UE2 AActor::Tick order; to be confirmed in the game exe")]
    [Provenance(ProvenanceKind.Decoded, "net-replication.md rule 26: role gates and the remote autonomous catch-up")]
    protected internal virtual void NativeTick(float DeltaSeconds)
    {
        if (Role < ENetRole.ROLE_SimulatedProxy)
        {
            if (Physics is EPhysics.PHYS_Falling or EPhysics.PHYS_Rotating)
            {
                XLevel.PhysicsDriver?.performPhysics(this, DeltaSeconds);
            }

            return;
        }

        eventTick(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        ProcessState(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        UpdateTimers(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        if (LifeSpan != 0f)
        {
            LifeSpan -= DeltaSeconds;
            if (LifeSpan <= 0.0001f)
            {
                XLevel.Destroy(this);
                return;
            }
        }

        // A class with its own simulated tick moves its proxies there (ISimulatedTick), whatever the physics mode.
        if (Role == ENetRole.ROLE_SimulatedProxy && XLevel.GetService<ISimulatedTick>() is { } simulated &&
            simulated.TickSimulated(this, DeltaSeconds))
        {
            return;
        }

        if (Physics == EPhysics.PHYS_None || Role == ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        if (RemoteRole == ENetRole.ROLE_AutonomousProxy && TopPlayerController() is { Player: not UViewport } remote)
        {
            CatchUpRemoteAutonomous(remote);
            return;
        }

        XLevel.PhysicsDriver?.performPhysics(this, DeltaSeconds);
    }

    /// <summary>The player controller at the top of the Owner chain (this actor itself when it is one), or null.</summary>
    public PlayerController? TopPlayerController()
    {
        Actor top = this;
        for (int guard = 0; top.Owner is { } owner && guard < 64; guard++)
        {
            top = owner;
        }

        return top as PlayerController;
    }

    /// <summary>
    /// The server side of a remote player's autonomous actor (rule 26): its moves run its physics, so the tick
    /// does not; when no move arrived for more than half of <c>MaxResponseTime</c> (0.35 s) the physics catches up
    /// for the elapsed time (only while the actor moves), and the controller's server time stamp becomes now.
    /// <para>Pawn classes with their own native tick replace it (ASBasePawn::Tick, spec/native/net-moves.md rule 18).</para>
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "net-replication.md rule 26: 0.5 x MaxResponseTime stall, velocity not zero")]
    protected internal virtual void CatchUpRemoteAutonomous(PlayerController pc)
    {
        float now = XLevel.TimeSeconds;
        float stalled = now - pc.ServerTimeStamp;
        if (pc.ServerTimeStamp == 0f || !(stalled > 0.5f * pc.MaxResponseTime))
        {
            return;
        }

        if (!Velocity.IsZero())
        {
            XLevel.PhysicsDriver?.performPhysics(this, stalled);
        }

        pc.ServerTimeStamp = now;
    }

    /// <summary>Advances the actor timer and calls Timer once if it expired (UE2 AActor::UpdateTimers).</summary>
    [UcNative("AActor::UpdateTimers")]
    protected internal void UpdateTimers(float DeltaSeconds)
    {
        if (TimerRate > 0f && (TimerCounter += DeltaSeconds) >= TimerRate)
        {
            int ticksPassed = SvmMath.Trunc(TimerCounter / TimerRate);
            TimerCounter -= TimerRate * ticksPassed;
            if (ticksPassed != 0 && !bTimerLoop)
            {
                TimerRate = 0f;
            }

            eventTimer();
        }
    }

    /// <summary>Called once at spawn before PreBeginPlay (UE2 AActor::Spawned).</summary>
    protected internal virtual void Spawned()
    {
    }

    /// <summary>
    /// Clears references to destroyed actors, which UE2 does for every actor after a destroy
    /// (ULevel::CleanupDestroyed). Subclasses clear their own actor references and call base.
    /// </summary>
    protected internal virtual void CleanupDestroyedReferences()
    {
        if (Owner is { bDeleteMe: true })
        {
            Owner = null;
        }

        if (Base is { bDeleteMe: true })
        {
            Base = null;
        }

        if (Instigator is { bDeleteMe: true })
        {
            Instigator = null;
        }
    }

    /// <summary><paramref name="actor"/> if it is alive, otherwise null.</summary>
    protected static T? Live<T>(T? actor)
        where T : Actor => actor is { bDeleteMe: false } ? actor : null;

    internal void ClearOwnerAndBaseForDestroy()
    {
        if (Base is not null)
        {
            SetBase(null);
        }

        for (int i = Attached.Count - 1; i >= 0; i--)
        {
            if (i < Attached.Count)
            {
                Attached[i].SetBase(null);
            }
        }

        while (Touching.Count > 0)
        {
            int before = Touching.Count;
            EndTouch(Touching[0], NoNotifySelf: true);
            if (Touching.Count == before)
            {
                Touching.RemoveAt(0);
            }
        }

        if (Owner is not null)
        {
            SetOwner(null);
        }
    }

    public override string ToString() => $"{ClassName}#{ActorId}";
}
