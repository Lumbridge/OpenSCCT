# Native audio effects

`ScctEaxReverbEffect` and `ScctEaxSourceFilter` are native Godot `AudioEffect` resources.
Their `AudioEffectInstance._process` callbacks execute on Godot's mixer thread. They do
not capture the mixer into C#, wait for the scene thread, or queue generator playback.
Godot documents that this callback requires a native extension:
[AudioEffectInstance](https://docs.godotengine.org/en/4.5/classes/class_audioeffectinstance.html).

The production backend uses the MIT-licensed reAL HRvb software kernel, pinned to
`b68e4d12c526c0155af702e92b95dcf78c0e790b`. It implements Creative ALchemy's reverb
network and stereo converter directly. Godot owns the physical output device.
The complete 112-byte EAX 3 environment, including size and flags, reaches the
kernel unchanged. This replaces the previous Freeverb parameter approximation.
Independent Creative-driver captures and comparison tooling live under `tools/audio`.
The tracked `scct_hrvb_kernel.h` adaptation retains Creative's original gain/tap
transitions and block scheduling. Its complete upstream permission notice is
included beside it in `LICENSE-reAL.txt` as well as the packaged notices.

## Build and distribution

Windows requires Visual Studio C++ tools, a Windows SDK, Python 3.10+ and Git:

```powershell
pwsh tools/audio/Build-NativeAudio.ps1
```

Linux requires a C++20 compiler, CMake 3.24+, Ninja, Python 3.10+, Git and the pinned
Godot editor:

```bash
GODOT=/path/to/pinned/godot bash tools/audio/build-native-audio.sh
```

Both scripts dump the actual editor's extension API, build a minimal godot-cpp
binding set, and stage the native libraries under `game/addons/scct_audio/bin`.
Exact dependency commits are pinned in `CMakeLists.txt` and `hrvb.cmake`. Godot's
`.gdextension` manifest exports the platform extension and its CPU spatializer companion.
The Windows smoke/export and Linux build pipelines invoke these scripts before
import/export. Release packaging includes the selected backend's dependency licenses.
Generated libraries, dependencies and game-derived
capture evidence are ignored by Git.

An optional OpenAL Soft comparison backend can be built with `-Backend OPENAL` on
Windows or `SCCT_AUDIO_REVERB_BACKEND=OPENAL` on Linux. It is retained for investigation,
not selected as an automatic fallback. Its dependency remains dynamically linked;
the complete patched source archive is packaged with its LGPL notices.
`patch_openal_callback.py` changes only this comparison backend's synchronous unity-rate callback adapter:
the unity-rate direct-copy path does not request unused resampler lookahead, and a
live callback is retained across block boundaries. Reverb coefficients and the
upstream EAX algorithm are unchanged. Native tests check block partition invariance;
the extension reports its measured adapter latency through `get_latency_samples()`.

## Resource API

- `ScctEaxReverbEffect.configure_eax(PackedFloat32Array)` accepts the complete original
  28-field EAX 3 listener block, in declaration order: Environment, EnvironmentSize,
  EnvironmentDiffusion, Room, RoomHF, RoomLF, DecayTime, DecayHFRatio, DecayLFRatio,
  Reflections, ReflectionsDelay, ReflectionsPanXYZ, Reverb, ReverbDelay, ReverbPanXYZ,
  EchoTime, EchoDepth, ModulationTime, ModulationDepth, AirAbsorptionHF, HFReference,
  LFReference, RoomRolloffFactor, Flags. Integer fields are numerical values, not
  reinterpreted float bits. Invalid whole blocks are rejected without changing the
  current environment, matching the original provider.
- `source_gains(distance, radius, distance_factor, rolloff)` returns original default
  EAX 3 source gains: direct, directHF, room, roomHF.
- `ScctEaxReverbEffect.configure(PackedFloat32Array)` accepts 27 values in native
  `EFXEAXREVERBPROPERTIES` order: density, diffusion, gain, gainHF, gainLF, decayTime,
  decayHFRatio, decayLFRatio, reflectionsGain, reflectionsDelay, reflectionsPanXYZ,
  lateReverbGain, lateReverbDelay, lateReverbPanXYZ, echoTime, echoDepth,
  modulationTime, modulationDepth, airAbsorptionGainHF, HFReference, LFReference,
  roomRolloffFactor, decayHFLimit. This converted interface supports comparison tests;
  the game uses `configure_eax` to preserve original parameter precision. Gains are
  linear; delays and decay are seconds.
- `ScctEaxSourceFilter.configure(PackedFloat32Array)` accepts overall amplitude gain,
  relative HF **power** gain, and HF reference in Hz. At the reference frequency,
  the relative amplitude is the square root of the HF power gain. Its first-order
  coefficient uses Creative's original 1/32768 quantization.
- Reverb provides `backend_version()`, `get_error()` and `get_latency_samples()`.

Both effects output their processed signal without an additional dry signal.
The game routes dry and wet sends explicitly. Configuration is copied atomically
and applied on the mixer thread; a resource update preserves its effect instance.
Silence processing starts only after input and ends after a bounded decay tail.
Godot retains each resource until its mixer instance is retired; the instance owns
and releases its native DSP context.

## Headphone spatializer

`ScctBinauralEffect` wraps the standalone CPU binaural effect from Valve's Steam
Audio 4.8.1 SDK. `steamaudio.cmake` pins the official archive by SHA-256. Both
platforms ship `phonon` beside the extension and include the Apache-2.0 and SDK
third-party notices; the GDExtension manifest exports the companion library.
Godot still owns output, decoding and device changes. No Steam client, GPU
runtime, Steam Audio simulation scene or acoustic bake is required.

- `configure([right, up, backward, enabled])` accepts listener-relative source
  direction and an explicit 0/1 bypass. Zero direction resolves to forward.
- `reset()` publishes a new source generation; the mixer clears its convolution
  history and adapter queues before processing the next source.
- `get_latency_samples()` reports the **128-sample adapter** (2.667 ms at 48 kHz),
  excluding the HRTF's physical impulse delay and Godot/device output buffering.
- `get_metrics()` returns calls, frames, total DSP nanoseconds, maximum call
  nanoseconds and individual-source deadline overruns. These are not whole-mixer
  underrun counters. `get_error()` reports initialization/processing failures.

The built-in RMS-normalized HRTF is shared per mix rate and constructed before
the bus effect is installed. Effects, convolution buffers and block queues are
preallocated. Mixer callbacks perform no I/O, managed calls, allocation or
geometry queries. Blocks are adapted independently of Godot callback size;
normal completion drains convolution tails. Private dry buses receive unpanned
Godot mono (equal-power stereo); the adapter reconstructs mono before HRTF
rendering. The room source filter delays its send by the same 128 samples in
headphone mode. Speaker mode bypasses both binaural processing and that delay.

SDK tests cover 24, 44.1 and 48 kHz. The built-in HRTF cannot initialize at every
possible Godot mix rate (96 kHz is rejected by this SDK); an unavailable
spatializer retains speaker panning and emits an explicit warning. Godot's
default 48 kHz mix is independent of the output device's hardware rate.

For an existing extracted SDK use Windows `-SteamAudioSdk <steamaudio-folder>`
or Linux `SCCT_STEAMAUDIO_SDK=<steamaudio-folder>`. Otherwise CMake downloads and
verifies the pinned archive. `tools/audio/test-binaural-dsp.py` exercises the
actual C ABI for block partitioning, in-place buffers, axes/elevation, reset,
tail drain and a 32-source CPU measurement. Its timing is host-specific evidence.
