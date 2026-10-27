# Traversal physics: pipe, hand over hand, ladder, zip line, fence, ledge

Units:
- eng:APawn::physOnPipe
- eng:APawn::physOnPipeH
- eng:APawn::physOnLadder
- eng:APawn::physLinear
- eng:ASBasePawn::SetZipSpeed
- eng:ASBasePawn::UnZipSpeed
- nat:SBase.SBasePawn.CanUnZipSpeed (exec over UnZipSpeed(true))
- eng:ASBasePawn::physOnVerticalPlane
- eng:APawn::physLedge
- eng:ASBasePawn::TestTopPipe

## Purpose

The SCCT GE modes. Each holds the pawn on its GE (ZipP1, ZipP2, m_geoNormal), moves it with the script's
Acceleration conventions, turns it to face the GE, and raises the script events that end or chain the move.
They do one move per call (no sub-steps) and always end with `Velocity = (Location - start) / dt` unless noted.

## Data

GE points and zones are SBasePawn fields (defaults):

| Zone | Offset (pawn space) | Radius |
|---|---|---|
| m_PipeArmsZone | (37, 0, 90.3) | 25.4 |
| m_HandOverHandArmsZone | (0, 0, 128) | 50 |
| m_NarrowLadderArmsZone | (37.5, 0, 100.5) | 19.05 |
| m_ZipLineArmsZone | (0, 0, 116) | 60 |
| m_FenceArmsZone | (45.72, 0, 90) | 25 |
| m_LedgeGrabArmsZone | (39, 0, 117) | 40 |
| m_NormalFeetZone | (39, 2, -85) | 60 |

ToWorld(p) below means Location + Rotation applied to p (the actor's local-to-world transform). Proj(p, A, B) is
the projection of p on the infinite line through A and B. Snap(target) means: Move = target - Location, limited to
500 dt (taken whole when `|Move| <= 500 dt` or `|Move| <= 0.1`).

The script's Acceleration encodes the mode input (native-contracts.md): pipe Z climb, X slide; ladder Z climb,
X slide, Y rung stop; zip v m_geoNormal; HOH X along the pipe; ledge 80 straf Y.

## Rules

### physOnPipe(dt) (6)

1. Clear bSimulateGravity. Q = ToWorld(m_PipeArmsZone); C = Proj(Q, ZipP1, ZipP2).
2. Pipe swap: when NewZipP differs from ZipP by more than 1 in X or Y of either point, m_geoNormal = the unit
   direction between the new points, oriented from the new point nearer the current pipe's line (3D point to
   infinite line distance; on a tie NewZipP1 - NewZipP2) to the farther, flipped when the lower end of the current
   pipe is nearer the pawn height (`|P.Z - Location.Z - 20|`, lower strictly less than upper).
3. Hold: Target = C + zone.X m_geoNormal + zone.Y (Z x m_geoNormal) - (0, 0, zone.Z); Snap(Target) with
   rotation turned toward -m_geoNormal: yaw by FixedTurn at 16384 dt per call (pitch and roll of that rotation
   masked to multiples of 4); bCollideWorld off for this move. During a swap, when the yaw reaches the target,
   ZipP = NewZipP and Controller.TopSynchro().
4. Vertical move: when `|Acceleration.X| > 10` (slide): Velocity.Z += Acceleration.X dt, clamped to
   [-450, 0], Delta = Velocity dt. Else Delta = (0, Acceleration.Y dt, Acceleration.Z dt).
5. Going down (Delta.Z <= 0):
   - sliding: when `(m_HandOverHandArmsZone.Z + Location.Z) - min(ZipP.Z) < 210`: Controller.TopSynchro() (the
     bottom);
   - climbing down: GE search with the arms zone set to (40, 0, -40.3); only the second GE of the search is looked
     at (the first is the pipe itself). A HOH within `dz > -210` and horizontal
     distance^2 < 600 of the HOH zone point: Acceleration.Z = (proj.Z + 207.2 - HOHzone.Z - Location.Z) / dt;
     on the controlling side, two box traces (35, 35, 87.5) with flags 0x243 (down 207.2, then forward 97.9) that
     are both clear give NewZipP = the HOH points and Controller.HandOverHand().
