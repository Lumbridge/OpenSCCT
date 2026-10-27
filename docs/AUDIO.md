# Audio

How the original's sounds are converted and played. The playback rules (UNIAudio, the subsystem SCCT Versus uses in
place of stock UE2 audio) and the file formats are specified in `spec/native/audio.md`; the map devices that call
the sound natives in `spec/devices/sound-music.md`.

| Part | Where |
|---|---|
| decoders and writers | `content/svmbuild/audio/` (`sound.py`, `uas.py`, `ogg.py`, `mux.py`, `refs.py`, `build.py`) |
| pipeline steps | `content/svmbuild/steps/audio.py` (`audio`), `content/svmbuild/steps/audio_map.py` (`audio-map`) |
| client | `game/Scripts/Audio/` |
| simulation calls | `Actor.PlaySound`, `PlayOwnedSound`, `StopSound`, `SetSwitch`, `PlayMusic`, `StopMusic` (`OpenScct.Core`), raised as `SimEvent`s; footsteps in `SBase/SBasePawn.Footsteps.cs` |
| events | `SimEventKind.PlaySound` (with `NoOverride`, `NoAttenuate`, `Bone`, `Owned` for PlayOwnedSound, `Heard` for the server's calls, `Flat` for 2D), `StopSound`, `PlayMusic`, `StopMusic`, `SetSwitch` (surface in `Slot`) |
| network | `OpenScct.Server/Net/NetHearSound.cs` (server: ClientHearSound to the players in reach), `PlayerController.Sound.cs` (`ClientHearSound`, `ClientPlaySound`, `ClientSetMusic`) |
| play scene | `game/Scripts/Play/PlayAudio.cs` (the sound of a match), `PlayAudioRouter.cs` (the client's own and the server's copy of one call play once) |
| tests | `tests/content/test_audio_formats.py`, `tests/content/test_audio_steps.py`, `tests/OpenScct.Core.Tests/SimEventsLateTests.cs`, `tests/OpenScctGame.Logic.Tests/AudioRouterTests.cs`, `AudioCallsTests.cs` and `AudioSessionTests.cs`, smokes `--svm-smoke=audio`, `play-audio`, `play-audio-merc` |

## Content

```
python -m svmbuild audio                          # shared/audio (about 2 s)
python -m svmbuild audio-map --maps SteeE         # svmap/SteeE/audio (needs maps, which it runs first)
python -m svmbuild audio audio-map --maps all --jobs 6
```

### shared/audio (`audio` step)

- `audio.json`, format `svm.audio/1`:
  - `packages`: every `Packages/Sounds/*.uax` with its version and sound count.
  - `sounds`: every Sound by object path (`Package.Group.Name`, the path the simulation's `AssetRef`s carry), with
    `kind` (wave, stream, multichannel, random, switch, sequence), `flags`, `volume` (the Sound's Gain, linear),
    `radius` (0: none), `pitch` (always 1: Sounds have none), `loop`, `positional` (false for 2D sounds); waves add
    `file`, format, channels, rate, frames, `duration` and the `smpl` loop points; streams their bank entry name
    (`stream`); lists their `entries` (`sound` resolved to a path, null for the 57 `Remove` entries, and `value`);
    multichannel sounds the speakers and the `.mux` `channelMap`.
  - `mux`: the 18 channel maps.
  - `classes`: class path -> property -> sound, from the effective class defaults (84 classes: weapons, gadgets,
    cameras, mines, the GUI...).
  - `unresolved`, `duplicates` (identical second exports in `Armes_impacts`), `problems` (must be empty).
- `wav/<Package>/<Group>/<Name>.wav`: the 1,078 RIFF WAVE blobs as stored (16-bit PCM).

### svmap/&lt;Map&gt;/audio (`audio-map` step)

- `audio.json`, format `svm.mapaudio/1`:
  - `bank`: the `.uas` index (packages and offsets, unused byte ranges, problems).
  - `streams`: every bank entry with its file, offset, length, duration, channels, rate, samples, page count, serial
    and the number of pages whose CRC failed (all 0).
  - `sounds`: every sound the map can play, `sound id -> {file, root, volume, radius, pitch, loop, positional,
    kind, duration}`: what its actors name, every streamed sound of its bank's packages, and what list sounds reach
    from those. `root` is `map` (a stream in this folder) or `shared` (a wave in shared/audio); a streamed sound the
    bank lacks has `missing: true`. Other sounds (weapons, footsteps) are looked up in the shared library.
  - `actors`: every actor with a sound reference, also the client-only actors that `sim/gameplay.json` leaves out
    (AmbientSound actors, lights and static meshes with an ambient sound): id, class, category, location, the sound
    properties it stores, and for an ambient loop the effective `ambientSound`, `soundVolume`, `soundRadius`,
    `soundPitch` and light values.
  - `zones`: every ZoneInfo with its ambience and its reverb (`effect`: the `ZoneEffect` object, its EFFECT_* class
    and the effective I3DL2 parameters, class defaults with the map's overrides).
  - `missing`: references that do not resolve.
- `streams/<Package>/<Name>.ogg`: the bank's Ogg Vorbis streams as stored.
- The step puts its files into the map's `manifest.json` (`files`, the `audio` group hash, and
  `{"pack": "audio", "sha256": <shared/audio/audio.json>}` in `requires`). The maps step owns the folder: when it
  rebuilds or restores a map it removes `audio/` and writes its own manifest, and the next `audio-map` run (built or
  from the cache) puts both back. Run `audio-map` after `maps` (the `all` target and the step's dependencies do).

### Numbers (reference install, 2026-10-23)

| | |
|---|---|
| Sound packages | 29 `.uax`, 1,513 sounds (+7 duplicate exports): 1,078 waves, 131 streamed, 18 multichannel, 231 random, 43 sequence, 12 switch |
| Map banks | 83 `.uas`, 1,101 streams, 422 MB, every page CRC good; 21 banks hold 1,020 stale bytes each |
| Map tables | 3,936 sounds, 8,883 sound actors, 2,143 zones, 1,292 with reverb (all 83 maps built, 0 failures) |
| SteeE | 22 streams (8.3 MB), 71 sounds, 162 sound actors (26 AmbientSound), 32 sound zones, 29 with reverb |
| Missing streams | 14 references in 8 maps (CineS, ClarD, MounS, OffsD, PyroD, VertE, VertU, VertX): the map uses a streamed sound of a package its bank does not hold; silent in the original as well (spec open question 5) |
| Times | `audio` 1.5 s; `audio-map` 0.4 to 1.2 s a map, all 83 in 21 s with 6 jobs |

## Client

`SoundSystem` (a `Node3D`) is the client world's `ISimEventSink` and plays the events of each tick:

| Event | Effect |
|---|---|
| `PlaySound` (also PlayOwnedSound: `Owned` set) | rules 9 to 17: sound id from the actor and slot, voice allocation over 32 voices (free, then same slot unless bNoOverride, then lowest priority), random / switch / sequence resolution, volume = call volume x the gains on the way, clamped to [1e-5, 1], radius = the Sound's radius when set, pitch clamped to [0.5, 2] |
| `StopSound` | stops the actor's sources of that sound |
| `PlayMusic`, `StopMusic` | one music voice, 2D, no Gain, fade in; the previous track fades out over at least 2 s |
| `SetSwitch` | the surface the next switch sound plays; without one, the system traces 15 uu above to 150 uu below the sound (`IAudioWorld.SurfaceOf`) |

Every update it moves sources with their actors, computes the gain itself (Godot's attenuation is off on every
`AudioStreamPlayer3D`, which only pans): `volume x fade x R / (R + 0.5 (d - R))` beyond the radius for 3D sources,
plain volume for 2D ones. Ambient loops (rules 19 to 22) come from two places: the simulation's actors whose
`AmbientSound` is set (zones, SAmbientSoundTrigger, movers), read through `IAudioWorld.CollectAmbient`, and the
client-only actors of the map's `audio.json`. 3D loops play within 100 x their radius; a zone's 2D ambience plays
while the listener is in that zone; other 2D loops fall off with distance and only the loudest of a sound plays.
A loop keeps its voice while it plays; when more loops are in range than there are voices (the KrauE merc spawn hears
about 40), the lowest priority ones lose theirs to rule 15 and start again only when they win a voice back. The
sound system remembers which voice each actor's loop started on and only treats it as that loop while the voice
still carries the loop's sound id: a voice another call took over belongs to that call, so a loop never stops or
restarts another actor's loop (it did, every update, at 1,000 to 6,000 starts a second on KrauE and MounE).
`--svm-smoke=play-audio-krau` (KrauE, merc) fails when one actor's loop starts more than 20 times in a run.

Reverb: `ReverbBuses` makes `SvmWorld`, `SvmFlat`, `SvmMusic` and one `SvmReverbN` bus per distinct zone effect
(SteeE: 29 zones share a handful of parameter sets). 3D voices play on the bus of the listener's zone. Godot's
`AudioEffectReverb` is not I3DL2; the parameter mapping (wet from Room + Reverb, room size from DecayTime, damping
from RoomHF and DecayHFRatio, spread from EnvironmentDiffusion, pre-delay from the two delays) is an estimate.

### In a match

`PlayRoot` adds a `PlayAudio` (before the HUD): it loads the map's audio, puts a `PlayAudioRouter` in front of the
`SoundSystem` as the client world's event sink (the HUD's tap wraps it), binds the sound system to the client's
world once the map is loaded (actor locations, ambient loops, zones, surfaces) and listens from the play camera
every frame. Where the sounds come from:

- The client's own simulation: the local pawn's and the proxies' footsteps and gear (walk-cycle notifies), the
  landing, gadgets and projectiles the client simulates, the goggles' mode sounds (`SGoggle.Sound.cs`), the HUD
  messages' jingles (`SHud.MessageSounds.cs`), ambient loops of replicated actors.
- The server (spec/native/audio.md rules 4 and 5): `NetHearSound`, the server world's event sink, sends each
  PlaySound of the server's world to every remote player in reach as `ClientHearSound`; the client's
  `PlayerController.ClientHearSound` raises it in the client's world, marked `Heard`. This is how the shots, the
  impacts and everything else only the server runs are heard. The device relay (`NetDeviceEvents`) keeps its own
  events.
- Calls to the player: `ClientPlaySound` (the spawn sound, shock sounds) and `ClientSetMusic` (objective jingles,
  victory and defeat music) are ported on PlayerController and SPlayerController.
- Events raised between two ticks (a call received before the step, an input command) are not in any tick's
  events; `NetHearSound.RelayLate` (server, before each step) and the router (`ClientSession.BeforeWorldTick`) take
  them from `SimEvents.Late`, so none of the sounds is lost.
- Scripts that run on both machines give the client two copies of one call; the router plays the first copy of a
  sound on an actor and drops the other when it comes within 0.2 s.
- Rule 5: an unattenuated call of the actor the local player views (the controller itself before it has a view
  target) plays 2D (`SimEvent.Flat`, decided where the call is made).

`SVM_AUDIO_MUTE=1` mutes the Master bus (the sound buses still mix and are metered) and `SVM_AUDIO_LOG=1` prints the
voices, the router's counts and every bus's peak every 10 s (`SVM_AUDIO ...` lines).

Wiring in another host (tools, smokes):

```csharp
var sound = new SoundSystem();
AddChild(sound);
string shared = AudioLibrary.SharedDirFor(mapFolder);
sound.Bind(AudioLibrary.Load(shared, mapFolder), new LevelAudioWorld(world.Level, collision, a => loaded.RecordOf(a)?.Id));
world.EventSink = sound;                 // SimEvents of every tick
sound.SetListener(cameraLocation, cameraRotation);   // every frame, UE space
```

### Smoke test

`--svm-smoke=audio` loads SteeE's audio and simulation (a merc `PawnRig`), binds the sound system to the rig's
world, and plays through SimEvents a footstep (the `PlayFootStepRight` port at running speed: `SW_FootRun` resolved
on the floor's surface, concrete `pasBet_course*` at the merc spawn, plus the gear sound), the merc rifle's shot
(`RA_GunDef_1shot`, a sequence of the shot and the shell on the floor) and a hack beep (`Hack_try1`); then it runs
90 frames at the merc spawn and checks the listener's zone and its reverb bus. It needs the converted SteeE audio
(`--svm-maps=` finds maps outside the repository's `.content`). Headless, Godot uses its dummy audio driver.
Result (2026-10-23): pass; 1,513 sounds, 32 zones, 27 reverb buses, load with the map 0.8 s.

`--svm-smoke=play-audio` (spy) and `play-audio-merc` play SteeE as the launcher does (Play Level, three bots at
normal, the listen server on its own thread, the wall clock), with the keys sent as Godot input events
(`Input.ParseInputEvent` of the bound keys; without a window the pointer cannot be captured and the keys go to
`PlayInput.Key`): the mouse turns the view around, the wheel raises the speed, C stands the spy up, W runs, Space
jumps, the left button fires. Master is muted; the test reads the sound system's calls and starts per kind and the
peak meters of the sound buses and of Master. Master is turned 80 dB down for the run (`AudioPeaks.Quiet`), so the
output is metered without being heard. It needs the voices (own landing, other pawns, zone ambience, the spawn
sound; for the merc also his own run steps and his rifle; the spy's steps at a walk or a crouch only as calls,
their sounds do not exist), a sound bus above -90 dB and a Master peak. Results (2026-10-25, headless, Godot's
dummy driver mixes): spy 106 voices (ambient 20, other pawns 67, gadgets 11, impacts 5, the spawn sound, the
landing), SvmWorld -11.0 dB, SvmFlat -5.9 dB; merc 84 voices with his own run steps and 4 rifle shots, SvmReverb0
-4.9 dB. With a window (WASAPI, 48 kHz) the merc run passed with Master at -81.9 dB (80 dB down), and the game
itself (`--svm-play=SteeE --side=spy --rules=playlevel --bots=3`, `SVM_AUDIO_MUTE=1 SVM_AUDIO_LOG=1`) logged
15 to 21 voices, Master -83.4 dB, SvmFlat -3.4 dB, SvmWorld -21.6 dB.

The ambient check: every start of a loop after its first needs a stop the rules explain, counted by the sound
system (`SoundSystem.AmbientStops`): its voice taken by another call (rule 15) or the loop leaving its range or zone
(rules 20 to 22). More than two unexplained restarts of a loop fail the run (the old bug restarted loops every
update). On Krauser's courtyard the far `Krau_airCourt` loop loses its voice to the player's footsteps and wins it
back, 11 to 15 starts a run, each one after a rule-15 loss; a bare count of 20 failed about one run in two.

## Findings

- `SBase.u` imports `Sound_Persos.FootSteps.SW_FootCrouch` and `SW_FootWalk`, which `Sound_Persos.uax` does not
  have: the original plays nothing for crouched and walking steps (only `SW_FootRun` exists). The rewrite keeps the
  calls; the library has no such sounds, so they are silent too.
- Random lists often leave part of the 10,000 roll empty (Ra_Chien 70 %, RA_matos_Run_Def has `Remove` entries):
  those triggers play nothing, as in the original.
- More sounds the ports call that no package of the reference install has, so silent in the original too:
  `Sound_Persos.vetements_materiel.RA_matos_Run` (the spy's running gear), `AbaisseGoggles`, `CSGEAR_1`,
  `SCGEAR_1`, `Frottement_tissus_Faible`, `RA_Frottement_Fort`, `Sound_Persos.divers.SW_saisir`. Every entry of the
  merc's running gear (`RA_matos_Run_Def` and its `RA_matos_runDef_*` lists) is `Remove`: it never plays either.
  The sound system lists them under `Unknown`.
- The server animates every pawn and fires its footstep notifies, so footsteps are heard both from the client's
  own animation and from the server; the router keeps one of each.
- Four of the original's waves end with a sampler chunk (`smpl`) that lists no loop: `Interface/Objectifs` and
  `Interface_PT/Objectifs` Jingl_AttacB, `Interface_PT/Objectifs` Jingl_DefB and `Amb_Krauser` krau_blup1. Godot's
  wave loader reads a loop entry past the end of such a chunk ("Reading less data than requested", four times a
  match) and makes the jingle a forward loop from 0 to 0. `AudioLibrary` drops an empty sampler chunk before
  loading (`WavChunks`, tests in `tests/OpenScctGame.Logic.Tests/WavChunksTests.cs`) and sets a one-shot's loop mode to
  none, so the jingles play once and the warning is gone (play-aim log: 4 before, 0 after). The sound data were
  never short.

## Not done yet

- Footsteps: the walk-cycle notifies call `PlayFootStepLeft` / `PlayFootStepRight`; `FootPrint` (the surface
  switch, footprints) is a movement hook with no implementation yet, so the sound system's own surface trace stands
  in.
- Occlusion (rule 18), the zone factor of the ambient range and Doppler.
- Following a bone (rule 17): sources follow their actor; `ClientHearSound` sends BoneIdx 0.
- The reach test of the server's hearing is an estimate (100 x the radius; spec open question 4).
- Script sounds not ported yet (no call site in the ports): the SHud binocular sounds (Snd_BinocularNoise,
  Snd_BinocularAnalyzeLoop, Snd_BinocularAttLoop, Snd_BinocularAnalyzeDetect), Snd_HudShutdown (the port of
  HudShutDown has no ClientPlaySound), Hud_viseProgress (SetBallisticValue), the cape sounds of SBasePawn
  (Cape_InOut, RA_Cape_damage), SCamera's CamDzzz and the camera, laser and sensor explosions
  (RA_camSurveil_EXPLO), SMuzzleSticky's GzitSound, SFlashGen's SND_Tonnerre, SNoiseEmitter, SShotGunProjector's
  SW_Impact_Shot, the push sounds of Decoration and the ESoftBodyActor sounds.
- `SPlayerController.ClientNotifyNoise` (the spy's noise meter on footsteps) is not ported.
- The default slot of the simulation's `PlaySound` is SLOT_None; the original's is SLOT_Misc (rule 1). It only
  changes which call replaces which when all 32 voices are busy.
