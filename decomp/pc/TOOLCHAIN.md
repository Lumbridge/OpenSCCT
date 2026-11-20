# Steam compiler and flag evidence

| Steam build 252084 program | Retail evidence | What remains to establish |
| --- | --- | --- |
| Campaign and co-op | The PE32 optional header records linker version 7.10. A scan of the DOS stub up to the PE signature found no Rich marker. REA found 150 exports. | The compiler build and options for the game translation units. The linker version identifies only one part of the build. |
| Versus | The PE32 optional header records linker version 7.00. Its Rich records include linker product `0x003d`, build 9466, count 1, along with C, C++, assembler and library producer records with multiple build numbers. REA found zero exports. | Which compiler produced each source unit, the compiler flags and the linker command line. |

Ghidra's product table associates `0x003d` with the Visual Studio .NET 2002 linker, `0x001c` and `0x001d` with its C and C++ tools, and `0x0060` with Visual Studio .NET 2003 C++. The Versus file includes all four IDs. Mixed producer records can come from several libraries and translation units. The output of `workflow.py … inspect` lists the raw product IDs, builds and counts for this exact binary. [Microsoft PE format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) · [Ghidra 12.1.4 product table](https://github.com/NationalSecurityAgency/ghidra/blob/Ghidra_12.1.4_build/Ghidra/Features/Base/data/ms_pe_rich_products.xml)

## Campaign sound catalogue

The campaign export table resolves `_SND_fn_lGetSoundTextureCount@0`, ordinal 57, to preferred VA `0x11048e10` (RVA `0x748e10`). REA export evidence is `ev_2265eec6bc83a81dc32381bb16c057ab071e61ff853f63232491831a06a3e06d`. Ghidra 12.1.4 identified a three-byte function returning zero. The independent C implementation uses the export's x86 calling convention.

| Experimental x86 compiler | Flags | Local comparison |
| --- | --- | --- |
| MSVC 19.44.35229.0 | `/O2 /c` | Three identical bytes, objdiff symbol 100% |
| MSVC 19.44.35229.0 | `/O1 /c` | Three identical bytes, objdiff symbol 100% |
| MSVC 19.44.35229.0 | `/Od /c` | Different function size |

An exact result for this constant return cannot distinguish the two optimisations. Reproducing an historical recipe requires independent evidence for the compiler version and flags. A shipped build record, debug information or a representative set of functions with differentiating code generation could advance the investigation.

## Versus vector arithmetic

The [Enhanced build annotations](../../re/ghidra/annotations/game.json) name `UObject::execDot_VectorVector` at preferred VA `0x109467f0`. The SHA-256 of that Enhanced reference is recorded in the [binary lockfile](../../re/binaries.lock.json). Ghidra 12.1.4 on the independently verified Steam payload found a 108-byte function at the same address. The complete routine has the same SHA-256 in the two binaries, `2789f485c74697821d84e0920b20249405600439c9591fe366e460f2eda95c13`. The Steam instructions dispatch two VM operands and compute the three-component dot product with x87 arithmetic. REA export evidence for this payload is `ev_017b93cbbfaa4dd81368ef0f59f67c444f3ac11a1601baa6242fa3ea4048528b`.

The function name transfers from a matching reference body. Its original translation unit, build flags and independently compiled implementation are still open. The study adds a reproducible anchor for Versus contributors.
