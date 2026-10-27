# Aim and look-at bone control: AimAt, the mesh instance's IK step and the pose turn

Units:
- nat:SBase.SBasePawn.AimAt
- eng:USkeletalMeshInstance::UpdateAnimation (its IK step)
- eng:USkeletalMeshInstance::BuildPose (the per-bone IK turn)
- uc:SBase.SBasePawn:TurnHead (the IKT_WEAPONLOADED case)

## Purpose

How an armed pawn's upper body follows its aim: the merc's rifle and the spy's pistol point where the player aims,
on every machine that draws the pawn (other players' pawns included). The script asks for the aim each tick, the
mesh instance turns its stored angles toward it at a fixed speed, and the pose build turns one spine bone by them,
so the arms, the head and the held weapon follow. It is presentation only: nothing in the simulation reads it.

## Data

Mesh instance (USkeletalMeshInstance) fields, editor build offsets:

| Offset | What |
|---|---|
| +0x314, +0x318 | look-at pair (yaw, pitch), radians |
| +0x31c | vertical aim (AAVERT) |
| +0x320 | a single angle returned with the others |
| +0x324, +0x328 | AAHOH pair |
| +0x32c, +0x330 | AABTW pair |
| +0x334, +0x338 | AAFULL pair (yaw, pitch) |
| +0x33c | the time of the last animation update |
| +0x340 | the pending request type: 0 none, 6 AAFULL, 2 AAVERT, 4 AAHOH, 5 AABTW (1 and 3 from LookAt) |
| +0x344 to +0x350 | limits of the request (min and max horizontal, min and max vertical), radians |
| +0x354, +0x360 | view direction and original direction of the request |
| +0x36c | the pitch axis, rebuilt by every pose build |
| +0x384, +0x388 | IK speed and return speed, 8.0 radians per second (constructor 0x1116cb80) |

SBasePawn.eAimAtType: AAFULL 0, AAVERT 1, AAHOH 2, AABTW 3.

## Rules

1. **AimAt** (native 1409, editor 0x111ff680, game 0x10ac26d0; worker 0x111fac60): when the pawn's mesh is a
   skeletal mesh with an instance, the type maps through the jump table at 0x111facd0 (AAFULL 6, AAVERT 2, AAHOH 4,
   AABTW 5) and 0x110f9160 stores it at +0x340 with the two directions and the four limits times 0.017453.
2. **TurnHead, IKT_WEAPONLOADED**: with WeaponLoaded not 0 and bWeaponTransit clear, Orientation = (pitch, yaw) of
   iDesiredYawPitch for a merc (SPAWNDEFENSE) or iYawPitchFromView for a spy, roll = the pawn's roll, and
   `AimAt(AAFULL, vector(Orientation), vector(Rotation), -90, 90, -90, 90)`. Both packed values are replicated, so a
   client asks for the aim of other players' pawns too. The other TurnHead cases (back to wall, grab, pipe and split
   jump arm rotations through SetBoneDirection) are not covered here.
3. **IK step** (0x110fbb90, the first thing UpdateAnimation 0x111056c0 does, after storing its time at +0x33c): by
   the request type, one pair steps toward the request and every other angle returns to 0; then the request is
   cleared, so a pawn whose script stops asking relaxes back.
   - Type 6: the look-at pair, +0x31c, +0x320, the AAHOH and AABTW pairs return; the AAFULL pair steps with share
     0.85 (0x3f59999a) at the IK speed. Types 4 and 5 step the AAHOH and AABTW pairs the same way, the rest return.
     Type 0 returns everything. Types 1 to 3 (look-at and AAVERT) are not used by the SBase script.
   - Step (0x110fb890, share k, speed v): pitch target = k asin(clamp(view.Z, -1, 1)); yaw target = k times the
     angle between the view and the original direction flattened and normalised (acos of the clamped dot), negated
     when `view.Y orig.X - orig.Y view.X > 0` (the view to the right). When both changes are non-zero their ratio
     slows the smaller one so both arrive together (|ratio| at most 1: yaw speed |ratio v|; else pitch speed
     |v / ratio|). Each angle moves by at most its speed times the update time, then is clamped to k times its limits.
   - Return (0x110fb440 for a pair, 0x110fb3c0 for one angle): toward 0 at the return speed without crossing it, a
     pair with the same ratio split.
4. **Pose turn** (BuildPose 0x11109a50, per bone after its space base is built and before its children,
   0x111022f0): bone 4 (B Spine2 in the SPerso skeletons) is turned by the AAFULL pair when either angle is not 0
   (0x11102160): the bone's coords are transposed, multiplied by the axis-angle coords of (+0x36c, pitch), then by
   those of (UnitCoords.ZAxis, yaw), and transposed back; the origin is kept, so the children follow. +0x36c is
   `Z x original direction` in the pose's space (0x1110cb5f). The axis-angle coords (0x10fdd330) have the X axis
   (x^2(1 - c) + c, xy(1 - c) + s z, xz(1 - c) - s y) and kin. Other bones: 5 by +0x31c / 2 (after bone 4's case),
   6 (B Head) by the look-at pair or +0x320 or half an AAHOH / AABTW pair, 21, 23 and 24 by the AAHOH and AABTW pairs.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| AAFULL share | 0.85 | - | decoded | 0x110fbb90 case 6 |
| IK speed, return speed | 8.0 | rad/s | decoded | constructor 0x1116cb80 |
| Degrees to radians | 0.017453 | - | decoded | 0x10fdefb0 |
| AAFULL bone | 4 | bone index | decoded | 0x111022f0 |

## Network

Nothing of its own: TurnHead reads iDesiredYawPitch and iYawPitchFromView, which the pawn replicates
(spec/net/proxy-pawns.md), and runs on every machine that ticks the pawn.

## Open questions

1. The space the pose build's bone coords are in when 0x11102160 runs. The port takes it as aligned with the actor
   (vertical Z), so the turn is about the world's vertical and the horizontal axis across the facing; the play check
   (play-aim) shows the arms following the view up and down.
2. The look-at (LookAt, native 1408) and the other IK types' steps are not ported.
