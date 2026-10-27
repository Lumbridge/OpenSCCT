# Weapon, gadget and vision replication

Units: def:Engine.Inventory, def:SBase.SWeapon, def:SBase.SWeaponAttaque, def:SBase.SStickyShocker, def:SBase.SWeaponDefense, def:SBase.SWeaponDefenseDummy, def:SBase.SGadget, def:SBase.SGadTorche, def:SBase.SGadGrenLauncher, def:SBase.SGadFragGrenade, def:SBase.SGadSmokeGrenade, def:SBase.SProjGrenade, def:SBase.SProjFrag, def:SBase.SProjSmoke, def:SBase.SGoggle, def:SBase.SGoggleBeam, def:SBase.SGoggleBeam_ATT, def:SBase.SGoggleBeam_DEF, rep:Engine.Inventory.Charge, rep:SBase.SWeapon.Controller, rep:SBase.SWeapon.ServerFireWeapon, rep:SBase.SWeapon.ServerSound, rep:SBase.SWeaponAttaque.MuzzleCount, rep:SBase.SWeaponAttaque.ServerFireWeaponWithLocation, rep:SBase.SWeaponAttaque.ServerSwitchLaser, rep:SBase.SWeaponAttaque.bLaserOn, rep:SBase.SWeaponDefense.LocalMessage, rep:SBase.SWeaponDefense.ServerSetAccurateRotation, rep:SBase.SWeaponDefense.ServerSetROF, rep:SBase.SWeaponDefenseDummy.MuzzleCount, rep:SBase.SGadget.AddChargeOnClientFromServer, rep:SBase.SGadget.BroadcastTeamLocalizedMessageToServer, rep:SBase.SGadget.FullReload, rep:SBase.SGadget.IsAPadUpGadget, rep:SBase.SGadget.MyGadget, rep:SBase.SGadget.ServerAddCharge, rep:SBase.SGadget.ServerDecCharge, rep:SBase.SGadTorche.Malfunction, rep:SBase.SGadTorche.TorcheOFF, rep:SBase.SGadTorche.TorcheON, rep:SBase.SGadTorche.bInUse, rep:SBase.SGadGrenLauncher.ThrowGren, rep:SBase.SGadStickyCam.TellClientToDecCharge, uc:SBase.SGadStickyCam:TellClientToDecCharge, def:SBase.SGadStickyCam, rep:SBase.SProjGrenade.QuickLaunch, rep:SBase.SGoggleBeam.Shift, rep:SBase.SGoggleBeam.ShiftOnCLient, uc:SBase.SWeapon:ServerFireWeapon, uc:SBase.SWeapon:ServerSound, uc:SBase.SWeapon:GiveTo, uc:SBase.SWeaponAttaque:FireWeapon, uc:SBase.SWeaponAttaque:ServerFireWeaponWithLocation, uc:SBase.SWeaponAttaque:ServerSwitchLaser, uc:SBase.SWeaponAttaque:ServerSound, uc:SBase.SWeaponAttaque:PostBeginPlay, uc:SBase.SWeaponDefense:FireWeapon, uc:SBase.SWeaponDefense:ServerSetAccurateRotation, uc:SBase.SWeaponDefense:ServerFireWeapon, uc:SBase.SWeaponDefense:ServerSetROF, uc:SBase.SWeaponDefense:LocalMessage, uc:SBase.SWeaponDefense:EventLocalMessage, nat:SBase.SGadget.ServerDecCharge, nat:SBase.SGadget.ServerAddCharge, uc:SBase.SGadget:AddChargeOnClientFromServer, uc:SBase.SGadTorche:TorcheON, uc:SBase.SGadTorche:TorcheOFF, uc:SBase.SGadTorche:Malfunction, nat:SBase.SGadGrenLauncher.ThrowGren, nat:SBase.SProjGrenade.Launch, uc:SBase.SGoggleBeam:Shift, uc:SBase.SGoggleBeam:ShiftOnCLient, uc:SBase.SGoggle:PostBeginPlay, uc:SBase.SPlayerController:PostBeginPlay, eng:AInventory::GetOptimizedRepList, eng:ASWeapon::GetOptimizedRepList, eng:ASWeaponAttaque::GetOptimizedRepList, eng:ASWeaponDefenseDummy::GetOptimizedRepList, eng:ASGadget::GetOptimizedRepList, eng:ASGadTorche::GetOptimizedRepList, eng:ASGadTorche::IsNetRelevantFor, eng:ASProjGrenade::GetOptimizedRepList, eng:ASGoggleBeam::GetOptimizedRepList, eng:ASSmokeEmitter::GetOptimizedRepList

## Purpose

