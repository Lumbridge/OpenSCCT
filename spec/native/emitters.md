# Particle emitters (AEmitter, UParticleEmitter and its sprite, spark, mesh and beam kinds)

Units:
- eng:AEmitter::Tick, eng:UParticleEmitter::Initialize, eng:UParticleEmitter::Reset, eng:UParticleEmitter::SpawnParticles,
  eng:UParticleEmitter::SpawnParticle, eng:UParticleEmitter::UpdateParticles, eng:UParticleEmitter::Scale,
  nat:Engine.ParticleEmitter.Trigger, nat:Engine.ParticleEmitter.SpawnParticle, nat:Engine.Emitter.Kill
- eng:USpriteEmitter::Initialize, eng:USpriteEmitter::UpdateParticles, eng:USpriteEmitter::FillVertexBuffer,
  eng:USpriteEmitter::RenderParticles, eng:FD3DRenderInterface::SetParticleMaterial
- eng:USparkEmitter::Initialize, eng:USparkEmitter::SpawnParticle, eng:USparkEmitter::FillVertexBuffer
- eng:UMeshEmitter::Initialize, eng:UMeshEmitter::RenderParticles
- eng:UBeamEmitter::SpawnParticle, eng:UBeamEmitter::UpdateParticles, eng:UBeamEmitter::RenderParticles

## Purpose
Every particle effect of the game (the smoke, chaff, gas and explosion clouds, sparks, muzzle smoke, shell casings,
map fires and steam) is an Emitter actor holding an array of sub-emitters (ParticleEmitter objects of one of four
draw kinds). The engine updates each sub-emitter once per frame on every machine that draws; nothing about particles
is replicated. This spec describes that update and how each kind is drawn, so the client draws the effect classes'
own properties (converted by the content step `fx`, docs/EFFECTS.md) as the original does.

## Data
Property names and enum values are the script's (Engine.ParticleEmitter, Engine.SpriteEmitter...). Ranges are
`(Min, Max)`; a RangeVector picks X, Y and Z independently. Every pick is `Max + (Min - Max) * FRand()` with
`FRand() = rand() / 32768`, so a range is uniform between its ends whichever is larger.

The per-particle record (200 bytes in this build): Location, OldLocation, Velocity, StartSize, SpinsPerSecond,
StartSpin, RevolutionCenter, RevolutionsPerSecond, RevolutionsMultiplier, Size, StartLocation, ColorMultiplier,
VelocityMultiplier, Color (bytes), Time, MaxLifetime, Mass, HitCount, Flags (1 alive, 2 resting, 4 spawned this
tick) and Subdivision (-1: chosen by age).

Notable differences from other UE2 builds (this engine has no `StartLocationShape`): `SphereRadiusRange` and
`StartLocationPolarRange` are declared but never read; a particle starts only in the `StartLocationRange` box.

## Rules

### Emitter actor (AEmitter::Tick)
1. Each frame, for each sub-emitter in array order: a `Disabled` one is skipped (not updated). One not yet
   initialized is initialized (rule 3). The sub-emitter's owner is the actor.
