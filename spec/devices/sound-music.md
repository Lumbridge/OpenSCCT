# Sound devices: SoundTrigger, SAmbientSoundTrigger, MusicTrigger

Units:
- def:Engine.SoundTrigger
- uc:Engine.SoundTrigger:Trigger
- def:Engine.MusicTrigger
- uc:Engine.MusicTrigger:PreBeginPlay
- uc:Engine.MusicTrigger:ClientPlayStopMusic
- uc:Engine.MusicTrigger:Trigger
- uc:Engine.MusicTrigger:UnTrigger
- rep:Engine.MusicTrigger.ClientPlayStopMusic
- def:SBase.SAmbientSoundTrigger
- uc:SBase.SAmbientSoundTrigger:PreBeginPlay
- uc:SBase.SAmbientSoundTrigger:Trigger
- uc:SBase.SAmbientSoundTrigger:UnTrigger
- nat:Engine.Actor.PlaySound
- nat:Engine.Actor.StopSound
- nat:Engine.Actor.PlayMusic
- nat:Engine.Actor.StopMusic
- nat:Engine.Actor.GetCurrentMusic

## Purpose

What the player hears when map events fire: one-shot positional sounds (SoundTrigger), switchable positional loops
(SAmbientSoundTrigger), and a music track (MusicTrigger). SteeE: 9 SoundTriggers, 15 SAmbientSoundTriggers, 1
MusicTrigger (the "stress" track when the crane blows the wall).

## Data

| Class | Field | Type | Default | Notes |
|---|---|---|---|---|
| SoundTrigger | `Sound` | Sound | None | |
| SoundTrigger | `SoundVolume` | byte | 190 | own field, shadows Actor's |
| SoundTrigger | `SoundRadius` | float | 64 | own field |
| SoundTrigger | `SoundPitch` | byte | 64 | own field; 64 is pitch 1.0 |
| SoundTrigger | defaults | | | `bCollideActors=False`, hidden, `RemoteRole=ROLE_DumbProxy` |
| SAmbientSoundTrigger | `AmbientSound` | Sound | per map | the loop |
| SAmbientSoundTrigger | `bInitialyOn` | bool | false | |
| SAmbientSoundTrigger | `bToggle` | bool | false | |
| SAmbientSoundTrigger | `SavedAmbientSound` | Sound | | |
| SAmbientSoundTrigger | defaults | | | `SoundVolume=190`, `RemoteRole=ROLE_SimulatedProxy`, `nativereplication`, no collision |
| MusicTrigger | `Song` | Sound | None | |
| MusicTrigger | `bToggle`, `bInstigatorOnly` | bool | false | |
| MusicTrigger | `FadeInTime`, `FadeOutTime` | float | 0 | seconds |
| MusicTrigger | defaults | | | `RemoteRole=ROLE_SimulatedProxy`, `bNoDelete`, `bAlwaysRelevant`, `bHidden=False` with `DT_None` |

## Rules

1. `SoundTrigger.Trigger`: `PlaySound(Sound, SLOT_None, SoundVolume / 255, default no-override, SoundRadius,
   SoundPitch / 64)`. Every trigger plays it again; overlapping plays are allowed (`SLOT_None`). UnTrigger does
   nothing.
2. `SAmbientSoundTrigger.PreBeginPlay`: save `AmbientSound` into `SavedAmbientSound`; if not `bInitialyOn`, set
   `AmbientSound = None` (silent until triggered).
3. `SAmbientSoundTrigger.Trigger`: if `AmbientSound == None`, restore it (the loop starts); else if `bToggle`, set it
   to None (the loop stops). A Trigger while on without `bToggle` changes nothing.
4. `SAmbientSoundTrigger.UnTrigger`: `StopSound(AmbientSound)`, `AmbientSound = None`.
5. `MusicTrigger.PreBeginPlay` (simulated): `SetDrawType(DT_None)`.
6. `MusicTrigger.Trigger`: `ClientPlayStopMusic(true, EventInstigator)`; `UnTrigger`: `ClientPlayStopMusic(false,
   EventInstigator)`.
7. `ClientPlayStopMusic(bTrigger, EventInstigator)` (simulated, replicated reliable from the authority): if
   `bInstigatorOnly` and the local player's pawn is not `EventInstigator`, return. If the current music is not `Song`
   and `bTrigger`: `PlayMusic(Song, FadeInTime)`. Else if `bToggle` or not `bTrigger`: `StopMusic(Song, 0,
   FadeOutTime)`. So with `bToggle` a second Trigger stops the track; an UnTrigger always stops it.
8. Volume, radius and pitch: `SoundVolume / 255` is the volume factor and `SoundPitch / 64` the pitch factor. A sound
   asset's own radius replaces the actor's `SoundRadius` (RE+ finding); the radius unit and attenuation curve
   belong to the audio spec.
9. Animation notify sounds are separate: see `animated-mesh.md` rule 12.

## States

None.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| pitch unit | 64 = 1.0 | | decoded | Trigger bodies |
| volume unit | 255 = 1.0 | | decoded | Trigger bodies |

