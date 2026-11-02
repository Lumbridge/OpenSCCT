# Enhanced SCCT Versus: feature inventory for the rewrite

Status: research spec, M0. Scope: what the Enhanced SCCT Versus patch (by Joshhhuaaa, GitHub
Joshhhuaaa/EnhancedSCCTVersus) changes in the game, and how each change reaches the rewrite.
Companion: `spec/reloaded/FEATURES.md` (the Reloaded patch, bundled with Enhanced). Not to be confused
with the plan's "Enhanced" render profile (PBR, GI), which is our own feature.

## 1. Sources

| Tag | Source |
|---|---|
| ENHR | Enhanced README (installation, playing online, server commands, hosting, regional relays) |
| ENHP | Enhanced `PatchNotes.md` (cumulative notes) |
| ENH30..ENH36 | GitHub release notes v3.0 (2024-01-30), v3.0a, v3.1, v3.2, v3.2a, v3.2b, v3.3, v3.4 (2024-11-14), v3.5 (2025-03-30), v3.5a, v3.6 (2025-11-06) |
| HOST | Hosting guide gist linked from the README |
| INT40 | Enhanced v4.0 Public Beta 3 install, `System\_PC_\GUI.int` and `SBase.int`, compared with the `.eng` files of the 3.6 install at `Desktop\SVM\SVM 3.6` (assumed to be Enhanced 3.6) |
| INST | Reference install `Desktop\Enhanced SCCT Versus v4.0 Public Beta 3` (83 map files, read-only) |
| CFG | Ryan's `SCCT_Versus.config` (Reloaded v4.0d): playlists and the map name cache |
| PCGW | PCGamingWiki page for Chaos Theory |
| SCP | sc-panel.com |
| MEM | Memory notes objective-player-counts, scct-interface-package, deploy-target |
| RL | Reloaded sources as listed in the companion document |

Enhanced v4.0 has no public notes yet (latest GitHub release is v3.6); v4.0 items come from INT40 and the
Reloaded v4.0d binary and are marked "v4.0".

## 2. How Enhanced reaches the rewrite

- Enhanced is mostly content and UnrealScript: maps (`.sdc`), map settings packages (`-i.utc`), the script
  packages in `System\_PC_` (SBase, GUI, Engine), localisation (`.int`), Magma menus, sounds and music. It
  also bundles Reloaded (`Reloaded.Core.dll`), Creative ALchemy (`dsound.dll`) and D3D compiler runtimes.
- The plan already converts content from the reference install and decompiles its `_PC_/*.u`. Because that
  install is Enhanced 4.0, Enhanced script changes arrive through the SBase port and the content pipeline
  without extra work, provided we never mix in stock packages.
- Behavioural target: Enhanced 4.0 + Reloaded 4.0d. Stock-only behaviour is kept only where Enhanced or
  Reloaded expose it as an option (original crosshair, legacy sniper vertical speed).
- Some Enhanced notes describe fixes contributed through Reloaded (credited to AllyPal). Those are specced in
  the Reloaded document and cross-referenced here. Items below say "script" (Enhanced packages), "content"
  (maps, sounds) or "via Reloaded".
- Enhanced versions are not compatible with each other or with stock (ENHR). Our handshake must keep the
  same strictness (protocol hash plus content hash).
- IDs: `enh:<group>.<name>`. The coverage tool needs an `enh:` prefix next to `rl:`; until then these map to
  the `uc:` units of the Enhanced packages and to content validation.
- Priority: **P0** = needed for the SteeE Story 2v2 slice, **P1** = parity, **P2** = later.
- Each entry describes a change from stock. Without a Stock line, the stock game simply lacks the change
  (for example, sounds that Enhanced silences are audible in stock). There are no Enhanced config keys:
  Enhanced options live in the Reloaded settings (companion document) or in the match settings.

## 3. Gameplay rules and balance (script)

### enh:rules.silent - Silent stealth movement
- **Players get:** no sound for crouched or sneaking footsteps, soft landings on standard surfaces, ledge
  climb and shimmy, pole and ladder climbing, back to wall, using a medkit, drawing the Shocker without
  its laser, switching goggle vision modes, activating the Camo Suit. Loading tips tell players they are
  silent when crouching, sneaking and climbing.
