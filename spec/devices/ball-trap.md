# SBallTrap (projectile launcher)

Units:
- def:SBase.SBallTrap
- uc:SBase.SBallTrap:ThrowGren
- uc:SBase.SBallTrap:Trigger
- uc:SBase.SBallTrap:UnTrigger
- uc:SBase.SBallTrap:Timer
- rep:SBase.SBallTrap.ThrowGren

## Purpose

A hidden actor that, once triggered, throws a projectile along its rotation at a fixed period until untriggered.
One on one map: TME33 `SBallTrap8390` (Tag `Projectile`), triggered by the `Projectile` AnimNotify_Trigger at time 0
of `SAnimatedMesh220`'s `tme04_gun_seq`, throwing `SProjFrag` (frag grenades) every 3 s at 700 to 1400 force. Nothing
untriggers it, so it fires for the rest of the round once the gun animation has played.

## Data

| Field | Type | Default | TME33 |
|---|---|---|---|
| `ProjectileClass` | class<Projectile> | SProjFlare | SProjFrag |
| `fFireRate` | float | 1.0 s | 3.0 |
| `iVelocityMin` | int | 500 | 700 |
| `iVelocityMax` | int | 1000 | 1400 |

## Rules

1. `Trigger`: `SetTimer(fFireRate, true)` (a new trigger restarts the period).
2. `UnTrigger`: `SetTimer(0, false)` (stops).
3. `Timer`: `ThrowGren(int(RandRange(iVelocityMin, iVelocityMax)), Location, Rotation)`.
4. `ThrowGren(Force, Origin, Rotation)`: spawn `ProjectileClass` owned by the trap at Origin/Rotation; when it is an
   `SProjGrenade`, `Launch(Force, false)`: velocity along the rotation of `Force` plus the grenade launch bonus
   (grenade-throw.md; the trap's own velocity is zero). A non-grenade class is spawned without launch.
5. With its default class the trap throws flares (SProjFlare, spec/equipment/flare.md): each sticks where it hits
   and burns for 20 s. The only placed trap (TME33) throws frags.

## States

None.

## Network

`ThrowGren` is declared a server function (`reliable if Role < ROLE_Authority`); the trap only fires on the server
(triggers run there), where the call is local. The projectile replicates as any grenade.

## Edge cases

- The trap is `bHidden`; nothing draws it.
