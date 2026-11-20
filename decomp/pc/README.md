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

## Reproduce the first unit

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

The texture stubs establish the stack argument widths. Their unused C argument
types are inferred. Each manifest entry records the compiler and flags used
to reproduce that function.