2. While the sub-emitter's `InitialDelay` is above 0 it is only counted down (`InitialDelay -= dt`, never below 0);
   once 0 it is updated every frame:
   1. Its `GlobalOffset` is the actor's GlobalOffset.
   2. Warm-up, once: with `WarmupTicksPerSecond` and `RelativeWarmupTime` both set, the update runs
      `round(avg lifetime x RelativeWarmupTime x WarmupTicksPerSecond)` times with dt `1 / WarmupTicksPerSecond`.
   3. Not all dead: `InactiveTime` grows while the sub-emitter was not drawn (`Inactive`), else is 0. The update
      (rules 5 to 8) runs unless `SecondsBeforeInactive` is set and InactiveTime exceeds it (an emitter out of view
      for more than SecondsBeforeInactive, default 1 s, freezes; the actor's AutoDestroy overrides this).
   4. All dead: with `AutoReset` (and the actor not AutoReset) `TimeTillReset` counts down and the sub-emitter is
      reset at 0 (a new `TimeTillReset` from AutoResetTimeRange); with `AutoDestroy` (and the actor not AutoDestroy)
      the sub-emitter is disabled.
3. **Initialize**: the particle array gets MaxParticles records; `MaxActiveParticles = MaxParticles`; Reset.
   **Reset**: no particle has been used (`ActiveParticles = 0`, the next slot is 0, the other-emitter counter 0), not
   inactive, not all dead, `InitialDelay` from InitialDelayRange, `TimeTillReset` from AutoResetTimeRange.
4. **Trigger** (ParticleEmitter.Trigger): with `TriggerDisabled` the `Disabled` flag is toggled; with `ResetOnTrigger`
   the sub-emitter is reset; `CurrentSpawnOnTrigger = round(SpawnOnTriggerRange)`, and when that is not 0 the
   sub-emitter is no longer inactive or all dead. **Kill** (Emitter.Kill): no more particles spawn; the actor goes once
   they are dead.

### Spawning (UParticleEmitter::UpdateParticles, first part)
5. The rate in particles per second:
   - while `ActiveParticles < MaxActiveParticles` (not every slot used once yet): with `AutomaticInitialSpawning`,
     `MaxActiveParticles / ((LifetimeRange.Min + LifetimeRange.Max) / 2)`, else `InitialParticlesPerSecond`;
   - afterwards `ParticlesPerSecond`;
   - plus `SpawnOnTriggerPPS` while `CurrentSpawnOnTrigger` is not 0.
   With a rate above 0 and no kill pending, **SpawnParticles(fraction, rate, dt)**: `n = floor(fraction + rate dt)`
   clamped to `[0, MaxActiveParticles]` (and to CurrentSpawnOnTrigger, which is reduced by n, when that is set);
   particle k of the n (k = 0..n-1) spawns in the next slot with the time offset
   `dt + fraction / rate - 1 / rate - k / rate` (its age at the end of the frame); the slot index wraps at
   MaxActiveParticles and `ActiveParticles = max(ActiveParticles, slot + 1)`; the new fraction is
   `fraction + rate dt - n`. Particles spawned by script (SpawnParticle) are deferred to here.
   So an emitter with `RespawnDeadParticles` off and no `ParticlesPerSecond` makes exactly MaxParticles particles
   once (at InitialParticlesPerSecond, or spread over one lifetime when automatic), then nothing.
6. **SpawnParticle(slot, offset)**, in this order:
   1. Nothing when MaxParticles is 0, no owner, a kill pending, or `LifetimeRange.Max <= 0`.
   2. Location = StartLocationOffset + pick(StartLocationRange); Velocity = pick(StartVelocityRange);
      ColorMultiplier = (1, 1, 1).
   3. With `AddLocationFromOtherEmitter`, the location of particle `n mod Active` of that sub-emitter (n counts every
      spawn of this sub-emitter) minus the owner's location is added.
   4. `UseRotationFrom`: PTRS_Actor turns the location by RotationOffset then by the owner's rotation; PTRS_Offset
      by RotationOffset; PTRS_Normal by `RotationNormal.Rotation()` (with PTEA_PositiveZ its pitch is lowered by a
      quarter turn).
   5. RevolutionCenter = pick(RevolutionCenterOffsetRange); RevolutionsPerSecond = pick(RevolutionsPerSecondRange).
   6. PTCS_Independent: the owner's location (plus offset x the owner's velocity) is added to the location and to the
      revolution centre. PTCS_Relative and PTCS_Absolute leave them as they are (Relative is drawn in the owner's frame).
   7. GlobalOffset is added, then the spawn position passed in (zero for rate spawns). OldLocation and StartLocation
      are the location.
   8. ColorMultiplier x= pick(ColorMultiplierRange); MaxLifetime = pick(LifetimeRange); HitCount 0; Flags = alive
      (plus "spawned this tick" for rate spawns); Time = pick(InitialTimeRange) + offset; Mass = pick(StartMassRange).
   9. StartSize = pick(StartSizeRange), with `UniformSize` Y = Z = X; Size = StartSize.
   10. Velocity += offset x Acceleration; VelocityMultiplier = RevolutionsMultiplier = (1, 1, 1).
   11. The velocity is turned as in rule 6.4.
   12. `GetVelocityDirectionFrom`, with `dir` the unit vector from the owner to the particle (the particle's own
       location for PTCS_Relative): PTVD_StartPositionAndOwner `Velocity = -(Velocity x dir)` per axis (inward),
       PTVD_OwnerAndStartPosition `Velocity = Velocity x dir` per axis (outward), PTVD_AddRadial
       `Velocity += dir x pick(StartVelocityRadialRange)`.
   13. With `AddVelocityFromOtherEmitter`, that particle's velocity x pick(AddVelocityMultiplierRange) is added.
   14. Location += offset x Velocity. Mass becomes 1 / Mass when not 0.
   15. StartSpin = pick(StartSpinRange), SpinsPerSecond = pick(SpinsPerSecondRange); each axis of SpinsPerSecond is
       negated when FRand() < that axis of SpinCCWorCW; both are scaled by 65535 (rotator units).
   16. `UseRandomSubdivision`: Subdivision = int(SubdivisionStart + FRand() (SubdivisionEnd - SubdivisionStart)) when
       SubdivisionEnd is set, else int(FRand() x USubdivisions x VSubdivisions); otherwise -1.
   17. When Time already exceeds MaxLifetime the slot is spawned again with offset `Time mod MaxLifetime`.

### Particle update (UParticleEmitter::UpdateParticles, second part, every alive particle in slot order)
7. A particle "moves" unless it is resting (flag 2), but always under PTCS_Relative. A particle spawned this tick
   neither ages nor moves this tick (the flag is cleared). Otherwise `Time += dt`.
8. `Time > MaxLifetime`: with `RespawnDeadParticles` the slot spawns again (age `Time mod MaxLifetime`) and skips the
   move; without, the particle dies.
9. Move: `Velocity += Acceleration dt`; OldLocation = Location; `Location += Velocity x VelocityMultiplier dt` (per
   axis). With `UseRevolution` the location then turns about RevolutionCenter by
   RevolutionsPerSecond x RevolutionsMultiplier x dt turns about the X, Y and Z axes.
10. Collision (`UseCollision`, a moving particle, not PTCS_Relative): with `d` the unit motion direction and
    `e = d x ExtentMultiplier x Size`, a world trace (the level and world geometry, flags 0x84) from `Location + e` back to OldLocation. On a
    hit: the particle is at `hit - e`; a collision sound may play (CollisionSound, CollisionSoundIndex,
    CollisionSoundProbability; PTSC_LinearGlobal, PTSC_LinearLocal by HitCount, PTSC_Random).
    - With `UseMaxCollisions` and `HitCount + 1 >= round(pick(MaxCollisions))` the particle dies (respawns with
      RespawnDeadParticles, age dt / 2) and is not updated further this tick.
    - Otherwise it bounces: `Velocity -= Acceleration dt / 2`; Velocity is mirrored by the hit normal
      (`v - 2 (v . n) n`) and multiplied per axis by pick(DampingFactorRange); with `DampRotation` SpinsPerSecond x=
      pick(RotationDampingFactorRange); Location = OldLocation = `hit + n x 0.01`; HitCount + 1.
    - `SpawnFromOtherEmitter` (an initialized sub-emitter, dt above 0): SpawnAmount particles of it start at the
      hit point, each with this velocity x pick(SpawnedVelocityScaleRange) added when `UseSpawnedVelocityScale`.
11. `t = clamp(Time / MaxLifetime, 0, 1)` (0 when MaxLifetime is 0). The curves below are sampled at
    `x = frac((Repeats + 1) t)` (frac(v) = v - floor(v)): the first point whose RelativeTime is at least x, interpolated
    linearly from the point before it; before the first point the previous value is the curve's start value (size 1,
    colour opaque white, velocity and revolution multipliers 1); past the last point the curve does not apply (size
    scale 1, colour white). A point whose RelativeTime is 0 gives its own value.
