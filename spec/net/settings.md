# Network settings and rates

Units: eng:IpDrv.TcpNetDriver.config, eng:Engine.DemoRecDriver.config, eng:Engine.Player.config, eng:UNetDriver::TickFlush, eng:UNetConnection::FlushNet, fmt:ini.Default.URL, def:Engine.PlayerController, def:SBase.SPlayerController, def:Engine.ReplicationInfo, def:Engine.PlayerReplicationInfo, def:Engine.GameReplicationInfo, def:Engine.TeamInfo, def:SBase.STeamInfo, def:Engine.Pawn, def:SBase.SPawnAttaque_1Mesh, def:SBase.SPawnDefense_1Mesh, def:Engine.Mover, def:SBase.SObjectiveTrigger, def:SBase.SComputerObjectiveTrigger, def:SBase.SFlagDropZone

## Purpose

The numbers that set how often the original server ticks and sends, how much it may send, when it
gives up on a connection, and the script-level send cadences layered on top. The rewrite reads the
same values from its own server settings (defaults below), so a faithful profile reproduces the
original timing.

## Data

`System/Default.ini` of the reference install (Enhanced 4.0 Public Beta 3), section
`[IpDrv.TcpNetDriver]` unless noted:

| Key | Value | Unit | Meaning |
|---|---|---|---|
| `NetServerMaxTickRate` | 20 | Hz | Dedicated server tick cap on the internet. |
| `LanServerMaxTickRate` | 35 | Hz | Dedicated server tick cap on LAN. |
| `MaxClientRate` | 20000 | bytes/s | Upper bound of a connection's send budget. |
| `ConnectionTimeout` | 15.0 | s | Drop a connection after this long without packets. |
| `InitialConnectTimeout` | 15.0 | s | Same, before the handshake completes. |
| `AckTimeout` | 1.0 | s | Resend delay for unacknowledged reliable data. |
| `KeepAliveTime` | 0.2 | s | Send an empty packet when nothing else went out for this long. |
| `RelevantTimeout` | 5.0 | s | Close a channel after its actor stayed not relevant this long. |
| `SpawnPrioritySeconds` | 1.0 | s | Stock priority boost window; not read by this build's server pass. |
| `ServerTravelPause` | 4.0 | s | Delay before a server travel. |
| `AllowDownloads` | True | | Package download allowed. |
| `DownloadManagers` | `IpDrv.HTTPDownload`, `Engine.ChannelDownload` | | |
| `AllowPlayerPortUnreach`, `LogPortUnreach` | False | | |
| `DefaultPktLag`, `DefaultPktLoss` | 0 | ms, % | Built-in network simulation, off. |
| `maxAcceptableUnAckBunch` | 40 | bunches | SCCT key: scales the client's move batching window, `0.09 x min(1, unacked reliable bunches / 40)` (not a channel cap). |
| `CONFClientNetSpeedStepUp` | 20 | | SCCT adaptive net speed, client. |
| `CONFClientInternetMinNetSpeed` / `MaxNetSpeed` | 5000 / 8000 | bytes/s | |
| `CONFClientLanMinNetSpeed` / `MaxNetSpeed` | 10000 / 11000 | bytes/s | |
| `CONFClientLagToTakeAction` / `MaxAllowedLag` | 0.250 / 0.400 | s | |
| `CONFListenServerNetSpeedStepUp` | 50 | | Listen server. |
| `CONFListenServerInternetMinNetSpeed` / `MaxNetSpeed` | 7000 / 9000 | bytes/s | |
| `CONFListenServerLanMinNetSpeed` / `MaxNetSpeed` | 15000 / 40000 | bytes/s | |
| `CONFListenServerLagToTakeAction` / `MaxAllowedLag` | 0.200 / 0.350 | s | |
| `CONFDedicatedServerNetSpeedStepUp` | 50 | | Dedicated server. |
| `CONFDedicatedServerInternetMinNetSpeed` / `MaxNetSpeed` | 8000 / 15000 | bytes/s | |
| `CONFDedicatedServerLanMinNetSpeed` / `MaxNetSpeed` | 15000 / 40000 | bytes/s | |
| `CONFDedicatedServerLagToTakeAction` / `MaxAllowedLag` | 0.150 / 0.300 | s | |

