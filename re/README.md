# re/: reverse-engineering tooling

Tools that turn the original game binaries into facts we can write specs from: a lock on the exact
build we study, a Ghidra project of the game and the editor built headless by scripts, and the names
and comments we add, exported as small JSON files that are tracked in git.

## Tracked vs local-only

| Tracked in git | Local only (gitignored) |
|---|---|
| `re/binaries.lock.json`: SHA-256, size and header facts of each studied binary | The game install itself (read-only, never copied) |
| `re/tools/*.py`, `re/tools/ghidra-env.ps1` | `re/.venv-ghidra/`: Python venv with PyGhidra |
| `re/ghidra/build.ps1`, `re/ghidra/scripts/*.py`, `re/ghidra/lib/*.py` | `local/ghidra/`: the Ghidra project `SVM` (`SVM.gpr`, `SVM.rep/`), logs, analysis markers |
| `re/ghidra/annotations/{game,editor}.json`: names, namespaces, function entry points and our comments, by address | `local/re-out/`: script reports and intermediate data (`*.analysis-stats.json`, `*-report.json`, `editor.natives.json`, `native-anchors.json`) |

Never commit decompiled C, disassembly, Ghidra databases, game files or anything else derived from
the game other than the annotation files above (names and addresses only).

## Requirements

- The reference install, default `%USERPROFILE%\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3`
  (override with `SVM_GAME_DIR`). It is only read.
- Ghidra 12.2 with its JDK, default `%USERPROFILE%\Desktop\ghidra_12.2_PUBLIC` (override with
  `GHIDRA_INSTALL_DIR`). Ghidra finds the JDK through its saved `java_home.save`; to force one for a
  single run, set `JAVA_HOME_OVERRIDE`.
- Python 3.10 through the `py` launcher.

## Rebuilding the Ghidra project

```powershell
python -I re\tools\lock_binaries.py --check   # the install must match the lock
re\tools\ghidra-env.ps1                       # once: creates re\.venv-ghidra from Ghidra's own wheels
re\ghidra\build.ps1                           # import, analyse, run the scripts, export annotations
```

`build.ps1` is safe to re-run. For each target, editor first and then game:

1. If the target has no marker in `local/ghidra/analysis/` (or with `-Reanalyze`), it imports the
   binary (`x86:LE:32:default`, compiler spec `windows`), runs auto-analysis, records the analysis
   baseline (`01_baseline.py`) and applies `re/ghidra/annotations/<id>.json` if it exists
   (`05_apply_annotations.py`).
2. It reopens the program with `-process -noanalysis` and runs the target's scripts, ending with
   `30_export.py`, which rewrites the tracked annotations file.

Options: `-Targets editor|game`, `-Reanalyze`, `-AnalysisOnly`, `-ScriptsOnly`, `-ProjectDir`,
`-ProjectName`, `-MaxHeap` (default `24g`). Close the project in the Ghidra GUI first; a project
open elsewhere is locked.

**From the binaries and the annotations alone** (no other repositories): `build.ps1 -Reanalyze
-AnalysisOnly` gives a freshly analysed project with every tracked name, namespace, function entry
and comment applied. The naming scripts in step 2 also skip their part when their inputs are absent.

To open the result, start Ghidra (`ghidraRun.bat`, or `support\pyghidraRun.bat` for Python
scripting) and open `local\ghidra\SVM.gpr`.

## Scripts

All scripts are Python run by PyGhidra (`@runtime PyGhidra`). Each one checks the program's SHA-256
against the lock and refuses any other build. Names we add are user-defined symbols; a name typed in
by hand is never overwritten by a script (the scripts keep track of the names they set themselves).

| Script | Program | What it does |
|---|---|---|
| `01_baseline.py` | both | After analysis: statistics, and a digest of every analysis comment so the export can leave those out |
| `05_apply_annotations.py` | both | Applies the tracked annotations file to a fresh import |
| `12_natives.py` | game | Creates and names every native exec thunk as `<CppClass>::exec<Function>` (A prefix for actors, U otherwise) from the reflection dump of the running game, the game's own `GNatives[index] = func` stores, and editor names by native index. Folded thunks get one label per extra native |
| `13_statics_vtables.py` | game | Labels the 438 static `UClass` objects (`<CppClass>::PrivateStaticClass`) and the 193 vtables (`<CppClass>::vftable`, owner = nearest native class), and types the vtable slots as pointers |
| `20_editor_guards.py` | editor | Names each guarded function from its `Class::Function` guard string, found through the MSVC exception tables (the string is pushed by the function's `catch(...)` handler) |
| `21_editor_natives.py` | editor | Names the editor's exec thunks from its `GRegisterNative(index, func)` calls; a differing guard name is kept as a comment |
| `30_export.py` | both | Exports our names, namespaces, function entries and comments to `re/ghidra/annotations/<id>.json` |

Inputs from other repositories are read-only and machine-specific, so their paths live in the
untracked file `local/re-inputs.json` (an environment variable or a script argument overrides it):

```json
{
  "natives": "C:\\path\\to\\natives_all.txt",
  "symbols": ["C:\\first\\choice\\scct_versus_symbols.json", "C:\\fallback\\scct_versus_symbols.json"]
}
```

- `natives` (`SVM_NATIVES_TXT`): the Bridge's reflection dump of native functions, used by
  `12_natives.py` and `21_editor_natives.py`.
- `symbols` (`SVM_SYMBOLS_JSON`): the RE+ symbol table `scct_versus_symbols.json`, used by
  `13_statics_vtables.py`. A list means the first file that exists.

## Annotation format

`re/ghidra/annotations/<id>.json`, format `svm.ghidra.annotations/1`: a header with the program
name, SHA-256 and image base, then `namespaces` (`path`, `kind`), `functions` (`address`,
`namespace`, `name`), `labels` (plus `primary`) and `comments` (`address`, `type` of eol, pre,
post, plate or repeatable, `text`). Entries are sorted by address, one per line, so diffs stay
readable. Edit names in Ghidra and re-run `build.ps1 -ScriptsOnly` (or just `30_export.py`) rather
than editing the JSON by hand.

## Binaries lock

`python -I re/tools/lock_binaries.py` rewrites `re/binaries.lock.json` from the install; `--check`
verifies it. Scripts load it through `re/tools/binlock.py` (`verify_file`, `verify_sha256`,
`locked_path`). Rewrite the lock only when we deliberately move to another build, and expect every
annotation file to need a fresh build afterwards.

`re/tools/upkg.py` reads package headers, names, imports and exports of `System/_PC_/*.u`, enough
for the lock and for the class hierarchy the scripts use to pick C++ class prefixes.
