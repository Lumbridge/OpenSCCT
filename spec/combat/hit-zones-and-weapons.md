# Hit zones, zone tables and weapon damage (merc guns, spy shocker gun)

Units:
- uc:SBase.SBasePawn:GetApproxPillFromHit
- nat:Engine.Actor.TraceBone
- def:SBase.SBasePawn (DamageLookupTable, DamageSnipeLookupTable)
- def:SBase.SPawnAttaque (DamageSnipeLookupTable)
- def:SBase.SPAWNDEFENSE
- uc:SBase.SWeaponDefense:FireWeapon
- uc:SBase.SWeaponDefense:ServerSetAccurateRotation
- rep:SBase.SWeaponDefense.ServerSetAccurateRotation
- uc:SBase.SWeaponDefense:ServerFireWeapon
- rep:SBase.SWeapon.ServerFireWeapon
- uc:SBase.SWeaponDefense:TraceFire
- nat:SBase.SWeaponDefense.GetAccuracy
- def:SBase.SWeaponDefense
- def:SBase.SWeaponDefense_Uzi
- def:SBase.SWeaponDefense_Shotgun
- nat:SBase.SWeaponDefense_Shotgun.TraceFire
- uc:SBase.SWeaponAttaque:FireWeapon
- uc:SBase.SWeaponAttaque:ServerFireWeaponWithLocation
- rep:SBase.SWeaponAttaque.ServerFireWeaponWithLocation
- uc:SBase.SWeaponAttaque:TraceFireWithPos
- uc:SBase.SWeaponAttaque:GetAxesAndLocation
- uc:SBase.SStickyShocker:Fire
- def:SBase.SStickyShocker
- uc:SBase.SLethalWeapon:TraceFireWithPos
- def:SBase.SLethalWeapon

## Purpose
Defines how a shot becomes a damage number and a pill (hit zone) before it enters the damage pipeline: the server
trace, the distance falloff per merc weapon, the zone multiplier tables, the approximate pill fallback, and the spy
shocker gun (which deals no health damage to players). Rate of fire, ammunition, reload, recoil and spread growth are
weapon-handling topics owned by the weapons spec; only what reaches `PawnTakeDamage` is specified here.

## Data
Zone tables (index = pill - 1; values are percent):

| Pill | Zone (mesh collision, decoded) | DamageLookupTable (spy and merc, and SWeaponDefense_Shotgun's own copy) | DamageSnipeLookupTable spy | DamageSnipeLookupTable merc |
|---|---|---|---|---|
| 1 | head (B Head capsule) | 300 | 650 | 500 |
| 2 | torso, left half | 100 | 300 | 200 |
| 3 | left upper arm | 40 | 150 | 80 |
| 4 | left forearm | 40 | 150 | 80 |
| 5 | left hand (sphere) | 20 | 150 | 40 |
| 6 | left thigh | 100 | 150 | 200 |
| 7 | left calf | 100 | 150 | 200 |
| 8 | left foot | 20 | 150 | 40 |
| 9 | torso, right half | 100 | 300 | 200 |
| 10 | right upper arm | 40 | 150 | 80 |
| 11 | right forearm | 40 | 150 | 80 |
| 12 | right hand (sphere) | 20 | 150 | 40 |
| 13 | right thigh | 100 | 150 | 200 |
| 14 | right calf | 100 | 150 | 200 |
| 15 | right foot | 20 | 150 | 40 |

The zone names are decoded from the meshes (hit-zones.md Data): SPerso.ukx has 13 capsules and 2 spheres between
26 collision points, identical on ATT_01 and DEF_01, in the order above, so 6 to 8 are the B L leg bones and 13 to 15
the B R leg. The script's hit animation mapping in `s_PlayerWalking.PlayAnimDmg` (pill 1 head, 2 and 9 chest sides,
3 to 5 left arm, 10 to 12 right arm, 6 to 8 its "right leg" reaction, 13 to 15 its "left leg" reaction) therefore
mirrors the legs: a hit on the left leg plays the right-leg reaction. Reproduce it as is. The snipe table only
matters for pills 2 to 15 (pill 1 is always lethal).

