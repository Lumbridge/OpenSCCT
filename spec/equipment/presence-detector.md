# Presence detector (SPresDetector)

Units:
- def:SBase.SPresDetector, def:SBase.SPresDetectorInteraction, def:SBase.SGadMultiPresDetectorLauncher
- uc:SBase.SPresDetector:PreBeginPlay, uc:SBase.SPresDetector:AcceptStickableGPO, uc:SBase.SPresDetector:PostBeginPlay,
  uc:SBase.SPresDetector:TakeDamage, uc:SBase.SPresDetector:Destroyed, uc:SBase.SPresDetector:s_LockedByChaff.BeginState,
  uc:SBase.SPresDetector:s_LockedByChaff.EndState
- eng:ASPresDetector::Tick (game 0x10abd500) and its scan (vtable slot 0x138, game 0x10abd710), no UFunction ids
- uc:SBase.SPlayerController:AddGPOOnHud, uc:SBase.SPlayerController:RemoveGPOFromHud, nat:SBase.SHud.AddMalfunctionGPO,
  nat:SBase.SHud.RemoveMalfunctionGPO

## Purpose
The merc's "Pres. Det.", the second item of the spy trap launcher (SGadMultiPresDetectorLauncher, not in the default
kit). Placed on a wall like a mine, it watches 500 uu around it: a moving spy in sight puts it on everyone's HUD (the
mercs see a radar alarm at it), the mercs read "Intruder detected" and the spy reads that he is detected. The warning
holds 9 s after the last sighting.

## Data
`SPresDetector` (an SMines, trap kind `iMineType` 1): `fDetectionRadius` 500, `fWarningDuration` 9, `DetectionPeriod`
1 (config, no ini entry), `VitesseMin` 0 (config, no ini entry), `Snd_ScanPresDec` PresenceCapt_scan, `HitPoints` 1,
`iPriority` 5, `InteractionClass` SPresDetectorInteraction (the trap pick-up), mesh `SGameplayObjects.Objects.noise_emitter`,
collision 5 x 5. Runtime: `fLastScanTime`, `fDetectionTime`, `DetectedPawn[2]`, `bDetected`, `bChaffed`.
Placement, the trap limit (`NbTrapMineInMapIOwn`) and "remove oldest" are the launcher's (mine-launcher.md rules 6, 7, 9).

## Rules
1. **Start.** `PostBeginPlay`: `fSpawnedTime = now`, then the mine's start (placement sound, arming timer, the
   interaction). `PreBeginPlay` halves the heat in rain (not ported, as for every mine). Nothing sticks to it.
2. **Tick** (native, every machine, after the actor tick): `bDamageable = !bDetected`, so the detector cannot be
   shot while it warns. When it has an interaction: not warning and the interaction's `bPresenceDetected` still set
   once `fDetectionTime + fWarningDuration < now` clears it (`ChangePresenceDetected(false)`); warning, the flag clear,
   `fDetectionTime > 0` and `now <= fDetectionTime + fWarningDuration` sets it. The rest runs on the server only.
3. **Scan due**: when `DetectionPeriod` is 0 or `now > fLastScanTime + DetectionPeriod`, the scan runs (rule 6).
4. **No sighting** (scan not due, or it found nobody): with no spy remembered, or within `fWarningDuration` of the last
   sighting, nothing. Otherwise both `DetectedPawn` are cleared, `RemoveFromHud` and `bDetected = false`.
5. **Sighting**: each `DetectedPawn` slot that now holds a spy it did not hold before the scan sends that spy's
   controller `AddMessageOnHud(1)` (the "detected" text). If any slot changed: `AddOnHud` (every pawn's controller
   gets `AddGPOOnHud(detector location)`, priority 5 is enough) and `BroadcastTeamLocalizedMessage(1, SGameMessages,
   254)` ("Intruder detected" to the mercs). Then `fDetectionTime = now` and `bDetected = true`.
