# Chaos Theory matching decompilation

The original editions cover campaign, cooperative play and Versus. Choose a
retail build to inspect, rebuild a named function from independently written
source, then compare its complete machine code with that release. The modern
Unreal edition tracks gameplay and rendering through its own project.

| Start here | What you will find |
| --- | --- |
| [PlayStation 2](ps2/README.md) | Identify the disc revision, boot program and secondary executables |
| [PC and Steam](pc/README.md) | Verify the campaign/co-op and Versus binaries and reproduce the first compiled match |
| [Progress feed](reports/progress.json) | Verified bytes, code denominator and match count per executable |
| [20 November 2026 report](reports/2026-11-20.md) | Baseline evidence, first result and remaining work |

## Source layout

Each edition owns its manifests in `config/`. Each manifest identifies one
executable by SHA-256, records its executable sections and lists the units that
contributors are working on. Independently written C/C++ belongs in the edition's
`src/` directory. Compiled output, game files, disc images and analyst databases
belong in an ignored `local/` directory or outside the checkout.

The shared [image reader](tools/images.py) reads x86 PE32, MIPS ELF32 and x86
COFF objects. The [progress command](tools/progress.py) uses Python 3.10+.
The existing [Ghidra and Unreal package tools](../re/README.md) support
function identification and the [behaviour specs](../spec/README.md) give
contributors evidence to work from. Check each Ghidra project's build hash
before reusing names or addresses.

## Contribute a match

1. Run `identify` on the player-supplied executable, then `verify` against its
   manifest. Record each distinct executable or revision separately. Keep
   inputs read-only. Follow the [source and evidence rules](../re/PROVENANCE.md).
2. Choose a function with a defensible boundary and a named export, symbol,
   call site or existing behavioural specification. Record the retail file
   offset, size and basis for the identification.
3. Implement the function from its behaviour. Record the toolchain and exact
   build command in the unit entry, together with all sources, headers,
   linker scripts and build scripts that affect the output.
4. Build the unit. For linked, relocated code, use its final linked artifact
   and record `rebuilt_offset` as a file offset. For an x86 COFF object,
   `rebuilt_symbol` selects a complete single-function section with no pending
   relocations. The [PC target](pc/config/steam-252084-campaign.json) is a
   worked example. Each byte of the complete unit must compare equal.
5. Run `match` to write a receipt. Regenerate the feed and include the source,
   manifest and feed together in the review. A reviewer with the matching
   game build can repeat the compiler invocation and byte comparison.

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
comparison. The receipt binds the original build hash, unit boundaries, compiler
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