Replication of what players carry and throw in the slice: the spy's sticky shocker
(`SStickyShocker`, an `SWeaponAttaque`), the merc's rifle (`SWeaponDefense`) and its third-person
dummy, gadgets (`SGadget` and the merc flashlight `SGadTorche`, grenade launchers for frag and smoke),
thrown grenades (`SProjGrenade`), and the vision helpers (`SGoggle`, `SGoggleBeam`). Firing is
client-initiated with server-side traces; vision is client-local.

## Data

| Class | RemoteRole | Owner only | NetUpdateFrequency | NetPriority | Other |
|---|---|---|---|---|---|
| `Inventory`, `SGadget`, `SGadGrenLauncher` and grenade gadgets | SimulatedProxy | yes | 100 | 1.4 | `bOnlyOwnerSee`, `bHidden`, `bClientAnim`, only dirty, no movement |
| `SWeapon` | SimulatedProxy | yes | 100 | 1.4 | `bReplicateInstigator` |
| `SWeaponAttaque`, `SStickyShocker` | SimulatedProxy | no | 100 | 1.4 | visible, `bReplicateInstigator` |
| `SWeaponDefense` | SimulatedProxy | no | 100 | 1.4 | hidden, `bOnlyOwnerSee`, script conditions (not nativereplication) |
| `SWeaponDefenseDummy` | SimulatedProxy | no | 100 | 1.0 | third-person merc gun, movement replicated |
| `SGadTorche` | SimulatedProxy | no | 100 | 1.4 | visible, native relevancy |
| `SProjGrenade`, `SProjFrag`, `SProjSmoke` | SimulatedProxy | no | 100 | 2.5 | `bNetTemporary`, `LifeSpan` 14 s, frag and smoke start in `PHYS_Falling` |
| `SSmokeEmitter` | SimulatedProxy | no | 100 | 1.0 | `LifeSpan` 10 s |
| `SGoggle` | DumbProxy | | 100 | 1.0 | spawned locally, never replicated |
| `SGoggleBeam`, `_ATT`, `_DEF` | None | | 100 | 1.0 | spawned locally per machine |

## Rules

1. **Inventory ownership.** Inventory is spawned on the server by `ServerSpawnInventory` and
   `ServerSpawnWeapon` (`rep-pawn.md` rule 6) and given to the pawn (`GiveTo` sets `Instigator`, owner
   and `MyWeapon`). `Charge` replicates to the owner when dirty; `SGadget.MyGadget` (the next gadget in
   the owner's list) and `IsAPadUpGadget` also go to the owner only.
2. **Charges.** `ServerDecCharge` and `ServerAddCharge` (native, reliable, simulated, client to server)
   change the server charge; `AddChargeOnClientFromServer(n)` (reliable, simulated, server to client)
   calls `AddCharge(n)` on the client copy. `SGadStickyCam.TellClientToDecCharge` does the same for the
   sticky cam.
3. **Merc rifle fire.** Each shot the owning client's `SWeaponDefense.FireWeapon` sends
   `ServerSetAccurateRotation(Yaw, Pitch, Roll)` with the exact ints of `MyDesiredRotation`, then
   `ServerFireWeapon(BulletInARow)` (both reliable). The server stores `AccurateRotation`, sets
   `BulletInARow`, counts the shot, takes dispersion 0 in `s_Binoculars_Def` else `GetAccuracy()`, calls
   `TraceFire(dispersion, 0, 0)`, plays the single-shot sound through `ServerSound` (radius 500, also a
   noise event), increments the dummy's `MuzzleCount` (replicated to all, so every client sees the
   muzzle flash by watching the counter change) and makes the dummy emit noise 1.0. Rate of fire mode
   changes go up through `ServerSetROF(mode)`. Reloading messages come back through `LocalMessage`
   (reliable, simulated, server to client), shown on the owner's reticule.
4. **Spy shocker fire.** `SWeaponAttaque.FireWeapon` (client) records `LastShot` and sends
   `ServerFireWeaponWithLocation(Location, Rotation.Yaw, Rotation.Pitch, Rotation.Roll)` (reliable): the
   server counts the shot and calls `TraceFireWithPos(Position, Orientation, DispersionActuelle, 0, 0)`.
   `Position` is a vector parameter, so the trace starts from the weapon location rounded to whole
   units; the angles are exact ints. Laser state goes up through `ServerSwitchLaser(b)`
   (`bLaserShouldBeOn`) and back down as `bLaserOn`; `MuzzleCount` drives the flash on other clients.
