# ExecuteAction: the per-action native

Units:
- nat:SBase.SBasePawn.ExecuteAction
- rl:SBase.SBasePawn.ExecuteAction
- eng:ASBasePawn::ExecuteAction

## Purpose

`SBasePawn.ExecuteAction(int Action)` (native 1411) is how every movement, combat and gadget action reaches the
engine. The script decides which `SA_*` action to play and when; the native switch then sets the physics mode,
collision, crouch state, IK, root-motion lock and end-of-move correction, the animation set used by the walk
blender, and plays the clips on the animation channels. Most physics mode changes in the game happen here (the
others are the physics natives' own transitions, see physics-dispatch.md).

This file lists the 329 action values (0 to 328) case by case. Animation semantics (channels, PlayAnim arguments,
blending, root-motion lock types) are in anim.md.

## Data

| Item | Value |
|---|---|
| Native | 1411, `ASBasePawn::execExecuteAction`: editor 0x1121eb10, game 0x10ad6800 |
| Body | editor 0x11215980 (jump table 0x1121d618, 329 entries, exit 0x1121d5bd); game 0x10ad6840 (this in ECX, Action on the stack) |
| Reloaded | wrapper 0x10023740 in Reloaded.Core around the native thunk (rule 6) |
| Action names | the `SA_*` constants of SBasePawn (0 to 328; 260 to 267, 270, 298 and 299 are not defined) |

Fields written (SBasePawn unless noted): Physics (through setPhysics), iLockRootMotionType,
RootMotionAdditionnalPhysics, bUseRootMotionAdditionnalPhysics (Actor), vMoveAfterRM, rRotAfterRM, vSpeedAfterRM,
IK_Type, IK_TypeBeforeDamage, BTW, OldBTW, ZoneConfig (through the crouch and pipe helpers), PipHFire, TypeLedge, the walk blender set (ForwardAnim,
RightAnim, LeftAnim, RunAnim, TurnAnim, fBlendCoeffWalkAnim, fBlendCoeffRunAnim, fWalkSpeedLimit, fRunSpeedLimit),
BlendChangeTime, ChannelForBlendWithC0, fBlendTimeWithC0, fPhysReposLinearSpeed, StrkType, fOldFrame,
m_AttackTarget, m_AttackedBy, iNbAmmoToReload, Acceleration, Velocity, ClientSmoothFlags and many bools.

No case starts a script timer. The time-valued fields an action writes are BlendChangeTime (the walk blender's step
time), fBlendTimeWithC0 (PlayAnimWithBlend), fTimeOfFeetLedge (171), fPhysReposLinearSpeed (the repositioning
speed) and, through AnimateStandUp, iLastWakeTime; the script times its own follow-ups from AnimEnd (anim.md).

## Notation used in the tables

- **Phys**: the mode passed to setPhysics, by number and short name (None 0, Walking 1, Falling 2, RootMotion 5,
  OnPipe 6, Linear 7, OnVerticalPlane 8, Ledge 9, Vandamme 10, Repos 11, OnLadder 13, OnPipeH 14). "-" when the
  case does not call setPhysics. setPhysics is the SBasePawn override (physics-dispatch.md), so a change of mode
  also sets bActionsSendByServerMove and clears bKeepLocalVandammePos; setting RootMotion while already in
  RootMotion only sets bActionsSendByServerMove.
- **RM**: root motion. `L7` = iLockRootMotionType 7; `+W` = bUseRootMotionAdditionnalPhysics set and
  RootMotionAdditionnalPhysics = Walking (1) (`+N` None, `+F` Falling, `+P` OnPipe ...); `-add` = the bool cleared;
  `end0` = vMoveAfterRM = 0 and rRotAfterRM = 0 (EndRootMotion then only applies vSpeedAfterRM); `end(v, r)` = a
  computed end correction.
- **Anim**: `cN Clip` = PlayAnim on channel N with rate 1, tween 0, no loop; extra arguments follow in brackets:
  `r1.5` rate, `t0.1` tween, `loop`, `back` (bBackward), `cont` (bContinueAtFrame), `x` (bMirror: the clip is mirrored through Pawn.BoneMirrors, anim.md).
  `A<Field>` means the clip name is read from that SBasePawn name field (per-class animation set). `blend cN a
  @Bone` = AnimBlendParams(N, alpha a, in 0, out 0, bone); `to cN a/t` = AnimBlendToAlpha(N, a, t); `rate0 cN` =
  the channel's rate scale set to 0 (frozen); `wblend(clip, cN, t)` = PlayAnimWithBlend: PlayAnim on N then
  ChannelForBlendWithC0 = N and fBlendTimeWithC0 = t.
- **Col**: SetCollision(bCollideActors, bBlockActors, bBlockPlayers) as `col(1,1,1)`; `+world` / `-world` =
  bCollideWorld set or cleared.
- **Calls**: other natives and events: AnimateWalking(Action), AnimateStandUp, PlayLanding(Action), PlayDamage,
  PlayDeath, PlayJumpStart, MyDoJump, PlayRoll, PlayRollUp, Crouch, UnCrouch, SetZipSpeed, UnZipSpeed,
  SetColForPipe4, SetColAfterPipe4, ZeroMovementAlpha(from, to, t), bServerTestRootMotion and the script events
  (`ev:Name`). `X(other, n)` = ExecuteAction(n) run on another pawn (the grab partner, the attacker).
- **Spec**: the behaviour spec that uses the action (movement/, combat/, equipment/, objectives/).

## Rules

### 1. Prologue (every call)

1. Remember the current BTW (back-to-wall state).
2. Build the rotation axes from Rotation (X and Y axes are used by the cases that compute an end offset).
3. BlendChangeTime = 0.25; fBlendTimeWithC0 = 0.
4. Return at once when the pawn has no mesh or the mesh is not a skeletal mesh (nothing below runs).
5. Make sure the mesh instance exists and use it for the channel calls.

### 2. Epilogue (every case that does not return early)

If BTW was 0 at entry and is non-zero now, set the Pawn bool initSlowReplication.

### 3. Shared tails

Several cases end in one of these shared blocks after their own first statements:

- **crouch walk tail** (75, 76): ev:EvSetWeaponLoaded(0) only for action 75, set bCanWalkOffLedges, clear
  bWeaponTransit, ClientSmoothFlags = 7, blend c14 0 and rate0 c14, blend c15 0, Phys Walking, then
  AnimateWalking(Action) (rule 5.1); if bIsRolling, clear it and SetAnimSetAfterRolling (rule 5.3).
- **stand walk tail** (85, 86, 87): col(1,1,1), ev:EvSetWeaponLoaded(0) only for action 85, set
  bCanWalkOffLedges, BTW = 0, clear bWeaponTransit, ClientSmoothFlags = 7, IK_Type = 0, blend c14 0, rate0 c14,
  blend c15 0, Phys Walking, then as the crouch walk tail from AnimateWalking.
- **land tail high** (92, 93, 98, 99, 101): Phys Walking, Acceleration = Velocity = 0, blend c15 0,
  PlayLanding(Action) (rule 5.2).
- **land tail low** (94, 96, 100): blend c15 0, blend c14 0 @B Spine1, rate0 c14, PlayLanding(Action).
- **damage tail** (49 to 54, 57 to 65): if IK_TypeBeforeDamage is 1 it takes the current IK_Type; IK_Type = 1;
  PlayDamage(Action) (rule 5.5).
- **goggle head tail** (71, 72): c18 GoglStAlDn2 (no weapon loaded, or a merc) or GoglStSpDn2 (spy with a loaded
  weapon); blend c18 0 @B Head; to c18 1/0.3.
- **uncrouch tail** (78, 257): UnCrouch(false).

### 4. Cases

The tables below give each case. Cases not listed (5, 97, 275, 324 and the undefined values) jump straight to the
exit and only do the prologue.

#### Cases 0 to 19

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 0 | SA_NONE | 0 None | - | - | col(0,0,0) | movement/jump-fall-land.md |
| 1 | SA_NO_IK | - | - | - | IK_Type = 1 (IKT_NONE) | - |
| 2 | SA_BUMP_WALL | 5 RootMotion | L7 +W end0 | c0 AtakChAlEd2 | - | combat/melee-elbow-charge-berserk.md |
| 3 | SA_BUMP_ENEMY | - | - | c0 AtakStAlFd2 | - | combat/melee-elbow-charge-berserk.md |
| 4 | SA_CHARGING | 1 Walking | - | - (walk blender) | ForwardAnim = AtakStAlCh2, fBlendCoeffWalkAnim = 385 | combat/melee-elbow-charge-berserk.md |
| 6, 293 to 297 | SA_RELOAD, SA_RELOAD_1 to _5 | - | - | blend c21 1 @B Spine1; c21 ReloStSpFd2, or ReloStSpFd2S for a shotgun, ReloStSpFd2SU for an Uzi | iNbAmmoToReload = 6 for SA_RELOAD, 1 to 5 for SA_RELOAD_1 to _5; IK_Type = 1. The weapon class is MyWeapon (ASWeaponDefense_Shotgun / _Uzi), or without MyWeapon the native-only pointer at +0xb64 (ASWeaponDefenseDummy_Shotgun / _Uzi) | equipment/merc-rifle.md |
| 7 | SA_DROPATTACK_LAND | 1 Walking | - | c0 LandStAkDA0 (WeaponLoaded 0) or LandStAkDA2 | m_AttackTarget = None | combat/drop-attack.md |
| 8 | SA_ELBOW_ATTACK | - | - | blend c19 1 @B Spine1; c19 AtakStAlFd2 with a loaded weapon, else c19 AtakStAlFd0 r1.5 | - | combat/melee-elbow-charge-berserk.md |
| 9 | SA_ELBOW_ATTACK_MISSED | - | - | as 8 (same clips and rates) | - | combat/melee-elbow-charge-berserk.md |
| 10 | SA_ELBOW_ATTACK_GROUND | - | - | blend c19 1 (no bone); c19 AtakStAlDn0 r1.5 | - | combat/melee-elbow-charge-berserk.md |
| 11 | SA_STANDUP | 1 Walking | - | AnimateStandUp | clear bStillGrabbed, bCanBeTerminated, bSleeping; m_AttackedBy = m_AttackTarget = None; col(1,1,1), +world | combat/knockout-and-terminate.md |
| 12 | SA_REANIMATE | - | - | blend c16 1; c16 ReanStAlFd2 / ReanCrAlFd2 (WeaponLoaded 2, standing / crouched), else ReanStAlFd0 / ReanCrAlFd0 | - | combat/knockout-and-terminate.md |
| 13 | SA_HANDINTERACT | - | - | blend c24 1 @B Spine1; c24 ButtStAIFd2 (loaded) or ButtStAIFd0 | - | objectives/interaction-system.md |
| 14 | SA_DROPMINE_GRAB | - | - | blend c24 1 @B Spine1; c24 MineStAlBg0, crouched MineCrAlBg0 | - | equipment (mine) |
| 15 | SA_DROPMINE | - | - | blend c24 1 @B Spine1; c24 MineStAlEd0, crouched MineCrAlEd0 | - | equipment (mine) |
| 16 | SA_DROPMINE_UNGRAB | - | - | as 14 with back | - | equipment (mine) |
| 17 | SA_ELBOW_ATTACKED_GOBACK | 5 RootMotion | L5 +W end0 | c0 StrkStAlBk2, crouched StrkCrAlBk2 | IK_Type = 1; Acceleration = 0; with m_AttackedBy the pawn first turns in place (MoveActor with zero delta) to the yaw of (attacker - pawn) flattened, and ReceivedRotation = Rotation; m_AttackedBy = None | combat/melee-elbow-charge-berserk.md |
| 18 | SA_GRAB_CATCH | 1 Walking | - | c0 GrabStAlBg0 | m_PawnNoMoreGrabbed = m_AttackTarget; X(m_AttackTarget, 31 SA_GRABBED_CATCH); clear bCanWalkOffLedges, set bAvoidLedges and bStopAtLedges; IK_Type = 1 | combat/spy-grab.md |
| 19 | SA_GRAB_REPOS | 11 Repos | - | - | m_AttackTarget col(1,0,0) when set, then falls into 148 SA_REPOS (fPhysReposLinearSpeed = 180, clear bServerControlsRepos, set bUseAdditionnalPhysicsForRepos) | combat/spy-grab.md |

#### Cases 20 to 39 (grab, grabbed, palm)

Common to the grabber's release cases 21 to 26 and 28, 29: m_AttackTarget = None, clear bAvoidLedges and
bStopAtLedges, set bCanWalkOffLedges ("release" below).

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 20 | SA_GRAB | 1 Walking | - | AnimateWalking(20): grab walk set (ForwardAnim and RunAnim GrabStAlFd0, RightAnim and TurnAnim GrabStSpRt0, coefficients 57, speed limits 150000) and c0 GrabStAlNt0 loop (AnimateWalking, rule 5) | IK_Type = 1; with m_AttackTarget: target.m_AttackedBy = self and X(target, 32 SA_GRABBED) | combat/spy-grab.md |
| 21 | SA_GRAB_RELEASE | - | - | c0 GrabStAlXxb | X(m_PawnNoMoreGrabbed, 34 SA_GRABBED_RELEASE_KO); release | combat/spy-grab.md |
| 22 | SA_GRAB_RELEASE_GRABBED_LD | - | - | c0 GrabStAlxxa r4 | release (the partner is not told) | combat/spy-grab.md |
| 23 | SA_GRAB_RELEASE_KO | - | - | c0 GrabStAlKo0 | X(m_PawnNoMoreGrabbed, 34); release | combat/spy-grab.md |
| 24 | SA_GRAB_RELEASE_STRANGLE | - | - | c0 GrabStAlxxa | X(m_PawnNoMoreGrabbed, 35 SA_GRABBED_RELEASE_STRANGLE); release | combat/spy-grab.md |
| 25 | SA_GRAB_RELEASE_DMG | - | - | c0 GrabStAlXxC | the partner gets the grabbed-end restore (below); release | combat/spy-grab.md |
| 26 | SA_GRAB_RELEASE_GRABBED_DMGED | - | - | c0 GrabStAlxxa | X(m_PawnNoMoreGrabbed, 36 SA_GRABBED_RELEASE_DMG); release | combat/spy-grab.md |
| 27 | SA_GRAB_AVOIDED | 1 Walking | - | AnimateWalking(85) (standing unarmed set) | - | combat/spy-grab.md |
| 28 | SA_GRAB_RELEASE_BOTH_DIE | - | - | c0 GrabStAlXxC | X(m_PawnNoMoreGrabbed, 36); release | combat/spy-grab.md |
| 29 | SA_GRAB_DROPATTACKED | spy: 5 RootMotion (after 0 None when already in RootMotion); merc: unchanged | spy: L3 +N end0 | PlayDeath(2) | X(m_PawnNoMoreGrabbed, 327 SA_GRABBED_RELEASE_DROPTECH); release; col(0,0,0) | combat/spy-grab.md, combat/drop-attack.md |
| 30 | SA_GRABBED_AVOID | 1 Walking | - | AnimateWalking(86) (standing armed set); to c22 0/0.2 | col(1,1,1), +world; clear bActionsSendByServerMove | combat/spy-grab.md |
| 31 | SA_GRABBED_CATCH | 12 FollowParent | - | blend c24 0 @B Spine1; to c22 0/0.2; c0 GrbdStAlBg0 | IK_Type = 1; col(1,0,0), -world; clear bActionsSendByServerMove | combat/spy-grab.md |
| 32 | SA_GRABBED | 12 FollowParent (only with m_AttackedBy) | - | AnimateWalking(32): grabbed walk set (GrbdStAlFd0, GrbdStAlRt0, GrbdStAlLt0, coefficients 57) and c0 GrbdStAlNt0 loop | IK_Type = 1; nothing without m_AttackedBy | combat/spy-grab.md |
| 33 | SA_GRABBED_RELEASE | 1 Walking | - | c0 GrbdStAlXxa | set bStillGrabbed; clear bActionsSendByServerMove; m_AttackedBy = None; +world | combat/spy-grab.md |
| 34 | SA_GRABBED_RELEASE_KO | 1 Walking | - | c0 GrbdStAlKo0 | set bStillGrabbed, bSleeping; clear bActionsSendByServerMove; m_AttackedBy = None; col(1,0,0), +world | combat/knockout-and-terminate.md |
| 35, 36 | SA_GRABBED_RELEASE_STRANGLE, _DMG | 1 Walking | - | c0 GrbdStAlXxa | set bStillGrabbed; clear bActionsSendByServerMove; m_AttackedBy = None; col(1,0,0), +world | combat/spy-grab.md |
| 37 | SA_GRABBED_RELEASE_GRABBING_LD | 1 Walking | - | c0 GrbdStAlEd0 | set bStillGrabbed; m_AttackedBy = None; col(1,1,1), +world; clear bActionsSendByServerMove | combat/spy-grab.md |
| 38 | SA_PALM_USING | - | - | blend c22 0.1 @B Spine1; to c22 1/0.2; when c22 is not already animating: c22 PalmStAlNt0 / PalmCrAlNt0 (unarmed, standing / crouched) or PalmStAlNt2 / PalmCrAlNt2, r0.5 loop | IK_Type = 1 | equipment (palm) |
| 39 | SA_PALM_DESACTIVATE | - | - | to c22 0/0.4 | IK_Type = IK_Type_Previous | equipment (palm) |

Grabbed-end restore (case 25 on the partner, editor 0x111fa1f0): Phys Walking, clear bActionsSendByServerMove,
m_AttackedBy = None, col(1,1,1), +world, c0 GrbdStAlEd0 (case 37 without bStillGrabbed).

#### Cases 40 to 59 (death, cough, damage)

Common to the death cases 40 to 44 and 46 ("dying" below): m_AttackedBy = None, set bSleeping, col(1,0,0).
PlayDeath(d) is rule 5.4 (d: 0 in place, 1 forward, 2 backward, 3 left, 4 right, 5 drop attack).

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 40 | SA_DIE_FORWARD | spy: 5 RootMotion (after 0 None when already in RootMotion); merc: 1 Walking | spy: L3 +W end0 | PlayDeath(1) | dying | combat/knockout-and-terminate.md, combat/death-and-respawn.md |
| 41 | SA_DIE_BACKWARD | 0 None then 5 RootMotion (both classes) | L1 +W end0 | PlayDeath(2) | dying | combat/death-and-respawn.md |
| 42 | SA_DIE_LEFT | as 40 | as 40 | PlayDeath(3) | dying | combat/death-and-respawn.md |
| 43 | SA_DIE_RIGHT | as 40 | as 40 | PlayDeath(4) | dying | combat/death-and-respawn.md |
| 44 | SA_DIE_INPLACE | 1 Walking, then spy 5 RootMotion | spy: L3 +W end0 | PlayDeath(0) | dying | combat/death-and-respawn.md |
| 45 | SA_DIE_FALLING | 1 Walking | - | c0 LandStXxDn2 | m_AttackedBy = None; col(1,0,0) (bSleeping is not set) | combat/blast-and-fall-damage.md |
| 46 | SA_DIE_DROPATTACK | spy: 5 RootMotion (after 0 None when already in RootMotion); merc: 1 Walking | spy: L3 +W end0 | PlayDeath(5) first, then the physics change | dying | combat/drop-attack.md |
| 47 | SA_COUGHT_START | - | - | blend c20 1 @B Head; c20 CaughStSpFd2 loop | - | equipment/smoke-grenade.md |
| 48 | SA_COUGHT_STOP | - | - | to c20 0/0.2 | - | equipment/smoke-grenade.md |
| 49 to 52, 57 to 65 | SA_DMG_SPECIAL_LEFT, _RIGHT, _FRONT, _BACK, SA_DMG_HEAD, SA_DMG_CHEST_BACK, _FRONT, _LEFT, _RIGHT, SA_DMG_ARM_LEFT, _RIGHT, SA_DMG_LEG_LEFT, _RIGHT | - | - | PlayDamage(Action) (rule 5.5) | damage tail: IK_TypeBeforeDamage takes IK_Type when it is 1; IK_Type = 1 | combat/state-damage-reactions.md, combat/damage-pipeline.md |
| 53, 54 | SA_DMG_ELECTRIC, SA_DMG_ELECTRIC_NOANIMS | - | - | PlayDamage(Action) | ev:BeginStickySkin, set bStickyShocking, ev:evStickyShockSfx(1), then the damage tail | combat/shock-and-taser.md, equipment/sticky-shock-effect.md |
| 55 | SA_DMG_ELECTRIC_STOP | - | - | to c23 0/0.2 | IK_Type = IK_TypeBeforeDamage when IK_Type is 1; IK_TypeBeforeDamage = 1; clear bStickyShocking; ev:RestoreSkins; ev:evStickyShockSfx(0) | combat/shock-and-taser.md |
| 56 | SA_DMG_ELECTRIC_STOP_ANIM | - | - | to c23 0/0.2 | writes a log line with IK_Type and IK_TypeBeforeDamage, then the IK restore of 55 (no skin or sound events, bStickyShocking kept) | combat/shock-and-taser.md |

#### Cases 66 to 84 (goggles, binoculars, crouch walk, crouch transitions)

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 66 to 70, 269 | SA_VISION_EMF, _NIGHT, _THERMAL, _MOTION, SA_GOG_SWITCH_WITHOUTANIM, SA_GOG_DYNNOANIM | - | - | - (script) | ev:ExecuteActionForGoggle(Action) only | equipment/vision-goggles.md |
| 71 | SA_GOG_HEADMVT_ON | - | - | goggle head tail | - | equipment/vision-goggles.md |
| 72 | SA_GOG_HEADMVT_OFF | - | - | goggle head tail (the same clip and blend as 71) | ev:ExecuteActionForGoggle(72) first | equipment/vision-goggles.md |
| 73 | SA_BINOCULAR_USING | - | - | c22 WaitSpSpMix loop; blend c22 0 @B Spine1; to c22 1/0.2 | - | equipment/merc-scope.md |
| 74 | SA_BINOCULAR_END_USING | - | - | to c22 0/0.2 | - | equipment/merc-scope.md |
| 75 | SA_WALK_CR_0H | 1 Walking | - | crouch walk tail: AnimateWalking(75) crouched unarmed set | ev:EvSetWeaponLoaded(0) | movement/walking.md |
| 76 | SA_WALK_CR_2H | 1 Walking | - | crouch walk tail: AnimateWalking(76) crouched armed set | ev:EvSetWeaponLoaded(2) first | movement/walking.md |
| 77 | SA_WANTS_TO_CROUCH | - | - | - | Crouch(false) (rule 5.6) | movement/walking.md |
| 78 | SA_WANTS_TO_UNCROUCH | - | - | - | UnCrouch(false) (rule 5.6) | movement/walking.md |
| 79 | SA_WALK_CR_0Hto2H | - (AnimateWalking sets 1) | - | AnimateWalking(79) crouched armed set; blend c14 1 @B Spine1; c14 ADrawCrouch2H r2 | set bWeaponTransit | movement/walking.md |
| 80 | SA_WALK_CR_2Hto0H | - (1 by AnimateWalking) | - | AnimateWalking(80) crouched unarmed set; blend c14 1 @B Spine1; c14 AUnDrawCrouch2H | set bWeaponTransit | movement/walking.md |
| 81 | SA_WAIT_CR2ST_2H | 1 Walking | - | blend c15 0; AnimateWalking(81) standing armed set; c0 AWaitCrOut2 (overrides the wait loop) | after a roll (bIsRolling): clear it and SetAnimSetAfterRollingUp (rule 5.3) | movement/walking.md, movement/roll.md |
| 82 | SA_WAIT_ST2CR_2H | - (1 by AnimateWalking) | - | AnimateWalking(82) crouched armed set; c0 AWaitCrIn2 | - | movement/walking.md |
| 83 | SA_WAIT_ST2CR_0H | - (1 by AnimateWalking) | - | AnimateWalking(83) crouched unarmed set; c0 AWaitCrIn0 | - | movement/walking.md |
| 84 | SA_WAIT_CR2ST_0H | 1 Walking | - | blend c15 0; AnimateWalking(84) standing unarmed set; c0 AWaitCrOut0 | as 81 after a roll | movement/walking.md, movement/roll.md |

#### Cases 85 to 110 (stand walk, draw, jump, land, fall, split jump)

Common to the fall cases 103 to 106 ("fall reset" below): PipHFire = 0, clear bWeaponTransit, UnZipSpeed(false)
when bIsZipSpeed, Phys Falling, blend c15 0 unless rolling, blend c14 0 @B Spine1, rate0 c14.

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 85 | SA_WALK_ST_0H | 1 Walking | - | stand walk tail: AnimateWalking(85) standing unarmed set | ev:EvSetWeaponLoaded(0) | movement/walking.md |
| 86 | SA_WALK_ST_2H | 1 Walking | - | stand walk tail: AnimateWalking(86) standing armed set | clear bWeaponTransit, ev:EvSetWeaponLoaded(2) first | movement/walking.md |
| 87 | SA_RUN_ST_2H | 1 Walking | - | stand walk tail: AnimateWalking(87) armed jog set | as 86 | movement/walking.md |
| 88 | SA_WALK_ST_0Hto2H | - (1 by AnimateWalking) | - | AnimateWalking(88) standing armed set; blend c14 1 @B Spine1; spy only: c14 ADrawStand2H r2 | set bWeaponTransit | movement/walking.md |
| 89 | SA_WALK_ST_2Hto0H | - (1 by AnimateWalking) | - | AnimateWalking(89) standing unarmed set; blend c14 1 @B Spine1; c14 AUnDrawStand2H | set bWeaponTransit | movement/walking.md |
| 90, 91 | SA_JUMP_ST_0H, SA_JUMP_ST_2H | 2 Falling | - | PlayJumpStart(Action) (rule 5.7); blend c1 0 | ClientSmoothFlags = 7; when bQuickGadgetHasBegun: to c22 0/0.1, clear it, ev:NotifyQuickLaunch. The jump velocity is not set here (MyDoJump is script-called) | movement/jump-fall-land.md |
| 92, 93, 98, 99 | SA_LAND_DROPATTACK, SA_LAND_ST_0H_HIGH, SA_LAND_ST_0HTo2H_HIGH, SA_LAND_ST_2H_HIGH | 1 Walking | - | land tail high: PlayLanding(Action) (rule 5.2) | Acceleration = Velocity = 0 | movement/jump-fall-land.md |
| 94, 100 | SA_LAND_ST_0H_LOW, SA_LAND_ST_2H_LOW | 1 Walking (in PlayLanding) | - | land tail low: PlayLanding(Action) | velocity kept | movement/jump-fall-land.md |
| 95, 102 | SA_LAND_CR_0H_QUIET, SA_LAND_CR_2H_QUIET | 1 Walking (in PlayLanding) | - | blend c15 0; PlayLanding(Action) | Crouch(false) first | movement/jump-fall-land.md |
| 96 | SA_WALK_ST_LAND_OH | 1 Walking (in PlayLanding) | - | land tail low; PlayLanding(96) plays c1 JoggStRpFd0 and blend c1 1 (no channel 0 clip) | set bNoRunAnimation | movement/jump-fall-land.md |
| 101 | SA_LAND_INERTE | 1 Walking, then 5 RootMotion in PlayLanding | L1 +W end0 (in PlayLanding) | land tail high: PlayLanding(101) switches to root motion and plays c0 LandStXxBk2 t0.083 | col(1,0,0); Acceleration = Velocity = 0 | movement/jump-fall-land.md, combat/knockout-and-terminate.md |
| 103, 105, 119, 125, 134 | SA_FALL_ST_0H, SA_FALL_ST_2H, SA_LEDGEFALL, SA_VANDAMMEFALL, SA_ZIPFALL | 2 Falling | - | c0 AFall t0.2 (WeaponLoaded 0) or AFall2H t0.2 | clear bBeginSpecial12Anim; IK_Type = 0; fall reset | movement/jump-fall-land.md |
| 104 | SA_LONG_FALL_ST_0H | 2 Falling | - | c0 AFall t0.35 or AFall2H t0.35 | fall reset; Velocity = vSpeedAfterRM when that is non-zero (before the mode change) | movement/wall-jump-split-vandamme.md |
| 106 | SA_FALL_INERTE | 2 Falling | - | c0 FallStXxBk2 t0.2 | col(1,0,0); clear bBeginSpecial12Anim; IK_Type = 1; SetColAfterPipe4(false); fall reset | movement/jump-fall-land.md, combat/knockout-and-terminate.md |
| 107 | SA_FALL_DEAD | - | - | c0 FallStAlFr0 loop | - | combat/death-and-respawn.md |
| 108 | SA_DBL_JUMP_R | 0 None | - | c0 SpltStAlRt0 t0.1 | Velocity = 0 (the split hold) | movement/wall-jump-split-vandamme.md |
| 109 | SA_DBL_JUMP_L | 0 None | - | c0 SpltStAlLt0 t0.1 | Velocity = 0 | movement/wall-jump-split-vandamme.md |
| 110 | SA_DBL_JUMP_IMP | 2 Falling | - | - | MyDoJump (rule 5.8): in Falling with nJumpNumber 1 this is the second jump, Velocity = 250 vWallJumpDir with Z = 700, nJumpNumber = 2 | movement/wall-jump-split-vandamme.md |

#### Cases 111 to 142 (ledge, Vandamme, fence, roll, zip line)

Offsets in `end(...)` are in pawn space at the time of the action: X forward and Y right (the yaw axes of the
prologue, flattened), Z up. rRotAfterRM is added to the start yaw (16384 = 90 degrees to the right).

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 111 | SA_LEDGE_GRAB | 9 Ledge | - | blend c15, c7, c8 0; c0 LedgStAlBg0 r2 t0.08 | clear bBeginSpecial12Anim; TypeLedge = 0 | movement/ledge.md |
| 112 | SA_ONLEDGE | 9 Ledge | - | blend c15 0; c0 LedgStAlNt0 loop; shimmy set: RightAnim LedgStAlRt0, LeftAnim LedgStAlLt0, fBlendCoeffWalkAnim 80 | BlendChangeTime = 0.15; set bBeginSpecial12Anim | movement/ledge.md |
| 113 | SA_TOPLEDGE | 5 RootMotion through bServerTestRootMotion | L3 -add end(57.8 X + 210.8 Z, 0) | c0 LedgStAlUp0 t0.15, only when bServerTestRootMotion(113) passes | clear bBeginSpecial12Anim; Acceleration = 0. On the authority bServerTestRootMotion re-runs TestTopLedge for this action and, when the pawn cannot stand, raises ClientStopRM(113) and plays nothing (physics-rootmotion-repositioning.md) | movement/ledge.md |
| 114 | SA_TOPLEDGECROUCH | 5 RootMotion | L3 -add end(75.7 X + 206.1 Z, 0) | c0 LedgCrAlUp0 t0.15 | clear bBeginSpecial12Anim; Acceleration = 0 | movement/ledge.md |
| 115 | SA_CHANGELEDGE_IR | 5 RootMotion | L3 -add end(0.01 Z, +16384) | c0 LedgStAlTiL t0.2 x (mirrored, anim.md) | clear bBeginSpecial12Anim; Acceleration = 0 | movement/ledge.md |
| 116 | SA_CHANGELEDGE_IL | 5 RootMotion | L3 -add end(0.01 Z, -16384) | c0 LedgStAlTiL t0.2 | as 115 | movement/ledge.md |
| 117 | SA_CHANGELEDGE_OR | 5 RootMotion | L3 -add end(75 X + 75 Y, -16384) | c0 LedgStAlToL t0.2 x | as 115 | movement/ledge.md |
| 118 | SA_CHANGELEDGE_OL | 5 RootMotion | L3 -add end(75 X - 75 Y, +16384) | c0 LedgStAlToL t0.2 | as 115 | movement/ledge.md |
| 119 | SA_LEDGEFALL | 2 Falling | - | as 103 | as 103 | movement/ledge.md |
| 120 | SA_VANDAMME_LT | 10 Vandamme | - | blend c15 0; c0 SpltStAlBgL | ClientSmoothFlags = 1 | movement/wall-jump-split-vandamme.md |
| 121 | SA_VANDAMME_RT | 10 Vandamme | - | blend c15 0; c0 SpltStAlBgR | ClientSmoothFlags = 1 | movement/wall-jump-split-vandamme.md |
| 122 | SA_VANDAMME_NT | 10 Vandamme | - | blend c15 0; c0 SpltStAlNt0 loop; aim set: RunAnim SpltStAlUL0, ForwardAnim SpltStAlUR0, LeftAnim SpltStAlDL0, RightAnim SpltStAlDR0 | clear bControlArmForVandamme; IK_Type = 5 (IKT_JCVD) | movement/wall-jump-split-vandamme.md |
| 123 | SA_VANDAMME_2H | 10 Vandamme | - | c14 SpltStSpBg1; blend c14 0 | set bWeaponTransit | movement/wall-jump-split-vandamme.md |
| 124 | SA_VANDAMME_0H | 10 Vandamme | - | c14 SpltStSpBg1 back; blend c14 0 | clear bControlArmForVandamme; set bWeaponTransit | movement/wall-jump-split-vandamme.md |
| 125 | SA_VANDAMMEFALL | 2 Falling | - | as 103 | as 103 | movement/wall-jump-split-vandamme.md |
| 126 | SA_FENCEGRAB | 8 OnVerticalPlane | - | c0 FencStAlIO0 t0.1 | Acceleration = Velocity = 0; the quick-gadget cancel of 90 | movement/fence.md |
| 127 | SA_FENCEGRAB_JUMP | 8 OnVerticalPlane | - | c0 FencStAlBg0 t0.1 | Acceleration = Velocity = 0 | movement/fence.md |
| 128 | SA_ONFENCE | 8 OnVerticalPlane | - | c0 FencStAlNt0 loop; climb set: RightAnim FencStAlRt0, ForwardAnim FencStAlUp0, fBlendCoeffWalkAnim 120, fWalkSpeedLimit and fRunSpeedLimit 1000 | - | movement/fence.md |
| 129 | SA_TOPFENCE | 5 RootMotion | L3 -add end(75 X - 3.25 Z, +32768) | c0 FencStAlCo0 r1.5 t0.1 | Acceleration = 0 | movement/fence.md |
| 130 | SA_ROLLING | 1 Walking | - | PlayRoll (rule 5.9): wblend(RollStAlFdR or RollStAlFdL by the run cycle phase, c15, 0.1) | set bIsRolling | movement/roll.md |
| 131 | SA_ROLLINGUP | 1 Walking | - | PlayRollUp: wblend(RollStJpFdR or RollStJpFdL, c15, 0.1) | set bIsRolling | movement/roll.md |
| 132 | SA_ROLL_LAND | 1 Walking | - | - | - | movement/roll.md |
| 133 | SA_ZIP_GRAB | from a vertical pipe (Physics OnPipe): 5 RootMotion; otherwise 7 Linear | pipe: L3 -add (RootMotionAdditionnalPhysics 0), end(-14 X + 6 Y + 4 Z, +32768) | not on a pipe: c0 ZippStAlBg0 t0.1; from a pipe: c0 PipVSZilBhL t0.1 (bIsLeftPart) or PipVSZilBhR t0.1 | not on a pipe: Velocity.Z = 0. From a pipe: ClientSmoothFlags = 0, IK_Type = 1 | movement/zip-line.md |
| 134 | SA_ZIPFALL | 2 Falling | - | as 103 | as 103 (UnZipSpeed when in the speed pose) | movement/zip-line.md |
| 135 | SA_ZIP_WALL | 7 Linear | - | c0 ZippCrAlBl0 t0.1 | UnZipSpeed(false) | movement/zip-line.md |
| 136 | SA_ON_ZIP | 7 Linear | - | coming from root motion (OldPhysics 5): c0 PipHStAlNt0 then at once c0 ZippStAlDn0 loop (the second call replaces the first in the same frame); otherwise c0 ZippStAlDn0 loop t0.2 | - | movement/zip-line.md |
| 137 | SA_ON_ZIP_SPEED | 7 Linear | - | c0 ZippCrAlBg0 | SetZipSpeed (rule 5.10) | movement/zip-line.md |
| 138 | SA_ON_ZIP_END_TRSPEED | 7 Linear | - | c0 ZippCrAlDn0 loop | - | movement/zip-line.md |
| 139 | SA_ON_ZIP_NORMAL | 7 Linear | - | c0 ZippCrAlEd0 | UnZipSpeed(false) | movement/zip-line.md |
| 140 | SA_ON_ZIP_SLOW2NORMAL | 7 Linear | - | c0 ZippStAlSN0 back | - | movement/zip-line.md |
| 141 | SA_ON_ZIP_SLOW | 7 Linear | - | c0 ZippStAlSN0 | - | movement/zip-line.md |
| 142 | SA_ON_ZIP_END_TRSLOW | 7 Linear | - | c0 ZippStAlSw0 loop | - | movement/zip-line.md |

#### Cases 143 to 160 (vertical pipe, repositioning, ladder)

"Hold set" below: set bIsStopped, bCanStop and bBeginSpecial12Anim, fOldFrame = 0, then the climb blend
(ForwardAnim and fBlendCoeffWalkAnim), the hold loop on c0, and Velocity = 0. The `L` clips are authored for the
left hand; `x` plays them mirrored for the right (anim.md).

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 143 | SA_PIPE_GRAB | 6 OnPipe | - | c0 PipVStAlIOR r1.5 t0.1 | the quick-gadget cancel of 90 first | movement/pipe-vertical.md |
| 144 | SA_PIPE_GRABFALL | 6 OnPipe | - | c0 PipVStAlBg0 r1.8 t0.1 | - | movement/pipe-vertical.md |
| 145 | SA_ON_PIPE | 6 OnPipe | - | hold set: ForwardAnim PipVStAlUpRL, coefficient 131, c0 PipVStAlNtL loop, x when not bIsLeftPart, t0.2 (t0 when OldPhysics is RootMotion) | bIsLeftPart = bInvertNextLeftPart, then clear bInvertNextLeftPart; first calls a level-side hook (an object at XLevel +0xf0, +0x34, virtual slot 0x7c; purpose not decoded) | movement/pipe-vertical.md |
| 146 | SA_TOPPIPE | 5 RootMotion | L1 -add end(79.4 X + 45 Y + 191.5 Z, 0) | c0 PipVStAlEuR r1.5 t0.1, or PipVStAlEuL with bIsLeftPart | Acceleration = 0 | movement/pipe-vertical.md |
| 147 | SA_SPEEDPIPE | 6 OnPipe | - | c0 PipVStAlDn0 loop t0.2 | clear bBeginSpecial12Anim | movement/pipe-vertical.md |
| 148 | SA_REPOS | 11 Repos | - | - | fPhysReposLinearSpeed = 180; clear bServerControlsRepos; set bUseAdditionnalPhysicsForRepos | movement/native-contracts.md |
| 149 | SA_DOWN_TOPPIPE | 5 RootMotion | L3 -add; standing end(96.3 X + 38.67 Y - 181.7 Z, +32768), crouched end(82.7 X + 35 Y - 179.6 Z, +32768) | standing c0 PipVStAlBuL r1.5 t0.1; crouched UnCrouch then c0 PipVCrAlBuL r1.5 t0.1 | Acceleration = 0 | movement/pipe-vertical.md |
| 150 | SA_DOWN_TOPPIPE2 | 5 RootMotion | L3 -add; standing end(97.3 X - 39.17 Y - 186.15 Z, +32768), crouched end(82.7 X - 35 Y - 179.6 Z, +32768) | standing c0 PipVStAlBuR r1.5 t0.1; crouched UnCrouch then c0 PipVCrAlBuR r1.5 t0.1 | Acceleration = 0 | movement/pipe-vertical.md |
| 151 | SA_ON_PIPE2 | 6 OnPipe | - | hold set as 145, t0, x | clear bIsLeftPart | movement/pipe-vertical.md |
| 152 | SA_ON_PIPE2L | 6 OnPipe | - | hold set as 145, t0, no x | set bIsLeftPart | movement/pipe-vertical.md |
| 153 | SA_LADDER_GRAB | 13 OnLadder | - | c0 LaddStAlIOR r1.5, t0.2 crouched or t0.1 standing | quick-gadget cancel; ClientSmoothFlags = 1 | movement/ladder.md |
| 154 | SA_LADDER_GRABW | 13 OnLadder | - | c0 LaddStSpIOL | ClientSmoothFlags = 1 | movement/ladder.md |
| 155 | SA_ON_LADDER | 13 OnLadder | - | hold set: ForwardAnim LaddStAlUpLR, coefficient 128, c0 LaddStAlNtL loop t0.2 x | clear bIsLeftPart | movement/ladder.md |
| 156 | SA_ON_LADDERW | 13 OnLadder | - | hold set as 155, no x | set bIsLeftPart | movement/ladder.md |
| 157 | SA_LADDER_TOTOP_R | 13 OnLadder | - | c0 LaddStAlEuR | clear bBeginSpecial12Anim | movement/ladder.md |
| 158 | SA_LADDER_TOTOP_L | 13 OnLadder | - | c0 LaddStAlEuL | clear bBeginSpecial12Anim | movement/ladder.md |
| 159 | SA_ON_LADDERTOP | 13 OnLadder | - | c0 LaddStAlNt0 loop | - | movement/ladder.md |
| 160 | SA_LADDER_TOP | 5 RootMotion | L1 -add; spy end(73.9 X + 192 Z, 0), merc end(68.8 X + 196 Z, 0) | spy c0 LaddStAlEu0 r1.5; merc c0 LaddStAlEu2 | Acceleration = 0 | movement/ladder.md |

#### Cases 161 to 180 (ladder exits, feet and waist ledges, back to wall)

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 161 | SA_FAST_REPOS | 11 Repos | - | - | fPhysReposLinearSpeed = 300; clear bServerControlsRepos; set bUseAdditionnalPhysicsForRepos | movement/ladder.md, movement/native-contracts.md |
| 162 | SA_BACK_LADDER_TOP | 13 OnLadder | - | c0 LaddStAlEuR back | - | movement/ladder.md |
| 163 | SA_LADDER_DOWN_FROM_TOP | 5 RootMotion | L3 -add end(0, +32768) | standing: spy c0 LaddStAlBu0 r1.5, merc c0 LaddStSpBu2; crouched: UnCrouch then spy LaddCrAlBu0 r1.5, merc LaddCrSpBu2 | ClientSmoothFlags = 1; Acceleration = 0 | movement/ladder.md |
| 164 | SA_SPEED_LADDER_FROM_TOP | 13 OnLadder | - | c0 LaddStAlDn0 loop t0.2 | clear bBeginSpecial12Anim | movement/ladder.md |
| 165 | SA_LADDER_DOWNW | 5 RootMotion | L3 -add end(0, +32768) | c0 LaddStEdIOL (bIsLeftPart) or LaddStEdIOR | - | movement/ladder.md |
| 166 | SA_SPEEDLADDER | 13 OnLadder | - | c0 LaddStAlDn0 loop t0.2 | clear bBeginSpecial12Anim | movement/ladder.md |
| 167 | SA_ON_STOPLADDER | 13 OnLadder | - | c0 LaddStAlNt0 loop t0.1 | - | movement/ladder.md |
| 168 | SA_LADDER_UP2 | 13 OnLadder | - | c0 LaddStAlBgR | clear bBeginSpecial12Anim | movement/ladder.md |
| 169 | SA_LADDER_GRABFALL | 13 OnLadder | - | c0 LaddStAlBg0 t0.1 | - | movement/ladder.md |
| 170 | SA_LADDER_CATCH | 13 OnLadder | - | c0 LaddStAlCt0 t0.1 | - | movement/ladder.md |
| 171 | SA_FEETLEDGE | 5 RootMotion | L3 -add; left end(112.4 X + 39 Z, 0), right end(95.4 X + 39 Z, 0) | c0 LedgStAlFtL t0.1 with bIsLeftPart, else c0 LedgStAlFtR t0.1 | bIsLeftPart is flipped first when Velocity.Z < -100; fTimeOfFeetLedge = Level.TimeSeconds | movement/barrier-and-low-ledges.md |
| 172 | SA_FEETLEDGE_DETECT | 9 Ledge | - | blend c15 0 | TypeLedge = 3; quick-gadget cancel | movement/barrier-and-low-ledges.md |
| 173 | SA_WAISTLEDGE | 5 RootMotion | L3 -add end(56 X + 114.5 Z, 0) | c0 LedgStAlQk0 r1.3 | - | movement/barrier-and-low-ledges.md |
| 174 | SA_WAISTLEDGE_CR | 5 RootMotion | L3 -add end(77 X + 136.7 Z, 0) | c0 LedgCrAlQk0 r1.2 | - | movement/barrier-and-low-ledges.md |
| 175 | SA_WAISTLEDGE_DETECT | 9 Ledge | - | blend c15 0; c0 LedgStAlBQ0 r50 t0.08 (jumps to the clip end, a pose) | TypeLedge = 1 | movement/barrier-and-low-ledges.md |
| 176 | SA_BTW_ENTER | 1 Walking | - | c0 BackStAlBg0 t0.05, crouched BackCrAlBg0 | ClientSmoothFlags = 0 | movement/back-to-wall.md |
| 177 | SA_BTW_ENTERBACK | 1 Walking | - | c0 BackStAlEd0 back, crouched BackCrAlEd0 back | ClientSmoothFlags = 0 | movement/back-to-wall.md |
| 178 | SA_BTW_END | 1 Walking | - | c0 BackStAlEd0 t0.05, crouched BackCrAlEd0 t0.05 | - | movement/back-to-wall.md |
| 179 | SA_BTW_CORNER | 1 Walking | - | c0 BackStAlTr0 t0.4, crouched BackCrAlTr0 t0.4 | BlendChangeTime = 20 | movement/back-to-wall.md |
| 180 | SA_BTW_CORNEROFF | 1 Walking | - | ev:AnimateBTW (script sets the BTW clips) | BlendChangeTime = 0.2 | movement/back-to-wall.md |

#### Cases 181 to 205 (back to wall, computer and hack, after-fall walk, barrier)

BTW values (BTW_ enum): 1 BTWStand, 2 BTWCrouch, 3 BTWStandLeft, 4 BTWStandRight, 5 BTWCrouchLeft,
6 BTWCrouchRight, 7 BTWFireStandLeft, 8 BTWFireStandRight, 9 BTWFireCrouchLeft, 10 BTWFireCrouchRight. "BTW set"
below = Phys Walking, BTW = the value, IK_Type = 3 (IKT_BTW), ev:AnimateBTW. The "fire transition" cases set
bWeaponTransit, blend c14 1 and play the clip on c14.

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 181 | SA_BTW_LEFT | 1 Walking | - | ev:AnimateBTW | BTW set: 3, crouched 5 | movement/back-to-wall.md |
| 182 | SA_BTW_RIGHT | 1 Walking | - | ev:AnimateBTW | BTW set: 4, crouched 6 | movement/back-to-wall.md |
| 183 | SA_BTW_FIRE_ST_LEFT_TR | 1 Walking | - | fire transition: c14 BackStSpBgL | - | movement/back-to-wall.md |
| 184 | SA_BTW_FIRE_LEFT | 1 Walking | - | ev:AnimateBTW | BTW set: 7, crouched 9 | movement/back-to-wall.md |
| 185 | SA_BTW_FIRE_ST_RIGHT_TR | 1 Walking | - | fire transition: c14 BackStSpBgR | - | movement/back-to-wall.md |
| 186 | SA_BTW_FIRE_RIGHT | 1 Walking | - | ev:AnimateBTW | BTW set: 8, crouched 10 | movement/back-to-wall.md |
| 187 | SA_BTW_FIRE_ST_LEFT_UNTR | 1 Walking | - | fire transition: c14 BackStSpBgL back | - | movement/back-to-wall.md |
| 188 | SA_BTW_FIRE_ST_RIGHT_UNTR | 1 Walking | - | fire transition: c14 BackStSpBgR back | - | movement/back-to-wall.md |
| 189 | SA_BTW_FIRE_CR_LEFT_TR | 1 Walking | - | fire transition: c14 BackCrSpBgL | - | movement/back-to-wall.md |
| 190 | SA_BTW_FIRE_CR_RIGHT_TR | 1 Walking | - | fire transition: c14 BackCrSpBgR | - | movement/back-to-wall.md |
| 191 | SA_BTW_FIRE_CR_LEFT_UNTR | 1 Walking | - | fire transition: c14 BackCrSpBgL back | - | movement/back-to-wall.md |
| 192 | SA_BTW_FIRE_CR_RIGHT_UNTR | 1 Walking | - | fire transition: c14 BackCrSpBgR back | - | movement/back-to-wall.md |
| 193 | SA_BTW_CR | 1 Walking | - | ev:AnimateBTW | BTW set: 2 | movement/back-to-wall.md |
| 194 | SA_BTW_ST | 1 Walking | - | ev:AnimateBTW | BTW set: 1 | movement/back-to-wall.md |
| 195 | SA_BTW_OUT | 1 Walking | - | blend c14 0; rate0 c14 | ev:RemoveBTWArmControl; OldBTW = BTW; BTW = 0; IK_Type = 0; clear bWeaponTransit | movement/back-to-wall.md |
| 196 | SA_COMPUTER_ENTER | 1 Walking | - | c0 KbrdStAlBg0 t0.1 | IK_Type = 1; quick-gadget cancel | objectives/terminal-hack.md |
| 197 | SA_COMPUTER_ENTERBACK | 1 Walking | - | c0 KbrdStAlBg0 back | - | objectives/terminal-hack.md |
| 198 | SA_COMPUTER_LOOP | 1 Walking | - | c0 KbrdStAlNt0 loop | - | objectives/terminal-hack.md |
| 199 | SA_HACK_ENTER | 1 Walking | - | blend c24 0; c0 HackStAlBg0 t0.1 | IK_Type = 1; quick-gadget cancel | objectives/switch-hack.md |
| 200 | SA_HACK_ENTERBACK | 1 Walking | - | c0 HackStAlBg0 back | - | objectives/switch-hack.md |
| 201 | SA_WALK_ST_0H_AFTERFL | 1 Walking | - | blend c14 0, rate0 c14, blend c15 0; AnimateWalking(85); SetAnimSetAfterRolling | Velocity = RunningSpeed along the facing direction (Rotation as a vector) | movement/jump-fall-land.md |
| 202 | SA_WALK_ST_2H_AFTERFL | 1 Walking | - | blend c14 0, rate0 c14, blend c15 0; AnimateWalking(86) | velocity kept | movement/jump-fall-land.md |
| 203 | SA_BARRIER_JUMP | 5 RootMotion | L3 -add end(140 X + 20 Y + 12.8 Z, 0) | c0 JumpStAlBr0 t0.1 | - | movement/barrier-and-low-ledges.md |
| 204 | SA_BARRIER_JUMP_DETECT | 9 Ledge | - | blend c15 0; c0 JumpStAlBr0 t0.1 | TypeLedge = 2; quick-gadget cancel | movement/barrier-and-low-ledges.md |
| 205 | SA_FALL_AFTERBARRIER | 2 Falling | - | c0 JumpStAlBf0 | Velocity = 200 X with Z = -162 | movement/barrier-and-low-ledges.md |

#### Cases 206 to 225 (horizontal pipe, pipe transitions, ledge barrier)

PipHFire values: 0 NOPipH, 1 NoPipHFire, 2 PipH4, 3 PipH2, 4 PipH4NoFire. TypeLedge values (cases 111, 172, 175,
204): 0 LEDGE_NORMAL, 1 LEDGE_WAIST, 2 LEDGE_BARRIER, 3 LEDGE_FEET. `zero c2-13` = ZeroMovementAlpha(2, 14,
10000): every walk-blend channel 2 to 13 with a non-zero alpha drops to 0 at once; when all were already 0 their
rate scales and frames are reset to 0 (rule 5.11).

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 206 | SA_PIPH_GRAB | 14 OnPipeH | - | c0 PipHstAlBg0 t0.1 | PipHFire = 1; IK_Type = 4 (IKT_PIPH) | movement/pipe-horizontal.md |
| 207 | SA_ON_PIPEH | 14 OnPipeH | - | hands set: ForwardAnim PipHStAlFdRL, fBlendCoeffWalkAnim 126; c0 PipHStAlNt0 loop | PipHFire = 1 | movement/pipe-horizontal.md |
| 208 | SA_PIPH_UP4 | 14 OnPipeH | - | zero c2-13; c0 PipHPrAlBg0 | PipHFire = 1; SetColForPipe4 (rule 5.10) | movement/pipe-horizontal.md |
| 209 | SA_ON_PIPEH_4 | 14 OnPipeH | - | legs set: ForwardAnim PipHPrAlFd0, fBlendCoeffWalkAnim 113; c0 PipHPrAlNt0 loop | PipHFire = 4 | movement/pipe-horizontal.md |
| 210 | SA_PIPH_TURN | 14 OnPipeH | - | zero c2-13; c0 PipHStAlTr0 | PipHFire = 1 | movement/pipe-horizontal.md |
| 211 | SA_PIPH_DOWN2 | 14 OnPipeH | - | zero c2-13; c0 PipHPrAlEd0 | PipHFire = 1; SetColAfterPipe4(false) | movement/pipe-horizontal.md |
| 212 | SA_PIPH_TR_FIRE4 | 14 OnPipeH | - | c0 PipHStSpBg2 t0.1; aim set RunAnim PipHPrAlUL1, ForwardAnim PipHPrAlUR1, LeftAnim PipHPrAlDL1, RightAnim PipHPrAlDR1 | - | movement/pipe-horizontal.md |
| 213 | SA_PIPH_FIRE4 | 14 OnPipeH | - | the aim set of 212; c0 PipHStSpNt2 loop | PipHFire = 2 | movement/pipe-horizontal.md |
| 214 | SA_PIPH_FIRE_TR4 | 14 OnPipeH | - | c0 PipHStSpEd2 | PipHFire = 1 | movement/pipe-horizontal.md |
| 215 | SA_PIPH_TR_FIRE2 | 14 OnPipeH | - | c0 PipHStAlBg1 t0.1; aim set RunAnim PipHStAlUL1, ForwardAnim PipHStAlUR1, LeftAnim PipHStAlDL1, RightAnim PipHStAlDR1 | set bWeaponTransit | movement/pipe-horizontal.md |
| 216 | SA_PIPH_FIRE2 | 14 OnPipeH | - | the aim set of 215; c0 PipHStSpNt1 loop | clear bWeaponTransit; PipHFire = 3 | movement/pipe-horizontal.md |
| 217 | SA_PIPH_FIRE_TR2 | 14 OnPipeH | - | c0 PipHStAlEd1 | PipHFire = 1 | movement/pipe-horizontal.md |
| 218 | SA_PIPH_JUMP4 | 2 Falling | - | zero c2-13; c0 PipHStSpFl0 | SetColAfterPipe4(false); PipHFire = 0; IK_Type = 0 | movement/pipe-horizontal.md |
| 219 | SA_PIPH_JUMPFIRE4 | 2 Falling | - | zero c2-13; c0 PipHStSpFl2 r2 | PipHFire = 1; IK_Type = 0; set bWeaponTransit; SetColAfterPipe4(false) | movement/pipe-horizontal.md |
| 220 | SA_TR_PIPEV_H | 5 RootMotion | L3 -add end(-5.6 Z, +32768) | c0 PipVStAlBhR t0.1, or PipVStAlBhL with bIsLeftPart | ClientSmoothFlags = 0; IK_Type = 4 | movement/pipe-horizontal.md, movement/pipe-vertical.md |
| 221 | SA_TR_PIPEH_V | 5 RootMotion | L3 -add end(21.5 Z, 0) | c0 PipHStAlBV0 t0.1 | IK_Type = 1 | movement/pipe-horizontal.md |
| 222 | SA_TR_PIPEH_V_UP | 5 RootMotion | L3 -add end(86.35 X + 205 Z, +32768) | c0 PipHStAlBu0 t0.1 | IK_Type = 1 | movement/pipe-horizontal.md |
| 223 | SA_TR_PIPEV_H_DOWN | 5 RootMotion | L3 -add end(97.9 X - 207.2 Z, 0) | c0 PipVStAlBDL t0.1 with bIsLeftPart, else PipVStAlBDR | IK_Type = 4 | movement/pipe-vertical.md |
| 224 | SA_LEDGE_BARRIER | 5 RootMotion | L3 -add end(95 X + 121 Z, 0) | c0 LedgStAlRb0 t0.1 | - | movement/barrier-and-low-ledges.md |
| 225 | SA_FALL_AFTERLEDGEBARRIER | 2 Falling | - | c0 AFall t0.2 | Velocity = 200 X with Z = -162 | movement/barrier-and-low-ledges.md |

#### Cases 226 to 245 (pick up, throw, wall jump, blast, bomb)

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 226 | SA_PICKUP_CR_FD | - | - | c0 PickCrAlFd0 t0.1 | - | - |
| 227 | SA_PICKUP_CR_UP | - | - | c0 PickCrAlUp0 t0.1 | - | - |
| 228 | SA_PICKUP_ST_FD | - | - | c0 PickStAlFd0 t0.1 | - | - |
| 229 | SA_PICKUP_ST_UP | - | - | c0 PickStAlUp0 t0.1 | - | - |
| 230 | SA_THROW_ENTER | - | - | c0 ThroStAlBgH t0.1, crouched ThroCrAlBgH | - | equipment (throw, no spec yet) |
| 231 | SA_THROW_LOOP | - | - | c0 ThroStAlNtH loop, crouched ThroCrAlNtH loop | - | equipment (throw, no spec yet) |
| 232 | SA_THROW | - | - | c0 ThroStAlEdH loop, crouched ThroCrAlEdH loop | - | equipment (throw, no spec yet) |
| 233 | SA_THROW_LEAVE | - | - | c0 ThroStAlBgH back, crouched ThroCrAlBgH back | - | equipment (throw, no spec yet) |
| 234 | SA_WLJP_JUMP1 | 5 RootMotion | L1 +N end0 | c0 WljpStAlBgR with bIsLeftPart, else WljpStAlBgL | bIsLeftPart is first set from the run cycle phase (rule 5.9). Reloaded lowers the controller's fGEZLocation by 46 around this action (rule 6) | movement/wall-jump-split-vandamme.md |
| 235 | SA_WLJP_SALTO | 0 None then 5 RootMotion | L3 +N end0; vSpeedAfterRM = -370 X with Z = -615 | c0 WljpStAlBkL, or WljpStAlBkR with bIsLeftPart | - | movement/wall-jump-split-vandamme.md |
| 236 | SA_WLJP_UP | 5 RootMotion | L1 +N end0 | c0 WljpStAlUpR with bIsLeftPart, else WljpStAlUpL | - | movement/wall-jump-split-vandamme.md |
| 237 | SA_WLJP_UP_WAIST | 5 RootMotion | L1 +N end0 | c0 WljpStAlUwL, x with bIsLeftPart | - | movement/wall-jump-split-vandamme.md |
| 238 | SA_WLJP_UPSMALL | 5 RootMotion | L1 +N end0 | c0 WljpStAlUmL, x with bIsLeftPart | - | movement/wall-jump-split-vandamme.md |
| 239 | SA_BLAST_LOOP | 2 Falling | - | c0 BlstStAlDn2 loop t0.1 | set bLyingOnGround; col(1,0,0); IK_Type = 1 | combat/blast-and-fall-damage.md |
| 240 | SA_BLAST_LOOP_AFTERGRAB | 2 Falling | - | c0 BlstStAlDn2 loop t0.1 | set bLyingOnGround; col(1,0,0), +world; m_AttackTarget = m_AttackedBy = None; clear bAvoidLedges, bStopAtLedges, set bCanWalkOffLedges | combat/blast-and-fall-damage.md |
| 241 | SA_BLAST_OBSTACLE | 2 Falling | - | c0 BlstStAlIt2 | m_AttackTarget = m_AttackedBy = None; set bLyingOnGround | combat/blast-and-fall-damage.md |
| 242 | SA_BLAST_FLOOR | 0 None then 5 RootMotion | +W end0 (lock type unchanged) | c0 BlstStAlEd2 | m_AttackTarget = m_AttackedBy = None; set bLyingOnGround | combat/blast-and-fall-damage.md |
| 243 | SA_BLAST_WAKEUP | 0 None then 5 RootMotion | L1 +W end0 | merc AWakeStBack2H / AWakeCrBack2H, spy AWakeStBack / AWakeCrBack (standing / crouched) | - | combat/blast-and-fall-damage.md |
| 244 | SA_BOMB | - | - | c0 MineStAlBg0 t0.1, crouched MineCrAlBg0 t0.1 | - | objectives/bomb-target.md |
| 245 | SA_BOMB_END | - | - | c0 MineStAlEd0 t0.1, crouched MineCrAlEd0 t0.1 | - | objectives/bomb-target.md |

#### Cases 246 to 268 (pipe side change, blast wall, quick throw, short ladder)

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 246 | SA_CHANGE_PIPEV_L | 5 RootMotion | L3 -add end((2 m_PipeArmsZone.X + 16) X, +32768) | c0 PipVStAlTrRL t0.1, or PipVStAlTrLL with bIsLeftPart | with the default pipe arms zone X = 37 the end offset is 90 forward | movement/pipe-vertical.md |
| 247 | SA_CHANGE_PIPEV_R | 5 RootMotion | as 246 | c0 PipVStAlTrLL x t0.1, or PipVStAlTrRL x with bIsLeftPart | - | movement/pipe-vertical.md |
| 248 | SA_AFTER_CHANGE_PIPEV | 6 OnPipe | - | c0 PipVStAlNtL loop, x when not bIsLeftPart (ForwardAnim and the coefficient are not touched) | set bIsStopped, bCanStop, bBeginSpecial12Anim; fOldFrame = 0; Velocity = 0 | movement/pipe-vertical.md |
| 249 | SA_LAND_AFTER_BLAST_WALL | 1 Walking | - | c0 LandStXxFd2 | set bWeaponTransit; merc: ev:EvSetWeaponLoaded(0) | combat/blast-and-fall-damage.md |
| 250 | SA_WAKE_AFTER_BLAST_WALL | 0 None then 5 RootMotion | L1 +W end0 | spy c0 WakeStSpDn0, merc c0 WakeStSpFd2 | set bWeaponTransit | combat/blast-and-fall-damage.md |
| 251 | SA_QUICK_THROW | - | - | c22 ThroStAlMix; blend c22 0 @B Spine1; to c22 1/0.1 | set bQuickGadgetHasBegun (the actions that grab a traversal element cancel it: to c22 0/0.1, clear, ev:NotifyQuickLaunch) | equipment/grenade-throw.md |
| 252 | SA_SHL_S_JUMP | 5 RootMotion | L1 -add end0 | c0 CoopBtwAlHpEdS t0.1 | X(m_InteractionTarget, 254 SA_SHL_P_PERFORM), or a log line when there is no partner; Acceleration = 0 | movement/coop.md |
| 253 | SA_SHL_S_REPOS | 11 Repos | - | - | fPhysReposLinearSpeed = 380; set bUseAdditionnalPhysicsForRepos; clear bServerControlsRepos | movement/coop.md |
| 254 | SA_SHL_P_PERFORM | 5 RootMotion | L3 +W end0 | c0 CoopCrAlHpEdP t0.1 | clear bReadyForShortLadder | movement/coop.md |
| 255 | SA_SHL_P_STOP | 1 Walking | - | - | clear bReadyForShortLadder | movement/coop.md |
| 256 | SA_SHL_P_WAITING | - | - | c0 CoopCrAlHpNtP loop, only when not in RootMotion | set bReadyForShortLadder (same condition) | movement/coop.md |
| 257 | SA_SHL_P_FINISH | 1 Walking | - | - | UnCrouch(false) | movement/coop.md |
| 258 | SA_SHL_P_LEAVE | - | - | c0 CoopCrAlHpBgP back t0.1 | clear bReadyForShortLadder | movement/coop.md |
| 259 | SA_SHL_P_BEGIN | 1 Walking | - | c0 CoopCrAlHpBgP, t0 crouched or t0.5 standing | - | movement/coop.md |
| 268 | SA_SHL_S_JUMPWALL | 5 RootMotion | L1 -add end0 | c0 CoopBckFlpToNtS t0.1 | as 252 (partner gets 254) | movement/coop.md |

#### Cases 271 to 292 (jammer, camera network, hoist, switch, hand interact back, fire)

The hoist supporter cases 280 to 282 and 287 to 289 switch to Walking first and then to RootMotion (L1 -add end0,
Acceleration = 0), so a supporter that was in another mode goes through Walking.

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 271 | SA_JAMMER_START | - | - | - | ev:evStickyShockSfx(1) | combat/shock-and-taser.md |
| 272 | SA_JAMMER_STOP | - | - | - | ev:evStickyShockSfx(0) | combat/shock-and-taser.md |
| 273 | SA_CAMNETWORK_BG | - | - | - | set bUsingCamNetwork | equipment (camera network) |
| 274 | SA_CAMNETWORK_END | - | - | - | clear bUsingCamNetwork | equipment (camera network) |
| 276 | SA_HOIST_P_PERFORM | 9 Ledge | - | c0 CoopDynHstBP t0.1 | clear bReadyForHoistClimb | movement/coop.md |
| 277 | SA_HOIST_P_STOP | 9 Ledge | - | - | clear bReadyForHoistClimb | movement/coop.md |
| 278 | SA_HOIST_P_WAITING | 9 Ledge | - | c0 CoopDynHstBPnt loop t0.3 | set bReadyForHoistClimb | movement/coop.md |
| 279 | SA_HOIST_P_LEAVE | 9 Ledge | - | c0 LedgStAlNt0 loop t0.3 | clear bReadyForHoistClimb | movement/coop.md |
| 280 | SA_HOIST_S_BARRIER | 1 then 5 RootMotion | L1 -add end0 | c0 CoopDynHstBSRailUp | Acceleration = 0 | movement/coop.md |
| 281 | SA_HOIST_S_JUMP | 5 RootMotion | L1 -add end0 | c0 CoopDynHstBSBg t0.1 | X(m_InteractionTarget, 276 SA_HOIST_P_PERFORM) or a log line; Acceleration = 0 | movement/coop.md |
| 282 | SA_HOIST_S_FINISH | 1 then 5 RootMotion | L1 -add end0 | c0 CoopDynHstBSGround | Acceleration = 0 | movement/coop.md |
| 283 | SA_SWM_ENTER | 1 Walking | - | blend c24 0; c0 ButtStAIFd0 t0.1 | IK_Type = 1; quick-gadget cancel | objectives/switch-hack.md |
| 284 | SA_SWM_BACK | 1 Walking | - | c0 ButtStAIFd0 back | - | objectives/switch-hack.md |
| 285 | SA_SWM_MAINTAIN | 1 Walking | - | c0 ButtStAINt0 loop t0.1 | - | objectives/switch-hack.md |
| 286 | SA_HOIST_P_PERFORM_NOWALL | 9 Ledge | - | c0 CoopDynHstBPf t0.1 | clear bReadyForHoistClimb | movement/coop.md |
| 287 | SA_HOIST_S_JUMP_NOWALL | 5 RootMotion | L1 -add end0 | c0 CoopDynHstBSBgf t0.1 | X(m_InteractionTarget, 286) or a log line; Acceleration = 0 | movement/coop.md |
| 288 | SA_HOIST_S_BARRIER_NOWALL | 1 then 5 RootMotion | L1 -add end0 | c0 CoopDynHstBSRailUpf | Acceleration = 0 | movement/coop.md |
| 289 | SA_HOIST_S_FINISH_NOWALL | 1 then 5 RootMotion | L1 -add end0 | c0 CoopDynHstBSGroundf | Acceleration = 0 | movement/coop.md |
| 290 | SA_HANDINTERACT_BACK | - | - | blend c24 1 @B Spine1; c24 ButtStAIFd2 back (loaded) or ButtStAIFd0 back | set bActionsSendByServerMove | objectives/interaction-system.md |
| 291 | SA_FIRE | - | - | blend c21 1 @B Spine1; c21 ReloStSpFd3 | - | equipment/merc-rifle.md |
| 292 | SA_FIRE_GRENADE | - | - | blend c21 1 @B Spine1; c21 ReloStSpFd1 | - | equipment/merc-rifle.md |

#### Cases 300 to 312 (ladder side change, pipe and ledge transitions, crouched exits, grab from ledge)

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 300 | SA_CHANGESIDELADDER_L | 5 RootMotion | L3 -add end(2 m_NarrowLadderArmsZone.X X, +32768) | c0 LaddStAlTuR x | with the default zone X = 37.5 the offset is 75 forward | movement/ladder.md |
| 301 | SA_CHANGESIDELADDER_R | 5 RootMotion | as 300 | c0 LaddStAlTuR | - | movement/ladder.md |
| 302 | SA_AFTER_CHANGESIDELADDER | 13 OnLadder | - | c0 LaddStAlNt0 loop t0.1 | set bIsStopped, bCanStop, bBeginSpecial12Anim; fOldFrame = 0; Velocity = 0 | movement/ladder.md |
| 303 | SA_TR_PIPELEDGE_R | 5 RootMotion | L1 -add end(14 X + 45 Y - 27 Z, 0) | c0 PipVStLgEuR t0.1 | TypeLedge = 0 | movement/pipe-vertical.md, movement/ledge.md |
| 304 | SA_TR_PIPELEDGE_L | 5 RootMotion | L1 -add end(14 X - 45 Y - 27 Z, 0) | c0 PipVStLgEuR x t0.1 | TypeLedge = 0 | movement/pipe-vertical.md |
| 305 | SA_TR_LEDGEPIPE_R | 5 RootMotion | L1 -add end(-14 X + 45 Y + 8 Z, 0) | c0 PipVStLgBuR x t0.1 | set bInvertNextLeftPart (the next SA_ON_PIPE takes bIsLeftPart from it) | movement/pipe-vertical.md |
| 306 | SA_TR_LEDGEPIPE_L | 5 RootMotion | L1 -add end(-14 X - 45 Y + 8 Z, 0) | c0 PipVStLgBuR t0.1 | - | movement/pipe-vertical.md, movement/ledge.md |
| 307 | SA_TR_ORTHOPIPE_L | 5 RootMotion | L3 -add end(30 X - 30 Y, -16384) | c0 PipHStAlTuL t0.1 | - | movement/pipe-horizontal.md |
| 308 | SA_TR_ORTHOPIPE_R | 5 RootMotion | L3 -add end(30 X + 30 Y, +16384) | c0 PipHStAlTuR t0.1 | - | movement/pipe-horizontal.md |
| 309 | SA_TOPPIPE_CR | 5 RootMotion | L1 -add end(92.7 X + 45 Y + 143.15 Z, 0) | c0 PipVCrAlEuR r1.5 t0.1, or PipVCrAlEuL with bIsLeftPart | Acceleration = 0 | movement/pipe-vertical.md |
| 310 | SA_LADDER_TOP_CR | 5 RootMotion | L1 -add; spy end(75 X + 156 Z, 0), merc end0 | spy c0 LaddCrAlEu0 r1.5; merc c0 LaddCrSpEu2 | Acceleration = 0 | movement/ladder.md |
| 311 | SA_BARRIER_ABOVE_LEDGE_CR | 5 RootMotion | L1 -add end0 | c0 LedgCrNwUp0 t0.1 | - | movement/ledge.md |
| 312 | SA_GRABFROMLEDGE_GO | 5 RootMotion | L1 -add end0 | spy (the grabber): c0 GrabLgEdRail t0.1 and X(m_AttackTarget, 312); merc (the victim, through that call): c0 GrbdStLgRail t0.1 | - | combat/spy-grab.md |

#### Cases 313 to 328 (grab from ledge, berserk, terminate, flags, counter grab, repositioning)

| # | Action | Phys | RM | Anim | Col, fields, calls | Spec |
|---|---|---|---|---|---|---|
| 313 | SA_GRABFROMLEDGE_BEGIN | - | - | c0 GrabLgBgRail | - | combat/spy-grab.md |
| 314 | SA_GRABBEDFROMLEDGE_FALL | 2 Falling | - | c0 FallStAlFr0 | Velocity = 77.5 X with Z = -681 | combat/spy-grab.md |
| 315 | SA_BERSERK_ATTACK | 5 RootMotion | L3 +W end0 | c0 AtakStAlTu t0.1 | IK_Type = 1 | combat/melee-elbow-charge-berserk.md |
| 316 | SA_CHARGE_ATTACKED_GOBACK | 0 None then 5 RootMotion | L7 +W end0 | c0 StrkCrAlBk0 t0.05 (the same clip for every stance; StrkType records the stance) | IK_Type = 1; Acceleration = 0; after a roll: blend c15 0 and clear bIsRolling. Facing: without a Controller, or when the Controller's RemoteRole is AutonomousProxy, the pawn turns in place to SpecialYaw; otherwise, with m_AttackedBy, it turns to face the attacker and SpecialYaw = the new yaw. StrkType = (standing ? 1 : 0) + (WeaponLoaded ? 2 : 0) (STRK_CR0, ST0, CR2, ST2) | combat/melee-elbow-charge-berserk.md |
| 317 | SA_TERMINATEPAWN_BEGIN | merc 0 None then 5 RootMotion; spy 0 None | merc L5 +W end0 | merc c0 AtakDnMmBg2 t0.15; spy c0 AtkdDnMmBg0 t0.15 | merc: IK_Type = 1, X(m_PawnToTerminate, 317) | combat/knockout-and-terminate.md |
| 318 | SA_TERMINATEPAWN_KILL | merc 0 None; spy unchanged | - | merc c0 AtakDnMmKo2; spy c0 AtkdDnMmKo0 | merc: X(m_PawnToTerminate, 318) | combat/knockout-and-terminate.md |
| 319 | SA_TERMINATEPAWN_RELEASE | - | - | spy only: c0 AtkdDnMmEd0 t0.1 | - | combat/knockout-and-terminate.md |
| 320 | SA_REMOVE_COLLISION | - | - | - | m_AttackedBy = m_AttackTarget = None; set bCanBeTerminated and bLyingOnGround, and bSleeping when Health > 0; col(1,0,0) | combat/knockout-and-terminate.md |
| 321 | SA_SERVER_REPOS | 11 Repos | - | - | fPhysReposLinearSpeed = 220; set bUseAdditionnalPhysicsForRepos and bServerControlsRepos | combat/spy-grab.md |
| 322 | SA_PICKUP_FLAG | - | - | c0 ExtrStAlBg0 t0.1 | - | objectives/disk-flag.md |
| 323 | SA_DROP_FLAG | - | - | c0 ExtrStAlEd0 t0.1 | - | objectives/disk-flag.md |
| 325 | SA_COUNTER_GRAB_GO | 5 RootMotion | merc L1 -add end0 (RootMotionAdditionnalPhysics 1 is set but unused); spy L5 +W end0 | ZeroMovementAlpha(1, 25, 0.1) first; merc c0 GrbdStMmEd; spy c0 GrabStMmEd1 | merc: X(m_AttackedBy, 325) | combat/spy-grab.md |
| 326 | SA_EXIT_REPOS | - | - | - | set bNoReposAtEnd | combat/spy-grab.md |
| 327 | SA_GRABBED_RELEASE_DROPTECH | 5 RootMotion (after 0 None when already in RootMotion) | L3 -add end0 | c0 GrbdStAlXxb | set bStillGrabbed; m_AttackedBy = None; col(1,0,0) | combat/spy-grab.md, combat/drop-attack.md |
| 328 | SA_FAST_REPOS_SANS_RECAL | 11 Repos | - | - | fPhysReposLinearSpeed = 300; clear bServerControlsRepos; set bUseAdditionnalPhysicsForRepos, bKeepLocalVandammePos and bNoReposAtEnd (setPhysics clears bKeepLocalVandammePos first, so it stays set) | combat/knockout-and-terminate.md |

### 5. Helper natives called by the cases

1. **AnimateWalking(Action)** (editor 0x111fd360): PipHFire = 0, Phys Walking, clears the controller's
   bBlockControlForJump, LastZipP = (-1e7, 0, -1e7),
   fWalkSpeedLimit = 200 and fRunSpeedLimit = 250 as defaults, then by action sets the walk blender set and plays
   the wait loop on c0 (loop) with tween T:
   - T = 0.25; T = 0 when OldPhysics was RootMotion or Falling; when the pawn is jumping forward
     (IsJumpingForward) BlendChangeTime = 0.1 and T = 0.1, else BlendChangeTime = 0.25; T = 0.3 when OldBTW was
     set; OldBTW = 0 always.
   - 20 (grab): RightAnim and TurnAnim GrabStSpRt0, ForwardAnim and RunAnim GrabStAlFd0, coefficients 57 / 57,
     limits 150000 / 150000, wait GrabStAlNt0.
   - 32 (grabbed): ForwardAnim GrbdStAlFd0, RightAnim GrbdStAlRt0, LeftAnim GrbdStAlLt0, coefficients 57 / 57,
     limits 150000, wait GrbdStAlNt0.
   - 75, 80, 83 (crouched unarmed): clear bStillGrabbed and bLyingOnGround, col(1,1,1), limits
     LowLimitForSlowCrouchOnly / HighLimitForFastCrouchOnly, ForwardAnim AWalkCrFd0, TurnAnim AWalkCrTurnRt0,
     RightAnim AWalkCrRt0, RunAnim AJoggCrFd0, coefficients 60 / 90, wait AWaitCr0.
   - 76, 79, 82 (crouched armed): as above with the 2 clips (AWalkCrRt2, AWalkCrFd2, AWalkCrTurnRt2), IK_Type =
     0, coefficients 130 / 160, wait AWaitCr2; merc LeftAnim WalkCrSpLt2, limits 125 / 150, RunAnim AJoggCrFd2;
     spy limits 200 / 800, RunAnim AWalkCrFd2.
   - 81, 86, 88 (standing armed): clear bStillGrabbed and bLyingOnGround, col(1,1,1), +world, TurnAnim
     AWalkStTurnRt2, RightAnim AWalkStRt2, ForwardAnim AWalkStFd2, fBlendCoeffWalkAnim 120.6; spy RunAnim
     ARunStFd2, limits 190 / 290, fBlendCoeffRunAnim 192.5; merc LeftAnim AWalkStLt2, limits 1000 / 2000; wait
     AWaitSt2.
   - 84, 85, 89 (standing unarmed): clear bLyingOnGround and bStillGrabbed, RightAnim AWalkStRt0, TurnAnim
     AWalkStTurnRt0, limits LowLimitForWalkOnly / HighLimitForRunOnly, ForwardAnim AWalkStFd0, RunAnim
     ARunStFd0, coefficients 120.6 / 250, wait AWaitSt0.
   - 87 (armed jog): clear bStillGrabbed, TurnAnim AWalkStTurnRt2, RightAnim JoggStAlRt2, LeftAnim JoggStAlLt2,
     ForwardAnim JoggStAlFd2, limits 1000 / 2000, fBlendCoeffWalkAnim 327, wait AWaitSt2.
   - other values: only the common part.
2. **PlayLanding(Action)** (0x111f99d0): the c0 clip and tween by action, then Phys Walking (except 101):
   7 LandStAkDA0 / LandStAkDA2 (loaded); 92: blend c19 1, c19 LandStAkKA0 / LandStAkKA2; 93 ALandHi t0.05 (a
   crouched spy plays landCrAlHi0); 94 ALandLow t0.0833; 95 ALandQuiet t0.0833; 96: c1 JoggStRpFd0 and blend c1 1;
   99 ALandHi2H t0.05; 100 ALandLow2H t0.0833; 101 root motion (L1 +W end0) and LandStXxBk2 t0.0833;
   102 ALandQuiet2H t0.0833. The clip is played only when one was chosen.
3. **SetAnimSetAfterRolling** (0x111ff7e0): writes the walk set straight into the blend channels (c5 and c6
   ForwardAnim, c7 and c8 RightAnim, c4 RunAnim), sets c4's blend alpha to 1 at once and resets the frames of
   c5 to c13 to 0. **SetAnimSetAfterRollingUp** (0x111ff8f0) also fills c9 to c13 from the Copy* fields, sets c9
   and c4 alpha to 1 and c4 to c8 alpha to 0.
4. **PlayDeath(d)** (0x111f9410): IK_Type = 1, set bLyingOnGround, then c0 the ADeath{St|Cr}{Down, Forward,
   Back, Left, Right} clip (spy fields, merc the 2H fields) with tween 0.1, by d (0 Down ... 4 Right) and
   bIsCrouched. d = 5 (drop attack): spy XxxxStAlAk0 t0.25 / crouched XxxxCrAlAk0 t0.1; merc XxxxStAlAk2 t0.25 /
   crouched XxxxCrAlAk2 t0.1.
5. **PlayDamage(Action)** (0x111fd9a0, game 0x10adf190): first the script event PlayFirstPersonDamage, raised only
   when the pawn has MyWeapon and SPawnType is 1 (game +0xa84 and +0xadb). 49 to 52 (special
   hits): blend c17 1 @B Head and c17 PainStHsLt2, PainStHsRt2, PainStHsFd2, PainStHsBk2. Other actions: when the
   pawn moves (Velocity non-zero) and the action is not 53, blend c17 1 @B Head and c17 GoglStAlDn2 (a head
   flinch) and stop; at rest, blend c17 1 (no bone) unless the action is 53 or 54. Then by stance (St / Cr) and
   WeaponLoaded (0 or not): 53 blend c23 1 (no bone), 54 blend c23 1 @B Head, both c23 ADamage{St|Cr}Electric{0|2}
   loop t0.2; 57 and 58 ADamage..ChestBack, 59 ChestForward, 60 ChestLeft, 61 ChestRight, 62 ArmLeft,
   63 ArmRight, 64 LegLeft, 65 LegRight, all on c17 with tween 0; any other action: blend c17 0. A head hit (57)
   uses the chest-back clip. The bone is the name B Head (game name index 0x21f), which both converted pawn
   skeletons have, so the moving flinch moves the head only; the whole-body hit clips play only at rest
   (Velocity exactly zero when the action runs), where the channel has no start bone (the root).
6. **Crouch(bClientSimulation)** (0x11206990): set initSlowReplication; a simulated proxy (Role 2) only sets
   bIsCrouched; otherwise when the cylinder is not yet the crouch size: SetCollisionSize(CrouchRadius,
   CrouchHeight), MoveActor down by the height change and, unless bClientSimulation, PrePivot = default PrePivot
   plus the change in Z, set bIsCrouched, ZoneConfig = ZoneConfigForClient = 1 and event StartCrouch(change). **UnCrouch(bTestOnly)** (0x111fb920): set
   initSlowReplication; a simulated proxy (Role 2) without bTestOnly only clears bIsCrouched; otherwise a box
   trace (current radius and height, flags 0x2df) from Location up twice the height change must be clear; when
   it is and not bTestOnly: default PrePivot, default cylinder, MoveActor up by the change, clear bIsCrouched,
   ZoneConfig = ZoneConfigForClient = 0, event EndCrouch(change). **UnCrouch()** (0x111fb750, used by 149, 150,
   163) does the same without the trace, moving up with world collision off, and raises no event.
7. **PlayJumpStart(Action)** (0x111f97a0): LastZipP = (-1e7, 0, -1e7); BlendChangeTime = 0.05. With horizontal
   speed above 10 the run cycle phase decides the foot (rule 5.9): left: set bIsLeftPart and play AJumpForwardL
   (90) or JumpStAlFL2 (91); right: clear bIsLeftPart and play AJumpForwardR (90) or JumpStAlFR2 (91). At rest:
   nothing when bWantsToCrouch; else AJumpStart (90) or JumpStAlFL2 (91). All on c0, rate 1, tween 0.
8. **MyDoJump** (0x111f9cf0) is not called by the jump actions; ExecuteAction calls it only for 110. It is the
   controller-side jump (spec/movement/jump-fall-land.md, ANSWERS-physics.md).
9. **Run cycle phase**: PlayRoll, PlayRollUp, PlayJumpStart and UpdateIsLeftPart (234) read the frame of c4 (the
   run channel): a frame in [0.1, 0.6] means the left foot leads (bIsLeftPart). PlayRoll / PlayRollUp
   (0x111fe260, 0x111fe360) set bIsRolling and play wblend(clip, c15, 0.1): RollStAlFdL / RollStJpFdL on the left
   foot, RollStAlFdR / RollStJpFdR otherwise.
10. **SetZipSpeed, UnZipSpeed, SetColForPipe4, SetColAfterPipe4**: the speed-pose and legs-over cylinder changes of
    physics-traversal.md (ZoneConfig 2 and 3, PrePivot shifts, FarMoveActor tests).
11. **ZeroMovementAlpha(From, To, Step)** (0x110dd700): for channels From to To - 1, a channel whose blend alpha is
    above 0 moves toward 0 by at most Step (10000 = at once); when none had a positive alpha, each channel's rate
    scale and frame are set to 0.
12. **AnimateStandUp** (0x111f8ce0): reads the c0 clip (the fall-down pose) and plays the matching wake clip on
    c0. Merc: ADeath* to AWake* (St, Cr, the 2H variants), StrkCrAlBk0 by StrkType, the grabbed releases
    (GrbdStAlXxa, GrbdStAlXxb, GrbdStAlKo0) to GrbdStAlWkA, the drop-attack poses XxxxStAlAk0, XxxxStAlAk2,
    XxxxCrAlAk0, XxxxCrAlAk2 to WakeStSpFd0, WakeStSpFd2, WakeCrSpFd0, WakeCrSpFd2, XxxxDnAlBk0 to AWakeStBack2H or
    AWakeCrBack2H, LandStXxBk2 to WakeStSpBk2; the backward cases switch to root motion (L1 +W end0, Phys None then RootMotion).
    Spy: always root motion (L1 +W end0) and AWakeStBack, AWakeCrBack, AWakeStBack2H or AWakeCrBack2H by
    stance and WeaponLoaded. Afterwards iLastWakeTime is set from the wake clip's length.

### 6. Reloaded.Core

Reloaded replaces the native thunk of 1411 (wrapper 0x10023740, module-relative). It calls the original exec and,
for the local player's own pawn only, around action 234 (SA_WLJP_JUMP1) with bIsLeftPart set, lowers the
controller's fGEZLocation by 46 after the call and restores it (+46) at the next ExecuteAction of that pawn
(spec/native/net-reloaded.md rule 2). The action itself is not changed. The value it tests is sAction (the
action the script stored before calling the native), read before the original runs.

### 7. Game build check

The game's body (0x10ad6840, ECX = this, Action on the stack) was decompiled with the same reflection types and
split the same way: 280 case groups with the same case lists and the same 15 values that only exit. A
fact-level comparison (re/ghidra/scripts/actions/compare_cases.py: setPhysics values, PlayAnim argument tuples,
literal field writes, bool dword updates, float constants) finds no difference; the 10 groups it flags differ only
in where the decompiler placed shared code (a literal in one build is a variable set by the shared tail in the
other) and in float literals printed as hex. The game's link-time optimised blend helpers take the channel in a
register; their channels, counted over the whole function, match the editor's (c1, c7, c8, c14, c15, c16, c18
to c22, c24, and c23 for AnimBlendToAlpha).