12. Size: `UseSizeScale` with `UseRegularSizeScale` gives `1 / (Time + 1)`, without it the SizeScale curve
    (SizeScaleRepeats); `Size = StartSize x scale`.
13. `UseVelocityScale`: VelocityMultiplier from the VelocityScale curve (VelocityScaleRepeats).
    `UseRevolutionScale`: RevolutionsMultiplier from the RevolutionScale curve (RevolutionScaleRepeats).
14. Colour: white; with `UseColorScale` the ColorScale curve (ColorScaleRepeats), each byte rounded; the curve's
    alpha is used only with PTDS_AlphaBlend (other styles keep 255). R, G and B are then multiplied by
    ColorMultiplier (rounded). Then the fade:
    - fade out applies when `FadeOut` and `Time > FadeOutStartTime` and `MaxLifetime != FadeOutStartTime`:
      `f = (Time - FadeOutStartTime) / (MaxLifetime - FadeOutStartTime)` with FadeOutFactor;
    - else fade in when `FadeIn` and `Time < FadeInEndTime` and FadeInEndTime is not 0:
      `f = (FadeInEndTime - Time) / FadeInEndTime` with FadeInFactor;
    - PTDS_Modulated: the colour becomes neutral grey (127, 127, 127) with alpha `255 - round(factor.W x 255 f)`;
      PTDS_AlphaBlend: only alpha is lowered, `alpha - factor.W x 255 f` clamped to 0..255;
      other styles: each of R, G, B and A is lowered by its factor (X, Y, Z, W) x 255 f, clamped to 0..255.
