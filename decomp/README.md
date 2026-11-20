# Chaos Theory matching decompilation

The original editions cover campaign, cooperative play and Versus. Choose a
retail build to inspect, rebuild a named function from independently written
source, then compare its complete machine code with that release. The Godot and Unreal modernisations track gameplay and rendering separately.

| Start here | What you will find |
| --- | --- |
| [PlayStation 2](ps2/README.md) | Identify the disc revision, boot program and secondary executables |
| [PC and Steam](pc/README.md) | Verify the campaign/co-op and Versus binaries and reproduce the candidate C function |
| [Progress feed](reports/progress.json) | Verified matches, byte-identical candidates and code denominator per executable |
| [20 November 2026 report](reports/2026-11-20.md) | Baseline evidence, first result and remaining work |

## Source layout

Each edition owns its manifests in `config/`. Each manifest identifies one
executable by SHA-256, records its executable sections and lists the units that
contributors are working on. Independently written C belongs in the edition's
`src/` directory. Compiled output, game files, disc images and analyst databases
belong in an ignored `local/` directory or outside the checkout.

The shared [image reader](tools/images.py) reads x86 PE32, MIPS ELF32 and x86
COFF objects. The [progress command](tools/progress.py) uses Python 3.10+.
The existing [Ghidra and Unreal package tools](../re/README.md) support
function identification and the [behaviour specs](../spec/README.md) give
contributors evidence to work from. Check each Ghidra project's build hash
before reusing names or addresses.

## How matching works

1. **Disassemble.** Start with the PS2 disc or Steam installation you own. Identify the exact executable and verify its hash against the build manifest. Split it locally into code and data. Document each function boundary and keep extracted bytes, disassembly and analysis databases under `local/`. [Source and evidence rules](../re/PROVENANCE.md)
2. **Rewrite.** Write plain C for one complete function. Record the compiler version and flags for that translation unit, with evidence from the reference build. Record all source and build inputs. Compile locally using that recipe. A C implementation that produces identical bytes with a different compiler is a candidate.
3. **Verify.** Compare the whole compiled function against the locked original. Resolve relocations using the correct link layout first. Commit the C, build recipe and hashes. The website counts a function only when all bytes match and review establishes the original compiler and flags. Another owner of the same build can repeat the comparison.

### Tools and project guides

| Tool | Where it fits |
| --- | --- |
| [splat](https://github.com/ethteck/splat) and [spimdisasm](https://github.com/Decompollaborate/spimdisasm) | Split the PS2 executable and disassemble reviewed MIPS ranges. See the [PS2 guide](ps2/README.md). |
| [m2c](https://github.com/matt-kempster/m2c) | Draft a starting point from MIPS assembly locally. Review the types, control flow and function boundary before writing the C implementation. |
| [Ghidra](https://github.com/NationalSecurityAgency/ghidra) | Inspect MIPS and x86 code, exports and references. Hash the input before reusing annotations. See the [PC guide](pc/README.md). |
| [PCSX2](https://pcsx2.net/) | Run the owner's PS2 disc with their BIOS. Use the debugger to compare observed behaviour and static analysis. |
| [decomp.me](https://decomp.me/) | Collaborate on a compiler experiment for a single function. Use a supported platform and compiler. Share independently authored examples or material you have permission to upload. |
| [objdiff](https://github.com/encounter/objdiff) | Compare the compiler's output with the local reference object instruction by instruction. The final equality check compares complete bytes. |
| [OpenSCCT on ByteMatch](https://bytematch.dev/OpenSCCT/OpenSCCT) | The progress workflow publishes an objdiff-format `<target>_report` GitHub artifact for each measured PC program. It uses the same verified bytes as the player website. ByteMatch reads successful pushes to the default branch. PS2 programs with an unknown denominator stay unmeasured. |

From the repository root, run:

```text
python decomp/tools/progress.py identify PATH_TO_EXECUTABLE
python decomp/tools/progress.py verify EDITION TARGET PATH_TO_EXECUTABLE
python decomp/tools/progress.py match EDITION TARGET PATH_TO_EXECUTABLE UNIT BUILT_ARTIFACT
python decomp/tools/progress.py report
python decomp/tools/progress.py report --check
python -m unittest discover -s decomp/tools
```

## Reading a report

The numerator counts nonoverlapping, whole units with a recorded byte-for-byte
comparison **and** reviewed original build evidence. The optional unit field `recipe_evidence` names the matching `compiler`, a list of `flags` and a project-relative Markdown `basis` documenting how both were established for that function. The report records byte-identical candidates separately while this evidence is missing. Changes to the evidence require a new comparison. The receipt binds the original build hash, unit boundaries, compiler
recipe and tracked inputs to the result. `verify` checks the receipt's byte
digest against the owner's original. Public CI uses synthetic fixtures and
checks that receipts still correspond to the declared inputs and that the feed
is current. Reviewing the recipe and rebuilding the source establishes how the
candidate artifact was produced.

For the locked PC images the denominator is the file-backed portion of sections
marked as code and executable, excluding writable sections and file-alignment
padding. This is a reproducible section measure and can include in-section
constants or alignment. A mixed code/data ELF stays unmeasured until a
reviewed, exhaustive `code_map` lists its nonoverlapping file ranges and the
basis for them. The feed uses JSON `null` for an unknown denominator or
percentage. Asset conversion, emulator boot, symbol discovery, gameplay tests
and the modern client's porting coverage have their own evidence.

The conventions follow OpenRAC's build-specific measurements, source ownership
and local reproduction workflow. [OpenRAC project hub](https://openrac.dev/)
and [matching workflow](https://github.com/OpenRAC/rac1-decomp/blob/main/CONTRIBUTING.md).
