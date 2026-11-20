# PlayStation 2

The initial disc baseline is Chaos Theory's European PAL release with
serial `SLES-53287` and `SYSTEM.CNF` version `1.01`. Its three MIPS
executables have independent build identities.

| Program on disc | Build manifest |
| --- | --- |
| `SLES_532.87`, the disc's `BOOT2` target | [Boot](config/sles-53287-boot.json) |
| `SC3_OFF.ELF`, offline program | [Offline](config/sles-53287-offline.json) |
| `SC3_ON.ELF`, online program | [Online](config/sles-53287-online.json) |

Open your ISO with 7-Zip and extract these three programs and `SYSTEM.CNF`
into `decomp/ps2/local/`. Check the serial and version in `SYSTEM.CNF`.
From the repository root:

```text
python decomp/tools/progress.py verify ps2 sles-53287-boot decomp/ps2/local/SLES_532.87
python decomp/tools/progress.py verify ps2 sles-53287-offline decomp/ps2/local/SC3_OFF.ELF
python decomp/tools/progress.py verify ps2 sles-53287-online decomp/ps2/local/SC3_ON.ELF
```

For another disc, extract its own boot and secondary programs and run
`identify`. Add a manifest for each new build after confirming its serial
and revision. Each verified European image combines writable data and MIPS
instructions in one loaded section. Before reporting a percentage, establish
a complete code-range map in the owning manifest and document its evidence.

The online image matches the hash already used for OpenSCCT's
[PS2 world research](../../spec/native/ps2-world.md). Use those named
locations as starting points. Keep local Ghidra databases, extracted files
and disassembly outside version control. Source contributions and their build
commands follow the [matching workflow](../README.md).
