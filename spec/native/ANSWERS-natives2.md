# Answers from the second native study (natives the ports mark Estimated)

Each row: the question, the spec that asked it, the short answer, the rule that holds the full decode, and the
code that marks it Estimated today (file and member), so the owners can port the rule and switch the provenance
to Decoded. Addresses are in the Evidence sections of the natives2 specs.

## Traversal (spec/native/natives2-traversal.md)

| # | Question | Asked in | Answer | Rule | Marked Estimated in |
|---|---|---|---|---|---|
| 1 | CheckBTW probe distance, wall rules, normal returned | movement/back-to-wall.md Q1 | Reverse probes forward, else backward, 1.5 R; then two waist probes at +-0.5 R (-0.5 H) and a head probe (0.8 H) along the wall direction; every hit must be a PHYS_None actor; vWallNormal = SafeNormal(sum of the three later normals), X = 1 when zero. The script's ENTER uses the forward wall, ENTERBACK the one behind. | 1 | src/OpenScct.Game/SBase/SPlayerController.Movement.Traversal.cs: CheckBTW, BtwWallReach, BtwWallMaxNormalZ, BtwHeadProbe |
| 2 | CheckBTWEyes returns | back-to-wall.md Q2 | 1 = wall goes on and floor goes on; 0 = wall ends at head height on that side and 100 uu beside are clear (peek); -1 = blocked beside, or no floor beside (drop). Boxes (5, 5, 5), side point R + 4. | 2 | same file: CheckBTWEyes, BtwEyesSide |
| 3 | CheckBTWCorner accepted angles | back-to-wall.md Q3 | No angle window: the hit wall must be a full flat wall in the same plane as HitNormal (cross length <= 0.1) with the three CheckBTW probes passing; vWallNormal is not read or written. | 3 | same file: CheckBTWCorner, BtwCornerMaxDot, BtwCornerMinDot |
| 4 | CheckForOtherPipeV neighbour | pipe-vertical.md Q4 | The nearest GE within 10 of the point m_PipeArmsZone.X + 16 in front, if it is a PIPE: ZipP1/2 and normal SafeNormal(P3 - P1). | 4 | same file: CheckForOtherPipeV |
| 5 | CheckRoomToChangePipeV | native-contracts.md | Three (35, 35, 87.5) boxes round the pipe: aside zone.X + 8, forward 2 zone.X + 16, back to the centre line. | 5 | same file: CheckRoomToChangePipeV |
| 6 | bCanTakeLedgeFromPipe | native-contracts.md | An LG from FindArmsGE with zone (40, +-40, 90.3) radius 60, both ends more than sqrt(35) from the closest point, two clear boxes (down 27 and 45 aside, then 14 forward); writes NewZipP1/2. | 6 | same file: bCanTakeLedgeFromPipe |
| 7 | bTestGrabLedgeGeo | ledge.md Q4 | An LG 90 to 102 from the current ledge height, aligned within 10, with the controller's Location +-20 Right inside it; no output. | 7 | src/OpenScct.Game/SBase/SPlayerController.Movement.Zones.cs: bTestGrabLedgeGeo |
| 8 | CheckPipeFeet side choice and segments | physics-ge-queries.md Q2 | Stick from aForward/aStrafe; spot from P2 moved R across by the side of (A x N).Z; one side only; first box (35, 35, H - 8) at +16, then (26, 26, 22) along N, (35, 35, 22) across, (35, 35, 87.5) down (22 = controller height). | 8 | src/OpenScct.Physics/Movement/PhysicsDriver.GE.cs: CheckPipeFeet |
| 9 | TestTopLedge result 4 boxes and LG height reference | physics-ge-queries.md rule 8 | Reference G.P1.Z; box H up to P1.Z + 2 (CrouchHeight + 1) - H, box H 75 - R right, box CrouchHeight 95 forward from the top of the first box raised by H - CrouchHeight, then up to L.Z + 173. bCrouchOnly skips 3 and 4. | 9 | src/OpenScct.Physics/Movement/PhysicsDriver.GE.cs: TestTopLedge |
| 10 | CheckBarrier height reference and start | physics-ge-queries.md Q3 | Reference = Location.Z + PrePivot change; window G.Z - 40 < ref < G.Z + 5 with G = closest point + (R + 2) N; box start (ref.XY, C.Z + 23.5). | 10 | src/OpenScct.Physics/Movement/PhysicsDriver.GE.cs: CheckBarrier |
| 11 | CheckWallJump helpers | physics-ge-queries.md Q4 | Probe: line 0x1084, PHYS_None, no bNoSplitJump on BSP. Picker: 18 uu past R - 1 at -0.4 H; one side wins, else forward; both sides or none leave the result false. | 11 | src/OpenScct.Physics/Movement/PhysicsDriver.GE.cs: CheckWallJump |
| 12 | CheckGEForWallJump room boxes | physics-ge-queries.md rule 12 | Four wall probes (0, +160, +72.5 +-35 S); box to d1 - 50; floor boxes (35, 35, 60) at d1 - 55, d1 - 130, d1 - 210; LG box around probe 4; final box up to the ledge or +200; GEZPosition is Q.Z when no LG. | 12 | src/OpenScct.Physics/Movement/PhysicsDriver.GE.cs: CheckGEForWallJump |

