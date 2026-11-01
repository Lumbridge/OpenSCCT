# HUD counters and prompts: alarm box, objective counter variants, countdowns, coop icons, Speak, tutorial

Units:
- the shared page drivers of P_HudATT and P_HudDEF for ChronoAlarm, Objectif, ChronoObjectif(2) and TutoMessage, and
  the spy drivers for Coop and Speak (native, unnamed)
- uc:SBase.SHud:AddTutoMapMessage, uc:SBase.SHud:EmptyAlarmsList, uc:SBase.SHud:DrawRespawnWeapon, uc:SBase.SHud:HudUnit
- partial: uc:SBase.SHud:PostNetBeginPlay (Enhanced Reality level, distance unit, connected players list)
- uc:SBase.SPlayerController:SwitchEnhancedReality

## Purpose

The page areas that count something down or offer an action, beyond those docs/HUD.md already lists (timer,
countdown boxes, gauges).

## Data

| Field | Owner | Notes |
|---|---|---|
| AlarmsList | SHud | SAlarm entries (AddAlarm / RemoveAlarm natives) |
| MessagePriority, Duration, StartTime | SAlarm | StartTime on GRI.PreciseElapsedTime(), set by RestartCountdown |
| GameMode, TotalTeamLives[2], NumDisks, NumDisksToWin | GRI | counter variants |
| bReadyForShortLadder, bReadyForHoistClimb, bLyingOnGround | SBasePawn | coop and Speak |
| bCanTalkToGuy, bCanListenSpyBulletedGuy, fDistToTalkWhenOnGround | SPlayerController | Speak (300 uu by default) |
| sTutoMap | SHud | tutorial text |

## Rules

Alarm box (`ChronoAlarm` -> A_ChronoAlarme, both pages):

1. Among the SHud.AlarmsList entries with `MessagePriority > 4`, take the largest
   `Duration + StartTime - GRI.ElapsedTime` (bAlarmOn is not tested). While that value is above 0 the box shows
   `"0:%02u"` of it, opening from frame 0 when it appears; otherwise it hides.
   OpenSCCT timing correction: subtract `GRI.PreciseElapsedTime()` so the display uses the same clock as the
   alarm's deadline, and round a positive remainder up to whole seconds. All countdown boxes carry minutes
   (`90` seconds displays `1:30`); the original native rounding and format above remain the parity reference.
1a. `SHud.EmptyAlarmsList` (entering s_Dead, and every HUD frame while the player spectates): stops the
   high-priority alarm sound, empties AlarmsList and MalfunctionGamePlayObjects, and in GM_Neutralization (3) for a
   merc view pawn also empties ActorToDraw. The mode test reads `Level.Game`, so on a client it is skipped.

Objective counter (`Objectif`):

2. GM_DeathMatch (6): the Story counter is hidden and `DeathMatch` shows `"%u/%u"` of `GRI.TotalTeamLives[0]` and
   `[1]` (a value of 1000 or more reads "Inf."), restarting when their sum changes. GM_DiskHunt (4):
   `"%u/%u"` of `GRI.NumDisks - objectives left` and `GRI.NumDisksToWin`. Other modes: the objectives left (docs/HUD.md).

Objective countdowns (completing docs/HUD.md):

3. The ChronoObjectif driver returns whether any of its two boxes is in use; the merc page's alert box follows it
   (spec/hud/merc-reticle.md rule 13); spies ignore it.

Coop icons (`Coop`, spies):

4. All four hidden while the controller is in `s_Hack_Use` or `s_Computer_Use`, or while the own pawn is
   `bReadyForShortLadder` or `bReadyForHoistClimb` (being the porter). On tutorial maps icons 1 to 3 stay hidden.
5. A teammate ready as a short ladder close enough (height window, trace, placement test) shows Move_coop2 ("climb
   on him"); a teammate ready for a hoist shows Move_coop4 ("be boosted up"); otherwise Move_coop (offer a ladder:
   walking and a placement test passes) or Move_coop3 (hanging on a ledge where a hoist is possible). Each icon plays
   from frame 0 when it appears; the others hide.
6. Rule 5 is decoded in `../objectives/team-interactions.md` (HUD prompts): the driver is game 0x10b2db20, its
   mate search the co-op key's own (CheckIfCanInteractWithMate with its quick argument), its placement tests the
   bodies of CheckIfCanStartSHL and FindHoistClimbType. The rewrite draws all four icons from it.

Speak / Hack prompt (`Speak`, spies), completing docs/HUD.md:

7. Also shown, with "Speak", while an enemy pawn with Health above 0 lies on the ground (`bLyingOnGround`) closer
   than `fDistToTalkWhenOnGround`.

