# Goggle glow (SGoggleBeam): the lit goggle lenses drawn on spies and mercs

Units:
- uc:SBase.SBasePawn:ExecuteActionForGoggle, uc:SBase.SBasePawn:AnimEvent_GoggleSwitchDown,
  uc:SBase.SBasePawn:AnimEvent_GoggleSwitchUp (the beam parts), nat:SBase.SBasePawn.ExecuteAction (cases 66 to 72, 269)
- uc:SBase.SGoggleBeam:ChangeSkin, uc:SBase.SGoggleBeam:X, uc:SBase.SGoggleBeam:Y, uc:SBase.SGoggleBeam:Z,
  uc:SBase.SGoggleBeam:Shift, uc:SBase.SGoggleBeam:ShiftOnCLient, uc:SBase.SGoggleBeam_ATT:PostBeginPlay
- uc:SBase.SPawnAttaque:PostBeginPlay (beam spawn), uc:SBase.SPAWNDEFENSE:PostBeginPlay (beam spawn),
  uc:SBase.SBasePawn:Destroyed (beam destroy)
- def:SBase.SGoggleBeam, def:SBase.SGoggleBeam_ATT, def:SBase.SGoggleBeam_DEF

## Purpose
Every spy and merc wears goggles whose lenses glow: three green dots on a spy, a lens-shaped glow on a merc that is
orange in normal view, red in motion tracking and blue in EMF. The glow is a small skeletal mesh (an SGoggleBeam
actor) attached to the pawn's head, spawned separately on each machine and never replicated. Other players learn a
merc's mode only through the pawn action stream, so the colour follows the goggle actions on every machine. This
spec covers which pawns carry a glow, when it shows, its skins and how it is drawn. The vision mode logic itself is
in vision-goggles.md.

## Data
| Field | Default | Notes |
|---|---|---|
| SBasePawn.GoggleBeam | None | the pawn's beam on this machine |
| SGoggleBeam.CurrentMode | 0 | 0 none yet, 1 thermal, 2 night, 3 motion, 4 EMF, 5 normal (merc) |
| SGoggleBeam.offset | (0, 0, 0) | debug offset of X, Y, Z |
| SGoggleBeam.Mesh | SGoggle.ATT01_beams | SGoggleBeam_DEF: SGoggle.DEF01_beams |
| SGoggleBeam.bHidden | true | SGoggleBeam_DEF: false; the spy beam unhides itself in PostBeginPlay |
| SGoggleBeam.RemoteRole | ROLE_None | never replicated |
| SGoggleBeam.HeatOpacity, EMFOpacity | 0, 0 | not drawn in thermal or EMF vision |
| SGoggleBeam.bIgnoreEchelonLighting, bIgnoreDynamicEchelonLighting | true | not lit by the map |
| SGoggleBeam.bAcceptsProjectors | false | |

Meshes (SGoggle.ukx): ATT01_beams, one bone, 120 wedges, 110 faces, its own material SGoggleTextures.TRA_goggle_green;
DEF01_beams, one bone, 86 wedges, 78 faces, its own material SGoggleTextures.TRD_goggle_orange. Both lie a few units in
front of the head bone's origin.

Skins (SGoggleTextures.utx, group beam_goggle), every one two-sided, OutputBlending 3 (translucent) and
self-illuminated by its own diffuse:
| Skin | Texture | Opacity | Look |
|---|---|---|---|
| TRA_goggle_green | A_goggle_green (64 x 32, alpha) | A_goggle_green | yellow-green dots through the alpha |
| TRD_goggle_orange | D_goggle_red_orange (64 x 64) | none | orange lens shape, screen-added |
| TRD_goggle_motion | D_goggle_Motion (64 x 64) | none | red lens shape, screen-added |
| TRD_goggle_EMF | D_goggle_EMF (64 x 64) | none | blue lens shape, screen-added |

## Rules
1. **Spawn.** `SPawnAttaque.PostBeginPlay` spawns an `SGoggleBeam_ATT` (owner the pawn) when the pawn's `RemoteRole`
   is not `ROLE_AutonomousProxy` at that moment (before possession that is every machine, the server included),
   then attaches it to bone `B Head` with relative rotation (0, 1500, 0). `SPAWNDEFENSE.PostBeginPlay` spawns an
   `SGoggleBeam_DEF` the same way, with relative rotation (0, 1250, 0), when the pawn's `Role` is below
   `ROLE_Authority` or the machine is standalone or a listen server (no merc beam on a dedicated server). Both spawn
   before the parent PostBeginPlay. A spawn that destroys itself leaves `GoggleBeam` None.
2. **Spies are hidden from mercs.** `SGoggleBeam_ATT.PostBeginPlay` destroys the beam when the machine's local player
   controller is a merc and its owner is a spy, then sets `bHidden = false` (spy beams show from their spawn). A
   machine with no local player controller reads its type as `SPAWNOFFENSE` (a spy), so the beam stays. Merc beams
   start visible (class default).
