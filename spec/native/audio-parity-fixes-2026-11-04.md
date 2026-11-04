# Audio parity repairs — 4 November 2026

Follow-up to the [full audio audit](audio-parity-2026-11-04.md). The ten confirmed code/content defects are repaired
locally. Reverb was subsequently replaced and measured in the [reverb follow-up](reverb-parity-2026-11-04.md). No release, commit,
remote pack publication or service deployment was performed. Unrelated concurrent changes were preserved.

## Repairs

| Audit item | Result |
|---|---|
| A-01 voice volume | Removed the second output-volume multiplication from decoded PCM. Per-player volume still applies there; output volume applies once on SvmVoice. Echo reference follows actual bus/Master gain and mute. Real mixed PCM measures 50.3% and 25.1% amplitude at the 50% and 25% settings. |
| A-02 microphone settings | Active online/hub capture and Options meter reapply device changes immediately. Missing/failed devices retry at a bounded five-second interval; disabling/re-enabling or changing visits resets capture state. Hardware/permission acceptance remains separate from deterministic lifecycle tests. |
| A-03 footstep cues | Restored spy walk/run noise notifications. Authority delivers remote listeners' notifications once; the predicted local owner retains its local call, and replay/proxy paths suppress duplicate cues. A two-client test verifies exactly one remote Merc echo and no owner echo. |
| A-04 omitted slots | Actor PlaySound/PlayOwnedSound and the event helper default to SLOT_Misc. Explicit SLOT_None still permits independent overlapping sources. The real mixer regression fills 32 sources and checks ordinary same-slot replacement, bNoOverride rejection and equal-priority SLOT_None rejection. |
| A-05 wildcard stops | None sound survives simulation, reliable device relay and ordered pawn delivery. Mixer supports actor-wide stops and global non-music stops. Controller stops find their listener-bound pawn voices after possession changes. Unresolved network actor references cannot become global stops. Server reinit entry and client reinit completion use the restored cleanup; replay suppresses stop events. |
| A-06 voice bindings | Online, hub and generic voice use shared BindingInput state, including binding capture/focus suppression, held-key recovery and single mute edges. |
| A-07 speaker controls | Remote-player rows support click mute/unmute and scroll volume. Muted rows remain visible for unmuting; own rows ignore mouse. Online controls use existing name-keyed persistent settings. Generic overlay uses its VoiceChat controls. |
| A-08 content | Launcher fetches missing/outdated Entry and Lobby banks despite their having no playable modes. Bank streams retain complete source definitions and decode without a corresponding shared definition. Converter rejects unresolved authored AmbientSound. Local Entry/Lobby/KrauE packs were repaired through ContentStore; all 2,148 shared sounds and 24 restored lists remain unchanged. |
| A-10 dampening | Actor playback applies instigator SoundDampening for self/owner cases, for both ordinary and owner-predicted calls. Tests retain explicit volume/slot behavior and exclude unrelated instigators. |
| A-11 targeted music | Server forwards the original reliable MusicTrigger call. Each recipient evaluates bInstigatorOnly, current music and toggle/stop itself. Dedicated/listen worlds are covered; the resulting listen-host music event is not broadcast a second time. |

The stop relay adds an actor-scope bit and changes the device-event signature to `simevent2`. **Clients and servers
need matching builds**; protocol hashes reflect that signature. Deployed servers and client versions are unchanged.

## Installed pack repair

Published menu archives were authentic older content: roughly 125-second tracks, rather than B5's Entry song
(62.69381 s) and Enhanced Lobby song (188.28453 s). Merely downloading them would restore music without B5 parity.
Only the necessary audio tables/streams were rebuilt from the owner's B5 source, preserving installed shared audio.

- Entry: 8 source-identical bank streams; 30 installed files.
- Lobby: 8 source-identical bank streams; 30 installed files.
- KrauE: 10 source-identical bank streams; 162 installed files. Only its audio table and manifest changed; missing
  ZoneInfo328 DefT_Ext1 ambience is defined by the map bank itself.
- Shared audio stays SHA-256 `430fbfe1a7700666e349374e2ecbf27a74e857e19f6263f68da1bdcd045fa991`, with 2,148 sounds.

Repaired file sizes, manifest hashes, source bytes and Ogg CRCs pass. Backups and installation receipts are under
ignored `local/audio-parity-audit/backup`, `repair-content-receipt.json`, `repair-menus-receipt.json` and
`repair-validation.json`. Corrected archives have explicit local versions/hashes; the public index is unchanged.
An update against that index can restore its older archives. Publishing corrected packs is required for durable
distribution; this local repair does not claim a published content fix.

## A-09: superseded by the reverb follow-up

The following describes the state before the user's reverb follow-up. The Freeverb approximation has now been
replaced with a licensed Creative-compatible HRvb kernel and independent original-provider PCM comparisons;
see [current implementation and evidence](reverb-parity-2026-11-04.md). Doppler, zoom, speaker calibration and
physical-device listening are separate from that reverb verification.

Godot Freeverb has not been relabeled as exact I3DL2/EAX. Additional retail inspection confirms that Update calls
`0x10b0c420`, forwarding the environment structure through the backend property-set interface when available.
The supplied install contains EAX Unified 3.04 and Creative ALchemy 2.4.2.18. Static evidence does not establish
the active runtime device path or its impulse response (`local/audio-parity-audit/native-reverb-set.json`).

A replacement needs a compatible effect implementation and paired reference-output measurements. Godot's custom
effect callback is a native pointer API, not a supported C# override
([AudioEffectInstance documentation](https://docs.godotengine.org/en/4.7/classes/class_audioeffectinstance.html)).
Changing Freeverb's wet/decay settings without reference evidence would introduce another uncalibrated approximation.
Independent early/late reflections, RoomHF/RoomLF, diffusion/density, room rolloff, EAX obstruction and exact
speaker/Doppler/zoom calibration therefore remain outside the verified repairs. Exact acoustic parity is not claimed.

## Verification

- Game solution builds with zero warnings/errors; Godot import generates new C# UID sidecars.
- **327 focused tests pass:** 127 launcher, 19 Python audio, 89 gameplay/network audio and 92 voice. Zero failures/skips.
- **Eight mixer/gameplay smokes pass:** audio, audio-listener, play-audio, play-audio-merc, play-audio-krau,
  voice-output, voice-lifecycle and voice. Audio now decodes a self-contained bank against an empty shared library
  and checks wildcard/controller stops and the full 32-source allocation cases.
- Voice-output captures real PCM after bus volume without microphone capture. Voice-lifecycle covers 16 repeated
  visits, online/hub binding suppression and controls. Generic voice smoke emits actual Label.GuiInput signals
  for mute/unmute/scroll. Its synthetic 440 Hz tone decodes at 439.7 Hz within the bandwidth budget.
- Own near/far movement amplitude remains 1.000; remote far/near remains 0.1707.
- All 26 repaired bank streams match source bytes, with valid manifest hashes and CRCs.

Logs and PCM are under `local/audio-parity-audit/fixes-mixer`, `voice-fixes`, `voice-overlay` and
`local/artifacts/audio-voice-*`. These checks do not certify physical microphone hot-plug/permissions, live internet
voice quality, Linux output-device behavior, every absent original sound import or original-versus-rewrite acoustics.