5. **Merc flashlight.** `TorcheON`/`TorcheOFF` (reliable, client to server) turn the light on the
   server (`LightType` `LT_Steady` or `LT_None`, `UsesLightBeam`), switch off the laser light if it is in
   use, and play the switch sound on the owner through `ClientPlaySound`. The light fields replicate
   through the Actor light group to every client that has the gadget (it is not owner only); `bInUse`
   replicates to everyone. `Malfunction(Time)` (client to server) enters `s_Malfunction` for `Time`
   seconds.
6. **Grenades.** `SGadGrenLauncher.ThrowGren(int, Vector, Rotator, bool)` (native, reliable, client to
   server) is a `Reloaded.Core.dll` thunk in the captured game (Reloaded's server-simulated grenades,
   `rl:net.grenades`). Reloaded reimplements it (net-reloaded.md rule 4): unless a Reloaded option is set, the
   launch location is rounded to whole units and each rotation component cut to its high byte (a multiple of
   256) before the grenade is spawned, which applies the network quantisation locally, so a listen host's or
   standalone throw matches what a remote client's throw produces on the server. The projectile is spawned on
   the server, replicated with `bNetTemporary` (one initial bunch, then the client simulates it alone) and
   `QuickLaunch`. `Launch(Force, bQuickLaunch)` is a native event.
