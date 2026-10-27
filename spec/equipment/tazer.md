# Merc taser (SGadTazerLauncher, STazerEmitter)

Units:
- nat:SBase.SGadTazerLauncher.UseBulletSet, rep:SBase.SGadTazerLauncher.UseBulletSet
- uc:SBase.SGadTazerLauncher:Use (the damage, uc:SBase.SGadTazerLauncher:DamagePawn, is in
  spec/combat/shock-and-taser.md rules 27 and 28)
- eng:ASTazerEmitter::Tick, rep:SBase.STazerEmitter.PawnOwner, rep:SBase.STazerEmitter.PawnTarget,
  rep:SBase.STazerEmitter.fDefaultLength
- def:SBase.SGadTazerLauncher, def:SBase.STazerEmitter

## Purpose
The merc's "Tazer": a short electric shot from the drawn rifle that freezes (and in a walking state knocks down)
every pawn in a short cone in front of it. Each pawn hit gets a beam from the gun to it for 0.3 s; a miss shows a
short beam straight ahead.

## Data
`SGadTazerLauncher`: `RefireRate` 1, `Charge` -10 (never tested), `fBulletRange` 200, `fBulletAngle` 60 (not read
by the native), `FireSound` SpyBullet_shot1, `MyDamageType` SDTStickyShocker; `ImpactEffect`, `ImpactEffectMetal`
and `SpawnSFX` are unused. `STazerEmitter` (Effects): static mesh `SGameplayObjects.tazer_mesh.mesh_tazer_beam`,
`LifeSpan` 0.3, `bHidden`, simulated proxy, `fDefaultLength` 90, `HeatIntensity` 1, `bIgnoreEchelonLighting`.

