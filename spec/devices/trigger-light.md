# STriggerLight (switchable map light)

Units:
- def:SBase.STriggerLight
- uc:SBase.STriggerLight:SetEMFDLightToClient
- uc:SBase.STriggerLight:UnSetEMFDLightToClient
- uc:SBase.STriggerLight:PostBeginPlay
- uc:SBase.STriggerLight:Trigger
- uc:SBase.STriggerLight:UnTrigger
- uc:SBase.STriggerLight:Timer
- uc:SBase.STriggerLight:LightToggle
- uc:SBase.STriggerLight:LightOn
- uc:SBase.STriggerLight:LightOff
- eng:ASTriggerLight::Tick
- rep:SBase.STriggerLight.SetEMFDLightToClient
- rep:SBase.STriggerLight.UnSetEMFDLightToClient

## Purpose

A dynamic map light (extends `Engine.Light`) that events switch, and that `SGamePlayObject.ObjectLights` put out
when the object breaks or is chaffed. 1,114 placed on 64 maps (ShadD alone has 161). Event senders seen in the
maps: SMagicEvent groups (most), SInterrupteur switches (138), SGamePlayObject damage stages and events (135),
SoundTriggers (54), SAlarms (39), objectives and computers.

## Data

| Field | Default | Meaning |
|---|---|---|
| `bInitialyOn` (edit) | false | lit at start (715 placed lights set it true; the rest start dark) |
| `TriggerMethode` (edit) | TriggerControl | Control: Trigger lights it; Toggle (568 placed): Trigger flips it; UnTriggerOnly (28): Trigger ignored |
| `GzzzitSound` (edit) | `RA_Break_light` | break sound |
| `AssociatedShadowActors` (edit) | empty | meshes drawn only while the light is lit (14 placed lights, DeftD) |
| `DistAlwaysVisibleIn`, `DistNeverVisibleOut` (edit) | 2500, 2500 | network relevancy distances (`net-relevancy.md`) |
| `InitialLightType`, `bInitialCorona`, `bInitialUsesLightBeam` | | remembered placed type, corona and beam |
| `CurrentLightType` (transient) | LT_None | the type the native tick last applied |
| `bForceReplicationForCurrentLightType` | false | set once at start: the light type is sent once |

## Rules

1. PostBeginPlay (every machine, the parent's is not called): remember `LightType` and `bCorona`. Not initially on:
   type None, every associated shadow actor `DT_None`, no corona. Initially on: the placed type and corona. A
   hidden light becomes not hidden with draw type None. Set `bForceReplicationForCurrentLightType`.
2. Trigger: TriggerControl → LightOn; TriggerToggle → LightToggle; UnTriggerOnly → nothing. UnTrigger → LightOff.
3. LightOn: the placed type; the beam back when it had one.
4. LightOff(bBroken): broken → type LT_Flicker, `GzzzitSound` (Misc slot, radius 50), a 0.5 s timer; the timer turns
   a still flickering light fully off. Not broken → type None, remember the beam flag and drop the beam.
5. LightToggle: when the type differs from the placed one, back to it (with its beam); else off as in rule 4.
6. `SetEMFDLightToClient` / `UnSetEMFDLightToClient`: EMF shown (`bEMFIsOn`, opacity 1) or not (opacity 0.3).
   Declared as reliable server-to-client calls; no script calls them.
7. ObjectLights (`gameplay-object.md`): a destroyed object calls LightOff, a chaff lock LightOff(true) (the flicker),
   the end of the lock LightOn, a reset LightOn or LightOff by `bInitialyOn`; the object's Trigger and UnTrigger are
   passed on. `SGamePlayObject.SetLockedByChaff` reads `CurrentLightType` to skip a lock when every light is off.

## Native tick

8. ASTriggerLight::Tick (game 0x10ab9470), every machine after the actor tick: when `LightType` differs from
   `CurrentLightType`, copy it; a dark light loses its corona and draws every associated shadow actor with
   `DT_None`; a lit one gets `bCorona = bInitialCorona` and draws them as `DT_StaticMesh`.

## Network

- LightType changes reach clients through the Actor list (`LightType` when dirty) and the class list's forced send
  (`net-replists.md` rule 25); the vision group (`bEMFIsOn`, `EMFOpacity`, `EMFIntensity`, `HeatIntensity`) on
  every update. Relevancy uses the two distances. Clients run rules 1 and 8.

## Render

- The client's `DeviceViews` follows every STriggerLight and switches the map light of the same name (the light's
  actor id) when its type goes to or from None: the dynamic world term (`WorldLights`), the actor lighting set and
  the corona (`RenderMap.SetLightOn`). The flicker of a breaking light is drawn steady for its half second.

## Port notes

- `src/OpenScct.Game/SBase/STriggerLight.cs`; render wiring in `game/Scripts/Views/DeviceViews.cs`,
  `game/Scripts/Render/MapBuilder.cs` (RenderMap), `ActorLighting.cs`, `Coronas.cs`; the beam (live LightType and
  UsesLightBeam) in `game/Scripts/Render/LightBeams/MapLightBeams.cs` (docs/RENDERING.md#light-beams).
- Not drawn: `AssociatedShadowActors` (the maps give them as package paths of baked StaticMeshActors, which the
  sim does not hold; they stay drawn) and the thermal heat of lights (`HeatIntensity` 500 with `HeatRadius` 200/1000 on 31 lights). Static meshes lit once at
  build keep their light set when a light switches; moving actors relight every 0.1 s.

## Sources

- `local/re-out/uc/SBase/STriggerLight.uc`; game 0x10ab9470 (Tick: LightType +0x2d1, CurrentLightType +0x326,
  corona bit 0x10 of +0x2e8, `AssociatedShadowActors` +0x30c/+0x310).
