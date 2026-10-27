# OpenScct.Core

The UE2 runtime the simulation is built on: names, math, actors, the level tick, states with latent code,
timers, the tag event bus, presentation events and seeded random streams. Pure C#, no Godot, deterministic
(the banned-API analyzer from `build/Sim.props` applies).

Namespaces:

- `OpenScct.Core`: `Name`, `FVector`, `FRotator`, `FCoords`, `SvmMath`, `SimRandom`, `Sim`, `World`, `Level`,
  `SimEvents`, `ActorState`, `StateTable`, `LatentFrame`, `ActorIterator<T>`, `IActorPhysics`, `StableHash`.
- `OpenScct.Core.Engine`: ports of Engine package classes (`Actor`, `Pawn`, `Controller`, `PlayerController`, `Info`,
  `ZoneInfo`, `LevelInfo`, `GameInfo`, `ReplicationInfo`, `GameReplicationInfo`, `PlayerReplicationInfo`) and
  their enums (`EPhysics`, `ENetRole`, `ENetMode`, values decoded from Engine.u).

## Math

- `Name`: case-insensitive interned name, compared by index. `default(Name)`, `""` and `"None"` are `Name.None`.
  Strings convert implicitly. Indexes are process-local: serialize names as text.
- `FVector`: UE2 operators. `|` is dot, `^` is cross (bracket them, C# gives them low precedence), `*` and `/` with
  a vector are component-wise, `v / s` multiplies by `1 / s` like UE2. `Size`, `SizeSquared`, `Size2D`,
  `SafeNormal`, `Normalize`, `Rotation`, `MirrorByVector`, `TransformVectorBy`...
- `FRotator`: ints, 65536 units per turn. `Clamp` (0..65535), `Normalize` (-32768..32767), `ClampAxis`,
  `NormalizeAxis`, `Vector`, `GetAxes`; `r * f` rounds like UE2.
- `SvmMath`:
  - `Sin(int)` / `Cos(int)`: the UE2 16384-entry table, indexed `(a >> 2) & 16383` and `((a + 16384) >> 2) & 16383`.
    The table is computed with our own double-precision kernels; every entry equals `(float)Math.Sin` of the UE2
    argument (tested).
  - `Sin(float)`, `Cos`, `Tan`, `Atan`, `Atan2`, `Acos`, `Asin`, `Exp`, `Loge`, `Pow` take radians and return the
    nearest float to the C runtime result, computed without the runtime (tested within 1 ulp).
  - `Sqrt`, `InvSqrt`; `Round` (ties to even), `Trunc`, `Floor`, `Ceil` with the x87 result for overflow and NaN
    (`int.MinValue`); UE2 `Min`, `Max`, `Clamp`, `Lerp`, `Square`; constants `SMALL_NUMBER`, `KINDA_SMALL_NUMBER`.
- `SimRandom`: xoshiro128** seeded from `(matchSeed, stream, purpose)`. `FRand` in [0, 1), `Rand(Max)` (0 when
  `Max <= 0`), `RandRange`, `VRand`, `GetState` / `SetState`. `World.Random` (stream 0, purpose 0) backs
  `Actor.FRand`, `Rand`, `RandRange` and `VRand`; `World.CreateRandom(stream, purpose)` makes the others.

## Actors and events

Fields keep their UnrealScript names. `Actor.Level` is the `LevelInfo` (as in script) and `Actor.XLevel` is the
runtime `Level`. `Owner` and `Base` change through `SetOwner` and `SetBase` (they send GainedChild / LostChild and
Attach / Detach / BaseChange). `BeginTouch` / `EndTouch` keep `Touching` in step on both actors.

Every UE2 event has two members:

```csharp
protected internal virtual void Touch(Actor Other)   // class-level (global) version: override this
public void eventTouch(Actor Other)                  // dispatcher: current state first, then the global version
```

Natives and other actors always call `eventX`. In another assembly the override is written
`protected override void Touch(Actor Other)`. UnrealScript `Super.Touch(Other)` is `base.Touch(Other)`;
`Global.Touch(Other)` is `Touch(Other)`.

Events: BeginState, EndState, PreBeginPlay, BeginPlay, PostBeginPlay, SetInitialState, PostNetBeginPlay, Tick,
Timer, Touch, UnTouch, Bump, HitWall, Landed, Falling, Trigger, UnTrigger, Destroyed, BaseChange, Attach, Detach,
GainedChild, LostChild, AnimEnd.

Other members: `Spawn<T>(SpawnOwner, SpawnTag, SpawnLocation, SpawnRotation)`, `Destroy()`,
`SetTimer(Rate, bLoop)` (UE2 timer: fires once per tick at most and keeps the remainder),
`TriggerEvent` / `UntriggerEvent`, `GotoState`, `GotoLabel`, `IsInState`, `GetStateName`.
`NativeTick(DeltaSeconds)` is the per-tick native update (Tick event, state code, timer, LifeSpan, physics) and
`CleanupDestroyedReferences()` clears references to destroyed actors; subclasses override both as needed.

`Controller` links itself into `LevelInfo.ControllerList` in PreBeginPlay and unlinks in Destroyed.

## Level and World

```csharp
var world = new World(matchSeed, ENetMode.NM_DedicatedServer);
Level level = world.Level;
level.PhysicsDriver = physics;          // IActorPhysics, optional
level.BeginLoad();                      // map load: spawns join now, begin-play waits
level.SpawnActor(actor, location, rotation);
level.EndLoad();                        // PreBeginPlay, BeginPlay, PostBeginPlay, SetInitialState, PostNetBeginPlay passes
world.Step();                           // one 60 Hz tick
foreach (Pawn p in level.AllActors<Pawn>()) { }
level.TriggerEvent("Door", other, instigator);
```

- Clock: `Sim.Hz = 60`, `Sim.DeltaTime = 1f / 60`. `TickCount` is 1 during the first tick. `DeltaTime` is
  `Sim.DeltaTime * LevelInfo.TimeDilation`; `TimeSeconds` adds it in float each tick, like UE2.
- Tick order: list order (spawn order). An actor whose Owner or Base has not ticked yet waits until after the main
  pass; waiting actors then tick newest first, as UE2's linked list does. Loops are broken by ticking anyway.
  bStatic actors do not tick.
- Spawn: runs SetOwner, Spawned, PreBeginPlay, BeginPlay, PostBeginPlay, SetInitialState, PostNetBeginPlay at
  once and returns null if the actor destroyed itself. During a tick the actor joins the list and the tag index
  at the end of the tick (first tick next tick). bStatic and bNoDelete actors only spawn while loading.
  `bRemoteOwned` skips PostNetBeginPlay for replicated spawns.
- Destroy: Destroyed event, detach, end touches, drop owner, then `bDeleteMe`. The actor leaves the list at the
  end of the tick (or the start of the next one) and every actor's `CleanupDestroyedReferences` runs.
- Tags: `Tag` defaults to the class name. The index follows tag changes and keeps list order. `TriggerEvent`
  calls Trigger on live, non-static actors with that tag (DynamicActors), walking a snapshot.
- `SortActorList()` regroups like UE2 (LevelInfo, static, dynamic non-replicated, replicated).
- `World.Events` (`SimEvents`): PlaySound, SpawnEmitter and Notify for views, cleared every tick, dropped while
  `World.IsReplaying`. `World.EventSink` receives each tick's events after the step.

## States and latent code

A state is a class with one shared instance; per-actor data lives in the actor and its `LatentFrame`. A class
with states publishes a `StateTable` and returns it from `GetStateTable()`. This is the pattern a generator emits:

```csharp
public class SleeperActor : Actor
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, Idle.Instance, Busy.Instance);
    public override StateTable GetStateTable() => ClassStates;

    // auto state Idle { Begin: Counter++; Sleep(0.5); Counter += 10; Goto('Begin'); }
    public class Idle : ActorState
    {
        public static readonly Idle Instance = new(nameof(SleeperActor), nameof(Idle), null, true, ("Begin", 1));
        protected Idle(string className, string stateName, ActorState? super, bool isAuto,
            params ReadOnlySpan<(string Name, int Pc)> labels) : base(className, stateName, super, isAuto, labels) { }
        public override void BeginState(Actor self) { /* state version of BeginState */ }
        public override void Code(Actor self, ref LatentFrame frame) => ((SleeperActor)self).Idle_Code(ref frame);
    }

    private void Idle_Code(ref LatentFrame f)
    {
        switch (f.Pc)
        {
            case 1: Counter++; f.Pc = 2; Sleep(0.5f); return;     // label Begin is Pc 1
            case 2: Counter += 10; GotoLabel(NAME_Begin); return;
            default: f.Pc = LatentFrame.NoCode; return;
        }
    }

    // state Busy extends Idle { ignores Bump; event Touch(Actor Other) { ... } }
    public class Busy : Idle
    {
        public static new readonly Busy Instance = new(nameof(SleeperActor), nameof(Busy), Idle.Instance, false);
        protected Busy(/* same parameters */) : base(/* ... */) { }
        public override void Bump(Actor self, Actor Other) { }          // ignores
        public override void Touch(Actor self, Actor Other) { /* ... */ }
    }
}
```

- `state B extends A`: B derives from A and passes `A.Instance` as super. A subclass redefining a parent class's
  state derives from the parent's state class (declare it `new`) with the parent's instance as super, and lists
  it in its own `StateTable`.
- `ignores F`: override F with an empty body. BeginState and EndState are ordinary overrides.
- Functions declared by Actor subclasses: the declaring class nests `interface IFoo { R Foo(Decl self, ...); }`,
  its dispatcher is `eventFoo(...) => StateNode is IFoo s ? s.Foo(this, ...) : Foo(...)`, and states implement
  the interface (see `GuardActor` in the tests).
- Code runs in segments: the runner calls `Code` while nothing waits. A segment ends with a latent call
  (set Pc to the resume point, then `Sleep`, `FinishAnim`, `FinishInterpolation`, `WaitForLanding` or
  `SetLatentWait`), a jump (`GotoLabel`, `GotoState`) or a stop (`Pc = NoCode`). Labels need Pc 1 or more;
  label lookup walks the super states, so a state without code runs its parent's. A state change made by the
  code runs the new state's code in the same tick. 100,000 segments in one tick count as a runaway loop.
- `Sleep(s)` wakes on the tick UE2's poll would (`LatentFloat -= dt` until below `dt / 2`), stored as
  `UntilTick`. `PollLatentWait` can be overridden for MoveTo and real animations.
- `GotoState(NewState, Label)` follows UE2: EndState then BeginState on a real change, a BeginState or EndState
  that changes state again wins (`GOTOSTATE_Preempted`), same state only jumps to the label, an unknown state
  leaves the actor in no state.
- `LatentFrame` is plain data. Ids are stable hashes of `Class.State` and label names, so `RestoreStateFrame`
  works across processes.

## Still to confirm against the game

Items marked `[Provenance(Estimated, ...)]` follow stock UE2 and wait for the Ghidra and decompiler passes:
actor tick order inside `AActor::Tick`, spawn and destroy event order, map-load pass order, `SortActorList`,
`FVector::Rotation` scaling, `GetAxes` product order, `SafeNormal` and `InvSqrt`, stock Actor defaults, and the
sine table itself (to compare with a live read of the game's table).
