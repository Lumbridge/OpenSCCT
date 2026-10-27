# Pawn classes: per-tick hooks, crush, eye height, bob, animation-end bookkeeping

Units:
- uc:SBase.SBasePawn:Tick
- uc:SBase.SBasePawn:PostNetBeginPlay
- uc:SBase.SBasePawn:EyePosition
- uc:SBase.SBasePawn:CrushedBy
- uc:SBase.SBasePawn:KilledBy
- uc:SBase.SBasePawn:EncroachedBy
- uc:SBase.SBasePawn:BaseChange
- uc:SBase.SBasePawn:TakeFallingDamage
- uc:SBase.SBasePawn:TakeDamage
- uc:SBase.SBasePawn:ShouldCrouch
- uc:SBase.SBasePawn:GetAnimName
- uc:SBase.SBasePawn:AnimEnd
- uc:SBase.SBasePawn:ImpulsionSplit
- uc:SBase.SBasePawn:RMColOn
- uc:SBase.SBasePawn:SetVelocityForDyingPawn
- uc:SBase.SPawnAttaque:Tick
- uc:SBase.SPawnAttaque:PointOfView
- uc:SBase.SPAWNDEFENSE:Tick
- uc:SBase.SPAWNDEFENSE:EyePosition
- uc:SBase.SPAWNDEFENSE:CheckBob
- uc:SBase.SPAWNDEFENSE:StartCrouch
- uc:SBase.SPAWNDEFENSE:PointOfView
- uc:Engine.Pawn:UpdateEyeHeight
- uc:Engine.Pawn:BecomeViewTarget
- uc:Engine.Controller:WantsSmoothedView
- uc:Engine.Controller:AdjustView
- uc:Engine.PlayerController:AdjustView
- uc:Engine.Pawn:Landed
- uc:Engine.Pawn:TakeFallingDamage
- uc:Engine.Pawn:JumpOffPawn
- def:SBase.SPawnAttaque_1Mesh
- def:SBase.SPawnDefense_1Mesh

## Purpose

The pawn-side script that movement relies on: the per-tick native hooks, being crushed by movers (the SteeE duct
drop), the eye position used by the cameras, the merc view bob, and the pawn's own AnimEnd bookkeeping that the
controller states depend on (weapon transit, back-to-wall pose, the fall after being pulled off a ledge).

## Data

| Field | Class | Default | Notes |
|---|---|---|---|
| EyeHeight, BaseEyeHeight | Pawn | 54.0, 55.5 | |
| BaseCrouchEyeHeight | SPAWNDEFENSE | 20.0 | |
| WalkingBob, CrouchingBob | SPAWNDEFENSE | 0.01, 0.08 | read by MyCheckBob |
| bWeaponTransit | SBasePawn | false | draw or holster clip in progress (blocks traversal grabs) |
| bNoRunAnimation | SBasePawn | false | |
| BlendChangeTime | Pawn | 0.25 | |
| m_AttackTarget, m_AttackedBy | SBasePawn | None | grab pair |

SPawnAttaque_1Mesh and SPawnDefense_1Mesh add nothing to their parents (no functions, no states); their defaults
are the parents' plus mesh choices.

## Rules

### Per tick

1. SBasePawn.Tick: `NativeTick(dt)` (native, see native-contracts.md); then on the authority the spy-bullet expiry
   (not movement).
2. SPawnAttaque.Tick: `TickNative(default.Health)` (native; the shipped binding is inside Reloaded.Core.dll), then
   the SBasePawn Tick. TickNative does no movement. Its screen glass effect part, shared by stock and Reloaded, is
   decoded in reloaded-hooks.md rule 11: when locally controlled with DesiredGlassEffect set, a rain volume, a
   sticky shock or fast movement (over 240 uu/s, not on a ladder) clears the effect; GlassEffect moves toward the
   desired value and jitters during a perturbation; Reloaded can ignore the fast-movement condition and clamps the
   jitter to 1..254. The health regeneration it is expected to run is specced in combat/health.md rule 3.
   NativeTick for a remote pawn (proxy) smooths the received position and plays the received actions
   (net-proxy.md rules 8 and 13; net/proxy-pawns.md).
3. SPAWNDEFENSE.Tick: the SBasePawn Tick. CheckBob(dt, Y) calls the native MyCheckBob(dt). The walking states call
   CheckBob each PlayerTick for a merc (walking.md).
