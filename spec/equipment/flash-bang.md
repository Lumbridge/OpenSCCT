# Flash-bang (SGadFlashBang, SProjFlashBang, SFlashBang) and the flash on mercs

Units:
- def:SBase.SGadFlashBang, def:SBase.SProjFlashBang, uc:SBase.SProjFlashBang:PostBeginPlay
- nat:Engine.Projectile.Explode (SProjFlashBang C++ override, game 0x10ab83d0)
- nat:Engine.Projectile.NativeTouch (the ProcessTouch overrides of SProjFlashBang, game 0x10ab87b0, and SProjSmoke,
  game 0x10ac1cb0)
- def:SBase.SFlashBang, uc:SBase.SFlashBang:PostNetBeginPlay, def:SBase.SFlashLight

## Purpose
The spy's flash-bang ("Disturbs the vision and hearing of everyone looking at it when it blows"). It is thrown and
bounces like every grenade (`grenade-throw.md`). When it goes off, every merc within 2500 uu who faces it and has a
clear line to it is flashed: a zero-damage hit of type SDTFlash whose client side gives that merc the white-out,
the after-image and the whistle (`screen-effects.md` rules 7 and 14). Spies are never flashed. The smoke grenade
shares its pawn-touch rule (rule 6).

## Data
| Field | Class | Default | Notes |
|---|---|---|---|
| Charge, RefireRate, CanQuickLaunch | SGadFlashBang | 5, 2.0 s, true | option 6 of the spy's team gadgets, not in the default kit |
| Range | SProjFlashBang | 2500 | int, uu |
| TempsExplosion | SProjGrenade | 3 s | fuse |
| WallReboundFactor / ActorReboundFactor | SProjFlashBang | 0.2 / 0.2 | |
| ProjectileSpeed / MaxSpeed | SProjFlashBang | 900 / 1600 uu/s | |
| PercentDamageRefillable | SProjGrenade | 50 | passed with the flash hit |
| Sounds | SProjFlashBang | launch RA_GrenDegoup, impact SW_GrenRebonds, explode GR_FlashBang_explo, touched GR_FlashBang_siffle | SoundTouched has no reader in the decoded code |
| DES_fSeuilFlashBang_Cos | SPlayerController | 0.0 | facing threshold, cos 90 degrees |
| SFlashBang | Emitter | one sprite emitter (flare_anm, 4 x 4 frames, life 0.3 s, size x15 at 0.2, fade from 0.1 s), LifeSpan 5, AutoDestroy, RemoteRole SimulatedProxy | |
| SFlashLight | Light | brightness 2000, radius 20, LifeSpan 0.15 s, LE_PointAmbient, saturation 250, bAffectOwnZoneOnly, RemoteRole None | |

## Rules
1. **Launch** (`PostBeginPlay`): unless quick-launched, the launch sound (volume 1, radius 100) and, off a dedicated
   server, the SSmokeLaunchEmitter trail.
2. **When it goes off**: the fuse (`grenade-throw.md` rule 9), the first hit of a quick-launched one (rule 10a there,
   HitWall override 0x10ab8760), the BSP "nothing behind the hit" stop (rule 10 there), or a pawn touch (rule 6).
   All of them run `Explode` on the server only; a client copy is destroyed.
