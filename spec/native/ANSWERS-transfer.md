# Answers from the editor to game name transfer study

For everyone who reads the game build in Ghidra. The editor (`ChaosTheory_Editor.exe`, base 0x10E00000)
keeps guard strings that name about 3,900 functions; the game (`SCCT Versus`, base 0x10900000) has none.
This study carries those names to the game. Names live in `re/ghidra/annotations/transfer.json`
(1,084 functions in 365 namespaces), applied to the transfer work copy. Tools are in
`re/ghidra/scripts/transfer/`.

## Results in short

| Item | Value |
|---|---|
| Game functions (work copy, after creating 1,851 vtable targets analysis had missed) | 15,842 |
| Editor functions (after creating 2,022 vtable targets) | 37,412 |
| Fixed anchors | 1,124 native pairs by native index, 101 class constructor pairs from the reflection dumps |
| Matched pairs (besides anchors) | 3,251 |
| New game names | 1,084 (1,046 as the editor names them, 6 with the class taken from the vtables, 32 with the class spelling corrected) |
| Pairs matched to unnamed editor functions | about 2,100 (structure only, no name to carry) |
| Precision, hand check 1 (100 random names of an earlier run, 1,047 names) | 100 of 100 pairs right; 99 names right |
| Precision, hand check 2 (100 random names of the 1,074-name run before the last rules, seed 424242) | 100 of 100 pairs right; 99 names right |
| Tier 2 names and the 10 names the last rules added (36 names, all checked) | 36 of 36 pairs right (4 with weaker evidence) |
| Held-out natives (20% of the native anchors hidden, then scored) | 92 right, 3 wrong, 99 not matched: 96.8% |
| Names of the physics and net studies (independent) | 70 of 72 agree; both differences are editor guard names, the pairing itself is right |

With 200 of 200 checked pairs right, the 95% lower confidence bound on pair precision is about 98%.
Name precision is slightly lower because some editor guard strings are wrong (below).

## How the matcher works

`match_names.py` runs outside Ghidra on the exports of `T10_export_features.py` (per-function call
targets, strings, constants, data references, address-free instruction tokens, vtables) and
`T20_bsim_sigs.py` (BSim vectors and nearest lists).

1. Features that survive link-time optimisation: strings (the game is UTF-16, the editor 8-bit; text
   only), imports with ANSI and wide names folded together and WS2_32 folded with WSOCK32, float
   constants, other immediates, read-only constants, and `[reg+disp]` field offsets. Class layouts are
   the same in both builds for nearly all classes, so offsets are the strongest single feature.
   Weighted TF-IDF, cosine. The editor side is also scored with its non-hub callees folded in, which
   models the game inlining them.
2. BSim cosine from the exported vectors, the BSim nearest lists, and token trigram Jaccard (separates
   sibling bodies that differ only in one offset, such as the X, Y and Z variants of a GE query).
3. Call graph: a matched callee of the game function must be reachable from the editor candidate within
   three calls (inlining removes levels); matched callers likewise. An unreachable matched neighbour is a
   conflict. Hub functions (more than 120 callers) are not followed.
4. Data: global addresses paired by votes from matched pairs (121 pairs at the end).
5. Vtables: 421 table pairs (406 from the reflection dumps, the rest from the data map). The editor adds
   editor-only virtuals, so tables are aligned with a banded dynamic programme rather than by index.
   An aligned slot is strong evidence when all tables agree.
6. Acceptance: the pair must be the best candidate of both functions by a margin, with no conflict, and
   meet a rule that combines two kinds of evidence (content and BSim, content and graph, vtable slot and
   graph, a unique shared string). Strict rules first, then all tier 1 rules to a fixed point, then tier 2
   rules (one strong kind of evidence with a large margin) to a fixed point.

