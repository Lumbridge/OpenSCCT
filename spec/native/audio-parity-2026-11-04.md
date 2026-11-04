# Full audio parity sweep — 4 November 2026

Implementation follow-ups: [audio repairs and validation](audio-parity-fixes-2026-11-04.md) and
[completed Creative reverb implementation and measurements](reverb-parity-2026-11-04.md).
This assessment records the earlier baseline; its Freeverb/A-09 finding is superseded by the reverb follow-up.
The findings below describe the audit snapshot before those repairs.

The original recordings and most playback/event machinery are present. The largest remaining fidelity gap is the
acoustic result: Godot's reverb substitutes a different algorithm for I3DL2, several spatial/network rules remain
approximations, and there is no calibrated original-versus-rewrite listening comparison. This sweep also found
specific defects in voice volume, voice settings/input, footstep noise cues, sound slots and stopping sounds.

This is an assessment and repair backlog, not an implementation or release. No gameplay source, reference install,
published pack or live service was changed by this sweep. Unrelated work was already in progress in this checkout.
The baseline was `ea4cb80d49d5c96e29b627bfd9550ff7af01c425` (0.8.84), plus that concurrent working tree.

## Reference and method

- Original content/scripts: owner's `C:\Users\ryans\Desktop\Enhanced SCCT Versus 4.0 Beta 5`.
- Retail native image: SHA-256 `a98873aaec1de21fdcccc0afb9985e3ddcf38a9ddce1207214872d6428ec9950`.
  REA/Ghidra inspection checked playback, music, source layout and selected update/stop instructions.
- Rewrite: current source, repository `.content`, and installed `%LOCALAPPDATA%\OpenSCCT\packs`, inspected separately.
- Verification: decoded-property and byte comparisons, existing focused tests, local simulation probes, and hidden,
  muted Windows Godot runs through the actual 48 kHz mixer. No personal microphone was recorded.

Evidence and reproducible probes are in ignored `local/audio-parity-audit/`: `content.json`,
`content_audit.py`, `content.md`, `gameplay-network.md`, `voice-settings.md`, `gameplay-probe.log`,
`ambient-gain-probe.json`, `smokes.json`, `smokes-extended.json`, `listener-pcm/report.json`, and native REA records.
Native output is static evidence, not an observation of the original running game.

This report does not assign a replacement percentage. A single number would mix asset completeness,
implemented behavior and audibly verified equivalence. The table below keeps those separate.

## Coverage of all audio areas