- **Evidence:** ENHP Enhancements; INT40 `aTipStrings`.
- **Stock:** these actions make sounds that the enemy hears and that can show on the merc's sound
  indicator.
- **Rewrite:** comes with the Enhanced SBase port and sound packages. Keep sound playback and gameplay
  noise events as separate SimEvents so each can be checked in difftests.
- **Priority:** P0 (stealth on SteeE).
- **Open:** whether Enhanced removed only the audible sound or also the noise event that drives the merc
  reticle's sound indicator. Check the decompiled `MakeNoise`/`PlaySound` call sites.

### enh:rules.damage - Symmetric hit damage
- **Players get:** the same damage whichever side of the body is hit.
- **Evidence:** ENH30, ENHP.
- **Stock:** left and right hit zones gave different damage.
- **Rewrite:** decoded hit zones (plan M4) taken from the Enhanced packages; add a test that mirrored hits
  deal equal damage.
- **Priority:** P0 (merc rifle in the slice).

### enh:rules.adrenaline - Adrenaline restored, without client stutter
- **Players get:** spies move faster for a short time after taking damage (restored in v3.4); no stutter
  for client spies (via Reloaded, see `rl:net.clientsync`).
- **Evidence:** ENH34, ENHP.
- **Rewrite:** port the speed boost; carry the speed state in saved moves.
- **Priority:** P0.

### enh:rules.berserk - No accidental Berserk
- **Players get:** a merc moving forward no longer triggers a Berserk by accident.
- **Evidence:** ENH32, ENHP.
- **Rewrite:** script port; scenario with forward movement plus Use near a spy.
- **Priority:** P0 (close combat is in the slice).

### enh:rules.crosshair - Centred merc crosshair
- **Players get:** the merc crosshair centred on screen by default (v3.0a moved it closer, later fully
  centred); an option restores the original off-centre position (via Reloaded `classic_crosshair`).
- **Evidence:** ENH30 (v3.0a), ENHP, ENH36.
- **Rewrite:** HUD default centred; the aim trace must match the centred crosshair.
- **Priority:** P0.
- **Open:** whether the shot direction moved with the crosshair or only the HUD mark (difftest a shot at a
  wall marker).

### enh:rules.flashlight_sniper - Flashlight stays on in Sniper Mode
- **Players get:** entering Sniper Mode no longer turns off the merc flashlight, as on Xbox.
- **Evidence:** ENH35, ENHP; also listed as Enhanced-only in RL20.
- **Rewrite:** script port.
- **Priority:** P0.

### enh:rules.motion - Motion Vision corrections
- **Players get:** Motion Vision no longer detects players behind solid objects seen through glass (v3.5)
  or through surfaces that bullets can pass (v3.5a); see also `rl:fix.motion_glass`.
- **Evidence:** ENH35, ENH35a, ENHP.
- **Rewrite:** the vision pass and the detection logic must test occlusion against vision-blocking
  geometry, not bullet-blocking geometry. Needs material flags from the content pipeline.
- **Priority:** P0 (motion vision is in the slice).

### enh:rules.emf - No EMF ghosting
- **Players get:** spies not using equipment no longer show faintly in EMF vision.
- **Evidence:** ENHP, INT40 tips.
- **Priority:** P1.

### enh:rules.camo - Camo Suit changes
- **Players get:** Camo is not detected by security cameras or Motion Vision, not drawn in Motion Vision,
  not highlighted in red vision when moving; no uncloak during ledge climbs at high fps; never drawn
  uncloaked on clients.
- **Evidence:** ENH35, ENHP, RL20 Enhanced-only section.
- **Priority:** P1 (Camo is not a slice gadget).

### enh:rules.proxymine - Proxy mine trigger speed from Xbox
- **Players get:** proxy mines use the Xbox minimum detected velocity, so sneaking never sets them off;
  moving slowly or neutralising with the Shocker drawn near one is safe.
- **Evidence:** ENHP, INT40 tips.
- **Stock:** rare explosions while sneaking.
- **Priority:** P1.
- **Open:** the exact velocity value (decode from the Enhanced class defaults).