## Engine movement (spec/native/natives2-movement.md)

| # | Question | Asked in | Answer | Rule | Marked Estimated in |
|---|---|---|---|---|---|
| 13 | physicsRotation | physics-dispatch.md Q1, docs/PHYSICS.md | Players turn only with bRotateToDesired (off in Versus); the walking roll lean always runs (RotationRate.Roll 3072): lean from side acceleration / AccelRate x 2048 above 200 uu/s, decay otherwise; FollowParent zeroes pitch and roll. | 1, 2 | src/OpenScct.Physics/Movement/PhysicsDriver.cs: performPhysics |
| 14 | FindBase | docs/PHYSICS.md | Cylinder box 8 down, flags 0xdf, SetBase when the hit actor changes. Stock. | 3 | src/OpenScct.Physics/Movement/PhysicsDriver.cs: FindBase |
| 15 | FindSpot search | collision-movement.md Q1 | Stock UE2 with actors never checked: CheckSlice up and down, then four corner slices at 0.55, move 1.1, mirror a single fit, pull back 0.2. No MaxShift. | 4, 5 | src/OpenScct.Physics/Movement/PhysicsDriver.Move.cs: FindSpot |
| 16 | moveSmooth | docs/PHYSICS.md, mover spec | MoveActor, then stepUp for a level move into a wall (normal Z below 0.2, -0.2 < u < 0.5) or one slide plus TwoWallAdjust and a third move; gravity is the constant (0, 0, -1). | 6 | src/OpenScct.Physics/Movement/PhysicsDriver.Movers.cs: MoveSmooth, CheckEncroachment |
| 17 | TwoWallAdjust | physics-walking.md Q2 | Stock UE2 exactly. | 7 | src/OpenScct.Physics/Movement/PhysicsDriver.Walking.cs: TwoWallAdjust |

## Damage (spec/native/natives2-damage.md)

| # | Question | Asked in | Answer | Rule | Marked Estimated in |
|---|---|---|---|---|---|
| 18 | ProcessDamage percent, stages, bands, blast, shake | devices/gameplay-object.md Q1, combat/environmental-damage.md | pct = int((1 - HP / InitialHP) 100); the highest stage at or below pct is re-applied (and its event re-fired) on every call; a Skin stage keeps the mesh; band spawn while inside, kill above MaxPercent; blast to every visible actor of the level (centre distance, sight 0x286) with linear falloff, instigator None (the script wrapper sets GPODamageInstigator); shake only for blasting objects. | 1 | src/OpenScct.Game/SBase/SGamePlayObject.cs: ProcessDamage, DamageActorsInRange |
| 19 | NativeHitWall bounce, sound, StoppedMoving | gameplay-object.md Q2 | Self damage 0.02 speed above 100; wall damage speed Mass 0.002; restitution 0.5, spin 0.75; settles below 60 on a floor (or without bBounce): tall objects tip, others stop with PHYS_Rotating and StoppedMoving; bounce sound above 40. | 2 | src/OpenScct.Game/SBase/SGamePlayObject.cs: NativeHitWall |
| 20 | Frag falloff, fDistForBlast, momentum, instigator | combat/environmental-damage.md, blast-and-fall-damage.md | int((1 - dist / Range) Damage); type SDamage_Explosion inside fDistForBlast, SDTBase outside; momentum victim - frag (not normalised); instigator the owner pawn; pawns need sight by one of three lines. | 4 | src/OpenScct.Game/SBase/SProjFrag.cs: DamageAt, Explode |
| 21 | Mine explosions | environmental-damage.md | Reach from refloc (MyRange, or MyDamageRange for the laser mine), falloff over MyDamageRange from the mine, instigator PcOwner.Pawn, only gameplay objects and extinguishers among non-pawns. | 5 | not ported (environmental-damage.md units) |
| 22 | HurtRadius for players | damage-pipeline.md | Never: native blasts deliver PawnTakeDamage to the victim's SPlayerController; HurtRadius only reaches TakeDamage. | Data | src/OpenScct.Game/SBase/SProjFrag.cs: Explode |
| 23 | Damage volume tick | devices/damage-volume.md Q1 | Authority, bActive, real-time period (appSeconds) strictly exceeded, every touching actor of DamagedClass; pawns only through bIsDamageVolumeSensitive; LastTouched cleared on each period after the first; no Super tick. | 6 | src/OpenScct.Game/SBase/SDamageVolume.cs: NativeTick |
| 24 | Shotgun pellets, cone, multipliers | combat/hit-zones-and-weapons.md (shotgun) | Nine bone traces in a fixed pattern (centre with 0.067 SR spread, ring 0.194 SR, diagonals 0.097 SR); falloff by DistCac / DistMed / DistLoin with CMax and the three Coefs; factors 1, 0.3, 0.5; only each actor's best pellet counts; TotalShotgunDamageMultipler, RadiusScale, DemiAngleDispersion and the class table are unused. | 7 | not ported (hit-zones-and-weapons.md rule 10) |
| 25 | Spy health regeneration | combat/health.md rule 3 and its question | Not in TickNative (stock and Reloaded ignore the argument). SBasePawn.NativeTick refills: server, SPawnAttaque, 0 < Health < 100, still and not lying; after 3 s, min(4, pool) on each whole second; cap 100. | 8, 9 | src/OpenScct.Game/SBase/SPawnAttaque.Damage.cs: TickNative |
| 26 | TickNative glass effect (cloak ramp) | equipment cloak spec | Stock rule as reloaded-hooks.md rule 11 (clear on rain, sticky shock or speed above 240; slope ramp; perturbation jitter). | 9 | src/OpenScct.Game/Equipment/PawnVision.cs: SetDesiredGlassValue, TickGlass |

