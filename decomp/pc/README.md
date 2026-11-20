# Original PC and Steam

Steam app 13570, build 252084, contains two game executables. Open Steam's
installed game folder and find the paths below.

| Program | Relative path | Build manifest |
| --- | --- | --- |
| Campaign and co-op | `System/splintercell3.exe` | [Campaign](config/steam-252084-campaign.json) |
| Spies vs. Mercenaries | `Versus/System/SCCT_Versus.ex` | [Versus](config/steam-252084-versus.json) |

The stock Versus launcher is the smaller `SCCT_Versus.exe`. Its payload uses
the extension `.ex`. Enhanced and Reloaded have their own reference files and
hashes. The existing [PC research environment](../../re/README.md) identifies
which builds its annotations apply to.

## Verify the complete Steam pass

From the repository root, pass the installed game directory to the verifier.
Run in an x86 Visual Studio Developer PowerShell with MSVC 19.44.35229.0,
or use `--cl` to select that compiler explicitly.

```powershell
python decomp/pc/verify_build.py 'D:\Games\Steam Games\steamapps\common\Splintercell Chaos Theory'
```

The verifier checks each complete retail SHA-256 and each source receipt,
compiles in a fresh directory, and compares every byte in each named COFF
function section. Its output includes the retail file offset and length of
each successful comparison.

## Reproduce one unit

The campaign executable exports `_SND_fn_lGetSoundTextureCount@0` at RVA
`0x748e10`. This build returns a sound texture count of zero. The complete
function occupies three bytes, followed by alignment. The [C implementation](src/sound_texture_count.c)
uses the export's x86 calling convention.

Use an x86 Visual Studio Developer PowerShell with the compiler identified in
the campaign manifest. Set `$game` to the installed game folder. These commands
start in this directory, `decomp/pc`.

```powershell
$game = 'D:\Games\Steam Games\steamapps\common\Splintercell Chaos Theory'
$campaign = Join-Path $game 'System/splintercell3.exe'
$versus = Join-Path $game 'Versus/System/SCCT_Versus.ex'
python ../tools/progress.py verify pc steam-252084-campaign $campaign
python ../tools/progress.py verify pc steam-252084-versus $versus
New-Item -ItemType Directory -Force local | Out-Null
cl /nologo /O2 /c /Folocal/sound_texture_count.obj src/sound_texture_count.c
python ../tools/progress.py match pc steam-252084-campaign $campaign sound-texture-count local/sound_texture_count.obj
python ../tools/progress.py report --check
```

The match selects the named COFF symbol and checks its whole section. It
rejects pending relocations, unexpected length and mismatched retail hashes.
This unit establishes a working compiler comparison. The original toolchain
and flags for the complete game are still under study.

## Campaign audio

The retail export table identifies each entry point below. Each measured range
ends immediately after the function's return and excludes the following alignment.
RVA and file offset coincide in the campaign's measured code section.

| Export (without x86 decoration) | Retail RVA | Bytes | Recovered behavior |
| --- | ---: | ---: | --- |
| `SND_fn_lGetSoundTextureCount` | `0x748e10` | 3 | Returns zero. |
| `SND_fn_lGetSoundTextureIdArray` | `0x748e20` | 5 | Returns zero and leaves the output untouched. |
| `SND_fn_lGetSoundTextureId` | `0x748e30` | 8 | Returns −2 for every input. |
| `SND_fn_lGetSoundTextureName` | `0x748e40` | 5 | Returns zero and leaves the output untouched. |
| `SND_fn_rMulRealRealQuickSnd` | `0x74b1d0` | 20 | Multiplies two signed 16.16 values after discarding eight low bits from each. |
| `SND_fn_fGetDolbyLtCoef` | `0x74b350` | 33 | Looks up the left coefficient and negates it for the second half of the angle range. |
| `SND_fn_fGetDolbyRtCoef` | `0x74b380` | 33 | Performs the same lookup for the right channel. |

The texture stubs establish the stack argument widths. Their unused C argument
types are inferred. Each manifest entry records the compiler and flags used
to reproduce that function.

The Dolby queries reference the retail float tables at image addresses
`0x111e7838` and `0x111e7dd8`. Their C definitions use these fixed addresses
for this build. Both return paths are included in each 33-byte comparison.
The table data is outside the measured ranges.

## Versus data objects

The Versus payload has no export directory. Its C++ runtime type information
identifies the virtual tables for `clDataLong`, `clDataBin`, `clDataStr`
and `clDataRef`. The [Versus manifest](config/steam-252084-versus.json)
records the table RVA, zero-based slot and function boundary for each match.

Two functions return the byte size of a long and of a pointer range. A third
dispatches a formatting call through another slot on the same object. Five
more forward operations to the referenced object held by `clDataRef`.
Shared implementations count once. The C names describe the recovered
operations. The C calling conventions put the object in ECX and preserve
the retail layout of arguments passed on the stack.