SteeE instances:

| Actor | Tag | Sound | Volume / radius / pitch | Fired by |
|---|---|---|---|---|
| SoundTrigger359, 396 | `blast` | `Seq_alarme` | 255 / 300 / 64 | crane switch (SInterrupteur28808) |
| SoundTrigger398, 347 | `sirene` | `Ra_sirene_ext` | 255 / 200 | three player-proximity Triggers |
| SoundTrigger510, 694 | `Chien` | `Ra_Chien` | 255 / 75 and 90 | two Triggers |
| SoundTrigger2052 | `Mouche` | `RA_mouche` | 255 / 20 | Trigger4176 |
| SoundTrigger2053 | `Rats` | `Ra_Rats` | 255 / 20 | Trigger4177 |
| SoundTrigger267 | `vitre_bas_brisee` | `Squ_Fenetre_ouverte2` | 240 / 19 | SVitre1935 breaking |
| SAmbientSoundTrigger453, 21258, 736, 494, 21259, 418, 297, 737, 484, 493, 474, 325 | `baie_brisee1..4`, `vitre_brisee..7` | `Squ_Fenetre_ouverte1` | 255 / 17 to 35 | SVitre panes breaking: outside ambience through the hole |
| SAmbientSoundTrigger273 | `moloaie` | `Squ_Feu_Molotov`, bToggle | 255 / 24 | SMagicEvent232 at +1 s (on) and +32 s (off) |
| SAmbientSoundTrigger283 | `doorblast2` | `Squ_Rue_Short` | 255 / 18 | breakable door explosion |
| SAmbientSoundTrigger285 | `telespark` | `TV_Buzz`, on, bToggle | 190 (default) / 8 | SGamePlayObject259 (TV) breaking switches the buzz off |
| MusicTrigger263 | `blast01` | `Squ_Mus_Stress`, bToggle | (SoundVolume 50 set, unused by script) | crane notify |

## Network

- SoundTrigger and SAmbientSoundTrigger run on the server. `PlaySound` on a server is heard by remote players through
  the engine's sound replication (UE2 sends the sound to each player in range); the ambient loop is the replicated
  `AmbientSound` property (Actor replication: `AmbientSound`, then `SoundVolume`, `SoundRadius`, `SoundPitch` while
  it is set).
- MusicTrigger: `ClientPlayStopMusic` is declared reliable from the authority on an unowned, `bAlwaysRelevant`,
  `ROLE_SimulatedProxy` actor. Stock UE2 sends such calls only to an owning connection (none here), so the call would
  just run on the server. But this build relies on the same pattern for every event-triggered emitter
  (`Engine.Emitter.ReplicateTrigger`, enabled by `bNetworkTrigger`, which makes the emitter `ROLE_SimulatedProxy` and
  `bAlwaysRelevant`; all 16 event emitters in SteeE set it), which only makes sense if the engine delivers these calls
  to every client. Expected behaviour therefore: the call runs on the server and on every client, and each machine
  plays the track locally. Confirm (Open question 1).

## Edge cases

- `bInstigatorOnly` (unused in SteeE) reads the local player's pawn, which is meaningless on a dedicated server.
- The crane notify passes no instigator; with `bInstigatorOnly` false the instigator is ignored anyway.
- SAmbientSoundTrigger285 has both Tag and Event `telespark`; the class never fires its Event, so there is no loop.

## Evidence

- `local/re-out/uc/Engine/SoundTrigger.uc`, `Engine/MusicTrigger.uc`, `SBase/SAmbientSoundTrigger.uc`.
- Reflection: SoundTrigger owns `SoundPitch` (offset 769), `SoundVolume` (768), `SoundRadius` (764), `Sound` (760),
  separate from Actor's fields (`local/reflect/game.json`).
- RE+ MapBuilderFacts.md: a sound's own radius replaces the actor's SoundRadius; SAmbientSoundTrigger
  `bToggle=False`, `bInitialyOn=True` restarts from the beginning on Trigger after an UnTrigger.
- SteeE.sdc exports listed in the table.
- Natives: PlaySound `0x10a25e50`, StopSound `0x10a25720`, PlayMusic `0x10a25c60`, StopMusic `0x10a25d10`,
  GetCurrentMusic `0x10a25e20`.

## Open questions

1. Does a remote client hear `Squ_Mus_Stress` when the crane blows the wall, and does the server (listen host) play
   it too? Scenario: listen host plus one remote client; complete the crane hack; record both machines' music and the
   crane debris emitters. This settles the engine rule for client calls on unowned always-relevant actors (also used
   by `Emitter.ReplicateTrigger` and SGamePlayObject `SpawnDamageEmitter`); native call path in ProcessRemoteFunction.
2. Server-side `PlaySound` reach: which players receive it (radius test, occlusion, owner exclusion). Audio spec.
3. Answered: `PlayMusic` ignores the trigger's `SoundVolume` (UUNIAudioSubsystem::PlayMusic, game 0x10b04920, plays
   at the music volume setting with no actor).
