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
  - `zoneDist`: the portal distance between every two zones, rows and columns by zone number (the collision ZONE
    records): the engine's ZoneDist table (game 0x109ea770), made from the world model's zone Connectivity masks.
    Occlusion and the 3D ambient range scale with it (rules 18 and 20).
  - `missing`: references that do not resolve.
- `streams/<Package>/<Name>.ogg`: the bank's Ogg Vorbis streams as stored.
- The step puts its files into the map's `manifest.json` (`files`, the `audio` group hash, and
  `{"pack": "audio", "sha256": <shared/audio/audio.json>}` in `requires`). The maps step owns the folder: when it
  rebuilds or restores a map it removes `audio/` and writes its own manifest, and the next `audio-map` run (built or
  from the cache) puts both back. The maps step names `audio-map` as a follower (`Step.followers`), so any plan that
  runs `maps` with dependencies on (`python -m svmbuild maps --maps X`) runs `audio-map` after it; only `--no-deps`
  leaves a map without audio. The client warns (`SVM_AUDIO the map has no audio table ...`) when a map folder has no
  `audio/audio.json`: its ambience, reverb and streamed sounds are then silent.

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
plain volume for 2D ones. Occlusion (rule 18) shrinks R for a 3D source whose actor's SoundOcclusion is not
OCCLUSION_None: two line checks (listener to source and back, movers and level geometry) give the thickness between
the first surfaces seen from each end and their materials' OcclusionType (collision TFLX); the factor
`min(1, 0.95^(ZoneDist^2) x 1.5 x (2 - thickness / 700) x m)`, pulled halfway to 1 when both surfaces are in one
zone, with m the EAX material preset of the type (Generic 0.32, Full 0, SingleWindow 0.2, DoubleWindow 0.056, ThinDoor
0.35, ThickDoor 0.079, WoodWall 0.1, BrickWall 0.056, StoneWall 0.032, Curtain 0.5), multiplies R (at least 0.1 uu); R
eases to a new value over at most 1 s. This is the subsystem's DirectSound path; with EAX on the original applies
the same factor as EAX occlusion (direct path -2000 log10 f mB at high frequencies, the preset's LF and room ratios),
which Godot cannot do per voice. A second hit in zone 0 leaves the sound unoccluded, as in the original. Ambient loops (rules 19 to 22) come from two places: the simulation's actors whose
`AmbientSound` is set (zones, SAmbientSoundTrigger, movers), read through `IAudioWorld.CollectAmbient`, and the
client-only actors of the map's `audio.json`. 3D loops play within 100 x their radius x 0.8^(ZoneDist^2) between the
actor's zone and the listener's; a zone's 2D ambience plays while the listener is in that zone; other 2D loops fall off with distance and only the loudest of a sound plays. When the loudest changes (or the listener walks into another zone with the same ambience) the playing loop is handed to the new actor and carries on, as in the original; it used to stop and start again from the beginning (MoonD's park organ, seven 2D speakers, restarted each time the player walked past one).
A loop keeps its voice while it plays; when more loops are in range than there are voices (the KrauE merc spawn hears
about 40), the lowest priority ones lose theirs to rule 15 and start again only when they win a voice back. The
sound system remembers which voice each actor's loop started on and only treats it as that loop while the voice
still carries the loop's sound id: a voice another call took over belongs to that call, so a loop never stops or
restarts another actor's loop (it did, every update, at 1,000 to 6,000 starts a second on KrauE and MounE).
`--svm-smoke=play-audio-krau` (KrauE, merc) fails when one actor's loop starts more than 20 times in a run.

Reverb: `ReverbBuses` makes `SvmWorld`, `SvmFlat`, `SvmMusic` and one `SvmReverbN` bus per distinct zone effect
(SteeE: 29 zones share a handful of parameter sets). 3D voices play on the bus of the listener's zone. Godot's
`AudioEffectReverb` is a Freeverb (eight damped combs of 25 to 37 ms, four allpasses), not I3DL2. The mapping
follows the I3DL2 definitions where the models meet: the comb feedback that loses 60 dB over DecayTime along the mean
comb (room size = (g - 0.7) / 0.28, so decays below about 0.6 s or above about 10 s clamp); the comb low-pass that
gives DecayTime x DecayHFRatio at HFReference (Godot's damping is a 2.5 to 10 kHz cutoff, so a ratio of 1 or more
takes 10 kHz); the wet level from Room + Reflections and Room + Reverb summed as energy, dry at 1; the pre-delay from
ReflectionsDelay + ReverbDelay (20 to 500 ms), no pre-delay feedback. Still estimated: the absolute wet level
(Freeverb's gain is not the I3DL2 reference), and RoomHF / RoomLF, EnvironmentDiffusion, Density, the reflection
pattern and RoomRolloffFactor have no Godot counterpart.

Music at map load: the engine plays the LevelInfo's Song with no fade when a map loads (game 0x109e82c4,
`Audio->PlayMusic(Level->Song, 0)`); `SoundSystem.Bind` does the same from the map table (TermD's `DAPlayerStart`,
the menu levels' music). MusicTrigger's SoundVolume plays no part: the subsystem's PlayMusic plays at the music
volume setting only (game 0x10b04920). AquaE and the other Aqua maps' `aqua_musicA` is a zone ambience (ZoneInfo
AmbientSound), so it plays as the zone's 2D bed by rule 21, as in the original.

### In a match

