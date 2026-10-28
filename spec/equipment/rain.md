# Rain (ERainVolume, wet pawns, SFlicFlocEmitter)

Units:
- def:Engine.ERainVolume, nat:Engine.Actor.GetRainVolume, eng:ASBasePawn::Tick (the rain part)
- uc:SBase.SBasePawn:BeginWetSkin, uc:SBase.SBasePawn:PostBeginPlay
- def:SBase.SFlicFlocEmitter, uc:SBase.SFlicFlocEmitter:PreBeginPlay, uc:SBase.SFlicFlocEmitter:Tick,
  uc:SBase.SFlicFlocEmitter:Timer, def:Engine.FlicFlocEmitter
- uc:SBase.SLaserBeam:PreBeginPlay, uc:SBase.SMines:PreBeginPlay, uc:SBase.SPresDetector:PreBeginPlay,
  uc:SBase.SProjMarqueurSpy:PreBeginPlay
- Rules that read the wet state live elsewhere: the Shocker freeze (`spy-shocker.md`, `fRainFreezeFactor`), the
  cloak block (`spy-energy.md`), the spy's screen glass (`spec/native/reloaded-hooks.md`), the HUD droplets.

## Purpose
Twelve maps (Aqua*, ClarD, Orph*, Skys*, Stat*) place rain volumes. A pawn standing in one is wet: it cools in
thermal vision, the Shocker freezes it longer, the spy cannot cloak, rain splashes on the body, and gadgets and
beams that start in the rain are half as warm.

## Data
| Field | Default | Notes |
|---|---|---|
| ERainVolume.m_RainSize | (250, 250, 450) | the box half sizes (X, Y) and height (Z), times DrawScale x DrawScale3D |
| ERainVolume.m_RainFalloff | 0 (maps: 100) | how fast a pawn cools, and the splash rate |
| SBasePawn.CurrentRainVolume | None | the rain actor the pawn stands in, every machine works it out itself (not replicated) |
| SBasePawn.bWet | false | not replicated |
| SBasePawn.HeatTransitionTime | 10 s | config |
| SBasePawn.InRainHeatIntensity | 0.5 | config: the coolest a wet pawn gets |
| Actor.HeatIntensity | 1 for pawns | replicated from the server |