Other sections: `[Engine.Player] ConfiguredInternetSpeed=10000`, `ConfiguredLanSpeed=20000`;
`[URL] Port=8888`, `Protocol=shadowstrike`, `Class=SBase.SBasePawn`; `[WinDrv.WindowsClient]
MinDesiredFrameRate=35.000000`; `[Core.System] UseAlternateTimer=False`;
`[Engine.DemoRecDriver]` (demo recording only): `MaxClientRate=25000`, `KeepAliveTime=1.0`,
`NetServerMaxTickRate=60`, `LanServerMaxTickRate=60`, other timeouts as above.

Per-class network defaults that matter for the slice (`def:` values):

| Class | RemoteRole | NetUpdateFrequency | NetPriority | Relevance |
|---|---|---|---|---|
| `SPlayerController` | AutonomousProxy | 100 | 3.0 | owner only (native) |
| `SPawnAttaque_1Mesh`, `SPawnDefense_1Mesh` | SimulatedProxy | 100 | 2.0 (3.0 once possessed) | `bAlwaysRelevant` |
| `PlayerReplicationInfo` | SimulatedProxy | 5 | 1.0 | `bAlwaysRelevant` |
| `GameReplicationInfo` | SimulatedProxy | 10 | 1.0 | `bAlwaysRelevant`, `bNetNotify` |
| `STeamInfo` | DumbProxy | 10 | 1.0 | `bAlwaysRelevant`, not only-dirty |
| `Mover` | SimulatedProxy | 100 | 2.7 | `bAlwaysRelevant` |
| `SObjectiveTrigger`, `SComputerObjectiveTrigger`, `SFlagDropZone` | SimulatedProxy | 5 | 1.4 | `bAlwaysRelevant` |
| `SObjective`, `SMission` | DumbProxy | 100 | 1.0 | `bAlwaysRelevant` |
| `SFlag`, `SDisk` | SimulatedProxy | 100 | 1.4 | native test |
| `SGamePlayObject`, `SVitre`, `SInterrupteur` | SimulatedProxy | 100 | 1.0 | native or visibility test |
| `Inventory`, `SGadget` | SimulatedProxy | 100 | 1.4 | owner only |
| `SWeaponAttaque`, `SWeaponDefense`, `SGadTorche` | SimulatedProxy | 100 | 1.4 | not owner only |
| `SProjGrenade` family | SimulatedProxy | 100 | 2.5 | `bNetTemporary`, lifespan 14 s |

## Rules

1. **Server tick.** A dedicated server runs its world tick at most `NetServerMaxTickRate` (20 Hz), or
   `LanServerMaxTickRate` (35 Hz) only when it was started with `-lanplay`, the value clamped to [10, 120]
   (decoded, `UGameEngine::GetMaxTickRate` game 0x109ecb10), and flushes network output once per tick. The same
   value gates replication by `NetUpdateFrequency`. Each server move processed from a client advances that player's pawn by the client's
   own delta time (see `move-protocol.md`), so the server tick rate limits how often corrections and
   proxy updates go out, not the simulation of player movement.
2. **Listen server.** `GetMaxTickRate` returns 0 (no cap) on a listen server and on clients, so a listen server
   ticks and flushes with the host's frame (Reloaded caps the host frame rate, see `spec/reloaded/FEATURES.md`
   `rl:net.rates`).
3. **Send budget (decoded, game 0x109eeff0, 0x10a0d690).** The game's server has one send budget for all client
   connections, not one per connection: its rate is the adaptive driver speed below, refilled each frame, with
   two frames of carry-over. `MaxClientRate` and the per-connection `NETSPEED` only feed the correction rate
   limit (`move-protocol.md`). The client's own net speed starts at `ConfiguredInternetSpeed` (10000) or
   `ConfiguredLanSpeed` (20000).
3a. **Adaptive net speed (decoded, game 0x10a0eac0, 0x10a0e570; net-driver.md rule 8).** Once per second per
   connection, from the average acknowledgement lag `L`: `L > MaxAllowedLag` sets the speed to `Min`;
   `LagToTakeAction < L <= MaxAllowedLag` sets it linearly between `Max` and `Min`; `L <= LagToTakeAction` adds
   `StepUp` up to `Max`; for 2 s after a reset it is forced to `Max`. The client adapts its own rate; the server
   adapts one shared rate for all clients from the worst lag (dedicated or listen row), stepping by
   `StepUp / clients`. The LAN rows apply after `EnterLAN`.
