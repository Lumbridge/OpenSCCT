# Creative EAX reverb parity — 4 November 2026

The Freeverb approximation has been replaced by a native Creative-compatible HRvb renderer. The implementation
uses the MIT-licensed [reAL kernel](https://github.com/PlayNeth/reAL), pinned to
`b68e4d12c526c0155af702e92b95dcf78c0e790b`, with the original first-apply LF shelf and late-pan routing quirks
restored. No original game or vendor DLL is redistributed. Changes are local; no release or deployment was made.

## Reference and scope

The owner's Enhanced SCCT Versus 4.0 Beta 5 install supplies Creative ALchemy 2.4.2.18 `dsound.dll`, SHA-256
`c79e62a9b02efbb6adf28a070c48c6c9e72ffb114a8aee134442cc82051dbbf9`, and EAX Unified 3.04. The retail
audio setter at `0x10b0c420` forwards the complete 112-byte EAX 3 listener packet. The installed configuration has
`UseEAX=True`. REA inspection of that setter, its caller, the source occlusion call, and this particular provider's
HRvb renderer was combined with controlled live provider output. This establishes the supplied Creative software
path; it does not claim identical behavior from other historical hardware/providers.

The reference harness loads the owner's DLL in an isolated process. WASAPI process loopback captures that process
only; it does not record a microphone or other applications, replace a system DLL, or change saved/system volume.
Synthetic PCM impulses and tones are used as inputs. Native setter HRESULTs and property readbacks are logged.
Input, listener properties, packet hashes, sample rate and original-provider captures are retained separately
from candidate output. Licensed implementation source and notices are packaged; proprietary reference files stay
in ignored `local/reverb-parity`.

## Implemented behavior

- The complete EAX 3 environment reaches the kernel without the former EFX/Freeverb conversion: room size,
  diffusion, Room/HF/LF gains, independent early and late delays/gains/pans, HF/LF decay ratios, echo, modulation,
  air absorption, frequency references, room rolloff and flags.
- One effect belongs to each listener. Zone selection updates its retained delay network; it does not switch
  between independent per-zone reverbs. Initial missing effects preserve Generic driver defaults. Leaving an
  effect for a missing effect applies the distinct native NULL packet.
- Changes follow the original's 17 successive 512-frame delay stages, dual-tap delay crossfades, 512-frame gain
  ramps and 128-frame filter/rotation/modulation updates in the same network. The original's four-sample coefficient
  holds and repeated FDN/LAP stage assignment are preserved. Updates arriving during a transition replace its
  pending target, and run when the current transition completes.
- The original provider accepts 218 of 232 distinct converted map packets. It rejects 14 whole updates (137 zone
  instances): authored sizes below 1, diffusion above 1, or echo time above 0.25. The original caller ignores that
  failure and records the selected pointer. The rewrite preserves the previous environment and tail in these
  cases, rather than clamping and applying a different room.
- Positional sources have separate dry and unpanned room sends. Original source distance conversion and mB
  quantization determine broadband direct/room gains and relative room HF filtering. Source air absorption's
  default factor is zero. The room filter uses Creative's one-pole/Q15 power-gain convention.
- The original source-occlusion setter sends four bytes to EAX 3 OCCLUSIONPARAMETERS, which requires 16. Controlled
  Set/Get proves that the installed provider rejects this request and retains unoccluded filter defaults. The
  rewrite preserves that observed state on its Creative path, while retaining original occlusion allocation/range
  behavior. The separately decoded non-EAX fallback still adjusts minimum distance.
- Flat sounds and music bypass reverb. Dry audio receives SFX volume once on `SvmWorld`; wet audio receives it once
  on the environment bus, which sends directly to Master. New environments inherit the current SFX bus level.
  Stop, rebind and scene exit retire owned source/environment buses and native resources.
- Dry/room source initialization and paired playback start/stop occur under the audio-server lock. Initial gain,
  pitch, filters, location and bus routing are installed before either send can reach the mixer, including when a
  pooled voice replaces another sound. The mixer regression requires identical dry/send onset sample positions.
- Native callbacks process Godot mixer buffers directly. Parameters cross to the mixer atomically. Scratch and
  delay storage are allocated when the effect is created; there is no managed mixer capture/playback queue.

## Independent measurements

At 48 kHz with a 0.1-amplitude synthetic impulse, the strict HRvb candidate was compared with 15 non-dry original
provider fixtures: Room, StoneCorridor, Sewerpipe, Hangar, native NULL, and isolated gain/delay/diffusion/spectral
changes. After the independently measured four-sample direct-HRTF alignment and original PCM16 quantization,
each eight-second capture differs by at most **one PCM16 LSB**, in **0–12 samples**; maximum absolute RMS error is
`1.24e-7` full scale. Six fixtures are sample-identical. These are waveform-error measurements, not a feature
coverage percentage. The rejected OpenAL EFX candidate was materially different and is retained only as an
explicit investigation backend, never an automatic production fallback.

A real MissD map packet with modulation depth 0.7/time 0.25 was also tested with early, late and paired impulses
separated by 1.37 seconds. The candidate follows the original's continuous modulation state: 2–4 differing samples
per capture, each one PCM16 LSB, RMS error at most `7.13e-8` full scale. This tests time-dependent behavior beyond
a substituted static impulse response.

Continuous-tone gain changes and a Hangar-to-Room change during a sounding impulse tail verify transitions
independently. The complete 17-by-512-frame gain-transition window and its following tail are PCM sample-identical
to the original provider. The changed-topology transition differs by at most one PCM16 LSB. Reference API
play/write cursors establish the actual change frame; the comparison does not shift or fit gain to hide the
transition. The in-place update preserves the old tail while changing delayed taps and filter/gain state.
The wet-region comparison excludes one millisecond on either side of the second dry impulse: subtracting
the one-shot dry control there leaves up to four LSB of warmed direct-HRTF mismatch. The transition window itself
has no such exclusion; this is not a claim of bit identity for the entire unmasked mixed capture.

Two directional controls (early XYZ pan and late right-30-degree pan) are PCM sample-identical to the original,
including the restored SR/FR routing quirk. Four transitions into and out of the valid `RoomHF=-10000` and
`RoomLF=-10000` endpoints differ by at most one PCM16 LSB across the full transition. These use the original's
output-envelope fades at zero coefficients, rather than interpolating the filter coefficient through zero.

The real Godot mixer smoke independently captures room input, wet output and dry output. Replaying its captured
input through the standalone kernel with the same environment produces **bit-identical wet output**, maximum/RMS
error zero and fitted gain 1.0. The room input matches the mono source level within the measured 3D panner
normalization tolerance. Early wet arrival follows the authored delay; no extra dry impulse appears in the wet
output. Flat/music isolation, initial Generic, rejected updates, NULL fallback, retained effect identity and owned
bus cleanup are checked in the same real-output run.

Windows and Linux packaged libraries each pass 14 kernel checks, including active-transition callback partition invariance (maximum difference zero),
zero added adapter latency, in-place processing, separate early/late paths, sample-exact delay shifts, filter
frequency response, reset and rejected-update tail retention. Both extensions load and instantiate in the actual
pinned Godot 4.7.2 Mono editor and exported executables. Exported native library hashes match the tested staged
libraries on both platforms; Linux was checked inside an isolated Ubuntu 24.04 glibc environment. Sixteen fixture
renders from the packaged Windows and Linux libraries are float sample-identical. Eleven managed
environment/source-vector regressions and 19 Python audio/reference tests pass. Game builds with zero warnings or
errors. Five real Windows output smokes pass: `reverb`, `audio`, `audio-listener`, `play-audio-merc` and
`play-audio-krau`. The existing listener PCM ratios remain 1.000 local and approximately 0.171 remote.
A preceding Merc run timed out waiting for its local pawn; the final rerun passed. No audio-output failure was
reported in that startup timeout.

These measurements cover the supplied provider's stereo path at the measured rate, not physical headphone/speaker
calibration, every output layout, a subjective listening comparison, or unrelated Doppler/zoom approximations.

## Reproduction and retained evidence

- [Native build/API](../../src/OpenScct.Audio.Native/README.md), [Windows build](../../tools/audio/Build-NativeAudio.ps1),
  [Linux build](../../tools/audio/build-native-audio.sh), [kernel regression tool](../../tools/audio/test-native-dsp.py).
- [Original packet inventory](../../tools/audio/reverb_reference.py),
  [provider harness](../../tools/audio/Probe-OriginalEax.cpp), and its capture/comparison tools in `tools/audio`.
- Game: `--svm-smoke=reverb`; `SVM_REVERB_CAPTURE_DIR` chooses its synthetic fixture and captured WAV/JSON folder.
  Run with a real output mixer; a silent/dummy backend does not establish PCM output parity.
- Ignored local evidence: `reference-parameters/parameters.json`, `all-map-property-roundtrips.json`,
  `invalid-sequential-roundtrips.json`, `source-occlusion4.vendor.json`, `source-occlusion16.vendor.json`,
  `hrvb-original-vendor-pcm-comparison.json`, `modulation-reference/candidate-comparison.json`,
  `transition-candidate-v2/independent-comparison.json`, `endpoint-reference/independent-comparison.json`,
  `godot-final-pcm/independent-kernel-comparison.json`, `game-final-smokes/smokes-extended.json`,
  `final-hrvb-windows/native-dsp-checks.json`, `final-hrvb-linux/native-dsp-checks.json`, native build/probe logs and
  the `native-hrvb-transition*.json` REA exports. Addresses in these files belong to the recorded artifact hashes.
