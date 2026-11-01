using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Core;

/// <summary>
/// The runtime level (UE2 ULevel): the ordered actor list, the fixed-step clock, spawning and destroying, the tag
/// index used by events, and the per-tick update. Create it through <see cref="World"/>.
/// <para>Rules:</para>
/// <list type="bullet">
/// <item>Actors tick in list order (spawn order). An actor whose Owner or Base has not ticked yet this tick waits
/// until after the main pass (UE2 owner-first rule, extended to Base).</item>
/// <item>Spawning during a tick creates the actor and runs its spawn events at once, but it joins the list (and
/// the tag index) at the end of the tick, so it first ticks on the next tick. Outside a tick it joins at once.</item>
/// <item>Destroying marks the actor (bDeleteMe) and runs its Destroyed event at once; it leaves the list at the end
/// of the tick (or at the start of the next one when destroyed between ticks). References other actors hold to it
/// are then cleared, as UE2 does after a destroy.</item>
/// <item>Between <see cref="BeginLoad"/> and <see cref="EndLoad"/> spawns join the list at once and their
/// begin-play events wait for EndLoad, which runs them pass by pass over the whole list like a map load.</item>
/// </list>
/// </summary>
public sealed class Level
{
    private readonly List<Actor> _actors = new(1024);
    private readonly List<Actor> _pendingSpawns = new(64);
    private readonly Dictionary<Name, List<Actor>> _tagIndex = new();
    private readonly Dictionary<int, Actor> _byId = new(1024);
    private readonly List<List<Actor>> _scratchPool = new();
    private List<Actor> _deferred = new(64);
    private List<Actor> _deferredBatch = new(64);
    private int _scratchDepth;
    private int _nextActorId = 1;
    private long _nextListOrder = 1;
    private int _destroyedInList;
    private bool _cleanupNeeded;
    private bool _inBeginPlayPasses;

    internal Level(World world)
    {
        World = world;
        Info = new LevelInfo();
        Info.ActorId = _nextActorId++;
        Info.ListOrder = _nextListOrder++;
        Info.XLevel = this;
        Info.Level = Info;
        Info.Tag = Info.ClassName;
        Info.DeferredBeginPlay = true;
        _byId[Info.ActorId] = Info;
        Insert(Info);
    }

    public World World { get; }

    public ReplayClock ReplayTime(long tick, float seconds, float delta)
    {
        if (!World.IsReplaying) throw new InvalidOperationException("Replay time requires a prediction replay scope.");
        return new ReplayClock(this, tick, seconds, delta);
    }

    public ReplayClock CommandTime(long tick, float seconds, float delta) => new(this, tick, seconds, delta);

    private int _clockDepth;
    private long _worldTick;
    private float _worldSeconds, _worldDelta;

    /// <summary>
    /// Map events caused by a movement command run on the world's clock. Their timers and latent code are
    /// subsequently advanced by world ticks, so a client's older input timestamp cannot set their deadlines.
    /// Nested command scopes retain the outermost world clock. Prediction replay keeps its replay clock.
    /// </summary>
    internal ReplayClock WorldEventTime() => _clockDepth == 0 || World.IsReplaying
        ? default : new(this, _worldTick, _worldSeconds, _worldDelta);

    public readonly struct ReplayClock : IDisposable
    {
        private readonly Level _level;
        private readonly long _tick;
        private readonly float _seconds, _delta;
        internal ReplayClock(Level level, long tick, float seconds, float delta)
        {
            _level = level; _tick = level.TickCount; _seconds = level.TimeSeconds; _delta = level.DeltaTime;
            if (level._clockDepth++ == 0)
            {
                level._worldTick = _tick; level._worldSeconds = _seconds; level._worldDelta = _delta;
            }
            level.TickCount = tick; level.TimeSeconds = level.Info.TimeSeconds = seconds; level.DeltaTime = delta;
        }
        public void Dispose()
        {
            if (_level is null) return;
            _level.TickCount = _tick; _level.TimeSeconds = _level.Info.TimeSeconds = _seconds; _level.DeltaTime = _delta;
            _level._clockDepth--;
        }
    }

