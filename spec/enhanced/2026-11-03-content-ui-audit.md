# Enhanced 4.0 Beta 5: content, UI and presentation audit

Read-only investigation on 2026-11-03. Reference inputs are the owner's Desktop installs of
Enhanced 4.0 Public Beta 3 and 4.0 Beta 5. Decompiled code and converted content remain under
ignored local directories; this document records independently written conclusions and identifiers.

## Release changes and their value to OpenSCCT

| Change | Evidence | Current rewrite | Assessment |
|---|---|---|---|
| In-game Resign button with a confirmation | B3/B5 `GUI.SSMenu_LANMainPage` and new `GUI.ResignValidation` decoded diff | `game/Scripts/UI/InGameMenuModel.cs:18` has no corresponding button/page/action. The existing console/chat path reaches `OnlineServer.Lobby.cs:867` and `ServerResign`. | Useful missing UI feature. Offer it only during an active, unfinished network round to a non-spectator with lives remaining, excluding tutorials. Cancellation must leave the round unchanged. |
| Reject Ready while the client lacks the map | Decoded `GUI.Lobby_Create.ButtonLaunch` change | `LobbyRoomScreen.cs:633` enables the guest button without considering map state; `OnlineLobby.cs:145` forwards it and `OnlineServer.Lobby.cs:151` accepts it. | Useful UX fix. A guest can display Ready beside a missing/different-map warning. `OnlineServer.cs:1010` separately rejects launch for Missing/Differs, so this is not evidence that those clients can start an invalid map. Keep Not Ready available and re-evaluate after download/hash verification. |
| Demo browser entry and return-to-browser routing | New `GUI.Demo_Browser`, changed `Menu_Multi`, `loading` and `SBase.LancementMenu` | `UI/DemosScreen.cs:48`, `Play/Demo/DemoHud.cs:496` and `:514` already provide the library and confirmed return. | Already covered at feature level. File formats and native playback behavior are separate comparisons; this does not establish compatibility with Enhanced recordings. |
| New local fog-volume authoring class | New `SBase.SFogVolume`: zone restriction, edge fade, height falloff, distance and colour declarations | No corresponding generated class or volume rendering path. Existing `Render/Data/RenderDocs.cs:570` and `Render/FaithfulView.cs:452` implement zone distance fog. | Future content capability. The class declaration alone does not establish the native rendering equation. No supplied B5 map changed to use it. Decode the native volume renderer before implementing it; do not substitute arbitrary Godot fog. |
| GUI subobject names/order | `Controller_Settings`, `GUIController`, `GUIYesNoPage`, `moListEditBox` decoded diffs | OpenSCCT uses its own menu widgets. | No behavior change established by these declaration/order differences. Do not mechanically port them. |

All nine files under `Menus` are SHA-256 identical between the supplied B3 and B5 installs
(three MGM files and six fonts). All seven top-level System INI files are line-identical.
The changed `GUI.int` adds demo/resignation UI entries; the inspected `SBase.int`, `Magma.int`,
`MagmaDynamic.int` and `Engine.int` are line-identical. The complete tree hash comparison from the
parent investigation establishes that `Packages` maps, settings, textures, meshes and audio are
unchanged. This is a B3-to-B5 comparison, not a B4-to-B5 attribution.

## Wider gaps verified in current code/content

### Animated map materials stop at their first sequence item

`tools/scctpkg/scctpkg/materials.py:506` resolves a MaterialSequence to its first usable texture
and stops. The renderer has no general MaterialSequence playback path. This leaves authored
monitor switching, noise transitions and alert animation static, even though the converted
material graph preserves timing and item references.

The current 85-map content inventory contains 32 MaterialSequence graph nodes on 11 maps:
ClarD, KrauE, KrauU, MoonD, OffsD, PrisD, ScheE, ScheU, ShadD, WareD and WareP. These are graph-node
counts, not a count of independently visible screens. Merc blinking has a separate explicit
implementation (`export_skel_gltf.py:51`) and must not be reported missing because general
sequence support is absent.

Recommended validation: choose a placed monitor that references a sequence, compare its full
cycle and transition action in the original, preserve each item's material/UV behavior, then
test time advancement and restart/seek. A still-image comparison cannot verify this behavior.
This is an existing presentation gap, not a B5 regression.