3. **Explode** (native, decoded game 0x10ab83d0), in this order:
   1. the launch trail (`SmokeLaunchEmitter`) is destroyed and cleared;
   2. for each SPlayerController (the class or a subclass) whose `SPawnType` is SPAWNDEFENSE (1) and whose `SBPawn`
      is set, with `P = SBPawn`: when `|P.Location - Location| < Range` (strict, Range as a float),
      `eye = P.Location + P.EyeHeight * (0, 0, 1) + P.WalkBob` and `d = SafeNormal(Location - eye)`; when
      `vector(P.Rotation) . d > DES_fSeuilFlashBang_Cos` (strict) and a zero-extent line from the grenade's
      `Location` to `P.Location` with source `P` and flags 0x80086 (level, movers, world geometry, backdrop skip)
      ends with `Time == 1` (nothing hit): `PC.PawnTakeDamage(0, Instigator, Location, (0, 0, 0), SDTFlash, 0,
      PercentDamageRefillable)` (`damage-pipeline.md`);
   3. every controller with `bIsPlayer` hears `SoundExplode` at the grenade (`CheckHearSound`, volume 1, radius 100,
      pitch 1), the same call as the chaff burst;
   4. an SFlashBang is spawned at `HitLocation` with the rotation `rotator(HitLocation)` (the native turns the
      location vector into a rotator; the sprite flare does not show it), owner the grenade's `Owner`, instigator
      its `Instigator`;
   5. the grenade is destroyed.
   The hit is only sent to mercs, so a spy (thrower or team mate) is never flashed, and a merc team mate of nobody
   in particular is: there is no team test.
4. **The flash on the merc** (server, then his machine): the zero-damage hit runs the merc's `PawnTakeDamage` (no
   health lost; the hit animation of rule 8 there with momentum 0) and `ClientDamage(SDTFlash)`, which tests again
   on his machine from his own camera (`damage-pipeline.md` rule 34: in front, `d . forward >= 0`, and a world trace
   clear), then `ClientDamageHudEffect(8)` (decoded game 0x10ab0990): nothing in thermal, motion or EMF vision
   (RendMap 36, 37, 38); otherwise `bCaptureRemanence` is set, the flash whistle plays through the audio subsystem
   and the FlashEffect slot runs 1 to 0 over 8 s (linear), which drives `RemanenceBlendFactor`; then
   `ClientStopBinoculars()`. On the merc HUD page the `Flash` white-out plays (`screen-effects.md` rule 14).
5. **After-image** (viewport post-render, decoded game 0x10a0b050). While `bCaptureRemanence` is set the render
   device captures the frame just drawn (scene, before the HUD) and the flag clears; every frame after that, when
   `RemanenceBlendFactor > 0.0039`, the capture is drawn over the scene with that factor (before the vision pass in
   normal and night vision, after it in thermal, motion and EMF). How the device blends it (render device virtuals
   0x80 and 0x84, in the Direct3D driver) is not decoded; the port blends the capture over the scene by the factor
   (Open questions).
6. **Pawn touch** (ProcessTouch overrides, decoded game 0x10ab87b0 for the flash-bang and 0x10ac1cb0 for the smoke
   grenade). With an `Owner` and `Other != Owner`: the grenade is marked `bAlreadyRebounded`; when `Other` is a
   Pawn, on the server `bBounce = false`, `PHYS_None` and `Explode` with a zero normal, at
   `Location - 100 * SafeNormal(Velocity)` for the flash-bang (so it goes off in front of the face it hit) and at
   `Location` for the smoke grenade; on a client the copy is destroyed. Any other actor goes to the stock grenade
   ProcessTouch (game 0x10ab90f0: the velocity mirrored on the hit normal and scaled by ActorReboundFactor, the
   grenade moved to the hit location), which the port does not have. The overrides do not read `QuickLaunch`: an
   aimed and a quick-launched grenade alike go off on the first pawn they touch, team mates included, the thrower
   excepted. Re-read 2026-10-26 (REA pseudocode of 0x10ac1cb0, disassembly of 0x10ab87b0, which Ghidra leaves
   undefined): both confirmed, the pull-back is `SafeNormal(Velocity)` (Velocity at 0x8c in this build) times 100.
