# GE queries and traversal tests

Units:
- eng:FGE (the GE element classes: POLE, FENCE, HOH, LG, LADDER, PIPE, ZL)
- eng:ULevel::GetGEInBox (partition query)
- eng:ASBasePawn::FindArmsGE
- nat:SBase.SPlayerController.CheckArmsZone
- nat:SBase.SPlayerController.CheckFeetZone
- nat:SBase.SPlayerController.CheckBackFeetZone
- eng:ASPlayerController::CheckLedgeArms (game 0x10aa4420)
- eng:ASPlayerController::CheckHOHArms, CheckLadderArms, CheckPipeArms, CheckZipArms, CheckFenceTopArms
- eng:ASPlayerController::CheckLedgeGrabFeet, CheckLadderFeet, CheckPipeFeet, CheckBackLedgeGrabFeet
- nat:SBase.SPlayerController.TestTopLedge (eng:ASBasePawn::TestTopLedge)
- nat:SBase.SPlayerController.bTestTopLadder
- nat:SBase.SBasePawn.CheckFence (eng:ASBasePawn::CheckFence)
- nat:SBase.SPlayerController.CheckBarrier
- nat:SBase.SPlayerController.CheckWallJump
- nat:SBase.SPlayerController.CheckGEForWallJump

## Purpose

How the natives find a GE near the pawn and decide whether a grab, climb or exit is allowed. All the "zone"
natives share one search; each kind then has its own test of position, facing, speed and clearance, ending in a
script event. The file format of the GE list is in docs/spikes/C3-ge-list.md.

## Data

GE element: kind, ID byte (activation), P1, P2, and for LADDER and PIPE a third point P3 (the side point 10 uu
toward the climber). Element methods used here:

| Method | Rule |
|---|---|
| Distance(p) | distance from p to the segment P1-P2 (clamped to the ends), every kind |
| ClosestPoint(p) -> (C, Dir) | C = closest point of the segment P1-P2 to p. Dir: LG, FENCE, HOH: normalize(P1 - P2) with Z = 0; LADDER, PIPE: normalize(P3 - P1) (toward the climber); ZL: from the high point to the low one, normalised; POLE: (0, 0, 1) |
| Connected(other) | two-point kinds: some end of each within 2 uu on every axis; three-point kinds: never |
| Equals(other) | same kind and every point within 0.1 on every axis |

Zone points (offset o, radius r from the zone fields; physics-traversal.md lists the defaults):
- Arms (FindArmsGE, CheckArmsZone): `Z = ToWorld(o + (PrePivot - Default.PrePivot))` with the pawn's full
  rotation.
- Feet (CheckFeetZone): the same offset correction, rotated by the yaw only.
- Back feet: offset (-o.X, o.Y, o.Z) with the yaw only, then Z lowered by ZCDecal[ZoneConfig * 4] when the zone
  configuration is not 0.

Activation: a GE takes part only while `ID == 0` or `(ID & LevelInfo.GEActivationMask) != 0`; the filter is part of
the partition query, so no test below ever sees an inactive GE.

## Rules

### 1. Partition query GetGEInBox(Min, Max)

The query box is `[Z - r, Z + r]` on each axis. The partition clips the box to its bounds (empty result when
they do not overlap) and walks the serialised k-d tree: a split node visits its low child when `Min.axis < split`
and its high child when `split <= Max.axis` (low first); a leaf adds every GE of its index range. The result is in
that visiting order, a GE may repeat, and it is then filtered by activation. Callers then measure Distance and
keep what is within r, so the tree only matters for which far GEs are excluded and for the order of ties.

### 2. FindArmsGE(out Best, out Second) (pawn method)

Nothing when m_CurrentArmsRadius is 0. Candidates from GetGEInBox around the arms zone point Z, with
d_i = Distance(Z) and limit r = m_CurrentArmsRadius:
1. In PHYS_OnPipe, Best = the nearest PIPE with d < r; in PHYS_OnPipeH, the nearest HOH with d < r.
2. Otherwise, or when that found nothing, Best = the nearest of any kind with d < r (strictly nearer replaces).
3. Second, scanning every other candidate in order:
   - Best is PIPE and the candidate is HOH, ZL or LG with d < r: Second = it (later ones overwrite).
   - Best is HOH and the candidate is ZL or PIPE with d < r: Second = it, stop.
   - Best is LG and the candidate is PIPE with d < r: Second = it, stop.
   - Same kind, d < r, and Connected(Best) (or both PIPE): Second = it (later ones overwrite).

