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