### enh:rules.presence - Presence detectors scan every second, silently
- **Players get:** presence detectors scan every 1 s instead of every 3 s, without the scan sound.
- **Evidence:** ENHP.
- **Priority:** P0 if SteeE's `SPresenceTrigger` actors are these detectors, else P1.
- **Open:** confirm which class "presence detector" means.

### enh:rules.stickycam - Sticky Cam handling
- **Players get:** bigger pick-up radius; no brief or permanent lock in weapon mode when firing a Sticky Cam
  during a crouch transition; the context menu no longer interrupts firing a new cam while moving; see
  the Reloaded sticky cam fixes.
- **Evidence:** ENH34, ENH35, ENHP.
- **Priority:** P1.

### enh:rules.shocker_snap - Optional Shocker lock-on snap
- **Players get:** the spy's lock-on snap when drawing the Shocker can be turned off (v3.6, through the
  Reloaded settings).
- **Evidence:** ENH36, ENHP.
- **Priority:** P1.
- **Rewrite:** draw snap and its settings toggle removed by design; drawing preserves the player's aim.
- **Open:** no matching key in Ryan's v4.0d config; where v4.0 stores it (profile or removed) is not known.

### enh:rules.smg - Submachine gun history
- v3.2 lowered its rate of fire and visual recoil; v3.4 reset its properties to default. Current
  behaviour is stock. **Rewrite:** nothing extra. **Priority:** n/a.

### enh:rules.misc - Other script fixes
| ID | Fix | Pri | Evidence |
|---|---|---|---|
| enh:fix.strategic_map | Opening the Strategic Map with a teammate on a map without one no longer crashes | P0 | ENH30, ENH35 |
| enh:fix.disk_replication | Disk pickup interaction replicates correctly for client spies | P0 | ENH35 |
| enh:fix.tase_sound | Shocker tase sound and Sticky Cam activation sound no longer repeat on clients | P1 | ENH35 |
| enh:fix.er_audio | Turning ER off as a merc no longer leaves audio at that spot | P1 | ENH35 |
| enh:fix.coop_wait_death | Spies no longer stuck in a death animation when hit by a grenade while waiting for a co-op move (Station) | P1 | ENH30a |
| enh:fix.barrels | Explosive barrels have a shorter but lethal radius (non-lethal ones left mercs asleep forever) on Krauser Lab and Schermerhorn | P1 | ENHP |
| enh:fix.sound_package | Club House sound package includes objective interruption, win and lose sounds | P1 | ENH30a |

## 4. Match settings and modes (script, v4.0)

### enh:match.create - Create Match options
- **Players get (v4.0):** Game Length (min), Respawn Length (sec), Max Players, Spy Lives, Mercenary Lives,
  Kills to Win, Active Disks, Disks to Take, Games in a Row, Revenge, Random Map, Password, Voice Chat mode,
  Voice in Lobby.
- **Evidence:** INT40 `[Create_Match]` captions (new compared with 3.6), "Kills to Win" also on the game
  settings pages.
- **Stock:** fewer options; values from each map's `SMapSettings.Modes` (TimeLimit, ScoreLimit, SpiesLives,
  SentriesLives, RespawnTime, Handicap, NumDisksMax; MEM scct-interface-package) and URL options
  (`?NBPlayers`, `?ScoreDEF`, `?ScoreATT`, `?GamesInARow`, `?Restart`; MEM objective-player-counts).
- **Rewrite:** match settings record sent in the lobby and the server config; defaults from the map's mode
  entry; the same names. "Revenge" and "Games in a Row" control the round pairs with sides swapped (confirm
  in script).
- **Priority:** P0 for Game Length, Respawn Length, lives, Max Players (SteeE Story); P1 for the rest.
- **Open:** semantics of Revenge, Active Disks and Disks to Take in each mode.

### enh:match.modes - Mode list including Merc Deathmatch
- **Players get:** modes Extraction, Sabotage, Neutralization, Disk Hunt, Story, Deathmatch and Merc
  Deathmatch (long name added in v4.0; short name "Merc DM" already in 3.6). Merc Deathmatch has its own
  playlist and spawns added to 15 maps (CFG playlist "Merc Deathmatch"); results show "X wins with N kills"
  or "Draw at N kills" (Reloaded v4.0d).
