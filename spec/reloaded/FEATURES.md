# Reloaded patch: feature inventory for the rewrite

Status: research spec, M0. Scope: every player-facing feature of the SCCT Versus Reloaded patch
(community patch by AllyPal, injected as `Reloaded.Core.dll`) that the rewrite should provide natively.
Companion: `spec/enhanced/FEATURES.md` covers the Enhanced SCCT Versus content and script changes.

Ryan wants most if not all of these features. The rewrite takes ideas and observable behaviour only.
`Reloaded.Core.dll` is never patched, copied, redistributed or bypassed, and the rewrite never talks to
the Reloaded master server.

## 1. What was examined

| Tag | Source |
|---|---|
| CFG | `Desktop\SVM\SVM 4.0 Public Beta\System\SCCT_Versus.config` (header "Reloaded V4.0d", Ryan's live settings) |
| CFGREF | `Desktop\SVM\Enhanced SCCT Versus 4.0 Public Beta Vanilla Reference\System\SCCT_Versus.config.orig` (V4.0b, `z_main_menu_count` 1, so close to first-run values; may hold imported settings) |
| CFG36 | `Desktop\SVM\SVM 3.6\System\SCCT_Versus.config` (V3.0a, shows retired keys) |
| LOGS | `SCCT_Versus.exe.log`, `Reloaded.Core.dll.scct versus.log`, `SCCT_Versus_crash.scct versus.log` in the SVM copy |
| RC@0x... | String at that file offset in `Reloaded.Core.dll` v4.0d, sha256 `6b9d4444...6995dc46` (identical in the reference install and the SVM copy) |
| LCH | `SCCT_Versus.exe` launcher, sha256 `e4ccd797...2bac93c9` |
| GAME | Game executable `SCCT Versus`, sha256 `a98873aa...28ec9950` |
| INT | Enhanced 4.0 `System\_PC_\GUI.int`, `SBase.int`, compared with the 3.6 `.eng` files |
| INI | Fresh install `Default.ini`, `DefaultProfile.ini`, `Default_kbd_0.ini`, `Default_kbd_1.ini`, `dsound.ini` |
| RLF | allypal.github.io/SCCT_Versus_Reloaded/features/ |
| RL11..RL30 | Release posts v1.1 (2024-11-14), v1.2 (2024-11-29), v2.0 (2025-03-30), v2.1 (2025-03-31), v3.0 (2025-11-05) on the same site; GitHub AllyPal/SCCT_Versus_Reloaded (MIT licence file, README points to the site, releases up to v3.0a) |
| ENH | Enhanced README, PatchNotes.md and release notes v3.0 to v3.6 (GitHub Joshhhuaaa/EnhancedSCCTVersus) |
| HOST | Enhanced hosting guide gist (Joshhhuaaa/6e4f8ff745f5fe5148d4d8b265fa6518) |
| PCGW | PCGamingWiki page "Tom Clancy's Splinter Cell: Chaos Theory" (read through the MediaWiki API) |
| SCP | sc-panel.com (community stats site fed by dedicated servers) |
| MEM | Memory notes: feedback-testing-servers, game-console-and-bots, headless-editor-and-game, scct-engine-memory-layout, objective-player-counts |

String dumps and fetched pages are kept in `local/reloaded_strings/` and `local/web/` (gitignored).
The installed build is Reloaded v4.0d ("Public Beta Build", bundled with Enhanced v4.0 Public Beta 3).
v4.0 has no public release notes yet; its features below come from the binary, the config and the
Enhanced 4.0 menu text. Items marked "v4.0" are therefore observed, not announced.

## 2. How the patch works (context, not a model)

- `SCCT_Versus.exe` (LCH) is a small launcher. It finds the real game (`SCCT Versus`, no extension),
  starts it suspended, injects `Reloaded.Core.dll` (remote thread + LoadLibrary), then resumes it. It warns
  when not placed in `Versus\System` and, for Ubisoft Connect installs without `Default.ini`, offers to run
  `..\..\support\support.bat`. It logs to `SCCT_Versus.exe.log`.
- `Reloaded.Core.dll` patches the game in memory, hooks UnrealScript functions by name (RC strings such as
  `ServerFireWeapon`, `GotoWaitingState`, `launchMatchNow`), converts the D3D8 renderer to D3D9 and rewrites
  old shader assembly, and draws its own UI with Dear ImGui.
- Third-party code visible in the DLL: Dear ImGui 1.92.8, SDL3 (gamepads), Opus 1.5.2, speexdsp
  (preprocess and echo canceller), MiniUPnPc 2.3.2, c-ares (DNS), nlohmann/json, SMAA, stb_image,
  Fork Awesome icons, D3DX9 (shader compile). These hint at our own picks (section 11).
- Some v4.0 features only switch on for the latest Enhanced build ("addresses are latest-Enhanced only",
  RC@0x2b7e6c, RC@0x2b85b0). Some Reloaded release notes have an "Enhanced only" section.

Consequence for the difftest program: the reference install runs with Reloaded injected, so recorded
traces already contain Reloaded behaviour (fixed grenades, frame-rate fixes, lag compensation). Our target
is "Enhanced 4.0 + Reloaded 4.0d", not stock. Two practical rules for R4 sandboxes: pin `fps_spy` and
`fps_merc` to 60 to match our 60 Hz tick, and keep `list_on_reloaded_master_server` false and `host_upnp`
false (MEM feedback-testing-servers). Where a decompiled Enhanced script and a trace disagree, the
difference is probably a Reloaded memory patch and needs its own spec entry.

## 3. Conventions in this document

- ID: `rl:<group>.<name>`, matching the `rl:` unit prefix of the coverage plan.
- Priority: **P0** = needed for the M6 SteeE 2v2 online slice, **P1** = parity (M7/M8), **P2** = later.
- "Ryan" = value in CFG, "first-run" = CFGREF, "doc" = a default stated in comments or release notes.
- "Stock" = the original game without Reloaded. An entry without a Stock line means the stock game has no
  such feature (voice entries share the stock baseline at the top of section 5).
- Rewrite settings: a single user settings file `user://settings.json` (JSON with comments, read with
  System.Text.Json comment skipping). Keys reuse Reloaded names wherever the meaning is the same, so an
  importer can read an existing `SCCT_Versus.config` (Reloaded itself offers "Import settings from your
  previous installation?", RC@0x332f90). Sim-relevant values never come from this file.

## 4. Networking, master server and NAT

### rl:net.master - Master server and internet server list
- **Players get:** internet games in the in-game server list again, with no third-party tools. LAN
  searches also ask the master server, so internet games appear in the stock LAN list.
- **Evidence:** RL11, RLF; RC@0x340dc4 and nearby (`SCLIREQL`, `SCLILIST`, `SCLIREGI`, `SCLIREGR`,
  `SCLIPING`, `SCPINGEX`, `SSRVINFO`, `SSRVINEX`, `SSRVSTOP`, `JOIN H=`); LOGS (DNS resolved to
  141.227.135.52); ENH README.
- **Config:** `master_server_dns` = "master-v2.scct-reloaded.ovh"; `z_cache_master_server_ip` =
  "141.227.135.52" (cached last good address); `list_on_reloaded_master_server` = true.
- **Stock:** Ubi.com services shut down in April 2016 (PCGW); only LAN beacons and the Ubi.com list existed.
  Stock "direct IP" was only available through Ubi.com.
- **Protocol facts:** UDP on the query port. Hosts register once per minute; a registration lapses about a
  minute after hosting stops. The master answers with version status (optional or mandatory update,
  "Enhanced must be updated", "Banned", "temporary timeout") and checks that the host's external query port
  matches its configured one ("Your external query port is not N ... Your server is not listed").
  Reloaded resolves the DNS name with c-ares and falls back to resolvers 8.8.8.8 and 1.1.1.1
  (RC@0x3408ac), then to the cached IP.
- **Rewrite:** `OpenScct.Master` (plan): HTTPS list API + UDP introducer on the VPS, behind an invite key while
  private. Hosts send a signed heartbeat every 30 s with name, map, mode, players, max players, version,
  protocol hash, content hash, password flag, dedicated flag; entries expire after 90 s. The introducer
  records the observed public endpoint and runs a reachability probe (direct, punch, relay) instead of a port
  equality check. Client resolves with `System.Net.Dns`, falls back to a cached address, and also accepts a
  list of alternate master hosts. Never contacts the Reloaded master.
- **Priority:** P0 (M5).
- **Open:** whether to accept the Reloaded master list format for interop. Recommendation: no (separate
  game, separate protocol).

### rl:net.listing - Listed and unlisted hosting
- **Players get:** host privately by turning listing off; console `listed true|false`.
- **Evidence:** RL11, HOST "Unlisted Servers", RC@0x338c58 (`listed`), RC@0x341830.
- **Config:** `list_on_reloaded_master_server` = true (Ryan, first-run).
- **Stock:** no equivalent beyond Ubi.com lobbies.
- **Rewrite:** host option `net.listed` (default true) plus an optional password. Unlisted servers stay
  reachable by direct address and by an invite code from the introducer.
- **Priority:** P0 (Ryan's test rule needs unlisted hosting).

### rl:net.upnp - UPnP port forwarding
- **Players get:** hosting without manual router setup; the lobby chat reports the result.
- **Evidence:** RL11, HOST; LOGS (`UPNP_AddPortMapping 8888: 0 (ok)`, same for 7776, repeated per hosted
  session); RC@0x340e58..0x3410e0 (success, failure and disabled messages); lease value 86400 (RC@0x340928);
  MiniUPnPc 2.3.2.
- **Config:** `host_upnp` = true.
- **Stock:** manual port forwarding only.
- **Rewrite:** Godot `Upnp` class (built on miniupnpc) on the client process only, never in `OpenScct.Server`
  for VPS use. Map the game port and LAN port with a 24 h lease, refresh on host start, delete on stop. Show
  the result as a lobby system message, with the manual ports when it fails. Add IPv6 pinhole support later.
- **Priority:** P0 for M4/M5 listen hosts, but Ryan is behind CGNAT, so the introducer and relay matter more.
- **Open:** none.

### rl:net.ports - Ports and LAN compatibility
- **Players get:** configurable ports; LAN and Radmin VPN discovery keeps working on the default query port.
- **Evidence:** CFG comments; HOST; PCGW port list (stock UDP 7776, 8888-8891, 8878, 9011 and more; TCP
  6668, 9102, 40000-43000 for Ubi.com); INI `[URL] Port=8888`, `CDKeyPort=8878`.
- **Config:** `host_port_game` = 8888, `host_port_query` = 7776, `client_port_query` = 7776. Values other
  than 7776 break LAN and Radmin joins (CFG comment). Server creation fails with a chat message when the
  game port is already in use (RC "Server creation failed ... UDP Port {} is already used").
- **Stock:** 8888 game, 7776 query/beacon.
- **Rewrite:** plan ports 28888 game, 28889 LAN discovery, 28880 introducer, so the rewrite never clashes
  with the original or RE+ test slots. LAN discovery uses a fixed port so a custom game port does not break
  LAN. Port-in-use is detected at bind time and shown in the lobby.
- **Priority:** P0.

### rl:net.serverlist - Manual server list (direct address)
- **Players get:** join servers that are not on the master by listing `IP:PORT` entries; they show up in
  the server list with ping.
- **Evidence:** RL11, CFG, HOST, ENH README (relay address list for players in Russia).
- **Config:** `server_list` = [] (array of "ip:queryport" strings).
- **Stock:** none outside Ubi.com.
- **Rewrite:** settings key `server_list` (same format, our query port) plus a "Join address" box and
  invite codes. Entries are queried like master entries.
- **Priority:** P0.

### rl:net.relay - Relays for blocked regions
- **Players get:** players whose ISP blocks the master and dedicated servers can still play through relay
  addresses (community relays listed in the Enhanced README, set through `server_list` and
  `master_server_dns`).
- **Evidence:** ENH README warning section.
- **Stock:** none.
- **Rewrite:** the plan's VPS relay fallback covers it. Allow several master/introducer hosts and relay
  endpoints in settings. Relays forward opaque UDP; they do not see gameplay semantics.
- **Priority:** P1 (the relay itself is P0 for Ryan's CGNAT host in M5).

### rl:net.version - Version gating and update notices
- **Players get:** the server list says why a game cannot be joined ("Requires Enhanced" or "Requires
  Stock"); optional and mandatory update notices with a download button; mandatory updates block listing
  and finding games.
- **Evidence:** RL11, RL20 (stock version number incremented); RC strings "Cannot join: Requires Enhanced
  SCCT Versus", "A mandatory update of Reloaded is available", "Download Optional Reloaded Patch", console
  `update` and `enh_update`.
- **Stock:** generic version mismatch on join.
- **Rewrite:** handshake carries protocol hash and content manifest hash (plan) and names the differing
  file, reusing the original wording "Your game's version is not the same as the server's". The browser
  greys out incompatible entries with the reason. The master reports latest and minimum client versions;
  the client shows a banner and opens the release page on request. No silent auto-update.
- **Priority:** P0 for the handshake, P1 for update notices.

### rl:net.dedicated - Dedicated servers
- **Players get:** community dedicated servers (several paid for by the Reloaded author). On dedicated
  servers every player may change the map; on listen servers only the host can. Matches on dedicated
  servers feed SC-Panel statistics.
- **Evidence:** RC `-dedicated` (RC@0x333040), `dedicated_server.exe` in the host list; dedicated start-up
  log lines that walk the menus ("Loading profile", "Skipped create match", "Skipped game settings",
  "Skipped multi menu", "Skipped default gadgets page", "Skipped waiting player page", RC@0x2b8ac0);
  ENH README note on map permissions; ENH 3.4 says the original dedicated server executable is not shipped;
  SCP.
- **Stock:** a separate Ubisoft dedicated server executable existed (PCGW) but is not supported now.
- **Rewrite:** `OpenScct.Server.Cli` (plan), headless, win-x64 and linux-x64, no menus to skip. Map rotation
  from playlists (`rl:qol.lobbymaps`), chat commands with a permission rule (dedicated: everyone may use map
  commands unless an admin list is set; listen: host only), JSON match log (`rl:qol.matchlog`).
- **Priority:** P0 (M5 needs a VPS dedicated server).
- **Open:** the "Streaming" label in the lobby status (RC@0x33ae7c) is unexplained; possibly map or data
  streaming to joiners. Check in a sandbox lobby.

### rl:net.lagcomp - Lag compensation
- **Players get:** sticky cams (v3.0) and possibly other shots (v4.0) feel responsive under latency.
- **Evidence:** RL30, ENH 3.6; RC `!lagcomp` with `on|off|reset|<ceiling>` and "lagcomp: on - ceiling
  {}ms", "lagcomp: server-side setting" (RC@0x3394c4..0x3395c0).
- **Config:** server-side, changed by chat command; no config key. Decoded (Reloaded.Core 0x100758f0): on at
  start-up, ceiling 120 ms, `reset` gives 120, a number is clamped to 0..500 (spec/net/lag-compensation.md).
- **Stock:** none; clients see the server's view with full latency.
- **Rewrite:** server keeps a ring buffer (about 1 s at 60 Hz) of hit volumes and launch-relevant state per
  pawn. A client fire or launch RPC carries its client time; the server rewinds to `min(estimated view
  delay, ceiling)` and validates there. Server setting `net.lagcomp` (on/off) and `net.lagcomp_ceiling_ms`;
  chat command `!lagcomp` for admins. Must stay deterministic: rewind uses recorded tick states, not
  interpolation of floats from wall time.
- **Priority:** P1 (original behaviour first, then this as a server option, default to match Reloaded).
- **Open:** which actions Reloaded compensates (sticky cam only, or bullets and shocker too).
- **Done (2026-10-28):** bone traces of every client combat call (merc guns, shotgun, spy pistol), off by
  default, ceiling 120 ms (Reloaded's, decoded later the same night); spec/net/lag-compensation.md, docs/NETCODE.md. Sticky cam launches are not
  rewound (projectiles).

### rl:net.fireprediction - Client fire prediction
- **Players get:** impact effects and hit sounds appear at once on the shooter's screen instead of after a
  round trip.
- **Evidence:** v4.0 RC "Fire prediction" messages and hooked names `TraceFire`, `TraceFireWithPos`,
  `ServerFireWeapon`, `ServerFireWeaponWithLocation`, `AddImpact`, `GetFireStart` and the impact classes
  `SImpactEffectBeton/Metal/Soft/Glass/Wood/Fence/Hole/Water/Sand`, `SImpactSteamPipe`, `SImpactWaterPipe`,
  `SImpactEffectShocker`, `SImpactEffectShockerMetal` (RC@0x2b80e4..0x2b85ec).
- **Stock:** effects come from the server.
- **Rewrite:** owner-only cosmetic prediction in the view layer: run the same trace on the client against
  the predicted world, spawn effects and sounds, then reconcile with the server's authoritative impacts
  (drop duplicates by shot id). Damage is server-only.
- **Priority:** P1.
- **Done (2026-10-28):** merc rifle/SMG and spy pistol world impacts (and the pistol's shocker spark on pawns);
  the server does not send a predicted shot's impacts back to its shooter instead of dropping duplicates. Client
  option `fire_prediction`, on. spec/net/lag-compensation.md.

### rl:net.grenades - Server-simulated grenades and the "instas" fix
- **Players get:** no more grenades exploding on impact ("instas"), no grenades seemingly dropping at the
  merc's feet when the host runs at a much higher frame rate, fewer grenade desyncs.
- **Evidence:** RL11, RL30, RLF, ENH patch notes; RC replication property names `bReplicateMovement`,
  `bNetTemporary`, `bUpdateSimulatedPosition`, `bAlwaysRelevant` near the input strings (RC@0x2b87c4).
- **Stock:** projectile simulated with frame-rate dependent steps and replicated loosely; detonation timing
  could break.
- **Rewrite:** projectiles are server-authoritative sim actors on the fixed 60 Hz tick; the owner predicts
  its own throw from the same initial state and reconciles; other clients get an initial-state bunch plus
  periodic corrections. Fuse time is a sim timer. Add a scenario: throw at 30, 144 and 240 render fps,
  identical sim output.
- **Priority:** P0 (merc frag is in the slice).

### rl:net.clientsync - Fewer position desyncs and no adrenaline rubber-banding
- **Players get:** other players' positions desync less (v3.0); spies no longer rubber-band when shot while
  the adrenaline speed boost runs (v1.1).
- **Evidence:** RL11, RL30, ENH.
- **Stock:** proxies drift; the server corrects a hurt spy whose client did not apply the speed change.
- **Rewrite:** ProxyMotion interpolation with a `net.proxy=ue2` mode (plan) and speed modifiers carried in
  the saved-move state so client and server agree. Difftest gate: corrections per minute (plan M4).
- **Priority:** P0.

### rl:net.rates - Network settings for high frame rates
- **Players get:** hosting works at high frame rates (up to 165, later 240 fps).
- **Evidence:** RL11, RLF. INI shows stock `NetServerMaxTickRate=20`, `LanServerMaxTickRate=35`,
  `MaxClientRate=20000` and adaptive `CONF*` net-speed limits.
- **Stock:** Versus is effectively locked to 30 fps for netcode reasons (PCGW).
- **Rewrite:** n/a by design: fixed 60 Hz sim, network flush at 20 Hz internet / 35 Hz LAN (configurable),
  render rate independent.
- **Priority:** P0 (architecture).
- **Open:** which ini values Reloaded rewrites at start. Read `Default.ini` in a sandbox after a launch.

### rl:net.ping - Accurate pings
- **Players get:** correct ping values in the server browser and LAN list.
- **Evidence:** RL20, RL30.
- **Stock:** inaccurate (frame-quantised) ping.
- **Rewrite:** query packets carry a client timestamp echoed by the host; measure with a monotonic
  high-resolution clock off the sim thread. RTT inside matches comes from OpenScct.Net acks.
- **Priority:** P0.

### rl:net.stats - Network readout and `!pings`
- **Players get:** an on-screen readout under the fps counter with ping and jitter (a host sees best and
  worst client ping), optional packet loss received/sent, ping per player on the scoreboard, and a `pings`
  console or `!pings` chat command listing ping, jitter and loss per connection.
- **Evidence:** CFG keys and comments; RC "{}: ping {}, jitter {}, loss {:.0f}/{:.0f}%" (RC@0x338430).
- **Config:** `show_net_stats` = true (Ryan) / false (first-run); `show_net_loss` = false (added after
  v4.0b); `show_ping` = true (Ryan) / false (first-run).
- **Stock:** none.
- **Rewrite:** OpenScct.Net exposes per-connection RTT (EWMA), jitter (mean absolute RTT delta), loss in each
  direction from ack bitfields, bytes per second. HUD overlay plus `!pings`. Also a host-side net debug page.
- **Priority:** P0 (needed to judge M4/M6).

### rl:net.practice - Practice server mode
- **Players get:** a practice server with lots of ammo, lives and quick spawns, and chat commands
  `!heal`, `!regen`, `!tank`, `!kill`.
- **Evidence:** RC@0x3395e0..0x3396d0, console command `practice`.
- **Stock:** none.
- **Rewrite:** a server rules profile `practice` layered on the mode rules; commands only when the profile
  is active. The port's round has no time limit, unlimited lives, and (its own choice for the "lots of ammo")
  ammunition, gadget charges and gas mask air that are never used up and gear changes that go on at once
  (`PracticeRules.On`, set on the server's level and, from the lobby setup, on each client's).
- **Priority:** P2.
- **Open:** exact effects of `!regen` and `!tank` (the port's choices, 10 health a second and no damage, are in
  docs/ONLINE.md "Practice servers"); the extra ammunition.

## 5. Voice chat

Stock baseline: the game already has voice chat. The executable carries Speex 1.1.6 (narrowband,
wideband and ultra-wideband modes, GAME), `DS\DSAudioVoice.cpp`, net stat "Voice Bytes", and script names
`VoiceChatterInfo`, `ServerChangeVoiceChatter`, `ServerGetVoiceChatters`, `ServerSetAllowVoiceChat`,
`ServerSetVoiceChatQuality`, `ClientSetGameVoiceChatQuality`, `allowVoiceChatInLobby`, `bVoiceBanned`,
`SetVoiceMask`, `SetVoiceOnSpeaker`, `bCanTalkToGuy`. The profile holds `allowVoiceChat=True`,
`voiceChatQuality=2`, `iVoiceChatVolume=1.0` (INI). Game rules: normally you talk to your team; a spy who
grabs a merc can talk to him; a merc kneeling over a downed spy can talk to him with Hack Comms; spies can
hack merc communications (SBase loading tips, INT). Enhanced 3.5 raised the sample rate from 8000 to
11025 Hz and stopped the game polling a missing or denied microphone. Enhanced 4.0 adds the voice modes
Modern, Classic, Team and Open (see `spec/enhanced/FEATURES.md`, `enh:voice.modes`).

### rl:voice.codec - Modern codec and transport
- **Players get:** clearer voice (v2.0 "slight improvement", v4.0 a new pipeline).
- **Evidence:** Opus 1.5.2 and speexdsp in the v4.0 DLL; CFG comment on frames: 320-byte frames, 20 ms
  narrowband or 10 ms wideband, which means 160 16-bit samples at 8 or 16 kHz.
- **Rewrite:** Opus, 48 kHz mono, VOIP application, 20 ms frames, VBR around 20 to 24 kbps, in-band FEC on,
  DTX on, packet-loss hint fed from net stats. Use Concentus (managed Opus port) or libopus through
  P/Invoke; voice is outside the sim, so determinism rules do not apply, but it must not allocate per frame.
  Transport: a separate unreliable-sequenced LiteNetLib channel, never part of actor replication. Packet:
  talker player id, sequence, frame count, codec id, Opus payload. Client to server to recipients, never
  peer to peer: the server decides who hears whom (`rl:voice.routing`), which also stops modified clients
  from listening to the enemy.
- **Priority:** P0 (Ryan names in-game voice as a headline feature; keep the P0 cut small: Opus, PTT and
  activation gate, team routing, self mute, volume).
- **Open:** what Reloaded v4.0 sends on the wire (Opus inside the stock voice packets, or its own channel).
  Not needed for the rewrite.

### rl:voice.activation - Push to talk and voice activation gate
- **Players get:** transmit while a key is held, or automatically when speaking. The gate opens at the
  threshold, stays open until the level falls to 60% of it, and holds for a release time so word endings
  are not cut.
- **Evidence:** CFG comments; INT key binds "Push To Talk" and "Mute Microphone" (Enhanced 4.0
  `ExtraKeyRows`); RC exec names `PushToTalk`, `ToggleVoiceMute`.
- **Config:** `voice_push_to_talk` = false; `voice_activation_threshold` = 0.02 (0.0 always transmits, 1.0
  never; same scale as the level meter); `voice_activation_hold_ms` = 300.
- **Stock:** stock binds `Talk` (F5) and `TeamTalk` (F6) are text chat; stock voice had no configurable gate.
- **Rewrite:** same three keys and semantics; level = RMS of the 20 ms frame after noise suppression;
  hysteresis 60%; hold timer in ms. Input actions `voice_ptt` and `voice_mute_toggle` in both team layouts.
- **Priority:** P0.

### rl:voice.device - Microphone selection
- **Players get:** pick the recording device; the UI explains that Windows voice uses the default
  communications device, a frequent cause of silent mics.
- **Evidence:** CFG comments; RC "Windows reports no microphones", "Not connected - falling back to
  Windows' choice".
- **Config:** `voice_input_device` = "{14305C1D-...}" (Ryan, a DirectSound GUID) / "" (first-run follows
  Windows); `z_cache_voice_input_device_name` = "Wave Link MicrophoneFX (Elgato Wave:3)".
- **Rewrite:** Godot `AudioServer` input device list (names), stored by name, empty = system default;
  needs `audio/driver/enable_input`. Capture through `AudioStreamMicrophone` on a muted bus with
  `AudioEffectCapture`. Fallback message when the saved device is missing.
- **Priority:** P1.
- **Open:** whether Godot's default capture device on Windows is the console or the communications role.
  Test on Ryan's machine with two devices.

### rl:voice.gain - Gain and automatic gain
- **Players get:** manual mic gain, or automatic gain that moves towards a comfortable level.
- **Config:** `voice_input_gain` = 1.0 (ignored while auto gain is on); `voice_auto_gain` = true.
- **Rewrite:** simple gain stage; AGC from the chosen DSP library (speexdsp-style AGC or WebRTC AGC2).
- **Priority:** P1.
- **Done (2026-10-28):** our own managed speex-style AGC (`VoiceAutoGain`, docs/VOICE.md "Microphone clean-up"):
  speech aimed at -20 dBFS, moved only by frames well over the noise floor; Auto-Adjust Mic Volume on the Voice page.

### rl:voice.noise - Noise suppression
- **Players get:** steady background noise (fan, hum, room tone) removed before the gate.
- **Config:** `voice_noise_suppression` = true; `voice_noise_suppression_level` = "Standard"; levels Low
  15 dB, Standard 30 dB, High 45 dB, Extreme 60 dB.
- **Rewrite:** RNNoise or a speexdsp-equivalent suppressor through P/Invoke, mapped to the same four levels
  (maximum attenuation). Runs before the activation gate.
- **Priority:** P1.
- **Done (2026-10-28):** a managed spectral Wiener suppressor instead of a native library (`VoiceNoiseSuppressor`),
  the four levels as gain floors, before the gate.

### rl:voice.echo - Echo cancellation
- **Players get:** play on speakers without sending everyone's voices back (experimental).
- **Config:** `voice_echo_cancellation` = false.
- **Rewrite:** we generate the voice playback mix ourselves, so it is the far-end reference for an echo
  canceller (speexdsp or WebRTC AEC3). Voice only, like Reloaded.
- **Priority:** P2.
- **Done (2026-10-28):** a managed partitioned-block frequency-domain adaptive filter (MDF's method,
  `VoiceEchoCanceller`, about 200 ms of tail) against the voice mix the game plays, plus residual echo suppression in
  the noise stage; off by default.

### rl:voice.jitter - Jitter buffer
- **Players get:** a trade between delay and crackle.
- **Config:** `voice_jitter_frames` = 4 (Ryan, first-run) / 2 (Ryan's mapping copy); range 1 to 12.
- **Rewrite:** per-talker jitter buffer in 20 ms frames, setting range 1 to 12 (default 3), Opus PLC and FEC
  on gaps, re-buffer after an underrun. Optional adaptive mode later.
- **Priority:** P0 (fixed size), P1 (setting).

### rl:voice.mix - Volume, mute, indicators and per-player control
- **Players get:** output volume; self mute; an overlay listing who is speaking per team, where a click
  mutes a player and the scroll wheel sets their volume; "Your microphone is muted" state.
- **Evidence:** CFG; RC `##VoiceSpies`, `##VoiceMercs`, "Muted", "Speaking", "Audible", "Click to %s,
  scroll to adjust" (RC@0x33a978..0x33aa00); the game's own "Allow VoiceChat" profile setting.
- **Config:** `voice_output_volume` = 1.0; `voice_muted` = false.
- **Rewrite:** one `AudioStreamGenerator` player per talker on a `Voice` bus; per-player volume and mute
  stored by player id for the session; speaking indicators on the HUD; the stock "Allow VoiceChat" switch
  maps to a master enable. Host-side voice ban (`bVoiceBanned`) as an admin action.
- **Priority:** P0 (mute, volume, indicator), P1 (per-player volume).

### rl:voice.test - Microphone test
- **Players get:** a level meter and a record-then-play-back test of exactly what others hear.
- **Evidence:** RC "Test my microphone", "Recording - speak now", "Playing it back", "Nothing was
  captured" (RC@0x33d028..0x33d0cc).
- **Rewrite:** run the full send chain (suppression, gain, gate, Opus encode and decode) into a loopback
  buffer and play it back.
- **Priority:** P1.

### rl:voice.routing - Who hears whom
- **Players get:** the stock team rules plus the Enhanced modes.
- **Evidence:** stock names above; INT voice modes; RC exec `dSetAllowVoiceChat`, `SetVoiceChatVolume`.
- **Rewrite:** server-side routing table rebuilt on events (team change, death, grab, downed, hack comms,
  lobby or match). Port the stock chatter rules from SBase (`VoiceChatterInfo`, `bCanTalkToGuy`, Hack Comms)
  and the Enhanced mode switch. Cap simultaneous talkers per recipient (for example 4) to bound upload.
- **Priority:** P0 for team routing in the match; P1 for grab talk, downed talk, hack comms, lobby voice.
- **Open:** `SetVoiceMask` and `VOICEMASK` in the stock executable suggest voice masking (an Xbox Live
  feature); confirm whether Versus PC uses it. Whether proximity talk is positional.

## 6. Display and graphics

The rewrite renders with its own Godot pipeline, so the D3D8-era fixes become requirements on our
Faithful renderer rather than ports.

### rl:gfx.widescreen - Widescreen 3D and the mercenary FOV cap
- **Players get:** un-stretched 16:9 for spy and merc views; merc first-person FOV capped for comfort and
  fairness.
- **Evidence:** RL11, RL12, RLF; CFG comments; console `widescreen`, `ws_fov` (RC@0x338c08..0x338cfc); ENH
  3.4 notes that 112 keeps the 4:3 vertical view at 16:9; RC "(21:9 stretched)" monitor label (support stops
  at 16:9).
- **Config:** `widescreen` = true; `ws_fov` = 112.0 (Ryan, first-run), doc default 105, maximum 112.
- **Stock:** 4:3 projection stretched to the screen.
- **Rewrite:** `Camera3D.keep_aspect = KEEP_HEIGHT` (Hor+). Store UE2 FOV values as 4:3 horizontal FOV and
  convert to vertical once in the camera port. Merc first-person FOV clamp `ws_fov` (max 112 at 16:9; define
  the clamp as horizontal FOV at the current aspect). Ultra-wide: allow Hor+ but apply the same merc clamp;
  spy third-person policy is a decision for Ryan. `widescreen=false` reproduces the stretched look.
- **Priority:** P0.
- **Open:** spy third-person FOV at 21:9 (fairness); exact stock merc 4:3 FOV value (from the camera port).

### rl:gfx.hud - Widescreen HUD and Enhanced Reality icon size
- **Players get:** HUD not stretched and repositioned for the resolution (v3.0); smaller ER icons in
  widescreen; ER same size at 16:9 as at 4:3 (v1.2).
- **Evidence:** CFG `widescreen_hud`, `ws_er_small`; RC `HudScreenRight`, `HudScreenBottom`,
  `<NAME>IconEnemyArrow{}</NAME>` (Magma element override).
- **Config:** `widescreen_hud` = true; `ws_er_small` = true.
- **Stock:** 640x480 HUD stretched horizontally.
- **Rewrite:** Magma HUD player (plan) lays out on a 640x480 virtual canvas scaled by height; elements
  anchored to the left, centre or right edge using the same groups as Reloaded (measure from screenshots at
  16:9). ER world icons scale by viewport height. `widescreen_hud=false` stretches as stock.
- **Priority:** P0 (simple HUD in the slice), P1 (full Magma HUD).
- **Open:** the per-element anchor table; derive from reference screenshots at 4:3 and 16:9.
- **Done (2026-10-28):** `widescreen_hud` and `ws_er_small` are their own settings (Display page); before, both
  followed `widescreen`.

### rl:gfx.modes - Resolution, refresh, display mode, monitor, window position and render scale
- **Players get:** every driver resolution (v1.1); highest refresh rate forced; Steam Deck native
  resolution; exclusive fullscreen, borderless fullscreen or windowed; monitor choice; window placement
  (top left, centred, stretched with bars); option to blank the border each frame; internal render scale
  0.5 to 1.0 in windowed and borderless.
- **Evidence:** RL11, RL20, CFG comments, RC display page texts (RC@0x33d164..0x33d380).
- **Config:** `force_max_refresh_rate` = true; `display_mode` = "BorderlessFullscreen" (options
  "Fullscreen", "BorderlessFullscreen", "Windowed"); `display_monitor` = 1; `window_position` =
  "Stretched" ("Default", "Centered", "Stretched"); `blank_border_every_frame` = false;
  `resolution_scale` = 1.0; `z_resolution_seeded` = true (resolution seeded from the desktop once; Ryan's
  `Default.ini` holds 2560x1440, the fresh one 640x480). Retired: `labs_borderless_fullscreen` (CFG36).
- **Stock:** limited mode list, fixed refresh, flicker during profile selection and launch (fixed in v1.1).
- **Rewrite:** Godot `DisplayServer` window modes (exclusive fullscreen, fullscreen, windowed),
  `window_set_current_screen`, refresh from the OS. We do not switch display modes; a lower "game
  resolution" becomes `Viewport.scaling_3d_scale` with the HUD at native resolution, which replaces the
  stretched-with-bars case. Keep the key names, map `window_position` to placement in windowed mode.
- **Priority:** P0 (mode, monitor, scale), P1 (the rest).

### rl:gfx.fps - Frame-rate caps per team and high frame-rate support
- **Players get:** smooth play up to 240 fps without gameplay bugs; separate caps for spy and merc
  because spy movement glitched above 120 fps; co-op moves forced to 60 fps while running.
- **Evidence:** RL11 (`fps_client`, `fps_host`), RL12 (replaced by `fps_spy`, `fps_merc`), RL20 (default
  merc 120, spy 60), RC limits 30 to 240 and the spy warning (RC@0x338708..0x338820), PCGW (stock Versus
  locked to 30).
- **Config:** `fps_spy` = 240 (Ryan) / 120 (first-run); `fps_merc` = 240 (Ryan) / 120 (first-run).
- **Stock:** 30 fps lock; above it, co-op moves, sniper sway, soft bodies and more break.
- **Rewrite:** sim fixed at 60 Hz with the UE2 sub-step rule; render rate independent and interpolated.
  Keep `fps_spy` and `fps_merc` as render caps (`Engine.max_fps`), range 30 to 360 plus unlimited, default
  matching the monitor. Regression scenario: the same inputs at render 30, 60, 144, 240 give the same sim
  hash.
- **Priority:** P0 (architecture), P1 (per-team caps).

### rl:gfx.pacing - Frame pacing and input latency
- **Players get:** steadier frame timing and less input lag (v1.1); "Reduce input lag" with Off, On and
  Ultra (like a driver low-latency mode); a spin-wait limiter kept as an escape hatch.
- **Config:** `low_latency_mode` = "On"; `alternate_frame_timing_mode` = false; retired
  `use_modern_frame_timing` (CFG36).
- **Rewrite:** Godot vsync modes; frame queue depth (`rendering/rendering_device/vsync/frame_queue_size`)
  and swapchain image count mapped to Off (default), On (queue 1), Ultra (queue 1 plus waiting on the GPU
  before sampling input); sample input as late as possible before building the tick input. Spin-wait mode
  not needed.
- **Priority:** P1.
- **Open:** whether Godot 4.7.2 accepts a frame queue size of 1 on Vulkan and D3D12; measure latency.

### rl:gfx.fpscounter - Frame-rate readout
- **Players get:** fps in the top right corner with the slowest and fastest 1% of the last few seconds.
- **Config:** `show_fps` = true (Ryan) / false (first-run).
- **Rewrite:** ring buffer of frame times (5 s), show current, 1% low and 1% high.
- **Priority:** P1 (cheap).

### rl:gfx.aa - MSAA and SMAA
- **Players get:** MSAA 2x/4x/8x for geometry edges, SMAA for alpha-tested fences and foliage; they combine.
- **Evidence:** CFG comments; RC SMAA HLSL and error messages; ENH notes on dgVoodoo MSAA artefacts earlier.
- **Config:** `msaa` = true (Ryan) / false (first-run); `msaa_samples` = 8 (Ryan) / 4 (first-run);
  `smaa` = true (Ryan) / false (first-run).
- **Stock:** no AA with HDR in single player; Versus D3D8 path without AA (PCGW).
- **Rewrite:** `Viewport.msaa_3d` and Godot's SMAA (screen-space AA, available since Godot 4.5). Alpha
  to coverage for alpha-tested materials when MSAA is on. Must not disturb the Faithful compositor (glow,
  vision modes): apply SMAA last.
- **Priority:** P1.

### rl:gfx.filtering - Anisotropic filtering
- **Config:** `filter_level` = 16 (Ryan) / 4 (first-run, doc default 4), range 0 to 16; console
  `filter_level`.
- **Stock:** only the ini value `LevelOfAnisotropy=1` in `[D3DDrv.D3DRenderDevice]` (INI), no menu option.
- **Rewrite:** project setting `rendering/textures/default_filters/anisotropic_filtering_level`, materials
  use the anisotropic filter variants where UE2 used trilinear.
- **Priority:** P1.

### rl:gfx.shadows - Dynamic shadow quality, optimised shadows, smooth edges
- **Players get:** shadow maps up to 16K; per-light shadow resolution scaled by light area and camera
  distance (faster, evenly sharp); softened edges (8-tap disc filter); warnings for high VRAM.
- **Evidence:** RL20, CFG comments, RC console texts (RC@0x3389c0..0x3390f4); PCGW (the single-player
  executable uses a 2048 shadow map that breaks at wide resolutions); retired `shadow_quality_terminus`
  (Terminus was too heavy).
- **Config:** `shadow_quality` = 8.0 (Ryan) / 3.0 (first-run), doc default 2.0, range 0.5 to 16.0 (8.0 =
  8K); `shadow_optimized` = true; `shadow_edge_smoothing` = true.
- **Rewrite:** the merc flashlight and dynamic lights use Godot shadowed lights; map `shadow_quality` to
  the positional shadow atlas size (1024 x value, clamped to GPU limits) and `shadow_edge_smoothing` to the
  soft shadow filter quality. `shadow_optimized` = per-light atlas quadrant assignment by screen size.
- **Priority:** P0 for the merc flashlight shadow, P1 for the settings.

### rl:gfx.lod - Model detail at distance
- **Players get:** higher quality player models at distance ("Use higher-quality player models at a
  distance").
- **Config:** `lod` = 10.0 (Ryan, first-run), stock 1.0.
- **Rewrite:** pawn LOD switch distance multiplied by `lod`; Godot `mesh_lod_threshold` for static meshes.
- **Priority:** P1.

### rl:gfx.renderfix - Rendering fixes for modern GPUs
- **Players get:** the merc flashlight, lighting and some effects render correctly without dgVoodoo or
  3D-Analyze; godray beams (flashlight beam) restored; better lighting quality; D3D9 path for G-Sync and
  FreeSync; Linux fixes (flashlight, red lighting); DXVK version detection with a legacy switch.
- **Evidence:** RL11, RL12, RL20, RLF, PCGW ("Missing torchlight/flashlight in Versus mode"); RC shader
  assembly rewriting (ps_1_x to ps_1_4 conversion, oFog/oPos swizzle fixes), `SCCT_Versus_shadow_shader_error.txt`,
  `d3dx9_43.dll` requirement, `wine_get_version`, `legacy_dxvk_graphics`.
- **Config:** `legacy_dxvk_graphics` = false; retired `flashlight_rendering_fix` (RL11).
- **Rewrite:** n/a as a port (no D3D8); requirement on the Faithful renderer: flashlight light and beam,
  projected light textures, shadow behaviour per `docs/EFFECTS.md` and the light rules. Reference captures
  for goldens must come from the Reloaded-fixed game (they do, since the reference install runs it).
- **Priority:** P0 (flashlight and its beam are in the slice), P1 (other beams).

### rl:gfx.animtex - Animated texture and shader speed
- **Players get:** animated textures and shaders run at the intended speed at high fps (no flicker at
  100+ fps).
- **Config:** `animation_fix` = false (Ryan's SVM copy) / true (first-run and every other copy).
- **Rewrite:** n/a by design: material animation (panners, oscillators, MaterialSequence) driven by level
  time. Ryan turned it off in one copy; ask whether something looked wrong.
- **Priority:** P0 (by construction, test it).

### rl:gfx.system - Power throttling, priority and affinity
- **Players get:** Windows 10+ power throttling off for the game (experimental); command-line process
  priority and CPU affinity; Windows 7 compatibility (v2.0).
- **Evidence:** CFG `throttle_stop`; RC switches `-no_throttle`, `-priority` (value such as `medium`),
  `-affinity` (RC@0x3358dc..0x335968); PCGW (stock slow motion on multi-core CPUs, `UseAlternateTimer`).
- **Config:** `throttle_stop` = false (Ryan's SVM copy) / false (first-run).
- **Rewrite:** Windows client calls `SetProcessInformation` to opt out of power throttling when the
  setting is on; priority and affinity as command-line options of the client and server. Timer issues
  n/a (Stopwatch in the view layer, VirtualClock in the sim).
- **Priority:** P2.

## 7. Input

### rl:input.mouse - Modern mouse input
- **Players get:** high polling rate mice read correctly (no need for 125 Hz), no negative acceleration,
  sensitivity independent of frame rate, fast flicks no longer ignored, better mouse in sticky cam and Cam
  Net views and on the Missile Strike spotlights.
- **Evidence:** RL11, RL20, ENH 3.4/3.5, PCGW (stock negative acceleration, reticle bloom tied to the
  in-game sensitivity slider). Stock binds `IK_MouseX=Axis aMouseX Speed=6.00` (INI), profile
  `MouseSensitivity=40.0`.
- **Rewrite:** Godot relative mouse motion in captured mode (Windows raw input; verify on 4.7.2),
  accumulated per render frame and handed to the sim as per-tick `aMouseX`/`aMouseY` sums, no smoothing,
  no per-frame clamp. Reticle bloom must use the turn rate, not the sensitivity slider (check what Reloaded
  does). Calibrate so that Ryan's current values give the same yaw units per count as the reference game.
- **Priority:** P0.
- **Resolved:** the formula is decoded in `spec/native/reloaded-features-from-code.md` rule 23a: a count
  turns 3 x multiplier rotation units, independent of the frame rate and the window size.

### rl:input.sens - Per-team and per-view sensitivity
- **Players get:** separate spy and merc profiles for mouse and controller: spy free look (gun holstered),
  gun look (gun drawn), sticky camera and Cam Net, binoculars; merc look, sniper scope, vertical sniper
  scale; a legacy sniper mode that restores the slower stock vertical scope speed (stock vertical was fixed
  at 40.8% of horizontal).
- **Evidence:** RL20, RL30, CFG, RC settings page texts (RC@0x33b6ec..0x33b898).
- **Config:** `controls_spy_config_kbm` = {cam 1.0, clutch_one 0.0 (off), free_look 1.4, free_look_response
  Linear, gun_look 1.0, gun_look_response Linear, scope 0.75, swap_ls_lb false, swap_rs_rb false, vertical
  3.5}; `controls_merc_config_kbm` = {cam 1.0, clutch_one 0.0 (off), clutch_two 0.0 (off), look 1.0,
  look_response Linear, scope 0.75, scope_vertical 2.45, swap_ls_lb false, swap_rs_rb false, vertical 1.0};
  controller variants in section 12. Retired console keys `sens`, `sens_cam` / `sens_camera`, `scope_sync`
  (RL11, RL12, ENH 3.4).
- **Rewrite:** settings groups `input.spy.kbm`, `input.spy.pad`, `input.merc.kbm`, `input.merc.pad` with
  the same fields; the active group follows the pawn class; the view mode (free, gun, cam, scope,
  binoculars) picks the multiplier in the PlayerInput port.
- **Priority:** P0 (team split and look multipliers), P1 (scope and binocular vertical, legacy mode).
- **Open:** meaning of `vertical` (3.5 for spy, 1.0 for merc) and how `scope_vertical` 2.45 relates to the
  40.8% stock ratio (2.45 x 0.408 = 1.0, so 2.45 likely restores 1:1). Confirm against the settings UI.

### rl:input.clutch - Sensitivity clutches
- **Players get:** a held button that applies a temporary multiplier (one for spy, two for merc).
- **Config:** `clutch_one`, `clutch_one_enabled`, `clutch_two`, `clutch_two_enabled` in the groups above;
  controller binds "Right Clutch" (spy), "Left Clutch" (merc).
- **Rewrite:** input actions `clutch_1`, `clutch_2`; multiplier applied before the response curve.
- **Priority:** P1.

### rl:input.curves - Response curves
- **Players get:** Linear, Soft (inner 75% linear, outer 25% ramps to 2x), Medium (2.5x), Aggressive (3x).
- **Evidence:** RC@0x33b088..0x33b15c.
- **Config:** `*_response` fields: "Linear", "CurveSoft", "CurveMedium", "CurveAggressive".
- **Rewrite:** same curve family for stick look; mouse stays linear unless chosen.
- **Priority:** P1.

### rl:input.menu - Menu cursor speed
- **Config:** `sens_menu` = 0.75.
- **Rewrite:** menus are Godot UI with the OS cursor; keep the setting only for the gamepad-driven cursor.
- **Priority:** P2.

### rl:input.keyboard - Separate spy and merc keyboard layouts
- **Players get:** different key bindings per team.
- **Evidence:** CFG; stock `Default_kbd_0.ini` and `Default_kbd_1.ini` (identical in the fresh install);
  RC `Default_kbd_{}.ini`; profile `SelectedKeyboard=0`.
- **Config:** `use_split_spy_merc_keyboard` = true (Ryan) / false (first-run).
- **Rewrite:** input map seeded from both files (plan), one layout per team, a toggle to share one layout.
  Stock aliases (`AliasHaut`, `AliasBas`, `AliasGauche`, `AliasDroite`, `GadgetQuickChoice`, `SwitchHeadset`)
  map to named actions.
- **Priority:** P0.
- **Done (2026-10-28):** the share toggle (Keys page): off, both sides load the spy's layout and user file. Our
  default is on (split), so layouts saved before the toggle keep both sides.

### rl:input.pad - Native controller support
- **Players get:** XInput, PlayStation, Switch and other pads (SDL mapping database) with full 360 degree
  movement, team-specific controls, deadzones, an automatic run threshold for the spy (or a manual speed
  toggle), analogue slow walk, optional 8-way snapping per team, vertical aim scale, swap stick clicks with
  bumpers, hot plug messages, and a first-run prompt to enable it.
- **Evidence:** RL30, RLF, PCGW (stock Versus does not see sticks or triggers), CFG, RC settings and bind
  page texts, "Gamepad connected", "Would you like to enable native controller support?".
- **Config:** `controller_enabled` = false; `controller_left_stick_enabled` = true;
  `controller_right_stick_enabled` = true; `controller_binds_enabled` = true;
  `controller_left_thumb_deadzone` = 0.25; `controller_right_thumb_deadzone` = 0.15;
  `controller_right_thumb_vertical_sensitivty` (sic) = 0.75; `controller_walk_threshold` = 0.95;
  `controller_left_stick_slow_walk_enabled` = true; `controller_left_stick_8_directional_spy` = false;
  `controller_left_stick_8_directional_merc` = false. All but the 8-way keys are marked experimental.
- **Stock:** `UseJoystick=False`; buttons only.
- **Rewrite:** Godot joypad input (SDL3 backend and mapping database in Godot 4.5+). Radial deadzone with
  rescale, curves, 8-way snap (movement only), walk threshold and slow walk feeding the UE2 movement input
  (aForward/aStrafe and speed control), vertical scale. Same keys under `input.pad`.
- **Priority:** P1 (Ryan wants it; not needed for the slice gate).
- **Decoded (2026-11-01):** native default bindings in `Reloaded.Core.dll` at `0x100b3e50` (spy) and
  `0x100b4f30` (merc), with the trigger comparator at `0x100b6090` and the 200 ms hold test at `0x100b5bd1`.
  Source: Enhanced SCCT Versus v4.0 Public Beta 3, DLL SHA-256
  `6b9d4444e7e05f7c04767509599df97c0f0fd9fc61686821cc8333066995dc46`.
  First-time rewrite profiles use these layouts, independent action dispatch and modifier/hold resolution;
  `docs/HUD.md` lists the controls. Existing profiles retain their saved behavior. Merc launch combinations
  shown in Reloaded's UI are absent from its native initializer, so they remain unassigned.
  Action callbacks distinguish armed slot launch (`0x100b0750`) from unarmed quick-use, and short trigger
  releases from 200 ms holds (cloak, scoped breath, camera gas/exit and the crouched long-roll pulse).
- **Done (2026-10-28):** the left stick is an analog move (spec/native/input.md rule 9: full circle, analog slow walk,
  `slow_walk`), hot plug lines and the once-only first-run prompt. Swapping stick clicks with bumpers (2026-10-28):
  `swap_ls_lb` and `swap_rs_rb` in the controller groups (`controls_spy_pad`, `controls_merc_pad`), off by default;
  on the Controller page per side; the side's binds then read the other control (`GamepadInput`).

### rl:input.padbinds - Controller binds and hybrid input
- **Players get:** bind pages for spy (Use/Grab, Last Sticky Cam, Hack Comms, Use Gadget, Back To Wall,
  Main Menu, Draw Gun, Coop Move, Night Vision, Thermal Vision, Toggle Laser, Long Roll, Toggle Run, Select
  Gadget, Launch Gadget, Toggle Binoculars, Toggle Scoreboard, Manual Speed Toggle, Right Clutch), merc
  (Use/Melee, Toggle Flashlight, Left Clutch, Motion Vision, EMF Vision, Rate Of Fire) and lobby (Swap
  Team, Previous Map, Next Map). Hybrid mode: turn off native movement, aim or binds to combine with Steam
  Input, JoyToKey or AntiMicro.
- **Evidence:** RC@0x33bd80..0x33c544; RL30.
- **Rewrite:** Godot InputMap actions with per-team pad layouts; the three hybrid toggles map to the keys
  above.
- **Priority:** P1.

### rl:input.rumble - Controller vibration
- **Players get:** rumble on firing, taking damage, being tased, grabbed or caught in a blast; strength
  scale; trigger test.
- **Config:** `controller_rumble_enabled` = false; `controller_rumble_strength` = 1.0.
- **Rewrite:** `Input.start_joy_vibration` driven by SimEvents (fire, damage, tase, grab, blast) in the
  view layer.
- **Priority:** P2.

### rl:input.console - Console key and font
- **Players get:** console on the key left of 1 (tilde or backtick) instead of the stock key; legible
  console font.
- **Config:** `override_console_key` = true (UI wording: "Use Chaos Theory's original console key" is the
  inverse).
- **Rewrite:** developer console on the physical key left of 1 (scancode, layout independent), option for
  the stock key.
- **Priority:** P1.

### rl:input.ledgeassist - Ledge grab with Use in slow mode
- **Players get:** holding Use in slow mode (standing or crouched) grabs the ledge when falling off, even
  when moving diagonally. Important on pads, where a single pure direction is hard to produce.
- **Evidence:** RL30, ENH patch notes.
- **Stock:** the spy only grabs when exactly one direction is held.
- **Rewrite:** port the ledge test with this extra condition, behind the Reloaded rules set; verify with a
  traversal scenario on SteeE.
- **Priority:** P0 (SteeE has 220 ledge GEs; the rule changes traversal outcomes).

## 8. Gameplay fixes

Most fixes fall into five groups. **F** = fixed by construction (60 Hz sim, render rate independent;
still needs a regression scenario at several render rates). **R** = a changed rule to port, with numbers
taken from the reference game (which runs Reloaded). **N** = netcode design. **V** = view or HUD layer.
**S** = robustness.

| ID | Fix | Stock behaviour | Kind | Pri | Evidence |
|---|---|---|---|---|---|
| rl:fix.sniper_sway | Scope sway identical from 30 to 240 fps | sway grew with fps | F | P0 | RL11, RL12 |
| rl:fix.sniper_kick | Scope kick the same for every player, not tied to mouse sensitivity | up to 10x kick differences | R | P0 | RL11, ENH 3.4 |
| rl:fix.scope_zoom_fps | Merc scope consistent at high fps when zoomed | drift at high fps | F | P0 | RL20 |
| rl:fix.spy_jump_fps | Spy can jump over objects at high fps | jumps failed | F | P0 | RL11 |
| rl:fix.coop_fps | Co-op moves succeed (Reloaded caps 60 fps during them) | failed at high fps | F | P1 | RL11, RL12 |
| rl:fix.footclimb_fps | Spy foot climb works at all heights | failed at some heights | F | P0 | ENH 3.4 |
| rl:fix.softbody | Soft bodies (hanging fabric) settle | moved forever | F | P1 | RL11 |
| rl:fix.shake | Screen shake keeps its length and strength | lasted longer at high fps | F | P0 | RL11, ENH 3.4 |
| rl:fix.alttab | Game no longer runs fast when alt-tabbed | sped up, broke hosting | F | P0 | RL11 |
| rl:fix.camo_fps | Camo stays on during grab, hack, climb at high fps | switched off | F | P1 | RL20 |
| rl:fix.camnet_zoom | Sticky Cam and Cam Net zoom consistent at any fps | broke at high fps | F | P1 | RL20 |
| rl:fix.instas | Launched grenades keep their fuse | exploded on impact | N | P0 | RL11 |
| rl:fix.grenade_feet | Merc grenades no longer appear at his feet on clients | host at 2x fps caused it | N | P0 | RL11 |
| rl:fix.adrenaline_rubberband | No rubber-banding when a spy is shot | adrenaline boost desynced | N | P0 | RL11 |
| rl:fix.disk_pickup | Client spy can always pick up the disk | interaction rotation replicated wrong | N | P0 | RLF, ENH 3.5 |
| rl:fix.stickycam_fire_moving | Sticky cams fire while the spy moves | sometimes did not fire | R | P1 | RL11 |
| rl:fix.stickycam_stuck | No slow exit, no getting stuck, no delay before running after a sticky cam | all three happened | R | P1 | RL20 |
| rl:fix.stickycam_none_left | Launching with no cams left does not switch to an old cam | switched cams | R | P1 | RL30 (was `sticky_camera_fix`) |
| rl:fix.stickycam_east_sound | Activation sound plays for clients when fired east | silent | N | P1 | RL30 |
| rl:fix.camo_slot2 | Selecting Camo after leaving a sticky cam does not fire slot 2 | auto-used slot 2 | R | P1 | RL30 |
| rl:fix.grab_angle | Grabbing a merc from his left side is reliable | narrow angle | R | P0 | RL20 |
| rl:fix.jump_lockon | Jump lock-on only for enemies within 7.5 m vertically; works inside volumes | locked from far; failed in volumes | R | P0 | RL20, RL30 |
| rl:fix.ko_after_interrupt | Spy can be knocked out or elbowed after interrupting a computer or hack panel | immune | R | P0 | RL20 |
| rl:fix.slow_strafe | Standing slow-mode strafe no longer faster (proxy mines stop triggering) | diagonal faster | R | P1 | RL30 |
| rl:fix.disk_radar | Disk velocity threshold raised, spies no longer flash on radar climbing poles or ladders | brief radar blips | R | P0 | RL30 |
| rl:fix.crosshair_shift | Merc crosshair no longer shifts sideways | occasional shift | V | P0 | RL30 |
| rl:fix.motion_glass | Motion Vision ignores movers behind solid objects seen through glass; red vision no longer sees through some walls | false detections | V | P0 | RL20, RL21 |
| rl:fix.motion_box | Motion tracking yellow box aligns with the grey box | misaligned | V | P1 | RL20 |
| rl:fix.floodlight | Merc input on controllable floodlights (Missile Strike) | broken input | R | P1 | RL20, ENH 3.5 |
| rl:fix.f7_crash | No crash on F7 (strategic map) in maps without one | crash | S | P0 | RL20, ENH 3.5 |
| rl:fix.empty_name | (Reloaded itself crashes on an empty PlayerName in `GRI.PRIArray`) | n/a | S | P0 | MEM game-console-and-bots |
| rl:fix.scoreboard_persist | Scoreboard (Show Connected Players) state remembered between rounds | reset | V | P1 | RL21, ENH 3.5 |
| rl:fix.empty_team_start | Host can start with an empty team | blocked | R | P0 | RL30 (needed for bot and solo tests) |
| rl:fix.er_sound | Turning Enhanced Reality off no longer pins audio at that spot | stuck listener | V | P1 | RL20, ENH 3.5 |
| rl:fix.ambient | All players hear ambient sound | some had none | V | P1 | RL11 |
| rl:fix.tase_sound_repeat | Tase and sticky cam sounds do not repeat on clients | repeated | N | P1 | ENH 3.5 |
| rl:fix.camo_client_render | Camo never renders the spy uncloaked on clients | occasional | N | P1 | ENH 3.5 |
| rl:fix.flashlight_sniper | Sniper mode keeps the flashlight on (Enhanced builds) | turned off | R | P0 | RL20, ENH 3.5 |
| rl:fix.camo_detection | Camo not seen by security cameras or Motion Vision, not drawn in Motion Vision, not highlighted in red vision (Enhanced builds) | detected | R | P1 | RL20, ENH 3.5 |

Notes:
- "F" items still get a scenario: same inputs at render 30/144/240, sim hash equal, and the trace matches
  the reference recorded at 60 fps.
- "R" items need numbers. The 7.5 m lock-on height is given; the grab angle, disk velocity threshold and
  slow-strafe speed must be measured or read from the decompiled Enhanced script (some may be in script,
  some in Reloaded memory patches). Tag each constant `measured` or `decoded` in the subsystem spec.
- The camo and flashlight items apply only on Enhanced builds; since our target is Enhanced, they are on.
- Decoded from Reloaded.Core (2026-10-28; spec/native/reloaded-hooks.md rules 29 to 37) and ported: jump_lockon
  (physics-falling.md section 4 rule 3), ko_after_interrupt (state entry hook, rule 35), disk_radar (200 uu/s,
  disk-flag.md rule 34), crosshair_shift (fixed reticle spot, merc-reticle.md rule 3), camo_detection for security
  cameras (security-camera.md rule 8; the motion-vision draw hook is decoded but not ported). grab_angle: no
  Reloaded patch touches the grab test (CanGrabPawn is script, iGrabAreaShift 7200 and fGrabHalfAngle 0.5 in the
  Enhanced 4.0 package, ported as is), so the fix is in the shipped script or not identifiable without the stock
  package. stickycam_none_left and camo_slot2: no Reloaded code; the Enhanced 4.0 script is ported as is.
  stickycam_east_sound: Reloaded sets SStickyCam's default iReplicatedPitch and iReplicatedYaw to 12345678
  (Reloaded.Core 0x1000f790) so that zero angles still replicate; not ported (spec/equipment/sticky-cam.md).

## 9. Anti-cheat and integrity

What Reloaded does (observed, for context only):
- Process mitigations: DEP on by default (`security_dep` = true), optional Arbitrary Code Guard
  (`security_acg` = false; breaks some capture tools and needs `d3d8.dll` removed) (RL11, CFG).
- A loaded-module check: DLLs loaded from the game folder must be on an allow list (the runtime, audio,
  D3DX/D3DCompiler, OpenAL Soft and DSOAL drivers, Vulkan loader for DXVK, VC++ runtime); anything else
  shows "Unsupported file modification detected" with the file list and the game closes (RC@0x337a88..
  0x337f32).
- An integrity gate over the game binary and the mod, on disk and in memory, which in beta builds asks for
  the CD key again on failure (RC@0x337030).
- Version gating by build (stock or Enhanced, mandatory updates).
- Master server bans and temporary timeouts ("Banned", "You are on a temporary timeout"), with a machine
  fingerprint built from WMI values (system UUID, disk, board, BIOS serials, CPU id, MAC; RC@0x340988..
  0x340d3c).
- A crash handler writing `SCCT_Versus_crash.<host>.log` with registers and stack (LOGS).

The rewrite never touches these checks. It provides its own protection:

### rl:ac.authority - Server authority and validation
- **Rewrite:** the server owns all gameplay state; clients send inputs and saved moves; traversal attach
  points are validated against GE data (plan); fire origins validated within a tolerance (and within the
  lag-compensation ceiling); rate limits on RPCs, chat and voice; voice routed by the server only.
- **Priority:** P0.

### rl:ac.handshake - Build and content checks
- **Rewrite:** handshake protocol hash and content manifest SHA-256 (plan), with the differing file named;
  the master lists build ids so mismatches are visible before joining.
- **Priority:** P0.

### rl:ac.bans - Bans and timeouts without hardware fingerprints
- **Rewrite:** each install creates an Ed25519 key pair; the public key is the player id for bans, admin
  lists and voice bans; the master can time out abusive hosts by key and IP. No hardware serials are
  collected (privacy).
- **Priority:** P1 (P0 for the invite-key gate on the private master).

### rl:ac.release - Signed releases and content
- **Rewrite:** release manifests signed by Ryan's key; the client verifies its own content packs on start
  and refuses to list on the master when unsigned (local dev builds still play LAN and direct).
- **Priority:** P2.
- **Not ported:** DEP and ACG settings (DEP is always on for 64-bit .NET; ACG conflicts with a JIT), the
  CD-key gate, the module allow list.

### rl:ac.crash - Crash reports
- **Rewrite:** unhandled exceptions write `crash-<utc>.log` (managed stack, build id, map, mode) next to
  the settings; native Godot crashes keep Godot's handler.
- **Priority:** P1.

## 10. Quality of life: server browser, lobby, chat, HUD

### rl:qol.settingsui - In-game settings overlay
- **Players get:** a graphical settings window (F12, also Ctrl+O, or a button in menus) with pages
  including Input, Display, Performance, Voice and About, a Save button and Discard; "Press Save, then restart"
  where needed; a welcome dialog on first run; settings import from a previous install; a recovery dialog
  when the config has invalid JSON (restore defaults or quit).
- **Evidence:** RL20, RL21, ENH 3.5, RC@0x33ac8c, RC@0x33b014..0x33b050, RC@0x332f90..0x3331f0.
- **Config:** `z_first_run` = false; `z_main_menu_count` = 30 (main menu visits, drives one-off prompts).
- **Rewrite:** native Godot options screens with the same groups; F12 opens them in game as an overlay;
  settings file validated on load with a backup copy and a reset prompt; importer for `SCCT_Versus.config`.
- **Priority:** P1 (P0: a minimal options screen for display, sensitivity, voice).

### rl:qol.lobbymaps - Lobby map picker and playlists
- **Players get:** in the lobby, previous and next arrows plus a drop-down with categories and search;
  playlists Standard, Extended, Classic, 3v3 and Merc Deathmatch (each with a description and game mode),
  plus "All installed maps"; variant words (Enhanced, Enh, Phoenix's, Snake, UMP, PS2, Covert) dimmed and
  ignored for sorting so variants sit with the original; option to show the variant word after the name.
- **Evidence:** RL30, CFG `map_playlists_v3`, RC@0x33aa9c..0x33ab20.
- **Config:** `active_map_playlist` = "All" (Ryan) / "Standard" (first-run); `active_map_playlist_mdm` =
  "Merc Deathmatch"; `map_playlists_v3` (internal); `map_variant_words`; `map_variant_words_last` = true;
  `z_cache_level_names` (map code to display name cache). Retired `map_playlists` (CFG36).
- **Rewrite:** playlists as data (`content/playlists.json`, shipped, overridable), map display names from
  each map's settings package; lobby UI with arrows, search and categories; server rotation uses the active
  playlist. See `spec/enhanced/FEATURES.md` for the playlist contents.
- **Priority:** P1 (P0: pick map from a list for the slice).

### rl:qol.chatcmds - Chat commands
- **Players get:** `!map <name>` (5-letter map code), `!next`, `!prev` (`!back` in the binary),
  `!resign` (forfeit the round by spending your lives; death message "has resigned"), `!pings`, `!lagcomp`,
  practice commands. Commands run on dedicated servers for everyone, on listen servers for the host.
- **Evidence:** ENH README "Server Commands", RL30, RC@0x339430..0x3396d0, lobby buttons send
  `rsay !map`, `nsay !next`, `nsay !back`.
- **Rewrite:** server-side chat command router with permission levels (everyone, host, admin key), help
  text, and the same names (accept both `!prev` and `!back`).
- **Priority:** P1 (`!map`, `!next`, `!resign` useful for the slice tests).

### rl:qol.console - Console commands
- **Players get (v4.0d):** `help`, `current` (all settings), `quit`, `widescreen`, `ws_er_small`,
  `listed`, `ws_fov`, `fps_spy`, `fps_merc`, `filter_level`, `shadow_quality`, `shadow_optimized`,
  `shadow_edge_smoothing`, `update`, `enh_update`, `rmap_next`, `rmap_prev`, `resign`, `pings`,
  `practice`, `map <name>` (host, main lobby page only), Discord shortcuts (`discord_ctss`, `discord_enh`,
  `discord_wr`). Older builds had `fps_client`, `fps_host`, `sens`, `sens_cam`, `sens_menu`,
  `scope_sync` (RL11, RL12).
- **Rewrite:** a developer console with typed commands bound to settings keys (`set <key> <value>`,
  `get <key>`) plus the named shortcuts above where useful. Discord links n/a.
- **Priority:** P1.

### rl:qol.lobby - Lobby behaviour
- **Players get:** join notification sound; server name shown to clients at the bottom of the lobby; host
  can start with an empty team; "Waiting for players (n/m)" and ready-up state; hosting errors and
  warnings in the chat box (UPnP, port in use, master status).
- **Evidence:** RL30, RC@0x33ae68..0x33aed0, RC "Could not start. Not all players are ready".
- **Rewrite:** Lobby scene with system messages, ready state, server name, join sound, start rules.
- **Priority:** P0 (lobby to match flow in M6), P1 (polish).

### rl:qol.scoreboard - Scoreboard
- **Players get:** scoreboard (F10, Show Connected Players) on by default and remembered; ping per player;
  split into one column per team at a chosen team size; kills, deaths, lives, objectives; Merc Deathmatch
  result lines ("{} wins with {} kills", "Draw at {} kills"); elapsed time.
- **Config:** `show_scoreboard` = true; `show_ping` = true (Ryan) / false; `scoreboard_split_team_size` = 3
  (1 to 4).
- **Rewrite:** scoreboard from GRI/PRI data in the HUD; same options.
- **Priority:** P1 (P0: basic team scores and lives for the slice).

### rl:qol.location - Location on the HUD
- **Players get:** the current zone or area name on the HUD on maps that define them (Enhanced labelled
  nearly all maps; SteeE has "Red Building", "Boss's Office" callouts).
- **Config:** `show_location` = true.
- **Rewrite:** from ZoneInfo or location volume names in the SVMap gameplay data.
- **Priority:** P1.

### rl:qol.hackalpha - Enhanced Reality hack icon opacity
- **Players get:** ER objective icon opacity while a hack is in progress, 20% to 100%.
- **Config:** `hack_alpha` = 178 (about 70%, Ryan and first-run).
- **Rewrite:** HUD setting, 0 to 255 stored, shown as a percentage.
- **Priority:** P1.
- **Open:** the settings page says "Default is 100"; check whether that means 100% or a different scale.

### rl:qol.crosshair - Classic crosshair position
- **Players get:** merc crosshair centred (Enhanced default) or the original off-centre position.
- **Config:** `classic_crosshair` = false.
- **Rewrite:** HUD option; centred by default.
- **Priority:** P0 (centred), P1 (option).
- **Done (2026-10-28):** decoded (Reloaded.Core 0x100a9140 / 0x100a9160): the reticle's page instance at (257, 179),
  or (254, 182) with the option; Options, HUD page, `classic_crosshair` (spec/hud/merc-reticle.md rule 3).

### rl:qol.matchlog - Match log
- **Players get:** `ChaosTheory.log` with match info in UTC (mode, alternate rounds, time limit, number of
  objectives) and end reasons ("All objectives taken", "Mission timer elapsed", "Team eradicated"); this
  feeds community stats (SC-Panel: seasonal skill ratings, round archive, live servers).
- **Evidence:** RC `[MatchInfo]`, `UTCTime=%s`, `Mode=Story`, `TimeLimit=%d`, `NbObjectives=%d`; ENH 3.5;
  SCP.
- **Rewrite:** JSON lines match log per round (UTC, map, mode, settings, players by key, teams, events,
  end reason) on the server; optional HTTPS push to a stats endpoint configured by the host.
- **Priority:** P1.

### rl:qol.menuskip - Skipped stock pages
- **Players get:** no ESRB notice, no Ubi.com pages or terms of use (Enhanced and Reloaded hook these
  pages: `PandoraESRBMessageMenu`, `UserAgreement`, `GSConnection`).
- **Rewrite:** n/a; our menus never had them.
- **Priority:** n/a.

### rl:qol.misc - Smaller items
- Steam Deck native resolution (covered by `rl:gfx.modes`).
- Notification when a new Reloaded version exists (covered by `rl:net.version`).
- Donation and about pages, Discord links: n/a.
- Launcher checks (wrong folder, Ubisoft Connect setup script): n/a.

## 11. Audio

### rl:audio.eax - EAX restoration
- **Players get:** environmental reverb (EAX) restored automatically at launch (registry entries rewritten
  when missing; can be turned off). The Enhanced install ships Creative ALchemy as `dsound.dll` (DirectSound
  to OpenAL with EAX 1 to 4; `dsound.ini` Buffers=4, Duration=5, MaxVoiceCount=128) plus `eax.dll`; the
  allow list also names OpenAL Soft and DSOAL drivers.
- **Evidence:** RL20, ENH 3.5, INI (`UseEAX=True`, profile `bEAX=True`), PCGW (EAX 2 and 3; DSOAL needs
  `boost=-6`).
- **Rewrite:** per-zone reverb on a Godot bus (`AudioEffectReverb`) selected by the listener's zone, using
  the zone reverb settings from the content pipeline (EAX environment presets converted to reverb
  parameters); occlusion as a low-pass. HRTF is not built into Godot (open).
- **Priority:** P1.

### rl:audio.cache - Audio settings available at boot
- **Players get:** the last profile's music and effects volumes apply before a profile loads.
- **Config:** `z_cache_music_volume` = 1e-05 (Ryan's music is off), `z_cache_sound_volume` = 0.5.
- **Rewrite:** volumes live in the settings file, not only in profiles.
- **Priority:** P1.

### rl:audio.join - Join sound
- Covered by `rl:qol.lobby`.

### rl:audio.thread - Audio thread priority
- **Evidence:** RC "Pro Audio" next to `ntdll.dll` and `kernel32.dll` (likely MMCSS task registration).
- **Rewrite:** register the voice capture and encode thread with MMCSS "Pro Audio" on Windows if needed.
- **Priority:** P2.
- **Done (2026-10-28):** the microphone clean-up runs on a voice thread registered as "Pro Audio"
  (`ProAudioThread`, `VoiceDspThread`); the encoder stays on the main thread with the gate.

## 12. Appendix A: every key in Ryan's SCCT_Versus.config (v4.0d)

Values: Ryan (CFG) / first-run (CFGREF, v4.0b). "same" means equal. Rewrite column gives the settings key or
"n/a".

| Key | Ryan | First-run | Meaning | Rewrite |
|---|---|---|---|---|
| active_map_playlist | "All" | "Standard" | Lobby playlist for objective modes | lobby.playlist |
| active_map_playlist_mdm | "Merc Deathmatch" | same | Playlist used for Merc Deathmatch | lobby.playlist_mdm |
| alternate_frame_timing_mode | false | same | Spin-wait the whole frame wait | n/a |
| animation_fix | false | true | Animated textures at intended speed at high fps | n/a (by design) |
| blank_border_every_frame | false | same | Clear the border in stretched borderless mode | n/a |
| classic_crosshair | false | same | Original off-centre merc crosshair | hud.classic_crosshair |
| client_port_query | 7776 | same | Local port for server pings | net.client_query_port |
| controller_binds_enabled | true | same | Native pad button binds (off for hybrid) | input.pad.binds |
| controller_enabled | false | same | Native controller master switch | input.pad.enabled |
| controller_left_stick_8_directional_merc | false | same | Snap merc movement to 8 directions | input.merc.pad.move8 |
| controller_left_stick_8_directional_spy | false | same | Snap spy movement to 8 directions | input.spy.pad.move8 |
| controller_left_stick_enabled | true | same | Native stick movement (off for hybrid) | input.pad.move |
| controller_left_stick_slow_walk_enabled | true | same | Spy speed follows stick deflection | input.pad.slow_walk |
| controller_left_thumb_deadzone | 0.25 | same | Left stick deadzone | input.pad.deadzone_left |
| controller_right_stick_enabled | true | same | Native stick aiming (off for hybrid) | input.pad.aim |
| controller_right_thumb_deadzone | 0.15 | same | Right stick deadzone | input.pad.deadzone_right |
| controller_right_thumb_vertical_sensitivty | 0.75 | same | Vertical aim relative to horizontal (key misspelt) | input.pad.vertical_scale |
| controller_rumble_enabled | false | same | Vibration on fire, damage, tase, grab, blast | input.pad.rumble |
| controller_rumble_strength | 1.0 | same | Vibration scale 0 to 1 | input.pad.rumble_strength |
| controller_walk_threshold | 0.95 | same | Stick deflection for the spy to run | input.pad.run_threshold |
| controls_merc_config_controller | cam 1.0, clutch_one 0.5 on, clutch_two 2.0 on, look 1.8, look_response CurveMedium, scope 0.75, scope_vertical 2.45, swaps false, vertical 1.0 | same | Merc pad sensitivities | input.merc.pad.* |
| controls_merc_config_kbm | cam 1.0, clutches off (0.0), look 1.0, look_response Linear, scope 0.75, scope_vertical 2.45, swaps false, vertical 1.0 | same | Merc mouse sensitivities | input.merc.kbm.* |
| controls_spy_config_controller | cam 1.8, clutch_one 2.0 on, free_look 1.6 CurveAggressive, gun_look 1.2 CurveSoft, scope 0.75, swaps false, vertical 3.5 | same | Spy pad sensitivities | input.spy.pad.* |
| controls_spy_config_kbm | cam 1.0, clutch_one off, free_look 1.4 Linear, gun_look 1.0 Linear, scope 0.75, swaps false, vertical 3.5 | same | Spy mouse sensitivities | input.spy.kbm.* |
| display_mode | "BorderlessFullscreen" | same | Fullscreen, BorderlessFullscreen, Windowed | display.mode |
| display_monitor | 1 | same | Monitor, numbered from 1 | display.monitor |
| filter_level | 16 | 4 | Anisotropic filtering 0 to 16 | gfx.anisotropy |
| force_max_refresh_rate | true | same | Use the monitor's highest refresh rate | n/a (desktop mode) |
| fps_merc | 240 | 120 | Frame cap as merc, 30 to 240 | gfx.fps_merc |
| fps_spy | 240 | 120 | Frame cap as spy, 30 to 240 | gfx.fps_spy |
| hack_alpha | 178 | same | ER icon opacity while hacking (0 to 255) | hud.hack_alpha |
| host_port_game | 8888 | same | Game port when hosting | net.host_port (28888) |
| host_port_query | 7776 | same | Query port when hosting | net.lan_port (28889) |
| host_upnp | true | same | UPnP forwarding when hosting | net.upnp |
| legacy_dxvk_graphics | false | same | Force pre-3.0 DXVK workarounds | n/a |
| list_on_reloaded_master_server | true | same | List hosted games on the master | net.listed |
| lod | 10.0 | same | Model LOD distance multiplier (stock 1.0) | gfx.lod_scale |
| low_latency_mode | "On" | same | Off, On, Ultra frame queue | gfx.low_latency |
| map_playlists_v3 | 5 playlists (15, 25, 13, 7, 15 maps) | same, Extended has 26 | Playlist definitions (internal) | content/playlists.json |
| map_variant_words | 7 words | same | Variant markers for map sorting | lobby.variant_words |
| map_variant_words_last | true | same | Show variant word after the name | lobby.variant_last |
| master_server_dns | "master-v2.scct-reloaded.ovh" | same | Master server host | net.master_hosts (ours) |
| msaa | true | false | MSAA on | gfx.msaa |
| msaa_samples | 8 | 4 | 2, 4 or 8 samples | gfx.msaa_samples |
| override_console_key | true | same | Console on the key left of 1 | input.console_key |
| resolution_scale | 1.0 | same | Internal render scale 0.5 to 1.0 | gfx.render_scale |
| scoreboard_split_team_size | 3 | same | Team size that splits the scoreboard | hud.scoreboard_split |
| security_acg | false | same | Arbitrary Code Guard | n/a |
| security_dep | true | same | Data Execution Prevention | n/a |
| sens_menu | 0.75 | same | Menu cursor speed | input.menu_cursor |
| server_list | [] | same | Extra servers as "ip:port" | net.server_list |
| shadow_edge_smoothing | true | same | Soft shadow edges | gfx.shadow_soft |
| shadow_optimized | true | same | Per-light shadow resolution by size and distance | gfx.shadow_optimized |
| shadow_quality | 8.0 | 3.0 | Shadow map scale 0.5 to 16 (doc default 2.0) | gfx.shadow_quality |
| show_fps | true | false | FPS readout with 1% low/high | hud.show_fps |
| show_location | true | same | Zone name on the HUD | hud.show_location |
| show_net_loss | false | (absent) | Packet loss in the net readout | hud.show_net_loss |
| show_net_stats | true | false | Ping and jitter readout | hud.show_net_stats |
| show_ping | true | false | Ping on the scoreboard | hud.show_ping |
| show_scoreboard | true | same | Scoreboard state kept | hud.show_scoreboard |
| smaa | true | false | SMAA on | gfx.smaa |
| throttle_stop | false | same | Opt out of Windows power throttling | sys.no_throttle |
| use_split_spy_merc_keyboard | true | false | Separate team keyboard layouts | input.split_layouts |
| voice_activation_hold_ms | 300 | same | Gate hold after speech | voice.hold_ms |
| voice_activation_threshold | 0.02 | same | Gate threshold 0 to 1 | voice.threshold |
| voice_auto_gain | true | same | Automatic mic gain | voice.auto_gain |
| voice_echo_cancellation | false | same | Remove other voices from the mic | voice.echo_cancel |
| voice_input_device | "{14305C1D-...}" | "" | Recording device GUID, empty = Windows | voice.input_device (name) |
| voice_input_gain | 1.0 | same | Manual mic gain | voice.input_gain |
| voice_jitter_frames | 4 | same | Playback buffer 1 to 12 frames | voice.jitter_frames |
| voice_muted | false | same | Self mute | voice.muted |
| voice_noise_suppression | true | same | Noise suppression on | voice.noise_suppression |
| voice_noise_suppression_level | "Standard" | same | Low 15, Standard 30, High 45, Extreme 60 dB | voice.noise_level |
| voice_output_volume | 1.0 | same | Other players' voice volume | voice.output_volume |
| voice_push_to_talk | false | same | Push to talk instead of the gate | voice.push_to_talk |
| widescreen | true | same | Hor+ 3D | gfx.widescreen |
| widescreen_hud | true | same | Unstretched HUD | hud.widescreen |
| window_position | "Stretched" | same | Default, Centered, Stretched | display.window_position |
| ws_er_small | true | same | Smaller ER icons at 16:9 | hud.er_small |
| ws_fov | 112.0 | same | Merc first-person FOV cap (doc 105, max 112) | gfx.merc_fov_cap |
| z_cache_level_names | 76 entries | 88 entries | Map code to display name cache | n/a (from content) |
| z_cache_master_server_ip | "141.227.135.52" | same | Last resolved master address | net.master_cache |
| z_cache_music_volume | 1e-05 | same | Cached profile music volume | audio.music_volume |
| z_cache_sound_volume | 0.5 | same | Cached profile effects volume | audio.sound_volume |
| z_cache_voice_input_device_name | "Wave Link MicrophoneFX (Elgato Wave:3)" | "" | Device name for the GUID | n/a |
| z_first_run | false | same | First-run dialogs shown | ui.first_run_done |
| z_main_menu_count | 30 | 1 | Main menu visits | n/a |
| z_resolution_seeded | true | same | Resolution taken from the desktop once | n/a |

Retired keys seen in older configs or notes: `labs_borderless_fullscreen`, `map_playlists`,
`shadow_quality_terminus`, `sticky_camera_fix`, `use_modern_frame_timing` (CFG36);
`flashlight_rendering_fix`, `fps_client`, `fps_host`, `sens`, `sens_cam`, `scope_sync` (RL11, RL12).
The importer maps those that still have a meaning and ignores the rest.

## 13. Appendix B: command line, chat and console

- Launcher: `SCCT_Versus.exe` with no documented switches. Game with Reloaded: `-dedicated`,
  `-no_throttle`, `-priority <level>`, `-affinity <mask>`. Stock URL options seen elsewhere:
  `?NBPlayers`, `?ScoreDEF`, `?ScoreATT`, `?GamesInARow`, `?Restart` (MEM objective-player-counts).
- Rewrite: `svm --dedicated --map SteeE --mode Story --port 28888`, `--priority`, `--affinity`,
  `--no-throttle`, `--settings <file>`; the server reads a JSON server config with rotation, rules profile
  and admin keys.
- Chat and console lists are in `rl:qol.chatcmds` and `rl:qol.console`.

## 14. Appendix C: third-party pieces and our equivalents

| Reloaded uses | Purpose | Rewrite choice |
|---|---|---|
| Dear ImGui 1.92.8 | Settings overlay, lobby picker | Godot UI |
| SDL3 | Gamepads | Godot joypads (SDL3 based since 4.5) |
| Opus 1.5.2 | Voice codec | Concentus or libopus (BSD) |
| speexdsp | Noise suppression, AGC, echo | RNNoise or speexdsp (BSD), WebRTC APM later |
| MiniUPnPc 2.3.2 | UPnP | Godot `Upnp` |
| c-ares | DNS with fallback resolvers | System.Net.Dns plus cached address |
| nlohmann/json | Config | System.Text.Json with comments |
| SMAA | AA | Godot SMAA |
| D3DX9, shader rewriting | Fix D3D8 shaders on D3D9 | n/a (own renderer) |
| Creative ALchemy, DSOAL (shipped by Enhanced) | EAX | Godot audio buses and reverb |

## 15. Open questions (summary)

1. Reloaded sensitivity formula and the meaning of `vertical` in the control groups (measure in a slot).
2. Lag-compensation scope and default ceiling; fire-prediction scope.
3. Which "R" fixes live in Enhanced script and which in Reloaded memory patches (decompile Enhanced
   `SBase.u`, compare with traces).
4. Default pad layouts per team resolved on 2026-11-01; see `rl:input.pad` and `docs/HUD.md`.
5. Voice masking in stock Versus; positional proximity talk; Godot default capture role on Windows.
6. HUD anchor table for widescreen; ER icon scaling rule.
7. `hack_alpha` default scale; the "Streaming" lobby label.
8. Ini values Reloaded rewrites at start (net rates).
9. Spy third-person FOV policy at ultra-wide aspect ratios (Ryan's call).