### 3. CheckArmsZone()

With a Pawn: FindArmsGE; with a Best: ZipP1 = Best.P1, ZipP2 = Best.P2 (for every kind but POLE and FENCE); then
by kind with the arms zone point Z and radius: HOH CheckHOHArms, LG CheckLedgeArms, LADDER CheckLadderArms, PIPE
CheckPipeArms, ZL CheckZipArms. FENCE and POLE raise nothing here (the fence top is tested from physOnVerticalPlane).

### 4. CheckLedgeArms(GE, Second) (Ledge, WaistLedge, AutoFeetLedge)

Common: Q = Location + (0, 0, m_LedgeGrabArmsZone.Z). (C, Dir) = ClosestPoint(Q). Fwd = the pawn's forward axis.
Require `Fwd . SafeNormal(C - Location) > 0`, and not (crouched and walking). N = (Dir.Y, -Dir.X, 0), negated when
`Fwd . N > 0` (N points from the wall to the pawn). off = R + 2. LocalV = Velocity in pawn space (X forward).

Walking (Physics == 1): AutoFeetLedge. Top = C.Z + H; Hxy = C.XY + off N.XY; d = Hxy - Location.XY. When
`Fwd . N < -0.7`, `Top - 64 < Location.Z < Top - 40` (the ledge 40 to 64 above the feet), LocalV.X > 300 and
`|d|^2 < (Top - Location.Z)^2 * 0.35`: box (35, 35, 87.5), flags 0x2df, from Location up to
(Location.XY, Top + 1), then from there 112.4 along -N; both clear: AutoFeetLedge(), return.

Not walking: first WaistLedge, then Ledge.
- Waist: Hp = (C.XY + off N.XY, C.Z - 20); L = Location with Z lowered by ZCDecal[ZoneConfig * 4] (the standing
  equivalent); d = Hp.XY - Location.XY. When `Fwd . N < -0.7`, -300 < Velocity.Z < 600, `Hp.Z - 18 < L.Z <
  Hp.Z + 22`, LocalV.X > -1, `|d|^2 < 225` and not bTestBackFeetGrab:
  - standing: box (35, 35, 87.5) from L up to (L.XY, Hp.Z + 114.5), then 56 along -N; both clear:
    WaistLedge(true), return;
  - crouched: box (35, 35, CrouchHeight) from (L.XY, L.Z - (H - CrouchHeight)) up 109.2, then 77 along -N; both
    clear: WaistLedge(false), return.
- Ledge (hang): Nh = normalize(N.XY); Hang = (C.XY + 39 Nh, C.Z - 117) (m_LedgeGrabArmsZone X and Z);
  Ref = Location.Z + H - 87.5. Require min(|C - P1|, |C - P2|) >= 24.5, Velocity.Z > -1270,
  `Hang.Z - 5 < Ref < Hang.Z + 35` and `|Hang.XY - Location.XY|^2 < 830`. Box (35, 35, 87.5), flags 0x2df, from
  (Location.XY, Ref) to Hang, then from Hang up 50; both clear: Ledge().

The ledge native does not compare against LastZipP; the script handlers do.

OpenSCCT's low-ledge floor correction raises the WAIST hold when its downward cylinder sweep reaches a walkable
floor before Hp.Z (physics-traversal.md). Both standing and crouched upward clearance destinations include that
same height adjustment, because the root-motion climb starts at the raised hold. The facing, velocity, distance
and detection windows above remain unchanged. This correction addresses the reproduced MoonD GE531 short hop;
it is not attributed to the decoded native.

### 5. Other arm tests