7. **Vision helpers are local.** `SGoggle` is spawned by the player controller's `PostBeginPlay` only
   where `Role < ROLE_Authority`, or in standalone or on a listen server, and only for non-spectators;
   it never replicates. `SGoggleBeam` (the visible goggle lights on other players' heads) is spawned on
   each machine by the pawn (`rep-pawn.md` rule 5). Its `Shift` debug functions send a relative rotation
   up and down.


#### Engine.Inventory (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `Charge` | var | int | `bNetOwner && bNetDirty && Role == Authority` | S to C | | |

#### SBase.SWeapon (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `ServerFireWeapon` | func | int? | `Role < Authority` | C to S | R |  |
| 0 | `ServerSound` | func | Sound, ESoundSlot?, int? | `Role < Authority` | C to S | R |  |
| 1 | `Controller` | var | Controller | `Role == Authority && bNetOwner` | S to C | | |

#### SBase.SWeaponAttaque (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `bLaserOn` | var | bool | `Role == Authority` | S to C | | |
| 0 | `MuzzleCount` | var | int | `Role == Authority` | S to C | | |
| 1 | `ServerSwitchLaser` | func | bool | `Role < Authority` | C to S | R |  |
| 1 | `ServerFireWeaponWithLocation` | func | Vector, int, int, int | `Role < Authority` | C to S | R |  |

#### SBase.SWeaponDefense (script conditions)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `LocalMessage` | func | string, float, byte | `Role == Authority` | S to C | R | S |
| 1 | `ServerSetAccurateRotation` | func | int, int, int | `Role < Authority` | C to S | R |  |
| 1 | `ServerSetROF` | func | SRateOfFireMode | `Role < Authority` | C to S | R |  |

#### SBase.SWeaponDefenseDummy (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `MuzzleCount` | var | int | `Role == Authority` | S to C | | |

#### SBase.SGadget (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `ServerDecCharge` (native) | func | byte? | `Role < Authority` | C to S | R | S |
| 0 | `ServerAddCharge` (native) | func | byte? | `Role < Authority` | C to S | R | S |
| 0 | `BroadcastTeamLocalizedMessageToServer` | func | byte | `Role < Authority` | C to S | R | S |
| 0 | `FullReload` | func | - | `Role < Authority` | C to S | R |  |
| 1 | `AddChargeOnClientFromServer` | func | byte? | `Role == Authority` | S to C | R | S |
| 2 | `IsAPadUpGadget` | var | bool | `bNetOwner && bNetDirty && Role == Authority` | S to C | | |
| 3 | `MyGadget` | var | SGadget | `(!bSkipActorPropertyReplication \|\| bNetInitial) && Role == Authority && bNetDirty && bNetOwner` | S to C | | |

#### SBase.SGadTorche (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `TorcheON` | func | - | `Role < Authority` | C to S | R |  |
| 0 | `TorcheOFF` | func | - | `Role < Authority` | C to S | R |  |
| 0 | `Malfunction` | func | float | `Role < Authority` | C to S | R |  |
| 1 | `bInUse` | var | bool | `Role == Authority` | S to C | | |

#### SBase.SGadGrenLauncher (script conditions)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `ThrowGren` (native) | func | int, Vector, Rotator, bool | `Role < Authority` | C to S | R |  |

#### SBase.SProjGrenade (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `QuickLaunch` | var | bool | `Role == Authority` | S to C | | |

#### SBase.SGoggleBeam (nativereplication)

| Blk | Member | Kind | Type or parameters | Condition | Dir | Rel | Sim |
|---|---|---|---|---|---|---|---|
| 0 | `Shift` | func | Rotator | `Role < Authority` | C to S | R |  |
| 1 | `ShiftOnCLient` | func | Rotator | `Role == Authority` | S to C | R | S |

### Native lists and relevancy (decoded)

- `ASGadTorche::IsNetRelevantFor` returns the relevancy of the torch's `Instigator` (editor 0x111de220), so the
  torch is relevant exactly when its owner's pawn is.
- `ASSmokeEmitter`'s list sends only `AmbientSound`, with no parent list (editor 0x111e07c0).

## States

`SGadTorche` has `s_Malfunction` (timer driven, server). Weapon states (`s_Selected` and others) are
not replicated; each side enters them from its own code.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Inventory NetPriority | 1.4 | weight | decoded | def:Engine.Inventory |
| Grenade NetPriority | 2.5 | weight | decoded | def:SBase.SProjGrenade |
| Grenade lifespan | 14.0 | s | decoded | def:SBase.SProjGrenade |
| Smoke emitter lifespan | 10.0 | s | decoded | def:SBase.SSmokeEmitter |
| Rifle shot sound radius | 500 | uu | decoded | uc:SBase.SWeaponDefense:ServerFireWeapon |

## Network

Owning client: fire input, charge requests, torch switches, grenade throws (all reliable RPCs on owned
actors). Server: traces, damage, charges, light state. Other clients: see muzzle flashes through
`MuzzleCount`, torch light through the light fields, grenades through their initial bunch.

## Edge cases

- `SWeaponDefense` is not nativereplication: its function conditions are the script ones, and it
  declares no variables.
- Every rifle shot costs two reliable RPCs; under loss the server can fire with the previous
  `AccurateRotation` only if the second arrives first, which reliable ordering prevents.
- `MuzzleCount` changes that happen twice between two updates of the dummy show only one flash on
  remote clients (counter compare).

## Evidence

- `local/re-out/uc/SBase/SWeapon.uc` (54, 62, 64), `SWeaponAttaque.uc` (47, 80-96, 97),
  `SWeaponDefense.uc` (109-117, 254-262, 291, 298, 577), `SGadget.uc` (replication 35-45, 66),
  `SGadTorche.uc` (61-110), `SProjGrenade.uc` (6, 29, 32), `SGoggle.uc`, `SGoggleBeam.uc`,
  `SPlayerController.uc` `PostBeginPlay` (Goggle spawn, line 2943).
- Natives: `ServerDecCharge` 1292 at 0x10a9fab0, `ServerAddCharge` 1294 at 0x10a9fb70, `ThrowGren`
  1314 (Reloaded thunk), `NativeTickTorche` 1300 (Reloaded thunk).
- Native lists (editor): `AInventory::GetOptimizedRepList` 0x110ab8f0 and 0x110aba70,
  `ASWeapon::GetOptimizedRepList` 0x111d5fb0, `ASWeaponAttaque::GetOptimizedRepList` 0x111d97a0,
  `ASWeaponDefenseDummy::GetOptimizedRepList` 0x111e0250, `ASGadget::GetOptimizedRepList` 0x111da6a0,
  `ASGadTorche::GetOptimizedRepList` 0x111dff60, `ASGadTorche::IsNetRelevantFor` 0x111de220 and
  0x111df8f0, `ASProjGrenade::GetOptimizedRepList` 0x111e0ec0, `ASGoggleBeam::GetOptimizedRepList`
  0x11222a90, `ASSmokeEmitter::GetOptimizedRepList` 0x111e07c0.

## Open questions

1. What the boolean of `ThrowGren` selects (quick launch?) and the exact Reloaded option that turns the
   quantisation off. Trace a throw at 30 and 144 client fps.
4. Fire prediction added by Reloaded (`rl:net.fireprediction`) is client cosmetic; confirm that it does
   not change the RPCs above (its server side changes the first two FRand calls of TraceFire,
   reloaded-hooks.md rule 1).

### Resolved

1. (part) `ThrowGren` and Reloaded. Resolved: Reloaded rounds the launch location and cuts the rotation to 256
   steps before spawning (the network quantisation applied locally), unless a Reloaded option is set. Rule 6.
   Evidence: net-reloaded.md rule 4 (ANSWERS-net rep-inventory 1).
2. `ASGadTorche::IsNetRelevantFor`. Resolved: the `Instigator`'s relevancy (editor). Rule "Native lists and
   relevancy". Evidence: editor 0x111de220 (ANSWERS-net rep-inventory 2).
3. `ASSmokeEmitter` list. Resolved: only `AmbientSound`, no parent list. Evidence: editor 0x111e07c0
   (ANSWERS-net rep-inventory 3).
