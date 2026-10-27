# natives2 study scripts

Helpers used to decode the natives the ports still marked Estimated (results: spec/native/natives2-traversal.md,
natives2-movement.md, natives2-damage.md, natives2-events.md and ANSWERS-natives2.md). They work on a private copy
of the Ghidra project, local/ghidra-work/natives2, never on local/ghidra. Everything they write that comes from the
game (decompiled C, disassembly) goes under local/re-out/c/natives2/<program>/.

```powershell
robocopy local\ghidra-work\physics local\ghidra-work\natives2 /MIR /XF SVM.lock SVM.lock~ /XD logs
robocopy local\ghidra-work\transfer\reloaded local\ghidra-work\natives2\reloaded /MIR /XF *.lock *.lock~ /XD logs   # optional
$env:MAXMEM = '3G'
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\natives2\ghserve.py          # keep running (port 47351)
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\natives2\ghq.py --exec re\ghidra\scripts\natives2\apply_tracked.py game
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\natives2\ghq.py --exec re\ghidra\scripts\actions\a35_noreturn_fix.py editor
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\natives2\ghq.py --decomp editor 0x111e5640 0x111c5e00
re\.venv-ghidra\Scripts\python.exe re\ghidra\scripts\natives2\ghq.py --exec re\ghidra\scripts\natives2\disasm.py editor '{"FUNCS": ["0x111e8d70"]}'
python re\ghidra\scripts\natives2\write_annotations.py
```

| Script | Runs in | Does |
|---|---|---|
| ghserve.py / ghq.py | server / client | one PyGhidra process holding the editor and game programs of the copy (and Reloaded.Core when the optional copy exists, as program `reloaded`); `--decomp` writes decompiles, `--exec` runs a file inside the server with optional JSON globals, `save` saves the copy |
| apply_tracked.py | server | sets the function names of the tracked study annotations (actions, net, transfer, natives2) where the copy still has default names |
| sigs.py | server | names and C signatures from a `SIGS` list (names_table.py entries) |
| disasm.py | server | writes the disassembly of functions, with float values of constant operands, under local/re-out/c/natives2 |
| names_table.py | data | the names, signatures and plate comments this study added |
| write_annotations.py | plain Python | writes re/ghidra/annotations/natives2.json, leaving out addresses other tracked files already name |

Scripts run inside the server get `p` (the program), `progs`, `addr`, `find_func`, `decomp`, `decomps`, `monitor`,
`OUT`, `write_local` and `key` as globals.
