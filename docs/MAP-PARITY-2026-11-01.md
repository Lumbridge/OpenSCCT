# Map parity sweep — 2026-11-01

The automated sweep covers all 85 repository-converted maps, their 28,718 simulation actors and 8,701 event
edges. The 83 maps with an available original package contain the same gameplay actor records and stored
properties as those packages, with no decode failures. MoonD and TestRange have no original package in the
reference install and were checked only as converted content. These results establish conversion and runtime
coverage; they do not establish complete interactive or visual parity with the original game.

## Fixed gaps

- **Periodic fire and hazard damage:** `SDamageVolume` called the pawn's empty `TakeDamage` implementation.
  It now calls the authoritative player's controller damage path, preserving the authored class filters,
  protected controller states and native timing rules. SteeE's bottle ignites its damage volume after one second,
  deals repeated 30-point damage and stops at the authored delay. A separate real-player regression verifies
  shooter kill credit, environmental-kill credit, damage, score and the victim's lost life.
- **Missing destruction effects over the network:** map emitter triggers used an unreliable presentation
  message. The original declares `Emitter.ReplicateTrigger` reliable. The relay now uses the existing reliable
  message without changing its wire layout. Gas-cylinder, can and bottle effects reach three clients once and
  in order through a forced outage, loss, duplication and reordering. With the props relevant to all observers,
  their intact meshes disappear and their bound colliders are disabled on all three clients.
- **Particles falling through the map:** placed emitters did not receive a collision trace. They now use the
  current client geometry during play, including moved and destroyed props, and initial map geometry in previews.
  The content inventory contains 399 collision-enabled sub-emitters across 29 maps. The SteeE smoke verifies
  moving blast particles, animated atlas frames, the can's separate flying mesh, floor collisions, delayed fire
  and the distinct lifetimes of all three effects.
- **Installed PrisD failed to load:** one older installed pack uses the mode key `7` with `GameModeId=7`.
  A narrow read-compatibility case preserves the original key, canonical bytes, hash and matching UI text while
  allowing `MercDeathmatch` lookup. Other numeric or mismatched keys remain invalid. All 80 installed maps then
  passed the loader checks. Installed content was not modified.
- **Missing decorative sprite lights:** 586 visible light sprites on 14 maps had no rendering path; textures
  used only by those sprites were also omitted during export. The converter now exports their texture, size,
  scale and blend metadata, and the renderer draws camera-facing sprites through the existing material/vision
  system, on the correct world or sky layer. This includes ClarD's streetlights and sky moons. The map conversion
  cache version was bumped so Prepare game files includes the new assets. The 14 repository content packs were
  refreshed and their complete manifest hashes verified; their simulation hashes did not change.
  Native sprite rendering uses constant vertex colour rather than mesh lighting, so sprites also remain visible
  when their actor leaves `bUnlit` at its default; a masked StatE moon regression covers this case.
- **Crane lamps remained at their original positions:** the original native `AttachToBone` and `SetBase` paths
  accept static lights. The renderer now resolves the five render-only flare attachments on each Stee crane and
  moves their sprites with the animated bones. Native evidence and remaining visual limitations are recorded in
  `spec/devices/animated-mesh.md`.

## Coverage

Counts overlap through inheritance: lifts, lift doors and rotating movers are included in movers; objective
devices and items can also be gameplay props.

| Area | Converted instances | Verification |
|---|---:|---|
| Movers | 1,857 | Typed load, properties, references; mover/device regression suites |
| Elevators | 41 on 28 maps | All 91 callable-floor routes, actual call switches, door sequencing, arrival and rearming |
| Rotating movers | 26 | Typed load plus rotation/pause/resume regression |
| Switches | 805 | 301 hackable, 6 held, 2 delayed; interaction creation and all 4 team managers' targets |
| Computer objective terminals | 334 | Typed load and objective interaction regressions |
| Items and supply stations | 2,505 | Actor/property retention and existing item/objective/station regressions |
| Damageable gameplay props | 2,137 | Actor/property retention, damage regressions and real SteeE chains |
| Damage volumes | 327 | Authority, class filtering, protected states, periodic damage and kill credit |
| Event relays | 463 | Actor/property retention, event index validation and device regressions |
| Animated props | 589 | Authored sequence availability and animation-notify event index validation |
| Map emitters | 2,132 | Exported emitter coverage where render FX are present; SteeE rendering and network regressions |
| Security devices | 1,778 | Actor/property retention, 317 control-zone references and device regressions |
| Visible light sprites | 586 on 14 maps | Texture export, blend/sky layer, rendered pixels; all five crane bone followers |

Every repository-converted map passed canonical roundtrip, simulation hash, derived indexes, actor references,
class-table validation and runtime loading. There were no unknown simulation classes, failed spawns, or
property type/class mismatches. FX metadata was checked in 84 maps; TestRange has no `render/fx.json` and is
explicitly marked unchecked in the detailed report.

There are 231 events with no converted tag receiver. They also exist in the source maps and are recorded as
review candidates, not automatically repaired. Five event edges also name render-only static meshes in SlauS,
ZiopD and ZiopS; those meshes have no custom trigger behavior. No behavior was invented from their tag names.

## Validation and remaining limits

- Both solutions build with zero warnings and errors.
- 322 device/objective tests passed, none skipped.
- Three focused integration tests passed: all-map elevator routes, all-map switch/security binding, and
  three-client destructible effects under loss/reordering.
- Ten content checks passed against both repository and installed content, including the legacy-mode
  regressions and all-map load.
- Twelve synthetic audit-tool tests and four sprite-export tests passed.
- The destructible FX smoke passed headlessly and with Vulkan rendering. Six rendered gas, can and bottle
  previews were inspected; the bottle's persistent floor fire and flying can debris are visible.
- The light-sprite smoke covers shader/material/layer checks and a Vulkan shown-versus-hidden pixel comparison
  on SteeE, ClarD and StatE. The crane smoke also checks five visible additive sprites following the final bone poses.

The elevator sweep exercises device sequencing without collision, rider movement or encroachment. The network
test covers the reported destructibles rather than every map event. Retail frame-by-frame visual/audio
comparison, complete player traversal of every event chain, and named Easter eggs remain unverified. Event
identifiers do not provide a reliable catalogue of Easter eggs. The gas explosion's authored atlas remains
coarse at close range. Sprite dimensions follow the decoded texture-size and authored-scale calculation; matching
their exact retail framing remains part of the visual comparison work.

## Repeat the checks

The new read-only inventory uses the repository's converters and emits only identifiers and diagnostic
summaries to a local report; it does not modify map content.

```powershell
$env:PYTHONPATH = 'content'
python -m svmbuild.mapconv.audit --source
python -m pytest tests/content/test_map_parity_audit.py -q

dotnet test --project tests/OpenScct.Content.Tests --filter-class OpenScct.Content.Tests.AllMapsTests
dotnet test --project tests/OpenScct.Integration.Tests --filter-class OpenScct.Integration.Tests.MapLiftParityTests --filter-class OpenScct.Integration.Tests.MapInteractionParityTests --filter-class OpenScct.Integration.Tests.DestructibleEffectNetworkTests
```

`--content`, `--game`, `--maps` and `--out` select the inventory inputs and output. Source packages are read-only.
The detailed `parity.md`/`parity.json` are under `local/test-results/map-parity`; loader reports are under
`local/content`. Vulkan previews are under `local/destructible-fx-previews`. These derived artifacts stay local.