4. SBasePawn.PostNetBeginPlay: a pawn without a controller (a remote proxy) calls InitializeBasicActions first.

### Eye position

SBasePawn.EyePosition returns (0, 0, EyeHeight). SPAWNDEFENSE.EyePosition returns (0, 0, EyeHeight) + WalkBob (the
merc's bob reaches the camera). Engine.Pawn.UpdateEyeHeight is the stock UE2 rule (smooth toward BaseEyeHeight at
`min(1, 10 dt / TimeDilation)` per frame, capped by a trace above the head: CollisionHeight + 34, or the hit minus
14). Crouching changes BaseEyeHeight: spy `min(0.8 CrouchHeight, CrouchHeight - 10)` = 40, merc 20 (walking.md).
It runs at the end of the pawn's post-tick (after UpdateMovementAnimation, ANSWERS-anim.md A9) while
`bUpdateEyeheight`, which Pawn.BecomeViewTarget sets: PossessedBy calls it for a PlayerController, so every player's
pawn smooths its eye on the server and on the owning client. With `Controller.WantsSmoothedView()` (walking, not just
landed) the eye keeps its world height through the tick's climb (`EyeHeight - Location.Z + OldZ`) and is clamped to
`-CollisionHeight / 2`; otherwise it eases without that and bJustLanded clears. It ends with the controller's
AdjustView (a PlayerController eases FovAngle toward DesiredFOV; another controller stops the updates and sets the eye
to its base unless a player views the pawn). Measured: a merc's EyeHeight 55.5 standing and 20.0 crouched (his camera
55 uu lower). Measured on the spy (local Play Level player, local/spyhand/capture.py): EyeHeight and BaseEyeHeight 55.5
crouched at spawn and standing, 40.0 after a crouch from the key. The spawn's crouch (EnterStartState) runs StartCrouch,
then ClientRestart puts BaseEyeHeight and EyeHeight back to the default, and the second EnterStartState finds the pawn
already crouched, so the spawned spy keeps 55.5 until he stands and crouches again. A remote player's server copy,
which runs no ClientRestart, keeps 40 (not captured: the sandbox runs one game).

### Crushed by movers

CrushedBy(EventInstigator) (called by the crush-type mover's script `EncroachingOn`; devices/mover.md rules 20, 21:
the native CheckEncroachment only lists the overlapped actors and asks the mover, crushing itself is script):
1. If this pawn is grabbing another (m_AttackTarget with m_AttackTarget.m_AttackedBy == self), that pawn takes
   `Health + 100` damage of type SDTBase from EventInstigator through its controller's PawnTakeDamage, and if it
   was alive and dies, the game counts an environmental kill for EventInstigator's controller (when there is an
   instigator and the game is an SGameInfo).
2. This pawn then takes `Health + 100` the same way, with the same environmental-kill count.
KilledBy(EventInstigator) deals `Health + 100` SDTBase through PawnTakeDamage. EncroachedBy and BaseChange are
empty (the stock "jump off a pawn" and decoration-crush damage of Engine.Pawn.BaseChange do not happen).
TakeDamage and TakeFallingDamage are empty: all damage goes through SPlayerController.PawnTakeDamage, and fall
damage is the script rule in jump-fall-land.md (Engine.Pawn.Landed still calls the empty TakeFallingDamage).

### AnimEnd bookkeeping (SBasePawn.AnimEnd, before the controller's AnimEnd)

