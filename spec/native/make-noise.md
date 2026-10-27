# MakeNoise and noise hearing

Units:
- nat:Engine.Actor.MakeNoise
- eng:AActor::CheckNoiseHearing

## Purpose

`Actor.MakeNoise(Loudness)` is the engine's noise for AI hearing. SBase calls it where a sound matters to an
artificial listener (the spy's weapon fire, movers opening and closing). The original game has no bots, and no SBase
controller state implements `HearNoise`, so in versus play the noise reaches nobody. The rewrite's bots
(`docs/AI.md`) are the only listeners: this spec says what the native computes so their hearing starts from the same
loudness the engine would hand to a listener.

## Rules

1. **Where it runs.** Nothing happens on a network client (NetMode 3) or when the actor has no Instigator.
2. **Dampening.** When the actor's Owner is its Instigator, or the actor is the Instigator itself, the loudness is
   multiplied by the Instigator's `SoundDampening` (Pawn default 1.0).
3. **Visibility.** When the Instigator's `Visibility` byte is below 2 (Pawn default 128), the loudness is multiplied
   by 0.3.
4. **CheckNoiseHearing(Loudness)** runs next. It needs the Instigator to have a Controller; otherwise nothing.
5. **Two noise slots per instigator.** The Instigator remembers its last two noises (time, spot, loudness): slot 1
   and slot 2. With `now` the level time:
   1. A noise is dropped when slot 1 was made less than 0.2 s ago, within 50 uu (distance squared below 2500) of
      this actor, and `0.9 x Loudness` is not above slot 1's loudness. The same test runs against slot 2.
   2. The noise is then stored: in slot 1 when slot 1 is older than 0.18 s; else in slot 2 when slot 2 is older than
      0.18 s; else (both recent) in slot 1 when it is within 50 uu of slot 1's spot and at least as loud as slot 1,
      or when slot 2's loudness is not above it; otherwise it is not stored (but still delivered). Storing writes the
      time, this actor's location and the loudness.
6. **Delivery.** When the Instigator is a player's pawn (or its controller's pawn is), every controller in the
   level's controller list whose Pawn is not the Instigator and that probes `HearNoise` receives
   `HearNoise(Loudness, NoiseMaker = this actor)`. When it is not a player's pawn, only controllers in the same zone
   as the noise maker receive it. There is no distance test in the native: hearing range is the listener's own rule.
7. **Listeners in SBase.** `HearNoise` is only named in state `ignores` lists of Controller and SPlayerController;
   no SBase class implements it, so players never react.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| visibility cut | Visibility < 2 | byte | decoded | execMakeNoise |
| low visibility factor | 0.3 | | decoded | execMakeNoise |
| repeat window | 0.2 | s | decoded | CheckNoiseHearing |
| repeat distance | 50 (2500 squared) | uu | decoded | CheckNoiseHearing |
| repeat loudness ratio | 0.9 | | decoded | CheckNoiseHearing |
| slot reuse age | 0.18 | s | decoded | CheckNoiseHearing |
| Pawn.HearingThreshold default | 2800 | uu | decoded | Engine.u Pawn defaults (AI category, no native reader in SCCT) |
| Pawn.SoundDampening default | 1.0 | | decoded | Engine.u Pawn defaults |

## Network

Server only (rule 1). Nothing is replicated.

## Evidence

- Game `AActor::execMakeNoise` 0x109b0150 (native 512), `AActor::CheckNoiseHearing` 0x109f1a50, the HearNoise
  event call 0x109f2150 (ProcessEvent of HearNoise when the controller probes it). Decoded 2026-10-23 from a private
  copy of the Ghidra project (`local/ghidra-work/ai`).
- SBase reflection: `HearNoise` appears only in `ignores` lists (`SPlayerController` states GameEnded, PlayerFlying,
  PlayerWaiting, WaitingForPawn, the back-to-wall states).

## Open questions

1. The zone test of rule 6 compares a field at the same offset on the controller and on the noise maker; it reads
   as the zone of each, not confirmed. Bots treat every noise as a player's (rule 6 first branch).
2. `HearingThreshold` has no reader in the SCCT native; bots use it as the hearing radius of a noise of loudness 1
   (radius `HearingThreshold x sqrt(Loudness)`, the UE2 AI convention), which is our choice, not the original's.
