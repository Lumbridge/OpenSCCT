# SGPOControl (security control zone)

Units:
- def:SBase.SGPOControl
- uc:SBase.SGPOControl:PostNetBeginPlay
- uc:SBase.SGPOControl:LockChaff
- uc:SBase.SGPOControl:s_On.BeginState
- uc:SBase.SGPOControl:s_On.EndState
- uc:SBase.SGPOControl:s_On.Tick
- uc:SBase.SGPOControl:s_On.Timer
- rep:SBase.SGPOControl.CylinderHeight
- rep:SBase.SGPOControl.CylinderWidth
- rep:SBase.SGPOControl.ZoneDescription

## Purpose

A named area ("Library", "Second Floor") that groups security devices. A gameplay object names it in its
`ERControl` property; when that object is jammed (chaff, sticky shocker), the lock is passed on to the control zone,
which stays "on" for the lock length. 54 on 15 maps (CineE/S/U, DeftD/S, KrauE/U, MounE/S/U, ScheE/U, VertE/U/X);
users are SVolumetricSensor (12 maps), SLaserTrigger (8) and SCamera (4).

## Data

| Field | Default | Meaning |
|---|---|---|
| `ZoneDescription` | "Unnamed zone" | area name (every map one sets it) |
| `CylinderWidth` / `CylinderHeight` | 1500 / 200 | area cylinder (26 set; values 1100 to 2000 wide, 500 to 950 high) |
| `PreviousState` | None | state at start, returned to after a lock |
| Defaults | | hidden, bAlwaysRelevant, ROLE_SimulatedProxy, no movement replication, native |

20 also set CollisionRadius/Height (editor display of the cylinder).

## Rules

1. PostNetBeginPlay: PreviousState = current state name (no state: the class name, which GotoState treats as no
   state).
2. LockChaff(Length): SetTimer(Length, once), enter `s_On`. A second lock restarts the timer.
3. s_On: BeginState, EndState and Tick are empty (the native tick does not run while locked); Timer returns to
   PreviousState.
4. SBasicGamePlayObject.SetLockedByChaff (and the same call inside s_LockedByChaff) calls `ERControl.LockChaff(Length)`
   after entering its own lock. The objects that override the lock pass their StickyNeutralizationTime.
5. Malfunction broadcasts message 21 (`GPOChaffed`, "System malfunction") to the merc team with the control zone as
   the optional object; the text does not use the zone.

## Network

The description and the cylinder replicate in the first bunch (Role == Authority && bNetInitial); the state does not
(clients see no s_On). Always relevant.

## Open questions

1. The native side (cpptext ASGPOControl.h) and SHud's `GPOControlFromPawn` struct (a control zone plus a draw flag)
   suggest a merc HUD display of control zones; no script or decoded native uses it yet, so nothing is drawn.

## Evidence

- `local/re-out/uc/SBase/SGPOControl.uc`, `SBasicGamePlayObject.uc` (SetLockedByChaff, Malfunction),
  `SGameMessages.uc` (switch 21), `SHud.uc` (GPOControlFromPawn).
- Map survey: VertE SGPOControl2435 "Library", 2462 "Office", 2516 "Computer Center" (2000 x 500), 38 users.