| Channel | Effect |
|---|---|
| 0 | SpltStAlRt0 / SpltStAlLt0 finished: play AFall (unarmed) or AFall2H with tween 0.35. HackStAlBg0: loop HackStAlNt0. BackCrAlTr0 / BackStAlTr0: replay the crouched or standing one by stance with tween 0.2 (looping the wall-end pose). AtakDnMmBg2: SetPhysics(Walking), loop AtakDnMmNt2. AtkdDnMmBg0: loop AtkdDnMmNt0. Spy death or knock-down clips (XxxxDnAlFd0, the ADeath* set, XxxxCrAlAk0, XxxxStAlAk0, StrkCrAlBk0): SetPhysics(Walking), bInternalChangePhysics, play XxxxDnAlBk0; for a merc only StrkCrAlBk0 does this. GrbdStLgRail (pulled over a rail): SetPhysics(Falling), bInternalChangePhysics, Velocity = 77.5 X with Z = -681, play FallStAlFr0, and the controller (if any) stamps m_LastSpecialMoveTime and goes to s_FallAfterGrabbedFromLedge |
| 1 | if bNoRunAnimation: clear it, blend channel 1 to 0, and when walking blend channel 4 to 1 |
| 4 | BlendChangeTime = 0.25 |
| 14 (draw) | blend 0; bWeaponTransit = false; back-to-wall pose update (back-to-wall.md) |
| 16 (reanimate) | blend 0 |
| 17 (damage) | sAction = 0, fade out over 0.3 s, restore IK_Type from IK_TypeBeforeDamage |
| 18 (goggles) | sAction = 0, toggle bGogglesUp, fade out 0.2 s |
| 19 (attack) | fade out 0.2 s |
| 21 (weapon) | sAction = 0, fade out 0.2 s, IK back to weapon-loaded after a reload clip |
| 22 (palm) | fade out ThroStAlMix over 0.1 s |
| 24 (interact) | fade out button or mine clips (ButtStAIFd0/2 backward, MineStAlEd0, MineCrAlEd0) over 0.2 s |

The sAction reset on channels 17, 18 and 21 means a later ChangeEnumAction of the same action id is not ignored.

## States

None (SBasePawn and its subclasses have no states).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Crush damage | Health + 100 | hit points | decoded | SBasePawn.CrushedBy |
| Pulled-over-rail fall | 77.5 forward, -681 down | uu/s | decoded | SBasePawn.AnimEnd |
| Merc bob | WalkingBob 0.01, CrouchingBob 0.08 | factor | decoded | def:SBase.SPAWNDEFENSE |
| Eye heights | 54 / 55.5; crouched spy 40, merc 20 | uu | decoded | defaults, StartCrouch |

## Network

- Tick and AnimEnd run on every machine that has the pawn. CrushedBy and KilledBy deal damage through
  PawnTakeDamage, which is authority-side (combat spec).
- SetVelocityForDyingPawn sends the velocity to the server x 100 when called on a client.

## Edge cases

- A crushed spy that is carrying a grabbed merc kills the merc first, so both kills are counted as environmental.
- BaseChange is empty, so a pawn landing on another pawn stays there (walking.md: the spy cannot jump from a pawn).

## Evidence

- Decompiled: local/re-out/uc/SBase/SBasePawn.uc (Tick, PostNetBeginPlay, EyePosition, CrushedBy, KilledBy,
  EncroachedBy, BaseChange, TakeDamage, TakeFallingDamage, ShouldCrouch, AnimEnd, RMColOn), SBase/SPawnAttaque.uc,
  SBase/SPAWNDEFENSE.uc, SBase/SPawnAttaque_1Mesh.uc, SBase/SPawnDefense_1Mesh.uc, Engine/Pawn.uc.
- Natives: NativeTick 0x10ae3c20, TickNative 0x58dbfb90 (Reloaded.Core), MyCheckBob 0x10abdfc0,
  InitializeBasicActions 0x10ae4720.

## Open questions

2. NativeTick: everything else it does on the owning and server sides (IK, and anything beyond the proxy smoothing
   and action playback, which are decoded). Ghidra 0x10ae3c20.

### Resolved

1. TickNative as patched by Reloaded.Core. Resolved for movement by reading the DLL: no movement; the glass effect
   part is shared by stock and Reloaded, Reloaded can ignore the fast-movement condition and clamps the jitter to
   1..254. Rule "Per tick" 2. Evidence: reloaded-hooks.md rule 11. (Health regeneration: combat/health.md.)
2. (part) NativeTick on proxies. Resolved: the proxy smoothing (error removed at up to 200 uu/s walking, ramping
   over 1 s, or 400 uu/s; snap over 6 x collision radius or on a physics change) and the action playback (the new
   batch in order when its time is newer than the last played). Evidence: net-proxy.md rules 8, 13 (ANSWERS-net
   proxy-pawns 2, 3).
3. Which callback raises CrushedBy. Resolved: the native CheckEncroachment (mask 0x9f) only lists the overlapped
   actors and asks the mover; the crush-type mover's script EncroachingOn calls CrushedBy. The SteeE duct is
   Mover2064 with MoverEncroachType Crush (devices/mover.md). Evidence: collision-movement.md (ANSWERS-physics 29).