- CheckHOHArms: (C, Dir) = ClosestPoint(Z), Dir flipped to the pawn's forward; hang height Hz = C.Z -
  m_HandOverHandArmsZone.Z (+ PrePivot change). Only while Falling: both HOH ends more than 35 from C
  horizontally, `Hz - 40 < Location.Z`, 0 < Velocity.Z < 600, horizontal distance^2 from C < 1000:
  m_geoNormal = Dir; HandOverHand().
- CheckLadderArms: (C, N) = ClosestPoint(Z) (N toward the climbing side), m_geoNormal = N. No grab when the top
  is not above `Location.Z + H`, or when crouched and UnCrouch(true) fails. Front spot F = bottom.XY + 35 N, back
  spot B = bottom.XY - 35 N. Near F (within 20): NarrowLadder() when `N . Fwd < -0.7` and
  `N . SafeNormal(Acceleration) < -0.7`, or (Falling or RootMotion) with `N . SafeNormal(Velocity.XY) < -0.7` and
  `N . Fwd < -0.3`. Near B: the same with the signs reversed (> 0.7, > 0.3).
- CheckPipeArms: needs at least 85 of pipe above Location.Z and the pawn at least 85 above the pipe bottom
  (crouched adds 87.5 - H). m_geoNormal = N. When within 20 of bottom.XY + 35 N, `N . Fwd < -0.7` and
  (`N . SafeNormal(Acceleration) < -0.7` or RootMotion): Pipe(bottom + zone.X N raised by H + 2,
  (-N).Rotation() masked to multiples of 4).
- CheckZipArms: (C, Dir) = ClosestPoint(Z); Hang = C - (0, 0, m_ZipLineArmsZone.Z) (zone X is 0). While Falling
  with -1270 < Velocity.Z < 254 and `|Hang - Location|^2 < m_ZipLineArmsRadius^2`: m_geoNormal = Dir;
  ZipLine(Hang, normalize(Dir.XY).Rotation() masked).
- CheckFenceTopArms (called by physOnVerticalPlane): (C, Dir) = ClosestPoint(Z), N as in the ledge test. When the
  fence top C is below Z: box (35, 35, 20) from Location up 127.5, then 75 along -N, then down 198.25, all clear
  (0x2df): FenceTop(C + zone.X N lowered by zone.Z, (-N).Rotation() masked).

### 6. CheckFeetZone()

With a Pawn and a non-zero m_CurrentFeetRadius: GetGEInBox around the feet zone point; Best = nearest with
Distance below the radius (any kind). ZipP = Best points; then LG: CheckLedgeGrabFeet, LADDER: CheckLadderFeet,
PIPE: CheckPipeFeet.

- CheckLedgeGrabFeet (FeetLedge): only while Velocity.Z >= -1. C, N as the ledge test (N flipped only when the
  ledge is ahead). L = Location with the ZCDecal correction; Top = C.Z + 50.5. When `Fwd . N < -0.7`, the ledge is
  ahead, Velocity.Z > -500, `Top <= L.Z < Top + 15`, LocalV.X > -1 and `|C.XY - L.XY|^2 < 1600`: for a merc
  (SPawnType 1) a box (35, 35, 87.5) trace down 51 from L must hit (floor under him); then box from L up to
  (L.XY, Top + 39), then 112.4 along -N; both clear: FeetLedge().
- CheckLadderFeet (TopLadder, from the top): `N . Fwd > 0.9` and `N . SafeNormal(Acceleration) > 0.9`, both ladder
  ends below the feet zone point + 24. Spot P = top.XY - (72 - m_NarrowLadderArmsZone.X) N at Location.Z (+ PrePivot
  change). Box (35, 35, 71.5) 0x243 from (Location.XY, P.Z + 16) to (P.XY, P.Z + 16); box (35, 35, 87.5) from P
  72 along N, then down 192; all clear: m_geoNormal = N, vVandammePos = P, TopLadder().