EDamageLocation (SBasePawn enum used by the approximate pill): P_Head 0, P_ArmL 1, P_ArmR 2, P_ChestL 3, P_ChestR 4,
P_ChestB 5, P_ChestF 6, P_LegL 7, P_LegR 8.

Merc weapon damage parameters (def:, effective):

| Field | SWeaponDefense (rifle) | SWeaponDefense_Uzi | SWeaponDefense_Shotgun |
|---|---|---|---|
| BaseDamage | 17 | 10 | 17 |
| MyDamageType | SDTBase | SDTBase | SDTBase |
| ShootingRange (uu) | 15000 (trace uses 15000 literal) | 20000 (trace uses 15000 literal) | 2500 (native trace) |
| CMax | 1.471 | 1.765 | 7.0 |
| CoefCaC | 1.471 | 1.688 | 6.0 |
| CoefMed | 0.882 | 0.95 | 1.57 |
| CoefMin | 0.588 | 0.65 | 0.43 |
| DistCac (uu) | 300 | 400 | 450 |
| DistMed (uu) | 1200 | 900 | 950 |
| DistLoin (uu) | 1800 | 1500 | 1300 |
| PercentDamageRefillable | 70 | 70 | 70 |
| bCanSnipe | true | false | false |
| extra | | | TotalShotgunDamageMultipler 0.2, RadiusScale 0.35, DemiAngleDispersion 0.97, DemiAngleDispersion_Coeff 1.0 |

Spy shocker gun (SStickyShocker extends SWeaponAttaque): BaseDamage 10 (applied only to non-pawn actors),
MyDamageType SDTStickyShocker, RateOfFire 0.5 s, ShootingRange 15000, DispersionActuelle 0 (no spread),
EnergyNeeded 15 per shot from `SPawnAttaque.EnergyLevel` (max 100), PercentDamageRefillable 70 (unused for players).

SLethalWeapon (spy lethal pistol, not in the Versus team weapon lists): BaseDamage 20, SDTBase, CMax 1.0, CoefCaC 0.7,
CoefMed 0.5, CoefMin 0.2, DistCac 300, DistMed 1000, DistLoin 1300.

Team weapon lists (def:SBase.STeamAttaquant.ClassWeapon = [SStickyShocker]; def:SBase.STeamDefenseur.ClassWeapon =
[SWeaponDefense, SWeaponDefense_Uzi, SWeaponDefense_Shotgun]).

## Rules