    /// <summary>The LevelInfo actor (always the first actor).</summary>
    public LevelInfo Info { get; }

    /// <summary>Physics implementation called for actors with a physics mode; null runs no physics.</summary>
    public IActorPhysics? PhysicsDriver { get; set; }

    // ---- services ------------------------------------------------------------------------------------------

    private readonly Dictionary<Type, object> _services = new();

    /// <summary>
    /// The per-level service of type <typeparamref name="T"/>, or null. Services are hooks one library installs for
    /// another it does not reference (the net layer for gameplay ports, for example). Lookup only, never iterated.
    /// </summary>
    public T? GetService<T>()
        where T : class => _services.TryGetValue(typeof(T), out object? service) ? (T)service : null;

    /// <summary>Installs <paramref name="service"/> as this level's <typeparamref name="T"/>; null removes it.</summary>
    public void SetService<T>(T? service)
        where T : class
    {
        if (service is null)
        {
            _services.Remove(typeof(T));
        }
        else
        {
            _services[typeof(T)] = service;
        }
    }

    // ---- clock ---------------------------------------------------------------------------------------------

    /// <summary>Number of the current tick (1 during the first tick); 0 before any tick.</summary>
    public long TickCount { get; private set; }

    /// <summary>Game seconds, accumulated in float each tick like UE2 (Sim.DeltaTime * TimeDilation per tick).</summary>
    public float TimeSeconds { get; private set; }

    /// <summary>Seconds of the current tick (Sim.DeltaTime * LevelInfo.TimeDilation).</summary>
    public float DeltaTime { get; private set; } = Sim.DeltaTime;

    public bool IsTicking { get; private set; }

    public bool IsLoading { get; private set; }

    // ---- actors --------------------------------------------------------------------------------------------

    /// <summary>The actor list in tick order. May hold destroyed actors (bDeleteMe) until the end of the tick.</summary>
    public IReadOnlyList<Actor> Actors => _actors;

    public int ActorCount => _actors.Count;

    /// <summary>Actors spawned this tick that join the list at the end of it.</summary>
    public int PendingSpawnCount => _pendingSpawns.Count;

    /// <summary>Finds a spawned, not yet removed actor by id.</summary>
    public Actor? FindActor(int actorId) => _byId.GetValueOrDefault(actorId);

    /// <summary>All live actors of type T in list order.</summary>
    public ActorIterator<T> AllActors<T>()
        where T : Actor => new(_actors, Name.None, dynamicOnly: false);

    /// <summary>All live actors of type T with the given tag, in list order.</summary>
    public ActorIterator<T> AllActors<T>(Name tag)
        where T : Actor => tag.IsNone ? new(_actors, Name.None, false) : new(_tagIndex.GetValueOrDefault(tag), tag, false);

    /// <summary>Like AllActors but skips bStatic actors (UnrealScript DynamicActors).</summary>
    public ActorIterator<T> DynamicActors<T>()
        where T : Actor => new(_actors, Name.None, dynamicOnly: true);

    public ActorIterator<T> DynamicActors<T>(Name tag)
        where T : Actor => tag.IsNone ? new(_actors, Name.None, true) : new(_tagIndex.GetValueOrDefault(tag), tag, true);

    // ---- spawn and destroy ---------------------------------------------------------------------------------

    /// <summary>
    /// Creates a T and spawns it. Returns null when the spawn fails (bStatic or bNoDelete outside a load, or the
    /// actor destroyed itself during its spawn events). The tag defaults to the class name; a given tag is applied
    /// after the spawn events, as UnrealScript Spawn does.
    /// </summary>
    public T? Spawn<T>(FVector location = default, FRotator rotation = default, Actor? owner = null,
        Pawn? instigator = null, Name tag = default)
        where T : Actor, new()
    {
        T? actor = SpawnActor(new T(), location, rotation, owner, instigator);
        if (actor is not null && !tag.IsNone)
        {
            actor.Tag = tag;
        }

        return actor;
    }

