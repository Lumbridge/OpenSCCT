# Netcode

`OpenScct.Net` reproduces the UE2 network model that SCCT Versus uses, so that movement, corrections and
replication behave like the original under the same conditions. The original's rules are specified in
`spec/net/*.md` (script side) and `spec/native/net-*.md` (engine side, decoded from the executable); this
file says how `OpenScct.Net`, `OpenScct.Server` and `OpenScct.Server.Cli` implement them. The M0 transport, packet and
bunch layers are described in `docs/NETCODE-M0.md` and are unchanged except where noted below.

## The original

SCCT Versus netcode is mostly script-level, on top of the standard UE2 actor replication.

- **Client moves.** The client sends `MyServerMove`, `MyShortServerMove`, `MyServerMoveWithPCRot`, or
  `BigServerMove` when pawn actions wait. Big moves carry compressed actions (`CompressActions`) and are
  resent three times with their traversal payload.
- **Corrections.** The server answers with `MyShortClientAdjustPosition` or `MyClientAdjustPosition`. The
  error threshold `fErrorMax` is 1.73 uu, corrections are skipped in `PHYS_RootMotion` (5), and the client
  shifts its position and its saved moves (at most 100) instead of replaying them.
- **Full resync.** `ClientReinitPawnBecauseOfDivergences` reinitialises the client pawn when client and
  server have diverged, back to walking through `setPhysics`, which unlocks the root of a pawn
  cut out of a root-motion move (written straight into the field, the next boost climb ran on the old lock).
- **Replication.** Standard UE2: actor channels, replication conditions on `Role`, `RemoteRole`,
  `bNetInitial`, `bNetOwner` and `bNetDirty`, but every replicated class has a native property list that
  decides what is sent. RPCs carry `FUNC_Net`, `NetReliable` and `simulated`; 20 functions carry the SCCT
  flag 0x01000000 and go to every client with an open channel. One send budget per server, steered by the
  `CONF*` law; no priority sort.

## Layers

```
BotMatch, BotClient, SpyBrain, MercBrain, NavGrid             OpenScct.Bots    (headless bot players, walk graphs)
ServerHost / ClientSession / ListenServer / MapCatalog       OpenScct.Server  (hosting, handshake, maps, bridges)
GameplayServices, DedicatedStartGate                          OpenScct.Server  (the game on a host)
NetObjectiveBridge, NetGameHost, NetMovementBridge,           OpenScct.Server  (game services on the network)
  NetCombatBridge, NetDeviceEvents, PawnProxySender/Receiver
NetDriver: server pass, relevancy cadence, RPC routing        OpenScct.Net
  NetConnection (budget, lag and loss statistics), ActorChannel, ControlChannel
  NetClassRegistry -> RepLayout (RepProperty, RepFunction, RepCondition, RepLeaf)
  NativeRepLists, NetRelevancy, NetMapBinding, NetFunctionHandlers
  Moves/: ClientMoves, ServerMoves, SavedMoveList, MovePacking, NetMoveTransport
FBitWriter / FBitReader, NetWire, ITransport (Loopback, SimLink, LiteNetLib, Composite)
```

Everything in `OpenScct.Net` and `OpenScct.Server` is simulation code: deterministic, no system clock, no
`System.Random`; the driver's random stream (relevancy jitter) comes from the world's seed.

## Replication model

### Layouts from the generated data

`NetClassRegistry` registers every concrete actor class of a `UcClassIndex` (Core, Engine and SBase in
`OpenScct.Server.NetClasses`) in index order, then runtime-only classes (`Register<T>(RepExtension)`). A class's
`RepLayout` is built on first use from `UcClassInfo.Replication` of the class and every ancestor, root first,
each class's blocks in source order:

- one `RepProperty` per property element (static arrays get one per element, as UE2 sends the element index),
  with its leaves (`RepLeaf`: a scalar or one member of a struct sent member by member), its place in the
  shadow block and its condition;
- one `RepFunction` per replicated function, at its first declaration, with its flags and parameters
  (`RepParam`, struct parameters flattened into leaves);
- property and function rep indexes share one index space, properties first.

The protocol hash covers the wire version, the condition source and every class path and layout signature,
so server and client must be the same build of the generated data.

### Conditions

`RepCondition` is the interpreter: it evaluates a `RepExpr` for one object with the `RepFlags` of one
connection (`Role`, `RemoteRole` after the autonomous downgrade, `bNetInitial`, `bNetOwner`, `bNetDirty`,
`Level.NetMode`). UnrealScript semantics: `&&` and `||` short-circuit, a member of None reads as zero,
references compare by identity, enums and bytes as numbers. Property reads go through compiled accessors
(`PropertyPath`, `PropertyReaders`), so the pass does not allocate. Each distinct condition of a layout is
evaluated at most once per update.

`RepConditionSource.Native` (the default) applies `NativeRepLists`: the decoded `GetOptimizedRepList`
rules (spec/native/net-replists.md) as replacement conditions per class and property, and the classes whose
list does not call its parent's. For example `bHidden`, `Role` and `RemoteRole` go only in the first bunch
for plain actors, `Owner` only to the owning connection, `StaticMesh` on every change for gameplay objects,
`SkinState` always and `bTriggering` only while `bActive` for terminals. `RepConditionSource.Script` uses the
replication blocks as written. Rules that are not conditions (the pawn list's 0.09 s cadence and dead
reckoning, one-shot flags) are listed in `NativeRepLists` as not ported.

