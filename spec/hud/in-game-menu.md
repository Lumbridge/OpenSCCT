# In-match menu: ShowMenu, the In-Game Menu page and its confirmation pages

Units:
- uc:SBase.SPlayerController:ShowMenu, uc:SBase.SPlayerController:StopAllControls
- uc:GUI.SSMenu_LANMainPage:InitComponent, uc:GUI.SSMenu_LANMainPage:ButtonClick,
  uc:GUI.SSMenu_LANMainPage:InternalOnKeyEvent, uc:GUI.SSMenu_LANMainPage:InternalOnClose,
  uc:GUI.SSMenu_LANMainPage:s_Init@code, def:GUI.SSMenu_LANMainPage
- uc:GUI.Start_Menu:InitComponent, uc:GUI.Start_Menu:ButtonClick, uc:GUI.Start_Menu:InternalOnKeyEvent,
  uc:GUI.Start_Menu:s_Init@code, def:GUI.Start_Menu
- uc:GUI.SSMenu_ConfirmQuitLAN:InitComponent, uc:GUI.SSMenu_ConfirmQuitLAN:HandleParameters,
  uc:GUI.SSMenu_ConfirmQuitLAN:YesButtonClick, def:GUI.SSMenu_ConfirmQuitLAN
- uc:GUI.GUIYesNoQuitPage:InitComponent, uc:GUI.GUIYesNoQuitPage:QuitButtonClick, def:GUI.GUIYesNoQuitPage
- uc:GUI.GUIYesNoPage:NoButtonClick, def:GUI.GUIYesNoPage
- uc:GUI.BackToLobbyValidation:InitComponent, uc:GUI.BackToLobbyValidation:YesButtonClick,
  def:GUI.BackToLobbyValidation
- uc:GUI.QuitMatch_Page:HandleParameters, uc:GUI.QuitMatch_Page:Timer, def:GUI.QuitMatch_Page

## Purpose

The menu a player opens during a match with Escape: go back to the game, look at the strategic map, change the
options, send everyone back to the lobby (the host of a network game) or leave the session (or the game). These are
GUI pages (UnrealScript `GUIPage` classes of GUI.u), not Magma pages: the Magma packages of the install
(`Hud.mgm`, `Options.mgm`, `common.mgm`) hold no in-match menu.

## Data

| Field | Owner | Notes |
|---|---|---|
| bIAmServer | PlayerReplicationInfo | the player runs the server (set at login when the URL has `LAN=Server`) |
| NetType() | SPlayerController | NET_NONE (offline, Play Level), NET_LAN, NET_GAMESERVICE (Ubi.com), dedicated |
| CurLevelIsTutorial() | SPersistentData | the map is a tutorial map |
| bKeyevent | the page | true for the first 0.5 s after the page opens |
| bMatchHasBegun | GameReplicationInfo | |

## Rules

Opening (`ShowMenu`, the exec on IK_Escape in both stock layouts):

1. `ShowMenu` first calls `StopAllControls` (every button input byte of the controller goes to 0: movement, fire,
   crouch, use, gadget, zoom...), then opens one page: `Dedicated_Server` on a dedicated server's own console,
   `SSMenu_LANMainPage` when NetType is NET_LAN or NET_NONE, otherwise (Ubi.com) `Start_Menu`. Both pages are
   titled "In-Game Menu".
1a. ShowMenu is an exec, so the controller's state can change it (port: SPlayerController.eventShowMenu, asked by
    the play scene before it opens the menu): `s_InteractWithObject`, `s_MenuInteraction_GoToInteractionPosition`,
    `s_InteractWithObjectWaitingEvent` and `s_InteractWithObjectWaitingEventCrToSt` ignore it (no menu during an
    interaction); `s_MenuInteraction` opens it only while Use is up; the mine-on-wall states
    (`s_BasePutMineOnWall` and the four that extend it) first ChangeState `s_PutMineOnWall_UnGrab`; the merc scope
    (`s_Binoculars_Def`, `s_Binoculars_Def_Reloading`) first releases the held breath (bHoldBreath 0); the sticky cam
    menu first goes to the waiting state.
2. Nothing pauses the game: no page of the menu calls SetPause and `GUIPage.bPauseIfPossible` is never read by
   script. The match goes on behind the menu, offline as well.
3. While a page is open the GUI controller shows the Windows pointer (`SetControllerStatus`:
   `bShowWindowsMouse`) and takes the keys; closing the last page hides the pointer and gives the keys back.

`SSMenu_LANMainPage` (offline and LAN; Reloaded lists internet games as LAN games, so all play after Reloaded):

4. Buttons, in this order: Back, Strategic Map, Options, Return to Lobby, Quit Session. Each is centred on the
   screen at a fraction of its height (WinTop) and is half the screen wide.
5. With bIAmServer and NetType not NET_NONE (the host of a network game): Return to Lobby is shown. Not a
   tutorial: Back 0.275, Strategic Map 0.375, Options 0.475, Return to Lobby 0.575, Quit Session 0.675. A tutorial
   hides Strategic Map: Back 0.325, Options 0.425, Return to Lobby 0.525, Quit Session 0.625.
6. Otherwise (offline, or a network client): Return to Lobby is hidden. Not a tutorial: Back 0.325, Strategic Map
   0.425, Options 0.525, Quit Session 0.625. A tutorial hides Strategic Map: Back 0.325, Options 0.475, Quit
   Session 0.625.