| Area | Current result | Remaining difference or evidence boundary |
|---|---|---|
| Sound packages and recordings | All 1,513 original definitions represented; all 1,078 original WAV blobs byte-identical | Installed library has a different content history; A-08 |
| Map streaming and multichannel banks | All 1,101 converted Ogg streams match original bank slices; mux definitions match | PC stereo multichannel fallback follows the bank layout; original speaker-device output was not compared |
| Map sound actors and zones | All audio actor, zone/effect and portal-distance metadata matches across 83 source maps | MoonD has no matching supplied B5 source; synthetic TestRange has no audio table |
| Random, surface-switch and sequence sounds | Definitions/weights match; native selection/sequence paths implemented | Surface fallback uses wrapper SurfaceType rather than the innermost material; no exhaustive timed-sequence output comparison |
| WAV loops and streamed loops | Sampler metadata and loop points retained; wave/stream loading implemented | No exhaustive seam/gapless playback or long-duration streaming/device stress run |
| Sound volume, gain, radius, pitch | Leaf overrides, gain chains, pitch clamps and inverse-distance attenuation implemented | Instigator SoundDampening missing; A-10. User-volume/capping equivalence still needs output calibration |
| Source allocation and priority | 32 voices, free/same-slot/priority replacement and occluded-zone priority radius implemented | Omitted sound slot differs; A-04. No paired original 33+ source contention capture |
| Source location and listener ownership | Native local-pawn relocation implemented; actual mixer confirms own near/far ratio 1, remote far ratio 0.1707 | Remote relayed bone identity absent; object-view listener rules are implemented but not freshly compared to original output |
| Panning and speaker layout | Godot 3D panning with its extra attenuation/filter disabled | DirectSound-versus-Godot azimuth, elevation, stereo image, surround and headphones uncalibrated |
| Distance, obstruction and occlusion | Native wall-thickness/material/zone formula, range scaling and smoothing implemented | Uses DirectSound-style radius attenuation, not EAX occlusion/filter/room behavior; original active EAX path unknown |
| Reverb and acoustic zones | 1,292 authored reverb zones retained; listener-zone routing implemented | Freeverb approximation differs from I3DL2; A-09 |
| Doppler and audio zoom | Native distance/Doppler factors and zoom behavior represented | Doppler arithmetic and zoom projection are estimates; no paired moving-source/directional output sweep |
| Footsteps, landing, gear and breath | Surface/stance/speed logic, animation notifies and owner prediction present; gameplay smokes pass | Missing spy walk/run noise notifications; A-03. Several absent B5 sound imports need separate interpretation |
| Weapons, gadgets, damage and private cues | Primary shots/reloads, shocker, handling, goggles, gas mask, shutdown/binocular/HUD paths present | Focused tests pass; exhaustive original action-by-action listening across all equipment remains open |
| Map devices, movers and ambient changes | Sound/ambient/music triggers, mover loops, replication and 2D ambience handover implemented | Instigator-only music routing capability gap; A-11. Complete interactive event traversal not performed |
| Music, objective jingles and menus | Entry/Lobby songs, music fades, objective preference and alarm/jingle paths implemented | Authored menu tones and selected join chime are not exact original sound identity; installed bank fallback needs checking |
| Multiplayer play/stop and prediction | Reliable ordered pawn sounds, late event capture, owner exclusion, replay suppression and duplicate handling tested | Hearer reach is estimated; 0.2 s duplicate window and late-one-shot discard are rewrite policies |
| Stop/reset/scene lifetime | Per-sound stop, scene StopAll and generator lifetime safeguards present | Native actor wildcard StopSound(None) is missing; A-05 |
| Voice routing, transport and codec | Team/open/classic/modern rules, game-state restrictions, mutes, jitter/FEC/loss handling and eavesdropping represented | Codec/tuning intentionally differs from Reloaded; live end-to-end sound quality not certified |
| Voice playback/mute controls | Separate voice bus and persistent per-player volume/mutes implemented | Double master voice gain; A-01. In-round overlay interaction missing; A-07 |
| Microphone, processing and voice settings | Device enumeration, capture, local test, managed noise/AGC/echo processing present | Live device reapplication gap; A-02. DSP quality, permissions, hot-plug and hardware latency untested |
| Voice bindings and focus | Shared BindingInput has voice/capture suppression logic | Online/hub consumers bypass that service; A-06 |
| Output settings and platform behavior | Master/effects/music/voice settings persist and separate buses exist | Windows mixer checked; Linux audio backends, output-device changes and suspend/resume not exercised |

## Confirmed findings and repair order

P2 means a useful correctness/fidelity repair; P3 means a confirmed capability gap without demonstrated current-map
impact. Items explicitly marked as uncalibrated require comparison evidence before numerical tuning.

### A-01 — P2: voice output volume is applied twice

`game/Scripts/Voice/VoiceAudio.cs:321,349` passes `Settings.OutputVolume` into `VoiceTalker.ReadFrame`, which scales
the decoded PCM (`src/OpenScct.Net/Voice/VoiceTalker.cs:124`). The same setting also controls the `SvmVoice` bus
(`VoiceAudio.cs:168` and the online/hub/generic voice consumers). Thus 50% gives 25% amplitude before Master.
The microphone-test path has only the bus application, so its loudness does not match live playback at reduced volume.

