# Reloaded.Core: the 37 natives it replaces or adds

Units: rl:Core.Object.FRand, rl:Engine.Actor.SetRotation, rl:Engine.Controller.GotoState,
rl:Engine.PlayerController.Exit, rl:IpDrv.UdpLink.BindPort, rl:SBase.SGadTorche.NativeTickTorche,
rl:SBase.SGadGrenLauncher.ThrowGren, rl:SBase.SLANServerLink.ReceivedBinary,
rl:SBase.SLANClientLink.ReceivedBinary, rl:SBase.SLANClientLink.PingServers,
rl:SBase.SLANClientLink.GetServerEngineVersion, rl:SBase.SLANClientLink.getGameMode,
rl:SBase.SPlayerController.MyServerMoveWithPCRot, rl:SBase.SPlayerController.CheckGEForWallJump,
rl:SBase.SBasePawn.ExecuteAction, rl:SBase.SPawnAttaque.TickNative,
rl:SBase.SMotionTrackingVision.GetActors, rl:SBase.SHud.ProcessSFX, rl:GUI.GUIController.LANParty_MatchStarted,
rl:GUI.GUIController.LANParty_DumpPlayerInfo, rl:GUI.StrategicMap.UpdateTeamMate,
rl:GUI.GUIController.StepPlaylistMap, rl:GUI.GUIController.OpenSettingsTab,
rl:SBase.SPlayerController.{ToggleFreeCamera, ToggleHideHud, RaiseChatLine, RaiseTeamChatLine,
NativeOnlineFlags, NativeSetLiveState, NativeRenderCaps, NativeStoreRenderCaps, NativeRumbleEnable,
NativeRumblePadStart, NativeRumblePadStop, NativeRumblePadTime, NativeRumblePadUpdate, NativeRumbleTriggers}

## Purpose

The reference install runs the game with `Reloaded.Core.dll` (v4.0d) injected. Reloaded points 37 entries of
the engine's native function table at its own code: 20 natives that the game already has, and 17 natives
(indices 1900 to 1916) that the Enhanced 4.0 scripts declare and that only Reloaded implements. This file
says, for each one, what the game does and what changes under Reloaded, so the port can reproduce the
reference behaviour (or knowingly choose the stock one). Behaviour only; companion specs:
`net-reloaded.md` (network side), `execute-action.md` rule 6, `spec/reloaded/FEATURES.md` (player-facing
features), `reloaded-features-from-code.md` (voice, master server and the other subsystems).

## Data

- **Native table copy.** At start-up Reloaded copies the engine's native table and, for each hooked index,
  stores the original function pointer. A wrapper looks its original up once by native index (first call,
  thread-safe static) and calls it. Wrappers that read their own parameters evaluate script expressions
  through the same table (the game's, or Reloaded's copy while its hooks are installed).
- **LevelInfo pointer.** Reloaded keeps the current `LevelInfo`; many rules below test `Level.NetMode`
  (0 standalone, 1 dedicated, 2 listen, 3 client) and `Level.LocalPlayerController`.
- **Start-up switches.** A few rules depend on internal Reloaded switches that are set at start-up, not by
  the user's config file (marked "internal switch" below). Their values in the reference install were not
  read; see Open questions.

## Rules

Kinds: **W** wrapper (calls the original with changes before or after), **R** replacement (own body, the
original is not called), **N** new native (the stock game has no body; script calls did nothing).

### Core and engine

1. **FRand (195), R.** Stock: `rand() / 32768`, uniform in [0, 1). Reloaded: identical, except for fire
   prediction. On a server (net mode 1 or 2), with fire prediction on, when the calling script function
   is `TraceFire` (merc weapons) or `TraceFireWithPos` (spy weapons), the first two `FRand` calls of that
   call return a deterministic value: a 32-bit hash of a per-shot input (the merc weapon's aim yaw, or a
   component of the spy shot's orientation parameter), a shot counter, the call number and a team tag (`M`
   or `S`), mixed with a golden-ratio constant and multiply-xorshift rounds, then the top 24 bits scaled by
   2^-24 (inputs decoded in part). The point (inferred from the feature) is that the shooter's client can
   compute the same spread for its predicted impacts. Further calls in the same trace, and all other
   callers, use stock `rand()`. Fire prediction turns itself off (with a log line) after an exception and
   when the script names are not found.
