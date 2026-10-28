# Gap analysis (2026-10-28)

What the original still has that OpenSCCT does not, after checking every unported unit of the coverage report
(`docs/progress/COVERAGE.md`) and every feature of `spec/reloaded/FEATURES.md` and `spec/enhanced/FEATURES.md`
against the code. The coverage tool only counts units carrying `[PortOf]`/`[Uc]`/`[UcNative]`, so many "unported"
units turned out to be done under other names; those are listed at the end, not as gaps.

Starting point (coverage run of 2026-10-28): 7,412 script units, 325 out of scope. Specced 56.7% and ported 50.1% of
in-scope bytecode; SteeE slice 91.4% specced, 85.0% ported. Of the ~340 KB of bytecode in no spec, 265 KB is the GUI
package, which is rebuilt in Godot by design (docs/MENUS.md), so it is judged page by page below, not unit by unit.

Status column: **open** (not started), **wave** (being ported in the 2026-10-28 overnight wave; see the commit log for
what landed), **later** (large, left for a planned piece of work).

## Gameplay

| Gap | What the original does | Status |
|---|---|---|
| Arm aim IK | `TurnHead`'s arm cases (ChangeLeftArmRot, ChangeRightArmRot, ...Grab, ...PipH2/PipH4, OrienteForVandamme, RemoveBTWArmControl) turn the upper arms toward the aim for corner fire, fire while grabbing, pipe-hang fire and split-jump fire | wave |
| Rain in game logic | 173 ERainVolumes in 12 maps exist only for drawing; nothing sets `CurrentRainVolume`/`bWet`. Blocks the Shocker's ×1.3 rain freeze, laser heat ×0.5, the cloak's rain block, HUD droplets, wet skins and head splashes | wave |
| Disconnect flag drop | a carrier who disconnects drops the flag/disk (`Destroyed` → `DiscardFlag`) | wave |
| Rifle interact and mine states | `SWeaponDefense` s_Interacting and s_DropMine* (clips, laser off/on); the controller already enters them | wave |
| Give gadget to a teammate | share menu Give (`AskServerGiveObjectToPawn`); receive side exists | wave |
| Best mine to remove | `GetBestMineIndexToRemove` picks which mine/sticky cam the menu removes | wave |
| Small state overrides | ladder-boost quick gadget, crouch in the scope, inert body drop attack, jump AltFire gate, `KilledBy` zone kills, `EmptyAlarmsList`, body shotgun partial reload, AnimNotifyNoise/RMColOn, visitor goggle reset, `ClientSetRotation`, `DistantClientReinit` | wave |
| Pad aim assist | `NativeFindTargetToSnap` never decoded; nothing sets `bSnappedToTarget` | wave (decode) |
| Merc squint | `NativeComputeSquinting` never decoded | wave (decode) |
| Merc elbow turn / quick 180 | global `UpdateRotationDefenseur` / `NativeRotationCameraDefenseur` | wave (decode, if time) |
| Free camera, hide HUD | Enhanced cinematic camera gated by `bAvlb_CinematicCamera`; ported (spec/hud/cinematic-camera.md); pad controls not wired | done |

## Effects and looks

| Gap | What the original does | Status |
|---|---|---|
| Merc gas mask | `SGasMask` on bone B Head for every merc with the gadget; ours is spawned unattached and never drawn | wave |
| Wet skins, head rain splashes | `BeginWetSkin` (HEAT_*_wet), `SFlicFlocEmitter` | wave |
| Binocular HUD sounds | s_H_Binoculars_Def/_Att loops and the 5 s detect blip | wave |
| HUD shutdown sounds, throw charge tick | Snd_HudShutdown, msg 8, Hud_viseProgress | wave |
| Shotgun barrel smoke, shotgun spray velocity | CreateSmoke1 (two SSmokeCanonEmitters); SShotGunEmitter adds the shooter's velocity | wave (velocity needs CombatFxView.cs) |
| Footprints | tint combiner, StepColor, per-surface texture, mirrored right foot | wave |
| Sprinkler splash height, water-pipe puddle | floor trace; SFlaque grow and shrink | wave |
| Damaged gameplay object particles | `SSpawnableEmitter` with the map's own SpawnParticle (80 entries, 25 maps) | wave |
| Soft bodies | explosion push and untraced bullet hits | wave |
| Blood splash overlay, blood drop trail | `SHud.DrawBloodSplash` (native), `SBloodSplat` | wave (decode) |
| Merc target name | `SHud.DrawTargetName` (native) | wave (decode) |
| Local spy shadow | `SetupOldSchoolShadow` ShadowProjector | open |