6. Going up (Delta.Z > 0), climbing: GE search with the arms zone (40, 0, 90.3); only the second GE is looked at.
   P is its closest point to the zone point (before this call's move; game 0x10ad1908 to 0x10ad1b21).
   - ZL: dz = P.Z - (Location.Z + zip zone.Z), and the horizontal test uses P - (Location + zip zone) with the
     zone offset as it is (not turned with the pawn). With `dz < 10` and distance^2 < 400: Delta.Z -= dz - 5.63;
     on the controlling side ZipP = the ZL points and Controller.ZipLine(0, 0). The top test still runs.
   - HOH: dz = P.Z - HOH zone point Z. With `dz < -6` and distance^2 < 600: Delta.Z += dz + 6; on the controlling
     side NewZipP = the HOH points, Controller.HandOverHand(). A HOH second GE skips the top test this call
     whether it joined or not.
   Top test: Top = max(ZipP1.Z, ZipP2.Z) and T = the hold target's Z before zone.Z is taken off; when
   `Top < T + Delta.Z` and Acceleration.Z > 10: Delta.Z = Top - T; on the controlling side TopPipe(bIsLeftPart)
   if TestTopPipe(dz, standing), else TopPipeCrouch(bIsLeftPart) if TestTopPipe(dz, crouched), else an LG as the
   second GE of a search with zone (40, 0, 90.3) whose closest point to Q (step 1, the start of the call) is
   within horizontal distance^2 1000 and 2 in Z of Q: Controller.Ledge().
7. MoveActor(Delta, Rotation); a hit with Normal.Z > 0 calls Controller.NotifyHitWall(Normal, Actor).

### TestTopPipe(unused, dz, bCrouch)

Box (35, 35, 87.5), or (35, 35, CrouchHeight) when crouched; flags 0x2df; all four segments must be clear, starting
at Location + (0, 0, dz): 30 along the pawn's right axis, then up 191.5 (crouched 156.9), then 15 more to the
right, then 79.4 (crouched 92.7) forward. The first argument (bIsLeftPart in the callers) is not read.

### physOnPipeH(dt) (14, hand over hand)

1. Clear bSimulateGravity. ZipP = NewZipP when they differ by more than 1 in X or Y.
2. Q = Location + m_HandOverHandArmsZone + (0, 0, H - Default.H); Dir = SafeNormal(ZipP1 - ZipP2), flipped to
   point along the pawn's forward axis. C = Proj(Q, ZipP1, ZipP2).
3. Hold: Snap(C - (0, 0, zone.Z + H - Default.H)); yaw FixedTurn at 1000 per call toward Dir's yaw (masked to
   multiples of 4); pitch and roll 0. Before the move, when the snap reaches the target and the current yaw (from
   the last call) equals that yaw: Controller.TopSynchro(). After it, a blocked hold move: Controller.NoGE().
   Neither ends the call.
4. Ends: E0 = distance from C to the nearer GE point; E1 = the same after the move (Q + Acceleration.X Dir dt).
   When E1 < E0 (moving toward an end): limit L = 27, or 95 when 0 < Acceleration.X < 120 (legs-over pose);
   search GEs with zone (40, 0, 90.3) (only here: without this search there is no chain in step 6); with
   `|Acceleration.X| >= 120` and a ZL second GE: L = 0, and on the controlling side with E1 < 3, ZipP = the ZL
   and Controller.ZipLine(0, 0). Then scale Acceleration by `(L - E0) / (E1 - E0)` when E0 > L > E1, or zero it
   when E0 <= L and E1 < L.
5. Move Delta = Acceleration.X Dir dt. A zero Delta ends the call here (no move, no wall event, no chain). In the
   legs-over pose (0 < Acceleration.X <
   120): a zero-extent trace (flags 0x201) from Location to Location + Delta + 140 Dir. A hit on a pawn in zone
   config 3 that faces us (`Dir . its rotation vector <= 0`) zeroes the move; any other hit is re-tested to
   Location + Delta + 70 Dir, and a hit there zeroes the move.