Apply the setting once and keep the echo-reference gain consistent. Verify real-mixer amplitude ratios at 100/50/25%
for live voice and the local microphone-test playback. This is confirmed by the source path; this sweep did not
capture live voice PCM at those settings.

### A-02 — P2: microphone changes and initial failures remain latched during a visit

Online and hub voice start capture once behind `_microphoneTried` (`OnlineVoiceChat.cs:163`, `HubVoiceChat.cs:124`).
Their settings-save callbacks update processing fields but do not restart/reselect the saved device
(`OnlineVoiceChat.cs:189`, `HubVoiceChat.cs:177`). Only `VoiceAudio.StartMicrophone` assigns `AudioServer.InputDevice`.
An initial failure also leaves that one-attempt guard set. Other paths, such as opening the mic test, can incidentally
change the shared device; that does not make the live consumer's save handling complete.

Reapply changed device settings and allow retry after failure. Validate switching A to B in an active visit and
recovering after an unplugged/missing device, with actual capture hardware.

### A-03 — P2: spy steps omit directional noise/echo notifications

`src/OpenScct.Game/SBase/SBasePawn.Footsteps.cs:103,110` lacks B5's spy walk/run
`ClientNotifyNoise(self, 0.5/1.0)` calls. The receiving method is implemented; its callers are missing.
Original script evidence is `local/enhanced-b5-audit/b5-uc/SBase/SBasePawn.uc:1688,1698`.

A running spy 300 uu in front of a merc produced no arrow intensity and no `echo_indic1` cue in a local probe.
Injecting the original notification produced intensity 0.8125 and one cue. Restore the calls while respecting local
prediction/remote proxy ownership; verify walking/running, distance/direction, and no doubled owner or remote cue.

### A-04 — P2 under contention: omitted sound slots use None instead of Misc

`src/OpenScct.Core/Engine/Actor.Devices.cs:173,199` defaults PlaySound/PlayOwnedSound to `SLOT_None` rather than the
native `SLOT_Misc`. A probe emits slot 0 instead of 1. Free voices are allocated first, so this affects overlap and
replacement when the 32-source pool is full, not every quiet scene. Correct defaults with a 33+ source test that
checks same-actor replacement and bNoOverride.

### A-05 — P2: StopSound(None) cannot stop all sources of an actor

`Actor.Devices.cs:225` drops None immediately. `SoundSystem.StopSound` also only matches an exact call path.
The native actor wrapper at `0x10a25720` forwards a null sound to subsystem virtual offset `0x74`; that vtable entry
is `0x10b047e0`. Its `0x10b04858..0x10b0488c` path tests the actor when the sound is null and stops every matching
source. Both arguments null instead stop non-music sources globally. None is a wildcard here, not an invalid call.

The rewrite probe emits zero stop events. B5 controller reinit methods use StopSound(None); scene teardown can hide
the omission, so a stuck sound during an actual reset is not claimed as reproduced. Preserve wildcard semantics
through events/relay/mixer and test one actor's multiple sources against another actor and music. The local-pawn
controller rebinding needs explicit consideration.

### A-06 — P2 integration gap: online/hub voice bypass shared binding suppression

Generic `VoiceChat.cs:113` reads BindingInput voice state. `OnlineVoiceChat.cs:172` and `HubVoiceChat.cs:133` poll raw
InputMap actions instead. Binding capture/focus suppression resets the shared service, not those raw consumers.
This is a confirmed integration mismatch; accidental audible transmission in a specific UI flow was not reproduced.
Route all consumers through the shared service and test controller/keyboard/chord alternates, binding capture,
focus loss and held-key recovery in actual online/hub scenes.

### A-07 — P2 feature gap: in-round speaker overlay lacks mute/volume interaction

Reloaded's `rl:voice.mix` overlay supports click mute and scroll volume (`spec/reloaded/FEATURES.md:378`).
Current in-round rows are plain Labels (`OnlineVoiceChat.cs:337`, `VoiceIndicator.cs:94`). Lobby/hub player lists do
have mute/volume controls. Add the missing interaction to the round overlay using the existing persistent stores.