- **Evidence:** INT40 `NameMode`, `ShortNameMode`; CFG; RC strings in the Reloaded document.
- **Rewrite:** game mode ids as in `NameMode` (0 Undefined, 1 Extraction, 2 Sabotage, 3 Neutralization,
  4 Disk Hunt, 5 Story, 6 Deathmatch, 7 Merc Deathmatch; Story = 5 matches the Play Level URL
  `GAMEMODE=5`). Merc Deathmatch moves everyone to the merc team (Reloaded hooks `MoveEveryoneToMercs`).
- **Priority:** Story P0; Disk Hunt, Deathmatch, Merc Deathmatch P1 (M7); others P1 (M8).

### enh:match.deathmsg - New death messages
- **Players get:** "sniped" and "has resigned from the game." messages (v4.0, the latter for `!resign`).
- **Evidence:** INT40 `SBase.int` `SDeathMessage`.
- **Priority:** P1.

## 5. Voice (script and Reloaded)

### enh:voice.modes - Voice chat modes
- **Players get (v4.0):** host picks a voice mode when creating a match: Modern (team always, coms
  hackable), Classic (living team only, coms hackable), Team (team always, no com hacking), Open (everyone
  hears everyone); plus Voice in Lobby and "Restrict Mic to Team" for the lobby.
- **Evidence:** INT40 `[Create_Match]` `VoiceChatModes`, `VoiceChatModeHints`, `VoiceInLobbyCaption`;
  `[Lobby_Create] TeamVoiceOnlyCaption`.
- **Stock:** team voice with the special cases in `rl:voice.routing` (grab talk, downed talk, com hacking);
  lobby voice switch `allowVoiceChatInLobby`.
- **Rewrite:** server routing table (`rl:voice.routing`) with a `voice_mode` match setting
  (Modern, Classic, Team, Open), `voice_in_lobby`, `lobby_team_only`. Hacked coms: a spy using Hack Comms
  on a merc receives that merc team's voice for the hack duration (confirm the exact rule in script).
- **Priority:** P0 Modern (default) team routing; P1 the other modes, lobby voice and com hacking.
- **Open:** default mode; what "Living team" does for dead players (do they hear each other, or nobody?).

### enh:voice.binds - Communication binds
- **Players get (v4.0):** a Communications section with Chat, Team Chat, Push To Talk and Mute Microphone.
- **Evidence:** INT40 `[ExtraKeyRows]`.
- **Rewrite:** input actions in both team layouts.
- **Priority:** P0 (Push To Talk, Mute), P1 (others already exist as stock Talk/TeamTalk).

### enh:voice.quality - Sample rate and microphone handling
- **Players get:** voice sample rate raised from 8000 Hz to 11025 Hz (v3.5); no repeated microphone access
  when Windows denies it or no microphone exists (v3.5); the old Low/Medium/High voice quality setting was
  removed in v4.0 (INT40 diff) in favour of the Reloaded voice page.
- **Evidence:** ENH35, ENHP, INT40.
- **Rewrite:** Opus at 48 kHz supersedes both (see `rl:voice.codec`); capture failures back off and show a
  state in the voice page instead of retrying every frame.
- **Priority:** P1.

## 6. Interface and menus

### enh:ui.serverlist - Server list columns
- **Players get (v4.0):** Map, Mode and Players columns in the LAN/internet list (with Ping and the
  dedicated flag).
- **Evidence:** INT40 `[LAN_Seek_Games_List]`.
- **Rewrite:** server browser columns name, map, mode, players, ping, dedicated, password, version state.
- **Priority:** P0.

### enh:ui.scoreboard - Show Connected Players on by default
- **Players get:** the scoreboard toggle (F10) defaults to on and is kept across games (v3.5).
- **Evidence:** ENH35, ENHP.
- **Priority:** P1.

### enh:ui.location - Location callouts
- **Players get:** an optional HUD element with the current location (v3.5); Enhanced added location names
  to nearly all maps and fixed callouts (SteeD and SteeE "Red Building", "Boss's Office"; Orphanage).