7. Back closes every page (`CloseAll`). Strategic Map opens `StrategicMap`, Options opens `All_Settings`, Return to
   Lobby opens `BackToLobbyValidation`, Quit Session opens `SSMenu_ConfirmQuitLAN` with the parameter "Session".
8. Keys: for the first 0.5 s (state s_Init: Sleep(0.5), then bKeyevent false) every key is taken and ignored. After
   that a press of Escape, the right mouse button, the pad's Start or the pad's Cancel closes every page. The page
   takes every key either way (the key handler returns true), so no key reaches the game while it is open.

`Start_Menu` (Ubi.com only; not reachable after Reloaded):

9. Back, Options, Return to Lobby (server only), Quit Session. Server: 0.35, 0.45, 0.55, 0.65; otherwise Back 0.3,
   Options 0.5, Quit Session 0.7. Same keys as rule 8, but keys other than the close keys go on to the game.

`SSMenu_ConfirmQuitLAN` (Quit Session):

10. The question is "Leaving the game now will end this session.|Are you sure?" for the server and "Are you sure you
    wish|to quit ?" for a client (`|` is a line break). The dialog box covers 0.2 to 0.8 of the width and 0.3 to 0.7
    of the height. Buttons: End session (Yes, left, at 0.35 of the width) and Cancel (No, right, at 0.65) on the row
    at 0.55 of the height, a thin line at 0.623, and Quit Game (right, 0.65) on the row at 0.65.
11. Cancel closes the dialog (back to the In-Game Menu). Escape does the same (estimated: the GUI controller's own
    escape handling is native and not decoded).
12. End session: the server ends the session for everyone (`ForceMenuQuit`, everyone goes back to the menus; the
    `Xb_Info_WaitingPlayer` page instead while a travel cannot start). A client opens `QuitMatch_Page` with
    "Leaving session, please wait ..." ("Returning to lobby, please wait ..." when the parameter was "Lobby"),
    which fades the screen and after 0.6 s leaves the match for the menus (`QuitMatch`).
13. Quit Game: a LAN or Ubi.com server first sends its clients back to the entry menus (`ForceMenuClientEntry`);
    then the game quits (`QuitGame`).

`BackToLobbyValidation` (Return to Lobby):

14. Question "Are you sure you wish|to return to lobby?", buttons Return to lobby (Yes, left, 0.35) and Cancel (No,
    right, 0.65) on the row at 0.65 of the height; box as rule 10. Yes sends everyone back to the lobby
    (`ForceMenuLobby`) when a travel can start; Cancel closes the dialog.

Scoreboard keys (spec/hud/scoreboard.md): F10 (`ShowConnectedPlayers`) and F8 (`DisplaySessionStatus`) are layout
execs that work in play; the menu takes them while it is open (rule 8).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Key | IK_Escape = ShowMenu | | decoded | Default_kbd_0.ini, Default_kbd_1.ini |
| Key ignore after opening | 0.5 | s | decoded | SSMenu_LANMainPage s_Init, Start_Menu s_Init |
| Close keys | IK_Escape (27), IK_RightMouse (2), PadStart, PadCancel, on a press | | decoded | InternalOnKeyEvent |
| Button tops | rules 5, 6, 9 | fraction of the height | decoded | InitComponent |
| Button width | 0.5 | fraction of the width | decoded | the pages' defaults |
| Dialog box | left 0.2, top 0.3, width 0.6, height 0.4 | fractions | decoded | GUIYesNoPage defaults |
| Dialog rows | 0.55 and 0.65 (quit), 0.65 (lobby); columns 0.35 and 0.65, width 0.15, height 0.05 | fractions | decoded | GUIYesNoPage, GUIYesNoQuitPage defaults |
| Leaving delay | 0.6 | s | decoded | QuitMatch_Page.InitComponent SetTimer |

## Network

The menu is the player's own machine. Only its results reach the server: the server's end of session, the
return to the lobby, a client's disconnect.

## Edge cases

- Offline (NET_NONE) the player is the server but never sees Return to Lobby.
- A network client's End session leaves only itself; the server's ends the session for everyone.
- `SSMenu_ConfirmQuitLAN` also closes itself while a match is launching (its Timer, before bMatchHasBegun); in a
  running match that never fires.

## Evidence

- `local/re-out/uc/SBase/SPlayerController.uc` ShowMenu, StopAllControls.
- `local/re-out/uc/GUI/SSMenu_LANMainPage.uc`, `Start_Menu.uc`, `SSMenu_ConfirmQuitLAN.uc`, `GUIYesNoQuitPage.uc`,
  `GUIYesNoPage.uc`, `BackToLobbyValidation.uc`, `QuitMatch_Page.uc`, `GUIController.uc` (OpenMenu, CloseMenu,
  CloseAll, SetControllerStatus; no SetPause anywhere in GUI.u).
- The install's `Menus` folder: only `Hud.mgm`, `Options.mgm`, `common.mgm`.
- spec/reloaded/FEATURES.md rl:net.master (internet games in the LAN list).

## Open questions

- The strategic map page (`StrategicMap`: the level's MapFloors textures, the you-are-here dot and the team mates by
  the natives 1531 and 1868) is not decoded; the rewrite shows its button disabled.
- How the PC build draws these GUI pages (fonts, button styles s_PcButton) is not decoded; the rewrite draws them
  in the front end's style.
