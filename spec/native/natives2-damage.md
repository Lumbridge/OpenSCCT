# Damage natives: gameplay objects, explosions, damage volumes, shotgun, health refill

Units:
- nat:SBase.SGamePlayObject.ProcessDamage
- nat:SBase.SGamePlayObject.NativeHitWall
- nat:Engine.Projectile.Explode
- eng:ASProjFrag::Explode
- nat:SBase.SMineProx.Explosion, nat:SBase.SMineLaser.Explosion
- eng:ASDamageVolume::Tick
- nat:SBase.SWeaponDefense_Shotgun.TraceFire
- nat:SBase.SBasePawn.NativeTick (health refill and poison part)
- nat:SBase.SPawnAttaque.TickNative (stock and Reloaded)

## Purpose

How the natives deal damage outside the hit-scan weapons: destructible objects, thrown and pushed objects hitting
things, grenade and mine blasts, damage volumes and the merc shotgun; and where the spy's health refill really
lives. Decoded from the editor build and checked against the game build (Evidence).

## Data

Two delivery paths recur:
- To a pawn: the pawn's Controller, when it is an SPlayerController, receives the script event
  `PawnTakeDamage(Damage, instigatedBy, HitLocation, Momentum, DamageType, PillTag, PercentDamageRefillable)`.
  This is how every native blast reaches players; `Actor.HurtRadius` never does (SBasePawn.TakeDamage is empty,
  damage-pipeline.md).
- To anything else: the actor's script `TakeDamage(Damage, EventInstigator, HitLocation, Momentum, DamageType)`.