6. MoveActor(Delta). On the controlling side: a hit calls NotifyHitWall. With a second GE from step 4 (a PIPE as
   the first GE replaces it):
   - both GEs HOH and `|Acceleration.X| >= 120` (read before step 4's scaling): when the second's points equal
     ZipP1 and ZipP2 exactly the first is used; its closest point to Q within horizontal 31 (distance^2 < 961):
     NewZipP = its points (there is no perpendicular test); Controller.OrthoPipeH(bToLeft): V runs from the HOH end nearer the pawn's Location
     (horizontal distance; P1 to P0 on a tie) to the other end, and bToLeft = `(Fwd x V).Z <= 0` with Fwd the
     pawn's X axis (game 0x10ad0b9e to 0x10ad0cca);
   - a PIPE: with the pipe zone point Q2 = ToWorld(m_PipeArmsZone), horizontal distance from Q2 to its closest
     point within `zone.X - 34` when a pipe end is at or below Location.Z (going down a pipe), or within
     `48 - zone.X` and two clear (35, 35, 87.5) traces
     (0x243: 86.35 forward, then up 205) otherwise: m_geoNormal = the pipe normal, ZipP = the pipe points,
     Controller.Pipe(closest point).

### physOnLadder(dt) (13)

1. Clear bSimulateGravity. N = m_geoNormal; Q = ToWorld(m_NarrowLadderArmsZone); C = Proj(Q, ZipP1, ZipP2);
   flip N when it points from the pawn to the ladder in XY (`(C - Location) . N` over X and Y above 0), so N points
   toward the pawn.
2. Hold: horizontal Snap only (target = C + zone.X N + zone.Y (Z x N), Z unchanged); hand height
   Hz = target Z (before the horizontal cut) + 2. Yaw FixedTurn at 1000 per call toward -N (masked to multiples of 4).
3. Rungs: h = Location.Z - min(ZipP.Z) - 28; rung = 64 trunc(h / 64) (the float to int helper
   0x10b83aa0 rounds toward zero); off = h - rung, so off is 0 to 64 above the first rung.
   - `|Acceleration.Y| > 10` (stop on a rung): off -= 64 when off > 32; Velocity = 0;
     Velocity.Z = -100 when off >= 0, +100 otherwise, or -off / dt when `|off| < 100 dt`; zero below 2.
   - Else `|Acceleration.X| > 10` (slide): Velocity.Z += Acceleration.X dt; when Acceleration.X < -524.5 and
     Velocity.Z > -200, Velocity.Z = -200; floor at -700. With Acceleration.X > 0 (slowing): at most -64, and
     when the next rung is reached this frame (`off < -dt Velocity.Z`), Velocity.Z = -off / dt and
     Controller.TopSynchro().
   - Else Delta = (0, 0, Acceleration.Z dt).
4. Top: when `max(ZipP.Z) < Delta.Z + Hz - 2` and Acceleration.Z > 0.1: Delta.Z = max - Hz + 2 and
   Controller.TopLadder().
5. MoveActor(Delta); a hit calls Controller.NotifyHitWall.

### physLinear(dt) (7, zip line) with SetZipSpeed and UnZipSpeed

1. Clear bSimulateGravity. Zone = m_ZipLineArmsZone, with Z lowered by Default.CollisionHeight - ZipHeight in the
   speed pose (bIsZipSpeed). C = Proj(ToWorld(zone), ZipP1, ZipP2).
2. When the cable has a horizontal extent: Snap(C - (0, 0, zone.Z)) with yaw FixedTurn at 3000 toward the
   cable's horizontal direction (masked to multiples of 4). A hit on that move: an SBasePawn gets
   Controller.DropAttack(it); anything else Controller.NoGE().
3. Speed: Down = the cable direction toward its lower point. Velocity opposite to Down: Velocity = 0. With
   s = |Velocity|: when s >= GroundSpeed, Acceleration = 0.03 (GroundSpeed - s) Velocity and
   Velocity += Acceleration dt (eases down to the target); else MycalcVelocity(SafeNormal(Acceleration), dt,
   GroundSpeed, 8, 0, 1).
4. MoveActor(Velocity dt); a hit sets bSimulateGravity and calls Controller.NotifyHitWall(Normal, Actor).

SetZipSpeed: when CollisionHeight != ZipHeight and the pawn is not a simulated proxy: d = CollisionHeight -
ZipHeight; SetCollisionSize(CrouchRadius, ZipHeight); FarMoveActor(Location + (0, 0, d)) (keeps the top of the
cylinder); PrePivot = Default.PrePivot - (0, 0, d); bIsZipSpeed = true; ZoneConfig = ZoneConfigForClient = 2.
UnZipSpeed(bTest): d = Default.CollisionHeight - CollisionHeight (0 for proxies); restore the default cylinder and
FarMoveActor(Location - (0, 0, d), bTest). Success without bTest: PrePivot = default, bIsZipSpeed = false,
ZoneConfig = ZoneConfigForClient = 0. On failure, or always with bTest, the zip cylinder and location are put
back. The result is "the move fits" for a test, else the new bIsZipSpeed. CanUnZipSpeed is the test form.

### physOnVerticalPlane(dt) (8, fence)

1. Target = Location + Acceleration dt.
2. Climbing (Acceleration.Z > 0): GE search with the current arms zone; a FENCE on the controlling side that
   passes the fence-top test (physics-ge-queries.md, CheckFenceTopArms, which raises FenceTop) sets
   Acceleration.Z = 0.
3. CheckFence(Target) == 1 (a climbable fence face there): horizontal snap to stay R + 2 off the face,
   `S.XY = Hit.XY + (R + 2) N.XY - Location.XY` (zero below 0.1); Delta = S + Acceleration dt; rotation faces -N;
   MoveActor(Delta). A hit with Normal.Z > 0.707: Controller.MyNotifyLanded(Normal, (1 - Time) dt, Actor).
   Otherwise (no climbable face): Acceleration = 0, no move.

### physLedge(dt) (9)

1. L = ZipP1 - ZipP2 with Z = 0; nothing when its length is 0. n = the horizontal ledge normal on the side of the
   point 100 behind the pawn (`Location - 100 Fwd`), so n points away from the wall; face = (-n).Rotation().Yaw
   masked to multiples of 4.
2. Hands: zone = m_NormalFeetZone for TypeLedge FEET, else m_LedgeGrabArmsZone; Q = (Location - zone.X n) with
   Z = Location.Z + zone.Z; C = Proj(Q, ZipP1, ZipP2); E0 = distance from C to the nearer ledge point.
3. Hang target T by TypeLedge: NORMAL (0) T = C + 39 n, T.Z = C.Z - m_LedgeGrabArmsZone.Z; WAIST (1) T.XY =
   Location.XY, T.Z = C.Z - 20; BARRIER (2) T.XY = Location.XY, T.Z = Location.Z when `C.Z - Location.Z < 10`, else
   C.Z - 10; FEET (3) T.XY = Location.XY, T.Z = C.Z + 50.5.
4. Snap along n only: D = ((T - Location) . n) n with D.Z = T.Z - Location.Z; moved whole when within 500 dt (or
   0.1), and then when the yaw already equals face: Controller.TopSynchro(); else 500 dt SafeNormal(D).
   Rotation (0, FixedTurn(yaw, face, 3000), 0); MoveActor; pitch and roll set to 0; a blocked snap:
   Controller.NoGE().
5. Other TypeLedge values: Velocity = 0, return.
6. Shimmy (NORMAL): Q' = Q + Acceleration dt, C' = Proj(Q', ...), E1 its end distance.
   - E1 >= E0: when equal and below 36, push away from the nearer end by 36 - E1 (MoveActor).
   - E1 < E0: crossing 36 (E1 < 36 < E0): Acceleration *= (36 - E0) / (E1 - E0); both below 36: Acceleration = 0.
   Velocity = 0. Delta = Acceleration dt. Head room at the new spot: a box (35, 35, 87.5) trace from
   Location + Delta up 50 (0x2df) that hits zeroes Delta. MoveActor(Delta); a hit sets bSimulateGravity and calls
   NotifyHitWall (Normal.Z <= 0.707) or MyNotifyLanded otherwise.
7. Still on a ledge (controlling side): set the current arms zone to the ledge grab zone and radius, search GEs;
   the best or second GE must be an LG whose segment contains C' (`0 < (C' - P1) . (P2 - P1) < |P2 - P1|^2`),
   else Controller.NoGE() and return.
8. Corners and chaining: search again with the zone (37, s 35, 90.3), s = +1 or -1 by the side of the input
   (0 with no input):
   - moving toward an end (Acceleration non-zero, E1 < 37 and E1 < E0) with two LGs found that share an end
     point (within 2): U and V the horizontal unit directions from the shared point to the far ends of the
     current and the other ledge. Collinear (`|(U x V).Z| < 0.001`): merge, ZipP1 = the current far end and
     ZipP2 = the other's far end. Perpendicular (`|U . V| < 0.1`): type = 0 or 1 (inside right / left) when
     `(U x V).Z < 0`, else 2 or 3 (outside), the bit from the input side. Outside corners need room: box
     (35, 35, H) 75 along the ledge (reversed for type 3), then 75 back along -n, both clear (0x2df). Then
     Controller.ChangeLedge(type, P1, P2) with the other ledge's points.
   - otherwise, an LG plus a PIPE where the LG is the current ledge: when the pipe zone point projects onto the
     pipe within 46 forward, 2 vertically and 16 sideways: m_geoNormal = normalize(pipe side - pipe bottom),
     ZipP = the pipe, Controller.TopPipe(0 for s >= 0, else 1) (ledge to pipe).
9. Roll = 0; Velocity = (Location - start) / dt.

## States

None. The script states (pipe-vertical.md, ladder.md, pipe-horizontal.md, zip-line.md, fence.md, ledge.md) decide
which mode and Acceleration convention apply.

## Constants

| Name | Value | Provenance |
|---|---|---|
| snap speed (pipe, HOH, ladder, zip, ledge) | 500 uu/s, tolerance 0.1 | decoded |
| yaw rates per call | pipe 16384 dt; HOH, ladder 1000; zip, ledge, repos 3000 | decoded |
| pipe slide | input threshold 10; Velocity.Z in [-450, 0] | decoded |
| pipe bottom | 210 below the HOH zone | decoded |
| pipe to HOH | 207.2 down, 97.9 forward, distance^2 600, -210, flags 0x243 | decoded |
| pipe to zip | dz < 10, distance^2 < 400, 5.63 | decoded |
| pipe top ledge | horizontal distance^2 1000, dz 2 | decoded |
| TestTopPipe | right 30, up 191.5 / 156.9, right 15, forward 79.4 / 92.7 | decoded |
| HOH end limits | 27, legs over 95, input split 120, zip join 3, ortho 961 (31^2) | decoded |
| HOH legs probe | 140 then 70, flags 0x201 | decoded |
| HOH to pipe | zone.X - 34, 48 - zone.X, 86.35 forward, up 205 | decoded |
| ladder rungs | 64 spacing, offset 26 + 2, snap 100 uu/s, dead band 2 | decoded |
| ladder slide | -524.5 split, -200, -700, slow -64 | decoded |
| zip ease | 0.03 (GroundSpeed - s) | decoded |
| ledge targets | 39 out, NORMAL zone.Z down, WAIST -20, BARRIER -10 / 10, FEET +50.5 | decoded |
| ledge end stop | 36; corner probe zone (37, 35, 90.3), end 37, room 75 | decoded |
| ledge head room | +50 box (35, 35, 87.5) | decoded |
| ledge to pipe | 46 / 2 / 16 | decoded |

## Network

All these modes run on the owning client and on the server for the same moves. Most chain events are raised only
on the controlling side (the Controller's RemoteRole is not AutonomousProxy); the server's copy follows with the
traversal data the client sends (state-and-network.md). TopSynchro and NoGE are raised wherever the physics runs
with a Controller.

## Edge cases

- The traversal modes ignore iteration counts and sub-steps: one MoveActor per call.
- The pipe hold move has bCollideWorld off; walls do not stop it.
- TestTopPipe always probes to the pawn's right; the left/right exit is decided by the animation, not here.

## Evidence

- Editor: physOnPipe 0x1120a470, physOnPipeH 0x112092c0, physOnLadder 0x1120b5d0, physLinear 0x11208af0,
  SetZipSpeed 0x111fbb80, UnZipSpeed 0x111fbcf0, physOnVerticalPlane 0x11208670, physLedge 0x11204870,
  TestTopPipe 0x111fe5e0.
- Game: physOnPipe 0x10ad10c0, physOnPipeH 0x10acff50, physOnLadder 0x10ad2a40, physLinear 0x10acf860,
  physOnVerticalPlane 0x10acf3e0, physLedge 0x10ac9400, TestTopPipe 0x10ae2f70 (constant fingerprints,
  confirmed by the dispatch table).
- Re-read with REA (Ghidra 12.1.4, 2026-10-26) from the game build's instructions: physOnPipe, physOnPipeH and
  physOnLadder in full; FindArmsGE 0x10ac90a0 (outputs best, second), the float to int helper 0x10b83aa0 (MSVC
  _ftol, truncates), FVector equality 0x1099dfd0, the event thunks TopLadder (name 0x20b6), NoGE (0x20b9) and
  TopSynchro (0x20c0). Constants: 26 + 2 rung offset, 100 rung speed, -524.5 / -200 / -700 / -64 ladder slide,
  -450 pipe slide, 5.63 and 6 junction offsets, 3 zip join, 961 ortho reach.

## Open questions

1. Answered from the game build's instructions (0x10ad11d8 to 0x10ad12a0, confirmed with REA): the two distances
   are 3D distances of NewZipP1 and NewZipP2 to the current pipe's line, compared strictly; the flip compares
   `|ZipP.Z - Location.Z - 20|` of the lower and upper ends.
2. The HOH legs probe in step 5 uses the hit pawn's zone configuration; its purpose (not kicking a pawn hanging in
   pose 3) is inferred.
