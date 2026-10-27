# SNotifyNoise (repeating noise marker)

Units:
- def:SBase.SNotifyNoise
- uc:SBase.SNotifyNoise:ChangeStateToAll
- uc:SBase.SNotifyNoise:PostNetBeginPlay
- uc:SBase.SNotifyNoise:Timer
- uc:SBase.SNotifyNoise:Trigger
- uc:SBase.SNotifyNoise:UnTrigger
- uc:SBase.SNotifyNoise:s_Wait.BeginState
- uc:SBase.SNotifyNoise:s_MakeNoise.BeginState
- rep:SBase.SNotifyNoise.bEnabled
- rep:SBase.SNotifyNoise.ChangeStateToAll

## Purpose

While enabled, a point that keeps making a noise for the merc's sound arrows (the HUD's NotifyHotSound). On the
Mission maps (MissD, MissE, MissP) one actor tagged `sound_echo` is switched on by an SMagicEvent.

## Data

| Field | Type | Default | Meaning |
|---|---|---|---|
| `bEnabled` | bool, edit, rep | false | making noise |
| `fTimeRateInSec` | float, edit | 2.0 | seconds between noises, at least 1 |
| `fIntensity` | float, edit | 1.0 | loudness passed to the HUD |
| Defaults | | | `RemoteRole=ROLE_SimulatedProxy`, not movable, no zero-extent blocking, `native`, `nativereplication` |

## Rules

1. `PostNetBeginPlay` (every machine, a late client too): `fTimeRateInSec` below 1 becomes 1, draw type None, then
   `ChangeStateToAll(bEnabled ? s_MakeNoise : s_Wait)`.
2. `ChangeStateToAll(State)`: `GotoState(State)`. From the server it is broadcast to every client with a channel and
   runs on the server too; on a client it runs locally.
3. `s_MakeNoise.BeginState`: a repeating timer of `fTimeRateInSec`. `s_Wait.BeginState`: timer off.
4. `Timer` (every machine): the local player controller's `ClientNotifyNoise(self, fIntensity)` (the HUD's sound
   arrow toward the actor; spec/hud/merc-reticle.md).
5. `Trigger`: `bEnabled = true`, `ChangeStateToAll(s_MakeNoise)`. `UnTrigger`: `bEnabled = false`,
   `ChangeStateToAll(s_Wait)`.

## Map data

All three: `Tag=sound_echo`, `fIntensity=0.05`, `fTimeRateInSec=1.0`, `bEnabled` false (switched on by the map's
SMagicEvent group).

## Network

`bEnabled` replicates (script set: the class has no list of its own decoded); `ChangeStateToAll` is a reliable
broadcast. Each machine runs its own timer, so the noise is never sent per tick over the network; the arrow is
local.

## Evidence

- `local/re-out/uc/SBase/SNotifyNoise.uc`, `SBase/SPlayerController.uc` (ClientNotifyNoise).
- `.content/svmap/Miss*/sim/gameplay.json`.

## Open questions

1. `ASNotifyNoise` is a native class; its vtable was not among the decoded ones, so a native list or tick of its own
   is not ruled out.
