# Provenance and legal hygiene

This project studies a commercial game. What goes into git is our own work: ideas, numbers, names
and code written from our own specs. Anything taken from the game, or produced from it, stays on the
machine that made it.

## Class A: tracked in git

Our own work, safe to commit:

- C# and Python code written by us, including the RE tools and the Ghidra scripts in `re/ghidra/scripts`.
- Behaviour specs in `spec/`, in our own words: rules, constants, state diagrams, network role and
  evidence references. Short identifiers (class, function and field names, addresses) are fine; code
  is not.
- File format specs: `spec/svmap/1.0`, `spec/formats/{ge,collision,uas,mgm}`.
- Ghidra annotations exported to `re/ghidra/annotations/*.json`: names, types and comments only,
  never decompiled function bodies.
- `re/binaries.lock.json`: SHA-256 hashes of the game exe, the editor, `Reloaded.Core.dll` and the
  `*.u` packages. Hashes only, never the files.
- `coverage/status.yaml`, scenario files that we write (`tests/scenarios/*.scenario.json`), synthetic
  test data (`fixtures/`, the generated TestBox map), docs and our own images in `docs/images/`.

## Class B: local only

Everything from the game or derived from it. It lives in `local/` or `.content/`, both in
`.gitignore`:

- Game binaries and packages: the game exe, `ChaosTheory_Editor.exe`, `Reloaded.Core.dll`, `*.u`,
  `.sdc`, `.usx`, `.utx`, `.utc`, `.ukx`, `.uax`, `.uas`, `.mux`, `.int`.
- Ghidra projects and archives (`.gpr`, `*.rep`, `.gzf`), kept in `local/ghidra`.
- Decompiled UnrealScript and C, disassembly and bytecode listings.
- Reflection dumps, live reads, traces, goldens and screenshots of the original.
- Converted content from the `svmbuild` pipeline (`.content/`) and anything read from the game's
  class defaults (for example the `class_defaults.json` that `tools/scctpkg/sync.py` fetches).
- The game's text (`.int` strings, localized class defaults, map briefings and tips). Generated code reads it at
  run time from the player's content (`UcText`); it is never written into tracked files.

Tools that write decompiled output should start each file with a banner line naming the tool, so
the guard below can spot a stray copy.

## Guards

Three layers keep class B data out of git:

1. `.gitignore` covers `local/`, `.content/`, `re/.venv-ghidra/`, `tools/godot/` and the game file
   types.
2. `tools/hooks/pre-commit` runs `tools/ci/no_game_data.py --staged` on every commit. Enable it once
   per clone with `pwsh tools/hooks/install.ps1`.
3. CI runs `tools/ci/no_game_data.py` on every push and pull request.

The guard fails on game file types, the UE package tag `C1 83 2A 9E` at offset 0, PE executables
and DLLs, Ghidra project files, DDS, glTF, OGG, WAV and image files outside `fixtures/` and
`docs/images/`, files over 5 MB, local-only paths, and text that looks like decompiler output
(tool banners, Ghidra variable and function names, UnrealScript class bodies outside `spec/`,
bytecode and disassembly listings).

Hand-written samples for decompiler tests may hold UnrealScript or C that trips the text checks.
Such a file can carry a comment line, near the top, that holds only the marker
`no-game-data: synthetic`. The marker lifts the text checks for that file and nothing else, and a
reviewer should check that the sample really is hand-written.

## Evidence

- Every constant taken from the original carries an evidence tag:
  - **decoded**: read from the binary, the bytecode or the class defaults
  - **measured**: observed in the running original
  - **estimated**: a best guess, to be replaced
- Every spec names its evidence: a Ghidra address, a decompiler unit or a scenario.
- Coverage units have IDs with a kind prefix: `uc:` (UnrealScript), `nat:` (natives), `eng:` (engine
  code), `rep:` (replication), `def:` (defaults), `fmt:` (file formats), `rl:` (Reloaded.Core).
  Each unit is unported, specced, ported, verified or n-a. CI fails on unknown IDs, missing spec
  anchors and n-a without a reason.

## Rules

- Decompiled C and UnrealScript, disassembly, Ghidra databases and game binaries are never committed.
- Specs carry ideas and numbers. The C# is written independently, from the spec.
- `Reloaded.Core` is only observed. It is never patched or redistributed.
- Converted content is built by the pipeline from the project owner's own install and never
  committed. Releases ship it; how it is distributed is the project owner's decision.
- The reference game install is read-only. Tools read from it and write elsewhere.
- Runs of the original go through the Reloaded Editor test harness only: editor Play Level, headless,
  muted, one run at a time under its lock, never on public servers, never over the owner's own game
  session, and never entering a CD key. The recorder reads memory only by default. The in-process
  TestHook probe is used only with the owner's OK, and only if it does not trip Reloaded's integrity
  check.
- Our services never touch the original or Reloaded master servers.
