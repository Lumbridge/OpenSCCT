# Actions study scripts

Helpers used to decode ExecuteAction, the animation natives, the input natives, the camera natives and the hit
pills (results: spec/native/execute-action.md, anim.md, input.md, camera.md, hit-zones.md and ANSWERS-actions.md).
They work on a private copy of the Ghidra project, local/ghidra-work/actions, never on local/ghidra itself.
Everything they write that comes from the game (decompiled C, case splits, pill tables) goes under
local/re-out/c/actions/.

```powershell
$env:MAXMEM = '3G'
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghserve.py          # keep running (port 47331)
# types and names on the copy (once per copy)
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghq.py --exec re\ghidra\scripts\physics\types_from_reflect.py editor
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghq.py --exec re\ghidra\scripts\physics\label_classes.py editor
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghq.py --exec re\ghidra\scripts\physics\widen.py editor
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghq.py --exec re\ghidra\scripts\actions\a35_noreturn_fix.py editor
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghq.py --exec re\ghidra\scripts\actions\a30_names.py editor
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghx.py editor re/ghidra/scripts/physics/commit_params.py '{"ROOTS": ["0x11215980"], "DEPTH": 2}'
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghx.py editor re/ghidra/scripts/physics/type_vtables.py '{"VTABLES": ["ASBasePawn", "AActor", "USkeletalMeshInstance"]}'
# decompile once, then split and compare offline
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\actions\ghd.py editor 0x11215980
python re\ghidra\scripts\actions\cases.py editor
python re\ghidra\scripts\actions\cases.py game local\re-out\c\actions\game\10ad6840_ASBasePawn__ExecuteAction.c
python re\ghidra\scripts\actions\compare_cases.py editor game
python re\ghidra\scripts\actions\pills.py "<game dir>" SPerso
python re\ghidra\scripts\actions\write_annotations.py
```

| Script | Runs in | Does |
|---|---|---|
| ghserve.py / ghq.py | server / client | one PyGhidra process holding both programs of the copy; decompile, save, or run a Python file inside the server |
| ghd.py | client | decompiles a list of functions through the server |
| ghx.py, run_with.py | client, server | runs another server script with input globals (reuses the physics typing scripts unchanged) |
| a10_types.py, a20_decomp.py, run.ps1 | headless post-scripts | the first pass (reflection types, batch decompile) |
| a30_names.py, names_table.py | server, data | names and signatures of the study (ExecuteAction helpers, anim channel natives, input, camera, pills) |
| a35_noreturn_fix.py | server | clears the wrong no-return flag on the log function and re-disassembles the code after its calls |
| cases.py | plain Python | splits the decompiled switch into one file per case group, follows shared labels and fall-through, annotates clip names, bool names and physics modes; `--annotate` annotates other decompiled files |
| compare_cases.py | plain Python | compares the case facts of two builds (editor and game) |
| pills.py | plain Python | reads the collision points, capsules and spheres of skeletal meshes from a package |
| write_annotations.py | plain Python | writes re/ghidra/annotations/actions.json from names_table.py |

Scripts run inside the server get `p` (the program), `progs`, `addr`, `find_func`, `decomp`, `decomps`,
`monitor` and `OUT` as globals.