### Installed content still lacks nine moon sprites

A separate read-only audit of `%LOCALAPPDATA%/OpenSCCT/packs` found 41 missing sprite-metadata
records across 13 of its 81 maps. Following the actual fallback in
`game/Scripts/Render/MapLightSprites.cs:56` reduces the effective missing set: 32 Stee crane
sprites find the shared torch-flare texture, but nine moon sprites have neither a map sprite
URI nor a matching shared texture lookup. The renderer therefore skips those nine records.

Affected installed maps are DeftD, DeftS, PanaD, PanaS, ShadD, ShadP, StatD, StatE and StatS.
The source textures are the DTK and GAR moon assets. Repository conversions include their
sprite metadata and passed the same audit. This finding concerns the installed pack set
observed here; the current published content index was not checked, so it does not establish
that the latest downloadable packs are defective. Check pack version/update selection before
changing the renderer or regenerating content. No packs were modified by this sweep.

### Remaining audio calibration is explicit, not a new B5 change

The one-shot allocation path in `game/Scripts/Audio/SoundSystem.cs:464` calls
`SoundRules.Priority` without the original's occluded-zone priority-radius adjustment.
`SoundRules.cs:94` takes distance, radius, volume and flags only. This is a known difference
under voice contention; audible occlusion itself has its own implemented calculation.
Validate with more than 32 competing sources across linked zones before assigning gameplay
severity. Reverb wet-level matching and listening comparisons remain unverified in this pass.

Some older gap notes are stale: `SPlayerController.NotifyNoise.cs:19` implements ClientNotifyNoise,
and `Options/GamepadInput.cs:254` implements cinematic-camera triggers/shoulders. They should
not be treated as missing features. The unported SFlashGen timer/thunder behavior was also
checked, but no SFlashGen instance occurs in the 85 converted gameplay documents; this is not
a demonstrated current-map problem.

## Fresh validation

| Check | Result | Boundary |
|---|---|---|
| B5 source versus repository converted maps | 85 maps, 28,718 actors, 8,701 event edges; zero structural issues, missing/extra actors, property differences or decode errors | 83 B5 source packages available; MoonD and TestRange have no matching source package |
| Repository map loader | Passed, 1 test covering all 85 maps, no skips | Canonical data, hashes, references, class/property application and runtime spawn; not every interactive event chain |
| Installed map loader | Passed, 1 test covering all 81 maps, no skips | Same loader checks; render completeness is separate |
| Audit-tool regression tests | 12 passed | Synthetic audit behavior |
| Installed structural/render metadata audit | 81 maps, 28,392 actors, 8,620 edges; 41 missing sprite-metadata records | 32 have runtime fallbacks, nine remain missing as described above |

The B5 source comparison retains 231 unmatched event names and five references to render-only
targets. These also occur in the source and remain review candidates, not confirmed missing
gameplay. FX metadata was checked in 84 repository maps; TestRange has none. The original
game was not launched for this sub-audit. It does not certify interactive elevator riding,
all event chains/Easter eggs, visual/audio equivalence, or multiplayer presentation.

Reproduction commands:

```powershell
$env:PYTHONPATH = 'content'
python -m svmbuild.mapconv.audit --source --game 'C:\Users\ryans\Desktop\Enhanced SCCT Versus 4.0 Beta 5' --out local/enhanced-b5-audit/content-map-audit
python -m pytest tests/content/test_map_parity_audit.py -q
dotnet test --project tests/OpenScct.Content.Tests --filter-class OpenScct.Content.Tests.AllMapsTests --no-restore
python -m svmbuild.mapconv.audit --content "$env:LOCALAPPDATA\OpenSCCT\packs" --out local/enhanced-b5-audit/installed-content-audit
$env:SVM_CONTENT = Join-Path $env:LOCALAPPDATA 'OpenSCCT\packs'
dotnet test --project tests/OpenScct.Content.Tests --filter-class OpenScct.Content.Tests.AllMapsTests --no-build
```

The final command reuses the binaries built by the preceding repository-loader run. Derived
evidence is under `local/enhanced-b5-audit`: `tree-diff.json`, `uc-diffs`,
`content-map-audit/parity.json`, `installed-content-audit/parity.json`,
`installed-sprite-fallbacks.json`, and `content-known-gaps.json`.