## Voice

Who-hears-whom matches the original (rules in `VoiceRouting.cs`). Gaps:

| Gap | Status |
|---|---|
| Client is never told the voice mode (Team mode still shows Hack/Speak prompts and plays the hacking sound) | wave |
| Voice bans can't be set | wave |
| Hack Comm stops on any grab, including ledges, in the original; ours only in s_GrabbingPlayer | wave |
| Symmetric mute lists (Xbox Live) | by design: ours are one-way per listener |

## Reloaded and Enhanced features

| Gap | Status |
|---|---|
| Rumble (settings stored, no motor calls) | wave |
| Per-view sensitivity multipliers stored but unused; clutch | wave |
| Sniper kick tied to mouse sensitivity (Reloaded fix) | wave |
| Invert X, feet/meters, Objective Sounds, wheel mode (profile options; profile is null) | wave |
| classic_crosshair, hack_alpha, Shocker snap option, location name on the HUD | wave |
| Anisotropic level, shadow quality, low-latency mode, window position | wave |
| Crash log, process priority/affinity | wave |
| Flashlight in the scope, ledge assist (Reloaded natives) | wave (decode) |
| `!resign`, jitter in `!pings` | wave |
| Lag compensation (rl:net.lagcomp) | later (L) |
| Client fire prediction (rl:net.fireprediction) | later (M) |
| Practice server mode, `!heal` `!regen` `!tank` `!kill` | wave (done; extra ammunition not ported, `!regen`/`!tank` effects are our choice: docs/ONLINE.md "Practice servers") |
| Client console, `set`/`get` | wave (done: docs/HUD.md "Developer console") |
| MSAA/SMAA | later (M) |
| Extraction, Sabotage, Neutralization modes | later (L) |
| Unverified Reloaded fixes: grab angle, jump lock-on 7.5 m, KO after interrupt, slow strafe, disk radar threshold, crosshair shift, sticky cam fixes | open: need decoding of the Reloaded patch values |
| Signed releases, MMCSS voice thread, lobby music, join sound, local match log, scoreboard split size | open (small or optional) |

## Front end

| Gap | Status |
|---|---|
| Alternate Mode (host gadget and ability restrictions); game side exists, no UI or config | wave |
| Auto balance toggle | wave |
| Lobby Ban button | wave |
| Session player names in the server browser | wave |
| Briefing page | wave |
| Strategic Map (in-game menu, F7 and lobby; spec/hud/strategic-map.md) | done (needs the maps and ui steps rebuilt for the pictures) |
| Tutorials hub, gadget encyclopedia, Visit a Map with help paths, first-time-map prompt | wave |
| HUD legend and keyboard pictures pages | wave |
| Tutorial zone hints (SZoneTips) offline: standalone only in the original, and offline play is a listen server here | open (M) |
| Video: post quality, brightness pattern, keep-settings countdown | open (S) |
| Main menu quit confirmation | open (S) |
| Per-player loading list | open (S) |
| Lobby Equipment button | open (S) |

## Out of scope or done under other names

- Out of scope: Xbox Live, Ubi.com, PS2, PunkBuster, memory cards, commandlets, the Reloaded client challenge,
  `SLethalWeapon` (no references), `SImpactEffect*` subclass code (the impact manager never runs it), `SFlashGen`,
  `SAlarmZoneInfo`, Fragment/Decoration, triggered materials, ZoneTrigger/LineOfSightTrigger (none placed),
  `SProjPhospho` (in no inventory), GameStats/SLogManager/Security/LANErrorHandler, Magma/GameService slot code,
  frame-rate bug fixes (fixed 60 Hz sim).
- Done without port attributes: voice routing, Hack Comm, downed chat, grab talk, voice trace, team-voice lobby option,
  proxy relay and reinit, terminate events, LAN discovery, map rotation, admin commands, kick/ban, chat, scoreboard,
  loadout sync (gear change), the muzzle flashes, grenade trails, projectors, F8 game status, Create Session, results.