6. **Scan** (vtable 0x138): nothing while `bChaffed`. `fLastScanTime = now`; every player hears `Snd_ScanPresDec` at the
   detector (volume 1, radius 2 x `fDetectionRadius` = 1000). Then for each pawn of the level's list that is a spy
   (SPawnAttaque or a subclass): distance squared below `fDetectionRadius` squared, speed squared above `VitesseMin`
   squared (3D speed when going up, horizontal otherwise), and the shot line (TraceBone) from the detector to him hits
   him or nothing. Each such spy goes into the next `DetectedPawn` slot; at two the scan stops. True when one was found.
   Slots past the ones found keep their old spy.
7. **Chaff**: the gameplay object lock (10 s). `s_LockedByChaff` sets `bChaffed` on entry (and the malfunction
   message), clears it on exit, so no scan runs while locked; the warning still runs out.
8. **Damage**: the gameplay object's (1 hit point, refused while `bDamageable` is off).
9. **Destroyed** (server): both slots cleared, `RemoveFromHud`, then the mine's clean-up (interaction, trap count).
10. **HUD markers.** `SPlayerController.AddGPOOnHud(loc)` / `RemoveGPOFromHud(loc)` (server to owner, unreliable) call
    `SHud.AddMalfunctionGPO` (native 1448: adds the place at the front once) / `RemoveMalfunctionGPO` (1449: removes
    its first entry). The HUD draws `MalfunctionGamePlayObjects` as the spy's GPO markers and the merc's radar alarm
    blips. The same path serves the alarms' devices.

## States
`s_Activated` (auto, empty) and `s_LockedByChaff` (rule 7). The native tick does not look at the state.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Scan sound radius | 2 x fDetectionRadius | uu | decoded | 0x10abd710 |
| Detected text | 1 | AddMessageOnHud | decoded | 0x10abd500 (event 0xa87e1, the same as DropMineOnWall's message 3) |
| Merc message | team 1, SGameMessages 254 | | decoded | 0x10abd500 |

## Network
- The scan, the warning and the messages run on the server. The detector is a replicated gameplay object; its
  `bDetected` is not replicated, so on clients `bDamageable` stays on (damage is the server's).
- `bPresenceDetected` of the interaction replicates. Off the server the original's tick would call the client-to-server
  `ChangePresenceDetected` on an actor the client does not own; the port does not send it.
- `AddGPOOnHud` / `RemoveGPOFromHud` go to each remote player's machine (gadget server-to-owner calls).

## Evidence
- `local/re-out/uc/SBase/SPresDetector.uc`, `SGadMultiPresDetectorLauncher.uc`, `SBasicGamePlayObject.uc` (AddOnHud,
  RemoveFromHud), `SPlayerController.uc` 8049 to 8057, `SHud.uc` 218 to 220, `SGameMessages.uc` (254 Intruder).
- Game: vtable 0x10bfdab8 (InternalConstructor 0x10aedb40, class registered with size 0x394 under ASMines); tick
  0x10abd500, scan 0x10abd710, events 0x10abd9c0 (ChangePresenceDetected), 0x10abd9a0 / 0x10abd980 (AddOnHud /
  RemoveFromHud), 0x10aa2ed0 (AddMessageOnHud), 0x10aa2c70 (BroadcastTeamLocalizedMessage); SHud natives 0x10a91d60,
  0x10a91dd0. The editor's ASPresDetector::Tick (0x111cb050) is only the actor tick. Private decompile:
  `local/re-out/c/presbp`.
- Field offsets match the class layout: fWarningDuration +0x374, fDetectionTime +0x378, DetectedPawn +0x37c,
  bChaffed / bDetected bits 0 / 1 of +0x384, DetectionPeriod +0x388, fLastScanTime +0x38c, VitesseMin +0x390;
  bDamageable bit 1 of +0x340; the interaction's bPresenceDetected bit 1 of +0x358.

## Open questions
- The names of the event ids 0x5cb18 / 0x5cb19 were read as AddOnHud / RemoveFromHud from their use (a new sighting,
  the end of the warning, and the script's Destroyed). A capture of a merc HUD during a detection would confirm.
- What reads `bPresenceDetected` (no script does; probably the HUD's interaction icons).
- The scan sound goes through each player controller's sound hook in the original; the port plays it once at the
  detector.