- CheckPipeFeet (TopPipe, from the top): stick `aForward^2 + aStrafe^2 < 4000^2`, `N . Fwd > 0.7`,
  `N . SafeNormal(Acceleration) > 0.7`, the pipe entirely below the feet zone point. Spot P = top.XY - (R + 15) N
  at Location.Z + 2. With Right = (-N.Y, N.X, 0), choose `side = ((Location.XY - top.XY) cross N).Z <= 0`;
  add `R Right` to P for side true, subtract it otherwise. First trace from (Location.XY, P.Z + 16) to P + 16 Up
  with box (35, 35, Pawn.CollisionHeight - 8). Then boxes (26, 26, Controller.CollisionHeight),
  (35, 35, Controller.CollisionHeight), (35, 35, 87.5): move along N, then -Right,
  then vertically by offsets (96.3, 38.67, -181.7) for side true or (97.3, -39.17, -186.15) otherwise.
  All clear: m_geoNormal = N, vVandammePos = P, TopPipe(side). There is no retry on the opposite side.
  The sideways start offset cancels the descent animation's lateral travel before the pipe hold takes over.

### 7. CheckBackFeetZone() -> bool

Nearest GE to the back-feet point within the current feet radius; when it is an LG: ZipP = its points and
CheckBackLedgeGrabFeet, which writes vBackFeetNormal = the ledge's horizontal normal turned to face the same way as
the pawn's forward axis (`Fwd . n <= 0` keeps (Dir.Y, -Dir.X), else its negative) and returns true. Other kinds or
nothing: false.

### 8. TestTopLedge(bCrouchOnly) -> int

Fwd and Right from the pawn rotation; boxes, flags 0x2df.
1. Unless bCrouchOnly: box (35, 35, 87.5) from Location up 210.8, then 57.8 forward; both clear: result 1.
2. Otherwise (or when 1 failed): box (35, 35, CrouchHeight) from the crouched centre
   (Location.Z - (H - CrouchHeight)) up 206.1, then 75.7 forward; both clear: result 2.
3. With result 1 or 2: vault test, box (35, 35, 44): from (Location.XY, Location.Z + 117 + 45) 95 forward, then
   from there down to Location.Z + 78 (at the forward point): both clear returns 3.
4. With no result: barrier above the ledge (crouched). GE search with the arms zone (37, 0, 210): an LG (best or
   second) 90 to 102 above the current ledge (ZipP1.Z); the point 20 to the left must project inside it and the
   point 75 to the right inside the current ledge; then box (35, 35, H) up to that ledge (+ 2 (CrouchHeight + 1)
   - H), 75 - R to the right, box (35, 35, CrouchHeight) 95 forward, and up to Location.Z + 173; all clear:
   result 4.
Returns the result (0 none, 1 stand, 2 crouch, 3 vault over, 4 barrier above the ledge, crouched).

### 9. bTestTopLadder(out iCanUncrouch) -> bool

Spy: up 192, forward 73.9; merc: up 196, forward 68.8. Box (35, 35, 87.5) up (0x2df), then forward (0x243): both
clear returns true; then iCanUncrouch = 1 when a box (35, 35, 8) from 20 below that point to 79.5 above it is clear
(0x2df), else 0.

### 10. CheckFence(out Normal, out Wall, Pos) -> int

Reach = R + 20; Fwd = yaw forward; P = Pos lowered by ZCDecal[ZoneConfig * 4]. Zero-extent traces, flags 0x1086
(material requested):
1. P to P + Reach Fwd must hit; the material is resolved through modifiers to the base material; the normal is
   turned to face the pawn. Result 1 when that material is bClimbable, 2 when not.
2. P along -Normal by Reach must hit the same plane (normals within 0.01 per axis), with the same material rule.
3. Four corner probes on the face plane at P plus or minus (R + 10) to the side and plus or minus (H - 15) up,
   each traced Reach into the face: each must hit with the same normal and a material; a non-climbable material
   makes the result 2.
4. Wall = the hit actor; return 1 or 2. Any miss returns 0. Only result 1 lets physOnVerticalPlane move; there is
   no GE test in CheckFence.

### 11. CheckBarrier() -> bool

FindArmsGE; Best must be an LG. With the ledge grab zone point, C and N as the ledge test: `Fwd . N < -0.7`, the
barrier top within (-5, 40) of the pawn height reference, LocalV.X > -1, horizontal distance^2 < 900. Box
(35, 35, 22.5): from the pawn to the point 140 beyond the top along -N, 20 to the side and 23.5 above it, then
down 85.2; both clear: ZipP = the LG points, true. (Exact reference height: open question 3.)

