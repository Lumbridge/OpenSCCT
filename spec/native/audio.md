# Audio: sound objects, map banks, UNIAudio playback rules

Units:
- fmt:uax-sound (the Sound export layout)
- fmt:uas (the per-map stream bank)
- fmt:mux (multichannel maps)
- nat:Engine.Actor.PlaySound
- nat:Engine.Actor.PlayOwnedSound
- nat:Engine.Actor.StopSound
- nat:Engine.Actor.SetSwitch
- nat:Engine.Actor.PlayMusic
- nat:Engine.Actor.StopMusic
- nat:Engine.PlayerController.ClientHearSound
- uc:Engine.PlayerController:ClientPlaySound
- uc:Engine.PlayerController:ClientSetMusic
- uc:SBase.SPlayerController:ClientSetMusic
- uc:SBase.SHud:LocalizedMessage (its sounds)
- eng:UUNIAudioSubsystem::PlaySound
- eng:UUNIAudioSubsystem::GetSwitchSound
- eng:UUNIAudioSubsystem::SoundPriority
- eng:UUNIAudioSubsystem::UpdateAmbientSounds
- eng:UUNIAudioSubsystem::Update
- eng:UUNIAudioSubsystem::GetOcclusion
- eng:UUNIAudioSubsystem::PlayMusic
- eng:UAnimNotify_Sound::Notify
- uc:SBase.SBasePawn:PlayFootStepLeft
- uc:SBase.SBasePawn:PlayFootStepRight
- uc:SBase.SBasePawn:PlayFootStepSound

## Purpose

What the player hears and how loud. SCCT Versus replaces the stock UE2 audio with Ubisoft's UNIAudio subsystem on
DirectSound 3D: its own Sound object (gain, radius, flags, random, switch and sequence lists), per-map Ogg Vorbis
banks for long sounds, zone ambiences, I3DL2 reverb from the listener's zone, and surface switches for footsteps.
The simulation only raises the calls (`PlaySound`, `StopSound`, `PlayMusic`, `SetSwitch`); every rule below runs on
the machine that plays the sound. Content conversion: `docs/AUDIO.md`. Device scripts that call these natives:
`spec/devices/sound-music.md`.

## Sound objects (fmt:uax-sound)

`Packages/Sounds/*.uax` hold the Sound exports (1,513 sounds in 29 packages, plus 7 identical duplicate exports in
`Armes_impacts`): 1,078 waves, 131 streamed, 18 multichannel, 231 random, 43 sequence and 12 switch sounds. After the tagged properties (always just `None`):

| Field | Type | Notes |
|---|---|---|
| FileType | FName (two compact indices) | `wav` for sounds with a wave or a stream, None for list and multichannel sounds |
| Source | FString | the bank entry name of a streamed sound; obfuscated like the name table where the package is |
| Flags | u32 | below |
| Gain | f32 | only with flag 0x400; a linear factor (the tool stored dB as 10^(dB/20): 0.891 is -1 dB, 3.98 is +12 dB) |
| Radius | f32 | only in packages of version 173 and later (172 has none: 0) |
| list | u32 count, count x (FName sound, u32 value) | random (0x100), switch (0x80), sequence (0x800) |
| channel mask | u32 | multichannel (0x2000): FL FR BL BR Center LFE from bit 0 |
| wave | i32 skip offset, compact size, RIFF WAVE bytes | everything else that is not streamed (0x04); all 1,078 are 16-bit PCM, mostly mono 22,050 Hz |

