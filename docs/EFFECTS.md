# Effects

The gameplay effects: particles, the meshes of thrown and placed gadgets, muzzle flashes, impact marks and sparks,
and flash lights. The engine rules are in `spec/native/emitters.md` (the particle natives, decoded); this page says
where the assets come from, which class draws what and how it reaches the screen.

## Content: `python -m svmbuild fx`

Also writes `skins` in `effects.json`: the three Stickess Shaders of `SBasePawn.BeginStickySkin` (Diffuse PNG,
self-illumination PNG and mask, and the TexPanner's pan as a unit direction and rate).

The step `fx` (`content/svmbuild/fx/`) reads the script packages and writes `.content/shared/fx/`:

| File | What |
|---|---|
| `effects.json` | every effect class (Emitter, Projectile, SMuzzleFlash, SImpactEffectBase, Projector, the flash lights, SFireTrace, SMines, SStickyCam, SLaserBeam families): its kind, the assets its defaults name (StaticMesh, Skins, ImpactMaterial, ProjTexture), and for an Emitter its sub-emitters (the class's subobjects in SBase.u, each with its class and stored values); the texture table (object path to PNG and the material's blending) and the mesh table (object path to glTF file and node) |
| `textures/*.png` | mip 0 of each texture an effect names (materials resolved to the texture they draw) |
| `meshes/<Package>.glb`, `meshes/<Package>.materials.json`, `meshes/textures/` | every static mesh package an effect names (SEffectStatic, SGameplayObjects), in the map render format |

The runtime lays each sub-emitter's stored values over its class defaults from `shared/classes.json`, as a map's
`render/fx.json` does. In this install: 95 effect classes, 40 Emitter classes with 66 sub-emitters (61 sprite, 2 mesh,
2 FlicFloc drawn as sprites, 1 beam), 51 textures, 21 meshes. Like all converted content it stays under `.content/`.

## Runtime

| Path | What |
|---|---|
| `game/Scripts/Render/Particles/ParticleParams.cs` | a sub-emitter's properties (Godot-free) |
| `game/Scripts/Render/Particles/ParticleEmitterSim.cs` | UParticleEmitter: spawning, SpawnParticle, the update (age, move, revolution, collision, curves, colour, fade, velocity loss) |
| `game/Scripts/Render/Particles/ParticleSystemSim.cs` | AEmitter::Tick over the sub-emitters (initial delay, warm-up, auto reset and destroy) |
| `game/Scripts/Render/Particles/ParticleGeometry.cs` | sprite quads (UseDirectionAs, spin, texture cells), spark trails, mesh placements, beam strips |
| `game/Scripts/Render/Particles/ParticleSystemView.cs` | the Godot meshes, filled each frame (sprite and beam quads written in place by `QuadBuffer.cs`, no Godot arrays per frame), with the draw style's blending (`game/Shaders/svm_particle.gdshaderinc`) |
| `game/Scripts/Render/Particles/Emitters.cs` | the map's placed emitters (`render/fx.json`) on the same simulation |
| `game/Scripts/Fx/FxLibrary.cs` | loads `shared/fx` |
| `game/Scripts/Fx/EffectViews.cs` | the gameplay effects of the client world |
| `game/Scripts/Fx/CombatFxView.cs` | shots: muzzle flashes, casings, tracers, impact decals and sparks; owns `EffectViews` |

`EffectViews` reads the client world after each tick and never writes to it:

- Every Emitter actor spawned during play is a particle system of its class, following the actor and gone with it:
  SSmokeEmitter (smoke grenade cloud), SChaffeEmitter (chaff), SGrenadeExplosion (frag), SStickyCamEmitter (sticky cam
  gas), SPoisonEmitter and SMinePoisonEmitter (poison mine), and any other Emitter class the simulation spawns.
- Every spawned actor drawn as a static mesh shows its class mesh, actor-lit, at its place, rotation and draw scale:
  thrown grenades (SProjSmoke, SProjChaffe, SProjFrag, SProjFlashBang...), the stuck sticky cam, mines, the alarm
  snare. Each mesh takes its actor's live vision values every frame (`GameplayObjects.VisionOf`: HeatIntensity,
  HeatOpacity, EMFIntensity while bEMFIsOn, EMFOpacity; class defaults such as mines 0.5 heat and 1 EMF, sticky cam
  0.2 and 1, snare 1 and 1, replicated in the first bunch), so they are warm in thermal and glow in EMF.