3. **Mode from the actions** (`ExecuteActionForGoggle`, raised by `ExecuteAction` for 66 to 70 and 269 only, and
   for 72 before its clip): 68 thermal sets mode 1, 67 night 2, 69 motion 3, 66 EMF 4; 72 (raise) sets 5 for a merc
   and 0 for a spy; 269 (normal view without the clip) sets the same and calls `ChangeSkin` at once; 70 (switch
   between two modes without the clip) calls `ChangeSkin` with the mode the action before it set. 71 (lower) does
   not touch the beam. Nothing happens without a beam.
4. **Skin at the clip's notify.** `AnimEvent_GoggleSwitchDown` (only while `bGogglesUp`) and
   `AnimEvent_GoggleSwitchUp` (only while not) also run where the goggle clip plays without goggles (other players'
   pawns, bots on the server): they set `LastZipP` far away, turn the spy's `BGoggles` bone (Down only; the Versus
   meshes have no such bone, so it has no effect), tell the controller's goggles `Down` or `Up` when it has any, then
   call the beam's `ChangeSkin`. So a lowered or raised merc glow changes colour at the frame where the goggles reach
   or leave the eyes, about 0.27 s after the key.
5. **ChangeSkin.** Returns at once when the local player is a merc and the owner a spy. Otherwise unhides the beam and
   by mode: 0 puts orange on a merc owner (a spy keeps its skin); 1 and 2 green; 3 motion; 4 EMF; 5 orange; any
   other mode leaves the skin. So a spy's glow is always green, and a merc's is orange, red in motion tracking and
   blue in EMF.
6. **Destroy.** `SBasePawn.Destroyed` clears the beam's owner and destroys it.
7. **Debug offsets.** `X`, `Y`, `Z` add their argument to the offset's pitch, roll and yaw and call `Shift`, which logs
   the offset, calls `ShiftOnCLient` and sets the relative rotation; `ShiftOnCLient` sets the relative rotation. Never
   called by the game.
8. **Drawing.** The beam is drawn at its bone with the relative rotation, with the current skin over its single
   material slot (its own material when none). The green skin is alpha blended through its opacity texture; the
   orange, motion and EMF skins are screen-added (black adds nothing). All are fullbright and ignore the map's
   lights. HeatOpacity and EMFOpacity 0 leave it out of thermal and EMF vision; night vision and motion tracking draw
   it. There is no corona, dynamic light or projector, and no distance fade beyond the mesh's own size on screen.
9. **Who sees what.** Spies see every spy's green glow (their own in third person) and every merc's glow. Mercs see
   every merc's glow and never a spy's. The local merc's own pawn is not drawn in first person, so neither is its
   glow.

## Network
Local to every machine: the beam is spawned per machine (rule 1), never replicated, and its mode follows the pawn
actions each machine executes (the owner's machine when the action starts, the server and the other clients when
the action stream arrives).

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Spy beam relative rotation | (0, 1500, 0) | rotation units | decoded | SPawnAttaque.PostBeginPlay |
| Merc beam relative rotation | (0, 1250, 0) | rotation units | decoded | SPAWNDEFENSE.PostBeginPlay |
| Beam bone | B Head | | decoded | both PostBeginPlay |
| Mode codes | 0 to 5 | | decoded | ExecuteActionForGoggle, ChangeSkin |
| Spy BGoggles rotation on Down | (0, 0, -5400) | rotation units | decoded | AnimEvent_GoggleSwitchDown |

## Edge cases
- A spy's own machine spawns its own beam (the pawn is not yet possessed when PostBeginPlay runs), so the spy sees
  its own glow in third person.
- A merc whose goggles are lowered while another mode is active (switch without the clip) changes colour at once
  through action 70.
- Raising a spy's goggles sets mode 0 and changes nothing visible.

## Evidence
- local/re-out/uc/SBase/SGoggleBeam.uc, SGoggleBeam_ATT.uc, SGoggleBeam_DEF.uc; SBasePawn.uc ExecuteActionForGoggle
  (2762-2825), AnimEvent_GoggleSwitchDown/Up (3060-3125), Destroyed (3660-3664); SPawnAttaque.uc 109-118;
  SPAWNDEFENSE.uc 183-191.
- execute-action.md cases 66 to 72 and 269 (which actions raise ExecuteActionForGoggle).
- Packages/Animations/SGoggle.ukx and Packages/Textures/SGoggleTextures.utx read with scctpkg (meshes, shader
  properties, textures).
- Real game captures in the sandbox (local/goggle-glow/game/), see docs/EFFECTS.md#goggle-glow.

## Open questions
- The exact D3D blend of a Shader with OutputBlending 3 and an Opacity texture (the green skin) is taken from the
  material resolver's rule (alpha blend through the opacity), not decoded from the render interface.