### A-08 — P2 content follow-up: installed packs differ materially from repository/B5

Repository originals and conversions agree. Installed shared audio has 36 packages/2,148 sounds versus B5's
29/1,513. Twenty-four matching Sound_Persos random/switch lists replace B5 Remove/silence entries with restored
sounds. Those are content-version differences, not evidence that the B5 converter lost recordings.

Installed KrauE `ZoneInfo328` lacks B5/repository's `Amb_Krauser.Ambiances.DefT_Ext1` ambience (volume 100, pitch 54),
and that sound is absent from the installed shared library. That table cannot start the authored ambient bed.
Sixteen original paths are absent installed, but fifteen are E_* aliases without named C# call sites in the bounded
search. All available original WAV/stream payloads match, with no referenced file or audio-manifest hash failure.

The installed tree has 80 map manifests/audio tables. Entry/Lobby are absent from that pack root; menu music can
still come from bundled or checkout fallback maps, so universal menu silence is not established. Determine the
intended source version and effective client roots before regenerating or publishing packs. No published index or
currently running client's chosen roots was audited here.

### A-09 — fidelity gap: I3DL2/EAX acoustics are approximated

`game/Scripts/Audio/ReverbBuses.cs:85` maps I3DL2 to Godot Freeverb. It omits RoomHF/RoomLF shelves, independent
reflection pattern, diffusion/density and distance-dependent room rolloff. Decay/predelay ranges clamp, and absolute
wet level is uncalibrated. Mapping both reflection delays into one predelay cannot recreate independent early/late
structure. Preserving zone parameters does not prove their audible reproduction.

This is the highest-value fidelity investigation: capture controlled original dry/wet/occluded output across
representative rooms, corridors, hangars and outdoors; establish which EAX/DirectSound path the reference uses;
compare decay, early/late balance, spectrum and zone transitions. Decide between a closer effect implementation
and a calibrated approximation from those results. Source inspection alone cannot supply a credible percentage.

### A-10 — P3 capability gap: SoundDampening is omitted from playback

Actor playback forwards call volume unchanged; native rule 2 applies self/owner instigator SoundDampening.
With SoundDampening=.25 and call volume 1, the probe emits 1 rather than .25. All seven serialized defaults found
are 1; no gameplay map override or runtime assignment was found. This is not evidence current players are four
times too loud. MakeNoise's separate dampening does not implement playback dampening.

### A-11 — P3 capability gap: instigator-only map music is filtered before recipients

`src/OpenScct.Core/Engine/MusicTrigger.cs:24` checks LocalPlayerController in the server world, while the device
music relay broadcasts events. A dedicated server can drop a non-null instigator's event; listen-host behavior can
drop or broadcast the wrong event. None of 33 converted placed MusicTriggers enables bInstigatorOnly. Preserve
recipient intent through the relay before adding a map that relies on it.

## Corrections to old gap notes and investigated non-defects

- The occluded-source priority-radius correction reported on 3 November is now implemented in SoundSystem.Start,
  PlayLeaf and SoundRules.Priority, with three passing focused priority tests. It is not a current missing feature.
- ClientNotifyNoise exists; A-03 is the missing footstep caller, not a missing implementation of the method.
- The gas-mask ambient random-list gain changes after the first update: the probe measured .06980393 to .19673419
  (2.818x). **This is not being reported as a parity defect.** Native PlaySound stores the resolved leaf in the
  source, and native Update's ambient-slot path at `0x10b05bf3..0x10b05d26` recalculates volume using that leaf's
  Gain, dropping the list gains too. REA instruction evidence confirms `FMUL [ECX+0x64]` at `0x10b05c2d`.
  A complete original output capture remains separate from this static arithmetic comparison.
- Native one-shot playback clamps pitch to .5..2 (PlaySound calls the clamp with those constants). The previous
  upper-clamp open question is resolved by this inspection.