## Rules
1. **Which rain** (top of the pawn's native tick, every machine): a CurrentRainVolume that is an ERainVolume is
   dropped, then CurrentRainVolume is the first of the level's rain volumes (level order) whose box holds the
   pawn's Location. The box is (-X, -Y, 0)..(X, Y, Z) of m_RainSize x DrawScale x DrawScale3D from the volume's
   Location (its floor). A rain actor of another class set by script stays.
2. **Heat** (the server only): with T = HeatTransitionTime / (1 - InRainHeatIntensity) (20 s by default), outside
   rain HeatIntensity rises by dt / T, in an ERainVolume it falls by m_RainFalloff x dt / (100 T), in another rain
   actor by 2 dt / T; it stays between InRainHeatIntensity and 1. With the map's falloff of 100 a pawn cools from 1
   to 0.5 in 10 s and warms back in 10 s.
3. **Wet** (every machine, after the heat): in rain and not wet, the event BeginWetSkin, then bWet. Out of rain,
   wet and back at HeatIntensity exactly 1, the event RestoreSkins (bWet still set, so it puts the wet skin back),
   then bWet is cleared. BeginWetSkin puts the wet skins on: the spy's Skins 0 is HEAT_SN02_SI_wet where this
   machine's player is a spy and HEAT_sn02_wet otherwise, Skins 1 heat_sn02_2_wet_head; the merc's Skins 0 and 2
   DEF01_heat_bodu_sv. RestoreSkins chooses the wet skin first while bWet. In normal vision each of those modifiers
   draws its base material, a LitSpecularMaterial: the dry skin's texture as Diffuse, the shared wet env map
   (TexEnvMap over SPersoTextures.wet_envmap) as Specular and a SpecularityMask (ATT_spec_mask for the spy's body,
   ATT_2_wet for his head, DEF_01_A_Body_envmap for the merc). wet_lit_ATT_SI (the spy's body seen by a spy) also
   glows through Shadw_agent_SI and lit_def_fx through DEF_01_Body_SI; wet_lit_ATT (the spy seen by a merc) has no
   glow.
4. **Splashes** (SFlicFlocEmitter): SBasePawn.PostBeginPlay spawns one per pawn on every machine, owned by and based
   on the pawn. PreBeginPlay turns its three sub-emitters off, hides it and attaches it to the pawn's 'B Head' at
   (3, -8, 0). Its Tick acts when the pawn's CurrentRainVolume changes, for a spy always and for a merc only where
   he is not the local player's pawn: shown; into rain sub-emitter 0 spawns at m_RainFalloff / 90 a second
   (respawning, restarted) with a budget of m_RainFalloff / 10 particles (20 a second and 80 for another rain actor),
   sub-emitter 1 stops, and a 2.5 s looping timer starts; out of rain sub-emitter 1 takes sub-emitter 0's rate and
   budget (restarted) and drips for 10 s, sub-emitter 0 and the timer stop. Past the 10 s the drips stop. The timer
   triggers sub-emitter 2 (a puff) while the pawn lives.
5. **FlicFlocEmitter spawning**: a budget (VariableMaxParticles) caps the rate's spawns while it is not 0 and counts
   down; at 0 the rate is free. In rain mode (sub-emitter 0) each particle starts on the owner's body: one of 11
   segments, a random point between two bones of the skeleton each raised by a few uu, spread across the pawn's
   forward and right axes. Outside rain mode (sub-emitter 1) the particle takes the pawn's horizontal velocity.
6. **Half heat in rain**: GetRainVolume(Location) is the rain volume holding that point. An SLaserBeam, a mine
   (SMines, SPresDetector) and the spy's marker (SProjMarqueurSpy) that start in one have their HeatIntensity
   halved; a laser tripwire sets its beam's heat to 1, then halves it in rain.

## States
None.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Heat period | HeatTransitionTime / (1 - InRainHeatIntensity) | s | decoded | ASBasePawn::Tick |
| Falloff divisor | 100 | | decoded | ASBasePawn::Tick |
| Other rain actor cooling | 2 x dt / period | | decoded | ASBasePawn::Tick |
| Splash rate | m_RainFalloff / 90 | 1/s | decoded | SFlicFlocEmitter.Tick |
| Splash budget | m_RainFalloff / 10 | particles | decoded | SFlicFlocEmitter.Tick |
| Drip time | 10 | s | decoded | SFlicFlocEmitter.Tick |
| Puff period | 2.5 | s | decoded | SFlicFlocEmitter.Tick |
| Splash segments | 11 | | decoded | UFlicFlocEmitter::SpawnParticle table |
| Heat in rain | x 0.5 | | decoded | PreBeginPlay of the beam, mines and marker |

## Network
- Nothing here is replicated except HeatIntensity (server to clients). Every machine finds the pawn's rain volume
  from the pawn's replicated location and sets bWet itself; the clients' drying waits for the replicated heat to
  reach 1.
- The splash emitter is spawned on every machine (RemoteRole None) and lives in the views.
- The rain boxes are in the map's sim part (sim/rain.json), so a server without the render part knows them.

## Edge cases
- The own merc never sees splashes on himself; a spy does.
- A pawn shocked while wet gets the Stickess skin; RestoreSkins after the shock puts the wet skin back.
- The original leaves the splash emitter behind when the pawn goes; the rewrite removes it.
- In the original the wet skin stays after drying until the next RestoreSkins (the drying RestoreSkins still sees
  bWet); the rewrite's pawn view follows bWet, so the dry skin comes back when bWet clears.

## Evidence
- local/re-out/uc/SBase/SBasePawn.uc (BeginWetSkin, RestoreSkins, PostBeginPlay), SFlicFlocEmitter.uc,
  Engine/ERainVolume.uc, Engine/FlicFlocEmitter.uc, SLaserBeam.uc, SMines.uc, SPresDetector.uc, SProjMarqueurSpy.uc.
- ASBasePawn::Tick editor 0x11205fb0 (the rain list, the heat, the wet bit 0x8 of the bSpyBulletOn group, the events);
  UFlicFlocEmitter::SpawnParticles editor 0x110f05e0 and SpawnParticle 0x110f0750 (the segment table at 0x116159f0);
  the reflection dump for the ERainVolume field offsets (Actor is 0x2f8 bytes, m_RainFalloff at 0x304).