2. **Actor.SetRotation (299), W.** Stock: `MoveActor` with a zero delta and the new rotation. Reloaded:
   unchanged except for the local player's controller when called from one of two script functions whose
   names it looks up at start-up. A call from the first only raises a Reloaded flag. A call from the second,
   while `bBinocularVision` is off, runs the original, adds the yaw and pitch change it made to two
   Reloaded view offsets and puts the controller's previous rotation back: those script rotations feed
   Reloaded's own view handling instead of turning the controller. Local only.
3. **Controller.GotoState (1175), R.** Stock: reads `NewState`, optional `Label`, optional `bNoStateChange`;
   if the state differs, calls `GotoState`; on success stores the previous state name (offset 0x2F8) unless
   the optional flag was given, then `GotoLabel(Label or Begin)`; logs "State not found" or "Label not
   found" except for `None` and one reserved name. Reloaded: the same sequence, plus, for the local
   player's controller only, Reloaded's integrity check runs first; when it fails the requested state is
   replaced by a fixed state. The rewrite does not reproduce this (no integrity system; see Edge cases).
4. **PlayerController.Exit (1181), W.** Stock: closes the game (`PostQuitMessage`). Reloaded first sets
   an "exiting" flag that its own code tests during shutdown, then calls the original.
5. **UdpLink.BindPort (1257), R.** Stock: create a UDP socket, bind the requested port (trying the next
   ports when asked), store the port. Reloaded keeps the contract (returns the bound port, 0 on failure,
   errors "winsock failed", "socket failed", "already bound") but always binds its configured port:
   `host_port_query` on a listen host, `client_port_query` otherwise, with broadcast enabled and up to 20
   attempts on the following ports when wrapping is asked. On a listen host with `host_upnp` it also adds
   the UPnP mapping for that port. The same socket carries the master server traffic
   (`reloaded-features-from-code.md` section 2).

### Gameplay

6. **SGadTorche.NativeTickTorche (1300), W.** With an internal switch on and the torch not in use,
   Reloaded sets `bInUse` for the duration of the original tick and clears it afterwards, so the light
   update runs as if the torch were in use. Visual fix (flashlight rendering); does not change what is
   replicated.
7. **SGadGrenLauncher.ThrowGren (1314), R.** Stock: reads the launch location and rotation and two
   arguments for the projectile; spawns `ProjectileClass` there (owner and instigator of the launcher) and
   sends it the throw event (name index 0x2E932) with those arguments. Reloaded: same spawn and
   event, but unless an internal switch is set, the location is first rounded to whole units (`floor(x +
   0.5)` per component) and each rotation component is cut to its high byte (multiple of 256), which is what
   those values become after replication. A listen host's own throw then matches a client's throw on the
   server (`net-reloaded.md` rule 4).
8. **SPlayerController.MyServerMoveWithPCRot (1392), W.** `MaxTimeMargin` is set to +infinity for the
   original call and restored afterwards (`net-reloaded.md` rule 1).
9. **SPlayerController.CheckGEForWallJump (1363), W.** Sets `fGEZLocation` to -10 before the original.
10. **SBasePawn.ExecuteAction (1411), W.** For the local player's own pawn: before the call, undo a pending
    shift (`fGEZLocation += 46`); after the call, if the action was 234 (`SA_WLJP_JUMP1`) with `bIsLeftPart`,
    lower the controller's `fGEZLocation` by 46 and remember it (`execute-action.md` rule 6).
11. **SPawnAttaque.TickNative (1414), R.** Stock and Reloaded share the screen glass effect logic of the
    attacking (spy) pawn: when controlled locally and `DesiredGlassEffect` is set, rain volume, sticky
    shock or fast movement (speed over 240 uu/s, not on a ladder) clear the effect (event with 1.0); the
    current `GlassEffect` moves towards the desired one by `SlopeGlassEffect × (TimeSeconds + 0.1) +
    BGlassEffect`; during a perturbation it jitters by a random amount (amplitude from 10 to 510 shrinking
    over `PerturbationDuration`, active while `cos(PerturbationValue × TimeSeconds) > 0`), and 125 replaces
    a value equal to the desired one. Reloaded changes: (a) with an internal switch the "fast movement"
    condition is ignored (only rain volume and sticky shock clear the effect); (b) the jittered value is
    clamped to 1..254 instead of wrapping around the byte range, which removed flicker. The engine's
    post-tick call is kept.