- No source sound has fixed-position flag 0x40; no source list root has nonzero Radius or loop/2D/fixed flags.
  Missing support for those combinations is not demonstrated as an active B5 content defect.
- 57 unresolved list entries are source Remove entries. Thirty-six animation-notify sound paths are absent from
  both repository shared audio and the supplied B5 sound packages, across 632 sound/stop records. Installed richer
  audio resolves them. Adding replacements would change B5 sound identity; original import behavior remains a
  runtime question. Smoke 'unknown' names include these known absent imports, so a pass is not zero unresolved calls.
- Voice's 48 kHz/20 kbit/s Opus versus Reloaded's 16 kHz/16 kbit/s, authored managed DSP, hub proximity voice and
  synthesized menu feedback are deliberate reconstruction/enhancement choices, not proof of exact original output.
- Existing pipe StopSound animation notifies and the non-looping heartbeat prevent claiming permanent loops merely
  from missing explicit script stops. No such stuck-loop reproduction was established.

## Fresh validation

| Check | Result | What it establishes |
|---|---:|---|
| Godot game solution build | Passed; 0 warnings/errors | Current client builds |
| Python audio formats/pipeline | 17 passed, 0 skipped | Includes B5 shared-library and SteeE bank fixtures |
| Gameplay/audio/router/priority/WAV/pawn replication/gas-mask tests | 69 passed, 0 skipped | Simulation/events, ownership, queue order and selected game behavior |
| Voice codec/routing/authority/lobby tests | 90 passed, 0 skipped | 75 Net, 10 Game, 5 Integration; not hardware/DSP-quality proof |
| Complete source/content audit | Passed | 1,513 sound definitions, 1,078 WAVs, 1,101 streams and 83 source-map audio metadata comparisons |
| Windows real-mixer smokes | 5 final passes | audio, audio-listener, play-audio, play-audio-merc, play-audio-krau |
| Local routing PCM | Own far/near 1.000; remote far/near .1707 | Local relocation and remote attenuation through actual stereo PCM |
| Targeted probes | Reproduced A-03/04/05/10; ambient transition recorded | Bounded event/source arithmetic checks |

Total: **176 focused tests**, all passing, plus five final mixer/gameplay smoke passes. Krauser's first run exceeded
the 120 s smoke budget while building navigation (165.5 s to ready). A rerun with a 300 s budget and prepared
navigation passed in 29.12 s. Preserve the first timeout as an infrastructure/coverage boundary, not an audio failure.

The mixer runs use the repository content root. They do not validate the installed drift in A-08 or establish that
every map event and sound is correct. Muting Master kept these unattended runs quiet while capture/metering occurred
upstream. No microphone capture, original device-output recording, calibrated listening session, Linux audio run,
full 24-player acoustic stress, or live internet voice acceptance was performed. Managed AGC/noise/echo processing
has no direct coverage identified in the inspected voice test directory; passing codec tests do not verify it.

## Acceptance work after the confirmed repairs

1. Capture a matched original/rewrite reference set: movement on every used surface, all original weapons/gadgets,
   damage/status sounds, objective/round events, devices/movers, menu/lobby/map music and ambient zone transitions.
2. Measure attenuation and panning at fixed radii/azimuths, material/door occlusion, reverb impulse/decay behavior,
   Doppler and scope/binocular zoom, with matching reference device/backend/settings.
3. Exercise 33+ competing sources and repeated starts/stops; test actor reset, possession, spectator/object views,
   travel/rejoin and reliable/late sound delivery under latency/loss.
4. Check voice volume against the local test, each game-state routing mode, mute persistence, device changes,
   focus/binding capture, hot-plug and managed DSP with deterministic signals before live hardware acceptance.
5. Repeat relevant output/device cases on Windows and Linux and audit the actual standalone client's effective
   content roots. Only then make an audibly verified parity claim.
