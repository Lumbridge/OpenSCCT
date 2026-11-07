# Gadgets

This reference follows gadgets from loadout selection through simulation, networking
and the HUD. Individual rules and evidence are in the
[equipment specifications](../spec/equipment/INDEX.md).

**On this page:** [Kits](#kits) · [The gadgets](#the-gadgets) · [Network](#network) · [Tests](#tests) · [Not done yet](#not-done-yet)

## Kits

`DefaultProfile.ini` ([spec/equipment/loadouts.md](../spec/equipment/loadouts.md)), in slot order:

| Side | Slot 0 | Slot 1 | Slot 2 | Slot 3 | Weapon | Always |
|---|---|---|---|---|---|---|
| Spy | smoke grenade (5) | chaff grenade (5) | sticky cam (5) | heartbeat sensor | Shocker | binoculars |
| Merc | frag grenade (4) | mine launcher (3) | camera network | gas mask | rifle | torch, laser, goggles |

Use [Change Gear](../spec/additions/gear-change.md) to select other gadgets from the
team's `ClassInventory`. Host restrictions allow all gadgets by default. The spy cloak
uses the same selection path; its server-controlled `GlassEffect` ramp is drawn by the
[renderer](RENDERING.md#cloak). Standalone games raise gadget charges to 999.

`SGameInfo.AddDefaultInventory` runs `Loadouts.UpdateInventory` (a server to owner call: the owner's machine reads its
profile and asks the server to spawn each item) and `AddDefaultGadgets` (the merc's torch and laser). The number of
slots comes from the team (`STeamInfo.iNBGadgets`), which login copies from the host's settings; those start from
`USPersistentData.Init` (InitPersistentData: 4 gadgets a side, everything allowed). Settings that never ran it leave
0 slots: no gadget box, no Alt-fire use, no quick choice.

## Selection and use

| Input | What happens | Code |
|---|---|---|
| 1 to 4 (`SelectGadget n`) | the slot's gadget, loaded | `SPlayerController.SelectGadget`, `PawnInventory.SelectSlot` |
| wheel | next / previous with the default mouse setting, else run / walk | `WheelUp`, `WheelDown` |
| Alt-fire | grenades: hold to charge, release to throw; any other gadget: `Use(1)` when the muzzle is clear | `SPlayerController.AltFire` |
| `GadgetQuickChoice 0/1` (spy, holstered) | stand up if crouched, then throw the slot's grenade at the feet; 2 s between quick throws, none in the first 2 s of play | `MarioGadgetQuickChoice`, `EquipmentMovementHooks.LaunchQuickGadget`, `RealGadgetQuickLaunch` |

Refills: the ammo crate refills the rifle (and a Sabotage bomb), never gadgets ([spec/equipment/ammo-stock.md](../spec/equipment/ammo-stock.md)). A spy
who picks up another spy's sticky cam gets one charge back (`ReloadStickyCam`); removing a mine from the mine menu
gives its charge back; the team backpack shares one charge of a gadget.

## The gadgets

Use the linked specifications for timings, ranges, charges and exceptions.

| Gadget | Purpose | Main implementation | Rules |
|---|---|---|---|
| Smoke grenade | Smoke cover and merc knockout | `SProjSmoke`, `SSmokeEmitter` | [Smoke grenade](../spec/equipment/smoke-grenade.md) |
| Chaff grenade | Disrupts electronics and gadgets | `SProjChaffe` | [Chaff grenade](../spec/equipment/chaff-grenade.md) |
| Sticky cam | Remote view, zoom and gas | `SGadStickyCam`, `SStickyCam` | [Sticky cam](../spec/equipment/sticky-cam.md) |
| Alarm snare | Decoy sounds and nearby alarms | `SGadNoiseEmitter`, `SNoiseEmitter` | [Alarm snare](../spec/equipment/alarm-snare.md) |
| Spy bullet | Marks a target or area for the spy team | `SSpyBulletLauncher`, `SProjMarqueurSpy` | [Spy bullet](../spec/equipment/spy-bullet.md) |
| Heartbeat sensor | Detects nearby pawns in the view cone | `SGadHeartBeatSensor` | [Heartbeat sensor](../spec/equipment/heartbeat-sensor.md) |
| Mine launcher | Place, select and remove mines | `SGadMultiMinesLauncher`, `SMines` | [Mine launcher](../spec/equipment/mine-launcher.md) |
| Presence detector | Placed trap that detects moving spies | `SPresDetector` | [Presence detector](../spec/equipment/presence-detector.md) |
| Backpack | Shares gadget refills with teammates | `SGadBackPack`, `SBackPackInteraction` | [Backpack](../spec/equipment/backpack.md) |
| Flare launcher | Throws a burning light source | `SGadFlareLauncher`, `SProjFlare` | [Flare launcher](../spec/equipment/flare.md) |
| Taser | Close-range electrical attack | `SGadTazerLauncher`, `STazerEmitter` | [Taser](../spec/equipment/tazer.md) |
| Camera network | Merc access to map cameras | `SGadCamNetwork`, `SCamNetwork` | [Camera network](../spec/equipment/camera-network-gadget.md) |
| Gas mask | Protects against smoke and gas while air remains | `SGadGasMask` | [Gas mask](../spec/equipment/gas-mask.md) |
| Spy binoculars | Zoom and objective marking | `SpyBinoculars` | [Spy binoculars](../spec/equipment/spy-binoculars.md) |
| Motion tracking | Merc view of moving targets | `SMotionTrackingVision` | [Motion tracking](../spec/equipment/motion-tracking.md) |
| EMF vision | Highlights electronics and visible electronic equipment | `EmfVision`, `PawnVision` | [EMF vision](../spec/equipment/vision-goggles.md) |

Shared helpers: `GadgetWorld` (sight lines with flags 0x286, the cylinder test of the laser beam), `GadgetBlast`
(mine and frag damage fall-off behind walls), `GadgetHud` (the SHud message stacks and view states), `GadgetFx` (the
view events: cam stuck, gas, chaff burst, mine placed or gone, motion lock, binocular marks).

## Weapons from the keys

What each side fires in SCCT Versus Enhanced 4.0 (DefaultProfile.ini `ATTWeapClass` / `DEFWeapClass`,
[spec/equipment/loadouts.md](../spec/equipment/loadouts.md)): the spy carries only the Shocker (`SStickyShocker`), the merc only the rifle
(`SWeaponDefense`, "FN2000 Auto"). The Shocker deals no health damage: it costs 15 of the spy's energy per shot (6 shots
from a full bar, 7 back each second), freezes the pawn it hits (sticky shock), and disables devices
([spec/equipment/spy-shocker.md](../spec/equipment/spy-shocker.md)). The rifle has a 25-round clip and 100 in reserve, three rates of fire and
distance-banded damage ([spec/equipment/merc-rifle.md](../spec/equipment/merc-rifle.md)). The same keys on both Enhanced layouts (Default_kbd_0.ini / _1.ini):

| Key | Spy | Merc |
|---|---|---|
| E (FireMode) | tap: draw the Shocker with its laser; hold 0.5 s: draw without; press again: holster | torch |
| Left mouse (Fire, GadgetQuickChoice 0) | Shocker drawn: shoot (held: every 0.5 s); holstered: quick-throw slot 0's grenade | fire (auto: held) |
| Right mouse (AltFire, GadgetQuickChoice 1) | use the selected gadget (sticky cam: fire it, in the cam: leave) | use the selected gadget (grenades: hold and release) |
| F (ROF) | | rate of fire: Auto to Burst, Burst to Auto |
| R (Reload) | | reload a part clip |
| B | binoculars (with the Shocker out: holster first) | scope; Page Up / Page Down zoom 30, 20, 10 |
| Shift (Use) | in the sticky cam: gas | in the camera network: next camera |
| L | laser on and off (Shocker drawn) | |

The spy cannot shoot with the Shocker holstered: the left button then throws a grenade, as in the original.

## Shots on screen

- Impacts go through the level's impact manager (`OpenScct.Game.Equipment.ImpactManager`, [spec/equipment/impact-manager.md](../spec/equipment/impact-manager.md)):
  the server's rifle and Shocker traces add a mark, which every player's machine keeps in its `ImpactTable` (50 marks,
  15 s) with its spark and sound.
- [game/Scripts/Fx/CombatFxView.cs](../game/Scripts/Fx/CombatFxView.cs) draws, on the client: the muzzle flashes (the Shocker's SMuzzleSticky from its
  replicated shot count, the third-person rifle's ClientFireMuzzle, the local merc's own shots from his clip), the
  tracers (`SFireTrace` actors flying at 7000 uu/s to their impact), the marks and the spark bursts. With converted `shared/fx` content, these use the original meshes, decals and particles.
  Missing content falls back to simple flashes, streaks, marks and sparks. See [effects](EFFECTS.md).

## Network

Gadget calls travel as `CombatCall`s over the combat bridge (`NetCombatBridge`, one reliable function on the player's
controller): owner to server (`GadgetNet.ToServer`) and server to owner (`GadgetNet.ToOwner`), received by
`GadgetNetReceive`. An owner-to-server call naming an actor the sender does not own is refused. A call naming an actor
the other side does not have yet waits for its channel. The offline play session installs the bridge on both of its
machines (`PlaySession.InstallCombatNet`), since its game rules come from the map loader.

Devices and mines reach the game rules and the HUD through the level's `IDeviceHooks`, which
`EquipmentDeviceHooks` (`CombatEquipment.Install`) answers: team messages go to
`SGameInfo.BroadcastTeamLocalizedMessage` on the server (the spy trap's "enemy locked" and the chaffed device's
message to the mercs), map hints to `SHud.AddTutoMapMessage`. `GetPlayerProfile` still has no source in the game
(it returns null), so whatever reads the profile there takes its no-profile path.

## HUD views

[game/Scripts/Hud/Gadgets/](../game/Scripts/Hud/Gadgets) (rules in [spec/equipment/gadget-hud.md](../spec/equipment/gadget-hud.md); the Godot-free answers are
`OpenScct.Game.Equipment.GadgetHudRules`, unit tested):

| View | Page or area | Up while |
|---|---|---|
| `StickyCamPage` | P_StickyCam replaces P_HudATT | the spy's SHud is in `s_Hud_UseObject_StickyCam` |
| `CamNetworkPage` | P_CamNetWork replaces P_HudDEF: camera name, ALERT while a spy hacks | the merc's SHud is in `s_Hud_UseObject_CamNetwork` |
| `StickyCamMenuDriver` | StickyCamInteraction on P_HudATT | `s_StickyCam_MenuInteraction` with the sticky cam selected |
| `MineMenuDriver` | MineInteraction on P_HudDEF | `s_PutMineOnWall_InteractionMenu` |
| `GasMaskDriver` | MaskGas on P_HudDEF: frame, breathing, air gauge | the gas mask is selected |
| `HeartSensorDriver` | Radar/Heart and Radar/Heart1 on P_HudATT, and the marked mercs as radar blips; its heartbeat and scan ping play 2D on the spy's own machine (`PlayAudio.PlayHudSound`, wired in `PlayRoot`) | the heartbeat sensor is selected |
| binoculars | P_BinocularsATT / P_BinocularsDEF (`HudBinocularPages`, the HUD's own) | the binocular states |

`HudLayer` attaches them (`GadgetHudViews.Attach`). The player's SHud comes from the server's `ClientSetHUD` at login
(`SPlayerController.ClientSetHUD`, run by the play client).

## Tests

- [tests/OpenScct.Game.Tests/Equipment/](../tests/OpenScct.Game.Tests/Equipment): one file per gadget (sticky cam, chaff, heartbeat, mines, camera network and gas
  mask, binoculars and motion tracking), the gadget natives, the HUD rules (`GadgetHudTests`), selection and the quick
  choice on the full controller (`QuickChoiceTests`), and the host settings and HUD login (`HostSettingsTests`).
- `--svm-smoke=gadgets` ([game/Scripts/Smoke/GadgetsSmoke.cs](../game/Scripts/Smoke/GadgetsSmoke.cs)): SteeE offline, the spy then the merc walk to a wall
  and use every default gadget through the controller's input bytes and commands, checking the server, the client and
  the HUD view each time. Run it with `tools/ci/godot-smoke.ps1 -Tests gadgets`.
- `--svm-smoke=play-combat` ([game/Scripts/Smoke/PlayCombatSmoke.cs](../game/Scripts/Smoke/PlayCombatSmoke.cs)): the weapons and gadgets through the real keys,
  as a player presses them: Godot input events for the bound keys, read by `PlayInput`, over loopback to the listen
  server on its thread, with the launchers' settings and bots. Spy: night vision, draw, shoot, holster, sticky cam
  (fire, look, zoom, gas, leave), quick throw. Merc: fire (clip, HUD, flash, mark), tracer, rate of fire, reload,
  scope and zoom, frag. Then a merc kills a spy bot and a spy shocks a merc bot (inline host, the bot held in front).
  With a window, `SVM_PLAY_SHOTS=<folder under local/>` saves a picture of each step and of its first muzzle flash.
- `--svm-smoke=play-merc` ([game/Scripts/Smoke/PlayMercSmoke.cs](../game/Scripts/Smoke/PlayMercSmoke.cs)): the merc kit through the real keys with what the
  player sees checked on the client: laser beam (Q), torch light (E), rate of fire (F) with the burst and auto cadence
  and the recoil, reload time (R) with the HUD ammo, scope and zoom with a single shot inside, a frag throw (launch
  speed, bounces, fuse, explosion), the gas mask (air, HUD) and the camera network (enter, next camera, zoom, motion
  tracking, exit); an inline leg checks the laser mark (server and radar blip) and the server's shot spread.
- [tests/OpenScct.Game.Tests/Equipment/MercKitTests.cs](../tests/OpenScct.Game.Tests/Equipment/MercKitTests.cs): the decoded grenade launch and bounce, the merc's throw origin, the
  torch direction and the laser's end point and mark target. `MercLaserMarkTests.cs`: a still spy held in the beam is
  marked after 3 to 4 s; one tick off him clears the stamp and the count starts again (merc-laser.md rule 9), so a
  spy who keeps leaving the beam, even for a tick, is not marked.
- [tests/OpenScct.Game.Tests/Combat/PistolAimTests.cs](../tests/OpenScct.Game.Tests/Combat/PistolAimTests.cs): the armed spy's estimated pistol aim turns toward the camera's target
  but never back toward the camera (a pawn standing between the camera and the muzzle keeps the camera's direction).
- [tests/OpenScct.Game.Tests/Equipment/WeaponButtonsTests.cs](../tests/OpenScct.Game.Tests/Equipment/WeaponButtonsTests.cs) (R and F in weapon walking), `ImpactManagerTests.cs`,
  [tests/OpenScct.Game.Tests/Combat/MercAimTests.cs](../tests/OpenScct.Game.Tests/Combat/MercAimTests.cs) (the mouse moves the merc's aim), `ShockerAimTests.cs` (the Shocker
  shoots where the camera looks), `FireModeButtonTests.cs` (E: tap, hold, and no redraw after a holster).

## Not done yet

- Without skeletal data the pistol and rifle muzzles are estimated (`CombatNatives.EstimatedBoneCoords`): the merc's
  at his eyes along his aim, the spy's at his eyes turned toward what his camera looks at.

- The renderer draws one torch at a time: the local merc's, otherwise the nearest eligible torch. (Other players' laser beams follow the decoded NativeReplaceLaserDistant, and the quick
  throw waits for ThroStAlMix's NotifyQuickLaunch, as in the original. The rate of fire key cycles Auto and Burst
  only, as the original's SwitchROF does; Single exists only inside the scope, merc-rifle.md rule 16.)
- The original has no merc eye adaptation (spec/equipment/merc-eye-adaptation.md: measured flat in the sandbox).
- The mine menu's greyed entries follow the launcher's availability array (one capture would settle which ones).
- Map-placed devices that react to the gadgets (cameras, lasers, alarms locked by chaff) belong to the map work.
