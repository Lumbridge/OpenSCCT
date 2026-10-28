# Gadgets

Every default gadget of both sides, from the profile to the HUD. The rules are in `spec/equipment/` (one file per
gadget, `INDEX.md` lists the units); this page says where the code is, how a gadget call travels and what is still
missing.

## Kits

`DefaultProfile.ini` (`spec/equipment/loadouts.md`), in slot order:

| Side | Slot 0 | Slot 1 | Slot 2 | Slot 3 | Weapon | Always |
|---|---|---|---|---|---|---|
| Spy | smoke grenade (5) | chaff grenade (5) | sticky cam (5) | heartbeat sensor | Shocker | binoculars |
| Merc | frag grenade (4) | mine launcher (3) | camera network | gas mask | rifle | torch, laser, goggles |

The gadgets marked "not in the default kit" below are picked from Change Gear (`game/Scripts/UI/GearMenu.cs`,
spec/additions/gear-change.md: the team's `ClassInventory`, e.g. the merc taser is merc option 6), and the host allows
them all by default (`USPersistentData` gadget restriction 255). The spy cloak (`SGadCloak`) is ported the same way. The spy's client asks
the server (`SetDesiredGlassValue`), which broadcasts the ramp to every client; each machine ramps GlassEffect in the
pawn tick and draws it as glass in normal vision (docs/RENDERING.md#cloak). Offline (standalone) games raise every
charge to 999.

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

Refills: the ammo crate refills the rifle (and a Sabotage bomb), never gadgets (`spec/equipment/ammo-stock.md`). A spy
who picks up another spy's sticky cam gets one charge back (`ReloadStickyCam`); removing a mine from the mine menu
gives its charge back; the team backpack shares one charge of a gadget.

## The gadgets

| Gadget | Rules in short | Code | Spec |
|---|---|---|---|
| Smoke grenade | 3 s fuse, cloud; a merc in it 6 s is knocked down; a worn gas mask keeps it out | `SProjSmoke`, `SSmokeEmitter` | smoke-grenade.md |
| Chaff grenade | 3 s fuse, then a 10 s field of 300 uu: mercs in sight get a 2 s HUD jam (no reload, scope or goggles) and their goggles reset, torches malfunction 5 s, mines and gameplay objects lock | `SProjChaffe` | chaff-grenade.md |
| Sticky cam | needs the Shocker drawn; sticks to world geometry without `bNoStickyCam` within range; the view (FOV 110 to 10, zoom keys and wheel), one gas dose (128 uu cloud 192 uu ahead, 5 s reuse), Alt-fire leaves; previous cam menu; shot or picked up | `SGadStickyCam`, `SStickyCam`, `SStickyCamEmitter`, `SPlayerController.s_StickyCam.cs`, `SPlayerController.StickyCam.cs` | sticky-cam.md |
| Alarm snare (not in the default kit) | needs the Shocker drawn; shot onto world geometry or a stickable gameplay object (laser projectors passed through), 60 s; fake footsteps and breaths with merc sound arrows on every machine; on the server it sets off the alarm of each laser, camera or volumetric sensor within 200 uu in sight (again after each alarm, for 30 s); any damage destroys it; "on" and "off" HUD texts | `SGadNoiseEmitter`, `SNoiseEmitter` | alarm-snare.md |
| Spy bullet (not in the default kit) | needs the Shocker drawn; an instant shot from the hand up to 15000 uu (laser projectors passed through, a mover stops it): a living pawn hit is marked for the spies' radar and hack comm (a merc also tells the spies), anything else gets a zone marker that marks every merc within 500 uu for its 45 s; the charge is taken on the server; "zone radar activated" text | `SSpyBulletLauncher`, `SProjMarqueurSpy` | spy-bullet.md |
| Heartbeat sensor | selected and Shocker drawn: every pawn within 2200 uu in a 60 degree cone of the view is marked for the radar; no wall or team test | `SGadHeartBeatSensor` | heartbeat-sensor.md |
| Mine launcher | Alt-fire at a wall within 55 uu: a tap places the selected type, a hold opens the menu (proximity, laser, poison, remove oldest, exit); 3 on the map per merc; proximity checks every 0.5 s, laser every 0.05 s across to the opposite wall (up to 2005 uu); chaff switches them off; any damage blows them | `SGadMultiMinesLauncher`, `SMines`, `SMineProx`, `SMineLaser`, `SMinePoison`, `SMinePiegeSpy`, `SPlayerController.s_PutMineOnWall.cs` | mine-launcher.md |
| Presence detector (not in the default kit) | second item of the spy trap launcher, placed like a mine (trap limit); on the server it scans every 1 s with a scan sound (radius 1000): a moving spy within 500 uu in sight is detected; a new one puts the detector on every HUD (the mercs' radar alarm), sends "Intruder detected" to the mercs and "detected" to the spy; the warning ends 9 s after the last sighting; chaff stops the scans; it cannot be shot while warning (1 hit point) | `SPresDetector`, `SPlayerController.AddGPOOnHud`, `SHud.AddMalfunctionGPO` | presence-detector.md |
| Backpack (not in the default kit) | holds a share of the merc's gadget charges (1 or 2 frags, mines and spy traps, 3 or 6 flares, else two thirds; less in a team of 3 or more), Alt-fire refills them ("Gadget refill done"); the server picks the first two other mercs and fills their shares on his pawn, which they take with the "Refill" Use entry on him; the backpack mesh rides his 'B Spine2' bone | `SGadBackPack`, `SBackPack`, `SBackPackInteraction`, `SPlayerController.UpdateMyInventoryChargeBaseOnMateBackpack` (view: `EffectViews` bone-attached mesh) | backpack.md |
| Flare launcher (not in the default kit) | thrown as a grenade (10 charges, 4 s refire); no bounce: it sticks at the first wall it hits (a pawn it touches only stops it falling sideways) and the server leaves a burning SFlareEmitter there for 20 s, riding what it hit: a steady dynamic light (radius 25) flickering at 100 to 115 for 15 s then fading, the burn loop and sparks; each machine leaves a 30 s burn mark; a burning trail with a small light in flight; fuse 10 s; light only, no effect on vision modes or the HUD (an SBallTrap at its default class throws flares too) | `SGadFlareLauncher`, `SProjFlare`, `SFlareEmitter`, `SFlareCourseEmitter`, `SFlareBurnProjector`, `Projector.AbandonProjector` (views: `EffectViews` light of a dynamic-light emitter, `CombatFxView.Decals.cs`) | flare.md |
| Taser (not in the default kit; Change Gear; counts `Merc_TaserHits` for the Live Wire and High Voltage achievements) | rifle drawn, 1 s refire, no charges: the server shocks every living pawn (no team test) not already shocking or attacked in a cone ahead of the rifle's FX bone (up to 200 uu, about 60 degrees close, 20 at full range) with a clear 15 uu line: the taser pill (51 damage, freeze, knock-down when walking); the merc turns toward the last one; a 0.3 s beam to each pawn hit, or a short one ahead on a miss, drawn on every machine from its own copy of the gun | `SGadTazerLauncher`, `STazerEmitter` (view: `EffectViews` mesh) | tazer.md |
| Camera network | walking merc only: the view through the first network camera; Use next, back-to-wall previous; the camera keeps its own vision mode; chaff throws the merc out; camera energy refills 5 a second to 100 (a shot needs 50, nothing in this build fires) | `SGadCamNetwork`, `SCamNetwork.Gadget.cs`, `SPlayerController.s_CamNetwork.cs` | camera-network-gadget.md |
| Gas mask | Alt-fire on and off; 30 s of air, choking below 7.5 s, off when empty or another gadget is selected; smoke and sticky gas do not reach the wearer | `SGadGasMask`, `PawnGadgets.SetGasMaskUseOnServer` | gas-mask.md |
| Binoculars (spy) | B: 30, 20 and 10 degree zoom, pitch clamp, Use marks the objective under the reticle at most once a second, EMF-visible while up | `SpyBinoculars`, `SPlayerController.s_SpyBinoculars.cs` | spy-binoculars.md |
| Motion tracking (merc goggles) | scans twice a second: movers above 150 uu/s within 2600 uu in sight (not cloaked spies, not the merc) are locked; a lock holds 1.5 s after the target stops | `SMotionTrackingVision` | motion-tracking.md |
| EMF (merc goggles) | electronics and EMF-visible spies glow | `EmfVision`, `PawnVision` | vision-goggles.md |

Shared helpers: `GadgetWorld` (sight lines with flags 0x286, the cylinder test of the laser beam), `GadgetBlast`
(mine and frag damage fall-off behind walls), `GadgetHud` (the SHud message stacks and view states), `GadgetFx` (the
view events: cam stuck, gas, chaff burst, mine placed or gone, motion lock, binocular marks).

## Weapons from the keys

What each side fires in SCCT Versus Enhanced 4.0 (DefaultProfile.ini `ATTWeapClass` / `DEFWeapClass`,
`spec/equipment/loadouts.md`): the spy carries only the Shocker (`SStickyShocker`), the merc only the rifle
(`SWeaponDefense`, "FN2000 Auto"). The Shocker deals no health damage: it costs 15 of the spy's energy per shot (6 shots
from a full bar, 7 back each second), freezes the pawn it hits (sticky shock), and disables devices
(`spec/equipment/spy-shocker.md`). The rifle has a 25-round clip and 100 in reserve, three rates of fire and
distance-banded damage (`spec/equipment/merc-rifle.md`). The same keys on both layouts (Default_kbd_0.ini / _1.ini):

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

- Impacts go through the level's impact manager (`OpenScct.Game.Equipment.ImpactManager`, `spec/equipment/impact-manager.md`):
  the server's rifle and Shocker traces add a mark, which every player's machine keeps in its `ImpactTable` (50 marks,
  15 s) with its spark and sound.
- `game/Scripts/Fx/CombatFxView.cs` draws, on the client: the muzzle flashes (the Shocker's SMuzzleSticky from its
  replicated shot count, the third-person rifle's ClientFireMuzzle, the local merc's own shots from his clip), the
  tracers (`SFireTrace` actors flying at 7000 uu/s to their impact), the marks and the spark bursts. The effect
  packages (SEffectStatic, sfx) are not converted yet, so the flashes, streaks, marks and sparks are simple stand-ins
  with the original's timing, colour and place.

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

`game/Scripts/Hud/Gadgets/` (rules in `spec/equipment/gadget-hud.md`; the Godot-free answers are
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

- `tests/OpenScct.Game.Tests/Equipment/`: one file per gadget (sticky cam, chaff, heartbeat, mines, camera network and gas
  mask, binoculars and motion tracking), the gadget natives, the HUD rules (`GadgetHudTests`), selection and the quick
  choice on the full controller (`QuickChoiceTests`), and the host settings and HUD login (`HostSettingsTests`).
- `--svm-smoke=gadgets` (`game/Scripts/Smoke/GadgetsSmoke.cs`): SteeE offline, the spy then the merc walk to a wall
  and use every default gadget through the controller's input bytes and commands, checking the server, the client and
  the HUD view each time. Run it with `tools/ci/godot-smoke.ps1 -Tests gadgets`.
- `--svm-smoke=play-combat` (`game/Scripts/Smoke/PlayCombatSmoke.cs`): the weapons and gadgets through the real keys,
  as a player presses them: Godot input events for the bound keys, read by `PlayInput`, over loopback to the listen
  server on its thread, with the launchers' settings and bots. Spy: night vision, draw, shoot, holster, sticky cam
  (fire, look, zoom, gas, leave), quick throw. Merc: fire (clip, HUD, flash, mark), tracer, rate of fire, reload,
  scope and zoom, frag. Then a merc kills a spy bot and a spy shocks a merc bot (inline host, the bot held in front).
  With a window, `SVM_PLAY_SHOTS=<folder under local/>` saves a picture of each step and of its first muzzle flash.
- `--svm-smoke=play-merc` (`game/Scripts/Smoke/PlayMercSmoke.cs`): the merc kit through the real keys with what the
  player sees checked on the client: laser beam (Q), torch light (E), rate of fire (F) with the burst and auto cadence
  and the recoil, reload time (R) with the HUD ammo, scope and zoom with a single shot inside, a frag throw (launch
  speed, bounces, fuse, explosion), the gas mask (air, HUD) and the camera network (enter, next camera, zoom, motion
  tracking, exit); an inline leg checks the laser mark (server and radar blip) and the server's shot spread.
- `tests/OpenScct.Game.Tests/Equipment/MercKitTests.cs`: the decoded grenade launch and bounce, the merc's throw origin, the
  torch direction and the laser's end point and mark target. `MercLaserMarkTests.cs`: a still spy held in the beam is
  marked after 3 to 4 s; one tick off him clears the stamp and the count starts again (merc-laser.md rule 9), so a
  spy who keeps leaving the beam, even for a tick, is not marked.
- `tests/OpenScct.Game.Tests/Combat/PistolAimTests.cs`: the armed spy's estimated pistol aim turns toward the camera's target
  but never back toward the camera (a pawn standing between the camera and the muzzle keeps the camera's direction).
- `tests/OpenScct.Game.Tests/Equipment/WeaponButtonsTests.cs` (R and F in weapon walking), `ImpactManagerTests.cs`,
  `tests/OpenScct.Game.Tests/Combat/MercAimTests.cs` (the mouse moves the merc's aim), `ShockerAimTests.cs` (the Shocker
  shoots where the camera looks), `FireModeButtonTests.cs` (E: tap, hold, and no redraw after a holster).

## Not done yet

- The effect meshes and particles (muzzle flash meshes, `balle_TR`, decal textures, SSpark* emitters) wait for the
  content pipeline; the stand-ins in `CombatFxView` should then be replaced by the converted assets.
- Without skeletal data the pistol and rifle muzzles are estimated (`CombatNatives.EstimatedBoneCoords`): the merc's
  at his eyes along his aim, the spy's at his eyes turned toward what his camera looks at.

- The quick throw fires at once; the original throws at the clip's notify (0.4 s into ThroStAlMix).
- Other players' laser beams (`NativeReplaceLaserDistant`) are drawn from the hand bone along the packed aim until
  that native is read; the local merc's beam and the torch follow the decoded natives
  (`game/Scripts/Render/MercLaserView.cs`, `FlashLightView.cs`). The torch uses the Faithful template's one spot slot,
  so only one torch lights the scene at a time (the local merc's, else the nearest).
- The rate of fire key cycles Auto and Burst only; Single exists only inside the scope (merc-rifle.md rule 16).
- The original has no merc eye adaptation (spec/equipment/merc-eye-adaptation.md: measured flat in the sandbox).
- The mine menu's greyed entries follow the launcher's availability array (one capture would settle which ones).
- Map-placed devices that react to the gadgets (cameras, lasers, alarms locked by chaff) belong to the map work.
