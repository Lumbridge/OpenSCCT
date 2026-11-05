# Destructible parity sweep — 4 November 2026

The Aquarius kiosk's one-hit damage stage changes its screen material to cracked glass. It retains the cabinet
mesh and collision. Three missing links prevented that appearance in OpenSCCT: the map converter omitted
materials used only by damage/trigger stages, map-built mesh surfaces lost their original skin-slot identities,
and client net receive never applied RepSkin to Skins[0]. All three paths are now implemented.

## Installed-content coverage

The sweep rebuilt all 83 maps from the reference install and inspected the converted gameplay and render data:

- 1,341 actors have authored damage stages; 1,356 actors explicitly set bDamageable.
- All 841 authored replacement-mesh references resolve to exported map library assets.
- 187 damage-stage skin references use 13 materials across 27 map variants. These include Aquarius kiosks,
  Club electrical cabinets and neons, Factory electrical equipment, Krauser neons, Missile pipes and panels,
  ScheE lamps, Warehouse/Shadow lamps, and Steel Squat lights.
- Damage and trigger materials now have exported material definitions and texture payloads, or ConstantColor
  definitions where the source intentionally uses a solid color.
- Mesh sections retain their source index through skipped/empty sections, baked-color variants, map geometry,
  replacement meshes, and shared gameplay meshes.

These are asset and data checks, not a claim that every prop has been shot and visually compared in both games.
Event-driven debris and other destruction effects retain their existing implementation.

## Verification

- Focused content checks cover original section identity, damage/trigger material payloads across converted maps,
  existing emissive layers, material sequences and map parity auditing.
- Gameplay-object and Steel Squat destruction tests exercise damage processing and effect behavior.
- Aquarius integration coverage checks cracked-skin replication, retained mesh/collision, a late observer and reset.
  The existing loss/reordering test still checks Steel Squat gas/can/bottle effects.
- The Vulkan `destructible-skin` smoke checks the real Aquarius cabinet surfaces: only slot 0 gets cracked glass;
  the cabinet mesh and its other materials remain. Local intact/broken/torch captures are under
  `local/destructible-skin/`.

## Intact cabinet appearance

The kiosk mesh positions, indices, normals and three source material images match between the reference install,
Beta 5, and the currently running original game's install. The source mesh is deliberately shallow. This pass
preserves that geometry rather than substituting a different cabinet.

The lighting follow-up found a shared renderer error: the original fits reciprocal-linear D3D attenuation to
its cubic target falloff over the object's bounding sphere. OpenSCCT evaluated the cubic directly at each vertex
and cut it off at the nominal radius. The native fit restores the cabinet's frame and lower-panel detail. It is
used by all actor-lit props, devices, characters and weapons, with the special sun/ambient/quadratic light paths
retained. Baked world lighting is a separate path.

The converter now retains each static mesh's cooked light sphere. Radius uses the largest scale axis, not the
world AABB diagonal, and relighting/mesh swaps preserve the correct center and radius. All 83 maps and the shared
render pack were rebuilt. A live read-only probe captured 24 original point/spot light setups; their coefficients
match the decoded fit to about one part per million. The cabinet smoke also checks the scaled kiosk sphere against
its original runtime value, and checks rendered lower-panel brightness to catch a return to the black silhouette.
These checks establish this shared lighting correction, not pixel-perfect parity for every scene in the game.

Changes and rebuilt proprietary content are local; this sweep does not publish a release.