- The mines' laser beams (SLaserBeam_MineLaser of the laser mine, spy trap and poison mine) are not drawn here: their
  LasDetectBeam mesh is a render-pack gameplay mesh, drawn by `LaserTriggerView` through `Lasers` like the tripwires,
  from the mine along the beam's rotation for the replicated `fTailleFaisceau`, at its HeatIntensity (0.4), only in
  thermal and EMF (`bHideInNormalVision`).
- Effects the original spawns on each drawing machine from script, never replicated: the launch trails
  (SSmokeLaunchEmitter on smoke, chaff, flash-bang and phosphorus grenades that are not quick-thrown, SFragLaunchEmitter
  on frags, removed when the frag explodes), and what `Spawn` is asked for.
- The flash lights (SFlashLight, SFlashLight_FragGrenade, whose brightness fades from 255 over 0.5 s as its native tick).
  Light actors are bHidden (Engine.Light defaults) and still light: each one alive is handed to the map's dynamic world
  lights (`WorldLights.SetSpawned`, at most 16), so the frag's flash lights the walls and floor as the map's own dynamic
  lights do (the same colour, gain, radius and `1 - (d / R)^2` world falloff; SFlashLight is bAffectOwnZoneOnly, so only
  its zone). The muzzle actors' own lights join them for their 0.1 s (SMuzzleGenerique and sMuzzleCanon for the rifle,
  SMuzzleSticky for the Shocker: steady LE_PointAmbient, brightness 255, LightRadius 1, so 50 uu).
- An effect attached to a pawn's bone (AttachToBone: the sticky shock's seven SEffectShockedPawnEmitter) is drawn at that
  bone of the drawn skeleton; the simulation has none.
- The spy pistol's SLaserBeam (and the merc laser's) is left to its own view (`SpyLaserView`, `MercLaserView`), which
  starts it at the drawn hand; drawn here too it showed a second beam from the simulation's estimated hand point.
  The beam mesh has two sections, each with its own additive texture (`SpyLaserView.ApplyBeamLook`): the body (two
  crossed quads mapped to the LaserBeamShader's bright core) and a quad across the start (lasebeam2fin, a soft red
  glow). One material over both drew the body's streak on the start quad, a short dashed red line across the merc's
  muzzle. The local merc's start quad is hidden: the original's weapon overlay covers the start of his beam.