    /// <summary>
    /// Spawns an already constructed actor (UE2 ULevel::SpawnActor). Map loaders set properties on the instance
    /// before or after this call (begin-play waits for EndLoad while loading). Replicated spawns on clients pass
    /// <paramref name="bRemoteOwned"/>, which skips PostNetBeginPlay (the net code calls it after the initial
    /// properties arrive).
    /// </summary>
    [UcNative("ULevel::SpawnActor")]
    [Provenance(ProvenanceKind.Estimated, "UE2 SpawnActor event order: SetOwner, Spawned, PreBeginPlay, BeginPlay, PostBeginPlay, SetInitialState, PostNetBeginPlay")]
    public T? SpawnActor<T>(T actor, FVector location, FRotator rotation, Actor? owner = null, Pawn? instigator = null,
        bool bRemoteOwned = false)
        where T : Actor
    {
        ArgumentNullException.ThrowIfNull(actor);
        // New actors subsequently tick on world time, including their spawn timestamps and initial state waits.
        using var clock = WorldEventTime();
        if (actor.XLevel is not null)
        {
            throw new InvalidOperationException($"{actor} has already been spawned.");
        }

        if (!IsLoading && (actor.bStatic || actor.bNoDelete))
        {
            return null;
        }

        actor.ActorId = _nextActorId++;
        actor.ListOrder = _nextListOrder++;
        actor.XLevel = this;
        actor.Level = Info;
        actor.Location = location;
        actor.Rotation = rotation;
        actor.Instigator = instigator;
        if (actor.Tag.IsNone)
        {
            actor.Tag = actor.ClassName;
        }

        _byId[actor.ActorId] = actor;
        if (IsTicking)
        {
            actor.PendingInsert = true;
            _pendingSpawns.Add(actor);
        }
        else
        {
            Insert(actor);
        }

        if (owner is not null)
        {
            actor.SetOwner(owner);
        }

        ZoneRegions.SetActorZone(actor, bTest: false, bForceRefresh: true);
        actor.Spawned();

        if (IsLoading && !_inBeginPlayPasses)
        {
            actor.DeferredBeginPlay = true;
            return actor;
        }

        actor.eventPreBeginPlay();
        if (actor.bDeleteMe)
        {
            return null;
        }

        actor.eventBeginPlay();
        if (actor.bDeleteMe)
        {
            return null;
        }

        actor.eventPostBeginPlay();
        if (actor.bDeleteMe)
        {
            return null;
        }

        actor.eventSetInitialState();
        if (actor.bDeleteMe)
        {
            return null;
        }

        if (!bRemoteOwned)
        {
            actor.eventPostNetBeginPlay();
            if (actor.bDeleteMe)
            {
                return null;
            }
        }

        return actor;
    }

    /// <summary>
    /// Destroys an actor (UE2 ULevel::DestroyActor): Destroyed event, detach from base and attached actors, end
    /// touches, drop the owner, then mark it deleted. False for bStatic or bNoDelete actors.
    /// </summary>
    [UcNative("ULevel::DestroyActor")]
    [Provenance(ProvenanceKind.Estimated, "UE2 DestroyActor order; to be confirmed in the game exe")]
    public bool Destroy(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!ReferenceEquals(actor.XLevel, this))
        {
            throw new InvalidOperationException($"{actor} does not belong to this level.");
        }

        if (actor.bDeleteMe || actor.Destroying)
        {
            return true;
        }

        if (actor.bStatic || actor.bNoDelete)
        {
            return false;
        }

        actor.Destroying = true;
        try
        {
            actor.eventDestroyed();
            actor.ClearOwnerAndBaseForDestroy();
        }
        finally
        {
            actor.Destroying = false;
        }

