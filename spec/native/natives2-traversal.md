# Back-to-wall, pipe swap and ledge natives (second pass)

Units:
- nat:SBase.SPlayerController.CheckBTW
- nat:SBase.SPlayerController.CheckBTWEyes
- nat:SBase.SPlayerController.CheckBTWCorner
- nat:SBase.SPlayerController.CheckForOtherPipeV
- nat:SBase.SPlayerController.CheckRoomToChangePipeV
- nat:SBase.SPlayerController.bCanTakeLedgeFromPipe
- nat:SBase.SPlayerController.bTestGrabLedgeGeo
- eng:ASPlayerController::CheckPipeFeet (completes physics-ge-queries.md rule 6)
- nat:SBase.SPlayerController.TestTopLedge (result 4, completes physics-ge-queries.md rule 8)
- nat:SBase.SPlayerController.CheckBarrier (completes physics-ge-queries.md rule 11)
- nat:SBase.SPlayerController.CheckWallJump (helpers, completes physics-ge-queries.md rule 12)
- nat:SBase.SPlayerController.CheckGEForWallJump (room boxes, completes physics-ge-queries.md rule 12)

## Purpose

The traversal natives that physics-ge-queries.md left open or only outlined, decoded from the editor build and
checked against the game build (same constants, same branch order). The rules below replace the stand-ins and
the outlines; where a rule only adds detail to physics-ge-queries.md it says so.

## Data

Axes. Two coordinate builders appear in these natives and both give the usual local-to-world mapping: local
(1, 0, 0) is the forward vector of the rotation, (0, 1, 0) its right vector, (0, 0, 1) its up vector. "Fwd",
"Right" and "Up" below always mean that.

Probe flags. Several probes use `0xdf`, plus `0x200` (stop at first hit) unless the controller itself has
bCollideActors. Controllers never collide in Versus (the class default is false), so in practice the flags are
`0x2df`. "World probe" below means a zero-extent line with these flags whose hit only counts when the hit actor
exists and has `Physics == PHYS_None` (level geometry, static meshes; movers and pawns do not count). The hit
normal is then turned to face the probe: if `N . (End - Start) > 0` it is negated.

Pawn sizes. R and H are the pawn's CollisionRadius and CollisionHeight; L is the pawn Location.

## Rules

### 1. CheckBTW(Reverse) -> bool

1. D = Fwd of the full pawn rotation when Reverse is true, -Fwd when it is false. (The script calls
   `CheckBTW(true)` to look for a wall in front of the spy and plays SA_BTW_ENTER; `CheckBTW()` looks behind him:
   SA_BTW_ENTERBACK on entry, and every frame on the wall, where the spy faces away from it.)
2. Probe A: world probe from L to L + 1.5 R D. Miss: false. N1 = its normal.
3. W = horizontal unit vector of -N1 (the yaw of `rotator(-N1)`, pitch and roll 0); S = the right vector of
   that yaw. V = 1.5 R W.
4. Probe B: world probe from (L + 0.5 R S) lowered by 0.5 H, to that point + V. Miss: false. N2 = its normal.
5. Probe C: the same from (L - 0.5 R S) lowered by 0.5 H. Miss: false. N3 = its normal.
6. Probe D: world probe from (L.X, L.Y, L.Z + 0.8 H) to that point + V. Miss: false. N4 = its normal.
7. vWallNormal = SafeNormal(N2 + N3 + N4) (not flattened; N1 only orients the other probes). If that is the zero
   vector, vWallNormal.X = 1. Return true.

### 2. CheckBTWEyes(ToRight) -> int