- The pawn skins effects put on: `SBasePawn.BeginStickySkin` draws the Stickess shaders over the shocked pawn's body
  (`StickySkins`, from `effects.json` "skins": the body Diffuse with SPersoTextures.sfx_perso.pan_sticky, a TexPanner of
  shadwagent_sticky_1 at 0.5 a second along V, as self-illumination and its mask; the template's `si_pan`).

`CombatFxView` per shot: SMuzzleSticky for the Shocker; SMuzzleGenerique and sMuzzleCanon for the rifle, with the
SDouilleEmitter casing (first person at the rifle's FX_DOUILLE bone); each impact mark draws its class's decal texture
(`ImpactMaterial`) and starts its spark emitter (SSparkBeton, SMetalSpark, SSparkWood, SSparkWater, SSparkShocker...).

## Checks

- `tests/OpenScctGame.Logic.Tests/FxParticleTests.cs`: the decoded rules on synthetic emitters.
- `tests/content/test_fx.py`: the step's helpers, and on the reference install the classes, sub-emitters, meshes,
  decals and the Stickess skins it must find.
- `--svm-smoke=play-fx` (`game/Scripts/Smoke/PlayFxSmoke.cs`): the effects from the real keys on SteeE, checking
  meshes in the camera's view and particle counts per class; with a window and `SVM_PLAY_SHOTS=<folder under local/>`
  it saves each step and timed moments of each effect.

## Flash-bang

The original has a spy flash-bang: `SGadFlashBang` (charge 5, "Disturbs the vision and hearing of everyone looking at
it when it blows"), `SProjFlashBang` (range 2500) and the `SFlashBang` emitter with its `SFlashLight`. It is option 6 of
the spy's team gadgets (STeamAttaquant.ClassInventory) and allowed by the default restriction mask, but it is not in
the default kit (smoke, chaff, sticky cam, heartbeat sensor), so it only appears when a player puts it in a slot.
Spec: spec/equipment/flash-bang.md. Its explode native flashes the mercs in range who face it (an SDTFlash hit: the
merc page's white-out, the after-image), leaves the `SFlashBang` flare, whose `SFlashLight` each machine spawns, and
the after-image is drawn by `game/Scripts/Render/Post/RemanenceEffect.cs` (svm_remanence.glsl) over the shown frame:
a capture at the flash, blended in by RemanenceBlendFactor (1 to 0 over 8 s; the blend itself is estimated).

## Goggle glow

Spec: spec/equipment/goggle-glow.md. Every spy and merc carries an `SGoggleBeam` on bone `B Head`, spawned on each
machine by the pawn's PostBeginPlay and never replicated (src/OpenScct.Game/Equipment/PawnVision.cs,
SBase/SGoggleBeam.cs). Its mode follows the goggle actions each machine executes (ExecuteActionForGoggle, actions 66 to
70, 72 and 269) and its skin changes where the goggle clip's notifies fire, so other players' and bots' glows change
on every machine through the action stream.

- Content: the chars step exports `SGoggle.ATT01_beams` and `SGoggle.DEF01_beams` to `shared/chars/SGoggle/` with the
  four skins (`TRA_goggle_green`, `TRD_goggle_orange`, `TRD_goggle_motion`, `TRD_goggle_EMF`) in `textures/`.
- Drawing: `game/Scripts/Render/Characters/GoggleGlowView.cs`, one per drawn pawn (SimPawnView). Fullbright and
  two-sided; the spy's green is alpha blended through its texture, the merc's skins are added. HeatOpacity and
  EMFOpacity 0 leave it out of thermal and EMF vision; night vision and motion tracking draw it.
- Who sees it: spies see every glow, mercs never see a spy's (the spy beam destroys itself on a merc's machine). The
  own merc's glow is not drawn in first person; the own spy's is drawn in third person.
- Look: a spy's glow is a large yellow-green centre lens with four small dots, always the same colour; a merc's is a
  lens band, orange in normal view, red in motion tracking, blue in EMF.
- Checks: tests/OpenScct.Game.Tests/Equipment/GoggleGlowTests.cs and the `goggle-glow` smoke (bots' and the player's
  own goggle keys, the drawn skin per step, shots with SVM_PLAY_SHOTS). Real-game captures of the spy from the front
  (normal, night, thermal) and the merc from behind, taken in the sandbox, are in local/goggle-glow/game/; the merc's
  red and blue need a goggle key in the real game, which the memory-only capture cannot press.

## Not done yet

- Effect lights light the world (above) but not pawns and props: actor lighting picks from the map's lights only.
  The Godot omni lights kept for them light nothing the templates draw.
- The sticky cam gas sprites are depth tested as UE2 draws them (no soft particles), so a sprite crossing a wall or the
  floor shows the straight line of that surface; not yet compared with a capture of the original.
- Map mesh emitters draw nothing: the meshes they name are not in the map content.
- Beam noise and branches, spawning and collision sounds, mesh skins swapped at run time (SMineProx's Skins).
- Decal size and blending are estimated (the projector natives are not decoded).
