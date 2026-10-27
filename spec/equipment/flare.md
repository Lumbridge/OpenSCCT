# Flare (SGadFlareLauncher, SProjFlare, SFlareEmitter)

Units:
- def:SBase.SGadFlareLauncher, def:SBase.SProjFlare, def:SBase.SFlareEmitter, def:SBase.SFlareCourseEmitter,
  def:SBase.SFlareBurnProjector
- uc:SBase.SProjFlare:PostBeginPlay, uc:SBase.SProjFlare:Explode
- nat:SBase.SProjGrenade.HitWall (the class's own HitWall), nat:Engine.Projectile.NativeTouch (its ProcessTouch)
- uc:SBase.SFlareEmitter:PostBeginPlay (and the class's native Tick)
- uc:SBase.SFlareCourseEmitter:PostNetBeginPlay, uc:SBase.SFlareCourseEmitter:Destroyed
- uc:SBase.SFlareBurnProjector:PostBeginPlay, nat:Engine.Projector.AttachProjector,
  nat:Engine.Projector.AbandonProjector

## Purpose
The merc's "Flare (10 Units Pack)", item 7 of the merc team's inventory (`STeamDefenseur.ClassInventory`, not in the
default kit): a grenade that sticks to the first wall it hits and burns there as a short-range light for 20 s. The
original's only effect is light, sound, particles and a burn mark: no script or native reads the flare elsewhere (the
three class objects are referenced only by their registration), so it does not touch vision modes, the HUD or AI.
`SBallTrap` uses SProjFlare as its default `ProjectileClass` (TME33's one trap is set to SProjFrag instead,
spec/devices/ball-trap.md).

## Data
- `SGadFlareLauncher` (an SGadGrenLauncher): `ProjectileClass` SProjFlare, `Charge` 10, `RefireRate` 4, icon `D_Flare`.
- `SProjFlare` (an SProjGrenade): `TempsExplosion` 10, `ProjectileSpeed` 900, `MaxSpeed` 1600, `LifeSpan` 20, static
  mesh `SGameplayObjects.Gadgets.flare_grenade` at DrawScale3D 2, collision 2 x 2, `SoundLaunch` Flare_eclairGo,
  `SoundExplode` Flare_eclairExplo. Inherited: `Damage` 0, `MomentumTransfer` 50000, `MyDamageType` SDTBase,
  `ExploWallOut` 0, `ActorReboundFactor` 0.5. Fields `CourseEmitter`, `BaseToFollow` (game +0x354, +0x358).
- `SFlareEmitter` (an Emitter, `RemoteRole` SimulatedProxy, `bNoDelete` false): `LifeSpan` 20, `LightType` LT_Steady,
  `LightEffect` LE_PointAmbient, `LightBrightness` 200, `LightRadius` 25, `LightSaturation` 250, `LightPhase` 1,
  `bDynamicLight`, `bAffectOwnZoneOnly`, `SoundEffect` Flare_loop; three sprite sub-emitters (flare_anm 175 particles
  at 10/s for 2.5 s, flare_fixe sparks 20 at once up to 600 uu up, flare_anm 200 following the first), none respawned.
- `SFlareCourseEmitter` (an Emitter, not replicated): `LifeSpan` 5, `AutoDestroy`, steady light 200 radius 6, two
  sprite sub-emitters (flare_burn, flare_anm).
- `SFlareBurnProjector` (a Projector, `bStatic` false): `ProjTexture` sfx.Flare.Burn_flare, `FOV` 200,
  `MaxTraceDistance` 30, `DrawScale` 0.1, modulate / alpha blend, clips BSP and static meshes.

## Rules
1. **Throw**: the generic grenade launcher (grenade-throw.md) spawns and launches it; it flies, spins and falls as any
   grenade.
2. **PostBeginPlay** (every machine): the launch sound (volume 1, radius 100); off a dedicated server an
   SFlareCourseEmitter owned by the flare. Its PostNetBeginPlay bases it on its owner, so it rides the flare.
3. **HitWall** (native, game 0x10ae8be0; replaces the grenade bounce, so a flare never bounces off a wall):
   `bAlreadyRebounded` is set. On the authority, when there is a wall: `Wall.TakeDamage(int(Damage), Instigator,
   Location, MomentumTransfer x Normal(Velocity), MyDamageType)` and `Wall.MakeNoise(1)` (the native calls MakeNoise
   on the wall without testing it; the port skips it with no wall). The wall becomes `BaseToFollow`. Then, on every
   machine, `Explode(Location + ExploWallOut x HitNormal, HitNormal)`.
4. **ProcessTouch** (native, game 0x10ae8d90), reached through NativeTouch (game 0x10a31e80) only for a toucher that
   is a projectile target (`bProjTarget`) or blocks both actors and players: skipped when the flare has not
   rebounded yet and the toucher is its owner. Otherwise the velocity is mirrored on the touch normal and scaled by
   `ActorReboundFactor`, then its X and Y are zeroed and `ProjectileSpeed` takes its size: the flare drops off a pawn
   it hits.
5. **Explode** (every machine that runs it): P = HitLocation + Normal(HitNormal) x 15.
   - Authority: an SFlareEmitter at P turned to `rotator(HitLocation)`, based on `BaseToFollow`; the catch sound
     (SLOT_Misc, volume 1, radius 500, attenuated).
   - Everyone: an SFlareBurnProjector at P turned to `rotator(-HitNormal)` (into the surface); the course emitter is
     destroyed (its Destroyed clears the flare's reference); the flare is destroyed.
   - The fuse (grenade Timer, 10 s): the authority stops the flare and explodes it at its location with a zero normal
     (P is the location, no burn mark is found); a client copy is destroyed.
6. **SFlareEmitter.PostBeginPlay** (every machine): `AmbientSound = SoundEffect` (the burn loop),
   `fDateEmission = TimeSeconds`.
7. **SFlareEmitter native Tick** (game 0x10a863f0), after the emitter tick, once `TimeSeconds > fDateEmission`, with
   age = TimeSeconds - fDateEmission and f = int(age x 1234.567):
   - age < 15: `LightBrightness = (f & 15) + 100` (a flicker between 100 and 115);
   - else `LightBrightness = max(0, 100 - (age - 15) x 20 + (f & 7))`, near 0 when its 20 s life ends.
8. **SFlareBurnProjector.PostBeginPlay**: `AttachProjector`, `AbandonProjector(30)`, `Destroy`: the mark stays 30 s
   without its actor.

## Constants
In `K.Flare.cs`: the launch and catch sound radii (100, 500), the 15 uu stand-off, the 30 s mark, the hit noise 1,
and rule 7's 15 s, 1234.567, masks 15 and 7, 100 and 20.

## Network
- The flare replicates as any grenade; each machine runs its own copy's physics, so each sees its own copy hit and
  leaves its own burn mark (rule 5). The server's copy decides the burning flare: SFlareEmitter is spawned there and
  replicates to the clients (simulated proxy), where its PostBeginPlay and Tick run again, so each machine has its own
  loop and the same flicker from its own spawn time.
- The course emitter is never replicated: each drawing machine spawns its own.
- The catch sound is the server's (sent to the clients as ClientHearSound); the launch sound plays on every machine.

## Port notes
- The views draw an Emitter actor with `bDynamicLight` with its light (game/Scripts/Fx/EffectViews.cs) and the
  abandoned projections (`AbandonedProjectors`, game/Scripts/Fx/CombatFxView.Decals.cs).
- Estimated: the projection is not decoded (FOV 200 does not make a frustum); the mark is a square of the texture's
  width x DrawScale (12.8 uu) on the first surface within MaxTraceDistance along the projector's axis.
- Estimated: Touch has no normal in the port; it is taken as up off the top of the toucher, else horizontal.
- A flare stuck on a mover or gameplay object rides it on the server; the client copy of the SFlareEmitter is placed
  from its replicated base and location as the net layer does for any based actor.

## Evidence
- `local/re-out/uc/SBase/SProjFlare.uc`, `SFlareEmitter.uc`, `SFlareCourseEmitter.uc`, `SFlareBurnProjector.uc`,
  `SGadFlareLauncher.uc`; `Engine/Projectile.uc` (the script HitWall the native follows).
- ASProjFlare vtable 0x10bfa910 (InternalConstructor 0x10af05b0): slot 78 ProcessTouch 0x10ae8d90, slot 80 HitWall
  0x10ae8be0, the other slots the grenade's. Event names through the game name table: 0x9e3 TakeDamage, 0x3ca
  MakeNoise, 0x3cb Explode. ASFlareEmitter::Tick 0x10a863f0 (constants 0x10c10f80, 0x10c11038, 0x10c10a54,
  0x10c10bac, 0x10c109e0, 0x10c109f8). Private decompiles under local/re-out/c/flare.
- Offsets: +0x350 bits (bAlreadyRebounded 2), +0x328 ActorReboundFactor, +0x304 ExploWallOut, +0x308 Damage, +0x310
  MomentumTransfer, +0x31c MyDamageType, +0x368 fDateEmission, +0xf8 LightBrightness.
