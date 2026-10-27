# Answers to spec/net open questions (native networking study)

For the owners of `spec/net/` (and one equipment question) to fold into their specs. Each entry:
the question, the spec file, the answer in short, the evidence. Full rules are in the `net-*.md`
files of this folder. Addresses: game base 0x10900000, editor base 0x10E00000.

## spec/net/model.md

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | Do native lists send the script set under the script conditions? | No, not everywhere. Actor list: `Base` never; `bHidden`, `Role`, `RemoteRole`, `bNetOwner`, `bTearOff` first bunch only; vision group (`bEMFIsOn`, `EMFOpacity`, `EMFIntensity`, `HeatIntensity`, `bUnlit`, echelon flags, `bSpecialLit`) first bunch only; ambient sound group first bunch only; `Owner` only to the owning connection; `AmbientGlow`, `SoundBone` never; `StaticMesh` in the first bunch for `DT_StaticMesh`. Subclasses resend what they need. Per-class differences in `net-replists.md`. | editor 0x110a8610, game 0x10988610; `net-replists.md` |
| 2 | How do mesh swaps (glass, crane wall) reach clients? | `SBasicGamePlayObject`'s native list sends `StaticMesh` on every change while `DrawType` is `DT_StaticMesh`; the client's `PostNetReceive` calls `SetStaticMesh` and, when the new mesh is none, turns collision off and clears `bBlockActors`/`bBlockPlayers`. | editor 0x111c1830, 0x110b2770 |
| 3 | Which connections receive a server call on unowned actors? Local run? Condition per connection? How is `ActorNetChannels` filled? | Only functions with flag 0x01000000 (20 functions, listed in `net-replication.md` rule 20) go to every client connection that already has an open channel for the actor; no channel is opened for them. They also run locally on a listen server, not on a dedicated server. The condition is evaluated once, not per connection, and `bNetOwner` is normally false at that time on the server. `ActorNetChannels` and its padding hold a native cache of four (connection, channel) pairs, filled on channel open and cleared on close. `Actor.ChangeClientState` and `SBasePawn.AddDefaultGadgets` do not carry the flag; `AddDefaultGadgets` (condition with `bNetOwner`) is never sent. | game 0x10a29830, 0x10a29530, 0x10992150; editor 0x1118fa40, 0x110a63f0 |
| 4 | Does a client skip only non-simulated functions of `ROLE_None` actors, or all script? | Both effects: `AActor::Tick` gives `ROLE_None` and `ROLE_DumbProxy` actors no script tick, no timers and no state code (only falling or rotating physics), and any non-simulated, non-native script function called on them is swallowed; simulated functions called by events still run. | editor 0x11183b50, 0x1118fa40 |
| 5 | `bNetDirty` semantics? | Each server tick, every actor with `bNetDirty` marks its channel on every open connection dirty, then `bNetDirty` is cleared. During an update `bNetDirty = channel dirty or first bunch`. `bOnlyDirtyReplication` actors with a clean channel are skipped entirely. Who sets `bNetDirty` (script writes) was not traced. | game 0x10a30ba0, 0x10a30730, 0x109e1300 |
| 6 | Does `GetPackedHW` sign-extend? | Yes, both `GetPackedLW` and `GetPackedHW` return the 16-bit half sign-extended. `PackW = High << 16 | Low & 0xFFFF`. | editor 0x10ff0ac0, 0x10ff0b10, 0x10ff09a0 |
| 7 | Vector rounding and rotator reduction? | Confirmed: vectors rounded to the nearest integer (adaptive bit count, up to 20 bits per component); rotators reduced to the high byte by truncation (negative angles wrap to 0..65280); `CompressedPosition` keeps only pitch and yaw bytes; floats, ints exact. | game 0x109567e0 |
| 8 | Map actor binding? | A `bStatic` or `bNoDelete` actor's channel opens with an object reference to the map copy (no class, no location); dynamic actors are spawned from class and location (and rotation with `bNetInitialRotation`). Deletion of other map actors at client load was not traced. | editor 0x11142700, 0x11140de0 |

