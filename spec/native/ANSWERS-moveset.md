# Answers from the moveset study (ladders, pipes, fences, wall moves, co-op)

Rules decoded while porting the spy and merc moveset (ladders, hand-over-hand, fences, wall jump, split jump,
Vandamme, barrier and low ledges, co-op moves, merc charge). They complete the movement specs and the native specs
named in each section; spec owners fold them in. The port (src/OpenScct.Physics/Movement/, src/OpenScct.Game/Movement/,
src/OpenScct.Game/SBase/ movement partials and states) follows them already.

Notation: L is the pawn Location, R and H its collision radius and height, defR and defH the class defaults, Up the
world Z axis. X, Y, Z are the axes of a rotation (GetRotationCoords: forward, right, up). "Box (a, b, c) from S to
E" is a SingleLineCheck with that extent; "clear" means it hits nothing. Flags are the native trace flags.

## Spec corrections

| # | Spec | Says | Should say | Evidence |
|---|---|---|---|---|
| 1 | physics-ge-queries.md rule 8, TestTopLedge | with bCrouchOnly the vault and barrier tests still run; an LG best or second "90 to 102 above" | with bCrouchOnly the native returns the crouch result at once (natives2-traversal.md rule 9); G is Best when it is an LG, else Second when it is an LG, and only G is range tested: `90 < abs(ZipP1.Z - G.P1.Z) < 102`; the forward box starts at A raised by H - CrouchHeight; the vault rise is m_LedgeGrabArmsZone.Z + 45 | ASBasePawn::TestTopLedge 0x1120f610 |
| 2 | coop.md Open questions 1 | the four co-op natives are unknown | decoded: sections M1 to M6 | editor 0x111effc0, 0x111e6d20, 0x112122e0, 0x11211c50, 0x11211b10, 0x11211130, 0x112108f0 |

## M1. AreaIsSafeToInteract(Target) -> bool (editor exec 0x111e6d20)