12. **SMotionTrackingVision.GetActors (1422), R.** Stock: the merc's motion tracker lists projectiles and
    pawns (not the viewer's own pawn) within `MaxDetectedDistance + CollisionRadius` of the viewer that move
    at least `MinDetectedVelocity` and are not hidden, with a clear line from the target to the viewer
    (trace flags 0x86, actors with the pass-through flag ignored); new entries play `detectsound`; entries
    not refreshed for `OutTime` after `KeepLockTime` are removed. The viewer point is the pawn, or the used
    object in one state, or the controller in three camera states. Reloaded: same lists and timings, but
    (a) with an internal switch the viewer is `ViewTarget` instead of `Pawn` (works while spectating or in
    a camera), (b) when the first visibility trace hits nothing, a second trace is made before the target is
    accepted, and (c) one actor class with a sentinel `DesiredGlassEffect` value is skipped.
13. **SHud.ProcessSFX (1452), R.** The HUD screen-effect pass (fades, flashes, colour washes, the vision
    blend amount, per-slot start and end values, constant or linear over time, clamped to their range).
    Reloaded reimplements it with the same effect types and the same timing; its full-screen quads are
    drawn through its own helpers. The per-type differences were not compared (Open questions).

### Menus, LAN and lobby

14. **SLANClientLink.PingServers (1345), R.** Stock: broadcast the LAN query (`SCLIPING`) on the query port.
    Reloaded: also sends it to every address of the manual `server_list`, to the servers last returned by
    the master server, and asks the master for its list (`SCLIREQL`, throttled to once per 3 s).
15. **SLANClientLink.ReceivedBinary (1344), R.** Stock: parse `SSRVINFO` (server name, map, players, mode,
    time limit, PunkBuster flag, match begun) and `SSRVSTOP`. Reloaded: the same plus the master's replies
    (`SCLILIST`, `SCLIREGR`) and the extended reply `SSRVINEX`, with the ping taken from its own send
    timestamps minus the server's reported processing time. For a server with another engine version the
    map field is replaced by a fixed map name (`AQU03` or `AquaD`, by local build), so the entry cannot be
    joined by mistake.
16. **SLANServerLink.ReceivedBinary (1342), R.** Stock: answer `SCLIPING` with `SSRVINFO` (204 bytes) built
    from script getters (`refreshInfos`, `getLevelName`, `getMatchHasBegun`, `getMaxPlayers`, `getNbPlayers`,
    `getPBServerSetting`, `getTimeLimit`, `getPlayerName`). Reloaded: the same answer (adding the alternate
    mode flag and game mode from the replication info), plus `SCPINGEX` answered with `SSRVINEX` (422
    bytes): the client's request identifiers echoed, the time the server spent answering, and the extended
    server information the browser shows.
17. **SLANClientLink.GetServerEngineVersion (1355), R.** Returns true when list entry i has the same engine
    version as this build (Reloaded compares its own version number).
18. **SLANClientLink.getGameMode (1905), N.** Returns the game mode byte that the `SSRVINEX` reply reported
    for list entry i (0 when unknown).
19. **GUIController.LANParty_MatchStarted (1832), R.** Stock: Xbox LAN party bookkeeping. Reloaded: appends a
    `[MatchInfo]` block to `ChaosTheory.log` in the user folder (the match log): `UTCTime`, `Map`,
    `Alternate=True|False`, `Mode=Story|Deathmatch|<n>`, `NbObjectives`, `TimeLimit=<n>|Inf`.
20. **GUIController.LANParty_DumpPlayerInfo (1834), R.** Reloaded reads and discards the parameters (no-op).
21. **StrategicMap.UpdateTeamMate (1868), R.** Places a team-mate marker on the strategic map: finds the
    map section by the mate's height, projects the world position with the section's camera
    (`tan(FOV/2)` and the view axes) into 0..1 map coordinates and positions the marker widget. Reloaded
    uses the same projection, guards against a missing pawn, level or map section (the stock code reads
    them unchecked) and returns true only when the marker lands inside the map (0..1 on both axes).
22. **GUIController.StepPlaylistMap (1904), N.** Steps the lobby map playlist forward or back (`rl:qol.lobbymaps`).
23. **GUIController.OpenSettingsTab (1916), N.** Opens Reloaded's settings window on tab 0..5; 16 and 17 open
    tab 0 in one of two modes; ignored while the window is already open.

### Player controller additions (all N)

24. **ToggleFreeCamera (1900).** Allowed when standalone, or when the server's `GameReplicationInfo` has
    `bAlternateMode` and `bAvlb_CinematicCamera`, and the local player has a camera and a living pawn.
    Saves `vCamLocation`, calls the script's `ClientSetViewTarget` for the free camera and, unless the
    player is on team 1, `ClientSetBehindView(false)`; sets two camera speeds of 600 uu/s; remembers the
    pawn rotation, `IK_Type` and the time. Called again it restores the saved view.
25. **ToggleHideHud (1901).** Same permission test; toggles a Reloaded flag that hides the HUD.
26. **RaiseChatLine (1902), RaiseTeamChatLine (1903).** Call the console's `Talk` or `TeamTalk` (open the
    chat line).
