# R2 spike: UnrealScript bytecode decompiler

Status: **GO**. Every script in the five v300 script packages decodes to a token tree that re-encodes
byte for byte (T1 100%), and every structured function and state lowers back to the original bytes
including the jump layout (T2 100%, target was 98%). No function needed a goto.

Tool: `re/tools/ucdecomp` (run from `re/tools`, see its README). Outputs (gitignored):
`local/re-out/uc/<Pkg>/<Class>.uc`, `local/re-out/uc/uc-index.json`, `local/re-out/uc/check.json`.

## Results

Measured on 2026-10-23 against `Enhanced SCCT Versus v4.0 Public Beta 3\System\_PC_` with
`python -B -m ucdecomp check --game <install>`.

| Package | UStructs | Functions | Scripts | Script bytes (in memory) | T1 | T2 | gotos |
|---|---:|---:|---:|---:|---:|---:|---:|
| Core | 225 | 200 | 200 | 2,346 | 100% | 100% | 0 |
| Engine | 1,334 | 1,124 | 1,172 | 85,920 | 100% | 100% | 0 |
| SBase | 4,334 | 3,686 | 4,131 | 405,021 | 100% | 100% | 0 |
| GUI | 1,703 | 1,396 | 1,420 | 301,393 | 100% | 100% | 0 |
| IpDrv | 38 | 34 | 34 | 214 | 100% | 100% | 0 |
| Total | 7,634 | 6,440 | 6,957 | 794,894 | 100% | 100% | 0 |

"Scripts" counts every function, state and class with bytecode (class bytecode holds the replication
conditions). T1 compares the re-encoded file bytes and also requires the in-memory size computed from the
tree to equal ScriptSize. T2 is stricter than the plan asked: the whole lowered script must equal the
original bytes, not only the jump offsets.

Independent checks that the operand sizes are right (all from the same run):

- 22,918 EX_Context / EX_ClassContext skip sizes, 0 mismatches against the computed size of the skipped
  expression.
- 3,114 EX_Skip sizes (short-circuit operands), 0 mismatches. The stored value is the operand size plus
  one (it also skips the operator's EX_EndFunctionParms).
- 25,311 native and final-function calls checked against the callee's declared parameters: 0 with too
  many arguments, 0 with too few required ones.
- Native table: 1,066 native functions read from the packages' own iNative fields, 0 index conflicts,
  0 unresolved native tokens.
- Every class export, property, enum and const consumes its export exactly (0 load errors over all
  exports).

Structured statements found: 8,019 if, 440 for, 31 while, 0 do-until, 179 switch, 129 foreach,
951 break, 27 continue, 7,985 return. 435 states (Engine 30, SBase 381, GUI 24), 64 state labels, 543
ignored functions (probe mask bits plus stubs). Latent calls in state code: Sleep and
FinishInterpolation only.

Replication: 177 statements (168 reliable, 9 unreliable) covering 379 variables and 287 functions; 23
functions carry RepOffset 0xFFFF and are replicated by a parent class's statement.

defaultproperties: 665 classes, 11,892 values, 722 sub-objects (Begin Object blocks, mostly GUI
controls), 0 classes that fail to decode, 0 arrays left undecoded.

Output size: 665 class files, about 114,700 lines; the index is 5.3 MB. A full check takes about 2 s and
a full decompile about 4 s.

## Format facts confirmed by this work

- UStruct header in an export: SuperField, Next, ScriptText, Children, FriendlyName (paired name),
  CppText, Line, TextPos, ScriptSize, then the tokens. UFunction adds iNative u16, OperPrecedence u8,
  FunctionFlags u32 and RepOffset u16 when FUNC_Net is set. UState adds ProbeMask u64, IgnoreMask u64,
  LabelTableOffset u16, StateFlags u32. UClass adds ClassFlags, a 16-byte GUID, Dependencies (with many
  duplicate entries), PackageImports, ClassWithin, ClassConfigName and HideCategories, then the class
  defaults. Struct exports have no struct flags and no struct defaults in v300.
- UProperty: ArrayDim i32, PropertyFlags u32, Category (paired name), RepOffset u16 when CPF_Net, then
  the subclass reference.
- Bytecode in the file stores object references as compact indices and names as two compacts (local
  index, cooked global index), but ScriptSize and every jump, case, iterator and label offset count the
  in-memory form (4 bytes per object or name).
- Native functions start at token 0x56; extended natives use 0x46 to 0x55 (index = (token - 0x46) << 8
  plus the next byte). 0x40/0x41 are dynamic array Insert/Remove, 0x43 is a delegate call (property,
  name, parameters), 0x44 a delegate value, 0x45 a delegate assignment.
- foreach: `break` is a jump to the loop's IteratorPop, `continue` is the EX_IteratorNext token itself,
  and a return inside the loop is preceded by one IteratorPop per enclosing foreach.
- State code ends with Stop, padding Nothing tokens and EX_LabelTable; LabelTableOffset points just past
  the table token so the entries are 4-byte aligned.
- Class and state member chains list the newest declaration first; struct members and function
  parameters are in declaration order. Function line numbers survive and confirm the order.
- Probe names start at global name 300 (Destroyed is 301, Tick 336); IgnoreMask bits are cleared for
  ignored probes. Non-probe ignores and bodiless state declarations are both stored as stub functions
  without FUNC_Defined.
- Constants and enum values reached through an object (`Obj.NAME`) compile to a context around the
  literal. 1,274 occur in the bytecode; 992 are named from a matching const or a unique enum.
- In tagged defaults, tag type 7 is a delegate (object reference plus function name).
  `scctpkg.properties.read_properties` reads it as a string and fails on 53 GUI classes; this tool
  decodes delegates itself and passes everything else to the scctpkg helpers. Upstream should add the
  delegate case.
- The Bridge disassembler's recorded failures (4 in SBase, 2 in Engine) came from the other install it
  was pointed at. On this install it has none, but its header parse only lines up when the friendly name's
  global index happens to be a 3-byte compact; the new loader parses the whole header.

## Known gaps

- 282 object-qualified constants (on 226 lines) are ambiguous, because several consts or enums share
  the value, and are printed as `Obj.<number> /* constant */`, which does not compile as written.
- Casts the compiler inserted are printed explicitly (`int(...)`, `float(...)`, `byte(...)`). They compile
  to the same tokens but read noisier than the original; the byte-to-int casts around enum comparisons
  are dropped.
- Equivalent forms are chosen by rule, not recovered: for versus while (initialiser and increment on the
  loop variable), if/else versus a trailing break or continue, ignores versus bodiless declarations.
  The choice never changes the compiled bytes.
- Not recoverable from the packages: comments, original formatting, variable grouping and order inside
  a class (printed in chain order), constant expressions, `dependson`, struct modifiers. cpptext blocks
  are kept from CppText.
- Objects in defaults are printed with full paths; sub-objects are printed as Begin Object blocks before
  the class's own values.
- Not done yet: recompiling a sample through the RE+ headless editor `script.compile` (plan target 200
  functions, 95% token-equal) and checking defaults against a live class-default oracle.

## Next steps

1. Recompile 200 sampled functions in an isolated editor sandbox and compare tokens.
2. Compare decoded defaults with live class-default objects read from a sandbox editor.
3. Feed `uc-index.json` (replication groups, signatures, flags, sha1 per function) to the OpenScct.Generators
   work (defaults, replication skeletons, state dispatch) and to the R3 coverage inventory.
4. Add the delegate tag case to scctpkg upstream and drop the local reader.