T = Fwd of the full rotation negated (the wall side); side = Right when ToRight is false, -Right when it is true.
Boxes below are (5, 5, 5) with flags `0xdf` (no stop-at-first-hit).
1. P = L + (R + 4) side + (0, 0, 0.8 H). Box from P to P + 2 R T.
2. If it hits (the wall is still behind the spy at that point, at head height):
   1. Q = L + 5 side. Box from Q down to Q.Z - (H + 31).
   2. Start S = L + (R + 4) side at L.Z. If the first box hit a floor at F, the end is (S.X, S.Y, F.Z - 31),
      else (S.X, S.Y, L.Z - H - 31).
   3. Box from S to that end: a hit (the floor goes on) returns 1, a miss returns -1 (a drop ahead).
3. If it misses (the wall ends on that side): a zero-extent line from L to L + 100 side; clear returns 0 (room
   to peek round the end), blocked returns -1.

So 1 is "keep sliding", 0 is "end of wall with room to look round it", -1 is "stop" (blocked or a drop).

### 3. CheckBTWCorner(HitNormal) -> bool

1. V = -HitNormal * 1.5 R. World probe from L to L + V. Miss: false. N = its normal.
2. The wall must be the same plane as HitNormal: `|HitNormal x N| <= 0.1` (a cross product length, so normals
   within about 5.7 degrees, either sign). Otherwise false.
3. Then probes B, C and D of CheckBTW rule 1 with W the horizontal unit vector of -N; all three must hit; true.
   vWallNormal is not written here (the script copies HitNormal itself). The current vWallNormal is not used:
   any corner angle passes as long as the new wall is a full-height flat wall.

### 4. CheckForOtherPipeV(out vZipP1, out vZipP2, out vNormal) -> bool

1. Q = L + (m_PipeArmsZone.X + 16) Fwd (full rotation).
2. GE query in the box Q +- (5, 5, 5); among the results the nearest by GE Distance(Q) strictly below 10.
3. True only when that nearest one is a PIPE: vZipP1 = P1, vZipP2 = P2, vNormal = SafeNormal(P3 - P1). Any other
   kind or nothing: false (outputs untouched).

### 5. CheckRoomToChangePipeV(bToLeft) -> bool

Boxes (35, 35, 87.5), flags `0x2df`, each must be clear. d = m_PipeArmsZone.X + 8, f = 2 m_PipeArmsZone.X + 16;
S = d Right when bToLeft is false, -d Right when it is true.
1. From L to L + S.
2. From L + S to L + S + f Fwd.
3. From L + S + f Fwd to L + f Fwd.
All clear: true. (A path round the current pipe to its far side.)

### 6. bCanTakeLedgeFromPipe(bOnRight) -> bool

1. s = -1 when bOnRight, else +1. The arms zone is replaced for one FindArmsGE call by (40, 40 s, 90.3) with
   radius 60, then the radius is restored (the zone is restored on exit).
2. G = Best when it is an LG, else Second when Best is not an LG and Second is; otherwise false.
3. Q = L + 14 Fwd - 45 s Right (yaw from the pawn rotation). C = G.ClosestPoint(Q); both segment ends must be more
   than sqrt(35) from C (the native compares squared distances with 35).
4. Boxes (35, 35, 87.5), flags `0x2df`, must be clear: from L to E = (L.X, L.Y, L.Z - 27) - 45 s Right, then from
   E to E + 14 Fwd.
5. True: NewZipP1 = G.P1, NewZipP2 = G.P2.

Note the search zone and the clearance are on opposite sides (bOnRight searches at local Y -40 and clears to
+45 Right). This is how both builds read.

### 7. bTestGrabLedgeGeo() -> bool

1. FindArmsGE with the arms zone (37, 0, 210) and the current radius; zone restored.
2. G = Best when LG, else Second when LG; otherwise false.
3. `90 < |ZipP1.Z - G.P1.Z| < 102`.
4. The projections of the pawn Location on the current ledge line (ZipP1, ZipP2) and on G's line are within 10
   horizontally (squared distance below 100).
5. The points C - 20 Right and C + 20 Right, where C is the controller's own Location (not the pawn's), both
   project strictly inside G's segment (parameter in (0, 1)).
6. True; nothing is written.

### 8. CheckPipeFeet(GE, ZonePoint) (completes physics-ge-queries.md rule 6)