The spy hold rule (blasts only): a victim spy (SPawnType 0) whose m_AttackTarget is set and within 30 uu
vertically makes the native send the same PawnTakeDamage to both the spy's and the target's controllers; a
victim merc (SPawnType 1) whose m_AttackedBy is set and within 30 uu vertically is skipped in its own turn (it
already received the spy's copy, or nothing when the spy was out of reach). Order: ProcessDamage sends to the spy
first; the frag and the proximity mine send to the target first. ProcessDamage handles a pawn only when its own
controller is an SPlayerController (tested before the hold rule); the frag and the proximity mine test each of the
two controllers on its own, so a held target with a player controller still gets the copy of a spy without one.

All float to int conversions below truncate toward zero. Distances are between actor Locations unless stated.
"Actors of the level" means a copy of the level's actor list taken at the start, in list order, skipping empty
slots, hidden actors (bHidden) and the source itself.

## Rules

### 1. SGamePlayObject.ProcessDamage(Damage)

1. Nothing unless bDamageable and not bAlreadyDestroyed.
2. HitPoints = max(HitPoints - Damage, 0) (Damage 0 still runs the rest).
3. HitPoints reached 0 now:
   1. bAlreadyDestroyed = true.
   2. Every player controller (bIsPlayer) of the controller list is told to play DestroySound from the object
      (the controller hear-sound method: volume 1, radius 200, pitch 1).
   3. With a DestroySound: event NotifyNoise(0.5).
   4. With bCanDamageActor and DamageActorRange > 0, for every actor A of the level:
      - d2 = int(|A - object|^2). When d2 < DamageActorRange^2:
        - Line of sight: line from A to the object, flags `0x286` (world only); the hit must be nothing or A.
        - Damage = int((DamageActorRange - |A - object|) * DamageActorPoints / DamageActorRange) (linear falloff,
          float distance).
        - A pawn (SBasePawn) gets PawnTakeDamage(Damage, None, A.Location, A.Location - object.Location,
          DamageActorType, 0, PercentDamageRefillable) with the spy hold rule; any other actor gets
          TakeDamage(Damage, None, A.Location, A.Location - object.Location, DamageActorType). Other gameplay
          objects therefore chain. No team test.
      - Else, when d2 < ShakeRange^2 and A is an SBasePawn: its controller gets ClientShake(50 S / d2,
        2000 (S / d2), 500, true, true, true) with S = ShakeRange^2 and integer division.
      (No shake at all for an object without bCanDamageActor or with DamageActorRange 0.)
   5. Every ObjectLights entry: its LightOff event.
   6. ResetDamageTime > 0: event WaitReset.
4. Damage percent pct = int((1 - HitPoints / InitialHitPoints) * 100) when InitialHitPoints > 0, else 100 (x87
   arithmetic; a port should compute the ratio in double before truncating).
5. Spawns, every call, for each SpawnableObjects entry i:
   - No SpawnedActor yet and MinPercent <= pct <= MaxPercent: with SpawnParticle set, event SpawnDamageEmitter(i);
     else SpawnedActor = spawn of SpawnClass (when set) at the object's Location and Rotation, owner the object's
     Owner, instigator its Instigator (the spawn may fail). A spawned Emitter with the entry's scale flag has each
     of its emitters' StartLocationRange scaled by DrawScale3D (X by X, Y by Y, Z by Z).
   - A SpawnedActor and pct > MaxPercent: with SpawnParticle, event KillDamageEmitter(i); else the actor is
     destroyed and SpawnedActor cleared.
6. Damage stage, every call: the DamagedMeshes entry with the highest Percent not above pct (the first one on a
   tie). Nothing when none. Otherwise:
   - Skin set: RepSkin = Skin and Skins[0] = RepSkin (Skins grown to one entry when empty); the mesh is kept.
   - No Skin: StaticMesh None sets DrawType to none (the object vanishes); otherwise DrawType static mesh and
     SetStaticMesh(StaticMesh).
   - Collision: with no Skin, no StaticMesh and HitPoints 0: SetCollision(false, false, false) and every
     non-pawn Attached actor is destroyed. Else with bDisableCollision: SetCollision(false, false, false) and every
     Attached actor is destroyed. Else SetCollision(true, InitialBlockActors, InitialBlockPlayers).
   - Unless the object blocks actors or players, both trace-blocking flags (zero and non-zero extent) are cleared.
   - Event (when set) by EventType: 0 TriggerEvent, 1 UntriggerEvent, 2 TriggerEvent then EventType = 3,
     3 UntriggerEvent then EventType = 2; Other = the object, instigator None.
   Stages are not remembered: every damage call re-applies the current stage and fires its event again.

### 2. SGamePlayObject.NativeHitWall(HitNormal, Wall)

1. speed = |Velocity|. Above 100 and bDamageable: ProcessDamage(int(0.02 speed)) on itself.
2. Wall not a Pawn: Wall.TakeDamage(int(speed Mass 0.002), I, Location, Velocity, SDTBase) with I = Owner when it
   is a Pawn, else None. Then if Wall is a damageable SBasicGamePlayObject that does not block actors, stop here
   (the object passes through: no bounce, no other change).
3. Bounce: Velocity = 0.5 (V - 2 (V . N) N). RotationRate Pitch, Yaw and Roll each = int(0.75 x) (from the old
   values). s = |new Velocity|.
4. Wall not a Pawn, and either (s < 60 and N.Z > 0.7) or not bBounce: the object settles:
   - bRotateToDesired; RotationRate = (32767, 32767, 32767); DesiredRotation = rotator(HitNormal), then its Pitch
     and Roll take the sign of the current Rotation's (when both are non-zero) and gain 32768 when more than
     32768 below the current value; DesiredRotation.Yaw = Rotation.Yaw; RotationRate.Yaw = 0.
   - CollisionHeight > CollisionRadius (a tall object): SetCollisionSize(R, R), Velocity.Z = 60, done (it tips
     over and settles on a later hit).
   - Else: Velocity = 0, setPhysics(PHYS_Rotating), bBounce and bFixedRotationDir off, SetCollisionSize(R,
     OriginalColHeight), event StoppedMoving.
5. Otherwise (a pawn wall, or still bouncing): when s > 40 and the object has a StaticMesh: every player controller
   hears BounceSound (volume 1, radius 100, pitch 1); NotifyNoise(0.5); DesiredRotation = (2 rand() % 65535,
   2 rand() % 65535, 0) (pitch from the second call, yaw from the first); RotationRate =
   (0, int((2 f1 - 1) 800000 / Mass), int((2 f2 - 1) 800000 / Mass)) for yaw and roll with f1, f2 from appFrand.

rand() is the C runtime generator and appFrand is `rand() / RAND_MAX`; neither is the script FRand stream.

### 3. Projectile.Explode

The engine native only destroys the projectile; classes override it.

### 4. SProjFrag.Explode(HitLocation, HitNormal)

1. A FragLaunchEmitter is destroyed and cleared. Event NotifyNoise(1.0). Every player controller hears
   SoundExplode (radius 500).
2. Spawns: SGrenadeExplosion at HitLocation with the rotation of the HitLocation vector (as written in both
   builds), instigator the frag's Instigator; SFlareBurnProjector (found by name) at 15 (HitLocation + HitNormal)
   facing -HitNormal (decal only).
3. I = Owner when it is an SBasePawn, else None. bExploding = true.
4. For every actor A of the level: d2 = int(|A - frag|^2).
   - Range != 0 and d2 >= Range^2: only the shake (SBasePawn, d2 < ShakeRange^2, same law as rule 1.3.4). Hidden
     actors are not in the list (Data), so a hidden pawn is neither hurt nor shaken.
   - Else (Range 0 means unlimited): skip mines with bExploding and frags with bExploding. Line of sight from the
     frag to A, flags `0x20286`; for an SBasePawn that is blocked, retry to (A.X, A.Y, A.Z + 0.9 H) and then to
     (A.X, A.Y, A.Z - 0.9 H) with `0x286`; the last line tried must hit nothing or A.
   - Damage = int((1 - dist / Range) Damage) (Damage when Range is 0); HitLocation = A.Location raised by 0.5 H.
   - SBasePawn: DamageType SDamage_Explosion when dist < fDistForBlast, else SDTBase; PawnTakeDamage(Damage, I,
     HitLocation, A - frag, type, 0, PercentDamageRefillable) with the spy hold rule; a pawn whose controller is
     not an SPlayerController gets nothing.
   - Other actors: TakeDamage(Damage, I, HitLocation, A - frag, SDamage_Explosion).
5. The frag is destroyed.

### 5. SMineProx.Explosion and SMineLaser.Explosion

Shared shape, with the centre point refloc used for reach and sight and the mine's Location used for falloff:
1. Spawn ExplosionEffect; set Exploded (proximity mine); event NotifyNoise(1.0); every player controller hears the
   explosion sound (ExplodeSound, SND_Explo for the laser mine) with radius 1000. PcOwner falls back to the
   owner's Controller; the instigator is PcOwner.Pawn (None without PcOwner). bExploding = true.
2. For every actor A of the level (the mine's own list order; hidden actors are not skipped here):
   - Reach: proximity mine `int(|A - refloc|^2) < MyRange^2`; laser mine `|A - refloc|^2 < MyDamageRange^2`
     (float). No zero case as the frag has: a range of 0 reaches nothing. Outside: shake only (SBasePawn,
     `int(d2) < ShakeRange^2`, rule 1.3.4 law).
   - Pawns (SBasePawn): sight from refloc with the three lines of rule 4 (all `0x286`); Damage =
     int((1 - |A - mine| / MyDamageRange) MyDamage), MyDamage when MyDamageRange is 0 (the value can go negative
     beyond MyDamageRange); HitLocation = A raised by 0.5 H; DamageType SDTBase when |A - mine| >= fDistForBlast,
     else MyDamageType; PawnTakeDamage with PillTag 0 and PercentDamageRefillable, spy hold rule.
   - Non-pawns: only SBasicGamePlayObject and SExtincteur actors; skip exploding mines and frags; one sight line
     `0x286` from refloc; Damage = int((1 - |A - mine| / MyDamageRange) MyDamage) (no zero guard); HitLocation =
     A.Location; Momentum A - mine, except an SExtincteur whose momentum is the forward vector of its Rotation
     turned by +16384 yaw (normalised); TakeDamage with MyDamageType.
3. The mine is destroyed.

### 6. SDamageVolume Tick (native, replaces the actor tick)

1. Only with Role Authority and bActive.
2. now = the engine's real-time clock (appSeconds), not Level.TimeSeconds. When `now - LastDamageTime > Period`:
   if LastDamageTime was not 0, LastTouched = None; LastDamageTime = now.
3. Every actor in Touching (in order) whose class is DamagedClass or a subclass (any class when DamagedClass is
   None):
   - a Pawn whose Controller answers `bIsDamageVolumeSensitive()` true: Controller.PawnTakeDamage(HitPoints, None,
     A.Location, A.Location - volume.Location, DamageType, 0, 0);
   - a Pawn without a controller, or one that answers false: nothing;
   - any other actor: TakeDamage(HitPoints, None, A.Location, A.Location - volume.Location, DamageType).
4. The volume's own Tick does not call the parent Tick: no script timers, no state code and no physics run on an
   SDamageVolume.

A newly touched actor is hurt on the next period boundary of the volume (one clock for all touchers); entering
with LastDamageTime 0 (damage-volume.md rule 1) makes the next tick hurt everyone touching at once.

### 7. SWeaponDefense_Shotgun.TraceFire(Accuracy, YOffset, ZOffset, TraceStartLocation)

YOffset and ZOffset are read and not used.
1. Needs an Instigator with a Controller. Axes Fwd, Right, Up from AccurateRotation; SR = ShootingRange.
2. Aim point E: the eye point (Location + EyeHeight Up + WalkBob) unless the pawn stands on a mover that is not
   moving, in which case Location + 55 Z (48 when crouched).
3. Trace start: TraceStartLocation when it is not zero; else on a still mover the aim point; else the weapon's
   fire start (the FX bone of a skeletal weapon mesh while the merc's laser is on, otherwise the eye point).
4. Pellet ends: E0 = E + SR Fwd + 0.067 SR Accuracy ((f1 - 0.5) Up + (f2 - 0.5) Right) with f1, f2 from appFrand
   (in that order). Then, each from the previous: E1 = E0 + 0.194 SR Up, E2 = E1 - 0.388 SR Up,
   E3 = E2 + 0.194 SR (Up + Right), E4 = E3 - 0.388 SR Right, E5 = E4 + 0.097 SR (Up + Right),
   E6 = E5 + 0.194 SR Right, E7 = E6 - 0.194 SR Up, E8 = E7 - 0.194 SR Right. So: the centre, four at 0.194 SR
   up, down, right, left, and four diagonals at 0.097 SR on each axis.
5. Each pellet is a bone trace (hit-zones.md) from the start to its end. Hitting a surface whose material is
   SURFACE_FakeCeiling retraces from 16 uu behind the hit (along its normal) to the same end. The nearest and the
   farthest hit distances update the impact dummy (its range fields get the nearest, and min(farthest, nearest +
   200)).
6. Pellet damage at hit distance d from the start, with BaseDamage B:
   - d < DistCac: B CMax;
   - d < DistMed: B (CoefCaC + (CoefMed - CoefCaC) (d - DistCac) / (DistMed - DistCac));
   - d < DistLoin: B (CoefMed + (CoefMin - CoefMed) (d - DistMed) / (DistLoin - DistMed));
   - else B CoefMin;
   truncated, then times 1.0 (centre), 0.3 (E1 to E4) or 0.5 (E5 to E8), truncated again.
7. Per hit actor only the largest pellet counts (with its hit location and pill tag); pellets do not add up.
8. Each recorded actor with damage >= 1: an SBasePawn's Controller gets PawnTakeDamage(damage, Instigator,
   hit location, 1000 (hit location - start), MyDamageType, pill tag, PercentDamageRefillable); other actors get
   TakeDamage(damage, Instigator, hit location, 1000 (hit location - start), MyDamageType).
   TotalShotgunDamageMultipler, RadiusScale, DemiAngleDispersion and its coefficient, and the class's own
   DamageLookupTable are not read by the native.

### 8. Spy health refill and poison: SBasePawn.NativeTick(DeltaTime)

Authority only (Role 4):
- Not poisoned (or Health <= 0): bHealthDecrease = Health. For an SPawnAttaque with 0 < Health < 100:
  - moving (|Velocity|^2 >= 0.01) or bLyingOnGround: HealthRefill = 0;
  - else HealthRefill += DeltaTime; when HealthRefill > MinTimeHealthRefill (3) and int(HealthRefill) grew this
    tick: n = min(NbPointsRefilledPerSecond (4), NbPointsRefillable); Health = min(Health + n, 100);
    NbPointsRefillable -= n; BloodDropStartTime = 0.
  The pool is filled by the script on damage (SPlayerController adds int(ActualDamage PercentDamageRefillable
  / 100) and zeroes HealthRefill). The cap is the literal 100, not the default health.
- Poisoned with Health > 0: L = DeltaTime fPoisonned_HealthLostPerSecond. When Health >= int(bHealthDecrease -
  2 L): bHealthDecrease -= L, Health = int(bHealthDecrease); at Health <= 0: bPoisonned = false, Health = 1 and
  the controller gets PawnTakeDamage(1500, PoisonnedBy or Instigator, Location, (0, 0, -1), SDTBase, 3, 0).
  When Health dropped below int(bHealthDecrease) by other damage, bHealthDecrease = Health.

### 9. SPawnAttaque.TickNative(DefaultHealth)

No health at all: the argument is read and ignored in the stock code and in Reloaded's replacement. The native
handles the screen glass effect only, as reloaded-hooks.md rule 11 describes, then the engine post-tick call. The
stock rules: on the controlling side, with DesiredGlassEffect set, a rain volume, bStickyShocking or speed above 240 (not on PHYS_RootMotion) clears the effect
(SetDesiredGlassValue(0, 1.0), then TestAndSetEMFVisible); GlassEffect moves to
`int((TimeSeconds + 0.1) SlopeGlassEffect + BGlassEffect)` while that stays in [0, 255] and has not passed the
target (else the target); a perturbation jitters it while `cos(PerturbationValue TimeSeconds) > 0`, with 125 in
place of a value equal to the target.

## Constants

| Name | Value | Provenance |
|---|---|---|
| GPO self damage on impact | speed > 100, 0.02 speed | decoded |
| GPO wall damage | speed Mass 0.002, SDTBase | decoded |
| GPO bounce | restitution 0.5, spin 0.75, settle below 60 on N.Z > 0.7, tip pop 60, noise above 40, spin 800000 / Mass | decoded |
| GPO blast | linear to 0 at DamageActorRange, sight 0x286 | decoded |
| shake law | strength 50 S / d2, speed 2000 (S / d2), fade 500 | decoded |
| spy hold window | 30 uu vertical | decoded |
| frag | sight 0x20286, pawn retries at +-0.9 H, hit point +0.5 H, decal at 15 (HitLocation + HitNormal) | decoded |
| explosion sound radii | frag 500, mines 1000, GPO 200, bounce 100 | decoded |
| shotgun pattern | spread 0.067 SR Accuracy, ring 0.194 SR, diagonals 0.097 SR, factors 1 / 0.3 / 0.5, momentum 1000 | decoded |
| health refill | 3 s, 4 per second, cap 100, still below 0.01 squared speed | decoded |
| poison death | 1500 damage, SDTBase, pill 3 | decoded |

## Network

ProcessDamage, NativeHitWall, the explosions, the damage volume and the health refill run on the server (the
volume tick and NativeTick test Role; the others run where the script calls them, which is the server). Health
replicates. The shotgun native runs where the script's fire runs (the server for damage).

## Edge cases

- A gameplay object at 0 hit points keeps re-applying its last stage on later damage calls only until
  bAlreadyDestroyed returns early (the stage at destruction is applied in the same call).
- A frag with Range 0 reaches every actor of the level.
- A proximity mine with MyRange above MyDamageRange can deal negative damage at the edge.
- The damage volume period runs on wall-clock time; a server running slower or faster than real time hurts at
  the real-time rate.

## Evidence

- Editor: ASGamePlayObject::execProcessDamage 0x111c6ec0 with the body 0x111c5e00, execNativeHitWall 0x111c6850,
  AProjectile::Explode 0x111b39a0, ASProjFrag::Explode 0x111e1f50, ASMineProx::Explosion 0x111cb7f0,
  ASMineLaser::Explosion 0x111cc980, ASWeaponDefense_Shotgun::execTraceFire 0x111d69c0 with GetFireStart
  0x111d66e0, ASBasePawn::execNativeTick 0x1120ff60, ASPawnAttaque::execTickNative 0x1121f350; event stubs
  CastToSPlayerController 0x111bde00 and the by-name event callers 0x111bf270, 0x111e16d0, 0x111cb720.
- Game: execProcessDamage 0x10a8fc40 with the body 0x10a8fc90 (constants checked), execNativeHitWall 0x10a90870,
  SProjFrag::Explode 0x10ab4780, SMineProx::Explosion 0x10ac11b0, SMineLaser::Explosion 0x10a93f00, the
  SDamageVolume vtable 0x10bfd848 slot 0x74 = Tick 0x10a85f90 with appSeconds 0x10904030 and the sensitivity event
  0x10a86240, shotgun TraceFire 0x10ab9d00, TickNative 0x10af5cf0.
- Reloaded.Core: TickNative body 0x100caae0 (no health code).
- REA recheck 2026-10-26 (game build, Ghidra 12.1.4 through the REA tools): ProcessDamage body 0x10a8fc90 (hidden
  skip 0x10a8fdf8, reach 0x10a8fe58, sight 0x10a8fe8d, falloff 0x10a8fecb, pawn path 0x10a8ffa5, shake 0x10a90055),
  SProjFrag::Explode 0x10ab4780 (hidden skip 0x10ab4a8d, Range 0 case 0x10ab4aea, the three sight lines 0x10ab4c23 to
  0x10ab4d05, damage type 0x10ab4e0b, hold order 0x10ab4e20) and SMineProx::Explosion 0x10ac11b0 (no hidden skip, no
  range 0 case 0x10ac134a, damage type 0x10ac1541, extinguisher momentum 0x10ac17d8) all match rules 1, 4 and 5. The
  exec thunks (0x10a8fc40, 0x10ab9d00, 0x10ae3c20, 0x10abcdb0) are not functions in that analysis; rules 7 and 8 rest
  on the editor build and the local game decompiles as before.
- Field, bit and enum names from the reflection dumps; script signatures from the decompiled packages (local).

## Open questions

1. The x87 precision mode in the running game (Direct3D may lower it): it can move a truncated percent or falloff
   by one at exact boundaries.
2. The hear-sound call the natives make on each player controller (vtable 0x14c): parameter meaning beyond the
   sound, volume and radius.