7. **SFlashBang** (`PostNetBeginPlay`, on every machine it reaches, server included unless dedicated): spawns an
   SFlashLight at its place (a 0.15 s point light of radius 20 that lights only its own zone).

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Range | 2500 | uu | decoded | def:SBase.SProjFlashBang |
| Facing threshold | 0.0 (90 degrees) | cos | decoded | DES_fSeuilFlashBang_Cos default, Explode 0x10ab85e3 |
| Sight flags | 0x80086 | | decoded | Explode 0x10ab8610 |
| Explosion sound | volume 1, radius 100, pitch 1 | | decoded | Explode 0x10ab8684 |
| Touch pull-back | 100 | uu | decoded | ProcessTouch 0x10ab883b |
| After-image | 1 to 0 over 8 s, drawn above 0.0039 | | decoded | 0x10ab0990, 0x10a0b050 |
| Flash light | 0.15 s, radius 20 | | decoded | def:SBase.SFlashLight |

## Network
- Server: the explosion, the sight tests and the hit. The hit reaches the merc's machine as the reliable
  `ClientDamage` RPC, which runs the client-side test and the HUD effects there.
- The SFlashBang flare replicates (SimulatedProxy); each machine spawns its own SFlashLight from it.
- A client copy of the grenade never explodes: it is destroyed at the first hit (quick launch), at a pawn touch, or
  at the fuse.

## Edge cases
- A flash-bang that pops at the feet of a merc looking at it from the front still flashes him: the server tests
  his body's facing, his machine the camera's.
- The thrower's own touch is ignored; a spy team mate walking into a flying flash-bang sets it off (it then
  flashes the mercs that see it, not the spy).
- A merc behind a wall is not flashed even facing it: the line runs from the grenade to his location (not his eye).

## Evidence
- Decompiled and disassembled under local/re-out/c/flashbang/game/ (private Ghidra copy local/ghidra-work/flashbang):
  Explode 0x10ab83d0, PawnTakeDamage event call 0x10ab9090 (name 0x3c4 with the parameter block at
  ESP + 0x38: Damage 0, Instigator, Location, zero momentum, 8, 0, `PercentDamageRefillable` at 0x348), ProcessTouch
  0x10ab87b0 and 0x10ac1cb0, the stock grenade ProcessTouch 0x10ab90f0, the chaff burst sound 0x10abe6d0 (same
  CheckHearSound call), ClientDamageHudEffect 0x10ab0ae0 and its body 0x10ab0990, the post-render after-image
  0x10a0b050.
- Field offsets from the generated layouts: SPlayerController `SBPawn` 0x774, `SPawnType` 0x780,
  `DES_fSeuilFlashBang_Cos` 0x9f8; Pawn `WalkBob` 0x328, `EyeHeight` 0x3f8; Controller `bIsPlayer` bit 0 of 0x310;
  LevelInfo `ControllerList` 0x510; PlayerController `bCaptureRemanence` bit 0x800 of 0x5f0,
  `RemanenceBlendFactor` 0x524; SProjGrenade `bAlreadyRebounded` bit 1 of 0x350; Actor `bBounce` bit 26 of 0x2f0,
  `Role` 0x2de.
- Reloaded (`reloaded-hooks.md`): no native-table entry, hook or code patch touches Explode (0x10ab83d0), the two
  ProcessTouch overrides or the touch path; its ThrowGren (rva 0x23c80) spawns the grenade with the thrower as owner
  and instigator as the stock one does. So Reloaded keeps the pawn-touch pop and the flash (decoded, not observed).
- local/re-out/uc/SBase/SProjFlashBang.uc, SFlashBang.uc, SFlashLight.uc, SGadFlashBang.uc.

## Open questions
- The render device's after-image (D3D driver virtuals 0x80 capture and 0x84 draw): the blend (alpha by the factor,
  additive, or brightened) and whether the capture is the frame at full resolution. Needs the D3D driver in Ghidra
  or a sandbox capture of a flashed merc's frames.
- What the audio-subsystem virtual 0x70 in ClientDamageHudEffect plays (the whistle; SoundTouched GR_FlashBang_siffle
  is the likely sound, read nowhere in the decoded code).
- The stock grenade ProcessTouch for non-pawn actors (0x10ab90f0) is not ported: grenades pass through touchable
  non-blocking actors.
