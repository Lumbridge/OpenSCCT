# SProjoFixe (manned searchlight)

Units:
- def:SBase.SProjoFixe
- uc:SBase.SProjoFixe:Tick
- rep:SBase.SProjoFixe.bInUse
- def:SBase.SProjoLight
- def:SBase.SProjoInteraction
- uc:SBase.SProjoInteraction:InitInteract
- uc:SBase.SPlayerController:UseMirador
- uc:SBase.SPlayerController:s_UseObject_Mirador.GadTorcheSwitch
- uc:SBase.SPlayerController:s_UseObject_Mirador.GadLaserSwitch
- uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeDropAttacked
- uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeElbowShocked
- uc:SBase.SPlayerController:s_UseObject_Mirador.CanBeGrabbed
- uc:SBase.SPlayerController:s_UseObject_Mirador.GoInSmoke
- uc:SBase.SPlayerController:s_UseObject_Mirador.BeginState
- uc:SBase.SPlayerController:s_UseObject_Mirador.EndState

## Purpose

A fixed searchlight (extends `SOrientableObject`) a merc mans to sweep a spotlight. 7 placed: MissD, MissE, MissP
(1 each), OffsD (2), PyroD (2), all `Missile.Bunker.Bunk_Bunk_Projo` meshes described "Projector" or "Spotlight".

## Data

| Field | Default (maps) | Meaning |
|---|---|---|
| `CameraOffset` (edit) | (35, 0, 25) (maps: (35, 0, 100)) | spot and view position in the projector's axes |
| `bPersistentLight` (edit) | false | always lit |
| `LightOnSkin`, `LightOnSkinIndex` (edit) | None, 0 | lens skin while lit; `LightOffSkin` keeps the old one |
| `bInUse` (replicated) | false | lit / manned |
| `SLight` | | the spawned SProjoLight |
| light values | brightness 1000 (maps 800), hue 42 (126), saturation 191 (200), spot 400 x 400 (800 x 800), radius 64, period 32, cone 128, `EchelonRange` (2000 / 3000) | copied to the spot |
| `PitchConstraint`, `YawConstraint` | (0, 16000, 16000), (0, 25000, 25000) (maps: pitch -14000..1000, yaw -14000..14000) | turn limits around the placed rotation |

`SProjoLight` (Effects): steady `LE_ESpotShadow` light with a corona (`def_flare`), own zone only, ignored by the
Echelon lighting. `SProjoInteraction`: priority 30, "Use Mirador", in front, mercs only.

## Rules

1. Tick (every machine; the parent's tick is not called): the authority copies the interaction's `bInUse`; a
   persistent light is always in use. Going in use with no spot: spawn an `SProjoLight` at the projector, add it to
   the projector's ignored Echelon lights, copy brightness, hue, saturation, spot height and width, radius, period,
   cone and Echelon range, and, with a `LightOnSkin`, keep the current skin at the index when it differs and set
   the lit one. Going out of use: destroy the spot and put `LightOffSkin` back.
2. In use: a viewer not steering it adopts the replicated angles; the projector takes (iPitch, iYaw); the spot sits
   at `Location + CameraOffset` in the projector's axes and takes its rotation.

## Manning it

3. `SProjoInteraction.InitInteract`: mark the interaction in use, call the merc's `UseMirador` (the usual interaction
   start is not called).
4. `UseMirador` (server): clear the used object; the first projector interaction colliding within 500 uu of the
   pawn, else nothing. A merc's pad-up gadget is unloaded and cleared. The used object becomes the interaction's
   owner (the projector), the used interaction is remembered, and the controller mirrors into `s_UseObject_Mirador`.
5. `s_UseObject_Mirador` (extends `s_UseObject`, which turns the used orientable and leaves on Use): BeginState puts
   the laser out, makes the pawn grabbable, puts a lit merc torch away, hides the reticule, behind view, then the
   parent's entry. EndState: not grabbable, the controller levelled to the pawn's yaw (pitch 0), the reticule back
   unless the laser was on, first person, then the parent's exit (which releases the interaction, so the light goes
   off). Torch and laser keys do nothing; the merc can be drop attacked, elbow shocked and grabbed; smoke runs the
   usual reaction and returns him to his waiting state. Damage: `state-damage-reactions.md` (UseObjectMirador).
6. The view: `PlayerCalcView` Mirador source (game 0x10ab06f0) from the projector and `CameraOffset`.

## Network

- `bInUse` replicates from the authority; the turn angles replicate as for every orientable (`cam-network.md`).
  Every machine spawns its own spot from `bInUse` (rule 1).

## Port notes

- `src/OpenScct.Game/SBase/SProjoFixe.cs`, `SProjoInteraction.cs`, `SPlayerController.s_Mirador.cs`; render in
  `game/Scripts/Render/FlashLightView.cs`: with no merc torch on, the nearest lit spot takes the one spot-shadow slot,
  shaped by its copied values. Estimated: the cookie and attenuation (the torch's; SProjoLight names no Echelon
  material), and the 500 uu `CollidingActors` search as a location radius test in level order.

## Sources

- `local/re-out/uc/SBase/SProjoFixe.uc`, `SProjoLight.uc`, `SProjoInteraction.uc`, `SPlayerController.uc`
  (`UseMirador`, `s_UseObject_Mirador`, `s_UseObject`).