## Events, states, objectives, motion tracking (spec/native/natives2-events.md)

| # | Question | Asked in | Answer | Rule | Marked Estimated in |
|---|---|---|---|---|---|
| 27 | SMagicEvent queue timing | devices/magic-event.md Q1 | Native 0.05 s timer from the script SetTimer; each expiry fires every entry with Time <= TimeSeconds in queue order. Repeat 0 is unlimited; Sequence groups advance only on a passing event. | 1 | src/OpenScct.Game/SBase/SMagicEvent.cs: Timer |
| 28 | HandleEventInfo | objectives/switch-hack.md | By the owner's TriggerMethode: toggle triggers every use, control alternates on bIsActive, untrigger-only untriggers; bTriggerOnceOnly clears bUsable. | 2 | src/OpenScct.Game/SBase/STriggerInteraction.cs: HandleEventInfo |
| 29 | GotoState, probe rules | devices/events.md Q1 | Same-state calls keep probes and jump to the label (Begin), a missing label stops the code; unknown states go to None with EndState; the mask is (Class or State) and Ignore; Disable lasts to the next state change. | 3 | src/OpenScct.Core/Engine/Mover.States.cs: Disable |
| 30 | Controller.GotoState PreviousState (Reloaded hook) | net-reloaded.md rule 6 | PreviousState = the state left on success, and also on a same-state call, unless bDontSaveCurrentState; Reloaded only adds an anti-tamper redirect to s_MultiSpectating. | 3 | src/OpenScct.Game/SBase/SPlayerController.StateSync.cs: GotoState |
| 31 | SObjective.Desactivate / Reset / ScheduleReset | objectives/missions.md | Desactivate: taken, timer off, triggers deactivated, HUD removal. Reset: delayed activation first, then DoReset on every trigger, taken false, activated true, RandomTriggers kept with C rand(), HUD add. ScheduleReset: one-shot timer. | 4 | src/OpenScct.Game/SBase/SObjective.cs: Reset, Desactivate, ScheduleReset |
| 32 | SMission.NotifyChildTaken / Desactivate | missions.md | ObjectivesLeft only for non-mission children; unchained: taken when all or MinimumObjectives (>= 1) children are taken; chained: next child DoReset plus broadcast, NotifyTaken past the last. Desactivate cascades to every child. | 4 | src/OpenScct.Game/SBase/SMission.cs: NotifyChildTaken, Desactivate |
| 33 | SObjectiveTrigger.Desactivate / IsActive / GetTriggerTime | objectives/objective-trigger.md | Desactivate clears bActive and bLocked, Triggering(false), GotoState_Inactive unless in s_TemporaryInactive; IsActive returns not bActive; GetTriggerTime is CaptureTime + TriggerTime while triggering. | 4 | src/OpenScct.Game/SBase/SObjectiveTrigger.cs: Desactivate, IsActive, GetTriggerTime |
| 34 | GetActors candidates, sight, sound | equipment/motion-tracking.md | Projectiles and other pawns within MaxDetectedDistance + R, speed >= MinDetectedVelocity, visible by a 0x86 line (see-through actors passed over); no size limits, no friend flag; sound radius 64; stale locks fade after KeepLockTime + OutTime. Reloaded: ViewTarget switch, cloaked spy skipped, multi-hit sight line. | 5, 6 | not ported (motion-tracking.md rule 4) |

## Not covered in this pass

- SObjective.NotifyTriggering rounding (src/OpenScct.Game/SBase/SObjective.cs: NotifyTriggering), SMission.ActivateDisks
  (src/OpenScct.Game/SBase/SMission.cs: ActivateDisks), the grenade HitWall rest speed (src/OpenScct.Game/SBase/SProjGrenade.cs),
  UObject::ProcessState timing (src/OpenScct.Core/Engine/Actor.States.cs) and the IsBlockedBy / IsOverlapping details.
