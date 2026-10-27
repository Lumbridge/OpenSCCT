# SHelpPath (in-game help markers)

Units:
- uc:SBase.SHelpPath:PostBeginPlay
- uc:SBase.SHelpPath:Visible

## Purpose

Help arrows and "info" signs (static meshes such as `help_path_INFO`) along a map's routes, shown only with the
profile's in-game help on. 592 on 46 maps. Local to each machine (ROLE_None, no replication).

## Data

Map-set properties are all display: DrawType DT_StaticMesh and a StaticMesh (all 592), bUnlit (568),
bUseDynamicLights off (537), AmbientGlow 80 to 150 (208), EMF and heat intensities, DrawScale (23, up to 20),
DrawScale3D (7), StaticMeshInstance (14 on PolaT). Defaults: hidden, no collision or traces, bulletproof
(bBulletGoTru), no camera blocking.

## Rules

1. PostBeginPlay: bHidden = not `PlayerProfile.bDisplayHelpInGame` (no profile: hidden).
2. Visible(b): bHidden = not b. SPlayerController.ShowHelpPath(b) calls it on every SHelpPath; SPlayerProfile.
   SetDisplayHelpInGame calls ShowHelpPath when the option changes.

## Open questions

1. The render pack marks help_path meshes hidden (`HIDDEN_MESH_WORDS` in scctpkg level.py); the renderer does not
   yet show them when the sim actor is unhidden. The options toggle (ShowHelpPath) is not ported.

## Evidence

- `local/re-out/uc/SBase/SHelpPath.uc`, `SPlayerController.uc` (ShowHelpPath), `SPlayerProfile.uc`.