## spec/net/settings.md

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | How do the `CONF*` values move the net speed? | Once per second per connection, from the average acknowledgement lag `L`: `L > MaxAllowedLag` → `Min`; `LagToTakeAction < L ≤ MaxAllowedLag` → linear between `Max` and `Min`; `L ≤ LagToTakeAction` → `+ StepUp` up to `Max`; forced to `Max` for 2 s after a reset. The client adapts its own rate; the server adapts one shared rate for all clients from the worst lag (dedicated or listen row), stepping by `StepUp / clients`. LAN rows apply after `EnterLAN`. Details `net-driver.md` rule 8. | game 0x10a0eac0, 0x10a0e570 |
| 2 | `maxAcceptableUnAckBunch`? | Not a channel cap. It scales the client's move batching window: `0.09 × min(1, unacked reliable bunches / 40)`. | game 0x10aad5c0 |
| 3 | Listen server tick and flush rate; which cap applies? | `GetMaxTickRate` returns 0 (no cap) on a listen server and on clients; a dedicated server uses `LanServerMaxTickRate` only when started with `-lanplay`, else `NetServerMaxTickRate`, clamped to [10, 120]. The same value gates replication by update frequency. | game 0x109ecb10 |
| 4 | `SpawnPrioritySeconds`? | Not read by the server pass; the pass has no priority at all (list order). | game 0x10a30730 |
| 5 | Are `MaxResponseTime`/`MaxTimeMargin` used? | Yes: every server move clamps its delta to `MaxResponseTime` (0.7 s), and the time-margin guard uses `MaxTimeMargin` (0.35, raised to 0.5 after a trip). The server also simulates a remote pawn itself when no move came for `0.5 × MaxResponseTime`. | game 0x10aac4a0; editor 0x11183b50 |
| new | Send budget | The game's server has one budget for all client connections: rate = the adaptive driver speed, refilled each frame, two frames of carry-over. `MaxClientRate` and the per-connection `NETSPEED` only feed the correction rate limit. Rule 3 of settings.md should change. | game 0x109eeff0, 0x10a0d690 |