- **Evidence:** ENH35, ENH36, RL20.
- **Rewrite:** zone or location names in the SVMap gameplay data; shown by `rl:qol.location`.
- **Priority:** P1.

### enh:ui.cinematic - Cinematic camera: Hide HUD and Free Camera
- **Players get (v4.0):** binds to hide the HUD and to fly a free camera "where the server allows it"
  (pad: left stick moves, triggers up and down, shoulders set speed; keyboard: movement keys, Space and
  Ctrl, wheel sets speed, Shift fast, Alt slow).
- **Evidence:** INT40 `[ExtraKeyRows]` Cinematic Camera section.
- **Rewrite:** done (spec/hud/cinematic-camera.md): a living player's free camera and HUD hide, asked of the server
  and allowed by Alternate Mode's Cinematic Camera switch; pad controls not wired yet.
- **Priority:** P2.
- **Open:** whether the Reloaded lobby label "Streaming" reflects this server permission.

### enh:ui.controller_settings - Invert axes and settings entries
- **Players get (v4.0):** Invert Y, Invert X (Spy), and buttons Configure mouse, Configure controller,
  Configure voice chat, which open the Reloaded pages; an Objective Sounds toggle in the audio settings.
- **Evidence:** INT40 `[Controller_Settings]`, `[Audio_Settings]`.
- **Rewrite:** our options pages hold all of these; Objective Sounds toggles the objective announcement
  sounds.
- **Priority:** P1.

### enh:ui.version - Version label and boot speed
- **Players get:** the Enhanced version shown on the main Versus menu (v3.0; Reloaded v4.0d shows
  "Enhanced v4.0", "Public Beta Build"); ESRB notice skipped (v3.5); menu title "Enhanced Versus" (INT40).
- **Rewrite:** build id on the main menu; no ESRB or Ubi.com pages to skip.
- **Priority:** P1.

### enh:ui.tips - Loading tips
- **Players get:** Enhanced-specific tips (silent movement, proxy mine speed, EMF ghosting).
- **Evidence:** INT40 `SBase.int`.
- **Rewrite:** localisation content.
- **Priority:** P1.

### enh:ui.profiles - Profile tools (history)
- v3.0 moved profile creation to a script in `System` with a Mouse Multiplier (default 6.0) and screen
  resolution presets; v3.1 to v3.3 refined `CreateProfile.cmd`, `ProfileManager.cmd` and `ConvertBuild.cmd`
  (switch between 3D-Analyze and dgVoodoo2 builds). Reloaded replaced all of it (mouse settings, display
  modes, native lighting fix). **Rewrite:** n/a beyond the profiles in plan M8. **Priority:** n/a.

## 7. Audio (content)

### enh:audio.lobby_music - Xbox lobby music
- **Players get:** the longer lobby music from the Xbox version.
- **Evidence:** ENHP.
- **Rewrite:** content pipeline P6 (music).
- **Priority:** P1.

### enh:audio.objective_sounds - Objective sounds toggle
- See `enh:ui.controller_settings`. **Priority:** P1.

### enh:audio.eax - EAX through Creative ALchemy
- Enhanced ships ALchemy as `dsound.dll` and EAX is restored by Reloaded; see `rl:audio.eax`.

## 8. Networking and hosting

- LAN sessions reachable over the internet, LAN master server, UPnP, configurable ports and direct
  addresses all come from Reloaded (ENH34, ENHR); see `rl:net.*`.
- Hosting facts (HOST): forward UDP 7776 and 8888 or use UPnP; allow the firewall prompt; double NAT needs
  forwarding on both routers; CGNAT cannot host. **Rewrite:** the plan's introducer and relay make CGNAT
  hosting possible (Ryan is behind CGNAT), which goes beyond Enhanced.
- Server commands `!map`, `!next`, `!prev`, `!resign`; on dedicated servers every player may change the map,
  on listen servers only the host (ENHR). See `rl:qol.chatcmds`.
- Players in Russia may need relay addresses (ENHR). See `rl:net.relay`.
- Dedicated servers track performance for SC-Panel (seasonal skill ratings, round archive, live servers;
  ENHR, SCP). See `rl:qol.matchlog`.