### Approximate pill (script)
1. `GetApproxPillFromHit(HitLocation)`: Y = `(0,1,0)` rotated by the pawn Rotation (the pawn's right axis);
   d = HitLocation - Location with d.Z = 0, d = `Normal(d)` (a zero vector stays zero); dotY = d . Y;
   p = (HitLocation.Z - Location.Z) / CollisionHeight.
   - p > 0.68 gives P_Head;
   - 0 <= p <= 0.68 gives P_ChestL if dotY < 0, else P_ChestR;
   - p < 0 gives P_LegL if dotY < 0, else P_LegR.
2. The damage pipeline stores `result + 1` as the pill: head 1, ChestL 4, ChestR 5, LegL 8, LegR 9. These numbers
   index the 15-pill scheme, so an approximate "left chest" plays the left-arm hit anim (pill 4), "right chest" the
   left-arm anim (pill 5), "left leg" the right-leg anim (pill 8) and "right leg" the right-chest anim (pill 9).
   Approximate pills never scale damage (`damage-pipeline.md` rule 20 branch A). Reproduce this mismatch exactly.

### TraceBone contract (native 1122 at 0x10a25270)
3. Signature: `TraceBone(out bIsAFakeBackdrop, out PillTag, out HitLocation, out HitNormal, TraceEnd, optional
   TraceStart, optional out Material, optional bRealFire)` returns the hit Actor.
4. Script expectations: the return value is never None (callers dereference it immediately; the shocker code tests
   `IsA('LevelInfo')`, so a miss returns the LevelInfo); world geometry and movers have `bWorldGeometry` or are Movers;
   for a pawn hit, PillTag is the struck collision pill 1 to 15; Material carries a SurfaceType for impact effects;
   bIsAFakeBackdrop = 1 suppresses impact decals.
5. Native behaviour (decoded, hit-zones.md rules 1, 2): a zero-extent trace with flags 0x40097 (pill bit 0x40000);
   bRealFire adds 0x20000 (no SingleResult: pass-through hits are reported; it does not change the collision
   shape); asking for Material adds 0x1000. PillTag is 0 unless a capsule or sphere of a pawn's mesh was hit: world,
   movers and cylinder-only hits give 0. A second pass tests every pawn within 3 x CollisionHeight of the trace
   against its pills and keeps the hit nearest the start. bIsAFakeBackdrop is 1 only for a BSP surface with poly
   flag 0x80.

### Merc rifle and Uzi (script TraceFire, server)
5. Client side, per bullet (`SWeaponDefense.FireWeapon`): `BulletsToFire -= 1`; call
   `ServerSetAccurateRotation(MyDesiredRotation.Yaw, .Pitch, .Roll)` then `ServerFireWeapon(BulletInARow)`; both are
   reliable client-to-server RPCs and arrive in this order. Clip and ammo are decremented on the client; muzzle
   effects, recoil and first person animation are local.
6. Server `ServerFireWeapon(iBulletInARow)`: store BulletInARow; shooter `Stats.ShotsFired += 1`; DispersionActuelle =
   0 if the shooter's controller state is `s_Binoculars_Def`, else `GetAccuracy()` (native 1286, 0x10abcfc0); run
   `TraceFire(DispersionActuelle, 0, 0)`; play FireSingleShotSound on the owner (radius 500); count a muzzle flash on
   the third person dummy and make noise 1.0.
7. `TraceFire(Accuracy, YOffset, ZOffset, optional TraceStartLocation)` (YOffset and ZOffset are unused):
   1. Return if there is no Instigator or no Controller.
   2. bOnMoverMoving = the shooter's Base is a Mover with non-zero velocity.
   3. Axes X, Y, Z from AccurateRotation (the client-sent aim).
   4. StartTrace = Instigator.Location + (0,0,48) if crouched or (0,0,55) if standing when bOnMoverMoving, else
      Instigator.Location + EyePosition() (EyePosition = (0,0,EyeHeight)).
   5. EndTrace = StartTrace + Accuracy*(FRand()-0.5)*Y*1000 + Accuracy*(FRand()-0.5)*Z*1000 + 15000*X.
      FRand is called twice in this order (Y first). Note FRand is hooked by Reloaded.Core in the live game
      (Reloaded.Core.dll+0x242f0).
   6. Trace origin wStartTrace = TraceStartLocation if it is not the zero vector; otherwise StartTrace when on a moving
      mover, else `GetFireStart()` (weapon override; the base is Instigator.Location + EyePosition()).
   7. `Other = TraceBone(..., EndTrace, wStartTrace, Material, true)`.
   8. If Other is world geometry or a Mover: if it is a damageable SBasicGamePlayObject call its
      `TakeDamage(BaseDamage, Instigator, HitLocation, -HitNormal*1000, MyDamageType, PillTag)`; spawn the surface
      impact unless bIsAFakeBackdrop; on SURFACE_FakeCeiling, if this is not already a re-trace, spawn hole decals
      and re-trace from `HitLocation - 16*HitNormal` with the same Accuracy (the bullet passes through once; a second
      fake ceiling in the re-trace stops it).
   9. Else if Other is not an SBasePawn: `Other.TakeDamage(BaseDamage, Instigator, HitLocation, -HitNormal*1000,
      MyDamageType, PillTag)`.
   10. Else (pawn): if the shooter is an SPlayerController and the teams differ or the mode is MercDeathMatch,
       shooter `Stats.ShotsHit += 1`. Then:
       - binocular shot (shooter state `s_Binoculars_Def`): `PawnTakeDamage(BaseDamage, Instigator, HitLocation,
         (HitLocation - StartTrace)*1000, SDTSnipeShot, PillTag, PercentDamageRefillable)` with no distance falloff;
       - otherwise d = `VSize(HitLocation - wStartTrace)` and
         Dmg = int(B * f(d)) with B = BaseDamage and
         f(d) = (CoefCaC - CMax)/DistCac * d + CMax for d < DistCac;
         f(d) = (CoefMed - CoefCaC)/(DistMed - DistCac) * (d - DistCac) + CoefCaC for DistCac <= d < DistMed;
         f(d) = (CoefMin - CoefMed)/(DistLoin - DistMed) * (d - DistMed) + CoefMed for DistMed <= d < DistLoin;
         f(d) = CoefMin for d >= DistLoin;
         evaluated in float32 one operation at a time, left to right as written, then truncated;
         `PawnTakeDamage(Dmg, Instigator, HitLocation, (HitLocation - StartTrace)*1000, MyDamageType, PillTag,
         PercentDamageRefillable)`.
       - if the victim is a spy: `BloodDropStartTime = now` (blood trail cosmetics).
   11. Tracer: outside binoculars, spawn an SFireTrace with probability PourcBTSingle/100, PourcBTBurst/100 or
       PourcBTFullAuto/100 (100 %, 60 %, 40 %) by fire mode; this consumes one more FRand.
8. Resulting rifle numbers (float32, BaseDamage 17), before and after the zone table:

| Distance d (uu) | Dmg | head (300 %) | torso (100 %) | arm (40 %) | hand/foot (20 %) |
|---|---|---|---|---|---|
| 0 to 300 | 25 | 75 | 25 | 10 | 5 |
| 600 | 21 | 63 | 21 | 8 | 4 |
| 900 | 18 | 54 | 18 | 7 | 3 |
| 1199 | 15 | 45 | 15 | 6 | 3 |
| 1200 | 14 | 42 | 14 | 5 | 2 |
| 1500 | 12 | 36 | 12 | 4 | 2 |
| 1800 and beyond | 9 | 27 | 9 | 3 | 1 |

   Rifle step boundaries (integer distances, first distance giving the value): 25 from 0, 24 from 301, 23 from 391,
   22 from 481, 21 from 571, 20 from 661, 19 from 751, 18 from 840, 17 from 930, 16 from 1020, 15 from 1110, 14 from
   1200, 13 from 1320, 12 from 1440, 11 from 1560, 10 from 1680, 9 from 1800.
   Uzi (BaseDamage 10, torso values): 17 from 0, 16 from 338, 15 from 460, 14 from 528, 13 from 596, 12 from 663,
   11 from 731, 10 from 799, 9 from 867, 8 from 1001, 7 from 1200, 6 from 1401 and beyond.
   Rifle sniping (no falloff): spy victim head lethal, torso 51, others 25; merc victim (MercDeathMatch only) torso
   34, arm 13, hand/foot 6.
9. Zone scaling is applied later in SRTD: `int(Dmg * table[pill-1] * 0.01)` with the victim pawn's table.

### Merc shotgun (native TraceFire; decoded in natives2-damage.md rule 7, see the Resolved list)
10. `SWeaponDefense_Shotgun.TraceFire` is native (1799, 0x10ab9d00). Script provides its parameters: the falloff
    coefficients above, ShootingRange 2500, its own DamageLookupTable, TotalShotgunDamageMultipler 0.2, RadiusScale
    0.35, DemiAngleDispersion 0.97 and coefficient 1.0. Client side, `SpawnImpacts` only draws 8 pellet decals; it
    does not deal damage. The nine pellets, their damage and the largest-per-actor rule are in natives2-damage.md
    rule 7; the multiplier, radius scale, dispersion fields and the class's own table are not read. The reload, the
    pellet marks and how a merc gets the gun are in spec/equipment/merc-guns.md.

### Spy shocker gun
11. Client (`SStickyShocker.Fire`): skipped in GameEnded; requires the weapon drawn (WeaponLoaded != 0), not in
    transit, `CheckIfCanFire()`, and `AuthorizeEnergy(15)` (subtract 15 from EnergyLevel if at least 15 is
    available); otherwise, at most every ReloadTime 0.5 s, plays the empty sound. The auto-fire loop in state
    `s_Selected` fires while Fire is held, every RateOfFire 0.5 s, not while frozen by a shock.
12. `SWeaponAttaque.FireWeapon` sends `ServerFireWeaponWithLocation(Location, Rotation.Yaw, Rotation.Pitch,
    Rotation.Roll)`: the weapon actor's own client-side location and rotation (reliable client-to-server RPC).
13. Server: shooter `Stats.ShotsFired += 1`; `TraceFireWithPos(Position, Orientation, DispersionActuelle = 0, 0, 0)`;
    shot sound radius 200; MuzzleCount += 1 (replicated, drives muzzle flashes on clients).
14. `GetAxesAndLocation`: if the pawn's `ShouldIGetPawnRot()` is true, the aim is the pawn's packed view rotation
    (Yaw = low word, Pitch = high word of `iYawPitchFromView`, Roll 0); else the client-sent Orientation. StartTrace =
    Position + 30*X + 8*Z - 4*Y. EndTrace = StartTrace + 15000*X (spread terms are zero).
15. Hit handling:
    - world or Mover: a damageable SBasicGamePlayObject takes `TakeDamage(10, Instigator, ..., SDTStickyShocker,
      PillTag)`; metal surfaces spawn a metal shocker impact; SURFACE_MediumWater also electrifies the water: for
      every Pawn whose 100 uu downward trace hits the same actor (and the hit actor is not the LevelInfo), call
      `PawnTakeDamage(0, Instigator, HitLocation, (HitLocation - StartTrace)*1000, SDTStickyShocker, PillTag)`;
      other surfaces spawn the shocker impact;
    - non-pawn actor: `TakeDamage(10, ..., SDTStickyShocker, PillTag)`;
    - pawn: `PawnTakeDamage(0, Instigator, HitLocation, (HitLocation - StartTrace)*1000, SDTStickyShocker, PillTag)`
      and, on a different team, shooter `Stats.ShotsHit += 1`; spawn the shocker impact.
16. A shocker hit therefore never removes health (Damage 0 times any table is 0). Its effect is the shock state
    (`shock-and-taser.md`). Friendly spies and the shooter's own team can be shocked.

## States
Not applicable (weapon firing states belong to the weapons spec).

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| head threshold | 0.68 of CollisionHeight above centre | - | decoded | GetApproxPillFromHit |
| rifle BaseDamage | 17 | hp | decoded | def:SBase.SWeaponDefense |
| rifle falloff | 1.471 / 1.471 / 0.882 / 0.588 at 300 / 1200 / 1800 uu | - | decoded | def:SBase.SWeaponDefense |
| trace length | 15000 | uu | decoded | TraceFire literal |
| spread scale | Accuracy * 1000 * (FRand - 0.5) per axis | uu at 15000 uu | decoded | TraceFire |
| moving-mover eye offset | 48 crouched / 55 standing | uu | decoded | TraceFire |
| fake ceiling re-trace offset | 16 | uu | decoded | TraceFire |
| shocker muzzle offset | 30 forward, 8 up, 4 left | uu | decoded | GetAxesAndLocation |
| shocker energy per shot | 15 of 100 | energy | decoded | def:SBase.SStickyShocker, def:SBase.SPawnAttaque |
| water electrocution probe | 100 | uu down | decoded | TraceFireWithPos |
| bullet momentum scale | 1000 | - | decoded | TraceFire |

## Network
- Merc guns: the client chooses the aim (ServerSetAccurateRotation) and the moment; the server traces from its own
  copy of the shooter's position against its own copy of the world and targets. There is no lag compensation.
- Shocker gun: the client sends both the weapon position and the rotation; the server traces from that position.
  This is more client authoritative than the merc guns.
- `MuzzleCount` and `bLaserOn` replicate from the server to all clients (reliable, Role == Authority).
- Damage results flow through the server-only pipeline.

## Edge cases
- A rifle hit exactly at DistCac uses the second segment, which starts at CoefCaC, so values are continuous.
- A binocular shot with pill 1 kills even through the snipe table; it is reported with the headshot death message
  and counts `Merc_SniperHeadshots`.
- Bullets and shocker bolts that hit a GPO also pass the shooter as instigator, so a GPO explosion credits the shooter
  (`environmental-damage.md`).
- `SWeaponDefense_Uzi` keeps the 15000 uu trace even though its ShootingRange is 20000.
- If `TraceBone` returned None the original would fail with an accessed-none on `Other.bWorldGeometry`; treat a miss
  as hitting LevelInfo.

## Evidence
- uc:SBase.SBasePawn:GetApproxPillFromHit sha1 f7cd26c0feae; uc:SBase.SWeaponDefense:TraceFire sha1 d314de74e130;
  ServerFireWeapon 80fb83c65df9; FireWeapon 18c3e79b883a; ServerSetAccurateRotation 426defaf3803;
  uc:SBase.SWeaponAttaque:TraceFireWithPos 23efb192317e; ServerFireWeaponWithLocation e89968ce1a1f;
  GetAxesAndLocation 6566875906ab; uc:SBase.SLethalWeapon:TraceFireWithPos a5f86e248aab.
- local/re-out/uc/SBase/{SBasePawn,SWeaponDefense,SWeaponDefense_Shotgun,SWeaponDefense_Uzi,SWeaponAttaque,
  SStickyShocker,SLethalWeapon}.uc; local/formats/classdefaults_effective.json.
- Natives: TraceBone 0x10a25270, GetAccuracy 0x10abcfc0, SWeaponDefense_Shotgun.TraceFire 0x10ab9d00,
  TickSWeaponAttaque 0x10ab7870 (muzzle spawning, see Bridge docs/GADGET_TIMINGS.md); FRand hook
  Reloaded.Core.dll+0x242f0 (local/reflect/game.json).
- Float32 damage table computed by evaluating the TraceFire expressions with numpy float32.

## Open questions
- The Reloaded.Core FRand hook: the distribution is unchanged (`rand() / 32768`), but with Reloaded's fire
  prediction on a server the first two FRand calls of TraceFire / TraceFireWithPos return a hash of per-shot inputs
  (reloaded-hooks.md rule 1, inputs decoded in part). Decide how the port's seeded RNG reproduces it, and finish
  decoding the inputs.

### Resolved
- `GetAccuracy` spread model. Resolved: merc-rifle.md rule 13, confirmed at 0x10abcfe0 (min of the speed, base and
  burst terms with MaxDispersion).
- Pill numbering on the meshes, and whether 6 to 8 is the right leg. Resolved: 15 pills (13 capsules, 2 spheres),
  identical on ATT_01 and DEF_01; 6 to 8 are the left leg (B L bones) and 13 to 15 the right leg, so the script's
  hit-animation mapping mirrors the legs. Data table above. Evidence: hit-zones.md Data, read from SPerso.ukx
  (ANSWERS-actions 9).
- `TraceBone` PillTag for cylinder-only and non-pawn hits; bRealFire. Resolved: zero extent, flags 0x40097,
  bRealFire adds 0x20000 (pass-through hits reported, same collision shape), Material adds 0x1000; PillTag 0
  unless a pawn's capsule or sphere was hit; a second pass over pawns within 3 x CollisionHeight keeps the hit
  nearest the start; bIsAFakeBackdrop only for BSP poly flag 0x80. Rule "TraceBone contract" 5. Evidence:
  hit-zones.md rules 1, 2 (ANSWERS-actions 10).
- Shotgun native TraceFire. Resolved: nine bone traces in a fixed pattern (the centre with a 0.067 SR spread, four at
  0.194 SR up, down, right and left, four diagonals at 0.097 SR); falloff by DistCac, DistMed and DistLoin with CMax
  and the three Coefs; factors 1 (centre), 0.3 (ring) and 0.5 (diagonals); only each actor's best pellet counts;
  TotalShotgunDamageMultipler, RadiusScale, DemiAngleDispersion and the class table are unused. Evidence:
  natives2-damage.md rule 7 (ANSWERS-natives2 24).