Tutorial message (`TutoMessage`):

8. `AddTutoMapMessage(text)` (standalone only) stores the text and plays a beep when it is new and at least 0.1 s
   after the last one. The area shows while the text is longer than four characters (and a level flag is clear),
   restarted on show; the game message queue waits while it shows (spec/hud/messages.md rule 9).

Respawn weapon (SHud canvas, `DrawRespawnWeapon`, both sides while the HUD shows):

9. While `CanPickRespawnWeapon()` (Merc Deathmatch, lives left, in `s_Dead`) and `RespawnWeapon` is set: the weapon
   class's 64 x 64 tile (IconU, IconV) of `Tex_All_Weapons` (SHudTextures `Icones_Weapons`) is drawn 48 units square
   8 units in from the right and bottom (above the key text), in the merc HUD colour (128, 93, 34), and
   `RespawnWeaponKey` (the Use key's name) centred under it. Pressing Use cycles the weapon (game flow spec).

Enhanced Reality switch (`SwitchEnhancedReality`, F9):

10. The HUD's Enhanced Reality level cycles 0, 1. A spy gets "Display off" or "Display all objective positions" on
    the message stack for 3 s, a merc "Display intrusions" or "Display all objectives and intrusions" on the state
    area. Dead (`s_Dead` ignores it) and spectating players are not switched. A new SHud starts at level 1.

Key tokens (the canvas text helper 0x10a02e40 and the Magma text draws):

11. A text that holds a backslash goes through the token pass (0x10a02900) before it is drawn. At each backslash
    the following characters are compared, case ignored, with the tokens in this order: MOVEF, MOVEB, MOVEL, MOVER,
    USE, PRIMARYFIRE, SECONDARYFIRE, JUMP, CROUCH, SPEEDMODE, HACKCOMM, SWITCHVISION, VISION1, VISION2, GADGET1 to
    GADGET4, NEXTGADGET, PREVIOUSGADGET, ACTION1, ACTION2, COOP, BTW, BINOCULARS, RELOAD, FIRERATE, LASER, CAMERA,
    ZOOMIN, ZOOMOUT, ER, HUD, INGAMEMENU. Each names a binding string (for example SECONDARYFIRE is
    `AltFire | GadgetQuickChoice 1 | HoldBreath`, the right mouse button of both stock layouts). The first key, in
    EInputKey order 0 to 254, whose whole binding equals that string (case ignored, 0x10a03060) gives its localized
    key name, which replaces the backslash and the token; the scan goes on one character past the name. A token whose
    binding no key has, or an unknown token, is drawn as it is. Table in game/Scripts/Hud/KeyTokens.cs.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Alarm box priority | above 4 | | decoded | Bridge docs/HUD.md (ChronoAlarme driver) |
| Respawn weapon icon | 48, 8 from the edges, tile 64 | units | decoded | SHud.DrawRespawnWeapon |
| Infinite team lives | 1000 | lives | decoded | Bridge docs/overlays/spyhud.md 7 |
| Talk to a downed enemy | 300 | uu | decoded | fDistToTalkWhenOnGround default |
| Coop search radius | (no longer used: the decoded mate search reaches 200 for the menu entries, fMaxDistToShortLadder 350 for the boost) | uu | decoded | ../objectives/team-interactions.md |

## Network

All read on the player's own machine from replicated state (GRI, PRIs, pawns) and client-side lists (SHud).

## Edge cases

- The alarm box ignores bAlarmOn, so a switched-off alarm still counts down to its end.
- A spy page's countdown boxes show without the alert box.

## Evidence

- SCCT Versus UE5 Bridge HUD research: docs/HUD.md (ChronoAlarm), docs/overlays/spyhud.md sections 2, 4, 5, 7, 9
  (drivers 0x10b32920, 0x10b2db20, 0x10b31310, 0x10b30f30, 0x10b31210).
- `local/re-out/uc/SBase/SHud.uc` AddTutoMapMessage, EmptyAlarmsList.
- Game: token pass 0x10a02900 (strings 0x10c48314..0x10c48904), key lookup 0x10a03060 (KEYNAME %i, KEYBINDING %s),
  text helper 0x10a02e40 (callers: UCanvas::execDrawTextJustified, the Magma text draws 0x10a54d10, 0x10a58b50,
  0x10a5aaf0, ASHud::execDrawObjectivesBinocularsAtt).

## Open questions

1. Resolved: the two coop placement natives (0x10ae6d40, 0x10ae5b50) are the bodies of CheckIfCanStartSHL and
   FindHoistClimbType; the driver is decoded in `../objectives/team-interactions.md`.
