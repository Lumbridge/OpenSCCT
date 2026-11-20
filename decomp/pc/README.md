# Original PC and Steam

The Steam original covers campaign, co-op and Versus. The two native targets have separate hashes, toolchain evidence and progress totals. The desktop Godot conversion and the Unreal modernisation have separate build workflows.

| Target | File in Steam app 13570, build 252084 | Build manifest |
| --- | --- | --- |
| Campaign and co-op | `System/splintercell3.exe` | [Campaign](config/steam-252084-campaign.json) |
| Versus | `Versus/System/SCCT_Versus.ex` | [Versus](config/steam-252084-versus.json) |

The small Versus launcher has the extension `.exe`. Select the `.ex` payload for the original game. The [research environment](../../re/README.md) identifies Enhanced and Reloaded builds separately.

## Identify the installed binaries

Run the examples from `decomp/pc` with Python 3. Set `$game` to your Steam game folder. The tool checks each complete SHA-256 and its code-section inventory before reading an address.

```powershell
$game = 'D:\Games\Steam Games\steamapps\common\Splintercell Chaos Theory'
python workflow.py steam-252084-campaign $game inspect
python workflow.py steam-252084-versus $game inspect
```

## Study a function

Install Capstone 5.0.7 in your Python environment to use the optional `disasm` command. These two measured ranges use the preferred virtual address, followed by the byte count. Output stays on the contributor's machine.

```powershell
python -m pip install capstone==5.0.7
python workflow.py steam-252084-campaign $game disasm 0x11048e10 3
python workflow.py steam-252084-versus $game disasm 0x109467f0 108
```

For decompiler output, set `$ghidra` to Ghidra's `support/analyzeHeadless.bat`. The script checks the supplied image hash, disassembles the function and prints its address, size and pseudocode. Each command creates and removes a fresh project in the ignored `local/` folder. Use Ghidra's environment and JDK instructions for your platform.

```powershell
New-Item -ItemType Directory -Force local | Out-Null
$campaign = Get-Content -Raw config/steam-252084-campaign.json | ConvertFrom-Json
$versus = Get-Content -Raw config/steam-252084-versus.json | ConvertFrom-Json
& $ghidra local ("study-" + [guid]::NewGuid()) -import (Join-Path $game $campaign.file) -noanalysis -scriptPath $PWD -postScript InspectPcFunction.java $campaign.image.sha256 0x11048e10 -deleteProject
& $ghidra local ("study-" + [guid]::NewGuid()) -import (Join-Path $game $versus.file) -noanalysis -scriptPath $PWD -postScript InspectPcFunction.java $versus.image.sha256 0x109467f0 -deleteProject
```

## Compile and compare

The first independent [campaign C source](src/sound_texture_count.c) implements the sound catalogue's count query. Use an x86 Visual Studio Developer PowerShell for MSVC 19.44.35229 and install the [objdiff 3.8.3 CLI](https://github.com/encounter/objdiff/releases/tag/v3.8.3). Supply the CLI path using `--objdiff` if your executable is outside `PATH`.

```powershell
New-Item -ItemType Directory -Force local | Out-Null
cl /nologo /O2 /c /Folocal/sound_texture_count.obj src/sound_texture_count.c
python workflow.py steam-252084-campaign $game diff sound-texture-count local/sound_texture_count.obj
```

The comparison puts a single-function retail COFF target and the objdiff JSON report under `local/steam-252084-campaign/sound-texture-count/`. Open this directory's [objdiff project](objdiff.json) to inspect the named target and rebuilt instructions. The CLI also checks the entire rebuilt function for byte equality and pending relocations. Comparison output can guide the next source change.

The campaign experiment reproduced three retail bytes under two different compiler settings. The [original toolchain investigation](TOOLCHAIN.md) explains the evidence needed before counting this toward matching progress. The [central progress workflow](../README.md) owns verified receipts and published totals.
