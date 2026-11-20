# PlayStation 2 originals

The PAL disc for Splinter Cell Chaos Theory identifies itself as `SLES-53287` in `SYSTEM.CNF`, version `1.01`. The boot program, offline game and online game have separate hashes and progress records.

| Retail program | Manifest | Current evidence |
| --- | --- | --- |
| `SLES_532.87` | [Disc boot](config/sles-53287-boot.json) | PCSX2 executes the boot entry at EE address `0x00200008`. |
| `SC3_OFF.ELF` | [Offline](config/sles-53287-offline.json) | Identified executable for offline game research. |
| `SC3_ON.ELF` | [Online](config/sles-53287-online.json) | A complete eight-byte accessor has a candidate C rewrite with identical machine code. |

## Prepare a local disc

Extract those three files and `SYSTEM.CNF` from your own ISO with 7-Zip into `decomp/ps2/local/disc/`. Run the following from the repository root. Each command verifies the file against its own size, format, executable sections and SHA-256. For a different release, run `python decomp/tools/progress.py identify PATH_TO_ELF` and create a separate manifest for that build.

```sh
python decomp/tools/progress.py verify ps2 sles-53287-boot decomp/ps2/local/disc/SLES_532.87
python decomp/tools/progress.py verify ps2 sles-53287-offline decomp/ps2/local/disc/SC3_OFF.ELF
python decomp/tools/progress.py verify ps2 sles-53287-online decomp/ps2/local/disc/SC3_ON.ELF
```

Build the analysis environment once. The build context contains only tool configuration. Then start the interactive shell to run the remaining commands. The source checkout is mounted at `/work`. In PowerShell, substitute `source=${PWD}` for `source=$(pwd)` if needed.

```sh
docker build -t openscct-ps2-tools decomp/ps2/toolchain
docker run --rm -it --network none --mount "type=bind,source=$(pwd),target=/work" openscct-ps2-tools bash
```

Linux users with the versions in the [Dockerfile](toolchain/Dockerfile) and [Python requirements](toolchain/requirements.txt) can run the same commands natively.

## Disassemble

```sh
python decomp/ps2/workflow.py disassemble sles-53287-online decomp/ps2/local/disc/SC3_ON.ELF --unit read-second-word --draft
```

The workflow checks the entire ELF, maps the EE virtual address to a file offset, and splits the selected range using splat's PS2 platform and spimdisasm. Look in `decomp/ps2/local/work/sles-53287-online/read_second_word/` for the generated symbol list, splat configuration, assembly and m2c draft. The m2c target is `mipsee-gcc-c`. Its types, field names and interpretation need review. Splitting eight bytes accounts for that function alone. The executable sections mix instructions and data, so the percentage of the complete program remains unknown. [splat](https://github.com/ethteck/splat) · [spimdisasm](https://github.com/Decompollaborate/spimdisasm) · [m2c](https://github.com/matt-kempster/m2c)

Use `--range EE_ADDRESS SIZE` instead of `--unit ID` to inspect another aligned range. For example, the trace predicate starts at `0x00178EC0` in the online build. Check its callers, control flow and delay slots before assigning a complete function boundary. [PS2 world collision research](../../spec/native/ps2-world.md)

## Rewrite

Two direct calls at `0x0018AA74` and `0x0018BE38` target `0x0018B4F0`. This leaf returns the signed 32-bit word four bytes past its first argument. The load executes in the return delay slot. The two instructions span eight bytes, beginning at file offset `0x8B5F0`. The independently written [C source](src/read_second_word.c) uses a descriptive name. The retail symbol and containing game type are unknown.

For another candidate, add independently written C under `src/`, plus its source, compiler, flags, symbol, address, file offset and evidence to the corresponding build manifest. Preserve local disassembly, decompiler drafts and emulator traces in `local/`. The [source-data rules](../../re/PROVENANCE.md) explain what contributors can publish.

## Verify

```sh
python decomp/ps2/workflow.py verify sles-53287-online decomp/ps2/local/disc/SC3_ON.ELF --unit read-second-word
python decomp/tools/progress.py match ps2 sles-53287-online decomp/ps2/local/disc/SC3_ON.ELF read-second-word decomp/ps2/local/work/sles-53287-online/read_second_word/candidate.bin
```

The first command compiles the C source for the R5900 with GNU GCC 12.2, reads the complete ELF function symbol and rejects a code section with pending relocations. It writes an [objdiff](https://github.com/encounter/objdiff) instruction comparison to `objdiff.json` and checks every function byte against the locked reference. The checked `candidate.bin` feeds the second command, which records the comparison in the manifest. For this unit the compiler, assembler and flags reproduce all eight bytes. The identity and flags of the original game compiler have yet to be established. See the [progress feed](../reports/progress.json) for the distinction between candidates and verified matches.

## Inspect the retail build

- **Ghidra.** Import the ELF into a local project and open CodeBrowser. For full PS2 instructions, install the [Emotion Engine extension](https://github.com/chaoticgd/ghidra-emotionengine-reloaded) matching your Ghidra version and select its PS2 language. Press **G** to go to the EE address and inspect references to the function. The [headless script](ghidra/ReviewEeLeaf.java) checks the nominated addresses and both direct calls. This particular leaf uses base MIPS instructions, so the following cross-check also works with a stock Ghidra installation.
- **PCSX2.** Start your disc with your own BIOS, open **Debug → Open Debugger**, select the Emotion Engine CPU and wait until the relevant ELF loads. For the online leaf, set an execute breakpoint at `0x0018B4F0`. Inspect register `a0`, the word at `a0 + 4` and the returned `v0` after the delay slot. The boot entry belongs to a different ELF. [PCSX2 debugger](https://pcsx2.net/docs/advanced/debugger/)
- **decomp.me.** Select **PlayStation 2 → New scratch** to compare compiler and flag hypotheses with your own small test cases. Keep retail assembly and game-derived drafts local. Record the compiler, flags and scratch link when publishing a synthetic reproduction. [PS2 workbench](https://decomp.me/platform/ps2)

With the working directory set to the repository root, create `decomp/ps2/local/ghidra` and run this command from your Ghidra installation's `support` tools. Windows uses `analyzeHeadless.bat`. For an existing project already containing the ELF, replace `-import ... -processor ...` with `-process SC3_ON.ELF`.

```sh
analyzeHeadless decomp/ps2/local/ghidra pal-online -import decomp/ps2/local/disc/SC3_ON.ELF -processor MIPS:LE:32:default -noanalysis -scriptPath decomp/ps2/ghidra -postScript ReviewEeLeaf.java 0x0018B4F0 8 0x0018AA74 0x0018BE38
```