        actor.bDeleteMe = true;
        actor.StopLatentCodeForDestroy();
        _cleanupNeeded = true;
        if (actor.InList)
        {
            _destroyedInList++;
        }

        return true;
    }

    // ---- loading -------------------------------------------------------------------------------------------

    /// <summary>Starts a map load: spawns join the list at once and their begin-play events wait for EndLoad.</summary>
    public void BeginLoad()
    {
        if (IsTicking || IsLoading)
        {
            throw new InvalidOperationException("BeginLoad must be called outside a tick and outside another load.");
        }

        if (TickCount != 0)
        {
            throw new InvalidOperationException("A level can only be loaded before its first tick.");
        }

        IsLoading = true;
    }

    /// <summary>
    /// Ends a map load: runs PreBeginPlay, BeginPlay, PostBeginPlay, SetInitialState and PostNetBeginPlay as
    /// separate passes over the loaded actors in list order, then sets LevelInfo.bBegunPlay.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 map load runs each begin-play event as a pass over all actors; exact pass order to be confirmed")]
    public void EndLoad()
    {
        if (!IsLoading)
        {
            throw new InvalidOperationException("EndLoad without BeginLoad.");
        }

        _inBeginPlayPasses = true;
        try
        {
            for (int pass = 0; pass < 5; pass++)
            {
                for (int i = 0; i < _actors.Count; i++)
                {
                    Actor a = _actors[i];
                    if (!a.DeferredBeginPlay || a.bDeleteMe)
                    {
                        continue;
                    }

                    switch (pass)
                    {
                        case 0:
                            a.eventPreBeginPlay();
                            break;
                        case 1:
                            a.eventBeginPlay();
                            break;
                        case 2:
                            a.eventPostBeginPlay();
                            break;
                        case 3:
                            a.eventSetInitialState();
                            break;
                        default:
                            a.eventPostNetBeginPlay();
                            break;
                    }
                }
            }

            for (int i = 0; i < _actors.Count; i++)
            {
                _actors[i].DeferredBeginPlay = false;
            }
        }
        finally
        {
            _inBeginPlayPasses = false;
            IsLoading = false;
        }

        Info.bBegunPlay = true;
        ApplyPending();
    }

    /// <summary>
    /// Reorders the list like UE2 ULevel::SortActorList: LevelInfo, then static actors, then dynamic actors that
    /// do not replicate, then replicated ones, each group keeping its order. Call it at the end of a map load.
    /// </summary>
    [UcNative("ULevel::SortActorList")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 SortActorList grouping; whether and when SCCT calls it is to be confirmed")]
    public void SortActorList()
    {
        if (IsTicking)
        {
            throw new InvalidOperationException("SortActorList cannot run during a tick.");
        }

        ApplyPending();
        var sorted = new List<Actor>(_actors.Count) { Info };
        foreach (Actor a in _actors)
        {
            if (!ReferenceEquals(a, Info) && a.bStatic && !a.bAlwaysRelevant)
            {
                sorted.Add(a);
            }
        }

        foreach (Actor a in _actors)
        {
            if (!ReferenceEquals(a, Info) && (!a.bStatic || a.bAlwaysRelevant) && a.RemoteRole == ENetRole.ROLE_None)
            {
                sorted.Add(a);
            }
        }

        foreach (Actor a in _actors)
        {
            if (!ReferenceEquals(a, Info) && (!a.bStatic || a.bAlwaysRelevant) && a.RemoteRole != ENetRole.ROLE_None)
            {
                sorted.Add(a);
            }
        }

        _actors.Clear();
        _actors.AddRange(sorted);
        _nextListOrder = 1;
        foreach (Actor a in _actors)
        {
            a.ListOrder = _nextListOrder++;
        }

        foreach (List<Actor> list in _tagIndex.Values)
        {
            list.Sort(static (x, y) => x.ListOrder.CompareTo(y.ListOrder));
        }
    }

    // ---- events --------------------------------------------------------------------------------------------

    /// <summary>
    /// Calls Trigger on every live, non-static actor tagged <paramref name="EventName"/>, in list order
    /// (UnrealScript TriggerEvent, which walks DynamicActors). Actors spawned this tick are not in the index yet.
    /// </summary>
    [Uc("Actor.TriggerEvent")]
    [Provenance(ProvenanceKind.Estimated, "Stock UE2 TriggerEvent walks DynamicActors; to be checked against the decompiled Engine.u")]
    public void TriggerEvent(Name EventName, Actor? Other, Pawn? EventInstigator) =>
        FireEvent(EventName, Other, EventInstigator, untrigger: false);

    /// <summary>Calls UnTrigger on every live, non-static actor tagged <paramref name="EventName"/>.</summary>
    [Uc("Actor.UntriggerEvent")]
    public void UntriggerEvent(Name EventName, Actor? Other, Pawn? EventInstigator) =>
        FireEvent(EventName, Other, EventInstigator, untrigger: true);

    private void FireEvent(Name eventName, Actor? other, Pawn? instigator, bool untrigger)
    {
        if (eventName.IsNone || !_tagIndex.TryGetValue(eventName, out List<Actor>? tagged) || tagged.Count == 0)
        {
            return;
        }

        // Handlers may change tags or fire further events, so walk a snapshot.
        List<Actor> snapshot = RentScratch();
        try
        {
            snapshot.AddRange(tagged);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Actor a = snapshot[i];
                if (a.bDeleteMe || a.bStatic || a.Tag != eventName)
                {
                    continue;
                }

                if (untrigger)
                {
                    a.eventUnTrigger(other, instigator);
                }
                else
                {
                    a.eventTrigger(other, instigator);
                }
            }
        }
        finally
        {
            ReturnScratch(snapshot);
        }
    }

    private List<Actor> RentScratch()
    {
        if (_scratchDepth == _scratchPool.Count)
        {
            _scratchPool.Add(new List<Actor>(32));
        }

        return _scratchPool[_scratchDepth++];
    }

    private void ReturnScratch(List<Actor> list)
    {
        list.Clear();
        _scratchDepth--;
    }

    // ---- tick ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Advances one fixed tick: clock (and a fresh SimEvents buffer), then every actor's native tick (Tick event,
    /// state code, timer, LifeSpan, physics) in list order with owners and bases first, then queued spawns and
    /// destroys. Hosts normally call <see cref="World.Step()"/>, which also feeds the event sink.
    /// </summary>
    [UcNative("ULevel::Tick")]
    public void Tick()
    {
        if (IsTicking || IsLoading)
        {
            throw new InvalidOperationException("Level.Tick cannot run inside a tick or a load.");
        }

        ApplyPending();
        Info.bBegunPlay = true;
        TickCount++;
        DeltaTime = Sim.DeltaTime * Info.TimeDilation;
        TimeSeconds += DeltaTime;
        Info.TimeSeconds = TimeSeconds;
        World.Events.BeginTick(TickCount);

        IsTicking = true;
        try
        {
            TickActors();
        }
        finally
        {
            IsTicking = false;
            _deferred.Clear();
            _deferredBatch.Clear();
        }

        ApplyPending();
        World.Events.EndTick();
    }

    private void TickActors()
    {
        long stamp = TickCount;
        float dt = DeltaTime;
        List<Actor> actors = _actors;
        int count = actors.Count;
        for (int i = 0; i < count; i++)
        {
            Actor a = actors[i];
            if (a.bDeleteMe || a.bStatic || a.TickedStamp == stamp)
            {
                continue;
            }

            if (MustWait(a, stamp))
            {
                _deferred.Add(a);
                continue;
            }

            a.TickedStamp = stamp;
            a.NativeTick(dt);
        }

        // Deferred actors, newest first like the UE2 linked list, repeated until no one is waiting.
        while (_deferred.Count > 0)
        {
            (_deferred, _deferredBatch) = (_deferredBatch, _deferred);
            bool progressed = false;
            for (int i = _deferredBatch.Count - 1; i >= 0; i--)
            {
                Actor a = _deferredBatch[i];
                if (a.bDeleteMe || a.TickedStamp == stamp)
                {
                    continue;
                }

                if (MustWait(a, stamp))
                {
                    _deferred.Add(a);
                    continue;
                }

                a.TickedStamp = stamp;
                a.NativeTick(dt);
                progressed = true;
            }

            _deferredBatch.Clear();
            if (!progressed)
            {
                // Owner or base loop: tick the rest anyway so no actor starves.
                for (int i = _deferred.Count - 1; i >= 0; i--)
                {
                    Actor a = _deferred[i];
                    if (!a.bDeleteMe && a.TickedStamp != stamp)
                    {
                        a.TickedStamp = stamp;
                        a.NativeTick(dt);
                    }
                }

                _deferred.Clear();
            }
        }
    }

    private static bool MustWait(Actor a, long stamp) => IsUnticked(a.Owner, a, stamp) || IsUnticked(a.Base, a, stamp);

    private static bool IsUnticked(Actor? other, Actor self, long stamp) =>
        other is not null && !ReferenceEquals(other, self) && other.InList && !other.bDeleteMe && !other.bStatic &&
        other.TickedStamp != stamp;

    // ---- list bookkeeping ----------------------------------------------------------------------------------

    private void Insert(Actor actor)
    {
        actor.PendingInsert = false;
        actor.InList = true;
        _actors.Add(actor);
        AddToTagIndex(actor, actor.Tag);
    }

    private void ApplyPending()
    {
        if (_destroyedInList > 0)
        {
            int write = 0;
            for (int read = 0; read < _actors.Count; read++)
            {
                Actor a = _actors[read];
                if (a.bDeleteMe)
                {
                    RemoveFromTagIndex(a, a.Tag);
                    _byId.Remove(a.ActorId);
                    a.InList = false;
                }
                else
                {
                    _actors[write++] = a;
                }
            }

            _actors.RemoveRange(write, _actors.Count - write);
            _destroyedInList = 0;
        }

        if (_pendingSpawns.Count > 0)
        {
            for (int i = 0; i < _pendingSpawns.Count; i++)
            {
                Actor a = _pendingSpawns[i];
                if (a.bDeleteMe)
                {
                    a.PendingInsert = false;
                    _byId.Remove(a.ActorId);
                }
                else
                {
                    Insert(a);
                }
            }

            _pendingSpawns.Clear();
        }

        if (_cleanupNeeded)
        {
            _cleanupNeeded = false;
            for (int i = 0; i < _actors.Count; i++)
            {
                _actors[i].CleanupDestroyedReferences();
            }
        }
    }

    internal void OnTagChanged(Actor actor, Name oldTag)
    {
        if (!actor.InList)
        {
            return;
        }

        RemoveFromTagIndex(actor, oldTag);
        AddToTagIndex(actor, actor.Tag);
    }

    private void AddToTagIndex(Actor actor, Name tag)
    {
        if (tag.IsNone)
        {
            return;
        }

        if (!_tagIndex.TryGetValue(tag, out List<Actor>? list))
        {
            list = new List<Actor>(4);
            _tagIndex[tag] = list;
        }

        // Keep list order: new spawns append, re-tagged actors go back to their place.
        int index = list.Count;
        while (index > 0 && list[index - 1].ListOrder > actor.ListOrder)
        {
            index--;
        }

        list.Insert(index, actor);
    }

    private void RemoveFromTagIndex(Actor actor, Name tag)
    {
        if (!tag.IsNone && _tagIndex.TryGetValue(tag, out List<Actor>? list))
        {
            list.Remove(actor);
        }
    }
}
