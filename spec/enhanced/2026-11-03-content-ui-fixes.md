# Content, presentation and lobby fixes after the Beta 5 audit

This records implementation and validation of the content/UI slice of the
[parity sweep](2026-11-03-beta5-parity-sweep.md). All converted assets and native
evidence remain in ignored local folders. No reference assets were published.

## Resignation and map readiness

An eligible network participant now gets a confirmation before resigning.
Cancel is focused initially. The menu rechecks current round, spectator and
lives state both while open and on confirmation, so stale UI cannot resign a
player after eligibility changes. The existing server resignation command
continues to enforce gameplay authority.

The guest Ready action is unavailable until the local map matches. A player
can still withdraw readiness. The server rejects a ready request for an
unknown or mismatched map, clears readiness if matching is lost, and refuses
launch while map verification is incomplete. This covers both UI and forged
or delayed client messages.

Validation: 17 menu-model tests and 10 lobby integration tests passed.
The `resign-menu` Vulkan smoke passed at 800×600 and 1280×720, checking button
bounds, default focus, cancellation, stale eligibility and one-shot action.

## Animated materials

MaterialSequence conversion now carries every timed input, its texture and
modifier chain. Runtime clocks preserve authored Show/FadeTo timing, looping,
non-looping completion, backward scene-time reset and trigger actions.
Native interpolation rounds to an 8-bit mask. Pausing intentionally leaves the
native last-update time unchanged, so resuming includes elapsed paused time.

Evidence: editor `UMaterialSequence::PreSetMaterial` at `0x110c9a40`; the
rounding helper at `0x10fa3430` adds float 0.5, stored at `0x11469d60`.
The local corpus has 32 sequence graph occurrences across 11 maps, representing
17 distinct sequences. The source validation retained all timed items and
64 animated inputs. The proxy-mine sequence additionally changes complete
material state, which is covered by the separate material-state implementation.

Validation: five clock tests, synthetic conversion tests, and a real GPU
sequence smoke check Show frames, last-to-first fading and the quantized
midpoint. Ten shipped maps converted successfully to a separate ignored
output, including the prison's animated security feeds.

## Installed moon texture updates

The nine affected installed packs were older than the current catalog, but
`ContentStore.Has` treated an unchanged gameplay hash as evidence that the
whole render pack was current. Full downloads now require the archive hash
to match. Simulation-only requests can still reuse equivalent gameplay data.
The regression test covers a visual-only pack update with unchanged gameplay.

Normal installation through `ContentStore` verified and updated the local
DeftD, DeftS, PanaD and PanaS packs to 1.0.2, and ShadD, ShadP, StatD, StatE
and StatS to 1.0.3. Every repaired pack has its moon sprite metadata and PNG.
The installer used existing local cached archives when available and retained
the production size, SHA-256 and manifest checks. Nothing was deployed.

The Vulkan `map-light-sprites` smoke passed for SteeE's eight sprites,
ClarD's 545 sprites and StatE's moon, producing 8,920 differing rendered
pixels when the tested sprites were hidden. This checks real texture loading,
blend modes, placement and sky layers.

## Occluded sound allocation

Initial sound allocation uses the occlusion query's effective radius. Once
allocated, voice priority uses the native zone-distance factor
`0.8^(portalDistance²)`. This prevents a muffled sound behind distant rooms
from displacing a more audible nearby source. Music, interface/local sounds,
authored unlimited radius and disconnected-zone underflow retain their
separate handling.

Evidence: editor audio PlaySound at `0x10f6c340`; retail PlaySound at
`0x10b03d50`, with the stored priority radius at `0x10b042dd`–`0x10b042f9`.
Three allocation-rule regressions and the headless `audio` smoke passed;
the smoke loaded 1,541 sounds, 32 zones and 27 reverb buses and played the
footstep, rifle and hack paths for 90 frames.

## Fog

The B5 native library embeds its fog shader source, which made the new portal
and volume behavior recoverable. See
[the fog specification](../native/enhanced-zone-fog.md) for the decoded rules,
implementation and remaining evidence boundaries. The converter now exports
all nonzero BSP zone boundaries and convex SFogVolume pieces. The serialized
model RootOutside field was verified in all 3,346 models in the supplied maps.
No supplied map has an authored SFogVolume actor; volume validation therefore
uses synthetic fixtures, including an explicitly synthetic fog actor around
one brush from the owner's AquaE install.