- The original dedicated server executable is not supported (ENH34).
- `ChaosTheory.log` written in UTC with an improved format (ENH30, ENH35). See `rl:qol.matchlog`.

## 9. Maps

### enh:maps.naming - Map codes
- A map code is exactly five characters: four letters for the family and one for the variant
  (MEM scct-interface-package: other lengths crash the game). Variants seen: D original, E Enhanced,
  T Enhanced 3v3 (and Museum 3v3), P Phoenix's community versions, S mixed (Snake's maps, PS2 ports, Shadow
  House, Sabotage variants), U Pandora Tomorrow UMP maps, X Extraction variant. `!map` takes the code.
- **Rewrite:** SVMap ids keep the code; display names come from each map's settings package (`MapName`).
- **Priority:** P0.

### enh:maps.playlists - Playlists
- Standard (15, competitive Story pool), Extended (25, variants and less common maps), Classic (13
  original maps), 3v3 (7), Merc Deathmatch (15, Story maps with MDM spawns), plus "All installed maps"
  (CFG `map_playlists_v3`). The rewrite ships these as `content/playlists.json` (see `rl:qol.lobbymaps`).
- **Priority:** P1 (P0 needs SteeE only).

### enh:maps.list - Map inventory

INST has 83 map files: 73 playable maps, `Entry` and `Lobby` (front end), and `TME30`, `TME32`, `TME33`,
`TSP20` to `TSP23`, `TSP29` (tutorial and exam maps, inferred from names; confirm). Ryan's SVM 4.0 copy
adds `MoonD`, `OffsE` and `ShipD` (his own maps) for 86. Names come from the Reloaded name cache in CFG.

| Code | Name | Playlists |
|---|---|---|
| AquaD | Aquarius | Classic |
| AquaE | Enhanced Aquarius | Standard, Extended, MDM |
| AquaT | Enhanced Aquarius 3v3 | 3v3 |
| BankD | Bank | Classic |
| BankP | Phoenix's Bank | Extended |
| CineE | Enhanced Cinema | Standard, Extended, MDM |
| CineS | Enhanced Cinema Sabotage | - |
| CineU | UMP Cinema | - |
| ClarD | Clarity Soft | - |
| ClubD | Club House | Classic |
| ClubE | Enhanced Club House | Standard, Extended, MDM |
| ClubS | Shadow House | Extended |
| ClubT | Enhanced Club House 3v3 | 3v3 |
| CpusS | Snake Cpusa | - |
| DeftD | Deftech Belew | Classic |
| DeftS | Snake Deftech Belew | Extended |
| FactD | Factory | Classic |
| FactE | Enhanced Factory | Standard, Extended, MDM |
| FactP | Phoenix's Factory | - |
| FactT | Enhanced Factory 3v3 | 3v3 |
| GhosS | Snake Ghost Town | Extended |
| KrauE | Enhanced Krauser Lab | Standard, Extended, MDM |
| KrauU | UMP Krauser Lab | - |
| MissD | Missile Strike | Classic |
| MissE | Enhanced Missile Strike | Standard, Extended, MDM |
| MissP | Phoenix's Missile Strike | - |
| MounE | Enhanced Mount Hospital | Standard, Extended, MDM |
| MounS | Enh Mount Hospital Sabo | - |
| MounU | UMP Mount Hospital | - |
| MuseD | Museum | Standard, Extended, Classic, MDM |
| MuseE | Enhanced Museum | - |
| MuseT | Museum 3v3 | 3v3 |
| OffsD | Offshore Oilrig | - |
| OrphD | Orphanage | Extended, Classic |
| OrphE | Enhanced Orphanage | Standard, MDM |
| OrphS | PS2 Orphanage | - |
| OrphT | Enhanced Orphanage 3v3 | 3v3 |
| PanaD | Panama Bank | - |
| PanaS | Snake Panama Bank | - |
| PolaD | Polar Base | Classic |
| PolaE | Enhanced Polar Base | Standard, MDM |
| PolaP | Phoenix's Polar Base | Extended |
| PolaT | Enhanced Polar Base 3v3 | 3v3 |
| PrisD | Prison | Standard, Extended, MDM |
| PyroD | Pyrocryptic | - |
| RiveD | River Mall | Classic |
| RiveE | Enhanced River Mall | Standard, Extended, MDM |
| RiveP | Phoenix's River Mall | - |
| SancD | Sanctuary | - |
| SancE | Enhanced Sanctuary | Standard, Extended, MDM |
| ScheE | Enhanced Schermerhorn | Extended |
| ScheU | UMP Schermerhorn | - |
| ShadD | Shadow Moses | Extended |
| ShadP | Phoenix's Shadow Moses | - |
| SkysD | Skyscraper | Standard, Extended, MDM |
| SkysP | Phoenix's Skyscraper | - |
| SlauS | Snake Slaughterhouse | - |
| StatD | Station | Classic |
| StatE | Enhanced Station | - |
| StatS | PS2 Station | - |
| SteeD | Steel Squat | Classic |
| SteeE | Enhanced Steel Squat | Standard, MDM |
| SteeP | Phoenix's Steel Squat | Extended |
| SteeT | Enhanced Steel Squat 3v3 | 3v3 |
| StroD | Stronghold | - |
| TermD | Terminus | Extended |
| VertE | Enhanced Vertigo Plaza | Extended |
| VertU | UMP Vertigo Plaza | - |
| VertX | Enh Vertigo Plaza Extraction | - |
| WareD | Warehouse | Classic |
| WareP | Phoenix's Warehouse | Extended |
| ZiopD | Zioplex | Extended |
| ZiopS | Zioplex Sabotage | - |