## States

ExecuteAction has no state of its own; the cases change the pawn's Physics, IK_Type, BTW, PipHFire, TypeLedge,
ZoneConfig and the root-motion fields listed above, which the physics modes and the animation tick then use.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| BlendChangeTime reset | 0.25 | s | decoded | prologue |
| End offsets of the climb-outs | see cases 113, 114, 129, 146, 149, 150, 160, 171, 173, 174, 203, 222 to 224, 246, 300, 303 to 310 | uu | decoded | ExecuteAction |
| Repositioning speeds | 180 (19, 148), 220 (321), 300 (161, 328), 380 (253) | uu/s | decoded | fPhysReposLinearSpeed writes |
| Wall-jump salto exit speed | -370 X, Z -615 | uu/s | decoded | case 235 |
| Barrier fall-off speed | 200 X, Z -162 | uu/s | decoded | cases 205, 225 |
| Grabbed-from-ledge fall speed | 77.5 X, Z -681 | uu/s | decoded | case 314 |
| Run cycle left-foot window | frame 0.1 to 0.6 of channel 4 | normalised | decoded | PlayRoll, PlayJumpStart, UpdateIsLeftPart |
| Death tween | 0.1 (drop attack 0.25 standing) | s | decoded | PlayDeath |
| Landing tweens | 0.05 high, 0.0833 low and quiet | s | decoded | PlayLanding |

