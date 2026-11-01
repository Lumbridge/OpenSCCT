# Tutorials, the profile's tutorial flags and Visit a Map

Units:
- uc:SBase.SPersistentData:InitTutorials
- uc:SBase.SPersistentData:CurLevelIsTutorial
- uc:SBase.SPersistentData:MarkTutoAsDone
- nat:SBase.SPlayerProfile.SetMapTutorialDone
- nat:SBase.SPlayerProfile.GetMapTutorialDone
- nat:SBase.SPlayerProfile.isMapAlreadyPlayed
- nat:SBase.SPlayerProfile.setMapAlreadyPlayed
- uc:GUI.Tutorials_Page:ButtonClick
- uc:GUI.TutoOrExam:ButtonClick
- uc:GUI.Tuto_MapsList:RefreshPage
- uc:GUI.Tuto_MapsList:LoadMap
- uc:GUI.Tuto_MapsList:ButtonClick
- uc:GUI.Tutorials_List:RefreshList
- uc:GUI.Tutorials_List:ListClick
- uc:GUI.Tutorials_List:SwitchTeam
- uc:GUI.Tutorials_Tuto:RefreshAll
- uc:GUI.Tutorials_Tuto:NextTutorial
- uc:GUI.Tuto_Gadgets:RefreshList
- uc:GUI.Tuto_Gadgets:ListClick
- uc:GUI.Tuto_Gadgets:SwitchTeam
- uc:GUI.Tuto_OneGadget:RefreshAll
- uc:GUI.Tuto_OneGadget:NextTutorial
- uc:GUI.View_Hud_Spy:InternalOnKeyEvent
- uc:GUI.View_Hud_Sentry:InternalOnKeyEvent
- uc:GUI.View_KeyboardConfigOne:InternalOnKeyEvent
- uc:GUI.View_KeyboardConfigTwo:InitComponent
- uc:GUI.Visit_Map_Set:InternalOnKeyEvent
- uc:GUI.FirstTimeToPlayThisMap:OkButtonClick
- uc:GUI.Lobby_Create:ButtonLaunch (the first-time check only)

Also described here, ported under their own specs: the offline tutorial branch of
`SPlayerController` `GameEnded.PlayerTick` (spec/game/end-of-round.md) and the offline launch of `GUI.loading.Timer`
(spec/game/match-options.md).

## Purpose

The original's training front end: eight tutorial maps played alone, a profile that remembers which are done and
whether the spy exam was passed, pages of tips and gadget details, pictures of the two HUDs and the keyboard, and
Visit a Map, which walks any map alone with the map's help arrows shown. A lobby prompt suggests Visit a Map the first
time the player launches a map.

## Data

| Field | Where | Meaning |
|---|---|---|
| `TutoMapsA[5]`, `TutoMapsASize` | SPersistentData | spy tutorials `TSP20`, `TSP21`, `TSP22`, `TSP23`, then the exam `TSP29` |
| `TutoMapsD[4]`, `TutoMapsDSize` | SPersistentData | merc tutorials `TME30`, `TME32`, `TME33` (size 3) |
| `bTutorialAdvancement_ATT_0_8`, `_DEF_0_8` | SPlayerProfile, config byte | the done flags of each side, read and set through natives 1826 / 1825 |
| `bExamDone` | SPlayerProfile, config | the spy exam was passed |
| `bExamJustCompleted` | SPlayerProfile | set the first time the exam is passed (not saved) |
| `bDisplayHelpInGame` | SPlayerProfile, not saved | the in-game help: help paths and help-only zone hints |
| maps already played | SPlayerProfile, natives 1843 / 1844 | the maps the first-time prompt was confirmed for |

## Rules

1. `InitTutorials` fills the two lists above. `CurLevelIsTutorial` compares the persistent `LevelName` with the spy
   list, then the merc list, and gives the map's index and team (0 spy, 1 merc). The gadget, co-op and HUD rules that
   ask "is this a tutorial level" use it.
2. `MarkTutoAsDone(index, team)`: the last spy map (the exam) also sets `bExamDone` and `bExamJustCompleted` the
   first time; then the map's done flag is set.
3. End of an offline round (the player's `GameEnded` tick, 10 s after the winner is known): on a tutorial map, a win
   for the player's side marks the map done. Then a spy goes to the quit page when the exam is done, else to the
   loading page with "TutoSpy" and no map; a merc goes to the quit page when the merc flags byte is 14 or more (the
   three merc maps done), else to the loading page with "TutoSentry". Any other offline map goes back with
   "VisitMapReturn" (the entry map, whose start menu then opens the Tutorials page offline).
4. Loading page, offline: no time limit, unlimited lives on both sides, 5 s respawn, Story mode. "TutoSpy" /
   "TutoSentry" with a map name plays that map; without one it plays the side's first map not done (the current map
   when all are done). The player joins as team 0 for "TutoSpy", team 1 for "TutoSentry", else as the profile's team.
