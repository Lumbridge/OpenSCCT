# Gap analysis (2026-10-28)

What the original still has that OpenSCCT does not, after checking every unported unit of the coverage report
(`docs/progress/COVERAGE.md`) and every feature of `spec/reloaded/FEATURES.md` and `spec/enhanced/FEATURES.md`
against the code. The coverage tool only counts units carrying `[PortOf]`/`[Uc]`/`[UcNative]`, so many "unported"
units turned out to be done under other names; those are listed at the end, not as gaps.

Starting point (coverage run of 2026-10-28): 7,412 script units, 325 out of scope. Specced 56.7% and ported 50.1% of
in-scope bytecode; SteeE slice 91.4% specced, 85.0% ported. Of the ~340 KB of bytecode in no spec, 265 KB is the GUI
package, which is rebuilt in Godot by design (docs/MENUS.md), so it is judged page by page below, not unit by unit.

Status column: **done** (ported in the 2026-10-28 overnight wave, not yet played: every row needs a hand check),
**partial** (what is left is named), **n-a** (decoded and found to be dead code in the original), **open** (not
started), **later** (large, left for a planned piece of work).

Content rebuild needed before some rows show in game: the `maps` step (rain `sim/rain.json` for the 12 rain maps, the
damaged-object particles of MissD/E/P and PrisD, the strategic map pictures; rain changes those maps' sim hash, so the
server's content must be rebuilt too) and the `ui` step (tutorial pictures, strategic map markers).

## Gameplay

| Gap | What the original does | Status |
|---|---|---|
| Arm aim IK | `TurnHead`'s arm cases (ChangeLeftArmRot, ChangeRightArmRot, ...Grab, ...PipH2/PipH4, OrienteForVandamme, RemoveBTWArmControl) turn the upper arms toward the aim for corner fire, fire while grabbing, pipe-hang fire and split-jump fire | done (SetBoneDirection's world mode estimated: if an arm points the wrong way, start there) |
| Rain in game logic | 173 ERainVolumes in 12 maps exist only for drawing; nothing sets `CurrentRainVolume`/`bWet`. Blocks the Shocker's ×1.3 rain freeze, laser heat ×0.5, the cloak's rain block, HUD droplets, wet skins and head splashes | done (point-in-box test is ours; needs the maps rebuild for servers without render files) |
| Disconnect flag drop | a carrier who disconnects drops the flag/disk (`Destroyed` → `DiscardFlag`) | done |
| Rifle interact and mine states | `SWeaponDefense` s_Interacting and s_DropMine* (clips, laser off/on); the controller already enters them | done |
| Give gadget to a teammate | share menu Give (`AskServerGiveObjectToPawn`); receive side exists | done (the original's empty "gave" word kept) |
| Best mine to remove | `GetBestMineIndexToRemove` picks which mine/sticky cam the menu removes | done (decoded: furthest along the view axis) |
| Small state overrides | ladder-boost quick gadget, crouch in the scope, inert body drop attack, jump AltFire gate, `KilledBy` zone kills, `EmptyAlarmsList`, body shotgun partial reload, AnimNotifyNoise/RMColOn, visitor goggle reset, `ClientSetRotation`, `DistantClientReinit` | done, except `EmptyAlarmsList` per spectator HUD frame and `ServerVerifyViewTarget`; `KilledBy` has no caller yet |
| Pad aim assist | `NativeFindTargetToSnap` never decoded; nothing sets `bSnappedToTarget` | done (decoded; spine height estimated) |
| Merc squint | `NativeComputeSquinting` never decoded | done, sim only (no visible effect in the original either) |
| Merc elbow turn / quick 180 | global `UpdateRotationDefenseur` / `NativeRotationCameraDefenseur` | n-a (never runs on PC) |
| Free camera, hide HUD | Enhanced cinematic camera gated by `bAvlb_CinematicCamera`; ported (spec/hud/cinematic-camera.md); pad controls not wired | done |

## Effects and looks

| Gap | What the original does | Status |
|---|---|---|
| Merc gas mask | `SGasMask` on bone B Head for every merc with the gadget; ours is spawned unattached and never drawn | done |
| Wet skins, head rain splashes | `BeginWetSkin` (HEAT_*_wet), `SFlicFlocEmitter` | done for thermal/EMF and splashes; normal-vision wet materials not converted |
| Binocular HUD sounds | s_H_Binoculars_Def/_Att loops and the 5 s detect blip | done |
| HUD shutdown sounds, throw charge tick | Snd_HudShutdown, msg 8, Hud_viseProgress | done |
| Shotgun barrel smoke, shotgun spray velocity | CreateSmoke1 (two SSmokeCanonEmitters); SShotGunEmitter adds the shooter's velocity | smoke done (also the rifle and Uzi burst smoke); spray velocity open (needs CombatFxView.cs) |
| Footprints | tint combiner, StepColor, per-surface texture, mirrored right foot | done (texture orientation estimated; existing decals are no longer world-aligned) |
| Sprinkler splash height, water-pipe puddle | floor trace; SFlaque grow and shrink | done (sprinkler puddles now drawn too) |
| Damaged gameplay object particles | `SSpawnableEmitter` with the map's own SpawnParticle (80 entries, 25 maps) | done, needs the maps rebuild (the data is on 4 maps, not 25) |
| Soft bodies | explosion push and untraced bullet hits | n-a for explosions (SBExplosionHit has no caller); untraced bullet hits open (soft bodies are not in the sim) |
| Blood splash overlay, blood drop trail | `SHud.DrawBloodSplash` (native), `SBloodSplat` | overlay n-a (nothing sets bSplashBloodStart); blood drop trail open (spawner not found) |
| Merc target name | `SHud.DrawTargetName` (native) | done (decoded) |
| Local spy shadow | `SetupOldSchoolShadow` ShadowProjector | open |

## Voice

Who-hears-whom matches the original (rules in `VoiceRouting.cs`). Gaps:

| Gap | Status |
|---|---|
| Client is never told the voice mode (Team mode still shows Hack/Speak prompts and plays the hacking sound) | done |
| Voice bans can't be set | done (console, chat and lobby) |
| Hack Comm stops on any grab, including ledges, in the original; ours only in s_GrabbingPlayer | done |
| Symmetric mute lists (Xbox Live) | by design: ours are one-way per listener |

## Reloaded and Enhanced features

| Gap | Status |
|---|---|
| Rumble (settings stored, no motor calls) | done (trigger motors not driven: Godot has no call for them) |
| Per-view sensitivity multipliers stored but unused; clutch | done (scope now 0.75 sideways by Reloaded default; `vertical` meaning estimated) |
| Sniper kick tied to mouse sensitivity (Reloaded fix) | done |
| Invert X, feet/meters, Objective Sounds, wheel mode (profile options; profile is null) | done (wheel defaults to Gadget, as before) |
| classic_crosshair, hack_alpha, Shocker snap option, location name on the HUD | hack_alpha and location done; classic_crosshair open (offset not decoded); Shocker snap open (the snap itself isn't in the game) |
| Anisotropic level, shadow quality, low-latency mode, window position | done (low latency: Ultra = On, Godot's floor) |
| Crash log, process priority/affinity | done (client only; Godot-callback exceptions not caught) |
| Flashlight in the scope, ledge assist (Reloaded natives) | done (also Reloaded's slow-strafe diagonal fix, found in the same function); two ledge patches need a reference-game check (spec/movement/ledge.md) |
| `!resign`, jitter in `!pings` | done |
| Lag compensation (rl:net.lagcomp) | done (off by default: `lagcomp on`, `!lagcomp on` or config `lagComp`; docs/NETCODE.md "Lag compensation"; Reloaded's default ceiling not decoded, ours is 250 ms) |
| Client fire prediction (rl:net.fireprediction) | done (for the merc rifles/SMG and the spy pistol's world impacts; option `fire_prediction`, on; pawn hits stay server-confirmed) |
| Practice server mode, `!heal` `!regen` `!tank` `!kill` | done (extra ammunition not ported, `!regen`/`!tank` effects are our choice: docs/ONLINE.md "Practice servers") |
| Client console, `set`/`get` | done (docs/HUD.md "Developer console") |
| MSAA/SMAA (rl:gfx.aa) | done: Display options msaa, msaa_samples, smaa (first run all off, 4 samples), applied at once; alpha to coverage on masked surfaces from the next map; with MSAA the post passes run on a copy of the resolved frame (docs/RENDERING.md "Anti-aliasing"). Not yet seen running with MSAA on |
| Map BeamEmitter / SparkEmitter, mirrors (RENDERING.md open issues) | n-a: sparks were already drawn (rule 22) and beams as straight strips; no map places a beam, the one map spark emitter (PanaD/PanaS Emitter241) waits for its event; every PF_Mirrored BSP surface (MuseD/E/T, OffsD, ScheU, SteeD/E/P/T, TermD) is also PF_Invisible, and UE2 leaves invisible surfaces out of drawing, so no map shows a mirror (the DTK_mirroir "mirrors" of ClarD and CpusS are plain textured meshes, already drawn) |
| Extraction, Sabotage, Neutralization modes | later (L) |
| Unverified Reloaded fixes: grab angle, jump lock-on 7.5 m, KO after interrupt, slow strafe, disk radar threshold, crosshair shift, sticky cam fixes | open: need decoding of the Reloaded patch values |
| Signed releases, MMCSS voice thread, lobby music, join sound, local match log, scoreboard split size | open (small or optional) |

## Front end

| Gap | Status |
|---|---|
| Alternate Mode (host gadget and ability restrictions); game side exists, no UI or config | done (online Deathmatch without Alternate now uses the original's fixed one-gadget rule) |
| Auto balance toggle | done (off by default; the original's hidden row was always on) |
| Lobby Ban button | done |
| Session player names in the server browser | done (internet sides need the master redeployed) |
| Briefing page | done (page 1 uses the loading picture; briefing picture not converted) |
| Strategic Map (in-game menu, F7 and lobby; spec/hud/strategic-map.md) | done (needs the maps and ui steps rebuilt for the pictures) |
| Tutorials hub, gadget encyclopedia, Visit a Map with help paths, first-time-map prompt | done, needs the ui rebuild (TutoOrExam gate skipped) |
| HUD legend and keyboard pictures pages | done |
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