4. **Timeouts.** A connection with no incoming packet for 15.0 s is closed (same before the handshake);
   unacknowledged reliable bunches are resent after 1.0 s; an idle connection sends a keep-alive every
   0.2 s; a channel for an actor not relevant for 5.0 s is closed.
5. **Script cadences** (all decoded from SBase script, client side unless stated):
   - Server moves: through `MyReplicateMove` (native); frames merge only while reliable data is
     unacknowledged, within a window of `0.09 x min(1, unacked reliable bunches / maxAcceptableUnAckBunch)`
     seconds (`move-protocol.md`).
   - `UpdatePositionForServer(Location, Rotation)` (unreliable): when more than 0.14 s passed since the
     last one, from the global `PlayerTick`; every frame in the spectator state that calls it from its
     own `PlayerTick`.
   - `UpdateDesiredRotation(MyDesiredRotation)` (unreliable): every frame for mouse aim
     (`UpdateRotationAttaquant_Mouse`, also used by the merc); for the pad camera of the merc, when the
     rotation changed or at least 0.2 s passed (`UpdateRotationDefenseur`).
   - Mirror state acknowledgement time is refreshed when more than 1.0 s old (`state-sync.md`).
   - `s_ClientIsReinit` repeats `TellServerIAMReadyToReinit` every 1.0 s.
   - Mover position check: server refreshes `RealPosition` every 1.0 s, client checks it every 4.0 s
     (`rep-map-actors.md`).
6. **Actor update rate** is capped by each class's `NetUpdateFrequency` (table above): players' pawns and
   controllers can be sent every server tick, PRIs at 5 Hz, GRI and teams at 10 Hz, objective triggers
   at 5 Hz. The server pass has no priority order: actors are served in list order (`SpawnPrioritySeconds` and
   `NetPriority` are not read by it; game 0x10a30730).
6a. **Move time checks.** Every server move clamps its delta to `MaxResponseTime` (0.7 s); the time-margin guard
   uses `MaxTimeMargin` (0.35, raised to 0.5 after a trip); the server simulates a remote pawn itself when no move
   came for `0.5 x MaxResponseTime` (decoded, game 0x10aac4a0, editor 0x11183b50; `move-protocol.md`).
7. **Ports.** The original listens on UDP 8888 by default (`[URL] Port`); Reloaded adds a query port
   7776 (`rl:net.ports`). The rewrite uses its own ports (plan: 28888 game, 28889 LAN, 28880 introducer)
   and never the original's.

## States

None.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| NetServerMaxTickRate | 20 | Hz | decoded | `Default.ini` |
| LanServerMaxTickRate | 35 | Hz | decoded | `Default.ini` |
| MaxClientRate | 20000 | bytes/s | decoded | `Default.ini` |
| ConnectionTimeout | 15.0 | s | decoded | `Default.ini` |
| InitialConnectTimeout | 15.0 | s | decoded | `Default.ini` |
| AckTimeout | 1.0 | s | decoded | `Default.ini` |
| KeepAliveTime | 0.2 | s | decoded | `Default.ini` |
| RelevantTimeout | 5.0 | s | decoded | `Default.ini` |
| SpawnPrioritySeconds | 1.0 | s | decoded | `Default.ini` (not read by the server pass) |
| ServerTravelPause | 4.0 | s | decoded | `Default.ini` |
| maxAcceptableUnAckBunch | 40 | bunches | decoded | `Default.ini`; scales the move batching window (game 0x10aad5c0) |
| ConfiguredInternetSpeed | 10000 | bytes/s | decoded | `Default.ini` [Engine.Player] |
| ConfiguredLanSpeed | 20000 | bytes/s | decoded | `Default.ini` [Engine.Player] |
| Game port | 8888 | UDP | decoded | `Default.ini` [URL] |
| Camera position send interval | 0.14 | s | decoded | uc:SBase.SPlayerController:PlayerTick |
| Merc pad rotation resend interval | 0.2 | s | decoded | uc:SBase.SPlayerController:UpdateRotationDefenseur |
| MaxResponseTime | 0.7 | s | decoded | def:SBase.SPlayerController; clamps every server move delta (game 0x10aac4a0) |
| MaxTimeMargin | 0.35 (0.5 after a trip) | s | decoded | def:SBase.SPlayerController; the time-margin guard (game 0x10aac4a0) |
| Server tick rate clamp | [10, 120]; listen server and clients 0 (no cap) | Hz | decoded | game 0x109ecb10 |
| Move batching window | 0.09 x min(1, unacked / 40) | s | decoded | game 0x10aad5c0 |
| Net speed forced to Max after a reset | 2 | s | decoded | game 0x10a0eac0 |