15. Then MaxAbsVelocity clamps each velocity axis whose limit is not 0; velocity loss is
    `v.axis -= pick(VelocityLossRange.axis) x v.axis x dt`, picked again every tick for every particle; a particle that
    bounced this tick and whose speed squared is below MinSquaredVelocity (when set) comes to rest (flag 2).
16. After the pass: `AllParticlesDead` is set when every used slot is dead (or as many particles died as there are
    slots), the rate is 0 and RespawnDeadParticles is off; else it is cleared.

### Sprites (USpriteEmitter)
17. Initialize: RealProjectionNormal = ProjectionNormal made unit; `UniformSize` is forced on when UseDirectionAs is
    PTDU_None (it then reads only Size.X).
18. Each alive particle is one quad with corners `L - A - B`, `L - A + B`, `L + A - B`, `L + A + B` and texture
    coordinates (u0, v1), (u1, v1), (u0, v0), (u1, v0), so A points up the texture and B to its right. With the camera's
    up `U`, right `R` and forward `F`, the motion direction `D = unit(Location - OldLocation)` (PTDU_Normal: the
    projection normal, turned by the owner's rotation under PTRS_Actor) and `C = unit(D x (Location - camera))` (for
    PTDU_UpAndNormal and PTDU_RightAndNormal `C = D x normal`):
    PTDU_None `A = U Size.X, B = R Size.X`; PTDU_Scale `A = U Size.Y, B = R Size.X`; PTDU_Up and PTDU_UpAndNormal
    `A = D Size.Y, B = C Size.X`; PTDU_Right and PTDU_RightAndNormal `A = C Size.Y, B = D Size.X`; PTDU_Forward and
    PTDU_Normal: `P` is the world Y axis when |D.X| > 0.57, else Z when |D.Y| > 0.57, else X; `A = (D x P) Size.Y`,
    `B = unit(A x D) Size.X`. So Size is a half extent. PTDU_UpAndNormal and PTDU_RightAndNormal cross with the stored
    normal (not the one PTRS_Actor turned). Under PTCS_Relative the camera location and axes are moved into the owner's
    frame first and the quads are drawn through the owner's transform, so the projection normal and the best axis P
    are the owner's, not the world's.
19. Spin (`SpinParticles`): the angle `round(StartSpin.X + Time SpinsPerSecond.X)` in rotator units (when
    SpinsPerSecond.X is negative: `65535 - (round(StartSpin.X - Time SpinsPerSecond.X) & 65535)`), turning A and B
    about F (about D for PTDU_Forward and PTDU_Normal).
20. Subdivisions (USubdivisions x VSubdivisions cells): a cell index i maps to `u0 = (i / V) / U` and
    `v0 = (i mod V) / V` (the index runs down a column first), `u1 = u0 + 1/U`, `v1 = v0 + 1/V`, each clamped at 1.
    A random subdivision is the particle's own. Otherwise, by age: `UseSubdivisionScale` takes the first SubdivisionScale
    entry above t; else `floor(U V t)`, or `floor(max(1, End - Start) t) + Start` when SubdivisionEnd is set; clamped to
    `[0, U V - 1]`. With `BlendBetweenSubdivisions` (not random) the next cell is the second texture set and the vertex
    alpha is the blend weight between the two cells.
21. Blending (SetParticleMaterial; texture x vertex colour): PTDS_Regular opaque (ONE, ZERO) with the alpha test,
    PTDS_AlphaBlend (SRCALPHA, INVSRCALPHA), PTDS_Modulated (DESTCOLOR, SRCCOLOR: twice the product) of the texture
    blended toward the vertex colour by the inverse of the vertex alpha, PTDS_Translucent additive (ONE, ONE),
    PTDS_AlphaModulate (ONE, INVSRCALPHA), PTDS_Darken (ZERO, INVSRCCOLOR), PTDS_Brighten (ONE, INVSRCCOLOR). AlphaTest
    uses AlphaRef; ZTest and ZWrite as set (depth writes off by default). Particles draw in slot order, sub-emitters in
    array order.