Rewrite: every map converts through `svmbuild` (plan); variant maps share textures and meshes with their
family, so the shared packs matter more than per-map work. Sabotage and Extraction variants need those
modes (M8). The v3.5 "Extended" edition shipped extra community maps, some experimental (ENH35); 4.0
merges the pool into one install.

### enh:maps.steee - Enhanced Steel Squat (slice map)
All P0. Most are content or map script and arrive through the converter; each one gets a check in the
SteeE validation scenario.
- A merc spawn on the third floor, active only for the match start, not for respawns.
- One merc always gets an outside spawn point.
- Computer objectives take 10 s to neutralise (matches the plan's `DominationCaptureTime 10`).
- The Secret Files disk resets 15 s after being dropped (matches the plan).
- Shutter hacking panels take 4 s (matches the plan's three 4 s shutter switches).
- The Restaurant shutters moved to the second floor.
- The Laboratory area was reworked for faster rotation.
- The Secret Files hatch wraps around the boss's office instead of leading to the connector building.
- Fixes: chaffing the Shop ceiling lights no longer hides the mesh; the explosive near Crane Control no
  longer plays sounds after being destroyed when mines or frags go off nearby; the Crane Control sequence
  explosives can no longer be destroyed before the event; a map leak that removed BSP collision on a
  connector wall is closed (v3.5); callouts for Red Building and Boss's Office fixed (v3.6); larger
  interaction radius for the ammo stock near the Shop shutters (v3.1).
- Steel Squat (SteeD) shares the chaff, crane explosive and callout fixes.
- **Open:** confirm the third-floor start spawn and the guaranteed outside spawn in the converted
  `gameplay.json` (spawn priority and enable flags), since the plan's spawn rule (priority before start,
  then distance rating) must reproduce them.

### enh:maps.changes - Other Enhanced map changes (summary)
Content and map script; all arrive with conversion. Priority P1 (P2 for maps outside the Standard pool).
Each line is a short summary of ENHP; the full notes stay the reference for validation.
- **Aquarius (E):** new spy pole to the Tech Room, climb back from the Tech Room vents, the fish tank that
  holds a disk can no longer be refilled, Pirates' Room balcony climbable, small gap, painting and collision
  fixes.
- **Cinema (E):** rebuilt from the Pandora Tomorrow layout; computers 12 s; new disk in Entry captured in the
  Lobby; advertising columns removed; extra medkit; ammo stock moved; Cam Net added; motion sensors allow
  sneak walking; Cafeteria interaction fixed.
- **Club House (E and D):** back-floor computers 12 s (E); extra pole (E); glass doors removed (E); visual,
  opacity and sound package fixes.
- **Factory (E and D):** rebuilt from the original in v3.4; corner vent removed; Corridor B door opens from
  both sides; digger 5 s; chaff "Security failure" message fix; upstairs shortcut door retries closing; Cam
  Net yaw fix (D).
- **Krauser Lab (E):** Pandora Tomorrow base with lighter security; 10 min limit; reworked Warehouse lasers;
  Laboratory lasers reduced; Irradiation computers moved out of chambers; front doors always open; one-way
  escape to the Parking Lot; Cam Net added; barrels lethal with a shorter radius.
- **Missile Strike (E and D):** 4 spy lives, 3 merc lives (E); insertion hacks 5 s (E); catwalks no longer
  shot through; East Control Panel boxes moved; generic "Hacking in Progress" messages; one disk of four
  opens the Silo (E); spam fix on the Central Shaft door; texture, label, ammo stock and drop zone fixes;
  spotlight mouse input (via Reloaded).
- **Mount Hospital (E):** Pandora Tomorrow base; computers 12 s; Room 116 reworked, Room 208 opened; room
  motion sensors removed; Nursery vent laser; extra medkit; Cam Net; ceiling penetration and ladder fixes.
- **Museum (E and D):** Cafe computers 12 s; Storage doors merc-only, unlocked by Cafe objectives or new
  hack panels; presence triggers re-centred; vent visibility fix.
- **Orphanage (E and D):** Cam Net redesigned; Sector 2 spawns activate 10 s after Sector 1 is bombed;
  vents removed and restored over versions; motion sensor changes; drop zone moved with cover; bomb blast
  timing fix; elevator, floor exploit, texture and tutorial tip fixes; callouts.
- **Polar Base (E):** generators 25 s; Drilling Tower doors 2 s; four of eight medkits removed; balcony
  raised; spawn glass unbreakable; laser, neck snap, door retry, geometry and blocking volume fixes.
- **River Mall (E):** rebuilt from the original in v3.4; spy insertion reworked; vents and Hi-Fi cameras
  changed over versions; Central Post Cam Net removed; Offices window lowered; Parking Lifts camera 30 s;
  curtain and glass door fixes.
- **Sanctuary (E):** added to the pool; a merc spawn moved to Fellowship Hall; street light collision;
  geometry optimised; grenade-blocking volumes removed.
- **Schermerhorn (E):** Pandora Tomorrow base with lighter security; computers 12 s; new Tech Room disk; Water
  Tank hacks 4 s; corridor computer moved; gate buttons and a camera hack removed; Cam Net; barrel and leak
  fixes.
- **Skyscraper (D):** exterior reworked with more cover; ledge grab additions and removals; door collision
  and object fixes; window gap and jump fixes; laser mine host crash fix; geometry optimised.
- **Station (E and D):** 10 min, 4 spy and 3 merc lives (E); parking jacks 20 s; door timings 0 s; doors open
  after objectives; Checkpoint doors, co-op and medkit restored; extra medkit; sniping, vent dust, death
  animation, localisation and texture fixes.
- **Terminus (D):** added in v3.3; Cam Net in the Cafeteria; Courtyard medkit; vehicle glass and wire
  collision fixes.
- **Vertigo Plaza (E):** Pandora Tomorrow base with lighter security; 10 min; computers 15 s; door hacks 3 s,
  none needed from inside; horizontal lifts on each bridge; bridge arches removed; Cam Net on the Helipad;
  door frame collision and presence trigger door fixes.
- **Deftech Belew (D):** Cam Net name localisation fix.
- **Exam (spy tutorial):** floating medkit fix.

## 10. Open questions (summary)

1. Silent sounds: sound only, or also the gameplay noise event.
2. Crosshair centring: HUD only, or the aim trace too.
3. Exact values: proxy mine velocity, presence detector class, voice mode defaults, Revenge and disk
   settings semantics (all from the decompiled Enhanced packages).
4. Where v4.0 keeps the Shocker lock-on snap option.
5. Tutorial map codes (TME and TSP) and whether the rewrite needs them early.
6. Whether 3.6 `.eng` files in `Desktop\SVM\SVM 3.6` are unmodified Enhanced 3.6 (the v4.0 diffs above
   assume so).
7. Add the `enh:` prefix to the coverage unit scheme, or map these items to `uc:` units.