### 12. CheckWallJump(X, Y, Z, out HitNormal) and CheckGEForWallJump(X, Y, Z, out GEZPosition)

- CheckWallJump depends on nJumpNumber. With 1 (first jump): three directions (from the arguments, the pawn's
  right axis and its opposite) go through a direction helper, the usable one becomes HitNormal (Z = 0), then two
  wall probes 200 each way along it, started at Location + (153 - 0.4 H) Up (plus the PrePivot change), must both
  hit with the summed distance in [182, 200) and one distance below R + 3. With 2: the same pair of probes along
  the right axis started at Location - 0.4 H Up, window (180, 198) and one distance below R + 3; on success
  vVandammePos = the midpoint of the two wall hits raised by 0.4 H, and HitNormal = the near wall's horizontal
  normal.
- CheckGEForWallJump traces (flags 0x10df) fDistanceForContextualJump along the direction at the pawn height,
  160 higher, and 35 to each side; each must hit a static (PHYS_None) non-climbable surface facing the pawn
  (`N . dir <= -0.94`) at a consistent distance (105 to fDistanceForContextualJump, then within 10 and 12.75 of
  the first); then box checks for room in front, and an LG search in a (60, 60, 300) box above the far point for
  a ledge roughly perpendicular to the direction (`|cos| < 0.342`) more than 87.5 above, with a final clearance
  box (35, 35, 87.5); GEZPosition = that ledge Z (or 200 above when none).

## States

None.

## Constants

| Name | Value | Provenance |
|---|---|---|
| GE connection tolerance | 2 per axis | decoded |
| GE equality tolerance | 0.1 per axis | decoded |
| ledge end margin | 24.5 | decoded |
| ledge hang offsets | 39 out, 117 down (zone), clearance +50, window -5 / +35, distance^2 830, Vz > -1270 | decoded |
| waist ledge | -20, window -18 / +22, 225, Vz in (-300, 600), 114.5 and 56 standing, 109.2 and 77 crouched | decoded |
| auto feet ledge | 40 to 64, LocalV.X > 300, factor 0.35, +1, 112.4 | decoded |
| feet ledge | +50.5, window 15, Vz >= -1 and > -500, 1600, merc floor probe 51, +39, 112.4 | decoded |
| facing | -0.7 grab, 0.9 ladder top, 0.7 pipe top, -0.94 wall jump | decoded |
| HOH grab | 35 end margin, 40, Vz in (0, 600), 1000 | decoded |
| ladder grab | 35 spots, 20 radius, 0.7 / 0.3 | decoded |
| pipe grab | 85 above and below, 35, 20 | decoded |
| zip grab | Vz in (-1270, 254), zone radius 60 | decoded |
| fence top | box (35, 35, 20), 127.5, 75, 198.25 | decoded |
| ladder top | 72 - zone.X, +16, boxes 71.5 and 87.5, 72, 192 | decoded |
| pipe top | R + 15 back, R aside, +2; approach +16 with H - 8; boxes 26 and 35, offsets (96.3, 38.67, -181.7) and (97.3, -39.17, -186.15) | decoded |
| TestTopLedge | 210.8 / 57.8, 206.1 / 75.7, vault 117 + 45, 95, 78, box Z 44; barrier 37 / 210, 90..102, 20, 75, 173 | decoded |
| bTestTopLadder | 192 / 73.9 spy, 196 / 68.8 merc, 79.5, -20, box Z 8 | decoded |
| CheckFence | R + 20, R + 10, H - 15, normal tolerance 0.01 | decoded |
| barrier | 140, 20, 23.5, 85.2, box Z 22.5, 900 | decoded |
| wall jump | 200, 0.4 H, R + 3, windows [182, 200) and (180, 198), 153 | decoded |
| GE wall jump | 105, 160, 35, 10, 12.75, 0.342, 87.5, 50 / 55 / 130 / 210 probes, box (60, 60, 300) | decoded |

## Network