## Network

ExecuteAction runs wherever the action is played: on the owning client when the script plays it, on the server
when the client's action arrives with the moves (net-moves.md), and on other clients through the proxy action
playback of NativeTick (net-proxy.md rule 8). The same switch runs on every role; the only role tests inside the
cases are 316 (the facing source depends on whether the Controller's RemoteRole is AutonomousProxy) and the
simulated-proxy shortcuts inside Crouch, UnCrouch, SetZipSpeed and the pipe-4 cylinder helpers. bServerTestRootMotion
(case 113) is the one place where the authority can refuse an action (physics-rootmotion-repositioning.md).
setPhysics marks the change for the next move (bActionsSendByServerMove); the grab cases 30, 31 and 33 to 37
clear that flag again right after.

## Edge cases

- Actions 5 (SA_CHARGING_END), 97 (SA_LAND_ST_2H), 275 (SA_HOIST_P_BEGIN), 324 (SA_COUNTER_GRAB_LOOP) and the
  undefined values do nothing beyond the prologue; the script handles them alone.
- Several actions run a second action on the partner pawn (18, 20, 21, 23, 24, 26, 28, 29, 252, 268, 281, 287,
  312, 317, 318, 325); the partner's ExecuteAction runs inside the first call, before the first pawn's
  epilogue.
- Without a skeletal mesh the whole call is skipped, including the physics change: a pawn whose mesh is missing
  never changes mode through actions.
- Case 136 plays two clips in a row on channel 0; only the second one is visible.
- Case 72 (SA_GOG_HEADMVT_OFF) plays the same head clip and blend-in as 71; the script event decides what the
  goggles do.
- Case 56 writes a log line on every call.

## Evidence

- Editor: body 0x11215980, jump table 0x1121d618 (329 entries), exit 0x1121d5bd, exec 0x1121eb10; helpers
  AnimateWalking 0x111fd360, AnimateStandUp 0x111f8ce0, PlayDeath 0x111f9410, PlayJumpStart 0x111f97a0,
  PlayLanding 0x111f99d0, MyDoJump 0x111f9cf0, UnCrouch 0x111fb750 / 0x111fb920, Crouch 0x11206990,
  SetZipSpeed 0x111fbb80, UnZipSpeed 0x111fbcf0, SetColAfterPipe4 0x111fbed0, SetColForPipe4 0x111fc0e0,
  PlayDamage 0x111fd9a0, PlayRoll 0x111fe260, PlayRollUp 0x111fe360, SetAnimSetAfterRolling 0x111ff7e0,
  SetAnimSetAfterRollingUp 0x111ff8f0, ZeroMovementAlpha 0x110dd700, PlayAnimWithBlend 0x111f9fd0,
  bServerTestRootMotion 0x11215580; slots of the SBasePawn vtable (0x114ae948): setPhysics +0x10c, PlayAnim
  +0x134, Crouch +0x158.
- Game: exec 0x10ad6800, body 0x10ad6840, setPhysics 0x10ad29a0 (slot +0xfc), PlayAnim 0x10a23590 (slot
  +0x124).
- Reloaded.Core: wrapper 0x10023740.
- Tools (tracked, our code): re/ghidra/scripts/actions/ (ghserve.py, a30_names.py, a35_noreturn_fix.py,
  cases.py, compare_cases.py); names in re/ghidra/annotations/actions.json. Case splits stay in
  local/re-out/c/actions/cases/.
- Clip names: the hard-coded FName indices resolve through the reflection name table; they match the Bridge's
  execaction_cases table.

## Open questions

1. Case 145 calls a level-side object (XLevel +0xf0, +0x34, virtual slot 0x7c) before entering the pipe; its
   purpose (statistics, sound, a GE cache) is not decoded.
2. The exact expression behind iLastWakeTime in AnimateStandUp (rule 5.12) is not decoded.
3. Why SA_DMG_HEAD (57) reuses the chest-back clips is a data choice to confirm against the clip catalogue.