27. **NativeOnlineFlags (1906), NativeRenderCaps (1908), NativeSetLiveState (1907),
    NativeStoreRenderCaps (1909).** A server-initiated request and reply that the Enhanced 4.0 scripts run
    (`RefreshPlayerLiveFlags` → client `NativeOnlineFlags(Stamp)` → server `NativeSetLiveState`, and
    `RequestRenderCaps(Token, Key)` → client `NativeRenderCaps` → server `NativeStoreRenderCaps`). The two
    client natives compute a reply from the challenge; in this build the two server natives read the value
    and discard it. It belongs to Reloaded's client verification; the rewrite does not reproduce it.
28. **Rumble (1910 to 1915).** `NativeRumbleEnable(bool)` turns script rumble on or off (off also stops
    the motors); `NativeRumblePadStart`, `NativeRumblePadStop`, `NativeRumblePadTime(seconds)` (start a
    timed rumble, or stop when 0) and `NativeRumblePadUpdate(low, high)` drive the SDL gamepad's two
    motors with 16-bit strengths scaled by `controller_rumble_strength` (clamped to 0..1);
    `NativeRumbleTriggers` drives the trigger motors. All act only with native controller support on, a pad
    with rumble connected, `controller_rumble_enabled`, and script rumble enabled.

### Direct code patches

Besides the native table, Reloaded's start-up routine (Reloaded 0x1001c350) writes calls, jumps and
constants into the game's code at about 110 places in 48 game functions. The names come from the
transfer study and the physics and net studies. Not analysed yet beyond the list.

| Game function | Name |
|---|---|
| 0x10996830 | UEngine::InitAudio |
| 0x1099a6f0 | UESoftBody::Update |
| 0x109ebb20 | UGameEngine::Draw |
| 0x10aa4420 | ASPlayerController::CheckLedgeArms |
| 0x10aa54d0 | ASPlayerController::execCheckArmsZone |
| 0x10aac4a0 | ASPlayerController::ProcessServerMove |
| 0x10ab01e0 | ASPlayerController::NativePlayerCalcView_StickyCamSmoothToPosition |
| 0x10ab2df0 | eventWaistLedge |
| 0x10ab8990 | ASProjGrenade::Tick |
| 0x10ac0a30 | ASMotionTrackingVision::execGetActors |
| 0x10ac6b40 | ASBasePawn::physWalking |
| 0x10ad3dc0 | ASBasePawn::physFalling |
| 0x10af8460 | ASHud::ERDrawOneObjective |
| 0x10aaa910 | the NativePlayerInput body (`input.md`) |
| 0x10b26ce0 to 0x10b33094 | 17 unnamed functions in the Direct3D renderer range |
| others | 0x109128b0, 0x10992100, 0x1099b130, 0x109ab740, 0x109ade10, 0x109e9890, 0x10a31fb0, 0x10a3d3b0, 0x10a415e0, 0x10a42620, 0x10a666d0, 0x10a96280, 0x10ab5df0, 0x10ab8cf0, 0x10aef530, 0x10af0390, 0x10af0730, 0x10af1d10, 0x10b08730 |

## States

None of their own. Free camera (rule 24) is a Reloaded mode on the local player.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Stock FRand scale | 1/32768 | | decoded | game 0x10945880 |
| Predicted FRand mantissa | top 24 bits × 2^-24 | | decoded | Reloaded 0x100242f0 |
| Predicted FRand calls per trace | 2 | calls | decoded | Reloaded 0x100242f0 |
| Grenade location rounding | floor(x + 0.5) | uu | decoded | Reloaded 0x10023c80 |
| Grenade rotation mask | 0xFF00 | rotator units | decoded | Reloaded 0x10023c80 |
| Glass effect clear speed | 240 | uu/s | decoded | game 0x10af5cf0 |
| Glass jitter amplitude | 10 to 510 | | decoded | game 0x10af5cf0, Reloaded 0x100caae0 |
| Reloaded glass jitter clamp | 1 to 254 | | decoded | Reloaded 0x100caae0 |
| Free camera speed | 600 | uu/s | decoded | Reloaded 0x10022f00 |
| Bind attempts with wrapping | 20 | ports | decoded | Reloaded 0x100cc2b0 |
| SSRVINFO / SSRVINEX sizes | 204 / 422 | bytes | decoded | Reloaded 0x100c9d50 |
| PCRot `MaxTimeMargin` | +infinity | s | decoded | Reloaded 0x10023870 |
| Wall-jump preset `fGEZLocation` | -10 | uu | decoded | Reloaded 0x10023930 |
| ExecuteAction height shift | 46 | uu | decoded | Reloaded 0x10023740 |
| List request throttle | 3 | s | decoded | Reloaded 0x100c1060 |