## Rules
1. **Use** (owner's machine): nothing while `now - LastUsed < RefireRate`; else `LastUsed = now`, UseBulletSet, the
   gadget rumble, the SGadget Use (empty). The charge is not tested.
2. **UseBulletSet** (native 1301, game 0x10aa0b50; a reliable owner-to-server function, so it runs on the server):
   first every player controller hears `FireSound` at the merc (volume 1.5, radius 150, pitch 1). Without an owner or
   without `MyWeapon` nothing else happens. The aim frame is the weapon's `FX` bone (name 0x189): origin O, axis X.
3. For each pawn of the level's pawn list, in order: the aim point P is its Location, lowered by 0.95 x
   CollisionHeight when `bLyingOnGround`, else raised by 25 unless `bIsCrouched`. The pawn is skipped unless
   `Health > 0`, it is not the merc, `m_AttackedBy` is None and `bStickyShocking` is off. There is no team test. With
   A = O - 50 X and d = |P - A|: it needs `d < fBulletRange` and `X . normal(P - A) > cos((fBulletRange - d) x 0.01 x
   0.345 + 0.35)` (a half angle of about 60 degrees at the gun, 20 degrees at full range). A lying pawn's P is then
   raised by 20. A box line (extent 15, flags 0x86: movers, level, world geometry; pawns do not block) from O - 40 X
   to P, source the gadget, must hit nothing. Then:
   - `DamagePawn(pawn, merc, P, merc.Location, -10)` (shock-and-taser.md rule 27);
   - an STazerEmitter is spawned at O (owner the merc, the gadget's instigator), rotated to `rotator(P -
     merc.Location)`, with `PawnOwner` the merc and `PawnTarget` the pawn.
   Every pawn that passes is hit, not only the nearest.
4. After the loop, when a beam was spawned: from the merc to the last beam's pawn (Location), the yaw `atan2(dy, dx)`
   and the pitch `atan2(dz, sqrt(dx^2 + dy^2))`, each times 10430.21875, truncated, kept to 16 bits signed; the side
   is -1 when `X.Y n.X - X.X n.Y >= 0` (n the normalised direction), else 1. The merc's controller gets
   `ForceRotateTo(rot(pitch, yaw, 0), side, 3)` (it turns toward the pawn; server to owner).
5. With no beam spawned: one STazerEmitter at O rotated to the bone's X axis (pitch and yaw truncated the same way,
   roll 0), with `PawnOwner = PawnTarget = merc` (a miss).
6. **STazerEmitter Tick** (native, game 0x10a94920, every machine, after the actor tick): nothing until both pawns
   are set; then `bHidden` is cleared. The beam's frame F: when the merc is played on this machine (his controller's
   Player is a viewport) and has `MyWeapon`, that weapon's `FX` bone; else his `WeaponDummy`'s `FX` bone; else only
   his Location (the axes are left unset). The beam moves to F's origin (FarMoveActor).
7. A miss (`PawnTarget == PawnOwner`): while `LaserLenghtWithoutPawn` is 0, a box line (extent 15, flags 0x86, source
   the beam) from O - 30 X to O + fDefaultLength X: nothing hit gives `fDefaultLength`; a hit in front of O
   (`(H - O) . X >= 0`) gives |H - O|, one behind gives 0. `DrawScale3D = (LaserLenghtWithoutPawn, 15, 15)`,
   `Rotation = rotator(X)`.
8. A hit: the end point E is the pawn's Location, or `Location - (0, 0, CollisionHeight)` while it lies on the
   ground. `DrawScale3D = (|O - E|, 15, 15)`, `Rotation = rotator(E - O)`. The mesh is one unit long on X, so it spans
   the gun to the pawn.

## Constants
Rule 2's volume 1.5 and radius 150; rule 3's 0.95, 25, 50, 0.01, 0.345, 0.35, 20, 40, 15 and 0x86; rule 4's
10430.21875 and step 3; rule 7's 30. The rest are the class defaults above.

## Network
- Use runs on the owner's machine; `UseBulletSet` is sent to the server (CombatCall `UseBulletSet`, received by
  `SPlayerController.CombatNet`), which picks the pawns from its own copy.
- The damage and the shock follow shock-and-taser.md (server, then `ClientStickyShock` to the victim's owner).
  `ForceRotateTo` goes back to the merc's owner.
- The beams are replicated actors (simulated proxy, 0.3 s life): the native list sends `PawnOwner`, `PawnTarget` and
  `fDefaultLength` on change from the authority (`ASTazerEmitter::GetOptimizedRepList` 0x10a94790, the same as the
  script block). Each machine's own tick places, scales and shows the beam, so each draws it from its own copy of
  the merc's gun.

## Port notes
- The FX bone is the weapon's estimated pose (`CombatPorts.BoneCoords`) until skeletal poses run, as for the other
  gadgets that shoot from it. Estimated.
- The FireSound is a PlaySound at the merc on the server, heard by every player in reach (the native calls each
  player controller's sound hook with slot code 1, read as slot None with no override). Estimated reading.
- Rule 6's frame without a weapon or dummy uses the merc's rotation for the axes (the original's are unset).
- The beam is drawn unlit by `EffectViews` (its materials are additive glow, `bIgnoreEchelonLighting`). Estimated.
- Rule 4's controller event is found by a name index (0xa5490) that is not resolved statically; ForceRotateTo is the
  only SPlayerController event taking (rotator, int, int), and its step 3 matches the elbow attack's. Inferred.
- The miss trace in rule 7 is a 15 uu box from 30 uu behind the bone; its vector table entry for the lying end point
  is read as (0, 0, 1) (the engine's up vector also used by SetBase's default floor). Decoded with that reading.

## Evidence
- `local/re-out/uc/SBase/SGadTazerLauncher.uc`, `STazerEmitter.uc`, `SPlayerController.uc` (ForceRotateTo).
- Game natives 0x10aa0b50 (execUseBulletSet), 0x10a94920 (ASTazerEmitter::Tick, vtable 0x10bf5d5c), 0x10a94790
  (GetOptimizedRepList); helpers 0x10a1d200 (SingleLineCheck), 0x109b57b0 / 0x109b7b50 (bone index and coords),
  0x10aa2df0 (DamagePawn event), 0x10b83aa0 (_ftol); field offsets from the class layouts (MyWeapon 0xa84,
  WeaponDummy 0xb64, bLyingOnGround and bStickyShocking in 0xaf0, bIsCrouched in 0x4a8, m_AttackedBy 0x630). Private
  decompile and disassembly under local/re-out/c/tazer.