## Network

Server: tick cap, send budget, relevancy timeouts. Client: net speed, keep-alive, timeouts, the
script cadences. All values are configuration, not replicated.

## Edge cases

- The `CONF*` keys exist only in SCCT; a profile with the stock UE2 rules alone (fixed net speed) will
  send more on a fast LAN than the original.
- One shared server budget steered by the worst client's lag means one laggy client slows updates to every
  client; reproduce it in the faithful profile.
- `[Engine.DemoRecDriver]` has its own 60 Hz caps; demo recording is not in the slice.
- Reloaded rewrites some of these at start for high frame rates (`rl:net.rates`); traces taken on the
  reference install already include that.

## Evidence

- `%USERPROFILE%\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3\System\Default.ini` lines 57-72
  ([URL]), 107-112 ([Engine.GameEngine], [Engine.Player]), 129-168 ([IpDrv.TcpNetDriver]), 175-186
  ([Engine.DemoRecDriver]).
- The game executable registers the same key names (`NetServerMaxTickRate`, `LanServerMaxTickRate`,
  `MaxClientRate`, the `CONF*` set, `maxAcceptableUnAckBunch`, `ConfiguredInternetSpeed`,
  `AckTimeout`, `SimLatency`, `SimPacketLoss`): `local/reloaded_strings/game_exe.txt` lines 10500-10644
  and 11356-11376.
- Editor addresses: `UNetDriver::TickFlush` 0x1113da00, `UNetDriver::TickDispatch` 0x1113d670,
  `UNetConnection::FlushNet` 0x11147ab0, `UTcpNetDriver::TickDispatch` 0x10f5cbc0,
  `ULevel::TickNetServer` 0x11184200.
- Script cadences: `local/re-out/uc/SBase/SPlayerController.uc` lines 3551 (0.14 s), 2536 (0.2 s),
  1095 (1.0 s), 8126 (1.0 s timer); `Engine/Mover.uc` `BeginPlay` (timers 1.0 s and 4.0 s).
- Defaults: `local/formats/classdefaults_effective.json`.

## Open questions

None left from the native study. Traces to confirm the decoded rules: two-client LAN and internet-like runs under
the RE+ host with `DefaultPktLag` 150, logging `NETSPEED` changes; `Level.TimeSeconds` deltas on a dedicated
server with and without `-lanplay`.

### Resolved

1. **Adaptive net speed.** Resolved: once per second per connection from the average acknowledgement lag (Min above
   MaxAllowedLag, linear between Max and Min up to it, + StepUp below LagToTakeAction, Max for 2 s after a reset);
   the client adapts its own rate, the server one shared rate from the worst lag, stepping by StepUp / clients.
   Rule 3a. Evidence: game 0x10a0eac0, 0x10a0e570; net-driver.md rule 8 (ANSWERS-net settings 1).
2. **maxAcceptableUnAckBunch.** Resolved: not a channel cap; it scales the client's move batching window,
   `0.09 x min(1, unacked reliable bunches / 40)`. Rule 5. Evidence: game 0x10aad5c0 (ANSWERS-net settings 2).
3. **Listen server tick and flush rate; which cap applies.** Resolved: no cap on a listen server or a client; a
   dedicated server uses LanServerMaxTickRate only with `-lanplay`, else NetServerMaxTickRate, clamped to
   [10, 120]; the same value gates replication. Rules 1, 2. Evidence: game 0x109ecb10 (ANSWERS-net settings 3).
4. **SpawnPrioritySeconds.** Resolved: not read; the server pass has no priority (list order). Rule 6. Evidence:
   game 0x10a30730 (ANSWERS-net settings 4).
5. **MaxResponseTime / MaxTimeMargin.** Resolved: both used (delta clamp 0.7 s; time-margin guard 0.35, 0.5 after a
   trip; server-side simulation after 0.5 x MaxResponseTime without moves). Rule 6a. Evidence: game 0x10aac4a0;
   editor 0x11183b50 (ANSWERS-net settings 5).
6. **Send budget (new).** Resolved: one budget for all client connections at the adaptive driver speed, refilled each
   frame with two frames of carry-over; MaxClientRate and NETSPEED only feed the correction rate limit. Rule 3
   rewritten. Evidence: game 0x109eeff0, 0x10a0d690 (ANSWERS-net settings new).
