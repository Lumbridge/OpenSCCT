# Independent original EAX reference

These tools compare against the owner's original Creative ALchemy `dsound.dll`,
using DirectSound 3D buffers and the EAX 3 property interface. They do not use the
replacement DSP to generate its own expected output.
The reference is the supplied game's Creative ALchemy software renderer on this
host. Other historical Creative hardware/OpenAL driver paths require their own
reference and are not certified by these measurements.

The Windows probe runs as x86, loads an explicitly named DLL, and creates silent
buffers for property-only checks. Supplying a WAV path plays one controlled
impulse and captures only the probe's own process tree through Windows process
loopback. It never opens a microphone or records another application, changes
the system volume, writes the original installation, or installs/registers a DLL.
Capture requires Windows build 20348 or later. Building requires Visual C++ and
the Windows SDK. The original vendor DLLs remain local, outside Git.

```powershell
python tools/audio/reverb_reference.py --content .content --out local/reverb-parity/reference-parameters
pwsh -File tools/audio/Build-OriginalEaxProbe.ps1
pwsh -File tools/audio/Capture-OriginalEaxReference.ps1 `
  -OriginalSystem 'C:\path\to\owned\SCCT\System'
```

The capture script copies the owner's `dsound.dll`, `eax.dll` and `dsound.ini`
into its evidence folder, records their hashes, and checks every EAX 112-byte
Set/Get round trip. Its representative map settings, isolated parameter
variations, dry control, repeat, and native null-zone fallback are all labelled
in `parameters.json`. The native listener field order is covered by
`tests/content/test_reverb_reference.py`.

To validate every distinct map setting without playing audio:

```powershell
python tools/audio/probe_eax_blocks.py `
  --probe local/reverb-parity/vendor-probe/Probe-OriginalEax.exe `
  --dll local/reverb-parity/vendor-reference/dsound.dll `
  --parameters local/reverb-parity/reference-parameters/parameters.json `
  --out local/reverb-parity/property-sweep.json
```

To analyze recorded impulses (NumPy and SciPy required):

```powershell
python tools/audio/analyze_reverb_ir.py `
  --directory local/reverb-parity/vendor-reference-captures `
  --directory local/reverb-parity/vendor-reference-controls `
  --dry local/reverb-parity/vendor-reference-controls/Dry.vendor.wav `
  --out local/reverb-parity/vendor-reference-analysis.json
```

The analysis removes the measured tail DC and capture-start latency before
subtracting the dry reference. It reports wet energy, onset, and octave-band T20
extrapolations. The supplied vendor has a 16-bit output quantization floor even
when captured as float32; very quiet tails and T20 fits near that floor need care.
Matching a property round trip or one scalar measure does not establish complete
audible equivalence. Repeated references, independent parameter changes, actual
map settings, distance checks, and time-varying modulation distinguish those claims.

Add `--candidate-directory <wet-only exports>` to the analysis command to compare
candidate PCM against those independent vendor recordings. It fits no gain and
reports differing sample counts, active sample counts, maximum PCM16 quantum
error, and absolute RMS error. The comparison accounts for the measured four-frame
dry HRTF alignment; `--candidate-frame` overrides the default input frame 12004.

The probe additionally accepts `--then=<112-byte block>` instead of a capture WAV
path for a silent valid-then-invalid state-retention check. Capture arguments after
seconds may include `4` or `16` to compare source occlusion structure lengths,
`--distance=<uu>` to set a 100 uu source minimum distance and the original's
listener factors (0.0127 metres/uu, rolloff 0.5), or
`--pattern=double` / `--pattern=late` and `--second-frame=<frame>` for time-varying
controls. `--pattern=tone` adds three deterministic quiet tones after an alignment
impulse. `--transition=<block>` / `--transition-ms=<time>` applies a new complete
listener block during playback and logs play/write cursors, capture frames and
elapsed time at the actual API call.
Pass `--driver-default` instead of a listener block to capture the initial EAX
environment without making any listener setter call.

API references: [Microsoft process-loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/),
[EAX SDK declarations](https://github.com/id-Software/DOOM-3/tree/master/neo/openal/include).