1. Stick: `aStrafe^2 + aForward^2 < 1.6e7` (the controller's raw input, not Acceleration).
2. C, N = GE.ClosestPoint(ZonePoint) (N toward the climber); P1, P2 = the GE points.
3. `N . SafeNormal(Fwd) > 0.7` and `N . SafeNormal(Acceleration) > 0.7`.
4. ZonePoint.Z above both P1.Z and P2.Z.
5. Spot P = (P2.XY - (R + 15) N.XY, L.Z + 2). P2 is used whatever its height.
6. A = SafeNormal((L - P2) with Z = 0); side = (A x N).Z. P is moved by R across the pipe: by
   R (-N.Y, N.X, 0) when side <= 0, by R (N.Y, -N.X, 0) when side > 0.
7. Box (35, 35, H - 8) from (L.X, L.Y, P.Z + 16) to (P.X, P.Y, P.Z + 16): clear.
8. With (a, b, c) = (96.3, 38.67, -181.7) when side <= 0, (97.3, -39.17, -186.15) when side > 0, and h the
   controller's CollisionHeight (22 in every Versus class):
   - box (26, 26, h) from P to P + a N: clear;
   - box (35, 35, h) from there by b (N.Y, -N.X, 0): clear;
   - box (35, 35, 87.5) from there down by c: clear.
9. m_geoNormal = N, vVandammePos = P, TopPipe(side <= 0).

### 9. TestTopLedge result 4 (completes physics-ge-queries.md rule 8)

Results 1 and 2 as in physics-ge-queries.md. With bCrouchOnly the native returns the result of test 2 at once:
no vault test and no barrier test. The vault test (3) starts at L.Z + m_LedgeGrabArmsZone.Z + 45 (117 + 45 with
the default zone) and returns 3 only when both boxes are clear; if it fails the earlier 1 or 2 stands. Result 4
runs only when 1 and 2 both failed and bCrouchOnly is false:
1. FindArmsGE with the arms zone (37, 0, 210); zone restored. G = Best when LG, else Second when LG.
2. `90 < |ZipP1.Z - G.P1.Z| < 102`.
3. L - 20 Right projects strictly inside G's segment; L + 75 Right projects strictly inside the current ledge
   (ZipP1, ZipP2).
4. Box A (35, 35, H) from L up to A = (L.X, L.Y, G.P1.Z + 2 (CrouchHeight + 1) - H): clear.
5. Box (35, 35, H) from A to A + (75 - R) Right: clear.
6. Box (35, 35, CrouchHeight) from A' = A raised by H - CrouchHeight (not from the end of step 5) to
   A' + 95 Fwd: clear.
7. Box (35, 35, CrouchHeight) from A' + 95 Fwd up to Z = L.Z + 173: clear. Result 4.

### 10. CheckBarrier (completes physics-ge-queries.md rule 11)

execCheckBarrier: FindArmsGE; Best must be an LG; the zone point is the ledge-grab zone (m_LedgeGrabArmsZone with
the PrePivot change, through the pawn's ToWorld) and the radius m_LedgeGrabArmsRadius. Then:
1. C, Dir = Best.ClosestPoint(zone point). Fwd must point at it: `Fwd . SafeNormal(C - L) > 0`.
2. N = (Dir.Y, -Dir.X, Dir.Z), negated when `Fwd . N > 0` (so it faces the pawn). G = C + (R + 2) N.
3. Ref = L + (PrePivot - Default.PrePivot) (the height reference).
4. Conditions, in order: `N . Fwd < -0.7`; `G.Z - 40 < Ref.Z < G.Z + 5` (the barrier top from 5 below to 40 above
   the reference); LocalV.X > -1 (velocity in pawn space); `(G.X - L.X)^2 + (G.Y - L.Y)^2 < 900`.
5. Box (35, 35, 22.5), flags `0x2df`, from S = (Ref.X, Ref.Y, C.Z + 23.5) to
   E = S - 140 N + 20 (N x Z): clear. Then from E down 85.2: clear.
6. ZipP1 = Best.P1, ZipP2 = Best.P2; true.

### 11. CheckWallJump helpers (completes physics-ge-queries.md rule 12)

Wall probe (editor 0x111e4ca0): zero-extent line, flags `0x1084`, from Start to End; it writes the hit location
and the hit normal (turned to face Start). It passes when the hit actor exists, has PHYS_None, and is not
(the LevelInfo with a material whose bNoSplitJump is set).

Direction picker (editor 0x111e4df0, used with nJumpNumber 1): for a direction D it probes from
(L + (R - 1) D) lowered by 0.4 H to 18 further along D, writes the hit normal into D and returns the probe result.

execCheckWallJump(X, Y, Z, out HitNormal) with X, Y, Z the script's axes:
- nJumpNumber 1: pick X, Y and -Y (results r1, r2, r3). Exactly one of r2, r3 passing gives HitNormal = that
  side's normal with Z = 0 and sets the result to true; neither side but r1 passing gives X's normal with Z = 0
  and true; both sides passing (or nothing) leaves HitNormal as passed in and the result false. Then, in every
  case, S = L + (153 - 0.4 H) Z + (PrePivot - Default.PrePivot); wall probes from S to S + 200 HitNormal and to
  S - 200 HitNormal must both pass (else false), with distances d1, d2 from S. False when both are at least
  R + 3, false when `d1 + d2 >= 200` or `d1 + d2 < 182`. vVandammePos is not written.
- nJumpNumber 2: S = L - 0.4 H Z plus the PrePivot change; probes along +Y and -Y, 200 each, must pass. If
  d2 >= R + 3: if d1 >= R + 3 false, else HitNormal = (N1.X, N1.Y, 0); if d2 < R + 3: HitNormal = (N2.X, N2.Y, 0)
  (the second probe wins when both are near). Then `180 < d1 + d2 < 198` or false. True: vVandammePos =
  midpoint of the two hit locations, raised by 0.4 H.
- Other jump numbers: false.

### 12. CheckGEForWallJump room boxes (completes physics-ge-queries.md rule 12)

With D = X (the direction), K = fDistanceForContextualJump, S = Z x X (the side axis):
1. Probe 1, flags `0x10df`, L to L + K D: hit actor with PHYS_None, material missing or not bClimbable, facing
   `N . D <= -0.94`, horizontal distance d1 from L to the hit in [105, K].
2. Probe 2 from (L.X, L.Y, L.Z + 160), same tests without the material test, horizontal distance within 10 of d1.
3. Probe 3 from (L.X, L.Y, L.Z + 72.5) + 35 S, tolerance 12.75; probe 4 from that point - 70 S, tolerance 12.75;
   W4 = probe 4's hit location.
4. Box (35, 35, 87.5), flags `0x2df`, from L to L + (d1 - 50) D: clear.
5. Box (35, 35, 60) from L + (d1 - 55) D down 50: must hit (floor at the wall). When d1 > 130 the same box at
   d1 - 130 must hit, and when d1 > 210 also at d1 - 210 (a clear one fails the test).
6. Q = W4 + 35 S. LG search in [Q - (30, 30, 0), Q + (30, 30, 300)]: an LG counts when
   `|(P1 - P2) . PawnFwd| / |P1 - P2| < 0.342`, its P1.Z is more than 87.5 and less than the best so far (start
   300) above Q.Z, and the horizontal distance from the pawn to the closest point of its segment is within 12.75
   of d1. GEZ = the chosen P1.Z, or Q.Z when none.
7. T = L + (d1 - 40) D. Box (35, 35, 87.5) from T up to GEZ when `GEZ - T.Z >= 100`, else up to T.Z + 200; clear
   returns true with GEZPosition = GEZ.

## Constants

| Name | Value | Provenance |
|---|---|---|
| BTW wall probes | 1.5 R reach, side offsets 0.5 R at -0.5 H, head 0.8 H, flags 0xdf (+0x200) | decoded |
| BTW corner plane test | cross length 0.1 | decoded |
| BTW eyes | side R + 4 at 0.8 H, reach 2 R, box 5, floor probe 5 aside to H + 31 down, floor offset 31, peek 100 | decoded |
| pipe swap | zone X + 16, query box 5, distance 10; room d = X + 8, f = 2 X + 16 | decoded |
| ledge from pipe | zone (40, 40, 90.3) radius 60, 14 forward, 45 aside, 27 down, end margin squared 35 | decoded |
| grab ledge geo | zone (37, 0, 210), window 90..102, alignment squared 100, 20 aside | decoded |
| pipe top | stick 1.6e7, R + 15, +2, R aside, first box (35, 35, H - 8) at +16, controller height 22 | decoded |
| barrier | R + 2, window -5..40 from the reference, 900, 23.5, 140, 20, 85.2, box Z 22.5 | decoded |
| wall jump | picker R - 1, 18, -0.4 H; probe flags 0x1084; 153; 200; R + 3; [182, 200) and (180, 198) | decoded |
| GE wall jump | 105, 160, 72.5, 35, 10, 12.75, boxes 50 / 55 / 130 / 210 and (35, 35, 60) down 50, 40, 100, 200 | decoded |

## Network

All of these run on the controlling side only; their results reach the server through the replicated
ZipP / m_geoNormal / vVandammePos and the action numbers (state-and-network.md).

## Edge cases

- CheckBTW uses the full pawn rotation for its first probe; with a pitched pawn the first probe is tilted.
- CheckBTWEyes returns -1 both for a blocked side and for a drop beside the wall end.
- bTestGrabLedgeGeo reads the controller's Location for its side points.
- CheckPipeFeet sizes two boxes by the controller's CollisionHeight (22), not the pawn's.
- TestTopLedge result 4 starts its forward box from the top of the first box, not from the side-stepped point.
- CheckWallJump with nJumpNumber 1 still probes along the incoming HitNormal when the picker fails.

## Evidence

- Editor: execCheckBTW 0x111e5640, execCheckBTWEyes 0x111e5dc0, execCheckBTWCorner 0x111e5050,
  execCheckForOtherPipeV 0x111e92a0, execCheckRoomToChangePipeV 0x111e4960, execbCanTakeLedgeFromPipe 0x111e81e0,
  execbTestGrabLedgeGeo 0x111e6980, CheckPipeFeet 0x111ec970, ASBasePawn::TestTopLedge 0x1120f610,
  execCheckBarrier 0x111ea600 and its test 0x111e8d70, execCheckWallJump 0x111ea780 with the probe 0x111e4ca0 and
  picker 0x111e4df0, execCheckGEForWallJump 0x111eaf20; GetRotationCoords 0x10f32d60, coordinate transform
  0x10eb2b10, segment parameter 0x10fde6a0.
- Game (constants and order checked): CheckBTW 0x10aa97e0, CheckBTWEyes 0x10aaa000, CheckBTWCorner 0x10aa91c0,
  CheckForOtherPipeV 0x10aa4110, CheckRoomToChangePipeV 0x10aa3e10, bTestGrabLedgeGeo 0x10aac090,
  bCanTakeLedgeFromPipe 0x10ab1de0, CheckPipeFeet 0x10aa8080, TestTopLedge 0x10ae3270.
- Field and bit names from the reflection dumps; script callers from the decompiled SBase package (local only).
- Scripts: re/ghidra/scripts/natives2 (decompiles and disassembly in local/re-out/c/natives2).

## Open questions

1. bCanTakeLedgeFromPipe's opposite-side zone and clearance: decoded as is in both builds; a pipe-to-ledge
   golden would show whether the clip relies on it.
2. CheckBTWEyes' -1 for a drop is inferred from the probe geometry; a BTW golden at a wall end over a drop would
   confirm the script reaction.