5. Tutorials page: View HUD, Tips, Gadget Details, Spy Tutorial Maps (team 0), Mercenary Tutorial Maps (team 1),
   Visit a Map.
6. Tutorial maps page: one tick and one caption per map. The spy page shows five rows (the fifth, Exam, ticks with
   `bExamDone`); the merc page hides rows 2 and 5 and shows the merc maps on rows 1, 3 and 4. Choosing a row plays
   that map (rule 4, the map named). The original also launches when a tick box is changed, after a 0.2 s guard.
7. TutoOrExam (opened by the online button while the exam is not passed): Tutorials plays the next spy map (rule 4,
   no map named); Exam map plays `TSP29`.
8. Tips: a list of nine titles per side; Switch Team flips the side; choosing one opens the tip page (picture
   `Tutorials_PC` `SPY_TutoNN` or `SENTRY_TutoNN`, legend, explanation with key tokens). Next shows the following
   tip and wraps to the first.
9. Gadget Details: the side's eight gadgets in the team's `ClassInventory` order; the gadget page shows the 64 x 64
   tile of the HUD's gadget atlas at the class's `IconU` / `IconV`, the explanation and the legend (`|` separates
   its lines), titled with the gadget's name; Next wraps.
10. View HUD: the spy HUD picture with eleven labels, A switches to the merc HUD picture (fourteen labels) and back.
    The keyboard pictures (Configuration 1 and 2, the same picture with the Use label moved to the page's middle on
    2) are built the same way; no menu of the PC build opens them.
11. Visit a Map: a map list (AquaE first selected) and the side's picture; Switch Team flips the profile's team;
    Launch turns the in-game help on and plays the persistent `LevelName` offline (rule 4, the profile's team).
12. The main multiplayer menu turns the in-game help off each time it opens, so the help is on only for a visit.
13. Lobby launch or ready: when the map is not in the profile's played list, the first-time box opens instead and
    the press is remembered; OK adds the map to the list, writes the profile and closes the box, and the remembered
    press then runs.

## The rewrite

- The flags live in `user://tutorials.json` next to the settings (`TutorialProfile`); the done flags are bit i of a
  side's value for map i, and the merc test of rule 3 is "all three done".
- Offline play is the listen-server path of Create a Session's offline play, alone (no bots), with the rules of rule
  4. The quit page of rule 3 is the Tutorials page; leaving through the In-Game Menu returns to the main menu.
- The in-game help is a profile option of the level (`IProfileOptions.DisplayHelpInGame`), set by a Visit a Map
  launch and cleared when the visit's play scene closes or the Tutorials page opens.
- TutoOrExam is not shown (the rewrite does not gate online play on the exam); the exam is the spy page's fifth row.
- Lobby Launch and Ready proceed immediately without the first-time map prompt from rule 13. Existing played-map
  history is retained in the profile for compatibility.
- The keyboard pictures open from a Keyboard button on the View HUD page.
- Texts come from the converted GUI.int; pictures from `Tutorials_PC` (ui content step).

## Open questions

1. Rule 3's byte test (`>= 14`) suggests the merc flags are bits 1 to 3; natives 1825 / 1826 are not decoded, so
   the bit layout of the saved byte is not known. The rewrite's own file does not need it.
2. Zone hints (`SZoneTips`) show only in standalone play; the rewrite's offline play is a listen server, so the
   tutorial maps' hints do not show yet (spec/devices/zone-tips.md rule 5).

## Evidence

- `local/re-out/uc/SBase/SPersistentData.uc`, `SPlayerProfile.uc`, `SPlayerController.uc` (GameEnded),
  `LancementMenu.uc`, `SHelpPath.uc`, `SZoneTips.uc`.
- `local/re-out/uc/GUI/` pages: `Tutorials_Page`, `TutoOrExam`, `Tuto_MapsList`, `Tutorials_List`,
  `Tutorials_Tuto`, `Tuto_View`, `Tuto_Gadgets`, `Tuto_OneGadget`, `View_Hud_Spy`, `View_Hud_Sentry`,
  `View_KeyboardConfigOne`, `View_KeyboardConfigTwo`, `Visit_Map_Set`, `FirstTimeToPlayThisMap`, `Lobby_Create`,
  `Menu_Multi`, `loading`.