Flags (stored, or added at play time; the subsystem ORs the play flags with the Sound's):

| Bit | Meaning |
|---|---|
| 0x0002 | loop (the DirectSound buffer plays with the looping flag); ambient plays add it |
| 0x0004 | streamed: the data is in the map's bank |
| 0x0008 | play flag of music: no Gain, no 3D, music volume instead of sound volume |
| 0x0010 | 2D: not positioned (head relative); zone ambiences and music beds |
| 0x0020 | play flag of ambient sounds: pitch follows the actor's SoundPitch every update |
| 0x0040 | play flag: the source stops following its actor |
| 0x0080 | switch: plays the entry whose value is the surface type |
| 0x0100 | random: entry values are weights out of 10,000 |
| 0x0400 | Gain is stored |
| 0x0800 | sequence: entries play in order, value = repeat count |
| 0x1000 | transient: Serialize skips the data of such an object (never stored) |
| 0x2000 | multichannel: the stream named after the Sound, the channel map in the .mux of that name |
| 0x4000 | only picks a statistics counter for streamed sounds |

List entries name sounds by object name; the engine finds them by name, so an entry resolves to the Sound of that
name in the same package first. 57 entries name `Remove` (deleted sounds): such an entry is silence.

## Map banks (fmt:uas)

`Packages/Sounds/<Map>.uas`, one per map (83). Little-endian:

1. `u32 n`, then n records of `char name[64]`, `u32 offset`: the packages whose streamed sounds the map uses.
2. At each offset: `u32 m`, then m records of `char name[64]`, `u32 start`, `u32 start2`, `u32 end`, `u32 one`.
   start2 always equals start and the last word is always 1; the index stores no duration.
3. Each entry is one complete Ogg Vorbis stream in [start, end).

Names end at the first NUL; the rest of the 64 bytes is leftover memory. The writer lays out section, streams,
section, streams, but 21 banks also hold 1,020 stale bytes (an older copy of the next index and a cut page) before
a section: only offsets are trusted. All 1,101 streams in the 83 banks pass the Ogg checks: every page CRC, one
serial per stream, first and last page flags, page sequence. Copies of one sound in different banks differ only by
their serial number (and so the CRCs), except four Interface music tracks (Jingle_Victoire1, Jingl_YouLose,
Menu_music1, Menu_music2) that the menu banks (Entry, Lobby) and VertX hold in another encoding. Durations come from the last granule position over the sample rate.

The subsystem loads the bank named after the map when the map starts and registers every loaded Sound with flag
0x04 against it. A streamed sound whose package section is missing from the bank cannot play (14 such references in
8 maps, for example CineS's `Amb_Ini.Ambiances.ini_amb1`; see `docs/AUDIO.md`).

## Multichannel maps (fmt:mux)

`Packages/Sounds/MC_*.mux` (18) are INI text: `[Channels]` with `FL`, `FR`, `BL`, `BR`, `Center`, `LFE`, each the
name of a mono stream or empty. The PC banks do not hold those streams: the multichannel Sound plays the stereo
stream that has the Sound's own name.

## Rules

### Actor natives

1. `PlaySound(Sound, Slot, Volume, bNoOverride, Radius, Pitch, Attenuate, BoneName)` and `PlayOwnedSound` (same
   parameters; the rewrite raises both as PlaySound events, PlayOwnedSound with `Owned`): omitted Slot is `SLOT_Misc` (1), Volume is `TransientSoundVolume`, Radius is
   `TransientSoundRadius`, Pitch is 1, Attenuate is true for PlaySound and false for PlayOwnedSound, BoneName none.
   A None sound does nothing.
2. When the actor's Instigator is the actor itself or its Owner, Volume is multiplied by the Instigator's
   `SoundDampening`.
3. The sound id is `ActorIndex x 16 + Slot x 2 + bNoOverride`.
4. On a client (NM_Client) every local player controller hears it; elsewhere every controller with `bIsPlayer`.
   A zero radius is passed to the hearers as 80. PlayOwnedSound skips the controller that owns the actor (the
   owning client plays the sound itself through its own simulated call).
5. A hearer that is remote gets `ClientHearSound(Actor, Id, Sound, Location, (Volume x 1000, Radius, Pitch x
   1000), Attenuate, BoneIdx)`: volume and pitch travel as thousandths, BoneIdx is the bone the source follows. The receiving client plays it with radius 80 when
   the radius is 0, and without 3D (flag 0x10) when Attenuate is false and the sound comes from the actor it views.
6. `SetSwitch(Surface)` stores the surface type in the subsystem (one value for all sounds); the next switch sound
   consumes it (rule 13).
7. `StopSound(Sound)` stops the sources of this actor that play that Sound.
8. `PlayMusic(Song, FadeInTime)` returns a handle; `StopMusic(Song, Handle, FadeOutTime)`; see Music.

### Playing one sound (UUNIAudioSubsystem::PlaySound)

9. A list sound plays one of its entries through the same function: random (rule 12), switch (rule 13), sequence
   (rule 14). Music cannot be a list sound.
10. Without the music flag, Volume is multiplied by Gain. Then Volume = max(Volume, 1e-5); without the music flag it
    is multiplied by the sound volume setting (1.0). Pitch is clamped to at least 0.5. The source volume is
    clamp(Volume, 1e-5, 1): a Gain above 1 only helps quiet calls.
11. Radius: the Sound's own Radius when above 0, else the call's. The source's minimum distance is that radius
    (at least 0.001; 10 when 0), its maximum distance 1e9: DirectSound 3D's inverse distance model, so the gain is 1
    up to the radius and `R / (R + Rolloff (d - R))` beyond, with the listener roll-off factor `Rolloff` = 0.5
    (Default.ini `Rolloff`). Distances are in Unreal units. Occlusion (rule 18) shortens the minimum distance.
12. Random: `rand() % 10000` against the running sum of the weights; the first entry whose sum exceeds it plays.
    When the weights sum to less than 10,000 the rest is silence (Ra_Chien in SteeE: 3 x 1000, so 70 % of its
    triggers play nothing).
13. Switch: the surface is the pending SetSwitch value (consumed), else the surface of the material a line check
    finds from 15 uu above to 150 uu below the sound's location. The entry with that value plays, else the entry
    with value 0, else nothing. The found material is unwrapped first (game 0x10b06cf0, trace flags 0x1297): while
    it is a Modifier (FinalBlend, TexModifier, TexPanner...) its `Material` is taken, and the innermost material's
    SurfaceType is used; no material gives 0. Script readers of `HitMaterial.SurfaceType` (footprints, impacts) use
    the hit material itself. Not ported: the collision data keeps one SurfaceType per material, the wrapper's.
14. Sequence: entries play one after the other at the source's location, each `value` times; a value with bit
    0x2000 loops that entry for `value & 0x1fff` times its duration instead. An entry that cannot play is skipped;
    the sequence ends after the last entry.
15. Voices: `Channels` = 32. A free source is used first; otherwise a source with the same id (ignoring the low bit)
    is replaced, unless the new call has bNoOverride (then the new sound is dropped); otherwise the source of the
    lowest priority is replaced when the new priority is higher; otherwise the sound is dropped. Slot SLOT_None (0)
    gets a fresh id every call, so it never replaces a slot.
16. Priority: `p x Volume + (2D ? 1 : 0) + (music ? 2 : 0) + (streamed ? 1 : 0)` with
    `p = clamp(1 - d^2 / (100 R)^2, 0.01, 1)` from the listener (1 when R is 0).
17. A 3D source follows its actor (or the bone it was started on) every update, unless flag 0x40 is set or the actor
    is gone; the listener's own sounds and sources moving faster than 300 uu/s get no Doppler velocity.
18. Occlusion, for actors whose SoundOcclusion is not OCCLUSION_None: a line check from the listener to the source
    and back gives the thickness of what is in between; the factor is `(2 - thickness / 700) x m`, m a per-material
    factor of the surface hit (indexed by the hit material's own OcclusionType, game 0x10b071eb: of the two
    directions' materials the one with the smaller factor; no material is type 0), then a zone term, at most 1. It multiplies the minimum distance (and is applied as
    EAX occlusion when EAX is on). Not ported yet.

### Ambient sounds (UpdateAmbientSounds, every update)

19. Every actor with an AmbientSound is a candidate, with id `ActorIndex x 16 + 8`, volume
    `SoundVolume / 255 x AmbientVolume` (setting 1.0), pitch `clamp(SoundPitch / 64, 0.5, 2)`, play flags loop and
    update pitch. A light with a LightType other than LT_None also scales it by LightBrightness / 255.
20. 3D ambient (the Sound lacks flag 0x10): when the Sound has a Radius above 0 it replaces the actor's SoundRadius.
    The sound plays while `d <= 100 x SoundRadius`, as a 3D source with that radius (rule 11).
21. 2D ambient of a ZoneInfo (and LevelInfo): plays only while the listener's zone is that ZoneInfo, 2D, at the
    candidate volume times Gain. This is the zone ambience (SteeE: 13 zones).
    Whether an ambient loop is 3D or 2D (rules 20 to 22) is decided by the actor's own Sound, also when it is a
    list sound whose entries are 2D waves (the gas mask breathing `RA_breath_masqGaz`, a random of the 2D wave
    `breath_masqGaz`): the loop plays as that Sound says, not as the picked entry's flag would.
22. 2D ambient of any other actor: volume `V x Gain / (((d - R) / R) x Rolloff + 1)` with R the actor's
    SoundRadius (or the Sound's Radius), played 2D while at least 0.05; of several actors with the same Sound only
    the loudest plays.
23. The listener is the view actor's location; its zone is the zone of that point (kept when the point is in zone
    0).

### Music

24. PlayMusic plays the song 2D with the music flag at the music volume (1.0), radius 1000, pitch 1, fading in over
    FadeInTime. One track at a time: a previous track fades out over `max(FadeInTime, 2)` seconds. A song that is
    already the current track and still playing is not restarted.

### Reverb

25. Each update sets the DirectSound 3D listener's I3DL2 environment to the `ZoneEffect` of the listener's zone
    (an I3DL2Listener object in the map package: the EFFECT_* classes of SBase, with per-map overrides). Reverb only
    affects 3D sources. SteeE: 29 of its 32 sound zones have one (Room, StoneCorridor, Sewerpipe, Stoneroom,
    Hallway, Hangar, City).

### Audio zoom (SetAudioZoom)

25a. `SetAudioZoom(factor)` (the merc's scope and the spy's binoculars: 0.33, 0.66, 1.0 per zoom level, 0 when put
    away) stores the factor on the subsystem and rescales every playing sound without the 2D flag (0x10): volume ×
    (1 + factor × (3 - 3.9 r)), r = min(1, 112.5 / camera FOV × the projected distance of the sound from the view
    axis); a sound behind the view has r = 1. On the aim line a sound is up to four times louder (capped at full
    volume by DirectSound), at the edge of the view or behind it down to 0.1 (game `execSetAudioZoom 0x10a25b80`,
    `0x10b06510`, `0x10b06340`, gain `0x10b06280`; also used when a sound starts, `0x10b03d50`). The rewrite reads the
    projected distance as the sideways share of the unit direction (estimated) and applies it every frame
    (`SoundRules.AudioZoomGain`). A negative factor only refreshes the volumes.

### Animation notifies

26. AnimNotify_Sound plays its Sound at the mesh owner with id 0, its Volume, Radius (an int; 0 means 80), pitch 1,
    on its bone.

### Footsteps (SBasePawn)

27. `PlayFootStepLeft` / `PlayFootStepRight` (walk-cycle notifies): FootPrint for that foot (a line check from the
    foot bone 100 uu down; `SetSwitch(material.SurfaceType)`, footprints and splashes), then
    `PlayFootStepSound(left)`.
28. `PlayFootStepSound(bLeft)`: nothing while falling. Bone `B L Foot` or `B R Foot`; speed = horizontal velocity
    length, truncated to an int. On the local player's own pawn FootStepsVolume becomes LocalFootStepsVolume
    (stored: it stays). Crouched: when speed > RunningSpeed / 4, `SW_FootCrouch` at volume
    `FootStepsVolume x speed / (RunningSpeed / 2)`; iLastWalkTime = int(TimeSeconds). Standing under 250 uu/s: the
    spy notifies the local controller of a 0.5 noise (ClientNotifyNoise), `SW_FootWalk` at
    `FootStepsVolume x speed / RunningSpeed`; iLastWalkTime set. Otherwise `SW_FootRun` at the same volume, and the
    spy notifies a 1.0 noise and plays `RA_matos_Run` (radius 250, bone `B Spine2`) and, when iLastWalkTime + 4 <=
    int(TimeSeconds), `RA_breath` (radius 10, bone `B Head`); the merc plays `RA_matos_Run_Def` and `RA_breath_Def`
    the same way. The footstep sounds use slot SLOT_Interact and radius `speed / (RunningSpeed / 200)`; the others
    SLOT_Misc at `FootStepsVolume x 1.0`. All sounds are in `Sound_Persos` (FootSteps, vetements_materiel,
    onomatopees). `SW_FootCrouch` and `SW_FootWalk` are imported by SBase.u but missing from Sound_Persos.uax, so
    crouched and walking steps play nothing (the reference resolves to None).

### Controller sound calls

29. `ClientPlaySound(ASound, bVolumeControl, inAtten, Slot)` (PlayerController, simulated, sent by the server to
    one player: the game info's PlayerStartSound at a spawn, the shock sounds): volume 0.9, or `inAtten` clamped to
    [0, 1] with bVolumeControl; plays `ASound` at the controller's ViewTarget with that Slot and Attenuate false
    (so, by rule 5, without 3D when the view target is the player's own pawn). Nothing without a ViewTarget.
    `ClientReliablePlaySound` calls it with the same sound.
30. `ClientSetMusic(NewSong, NewTransition)` (sent reliably to one player: the objective jingles, the end of round
    VictoryMusic or LooseMusic): with MTRAN_None the song is a jingle, `ClientPlaySound(NewSong, true, 1.0,
    SLOT_Talk)`; any other transition plays it as music (`PlayMusic(NewSong, 3.0)`, rule 24) and stores Song and
    Transition. SPlayerController drops the MTRAN_None jingles when the profile's `bObjectiveSounds` is off
    (default on).
31. `SHud.LocalizedMessage` (on the player's machine, with the message): for SGameMessages, an alarm message (the
    optional object an SAlarm with MessagePriority above 0, outside the game-queue switches 0, 2 to 7, 15 to 17,
    20, 53 to 55) plays `Interface.Objectifs.Jingl_alarm_Def` for a merc or `Jingl_alarm_Attac` for a spy at the
    HUD's owner (SLOT_Misc, volume 1.5, radius 150, attenuated); then switch 2, 6 and 7 play SndScoreLives
    (`Interface.Menu.Menu_start_point`) the same way, 5 the losing jingle SndYouLose (`Jingl_YouLose`), 10, 27, 33
    and 36 Snd_ObjectiveAttack (`Jingl_AttacB`), 12 and 34 Snd_ObjectiveDef (`Jingl_DefA`), 23 Snd_ObjectiveStop
    (`Jingl_DefB`), all three through ClientSetMusic with MTRAN_None. For SDeathMessage of a teammate other than
    the player, a spy (team 0) killed by a merc (team 1) plays Snd_ObjectiveDef.

## Constants

| Name | Value | Provenance | Source |
|---|---|---|---|
| default slot | SLOT_Misc (1) | decoded | execPlaySound |
| hearer radius for 0 | 80 | decoded | execPlaySound, ClientHearSound, AnimNotify_Sound (0x115bf000) |
| range factor | 100 | decoded | SoundPriority, UpdateAmbientSounds (0x115beffc) |
| priority floor | 0.01 | decoded | SoundPriority |
| volume clamp | 1e-5 .. 1 | decoded | PlaySound, Update |
| pitch | min 0.5 (one-shots), 0.5 .. 2 (ambient) | decoded | PlaySound, Update |
| minimum distance | radius, 0.001 at least, 10 for 0 | decoded | PlaySound |
| maximum distance | 1e9 | decoded | PlaySound |
| Rolloff | 0.5 | decoded | Default.ini [UNIAudio.UNIAudioSubsystem]; the DS3D listener factor is assumed to be the same |
| Channels | 32 | decoded | Default.ini |
| Doppler | 0.5 | decoded | Default.ini DopplerFactor |
| 2D ambient cut | 0.05 | decoded | UpdateAmbientSounds |
| ambient id offset | 8 | decoded | UpdateAmbientSounds |
| music radius | 1000 | decoded | PlayMusic |
| music fade-out minimum | 2 s | decoded | PlayMusic |
| switch trace | 15 uu up to 150 uu down | decoded | GetSwitchSound |
| occlusion thickness scale | 1 / 700 per uu | decoded | GetOcclusion |
| footstep volumes | FootStepsVolume 0.8, LocalFootStepsVolume 1.0 | decoded | SBasePawn defaults |
| footstep speeds | walk below 250, crouch above RunningSpeed / 4 | decoded | PlayFootStepSound |

## Network

Rules 4 and 5: the server tells each player in reach, the owner of a PlayOwnedSound excepted. The rewrite raises the
calls as SimEvents in whichever world runs the script (server, client, client prediction), and:

- The server's network layer (`NetHearSound`, the server world's event sink) turns each PlaySound event of its world
  into `ClientHearSound` for every remote player whose view is within 100 x the radius (0 counts as 80; an
  unattenuated call always), skipping the owner of a PlayOwnedSound and the device events the device relay sends.
  The reach test is an estimate (open question 4).
- The receiving client (`PlayerController.ClientHearSound`) raises the sound in its own world, marked as heard.
- Events raised between two ticks (a call received before the step, an input command) stay out of the ticks'
  buffers (`SimEvents.Late`); the server's relay and the client's sound router take them before the next step, so
  the sounds of received calls are not lost.
- Many scripts run on both machines (the pawns' footstep notifies, their gear sounds), so a client gets its own
  copy of such a call and the server's. The play scene's router plays whichever copy of a call on the same actor
  comes first and drops the other when it comes within 0.2 s (an adaptation of the rewrite, whose players are
  all remote clients of a separate server world; in the original the listen host hears its own world directly).

Ambient sounds need no events: AmbientSound, SoundVolume, SoundRadius and SoundPitch are replicated Actor
properties (spec/devices/sound-music.md).

## Evidence

- Editor build (named by its guard strings): AActor::execPlaySound `0x11189d50`, execPlayOwnedSound `0x11189fb0`,
  execStopSound `0x11187250`, execSetAudioZoom `0x111874a0`, APlayerController::execClientHearSound `0x11189020`,
  UUNIAudioSubsystem::PlaySound `0x10f6c340`, GetSwitchSound `0x10f6a350`, SoundPriority `0x10f6a1d0`,
  GetOcclusion `0x10f6a5e0`, UpdateAmbientSounds `0x10f6d390`, Update `0x10f6e980`, PlayMusic `0x10f69b10`,
  StopMusic `0x10f6d250`, SetI3DL2Listener `0x10f68540`, SetMap `0x10f6fca0`, USound::Serialize `0x11117a70`,
  UAnimNotify_Sound::Notify `0x11103360`; the sequence player `0x10f6e0c0` / `0x10f6e200`, the random pick
  `0x10f6a300`, the DirectSound 3D buffer setters `0x10f720e0` (min and max distance), `0x10f72ce0` (occlusion),
  `0x10f72120` (volume in millibels, -10000 floor).
- Game build: execPlaySound `0x10a25e50`, execPlayOwnedSound `0x10a26120`, execStopSound `0x10a25720`,
  execPlayMusic `0x10a25c60`, execStopMusic `0x10a25d10`, execClientHearSound `0x10a22ee0`.
- Reflection (local/reflect/editor.json): Actor SoundRadius 0x270, TransientSoundVolume 0x274,
  TransientSoundRadius 0x278, AmbientSound 0x198, SoundVolume 0x2e3, SoundPitch 0x2e4, SoundOcclusion 0x2e5,
  LightType 0x2d1, LightBrightness 0xf8; Pawn SoundDampening 0x440.
- Decompiler: `SBase/SBasePawn.uc` PlayFootStepSound, FootPrint, PlayLandingSound; `Engine/I3DL2Listener.uc`,
  `SBase/EFFECT_*.uc`; `Engine/ZoneInfo.uc` ZoneEffect.
- Formats: every .uax, .uas and .mux of the reference install through `content/svmbuild/audio` (counts above).

## Open questions

1. The listener roll-off: the 2D ambient formula reads the configured Rolloff (0.5); that the DirectSound 3D
   listener gets the same factor is assumed. Scenario: a SoundTrigger at a known radius, listener at 1x, 2x and 4x
   the radius; record the output level.
2. The zone term of occlusion and of the ambient range (a power of the listener's and the source's zone numbers)
   is not decoded; the rewrite uses 1.
3. The upper pitch clamp of one-shot sounds (2 is assumed, as for ambient sounds).
4. APlayerController::HearSound (the per-hearer reach test that decides who gets ClientHearSound) is not decoded.
5. Which streamed sounds a map really plays when its bank lacks the package (rule in Map banks): assumed silent.
