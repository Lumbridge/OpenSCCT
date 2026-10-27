# Spy bullet (SSpyBulletLauncher, SProjMarqueurSpy) and the spy mark expiry

Units:
- nat:SBase.SSpyBulletLauncher.UseSpyBullet, nat:SBase.SSpyBulletLauncher.ThrowBullet
- uc:SBase.SSpyBulletLauncher:Use, uc:SBase.SSpyBulletLauncher:LocalMessage, uc:SBase.SSpyBulletLauncher:MyLocalMessage
- rep:SBase.SSpyBulletLauncher.ThrowBullet, rep:SBase.SSpyBulletLauncher.MyLocalMessage
- uc:SBase.SProjMarqueurSpy:AcceptStickableGPO, uc:SBase.SProjMarqueurSpy:PreBeginPlay,
  uc:SBase.SProjMarqueurSpy:PostBeginPlay, uc:SBase.SProjMarqueurSpy:Timer, uc:SBase.SProjMarqueurSpy:DropDetected,
  uc:SBase.SProjMarqueurSpy:Destroyed, uc:SBase.SProjMarqueurSpy:RemoveActorInList, uc:SBase.SProjMarqueurSpy:Tick
- uc:SBase.SBasePawn:Tick (the mark expiry), uc:SBase.SBasePawn:TellClientToTestAndSetEMFVisible,
  rep:SBase.SBasePawn.TellClientToTestAndSetEMFVisible
- uc:SBase.SPlayerController:s_Dead.BeginState (the mark clean-up)
- def:SBase.SSpyBulletLauncher, def:SBase.SProjMarqueurSpy

## Purpose
The spy's "Spy Bullet Launcher (5 Units Pack)": a shot from the drawn Shocker. A merc it hits is marked on the spies'
radar (and can be listened to with the hack-comm key). A shot into a wall leaves a small marker that, for 45 s, marks
every merc who comes within 500 uu of it.

## Data
`SSpyBulletLauncher`: `Charge` 5, `RefireRate` 1, `FireSound` SpyBullet_shot1, `HitPawnSound` SpyBullet_Touch,
`HitWallSound` SpyBullet_Indic, `MessageOnWall` "Zone radar activated". `SProjMarqueurSpy`: `TempsMarqueur` 45,
`fDetectionRadius` 500, static mesh noise_emitter, `HeatIntensity` 1, `EMFIntensity` 1, dumb proxy, always relevant.
`SBasePawn.TimeSpyBullet` 90.

## Rules
1. **Use** (owner's machine): UseSpyBullet, the gadget rumble, then the SGadget Use (empty).
2. **UseSpyBullet** (native 1295, game 0x10aa17d0): nothing unless the owner pawn has a mesh and a weapon,
   `WeaponLoaded` is set and `now - LastUsed >= RefireRate`. With `Charge < 1` the gadget plays `SND_EmptyCharge`
   (slot Misc, volume 1, radius 100) and stops. Otherwise `LastUsed = now`; from the pawn's `B R Hand` bone (name
   0x228) the origin is `O + 30 X - 4 Y + 8 Z` and the end `origin + 15000 X`; `ThrowBullet(origin, end)`. No charge
   is taken here.
3. **ThrowBullet** (native 1296, game 0x10aa1a00; a reliable client-to-server event; the second vector is the end
   point despite its name `GrRotation`): a multi line from the origin to the end (source the spy pawn, flags 0xc10bf).
   Hits in order:
   - no actor or a mover: stop, nothing happens.
   - a pawn (SBasePawn): when `Health > 0`, `HitBySpyBullet(0)` on it; the spy's controller hears `HitPawnSound`; a
     merc (SPAWNDEFENSE) hit also sends `BroadcastTeamLocalizedMessage(0, SGameMessages, 13)` (EnemyLocked) to the
     spies; every player hears `FireSound` at the spy (volume 1, radius 100); then a merc who is marked is taken out of
     every zone marker within that marker's `fDetectionRadius` (`RemoveActorInList`), so the zone never clears the
     direct mark. `Charge` goes down by one. A dead pawn: stop, nothing happens.
   - a laser projector (SLaserProjector): passed through.
   - anything else (world geometry, a gameplay object): an SProjMarqueurSpy is spawned at the hit, owned by the spy,
     rotated to the hit normal; the spy's controller hears `HitWallSound`; `LocalMessage(MessageOnWall, 3)`; every
     player hears `FireSound`; `Charge` goes down by one.
   The charge is only taken on the server; `Charge` reaches the owner by replication.
