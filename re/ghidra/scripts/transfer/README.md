# Name transfer and Reloaded study scripts

Carry the editor's names to the game build, and read Reloaded.Core. Results: `spec/native/ANSWERS-transfer.md`,
`spec/native/reloaded-hooks.md`, `spec/native/reloaded-features-from-code.md`; names in
`re/ghidra/annotations/transfer.json`.

The Ghidra scripts run only on the transfer work copy (`local/ghidra-work/transfer/SVM` for the game and the
editor, `local/ghidra-work/transfer/reloaded/Reloaded` for Reloaded.Core.dll), never on `local/ghidra`:

```powershell
$env:MAXMEM = '3G'
re\ghidra\scripts\transfer\run.ps1 -Program 'SCCT Versus' -Scripts T15_vtable_functions.py,T10_export_features.py,T20_bsim_sigs.py -ScriptArgs 'query=ChaosTheory_Editor.exe'
re\ghidra\scripts\transfer\run.ps1 -ProjectDir local\ghidra-work\transfer\reloaded -ProjectName Reloaded -Program Reloaded.Core.dll -ReadOnly -Scripts T60_string_topics.py -ScriptArgs 'topics=<topics.json>'
```

| Script | Runs in | Does |
|---|---|---|
| run.ps1 | PowerShell | headless launcher for one program of a work copy project |
| T10_export_features.py | Ghidra, read-only | per-function features (calls, strings, constants, data refs, tokens) and vtables to `local/re-out/c/transfer/<id>.features.json` |
| T15_vtable_functions.py | Ghidra, writes | creates functions at vtable slot targets that analysis missed |
| T20_bsim_sigs.py | Ghidra | BSim vectors, database commit, nearest lists |
| T50_decompile.py | Ghidra, read-only | decompiles a list of functions and callees (local output) |
| T60_string_topics.py | Ghidra, read-only | decompiles the functions behind given strings, imports, globals or addresses, and their callers or callees (local output) |
| T61_xrefs.py | Ghidra, read-only | every reference to given addresses, with type and function |
| T62_disasm.py | Ghidra, read-only | instruction listing with loaded float constants (local output) |
| T70_apply_transfer.py | Ghidra, writes | applies `annotations/transfer.json` to the program |
| xfer.py | Python | loading, features, PE reading shared by the matcher |
| match_names.py | Python | the matcher (`--holdout 0.2` scores hidden natives) |
| write_transfer.py | Python | writes `annotations/transfer.json` from the matcher output |
| test_match_names.py | pytest | unit tests on synthetic data |

Outputs of the Ghidra scripts and the matcher stay in `local/`.