The climber's check before the short-ladder approach.
1. E = (R, R, H) of the climber. D = Normal(Target.L - L) (three dimensional).
2. S = Target.L - 70 D, with S.Z = Target.L.Z - Target.H + H (standing on the mate's floor).
3. Box E from L to S, flags 0x80086: must be clear.
4. Box E from S down to S - 15 Up, flags 0x80086: must hit (a floor). Then true; else false.

## M2. CheckIfCanInteractWithMate(out Mate) -> byte (editor 0x111effc0, exec passes a second argument 0)

Walks the level's pawns in order. For each SBasePawn M other than the own pawn with the same SPawnType:
F = own facing (X of the pawn rotation) flattened and normalised; Fm = the same for M; T = Normal of (M.L - L)
flattened.
1. M.bReadyForShortLadder (boost helper ready):
   - `abs(M.L.Z - L.Z) < M.R + 15`, horizontal distance under fMaxDistToShortLadder (350), `F . T > 0.75` and
     `-(F . Fm) > 0.75` (facing each other);
   - zero extent trace from M.L to L, flags 0x86, source the own pawn: the hit actor must be none or M;
   - then Mate = M; the short-ladder spot of M is computed (M4 on M, mode 0); when it fails the result is 0 (the walk
     stops). Otherwise the own pawn's ZipP1 and ZipP2 are set to the ends of that spot's LG and the pawn's
     SendZipPToServer event runs. Result 1.
2. Else M.bReadyForHoistClimb (hanging helper ready):
   - Q = M.L + 40 Fm; T' = Normal of (Q - L) flattened; `237.36 < M.L.Z - L.Z < 356.04`, horizontal distance from M
     under 300, `F . T' > 0.2`, `F . Fm > 0` (same way);
   - `M.FindHoistClimbType(None, M.L - 296.7 Up, -vector(M.Rotation), bIgnorePawn false, bQuickCheckBarrier true)`
     (M5) not 0;
   - box (R, R, 0.85 H) of the own pawn from L to M.L - 130.7 Xm - 296.7 Zm, flags 0x286: clear. Then Mate = M,
     result 3.
3. Nothing found: 0.

## M3. CheckIfCanStartSHL(out Loc, out Dir) -> bool (editor exec 0x112125a0, body 0x112122e0)

Dir is a vector (the uc declares `out Vector Rot`), the direction away from the wall that SetInfoForVandamme takes.
1. The arms zone is set to (0, 0, 512 - H) and the radius to 150; FindArmsGE gives Best and Second; zone and radius
   are restored.
2. When Best gives a spot (M4): that spot, and the ends of Best. When Second also gives a spot whose horizontal
   distance to L is smaller, Second's spot and ends replace them. True.
3. Else when Second gives a spot: that spot and Second's ends. True. Else false.

## M4. The short-ladder spot of an LG (editor 0x11211c50, room test 0x11211b10)

For the helper pawn and an LG G (P1, P2), mode m (0 from the script):
1. `(P1.Z - P2.Z)^2 < 225` and `502 < P1.Z - L.Z + H < 512` (the ledge 502 to 512 above the feet).
2. A, B = P1, P2 flattened; V = B - A; W = B - L (flattened); s = W . Normal(-V.Y, V.X, 0) (the distance from the
   ledge line) and t = W . V (unscaled): `-250 < s < 250` and `-18086 < t < |V|^2 + 18086`.
3. C = the point of segment P1-P2 closest to L. When `|P1 - P2|` (horizontal) is under 192, C = P1 + SafeNormal(P2 -
   P1) x half that length. When C is within R (horizontally) of P1, C = P1 + R SafeNormal(P2 - P1); then the same at
   P2 toward P1.
4. N = Normal(-(P2 - P1).Y, (P2 - P1).X, 0), turned to face the pawn: negated when `N . SafeNormal(P2 - L) >= 0`.
5. S = C + (R + 10) N with S.Z = P1.Z - 510 + H. The room test (6) on (S, N); when it passes the spot is (S, N).
   Else S' = C - (R + 10) N with the same Z, room test on (S', -N); when it passes the spot is (S', -N). Else none.
6. Room test (0x11211b10) of (S, N): S.Z is raised by defH - H (the standing centre);
   `FindHoistClimbType(G, S, N, bIgnorePawn = m, bQuickCheckBarrier false)` (M5) must be non zero, and a box (R, R, H)
   from L to S (flags 0x284 when m, else 0x2df) clear. The result is that climb type.

## M5. FindHoistClimbType (editor body 0x11211130, exec 0x11215680)

The exec form (`FindHoistClimbType(optional bIgnorePawn, optional bQuickCheckBarier)` in script) calls the body with
no GE, P = L - 296.7 Up (the climber's spot under a hanging helper) and D = -vector(Rotation) (away from the wall).

Body (G, P, D, bIgnorePawn, bQuick), on the helper. Es = (defR, defR, defH), Ef = (defR, defR, 5):
1. Floor under the climber: box Ef from P - 83 Up to P - 97 Up, flags 0x286, must hit; else 0.
2. Room behind the climber: box Es from P to P + 126 D. Without bIgnorePawn flags 0x287 and a hit on anything but a
   spy pawn gives 0 (the climber himself may stand there); with bIgnorePawn flags 0x286 and any hit gives 0.
3. Column above the climber: box Es from P + 456 Up down to P - 10 Up (flags 0x286) must travel at least 0.96 of the
   way, and box Es from P up to P + 446 Up must be clear; else 0.
4. The top: when the helper hangs (PHYS_Ledge): with bQuick the radius is set to 40 (it stays 40) and FindArmsGE with
   the zone (37, 0, 210) (zone restored): an LG as Best or Second gives the barrier class, else the standard class.
   Without bQuick, v = TestTopLedge(false). Not hanging: no G gives 0; else v = CheckIfBarrier(G, (P.X - 8 D.X,
   P.Y - 8 D.Y, G.P1.Z - m_LedgeGrabArmsZone.Z), (-D).Rotation(), bIgnorePawn) (M6). v > 2: barrier class; v 1 or 2:
   standard class; 0 gives 0.
5. The wall under the ledge: boxes (35, 35, 15) from P + h Up to P - 75 D + h Up for h = 69, 230, 345 (flags 0x286).
   All three clear (no wall): SHL_BarrierNoWall (4) or SHL_StandardNoWall (3). Else SHL_Barrier (2) or SHL_Standard
   (1). SHL_Palissade (5) is never returned.

## M6. CheckIfBarrier(G, L', Rot, bIgnorePawn) -> byte (editor 0x112108f0)

TestTopLedge for a virtual hang at L' with rotation Rot on G, for a pawn that does not hang yet. Flags 0x286 with
bIgnorePawn, else 0x287. No G gives 0. The radius is set to 150 and not restored.
1. Crouch test: box (35, 35, CrouchHeight) from L' lowered by defH - CrouchHeight up 206.1, then 75.7 along X. Both
   clear: the vault test as TestTopLedge (box (35, 35, 44) from L'.Z + m_LedgeGrabArmsZone.Z + 45, 95 along X, then
   down to L'.Z + 78): both clear 3, else 2.
2. Otherwise the barrier test: zone (0, 0, 512 - defH + 96) at the pawn's own location (zone restored); G' = Best
   when an LG, else Second when an LG; `90 < abs(G.P1.Z - G'.P1.Z) < 102`; L' - 20 Y projects strictly inside G'
   and L' + 75 Y strictly inside G; then the four boxes of TestTopLedge result 4 from L' (A from G'.P1.Z, the
   ceiling at L'.Z + 173). All clear: 4. Else 0.

## W1. Zero-extent lines against the BSP (UModel::LineCheck, editor 0x111943e0, walk 0x111925a0)

Answers the converted-BSP winding question (docs/spikes/R4-sim-vs-game.md, ANSWERS-fidelity.md "BSP windings"): the
native line test never looks at polygon winding.
1. No nodes: the model's RootOutside. A non-zero extent goes to the box sweep (collision-primitives.md).
2. The walk (start S, end E, node n, the last plane crossed P, Outside, ExtraNodeFlags F). CSG(n) means the node
   has vertices and `NodeFlags & (F | 0x21) == 0` (NF_NotCsg 0x01, NF_IsNew 0x20); the one-sided cases test
   `F & ~0x10` instead. With ds, de the plane distances of S and E:
   - both `> -0.001`: Outside |= CSG, go to the front child;
   - else both `< 0.001`: Outside &= !CSG, go to the back child;
   - else the plane is crossed at M = S + (S - E) ds / (de - ds): the near half (S to M, the child on S's side,
     back when ds <= 0) with Outside' = Outside || CSG on the front side, Outside && !CSG on the back side, and the
     same P; a hit there ends the walk; then the far half from M with the far side's Outside', P = n.
   - an empty child index is a leaf: Outside ends the call with no hit (and sets a global "was outside" flag);
     inside is solid: unless the flag is still clear and F & 0x10, the hit is at S (the entry point) on plane P.
3. ULevel::MultiLineCheck passes F = 0, so a line that starts in solid hits at its start.
4. The result: Time = the projection of the hit point on the segment, less 0.5 / |D|, clamped; Location from Time.
   Item and Normal come from the first node of P's coplanar chain (iPlane, node + 0x38) whose polygon holds the
   point (0x11192a30: each edge's side test, sign changes between consecutive edges reject, zero allowed); when none
   holds it, P's own node and plane. Material only with TRACE_Material (0x1000).
5. Node layout (0x5c bytes): Plane at 0, iBack 0x30, iFront 0x34, iPlane 0x38, iSurf 0x2c, NumVertices 0x5a,
   NodeFlags 0x5b.

## W2. Zero-extent lines against static mesh triangles (UStaticMesh::LineCheck walk, editor 0x111b6c50)

The collision tree is walked by node plane (the start's side first; both children when the segment spans the plane
by 0.001 at each end). Only when it spans the plane is the node's triangle tested: its bounds against the segment,
then the crossing point M (t = the projection of M on the segment): `0 <= t < 1`, t below the best, and M strictly
inside the three edge planes. No facing test: both faces are hit, and the normal is the node plane (in world space,
normalised) whichever side the line came from. A triangle is tested once per query (a mailbox).

## Findings from the moveset goldens (docs/spikes/R4-moveset.md)

Compared with the sim through tests/OpenScct.DiffTest (RunnerTests reads golden-moveset now).
1. Root track. A clip with a keyed RootTrack (rootTrack in svm.anim/1: LaddStAlEu0, LaddStAlBu0, LaddStSpBu2,
   LaddStEdIOR, WljpStAlBgR and 70 more) moves the pawn by that track, linear between its keys, not by the root
   bone: LaddStAlEu0 rises 192.8 at a constant 434 uu/s over keys 0 to 10, then goes 75 forward at 281 uu/s (the
   golden, all runs); the root bone would curve. Measured: spy-ladder-up, spy-ladder-down, merc-ladder-down.
2. Root position across a clip change. While root motion stays locked, the delta of the first frame of a new clip
   is taken from the previous clip's last root position, both absolute: WljpStAlBgR (track end 50 forward) to
   WljpStAlUpR (bone start 40.69 forward, 23.31 up) moves the pawn 9.3 back and 23.3 up in one frame (spy-wall-jump,
   frame 36). Both rules are for the clip loader (OpenScct.Game.Movement.PawnAnimation, PawnClip.Root relative to the
   first key today) and PawnRootMotion.
3. SA_FALL_AFTERBARRIER (205) plays JumpStAlBf0 with Velocity 200 X and Z -162 (one frame of gravity seen at the
   first sample, -187): spy-vault-barrier.
4. Script notifies of the movement clips (ImpulsionSplit, NotifyTakeArmControlForSplitJump,
   ChangeAnimsForDrawUndrawSplitJump, NotifyTakeArmControlForPipH2 and H4) reach SBasePawn through the level's
   AnimNotifyHandlers; without them the split jump never pushes off (spy-split-jump).

## F12 (still open)

physFalling (editor 0x11213090), ASBasePawn::performPhysics (0x11207b70), startNewPhysics (0x112157c0) and the
half-step integrator (0x1117b2b0: V + 0.5 a dt) were read in full: the only averaging in them is the drop-attack
homing pull (`V.XY = 0.5 V.XY + 0.5 k pull`). The narrow-top push path matches the port (push 0.1 R along the
probe's normal; processLanded only when the push is blocked before Time 0.1, then the step velocity). In
zip-line-29 frame 330 the golden velocity is (V_before + V_port) / 2 in every component (Y: 0 and -210.8 give
-105.4), and the next frame moves by it, so the second update is real and runs after physics: the script events of
the push (NotifyHitWall, Bump) and the controller's falling state are the places left to read.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Safe-area spot | 70 before the mate, floor probe 15 | uu | decoded | 0x111e6d20 |
| Boost mate window | Z within mate R + 15, facing dots 0.75 | uu, cos | decoded | 0x111effc0 |
| Hoist mate window | dZ 237.36 to 356.04, within 300, dots 0.2 and 0, Q 40 ahead | uu, cos | decoded | 0x111effc0 |
| Hoist climber box | (R, R, 0.85 H) to 130.7 X 296.7 Z under the mate | uu | decoded | 0x111effc0 |
| SHL search | zone (0, 0, 512 - H), radius 150 | uu | decoded | 0x112122e0 |
| SHL ledge window | 502 to 512 above the feet, flat within 15 | uu | decoded | 0x11211c50 |
| SHL across and along | s within 250 of the ledge line; t within 18086 (unscaled, uu^2) of the segment | uu | decoded | 0x11211c50 |
| SHL short ledge, spot | under 192 long uses the middle; R + 10 out, floor 510 under the LG | uu | decoded | 0x11211c50 |
| Hoist floor probe | 83 to 97 under the spot, box Z 5 | uu | decoded | 0x11211130 |
| Hoist room | 126 behind, column 456 up (0.96), 446 up | uu | decoded | 0x11211130 |
| Hoist wall probes | 75 toward the wall at 69, 230, 345, box (35, 35, 15) | uu | decoded | 0x11211130 |
| Hoist quick barrier | zone (37, 0, 210), radius 40 | uu | decoded | 0x11211130 |
| CheckIfBarrier hang point | 8 toward the wall, the LG height less m_LedgeGrabArmsZone.Z | uu | decoded | 0x11211130 |
| CheckIfBarrier barrier zone | (0, 0, 512 - defH + 96), radius 150 | uu | decoded | 0x112108f0 |

## Evidence

- Editor: execAreaIsSafeToInteract 0x111e6d20, CheckIfCanInteractWithMate 0x111effc0 (body to 0x111f0640),
  execCheckIfCanStartSHL 0x112125a0, the SHL search 0x112122e0, the SHL spot 0x11211c50, the room test 0x11211b10,
  FindHoistClimbType 0x11211130, execFindHoistClimbType 0x11215680, CheckIfBarrier 0x112108f0, closest point on a
  segment 0x10fde7a0, LG vtable 0x114ed6e4 (slot 0x1c kind 8, slot 0x38 the two ends).
- Game: execAreaIsSafeToInteract 0x10aad9f0, execCheckIfCanInteractWithMate 0x10aae290, execCheckIfCanStartSHL
  0x10ae6ff0, execFindHoistClimbType 0x10ae5150.

## Open

1. The pawn list the mate search walks (a level array at 0x5c) is taken as the level's SBasePawns in spawn order.