Converter tests cover transformed convex leaves, solid/air zones, floor and
open-face sampling, adjacent pieces, height culling and the native sampled-box
fallback. Portal GPU tests cover clear-to-fog transitions, density, polygon
bounds and duplicate boundaries. The converted AquaE render smoke passed
22 cameras plus vision, torch, laser and particle checks (520 maximum draw
calls). Further surface, volume and multi-view validation is recorded in the
overall fix report.

The B5 class database resolves `SBase.SFogVolume` through `Engine.Volume` and
`Engine.Brush`. A minimal instance using that actual database is classified as
render-only and emits a volume around the local test brush. The class need not
exist in simulation code generation. Explicit deleted and PS2-only actors still
remain excluded. The serialized defaults are distance 1500, RGB 128 and edge
fade 64; unassigned height falloff and zone limiting use zero/false.

World surfaces and gameplay effects now evaluate fog before framebuffer
blending. This includes particles, rain, projected effects at the receiver's
depth, laser beams, cloak surfaces and the additive shock-immunity overlay.
The native modulated fog neutral is byte gray 127, producing 254/255 after the
doubled framebuffer gain. Particle premultiplied draws use black fog and keep
their existing alpha. Editor evidence: `SetParticleMaterial` at `0x10f34260`,
`SetShaderMaterial` at `0x10f36a30`, its FinalBlend helper at `0x10f33c90`, and
`SetProjectorMaterial` at `0x10f39330`. Exact original corona and light-beam fog
enable flags remain unproven; their existing additive rewrite draws receive
attenuation at their own depth.

Gameplay effects previously using `StandardMaterial3D` now use the explicit
unlit `EffectMaterial` subset. It retains colour, texture, animated UVs,
billboards, fixed size, depth testing, stencil and follow-up passes without
altering shared engine materials. It supports the four used blend operations:
mix, add, subtract and multiply. Diagnostic collision/gizmo/walker materials
and authored hub materials retain their existing independent renderer.

Final validation:

- Final full content Python suite: 306 passed, including the new volume-class
  classification regression.
- `effect-material`: 13 paired comparisons with StandardMaterial and three
  shared-material, two-camera fog cases, including independent stencil reads
  and shader changes after attaching a follow-up pass.
- `surface-fog`: 36 GPU samples across two simultaneous views, alpha/add/
  modulate/darken, separate reflection, cloak, portal/volume fog and MSAA off/4×.
- `fog-volume`: 12 compute/surface scenarios and 96 off-centre comparisons.
- The same three GPU suites passed from a Windows debug export, verifying
  packaged shader-include remapping as well as editor resource loading.
- Final resignation, mine-light, laser-alignment, light-sprite and audio smokes
  passed. The laser suite checked nine weapons, 180 shots and 248 reload frames;
  the final sprite run changed 8,627 pixels. All final smoke stderr logs were
  empty, including the resignation fixture after deferred teardown.

All 83 supplied B5 maps were regenerated in the ignored local content tree,
followed by all 83 audio-map outputs and schema/reference/manifest validation.
The additional installed MoonD map was regenerated because it was the eleventh
map in the sequence corpus and was outside the supplied 83-map set. Its existing
converted source is
`C:/Users/ryans/source/repos/OpenSCCT-Content/staging/inputs/main/game/Packages/Maps/MoonD.sdc`;
this was supplied through `--addons`, with B5 remaining `--game` for script
metadata. This is an original converted map, not a rewrite-authored map.
The source SHA-256 matches the existing `original-MoonD-1.0.3.zip` pack
(`d1dcdee94f4977f1c358023ad1a14d41d19c2335750563aceb71b736aa25e786`).
Gameplay, collision, traversal, UI and audio files remain byte-identical to that
pack; the per-map class table changes to the B5 metadata. The refreshed map
also passes schema/reference/manifest validation. No other maps outside the
supplied set were rebuilt. No published archives, source assets or remote
services were changed.
All 84 refreshed maps have their audio metadata. MoonD's two missing streams
and absent native bank match its previous published pack; this refresh does
not introduce that limitation.

Reproducible local records: `local/b5-fixes/content/full-conversion-report.json`,
`full-audio-map-report.json`, `full-validation-report.json`,
`moond-conversion-report.json`, `moond-validation-report.json`,
`moond-source-verification.json`, `b5-volume-classification.json`,
`final-smokes.xml`, `final-effects.xml` and `export-smokes.xml`.
The private native-analysis helper was saved and stopped after its evidence
was exported; no analysis server remains running for this slice.