4. **LocalMessage** (the native's event): MyLocalMessage.
5. **MyLocalMessage** (server to owner): the owner's HUD adds `MessageOnWall` to its message stack for 3 s.
6. **SProjMarqueurSpy.PreBeginPlay**: in a rain volume `HeatIntensity` halves. **AcceptStickableGPO** is false.
7. **PostBeginPlay** (server only; the script function is not simulated): `SetTimer(TempsMarqueur)`. The gameplay
   object's own PostBeginPlay (interaction, hit points) is not called. **Timer** destroys the marker.
8. **Tick** (server): every merc within `fDetectionRadius` that is alive and not marked is put first in `DetectedActor`
   and gets `HitBySpyBullet(0)`. Then every listed merc farther than `1.1 x fDetectionRadius` is dropped.
9. **DropDetected(i)**: the entry leaves the list and its merc gets `DetectedByMarqueurSpy(0, false)` (the mark goes at
   once). **RemoveActorInList** takes a merc off the list without touching his mark.
10. **Destroyed** (server): every listed merc is dropped, then the gameplay object's Destroyed.
11. **Mark expiry** (SBasePawn.Tick, server, after NativeTick): when `fTimeSpy != 0`, not `bForceSpied` and
    `now - fTimeSpy > TimeSpyBullet` (90 s): `fTimeSpy = 0`, `bSpyBulletOn` off; when no pawn of this side is still
    marked, every controller of the other side stops listening (`bCanListenSpyBulletedGuy`); the controller's
    `OtherTeamCantHearMe`; `BroadcastTeamLocalizedMessage(SPAWNDEFENSE - SPawnType, SGameMessages, 14 for a merc, 61
    for a spy)`; `TellClientToTestAndSetEMFVisible(false)`. This covers every mark: the spy bullet, the merc laser and
    the trap mine. A zone mark does not expire while the merc stays in the zone only because the zone does not run that
    long (45 s).
12. **Death** (SPlayerController s_Dead.BeginState, server): `DetectedByMarqueurSpy(0 for a merc, 1 for a spy, false)`
    on the pawn; that native sets `fTimeSpy` back by `TimeSpyBullet`, so rule 11 runs on the next tick (with its
    message).
13. **TellClientToTestAndSetEMFVisible** (server to owner): `bSpyBulletOn` takes the value, then TestAndSetEMFVisible.

## Constants
Rule 2's hand offsets (30, -4, 8) and range 15000, rule 3's sound radius 100 and message switch 13, rule 5's 3 s,
rule 8's 1.1, rule 11's switches 14 and 61; the rest are the class defaults above.

## Network
- Use and UseSpyBullet run on the owner's machine; `ThrowBullet` goes to the server, which traces again from the
  origin the client sent.
- `MyLocalMessage` and `TellClientToTestAndSetEMFVisible` come back to the owner.
- The marker is a dumb proxy that is always relevant: every machine draws it; only the server ticks it. The marks
  (`bSpyBulletOn`, `iTeamSpying`) replicate on the pawn, and each spy's radar shows mercs with `iTeamSpying == 0`.

## Port notes
- Without a skeleton the hand bone is the drawn Shocker's estimated pose (the one the alarm snare and sticky cam use),
  so the shot follows the spy's aim. Estimated.
- The hit sounds the native gives the spy's controller are played as his interface sounds; the shot sound is a
  PlaySound at the spy that every player in reach hears. Estimated (the native calls a controller sound hook).
- Rule 2's mesh test is left out: the port's pawns do not always name a mesh.
- `RadiusPawns` walks the level's pawns in actor order with distance below the radius. Estimated.
- `OtherTeamCantHearMe` is not ported: the voice port reads `bSpyBulletOn` itself.
- The original has no projectile in flight: the shot is an instant trace.

## Evidence
- `local/re-out/uc/SBase/SSpyBulletLauncher.uc`, `SProjMarqueurSpy.uc`, `SBasePawn.uc` (Tick, HitBySpyBullet,
  TellClientToTestAndSetEMFVisible), `SPlayerController.uc` (s_Dead.BeginState).
- Game natives 0x10aa17d0 (UseSpyBullet), 0x10aa1a00 (ThrowBullet); event stubs 0x10aa2cf0 (ThrowBullet), 0x10aa2cc0
  (HitBySpyBullet), 0x10aa2c70 (BroadcastTeamLocalizedMessage), 0x10aa2c40 (RemoveActorInList), 0x10aa2f00
  (LocalMessage); names from the reflection name table (private decompile and disassembly under
  local/re-out/c/spybullet).