`PlayRoot` adds a `PlayAudio` (before the HUD): it loads the map's audio, puts a `PlayAudioRouter` in front of the
`SoundSystem` as the client world's event sink (the HUD's tap wraps it), binds the sound system to the client's
world once the map is loaded (actor locations, ambient loops, zones, surfaces) and listens every frame where the
original does (rule 23): UNIAudio's Update takes the viewport player controller's Location (game 0x10b05260), which
the following camera's solver moves to the camera spot, so that is the drawn camera; in the object views (sticky
camera, camera network, Mirador and the move to a sticky camera) the controller stays by the pawn and the player
keeps hearing from there. First person keeps the camera (where the original's controller sits then is not decoded). Where the sounds come from:

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

## Footprints

`SBasePawn.FootPrint` (the walk-cycle notifies and the landing, through `IMovementHooks.FootPrint`) traces 100 uu
down from the foot bone, sends the floor's SurfaceType to the sound system (SetSwitch, the next step's switch sound),
spawns SFootPrintEffect on sand, SWaterFootStepEffect on medium water, SSparkWater on deep water and SDustEmitter on
a fake ceiling above 200 uu/s, and while the feet are dirty (sand, snow, water: FootDirtyness falls by 1, 1 or
0.05 / 0.02 a step) leaves an SFootPrint projection for 20 s. The print draws what SFootPrint's run-time material
draws (its PostBeginPlay builds a Combiner and a TexScaler): the sand, water (water and snow) or phosphor texture
(all `sfx.FootPrints.step01`) as a mask, in the step's colour (StepColor: sand grey 140, snow (20, 25, 40), water
black) at an opacity of the texture's alpha times `1 + StepColor.A x FootDirtyness` / 255, the right foot's print
mirrored (V flipped). `CombatFxView.Decals.cs` draws it as a quad of the texture's size x DrawScale (25.6 uu, an
orthographic projector's frustum) on the floor, its length along the walk. It is cast straight down under the pawn's
yaw instead of along the foot bone, and the texture's axes on the floor are estimated.

## Not done yet

- Doppler (rule 17): the subsystem's Update (editor 0x10f6e980) gives the listener a velocity from its own movement
  over the update and each 3D source its velocity (zeroed above 300 uu/s or for the listener's own sounds), and
  DirectSound applies DopplerFactor 0.5. Godot cannot be given a velocity per player, so it would be a pitch change
  of ours; it needs the DirectSound distance factor first (whether one Unreal unit is taken as a metre), which is not
  decoded, so the players keep Doppler off.
- Following a bone: sources follow their actor; `ClientHearSound` sends BoneIdx 0. A bone-following source would
  need the bone index sent with the sound and the drawn skeleton's bone looked up each update.
- The switch fallback trace (rule 13) uses the hit material's own SurfaceType, not the innermost material's (the
  collision data keeps the wrapper's). FootPrint's SetSwitch, which precedes every step sound, uses the hit
  material's own value in the original too.
- The 0.8^(ZoneDist^2) factor the subsystem also applies to an occluded one-shot's priority radius.
- The reach test of the server's hearing is an estimate (100 x the radius; spec open question 4).
- Script sounds not ported yet (no call site in the ports): SFlashGen's SND_Tonnerre (the class is not ported: its
  lightning flicker belongs to the lights). The noise event NativeHitWall raises after a bounce sound is not ported.
- Ported since: the SHud binocular sounds (Snd_BinocularNoise, Snd_BinocularAnalyzeLoop, Snd_BinocularAttLoop,
  Snd_BinocularAnalyzeDetect; SHud.Binoculars.cs), Snd_HudShutdown (SPlayerController.CombatShock.cs),
  Hud_viseProgress (SHud.Ballistic.cs), the cape sounds of SBasePawn (Cape_InOut from SetDesiredGlassValueOnClients,
  RA_Cape_damage from SetPerturbationGlassOnClients, radius 300; PawnVision.cs), SShotGunProjector's SW_Impact_Shot
  (chance 0.3, volume 0.8, radius 100; SShotGunProjector.cs), the ESoftBodyActor shotSound and collSound (radius 128;
  Render/SoftBodies/SoftBodyViews.cs, docs/RENDERING.md "Soft bodies"), SCamera's CamDzzz (SCamera.cs), STriggerLight's sound (STriggerLight.cs), the camera, laser and
  sensor chaff explosions (RA_camSurveil_EXPLO), SNoiseEmitter's steps and breaths, SMuzzleSticky's GzitSound (at
  the pistol, SWeaponAttaque.TickSWeaponAttaque) and SGamePlayObject's BounceSound (NativeHitWall: half the mirrored
  velocity; a bounce that carries on faster than 40 uu/s on an object with a static mesh, volume 1, radius 100).
  Decoration's PushSound and EndPushSound need no port: no SCCT class extends Decoration and no map places one.
- Map loops over the network (read through, not run): SAmbientSoundTrigger.PreBeginPlay runs in every world, so a
  client starts with the server's on/off state; AmbientSound is replicated (Actor group 5 in the first bunch,
  SAmbientSoundTrigger and Mover on every change) against class-default shadows, so an initially-on trigger, a
  trigger switched later and a mover's loop reach the client.
- `SPlayerController.ClientNotifyNoise` (the spy's noise meter on footsteps) is not ported.
- The default slot of the simulation's `PlaySound` is SLOT_None; the original's is SLOT_Misc (rule 1). It only
  changes which call replaces which when all 32 voices are busy.