## spec/net/move-protocol.md

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | Client message choice, merging, old-move pair, `View`? | Frames merge only while reliable data is unacknowledged (window up to 90 ms). Big move when actions wait; nothing in root motion or follow-parent; PCRot for Vandamme, BTW fire and PipH fire (view from the controller's yaw); full move with non-zero acceleration (accel × 10); short move every other frame with zero acceleration. Old move: `OldTimeDelta` in 0.5 ms units (below 256), `OldAccel` packed 11+11+7 bits plus signs. `View = (Pitch/2 & 0x7FFF) × 32768 + (Yaw/2 & 0x7FFF)`. | `net-moves.md` rules 1 to 10 |
| 2 | Server processing: time checks, delta, thresholds, short vs full, reinit? | Delta = `min(MaxResponseTime, stamp − CurrentTimeStamp)`; time-margin guard; physics must match or nothing is applied (3 s mismatch → reinit); correction when error > 1.73 uu, at most every 0.3 s and every `180 / NetSpeed` s, forced after 1 s; short form when the server pawn's velocity is zero; reinit after a correction with error > 400 uu unless both walk. | `net-moves.md` rules 11, 12 |
| 3 | `DiversInfos` low nibble? | Bits 0..1 zone config, bit 2 merc charging, bit 3 jump; a spy's adrenaline flag is written as 1 into the zone bits. The server checks the physics nibble against its pawn (no switch to the client's mode). | `net-moves.md` rule 9 |
| 4 | Server-side `BSMS`, duplicate resends, traversal vectors? | The resend stores the whole struct in the server's `BSMS`; the reliable form stores only `BSMS.TimeStamp`. Big moves older than `BigServerMoveLastActionTime` are ignored. Traversal vectors set `ZipP1/2`, `NewZipP1/2`, `vVandammePos`, `vWallJumpDir` or `m_geoNormal` by the pawn's physics. | `net-moves.md` rule 13 |
| 5 | Is a stale `MSATime` dropped? | Yes, but with a 1 s tolerance (`trunc` of the difference must be 0), unlike the 0.01 s of the action RPCs. | game 0x10aae660, 0x10ab1670 |
| 6 | Saved-move lifetime? | One saved move per send (not per frame); spawned until 100, then the list becomes a ring and the oldest is overwritten; never freed. | editor 0x111e7160, 0x111f2a80 |
| 7 | Writers of `fLastTimeOnAMover` and `fLastTimeMoverMoves`? | `fLastTimeOnAMover`: none (rule 20.4b never fires). `fLastTimeMoverMoves`: mover physics writes `Level.TimeSeconds` while `|Velocity.Z| > 0.1`. | game 0x10a21970; offset search |
| 8 | `MovePawn`? | Swept move; if blocked more than 50 uu short of the target, teleport to the target. Base not changed explicitly. | editor 0x111fe4a0 |
| 9 | What does `MyServerMoveWithPCRot` add, and Reloaded's change? | Server: like a short move but the pawn's rotation is left alone (only controller rotation and `iYawPitchFromView` follow `View`), no speed cap, quicker corrections (0.3 s / 0.1 s), corrected Z in the client's zone config. Reloaded sets `MaxTimeMargin` to infinity during the call. | `net-moves.md` rules 14, 15 |
| 10 | `CompressActions` layout? | `a1 | a2<<9 | a3<<18 | count<<27` and `a4 | a5<<9 | a6<<18`; top bits unset. | editor 0x111f8ad0 |

## spec/net/state-sync.md

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | Who raises `ReinitPawnBecauseOfDivergences`? | The server move core: after a correction with squared error > 160000 (unless server and client both walk, or the pawn is dead), and after 3 s of continuous physics mismatch between client moves and the server pawn. | game 0x10aac4a0 |
| 2 | What does `ReinitBecauseOfDivergences` reset? | The basic action state: BTW, PipHFire, IK type, rolling, special anim, weapon transit, lying flags, blend times, attack partners; physics walking, idle animation, collision on. | `net-moves.md` rule 17 |
| 3 | Epoch check in `BigServerMove` on a dedicated server? | Yes, with a 1 s tolerance. | question 5 of move-protocol |

## spec/net/proxy-pawns.md

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | Scheduling of the four `Send*` events? | Decided inside the pawn's native property list, per viewer: a sample at most every 0.09 s and at least every 0.7 s, otherwise when dead reckoning fails (velocity off by more than √0.2 of speed, or position off by more than √30 uu, or the pawn stopped), always on physics change or new actions; actions alone repeated for 0.15 s; actions with position within 0.4 s of a change, reliable only for a batch new to that viewer. | `net-proxy.md` rules 1 to 5 |
| 2 | Smoothing law? | Error removed at up to 200 uu/s (walking, ramping over 1 s) or 400 uu/s; snap when over 6 × collision radius or on physics change; final step absorbs the rest. | `net-proxy.md` rule 13 |
| 3 | Action playback? | The proxy tick executes the new batch in order when its time is newer than the last played; the engine's action code is called directly (Reloaded's wrapper not involved). | `net-proxy.md` rule 8 |
| 4 | Server copy of a remote spy's `iYawPitchFromView`? | Set by every server move from `View` (`PackW(Yaw, Pitch)`). | `net-moves.md` rule 11.8 |
| 5 | Is `PawnPosition` still sent as a property? | No; the pawn list bypasses the Pawn list and sets `bCompressedPosition`. | editor 0x1120c9d0 |
| 6 | `ClientDistantMoveSmooth`? | Swept move with one slide along the hit surface. | editor 0x11206650 |