### Sparks (USparkEmitter)
22. Each particle keeps its spawn location and velocity, `TimeBeforeVisible` from TimeBeforeVisibleRange and
    `TimeBetweenSegments` from TimeBetweenSegmentsRange. It is drawn as `round(LineSegmentsRange.Max)` line segments:
    segment k joins the points at `max(0, Time - k TimeBetweenSegments)` and the same with k + 1, where the point at
    time s is `spawn location + s velocity + s^2 Acceleration` (a trail of the particle's path), in the particle colour,
    u 0 to 1 across, v = k / segments.

### Meshes (UMeshEmitter)
23. Initialize: MeshExtent is the mesh's bounding box half size; the extent multiplier for collisions is
    ExtentMultiplier x MeshExtent. StartSizeRange defaults to 1: Size scales the mesh.
24. Each particle draws StaticMesh at `Location + R(spin) (Size x (vertex - owner PrePivot))`, with spin (when
    SpinParticles) the rotator `(Pitch = Y, Yaw = X, Roll = Z)` of `int(StartSpin + Time SpinsPerSecond)`; under PTCS_Relative
    the owner's rotation and location follow. `UseMeshBlendMode` (default) keeps the mesh's own materials (per
    section the owner's skin, else the mesh's), lit by the owner's light list set once before the particles, and
    wrapped in a colour modifier of the particle colour when `UseParticleColor`; otherwise each section draws through
    the owner's particle material: the emitter's DrawStyle, `RenderTwoSided`, the colour as TFactor, the emitter's
    alpha test, ZTest and ZWrite, and the section's material as the texture only when it is a bitmap, unlit.

### Beams (UBeamEmitter)
25. A beam particle joins its location to an end point given by DetermineEndPointBy (PTEP_Velocity: the velocity;
    PTEP_Distance: pick(BeamDistanceRange) along the velocity; PTEP_Offset / PTEP_OffsetAsAbsolute: a BeamEndPoints
    offset; PTEP_Actor: an actor's location; PTEP_TraceOffset: a trace along the offset), with LowFrequencyPoints and
    HighFrequencyPoints along it displaced by pick(LowFrequencyNoiseRange) and pick(HighFrequencyNoiseRange), drawn as a
    camera-facing strip of half width Size.X with the texture scaled by BeamTextureUScale and BeamTextureVScale.

## Constants
| Name | Value | Provenance | Source |
|---|---|---|---|
| Particle record | 200 bytes | decoded | slot stride in UpdateParticles and SpawnParticle |
| Spin and rotation unit | 65535 per turn | decoded | SpawnParticle scales StartSpin and SpinsPerSecond by 65535.0 |
| Collision trace flags | 0x84 | decoded | UpdateParticles SingleLineCheck |
| Bounce push-out | 0.01 uu | decoded | UpdateParticles |
| Best axis threshold | 0.57 | decoded | the sprite fill's axis helper |
| Modulated fade grey | 0x7f7f7f | decoded | UpdateParticles, SetParticleMaterial texture factor |

## Network
None: emitters are drawn locally from their actors (Emitter RemoteRole None). Effect classes that need to be seen
by everyone are spawned on each machine by script, or replicated as actors (SSmokeEmitter, SChaffeEmitter,
SGrenadeExplosion are SimulatedProxy).

## Evidence
Editor twin (ChaosTheory_Editor.exe, the same engine code as the game; guard-named): AEmitter::Tick 0x110db890,
UParticleEmitter::Reset 0x110eb480, ::SpawnParticles 0x110eb6e0, ::Initialized 0x110eba00, ::SpawnParticle
0x110ebc10, the update 0x110ec950 (vtable slot 0x7c, called by USpriteEmitter::UpdateParticles 0x110eff40),
::Scale 0x110eb560, execTrigger 0x110eb300; USpriteEmitter::Initialize 0x110ee7c0, ::RenderParticles 0x110f0160 and
its vertex fill 0x110eebf0 (vtable slot 0x88); USparkEmitter::SpawnParticle 0x110f2d70, ::Initialize 0x110f30a0,
vertex fill 0x110f3340; UMeshEmitter::Initialize 0x110f3980, ::RenderParticles 0x110f3dc0; UBeamEmitter 0x110f4b60,
0x110f5d60, 0x110f62c0; FD3DRenderInterface::SetParticleMaterial 0x10f34260; helpers RangeVector pick 0x110db100,
Range pick 0x10eeeec0, FRand 0x10fa3370, the best-axis helper 0x110eea10, RotateAngleAxis 0x10fe9070. Property
offsets from the script packages (Engine.u ParticleEmitter 0x28..0x3e8). Decompiled text stays in local/re-out/c/fx/.

## Open questions
- The beam's noise, branching and sheet layout (rule 25) are summarized, not decoded line by line; the game's
  effect classes use one beam (SElectricArcEmitter, not spawned by any gameplay script) and no map uses one.
- Sounds on spawn and collision (rule 10) are not played yet.
- The sprite's thermal pass uses TextureInThermicVisionPSX2 on the PS2 only; the PC draws the normal texture.