## Network

Rules 1 (server side, mirrored by client prediction), 7 (server side) and 8 (server side) change match
behaviour; rules 14 to 18 change the LAN and internet browser traffic; rule 27 adds two client-to-server
replies. Everything else is local to one machine.

## Edge cases

- The rewrite has no integrity or attestation system; it never talks to Reloaded services and never
  reproduces Reloaded's checks (rules 3 and 27). The Enhanced script paths that call natives 1906 to 1909
  become no-ops in the port.
- Difftest traces recorded in the reference install contain rules 1, 7, 8, 10 and 11. A stock-mode port
  will differ there by design.
- Rule 1 uses a hash, not the engine's random stream, so a port that wants identical spread on both ends
  needs the same inputs (shot counter and the per-shot aim value), not the same RNG state.

## Evidence

- Reloaded.Core v4.0d (sha256 6b9d4444...6995dc46, base 0x10000000) wrappers: FRand 0x100242f0, SetRotation
  0x10024180, GotoState 0x10024080, Exit 0x10023fe0, BindPort 0x10023ec0, NativeTickTorche 0x10023df0, ThrowGren
  0x10023c80, MyServerMoveWithPCRot 0x10023870, CheckGEForWallJump 0x10023930, ExecuteAction 0x10023740,
  TickNative 0x10023710 (body 0x100caae0), GetActors 0x100236f0 (body 0x100ca650), ProcessSFX 0x10023660
  (body 0x100c8930), PingServers 0x10023a60 (body 0x100c8c70), client ReceivedBinary 0x10023a80 (body
  0x100c8fa0), server ReceivedBinary 0x10023b80 (body 0x100c9d50), GetServerEngineVersion 0x100239e0,
  getGameMode 0x10022c20, LANParty_MatchStarted 0x100232a0 (body 0x100c80f0), LANParty_DumpPlayerInfo
  0x10023190, UpdateTeamMate 0x10023090 (body 0x100cb020), StepPlaylistMap 0x10022d50, OpenSettingsTab
  0x10022580, ToggleFreeCamera 0x10022f00, ToggleHideHud 0x10022e70, RaiseChatLine 0x10022e00,
  RaiseTeamChatLine 0x10022d90, NativeOnlineFlags 0x10022bd0, NativeRenderCaps 0x10022b70,
  NativeSetLiveState and NativeStoreRenderCaps 0x10022b40, rumble 0x100226a0, 0x10022a70, 0x10022a00,
  0x10022720, 0x10022890, 0x100225f0. Fire prediction set-up 0x1000e2c0 (script names 0x1000e040).
- Game originals (base 0x10900000): FRand 0x10945880, SetRotation 0x10a27030, GotoState 0x109f1f20, Exit
  0x109f0020, BindPort 0x10a7fa80, NativeTickTorche 0x10ab6eb0, ThrowGren 0x10aa0a10, LAN server
  ReceivedBinary 0x10ab3d40, client ReceivedBinary 0x10ab39c0, PingServers 0x10ab3780,
  GetServerEngineVersion 0x10ab3590, CheckGEForWallJump 0x10aa6250, MyServerMoveWithPCRot 0x10aaec30,
  ExecuteAction 0x10ad6800, TickNative 0x10af5cf0, GetActors 0x10ac0a30, ProcessSFX 0x10a91e90,
  LANParty_MatchStarted 0x10a57260, LANParty_DumpPlayerInfo 0x10a576e0, UpdateTeamMate 0x10a62a10.
- Field names from the reflection dump; script context from the decompiled SBase and GUI packages
  (local only).

## Open questions

1. Values of the internal switches in the reference install (rules 6, 7, 11, 12): read them from a running
   sandbox with the recorder (memory read only), or infer from a grenade trace (rounded or not).
2. Rule 13: compare the SFX types of the two versions one by one. Rule 21: the stock return value when
   the marker is off the map.
3. Rule 2: name the two camera script functions (indices looked up at start-up).
4. The direct code patches: physWalking, physFalling, ProcessServerMove, the ledge and arms tests and the
   sticky camera view are gameplay; each needs a before/after reading and a note in the physics and net
   specs.