### Values

`NetWire` encodes values as `UStructProperty::NetSerializeItem` does: ints, floats, names and strings exact;
bytes in 8 bits; enums in as few bits as the enum needs; bools in 1 bit; vectors rounded to whole units with
an adaptive bit count (up to 20 bits per component); rotators reduced to their high byte; planes rounded to
16 bits; a compressed position as rounded location and velocity with pitch and yaw bytes. Object references
go through the connection (`INetObjectMap`): a map actor by its SVMap index, a dynamic actor by its net id
(mapped only when its channel's opening was acknowledged, otherwise None and retried), anything else as None.
Names, assets and class references go as text.

### Channels

`ActorChannel` follows spec/native/net-replication.md rules 9 to 18:

- the first bunch is reliable (unreliable and closing for `bNetTemporary` actors) and carries the spawn
  information: a map actor as its SVMap index, a dynamic actor as class, exact location and rotation;
- the shadow (`Recent`) starts as the class defaults with config properties zeroed, so values equal to the
  default are not sent in the first bunch;
- later property bunches are unreliable; when the opening is first seen acknowledged every property last
  sent unreliably is resent, and a lost packet re-dirties the properties it carried;
- an update sends the class's list (condition met, value changed) plus every dirty index whatever its
  condition says now (rule 13): a lost or unmapped `Controller.Pawn`, behind `bNetDirty`, goes again on the
  next pass even when nothing else changed (before this, a client could wait in `WaitingForPawn` for good after
  one lost packet);
- Role and RemoteRole are compared as they are and written into each other's slot (the role swap);
  `RemoteRole` reads `ROLE_SimulatedProxy` for a non-owner when the actor has an Instigator (the autonomous
  downgrade);
- a bunch that overflows takes the last value back and the rest waits.

On a client the channel binds its map actor or spawns the class with the class roles exchanged
(`ULevel::SpawnActor` with `bRemoteOwned`; OpenScct.Core's `SpawnActor` only skips `PostNetBeginPlay`, so the
exchange is done in `OpenScct.Net` before the spawn), applies the properties, runs `PostNetBeginPlay` after the
first bunch, recomputes `bNetOwner`, and makes an autonomous player controller the local player. Closing a
channel destroys a dynamic actor, not a `bNetTemporary` or torn-off one.

### The server pass

`NetDriver.TickFlush(replicate: true)` runs spec/native/net-replication.md rules 1 to 8: dirty propagation
(a replicated value changed since the last pass, or `bNetDirty` set, marks the actor's channels dirty: the
script writers of `bNetDirty` were not traced), then a pass per connection that has a player, is ready and
received a packet in the last 1.5 s. Actors go in level list order with no priority sort; a candidate is
always relevant, not owner-only, or owned directly by the viewer; the frequency gate is
`NetUpdateFrequency >= Rate || NetUpdateFrequency × dt > 1` with `Rate = GetMaxTickRate()` (20 or 35 on a
dedicated server, 0 on a listen server) and `dt` the time since the connection's last pass. The local Ghidra
output confirms the rule; on a 20 Hz dedicated server 5 and 10 Hz actors therefore update only when passes
stall on the budget, and once at the first pass of a connection. `NetUpdateGate.Stochastic` (the UE2.0
`FRand() < NetUpdateFrequency × dt`), `Always` and `DecodedGuarded` (below) are settings; `Decoded` is the
driver default. Relevancy is evaluated when a channel is
missing or more than 1 s after the last positive answer (plus 0.5 × FRand jitter); a channel irrelevant for
`RelevantTimeout` (5 s) closes, with the bone attachments of a skeletal actor.

### Update frequency on a dedicated server

The stock game has no true dedicated server: its "dedicated" host is a listen server that does not play
(spec/game/netmode-branches.md, `ServerOnly`), so `Rate` is 0 and every actor passes every pass. `ServerHost`
runs as `NM_DedicatedServer` at a steady 20 Hz, where the decoded gate alone never updates a PRI (5 Hz), the
GRI or an Info (10 Hz) or an objective trigger (5 Hz) after its first bunch: scores, lives, terminal states
and the end of the round would not reach the clients. `ServerSettings.Net` therefore defaults to
`NetUpdateGate.DecodedGuarded`: the decoded rule, plus a starvation guard that lets an actor the rule refuses
go when its channel on that connection has never sent or last sent `1 / NetUpdateFrequency` seconds ago. Each
actor then updates at its own frequency, as its class asks, and never more often than the decoded rule plus
that; with passes stalled on the budget it is the decoded rule. `svm-server --update-gate decoded` (or
`stochastic`, `always`) selects another gate for measurement; the tests
`DedicatedServerFrequencyGateHoldsFiveHertzTriggersAsDecoded` and
`DedicatedServerDefaultGateSendsFiveHertzTriggersAtTheirOwnRate` pin both behaviours.

### Relevancy

`NetRelevancy` holds `IsNetRelevantFor` per class as mapped from the game's vtables: the AActor test (always
within 2500 uu of the camera, else a BSP-only line check through `INetLineOfSight`, which `OpenScct.Server` fills
from the map's collision), the pawn test with its per-pass cache and the SBasePawn partner rule, owner-only
controllers, the flag and disk following their carrier, switches by distance, trigger lights and
interactions. The original's four-slot channel cache per actor is kept as an ordered list without the limit.

### Send budget and net speed

`SendBudgetMode.SharedAdaptive` (the game build): one budget per driver, refilled each network frame at the
rate and floored at two frames of savings; a connection is ready when the budget plus its pending packet is
not above zero, so all of a server's client connections stall together. Once per second per connection the
`CONF*` law steps the rate from the average acknowledgement lag: a client steers its own rate, a server one
shared rate from the worst lag of its clients (dedicated or listen row, LAN rows with `IsLan`), held at the
maximum for 2 s after `ResetConnectionPing` (called when a player joins). Connections over loopback (a listen
host's local players) are not counted. `PerConnection` (stock UE2) and `Unlimited` are settings.

### Function calls

`NetDriver.ProcessRemoteFunction(actor, name, args)` is `AActor::ProcessRemoteFunction`: static, standalone
and dying actors run locally; a non-simulated, non-native call on a proxy is swallowed; a net function whose
condition (evaluated once, in the actor's current flags) holds goes to the owning connection (server) or the
server (client) and does not run here; a reliable call is always queued, an unreliable one dropped when the
connection is saturated. A function with the broadcast flag 0x01000000 goes to every client connection that
already has an open channel for the actor (no channel is opened for it, so late joiners miss it), and also
runs locally on a listen server. Arguments are written in declaration order, each non-bool one after a bit
saying whether it differs from zero.

A received call is checked as the original does: on the server the condition with the roles exchanged and the
connection owning the actor (the receive path sets `bNetOwner`); on a client a proxy only runs simulated
functions. `NetFunctionHandlers` runs it: a handler registered for the class path and name, or by default the
C# method of the same name and parameter count, bound once with compiled argument conversion. A call nothing
can run goes to `NetFunctionHandlers.Fallback` when one is set (a client's `NetGameHost` delivers unported
presentation calls to its view host this way) and is counted by name in `NetDriverStats.UnhandledByName`.
`NetClassRegistry.Extend<T>(RepExtension)` appends hand-written members to an original class (same index and
path, before the protocol hash), and `NetDriver.ChannelForCall` opens and replicates a channel on the server
for a call that names an actor its connection has not seen yet.

`OpenScct.Server` connects the game ports to this:

- `NetObjectiveBridge` implements `IObjectiveNet` (the objective multicasts and owner calls).
- `NetGameHost` implements `IGameHost`: a `GameCall` becomes the replicated function it names, its fields
  filling the parameters by type; `resetConnectionPing` resets the connection's adaptive rate on the server and
  is never sent; on a client, received calls the ports do not run become `GameCall`s for the view host.
- `NetMovementBridge` implements the movement port's `IMovementNet` (below), with the server halves of the
  movement's own replicated functions (`MovementServerFunctions`: `ReplicateActionToServer` with its
  acknowledgement tolerance, `ServerGetZipP` and kin, `ServerSetWallJumpDir`, `ServerSetInfoForVandamme`,
  `UpdateDesiredRotation`).
- `NetCombatBridge` implements `ICombatNet`: every `CombatCall` travels as one reliable hand-written
  SPlayerController function (`SvmCombatCall`, `SvmCombatCallToClient`, added with `Extend`) and is handed to
  `SPlayerController.ReceiveCombatCall`, which checks that an owner's call only names what it owns. Wire form:
  name, target, then each field behind a presence bit (vectors and rotations exact, since the port carries
  non-geometric values in them). A call naming a dynamic actor the other side does not have yet waits, in
  order, until its channel is acknowledged (5 s at most); on a client a call aimed at the player's own pawn
  waits, with the calls after it, until the controller possesses that pawn. The original sends each combat
  function on its own actor, which carries the actor and its Controller first; one function per call is this
  port's form (`Provenance: Estimated`).
- `NetDeviceEvents` relays the sim events (`SimEvents`: sounds, effects, music) of map actors whose client
  copies run no script (static or no-delete actors with a remote role below simulated) as two LevelInfo
  functions, unreliable and reliable, so a door's sound or an alarm's music reaches clients that do not
  simulate the device.
- `PawnProxySender` and `PawnProxyReceiver` are the proxy pawn schedule of spec/native/net-proxy.md: per viewer
  dead reckoning on the server (a sample on the first update, on a physics change, every 0.7 s, on stopping,
  on a 20% velocity change or on a position error over 1 or 30 uu²) sent as `ClientGetPawnPosition` on the
  viewer's controller. The pawn sets `bCompressedPosition` (rule 4), so the Actor list never sends its `Location`,
  `Velocity` or `Rotation`; its own list pushes `Rotation` between samples at most every 0.09 s, sooner only for a
  turn of more than 1800 units (rule 3, through `NetDriver.PropertyPush`, per viewer). On the client each proxy's
  own tick runs `PawnProxyReceiver.Tick` in place of the stock physics (`ISimulatedTick`, ASBasePawn::TickSimulated,
  rules 10 to 15): with no error left a walking proxy walks by the received velocity (`physWalkingSimulated`) and
  any other moves by it (`ClientDistantMoveSmooth`); an error is blended out at 200 (ramping over a second) or
  400 uu/s, snapped beyond six radii or on a physics change, and the yaw turns toward the sample's (a pushed
  `Rotation` is turned back to it on the next tick). The action batches ride with the
  samples (rule 5): a new batch (the controller's tick stamps it, `ProxyAnimation.ServerUpdateActionCounts`) forces
  a sample that carries it reliably the first time (`ClientReliableGetPawnPositionAndActions`), unreliably while it
  is under 0.4 s old, and alone (`ClientGetPawnActions`) between samples for 0.15 s. The server records the actions a
  remote player sends (`ProxyAnimation.ExecuteReceivedActionsForServer`) so the other viewers get them too. On the
  client the proxy tick plays a new batch through the pawn's action code and applies the owner's zone
  configuration (crouched cylinder and mesh offset) before the smoothing (`OpenScct.Game.Animation.ProxyAnimation`,
  docs/ANIMATION.md). Every server runs the sender (the play session's host too, whose rules come from the map
  loader), and the play session's client takes the gameplay calls as an online client does.

## Move protocol

`OpenScct.Net.Moves` ports spec/native/net-moves.md and spec/net/move-protocol.md behind interfaces:
`IMovePawn` (the pawn's state, `MoveAutonomous`, `MovePawn`, the big move snap, the reinit reset),
`IMoveController` (aim, epoch, reinit events) and `IMoveTransport` (the messages).

- `ClientMoves`: `MyReplicateMove` moves the pawn locally, merges frames while reliable data is
  unacknowledged (window `0.09 × min(1, OutstandingReliable / 40)`), and `RealReplicateMove` sends the big move
  resends, records the saved move (a ring of 100), attaches the old-move pair when it fits a byte, and picks
  the message: big move when actions wait, nothing in root motion or following a parent, PCRot for Vandamme
  and fire on walls or pipes, full move with acceleration (× 10), short move every other frame standing.
  `MyReplicateMoveWithoutMoving` splits the frame at a physics change. Corrections
  (`MyLongClientAdjustPosition`, `ClientUpdatePosition`) match the saved move by exact stamp, skip root
  motion, cancel on a physics change, and shift the present position and later saved moves by the offset
  `MovePawn` actually applied. `MovePawn` sweeps by the offset and, when the sweep stops more than 50 uu short,
  teleports with a checked `FarMoveActor` (FindSpot and the encroachment test, editor 0x111fe4a0): a target inside
  a wall or another pawn is refused and the pawn stays where the sweep stopped.
- `ServerMoves`: `ProcessServerMove` with the old move, `min(MaxResponseTime, stamp − CurrentTimeStamp)`, the
  time-margin guard, the View aim, the physics check (3 s of mismatch raises the reinit), the speed cap, the
  simulation through `MoveAutonomous`, the zone configuration offset, the correction decision (forced after
  1 s, not before `180 / NetSpeed` s or 0.3 s, threshold 3 uu²), the short or full correction, and the reinit
  above 400 uu. Big moves check the epoch within 1 s, execute the actions, snap within 100 uu, apply the
  traversal payload of a resend. PCRot runs with Reloaded's change (MaxTimeMargin infinite during the call).
- `NetMoveTransport` sends the messages as SPlayerController's replicated functions (so the generated
  conditions, reliability and encoding apply: vectors rounded, corrections as exact floats) and routes the
  received ones to the controller's protocol objects.
- `NetMovementBridge` (OpenScct.Server) is the movement port's `IMovementNet`: on a client
  `SPlayerController.MyReplicateMove` runs `ClientMoves` for the autonomous controller, with `SBasePawnMoves`
  and `SPlayerControllerMoves` adapting the generated SBasePawn and SPlayerController fields; on the server the
  received moves run `ServerMoves`. The controller's reinit states are stand-ins until the controller port has
  them: the server leaves a grab or a knock-down alone (state-sync.md rule 12 case 2), the client answers
  `TellServerIAMReadyToReinit` at once, and on that answer the server mirrors `s_PlayerWalkingCr0H` (spy) or
  `s_PlayerWalkingCr2H` (merc) under the new epoch (rule 15) and asks for `GotoWaitingState`, which the client's
  `s_ClientIsReinit.EndState` would run (rule 14), so the client picks its idle state and the server follows.

## Maps and hosting

Online sessions (lobby, map travel, results, the master, NAT punch and relay) sit on top of this; see
`docs/ONLINE.md`.

- `MapCatalog` loads SVMap folders (`maps` next to the executable, `.content/svmap` and `fixtures` in a
  checkout, or maps built in code) through `MapLoader` with every generated Core, Engine and SBase class and the
  ported device classes, attaches physics with `PhysicsWorld.Attach`, and takes the player starts from the
  map. The content hash is the first 64 bits of the manifest's sim hash. Without a TestBox folder the M0 stub
  with four starts is used.
- A client load runs UE2's client map setup before begin play: bStatic and bNoDelete map actors exchange their
  roles, other map actors are destroyed. Server and client bind the map actors by SVMap index.
- Handshake: Hello carries the protocol hash (and optionally a content hash); Welcome carries the map, its
  content hash and its sim file digests; a client whose copy differs leaves with "Your game's version is not
  the same as the server's" and names the differing file.
- `ServerHost` ticks the world at 60 Hz and runs a network frame at `NetRate`; it installs the bridges, spawns
  an M0 `CubeWalker` per player unless told not to, and raises `PlayerJoined` so a game can give the connection
  its player controller. `ListenServer`, ports and `svm-server` are as in M0.

## Game sessions

With a game configured (the default when a map is given) `ServerHost` hosts the game itself:

- the map loads with `SGameInfo` spawned before begin play (`?MapName=...` plus `ServerSettings.GameOptions`,
  for example `?GameMode=5?TimeLimit=10?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3` for Story), then
  `GameplayServices` installs the device natives, the movement service and game-flow ports, and the combat and
  equipment layer;
- a joining player logs in through `SGameInfo.SpawnPlayActor` with its Hello name and options (`?Team=0`), the
  controller becomes the connection's actor, and leaving logs out and destroys pawn and controller;
- `DedicatedStartGate` is the host's half of the start gate (spec/game/match-lifecycle.md): every second, once
  no connection is still joining and at least `PlayersToStart` PRIs wait to play, it clears `bDelayedStart`,
  sets `bMatchHasBegun` and starts the match a second later;
- `ClientSession` with `Gameplay` installs the same services on the client world, tells the server it waits
  to play once its controller exists, and raises `BeforeWorldTick` (the bots' input) before each world step.

The roles gate what runs (spec/native/net-replication.md rule 26, in `Actor.NativeTick`): a dumb proxy or
ROLE_None actor gets no script tick, state code, timers or LifeSpan, only falling or rotating physics; an
autonomous proxy runs no physics of its own (its moves do); on the server a remote player's autonomous actor
runs its script but no physics, and when no move arrived for more than half of `MaxResponseTime` its physics
catches up once for the elapsed time and the controller's `ServerTimeStamp` becomes now. `Pawn.PreBeginPlay`
makes a pawn its own Instigator, which the autonomous downgrade relies on.

An SBasePawn replaces that catch-up with its own (`ASBasePawn::Tick`, spec/native/net-moves.md rule 18,
`SBasePawn.CatchUpRemoteAutonomous`): in root motion, following a parent, or in a repositioning the server
controls, the server runs the pawn's physics every tick, since the client sends no moves then; a falling pawn whose
moves stopped for 0.1 s falls on for the elapsed time and that time counts as processed (`CurrentTimeStamp`, which
`ServerMoves` shares with the controller); other modes wait for their moves.

## Traversal over the network

Ledges, pipes, ladders, fences, hand over hand, vaults and zip lines are played by the client's controller states;
the server follows through the move protocol: the actions of each step arrive in a big move or in
`ReplicateActionToServer` and run through `ExecuteAction` on the server's pawn (so an action's physics change, such
as the jump's `PHYS_Falling`, is what puts the server's pawn in the new mode), the traversal points arrive with
`ServerGetZipP` and kin or the big move resends, and the moves of the new mode are simulated by the server's own
physics. Where the client sends no moves (root motion: the ledge mount, a vault, the ends of ladders and pipes) the
pawn's server tick above runs it.

The client proposes the traversal points, and vectors arrive rounded to whole units, so the server's copy of a
ledge or zip line would sit up to 0.87 uu off the client's. `GEPointCheck` (an addition, the original takes the
rounded points as they come) takes a proposed point within rounding of a point of one of the map's GEs as that
exact point.

`tests/OpenScct.Integration.Tests/RubberBandTraversalTests.cs` plays the recorded traversal and moveset scenarios of
tests/scenarios as the local player of an offline match (`PlaySession`: listen server and client, the inputs as
`PlayInput` fills them from the bound keys, `PlaySettings.SimLink` for a simulated link), with the pawn placed by the
server and the client reinitialised there. Before the fixes of this section 12 of its 30 cases failed: the mount
(root motion never ran on the server, so the physics check reinitialised the client under the ledge 3 s later, the
"snap back"), both zip lines (the server's pawn never left walking on the jump, ignored every move of the jump and
the grab, and was snapped or reinitialised: the rubber band, the fall clip and the teleport), the ladders and the
wall jump. Now, at 0 ms and on SimLink C:

| Scenario | Corrections applied (0 ms) | On SimLink C | Reinits |
|---|---|---|---|
| ledge pull-up | 1, at the walk to the wall | 1 | 0 |
| zip line 29, ride and drop to the deck | 1 to 2, none during the ride | 1 at the walk, 2 under 2 uu during the grab | 0 |
| zip line 13, ride to the wall | 1 at the walk, none during the ride, 2 to 3 of up to 9 uu at the wall | the same, plus 2 under 2 uu during the grab | 0 |
| ledge grab, drop, shimmy, walk-off catch | 1 to 2, up to 7 uu after a drop | the same | 0 |
| pipe up and down, ladder up and down | 1 to 3, under 9 uu, at the start of a climb | the same | 0 |
| fence, hand over hand, vault, wall jump | 1 to 4, under 9 uu | up to 28 uu at a landing after a ledge drop | 0 |

The corrections left come from the decoded protocol, not from a disagreement of the two simulations:

- a player who does not move sends a move every other frame (rule 3.5 of net-moves.md, the process-wide toggle,
  confirmed in `RealReplicateMove` 0x111f2a80), so the first move after standing still covers two frames on the
  server and the server's pawn starts one frame ahead (7 uu at running speed once it is up to speed). The forced
  correction a second later fixes it. The same happens at the start of a pipe, ladder or hand over hand climb;
- a state change that sends no move in its frame (the drop from a ledge returns before `SendMove`) adds that frame
  to the next move as well, so the server's pawn falls two or three frames ahead; the correction grows with the
  fall speed (on SimLink C up to 28 uu at the landing);
- moves without acceleration every other frame also make the server integrate non-linear motion (the ease onto a
  zip line, the stop at a zip line's wall) in steps of two frames; the differences stay under 3 uu,
  except at a zip line's wall, where the server's `GroundSpeed` comes from the zero acceleration of those moves
  (rule 11.6 gives 0) while the client keeps its riding speed, so the two stop differently (up to 9 uu).

### Corrections in ordinary play

The server sends a correction at least once a second to a client that sends moves, moving or not (rule 12 of
net-moves.md: more than 1.0 s since `LastUpdateTime` forces one with the error taken as 10000), so a player sees
about one `ClientAdjustPosition` a second in any match; the client applies one only when the server's location
differs from its saved move by more than `fErrorMax` (1.73 uu), otherwise it only drops the older saved moves. A
count of corrections sent therefore measures the forced update, not disagreement: only the applied ones (and their
size) say whether the player was pulled. In the play-inspect smoke's 46 s script the server sends about 56, of
which the client applies 4 or 5: the starts from standing (a frame of motion, rule 3.5) and a jump.

`tests/OpenScct.Integration.Tests/PlayStepCorrectionTests.cs` plays that script's walking part (walk, look, run, strafe,
crouch walk, jumps) on SteeE from the spawn as the local player of an offline match, spy and merc, and requires no
reinit and no applied correction over 16 uu. The spy's script ends walking and jumping along the wall at x = -1664,
and the server's copy stands on the big moves' rounded location exactly on the wall's plane. There the box sweeps
treated the zero-overlap contact as a collision (docs/PHYSICS.md, `K.BoxContactSkin`): the server's spy lost its air
control, met the slope behind the wall in the jump and fell while the client rose, then waited on the floor for the
client's landing, which a 110 uu correction pulled down from the top of its jump; afterwards the server's walking
probe kept the copy stuck while the client walked on (10 uu corrections every 0.3 s). With the skin the spy applies
8 corrections of at most 8.8 uu in 36.6 s and the merc 6 of at most 4.3 uu.

## Bot matches

`OpenScct.Bots` plays matches with headless bot clients. Each `BotClient` is a `ClientSession` with gameplay on,
whose brain writes the controller's input bytes, view and aim before every client tick, as PawnRig scenarios
do; nothing is read from the server's world. Walk graphs (`NavGrid`, cached per map and team in `NavCache`)
are built at run time from the map's collision: floor probes every 32 uu, a stair-tolerant body sweep for each
edge, gaps up to three cells, drops up to 700 uu for spies and 400 uu for mercs, then A* with paths pulled
straight. The walker passes a waypoint early only when the next is in a clear straight line, measures progress
as the remaining path getting shorter (so a pawn its server keeps pulling back counts as stuck), plans around
places it cannot pass, keeps right past a pawn ahead on its floor when there is room, and gives way when another
pawn blocks it: it backs away from the pawn for a second, waits 0.3 to 1.8 s (drawn per bot) and plans again from
there, a merc at the first such spell and a spy at the second, so walkers meeting in a one-lane doorway take
turns (after three spells in a row without progress the place is avoided instead). A merc's view follows his aim
every frame, so the bots turn it with `SetViewRotation`, which turns both.

| Script | Spies | Mercs | Expected end |
|---|---|---|---|
| `spies` | `SpyBrain`: walk crouched to a terminal's use spot, hold Use until the menu opens, let the terminal states hack, then the next (each takes every other terminal by distance, then what is left) | patrol guard posts beside the terminals, holding fire | END_STORY, team 0 |
| `mercs` | `SpyRaidBrain`: walk to points 350 to 550 uu from the merc starts and stand there 6 s | stay at the start and shoot on sight (line of sight over the map's geometry, no teammate in the line; walking targets only within 600 uu, dead ones never) | END_TEAMDEATH, team 1 |
| `timeout` | stay home | stay home | END_TIMEOUT, team 1 |

`BotMatch` runs a server and four bots over any transport and reports per connection: bytes per second each
way, moves applied, corrections sent and applied, reinits, the client to server error of a resting pawn (p95
and maximum, sampled while the round runs and neither copy touches another pawn; the p95 samples once a second
from the second second of a rest, when the forced correction and its round trip have had time to land), the end each client saw and whether its GRI and PRIs (lives,
deaths, score) match the server's. `svm-server --map SteeE --bots spies` (or `mercs`, `timeout`) plays one over
UDP on localhost in real time.

Results (Debug build, `tests/OpenScct.Integration.Tests/BotMatchTests.cs`; bytes include the per-packet overhead;
the walk graphs, and so the routes and round times, depend on which match of a process builds them first):

| Map | Link | Script | End | Round | Down per client | Up per client |
|---|---|---|---|---|---|---|
| TestBox | SimLink A, B, C | spies | END_STORY | 26.4 to 26.8 s | 1.6 to 1.8 KB/s | 2.7 to 4.3 KB/s |
| TestBox | SimLink A, B, C | mercs | END_TEAMDEATH | 85.0 to 95.8 s | 1.3 KB/s | 2.1 to 3.8 KB/s |
| TestBox | SimLink A, B, C | timeout | END_TIMEOUT | 604.9 s | 1.1 KB/s | 2.3 to 3.8 KB/s |
| TestBox | UDP localhost, real time | spies | END_STORY | 26.8 to 33.4 s | 1.6 to 1.8 KB/s | 2.6 to 4.3 KB/s |
| SteeE | SimLink A, B, C | spies | END_STORY | 113.6 to 233.8 s | 1.9 to 2.3 KB/s | 2.5 to 5.0 KB/s |
| SteeE | SimLink B | mercs | END_TEAMDEATH | 241.1 s | 1.3 to 1.5 KB/s | 2.0 to 3.8 KB/s |
| SteeE | SimLink C | timeout | END_TIMEOUT | 604.8 s | 1.1 to 1.3 KB/s | 2.5 to 3.8 KB/s |
| SteeE | UDP localhost, real time | spies | END_STORY | 131.2 to 232.8 s (the latter with `svm-server --bots`) | 1.8 to 2.2 KB/s | 2.5 to 4.8 KB/s |

Every client saw the server's end and held the same results, resting live pawns agreed within 1.3 uu at the
95th percentile, and no reinit was needed except one on SteeE's eliminations match (open items). Seed sweeps
on TestBox (48 spies and 24 mercs matches over A, B and C) all ended as scripted, without a reinit. The forced
correction (once a second while moving) makes most of the corrections sent; between 0 and 30 per player were
applied in a TestBox match and up to 62 in a SteeE one.

Terminal collision: a walker used to stop 4 uu short of the use spot because gameplay objects collided with both
collision sets of their mesh; the terminal's per-poly triangles reach 33.2 uu out, its simple boxes 28.2, the spot is
64 uu out and a pawn is 35 uu wide. Gameplay objects, movers and glass now pair their sets through the map's CSRC
chunk, so traces and moves use the simple set as `UStaticMesh::LineCheck` does, and terminals block players again
(`BotMatchSettings.TerminalsBlockPlayers` false remains for tests).

## Checks

- `tests/OpenScct.Net.Tests`: condition truth tables against the spec formulas (tree, bound and parsed forms
  agree; every generated condition binds), map binding, role swap, native and script conditions, ownership,
  relevancy and its timeout, temporaries, broadcast and owner calls (with six clients), the frequency gate and
  its starvation guard, the tick cap, the resend of a lost property behind `bNetDirty`, `Extend`, the call
  fallback, `ChannelForCall`, the role tick gates and the remote autonomous catch-up, the pawn Instigator, the
  move packing and the client and server move halves over an in-memory link.
- `tests/OpenScct.Integration.Tests`: the M0 walker checks over loopback and SimLink A to D, the SteeE-shaped
  TestBox test (a server and three clients replicate objective state under the native lists and a broadcast
  arrives once, not at a late joiner), the dedicated server frequency gate, the move protocol between real
  drivers with SPlayerController under SimLink B and C, bandwidth, allocations, handshake and UDP sessions.
  With a locally converted SteeE in `.content/svmap` (skipped otherwise), a listen server and three clients
  under SimLink B and C bind every map actor, open all objective actors, replicate a terminal take-over and a
  radar broadcast, and stay near 3 KB/s per client with about 230 open channels. `BotMatchTests` plays the
  Story matches above: the TestBox matrix (three links, three scripts), the TestBox spy win on SimLink C with frames
  stalled as on a loaded machine (2% of frames late by 20 to 250 ms, then run back to back) and the UDP match by default, SteeE's
  spy win on SimLink C by default, and its other matches (spies on A and B, mercs on B, timeout on C, and the
  UDP match on SteeE) with `SVM_BOT_MATCHES=1`. `RubberBandTraversalTests` plays the traversal scenarios as the
  local player at 0 ms and on SimLink C (Traversal over the network).
- `StuckPawnMatrixTests` plays the stalled TestBox spy win at 5% stalls and fails on a stuck pawn: a copy in
  `PHYS_Falling` over 2.5 s (a fall out of the world is listed apart), a knockdown state over 12 s, or a server copy
  still knocked down 3 s after its owner got up, or a controller in the use menu over 15 s; rest errors, reinits,
  falls out of the world, closed connections and rounds that do not end go to the output. By default it plays seeds
  2016 and 2103; `SVM_STALL_SEEDS=2001-2150` runs the 150 seed matrix (it shows the open use-menu case). `StuckKnockdownTests` drop-attacks a
  spy (as the falling physics' trigger does) in the timeout and spy-win matches and checks that both copies get up.

### Stalled-frame bot matches (2026-10-25)

The 150 seed matrix at 5% stalls (TestBox spy win, SimLink C) before the fixes: 2 seeds with a client hanging in
`PHYS_Falling` in a wall for 4 s, 42 with a rest error (sampled from the first second of a rest), 3 with a reinit,
2 rounds not ended, 2 with a spy held in the use menu for the rest of the match; a spy knocked down by a drop attack
in the spy-win match stayed in `s_StandUp` on the server in 14 of 100 seeds. The causes:

- `MovePawn` teleported unchecked (`bNoCheck`), so a correction whose sweep stopped at a door jamb or at another
  pawn put the client into the wall: 22 uu into room A's south wall by the door to room C (seed 2016), where the
  walking floor probe met the wall's triangles at time 0 and lifted him 1.4 uu a frame until he fell and hung in
  `PHYS_Falling`, or 0.47 uu into a corridor wall (seed 2041), where the walking pre-probe's side box held him
  against it. Meanwhile the server's copy, falling on the floor until the client's landing arrives (as decoded),
  built up its fall speed, so his eventual landing could be a drop attack on a spy walking by.
- The reinit stand-in reinitialised a pawn the server still had in `s_StandUp` (its owner already walking) without
  mirroring a state: the new epoch made the server refuse every later state change of the owner.
- The touch pass of MoveActor took a box round the other actor's cylinder for the swept test and the cylinder for
  the end-of-move overlap, so a pawn walking past a terminal's interaction cylinder through the box's corners touched
  and untouched it on every move, each pair two reliable calls to its client (`AddObjetOnClient`,
  `RemoveInteractionObjOnClient`); after a stalled frame that overflowed the connection's reliable buffer and the
  server dropped the player (seed 1034 at 2% once the teleport was checked). The swept test now uses the cylinder.

After them: no pawn falling or knocked down for too long in the 150 seeds, 0 of 100 in the knockdown scan, but 4
seeds with a spy held in the use menu (open items); 4 seeds with a rest error (sampled from the second second; most
of the drop from 42 is the sampling, the rests that remain last 2 to 8 s), 20 with a reinit (a checked teleport that
is refused leaves the client behind until the divergence check reinitialises it, as in the original), 2 with a pawn
fallen out of the world and 4 rounds not ended (the use menu). At 2% stalls the 150 seeds show 2 reinits and no rest
error.

## Open items

- Proxy pawns: the aim properties of rule 3 (`iDesiredYawPitch`, `iYawPitchFromView`), the aim smoothing (rule 15)
  and the epoch reset of a forced reinit (rule 7) are not ported. Standing bots that turn all the time make
  their proxies' yaw flick between the pushed `Rotation` and the sample's (the decoded rules 3 and 15; measured
  1.9 x the pawn's yaw travel on SteeE, 3.0 x before the port); human players turn far less while standing.
- The corrections the decoded protocol makes in traversal (Traversal over the network) are kept as decoded; the
  original's own correction counts in traversal were not measured.
- `GEPointCheck` is an addition; points that are not GE points (none found in the scenarios) stay rounded.
- Broadcast functions the ports call directly (`ClientShake`, `ClientNotifyNoise`, `ClientPlayLandingSound`
  and others) run where they are called; only the sim events of map actors that run no script on clients are
  relayed (`NetDeviceEvents`). Each needs its replicated path when its port lands.
- Terminal use spots: fixed by the simple-set pairing of gameplay object meshes (above); to confirm in play.
- A server pawn wedged between level geometry and another pawn can stay stuck while its client slides free;
  the corrections then pull the client back about three times a second until the pawns part.
- A pawn resting against another pawn, its client and server copies touching that pawn's cylinder at different
  points, cannot be corrected: the correction is a chord into the cylinder, and MovePawn's single sweep (net-moves.md
  rule 16) is refused by the rule for a start inside a cylinder moving inward (collision-primitives.md), as decoded
  for the original. The copies stay apart (4.65 uu for a spy crouched against a merc at a TestBox terminal, SimLink
  B seed 72) until one of the pawns moves, so the bot matches leave such rests out of the rest error.
- On SteeE a spy hit while walking off a roof edge can have its server copy held at the lip in `PHYS_Falling`
  without moving while the client falls; 2 s later the error passes 400 uu and the server reinitialises the
  pawn (the divergence check working as designed). The eliminations script tolerates two reinits per client for
  this; the physics owners should look at falling from a ledge lip (not reproduced on TestBox: the TestBox
  doorway case was the unchecked teleport above).
- TestBox's world is open outside its rooms, and the box point check FindSpot uses sees only triangles, not the
  BSP's solid leaves (no leaf hulls are converted; TestBox has no BSP at all). So a checked teleport whose target lies
  outside a wall is accepted and the client falls out of the world (2 of the 150 stalled seeds); on a converted map
  the same happens for a target deep inside a thick wall. The original's FindSpot fails there and the pawn stays.
- After a stalled frame a saturated connection drops unreliable calls (`IsNetReady`), corrections among them, so a
  client that stops can rest up to about 1.5 s off its server copy before a correction lands (TestBox seed 1034 at 2%
  stalls: 68 uu for 1 s). The rest error is sampled from the second second of a rest for that.
- A spy bot can stay in the use menu at room C's terminal for the rest of a stalled match (4 of the 150 seeds at 5%:
  2006, 2042, 2064, 2068; 2 before the stalled-match fixes): its copies rest about 10 uu apart when it presses Use,
  the corrections sent while it is in `s_MenuInteraction` are not applied (the client's correction is applied from
  MyReplicateMove), no hack starts, and `SpyBrain` never leaves the menu (it walks back and asks again, which the
  menu ignores). Needs the bot to close the menu, and a look at corrections in the menu state.
- The forced correction fires about once a second while a client sends moves, as decoded; whether the original
  keeps that cadence in play is not measured. Play checks should count the corrections the client applies, not the
  ones sent (Corrections in ordinary play).
- `PostNetReceive` side effects other than Owner and Base (collision hash, `SetStaticMesh`, mover
  interpolation restart) are not applied yet.
- Datagram framing, packet ids and per-connection compression differ from the game (wire compatibility is not
  a goal); the statistics period and per-packet overhead are estimated.
