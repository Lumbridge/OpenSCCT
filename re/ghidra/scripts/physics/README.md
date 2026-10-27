# Physics study scripts

Helpers used to decode the native movement and collision code (results: spec/native/physics-*.md and
collision-*.md). They work on a private copy of the Ghidra project, never on local/ghidra itself:

```powershell
robocopy local\ghidra local\ghidra-work\physics /MIR
$env:MAXMEM = '3G'
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\physics\ghserve.py      # keep running
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\physics\ghq.py --exec re\ghidra\scripts\physics\types_from_reflect.py editor
```

| Script | Runs in | Does |
|---|---|---|
| ghserve.py / ghq.py | server / client | one PyGhidra process holding both programs; requests decompile (output to local/re-out/c/physics/), save, or run a Python file inside the server |
| types_from_reflect.py | server | class and struct types plus vtable structs from local/reflect/<program>.json, thiscall on class methods |
| widen.py | server | gives APawn the ASBasePawn layout and the controllers the ASPlayerController layout |
| ge_types.py | server | GE element struct and vtable types |
| label_classes.py | server | labels static UClass objects |
| name_events.py | server | names script event stubs from their FName index |
| sigs.py, rename.py, commit_params.py, type_vtables.py, fix_switches.py | server | signatures, names, decompiler parameter commits, typed vtable slots, undisassembled jump tables |
| match3.py | server | finds game copies of editor functions by float-constant fingerprints (the game is link-time optimised, so instruction shapes and vtable slots differ) |
| export_names.py | server | lists names added in the copy that the tracked annotations lack |
| fld.py, bits.py | plain Python | field and bool-bit lookup by offset in the reflection dumps |
| write_annotations.py | plain Python | writes re/ghidra/annotations/physics.json |

Scripts run inside the server get `p` (the program), `progs`, `addr`, `find_func`, `decomp`, `monitor` and `OUT` as
globals; a file passed to `ghq.py --exec` may define its inputs (TARGETS, RENAMES, ...) at the top.
