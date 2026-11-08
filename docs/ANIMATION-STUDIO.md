# Animation Studio runtime

The Godot-free `OpenScct.Animation` library owns skeletons, native keys, curve/layer/rig evaluation, first-person
profiles, graphs and sequences. Editor Core owns commands, imports, projects and compiled pack creation. The game
retains its existing pawn/weapon action state machines; explicit bindings replace their animation slots.

## Compiled contract

`sim/animations.json` is `svm.animation/1`, with `requiresRuntime: 1`, a compiled `osanim/1` project and a sorted
`assets` map of relative paths to SHA-256 digests. Models/clips live under `render/animations/`, audio under
`audio/animations/`. The simulation manifest includes bindings, timing, graph/sequence definitions, typed events,
root tracks and required visual digests. A changed collision-bearing pose therefore changes the simulation hash.
Models must have required digests; unsafe paths, missing/changed assets, unsupported runtime versions and duplicate
source bindings are rejected. Native project IDs permit ASCII letters, digits, underscores and hyphens.

Project transforms are metres, Y-up, with System.Numerics row-vector matrices. `StudioSpace` converts the existing
Unreal centimetre/rotator convention. Skeleton display transforms retain converted mesh scale/RotOrigin. Sparse
translation, normalized quaternion rotation and scale tracks support Step/Linear/Cubic keys and tangent vectors.
Direct source keys survive export; authoring layers/constraints bake in Editor Core, defaulting to 60 samples/sec.
`sourceNormalizedTime` retains stock notify values, including legacy values outside the nominal clip range.

Binding target/slot comparisons ignore case. Resolution is stock defaults, selected session pack, then map-local
overrides. IDs merge by identity; bindings merge by target/slot. A source may not define a slot twice. A visual
binding retains the stock slot's duration, notifies, root movement and hit volumes; `AnimSampler` rescales native
key time to that duration. Gameplay mode explicitly changes provider timing, supported notifies, root movement and
animated collision poses. Existing stock content takes the original sampling path when no binding applies.

## Authority and replication

`AnimationWorld` is level-scoped. `AnimationController` is a persistent, always-relevant replicated actor carrying
binding index, graph states/parameters, blend clocks, sequence generation and phase. Server ticks run transitions,
gameplay events and sequence transforms. Collision-bearing movement uses the physics encroachment service; blocked
motion stops the sequence. Clients sample poses and interpolate phase, without dispatching gameplay edges.

Graph/sequence snapshots restore current state for late joins without replaying old events. Event intervals are
`(from,to]`, with an explicit start edge on fresh playback. Sequence audio follows active segments and seeks to the
current offset for late joins. Trigger/untrigger starts/stops sequence controllers, so existing map event wiring can
drive them. Attach/detach events switch actor bases. Custom graphs bind to `actor:ActorId`; a studio-created prop
uses `osanim.<skeleton-id>` as its model asset. Its skinned GLB comes from the compiled bundle.

Camera tracks require an explicit `camera:local` or `camera:all` binding. The rendered play camera restores its
normal view when playback stops or is interrupted, forcing a cut to avoid an interpolation jump. Preview and
scrubbing have temporary scene instances and never dispatch gameplay edges.

First-person Spy profiles pair separate arms and P190/Shocker actions with a stored weapon mount. The Spy grip
solver and reference-aspect FOV conversion are shared with the editor. Merc rifle/Uzi/shotgun clips use the native
view-model channels. Authoring an unused action slot does not add new game behavior; holster has an explicit
authored alias while existing weapon draw/holster selection remains authoritative.

## Selecting and testing content

Content > Animation Packs imports a validated `.osanimpack` into the user's pack library and explicitly selects
one pack (or Stock). Selection is locked during a session. The choice persists in `user://animation-pack.txt`.
The selected canonical manifest contributes to `MapCatalog.CharacterContentHash`; mismatched clients cannot join.
The replicated class contract also rejects runtimes lacking the controller fields.

Automation/source launches can use `--svm-animation-pack=<folder>` (or `SVM_ANIMATION_PACK`).
`--svm-animation-probe` prints `OPENSCCT_ANIMATION_RUNTIME=1` and exits.
`--svm-animation-preview=<folder>` enters an isolated game-side preview; optional
`--svm-animation-fp=<profile-id>` selects first-person framing. Space pauses, arrows change actions/clips and Escape
closes. The editor creates temporary overlays for these tests and never replaces installed stock files. A source
game with the studio runtime is required until a release containing this feature is published.

Regression coverage lives in `AnimationStudioTests`, existing animation/equipment/content/network tests and the
editor's `tools/animation-checks`. Live human multiplayer and original-game visual comparisons remain separate
acceptance work. Runtime procedural IK, original animation-package export and automatic pack distribution are
outside this contract.