## Questions answered

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | Why did BSim alone score so low? | The game is link-time optimised: functions pass arguments in registers between themselves, inline many callees (exec natives inline `FFrame::Step`, Serialize functions inline their helpers), and identical bodies are folded. Only 147 of 1,124 native pairs share identical tokens and the true native pair is BSim's first choice for 342 of 1,124. Field offsets alone put the true pair first for 418. | `match_names.py --holdout 0.2`; local metrics |
| 2 | Where do the vtables differ between the builds? | Every UObject table in the editor has two extra virtuals: `UObject::Modify` (editor slot 8) and `UObject::Rename` (editor slot 22). Actor tables add three more: `AActor::RenderEditorInfo` and `RenderEditorSelected` (60, 61) and `AActor::CheckForErrors` (79). So a game slot index is the editor index minus 1 after slot 8, minus 2 after 22, minus 4 after 61, minus 5 after 79. | alignment of the 406 reflection table pairs |
| 3 | Why were 3,242 game vtable targets partly missing as functions? | Code reachable only through a vtable was never disassembled by auto-analysis (1,851 targets in the game, 2,022 in the editor). `T15_vtable_functions.py` creates them; this alone raised the matched count by half. | work copy report `game.transfer-vtable-functions.json` |
| 4 | Are the editor names always right? | No. Some guard strings were copied from another method: `AProjector::GetOptimizedRepList` is really AZoneInfo's; one of the two `AActor::PreNetReceive` is APawn's; `UActorChannel::UActorChannel` and `UControlChannel::UControlChannel` name the `Init` virtual (slot 24 of the channel tables). The writer fixes the class when both builds put the function in one class's vtables only and the named class has a vtable of its own that does not hold it (6 fixes), and corrects class names that are not real classes but match one ignoring case or with the U or A prefix added (32 fixes: `GUIComponent` becomes `UGUIComponent`, `AEMitter` becomes `AEmitter`, `SBasePawn` becomes `ASBasePawn`). Method names keep the editor spelling. | `write_transfer.py`, hand checks |
| 5 | What about folded functions? | When the game folded two editor functions into one, the game function gets one of the names (for example `eventBump_SBasePawn` is also APawn's slot 46). Template instantiations (`TLazyArray::Load`) carry the generic name. | hand check 2 |
| 6 | What is not named and why? | Editor-only code (`W*` windows, `UEditorEngine`, `UUnrealEdEngine`, script compiler, factories), functions the game inlined everywhere, near-identical siblings with no separating evidence (`FRawIndexBuffer::Stripify` and `CacheOptimize`, `FGESegment::Equals`), and tiny accessors. About 290 named editor functions are mutual best candidates that no rule accepts. | `mutual-fails.json` (local) |
| 7 | Are the natives' names affected? | No. Native exec thunks were already named by native index; transfer.json skips every address named in `game.json`, `physics.json` or `net.json`. | `write_transfer.py` |

## Reproducing

```powershell
# exports (read-only on the transfer work copy; T15 writes, then re-export)
re\ghidra\scripts\transfer\run.ps1 -Program 'SCCT Versus' -Scripts T15_vtable_functions.py,T10_export_features.py,T20_bsim_sigs.py -ScriptArgs 'query=ChaosTheory_Editor.exe'
re\ghidra\scripts\transfer\run.ps1 -Program 'ChaosTheory_Editor.exe' -Scripts T15_vtable_functions.py,T10_export_features.py,T20_bsim_sigs.py
python re\ghidra\scripts\transfer\match_names.py --holdout 0.2   # precision on hidden natives
python re\ghidra\scripts\transfer\match_names.py                 # the real run
python re\ghidra\scripts\transfer\write_transfer.py              # writes annotations/transfer.json
re\ghidra\scripts\transfer\run.ps1 -Program 'SCCT Versus' -Scripts T70_apply_transfer.py
python -m pytest re\ghidra\scripts\transfer\test_match_names.py
```

A run takes about 30 s after the exports. Hand-check records are in
`local/ghidra-work/transfer/match/review100.json` and the review lists next to it.

## Open questions

1. About 2,100 matched editor functions have no name. Naming them from structure (event stubs by
   their FName index, scalar deleting destructors by vtable slot 0, `InternalConstructor` from the
   reflection constructor addresses) would add several hundred more names without new matching.
2. The physics and net studies' game names are better than the editor's for two functions
   (`ASTriggerLight::IsNetRelevantFor`, `ASPawnAttaque::IsNetRelevantFor`); the editor table should
   carry those corrections too.
3. Natives stay the hardest population (half of the hidden natives are not matched) because they have
   no callers in the call graph; this does not matter while natives are named by index.