The zone natives run on the controlling side (the script calls them from PlayerMove). The server does not search:
it uses ZipP1, ZipP2, m_geoNormal and vVandammePos sent by the client (state-and-network.md). The one server-side
re-check is TestTopLedge in bServerTestRootMotion (physics-rootmotion-repositioning.md).

## Edge cases

- Two-point GEs connect only by shared ends (2 uu); ladders and pipes never connect, so a ledge-to-pipe chain uses
  the LG+PIPE second-GE rule instead.
- Ledges shorter than 49 can never be grabbed (24.5 from each end); with the clearance boxes the practical minimum
  is higher.
- Several clearance probes (flags 0x243) only see pawns and movers, not BSP or static meshes.

## Evidence

- Editor: FindArmsGE 0x11204510, GetGEInBox 0x1111fc00, partition query 0x11307140 (tree node queries
  0x11309fa0 / 0x1130a100 / 0x1130a270, leaf 0x11309d80), GE element vtables 0x114ed5c0 (ZL), 0x114ed5fc (POLE),
  0x114ed638 (PIPE), 0x114ed6a8 (LADDER), 0x114ed6e4 (LG), 0x114ed720 (HOH), 0x114ed75c (FENCE); execCheckArmsZone
  0x111f2330, execCheckFeetZone 0x111f26c0, execCheckBackFeetZone 0x111ec6d0, CheckLedgeArms 0x111e95d0,
  CheckHOHArms 0x111ed7a0, CheckLadderArms 0x111ec210, CheckPipeArms 0x111ebf10, CheckZipArms 0x111ebcd0,
  CheckFenceTopArms 0x111ea1e0, CheckLedgeGrabFeet 0x111ed2f0, CheckLadderFeet 0x111ecf30, CheckPipeFeet
  0x111ec970, CheckBackLedgeGrabFeet 0x111e4ef0, ASBasePawn::TestTopLedge 0x1120f610, bTestTopLadder 0x111e7c60,
  CheckFence 0x11207de0, barrier test 0x111e8d70, execCheckWallJump 0x111ea780, execCheckGEForWallJump 0x111eaf20.
- Game: CheckLedgeArms 0x10aa4420 (the ledge test the RE+ spike found), its events Ledge 0x10ab2dd0, WaistLedge
  0x10ab2df0, AutoFeetLedge 0x10ab2e20; FindArmsGE 0x10ac90a0; CheckHOHArms 0x10aa8f90, CheckLadderArms 0x10aa7650,
  CheckPipeArms 0x10aa7360, CheckZipArms 0x10aa7140, CheckFenceTopArms 0x10aa4ff0, CheckLedgeGrabFeet 0x10aa8b00,
  CheckLadderFeet 0x10aa8640, CheckPipeFeet 0x10aa8080, TestTopLedge 0x10ae3270, bTestTopLadder 0x10ab0f10,
  CheckFence 0x10aceb90, barrier test 0x10aa3540, CheckIfBarrier 0x10ae5260; natives as in native-contracts.md.

## Open questions

1. How the partition tree is rebuilt in the game when GEs change (it matters only if a port rebuilds instead of
   reading the stored tree). The shipped tree reproduces the original candidate order.
2. Resolved 2026-11-01: CheckPipeFeet game instructions 0x10aa822f..0x10aa8303 choose the side and shift P by R;
   0x10aa8318..0x10aa8397 test the raised approach. The vector cross helper 0x10940540 computes EDX cross ECX.
   0x10aa83ee and 0x10aa84ad read the controller's collision half-height (22 by default) for the horizontal probes.
   Polar Base GE19/20/110 regression traces cover the descent endpoint and the transition into the pipe hold.
3. CheckBarrier's height reference (the barrier top against the pawn, with the PrePivot change) needs a disassembly
   pass like the one done for the ledge test.
4. CheckWallJump's per-direction probe helper (editor 0x111e4ca0) and direction picker (0x111e4df0) are not decoded.
5. CheckIfBarrier (0x112108f0, used by the coop hoist) mirrors TestTopLedge with flags `0x287` minus 1 when a second
   GE is given; not written up here.