## spec/net/rep-map-actors.md

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | Mover client interpolation? | `OldPos = SimOldPos`, `OldRot` = current rotation, `PhysAlpha/PhysRate = SimInterpolate.XY × 0.01`, `KeyNum` low byte and `PrevKeyNum` high byte of Z, physics moving brush, interpolating; only when `SimInterpolate` changed. | editor 0x110b3020, 0x110a70e0 |
| 2 | What does `ASAnimatedMesh`'s list send beyond `SimAnim`? | `bHidden` on every change, nothing else. Notifies on clients not studied. | editor 0x111d2790 |
| 4 | Gameplay object relevancy? | AActor test (always within 2500 uu of the camera, else BSP line of sight); a pickable object on a bone follows its carrier. | `net-relevancy.md` rule 5 |
| extra | `BasePos` | The Mover list never sends `BasePos` (only `SLiftDoor` does, with `Base`). | editor 0x110aaeb0, 0x111d36b0 |

## spec/net/rep-pawn.md, rep-controller.md, rep-info.md, rep-objectives.md, rep-inventory.md

| Spec, # | Question | Answer | Evidence |
|---|---|---|---|
| rep-pawn 1 | Native lists against blocks 4 and 9? | Aim variables only to non-owners at most every 0.09 s; `AllActions` group first bunch to non-owners; Actor list at most every 0.09 s; the Pawn list is bypassed, so `PawnPosition`, `bIsWalking`, `bIsTyping`, `GroundSpeed`, `AccelRate`, `AirSpeed`, `JumpZ`, `AirControl` are never sent and `bIsCrouched` only in the first bunch; see `net-replists.md` rule 12. | editor 0x1120c9d0 |
| rep-pawn 3 | Vision fields on pawns? | The pawn list resends `bEMFIsOn`, `EMFOpacity`, `EMFIntensity`, `HeatIntensity` on every change. | same |
| rep-controller 1 | `ASPlayerController` list? | The 12 variables, no `bNetOwner` test, `MyDesiredRotation` first bunch only, `SpecTarget` forced by `bForceReplicationForSpecTarget`. | editor 0x111eecc0 |
| rep-info 1 | Native `PostNetReceive` of PRI and LevelInfo? | PRI (client): for the local player's own PRI, `Ping` is adjusted by the connection's measured time and floored at 0; a changed `PlayerName` raises `ClientNameChange`, a changed `CharacterName` raises `UpdateCharacter`. LevelInfo: only the Actor behaviour (nothing for `GEActivationMask`). | editor 0x110b32b0, 0x110b37e0 |
| rep-objectives 1 | Terminal skin after the first bunch? | The `SComputerObjectiveTrigger` list sends `SkinState` on every change (and `DeltaCaptureTime` while active). | editor 0x111cf840 |
| rep-objectives 2 | `ASFlag::IsNetRelevantFor`? | Hidden → no; carried by a pawn → the carrier's result; otherwise always relevant. | game 0x10ae9a00 |
| rep-objectives 4 | `ASDisk` and `ASFlagDropZone` lists? | Disk: `Base`, `bHidden`, `bTriggering` plus the objective trigger list. Drop zone: only `bTriggering` (no parent list at all). | editor 0x111ce9c0, 0x111cf480 |
| rep-inventory 1 | `ThrowGren` and Reloaded? | Reloaded reimplements it: launch location rounded and rotation cut to 256 steps before spawning (network quantisation applied locally), unless a Reloaded option is set. | `net-reloaded.md` rule 4 |
| rep-inventory 2 | `ASGadTorche::IsNetRelevantFor`? | The `Instigator`'s relevancy (editor). | editor 0x111de220 |
| rep-inventory 3 | `ASSmokeEmitter` list? | Only `AmbientSound`, no parent list. | editor 0x111e07c0 |
