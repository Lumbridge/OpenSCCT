# Strategic map: the StrategicMap pages, DisplayStrategicMap and the minimap positions

Units:
- uc:GUI.StrategicMap:InitComponent, uc:GUI.StrategicMap:Timer, uc:GUI.StrategicMap:ButtonClick,
  uc:GUI.StrategicMap:UpdateTeamMatesPositions, def:GUI.StrategicMap
- nat:GUI.StrategicMap.UpdateStrategicMapPosition, nat:GUI.StrategicMap.UpdateTeamMate,
  rl:GUI.StrategicMap.UpdateTeamMate
- uc:GUI.StrategicMap_InGame:InitComponent, uc:GUI.StrategicMap_InGame:InternalOnKeyEvent, def:GUI.StrategicMap_InGame
- uc:SBase.SPlayerController:DisplayStrategicMap
- uc:Engine.PlayerReplicationInfo:UpdateMinimapParams
- rl:fix.f7_crash, enh:fix.strategic_map

## Purpose

A top-down picture of the map, one per floor, with a blinking "you are here" marker and the player's team mates. It
opens from the In-Game Menu's Strategic Map button (`StrategicMap`) and from the DisplayStrategicMap key, F7 in both
stock layouts (`StrategicMap_InGame`). The legend beside the picture names the numbered areas drawn on it.

## Data

| Field | Owner | Notes |
|---|---|---|
| MapFloors | LevelInfo | one entry per floor, lowest first: FloorZ (height where the floor starts) and FloorMapTexture |
| SnapshotCamera | LevelInfo | the camera the pictures were taken with: Position, RotationRow0..2, FOV (degrees), TargetDistance |
| LegendMapInBrief | SMapSettings (`<Map>-i.utc`) | the legend text; "\|" is a line break |
| MinimapX, MinimapY, MinimapZ, MinimapYaw | PlayerReplicationInfo | a player's last reported position (whole units) and yaw; yaw -1 means none. Replicated to everyone |
| FloorIndex, DesiredFloorIndex | the page | the shown floor; -1 asks for the player's own floor |
| bFade | the page | the blink state, flipped every 0.5 s |

The floor pictures are textures of the map's texture packages (`Aquarium_TXT.Minimap.aqa_minimap_0`, 256 x 256) or of
the map itself. The markers are TexRotators of `Menu_PC.StrategicMapMenu`: `YouAreHereTexRotator` turns
`Menu_PC.YouAreHereTexPC` (a dot with a view cone pointing right) and `TeamMate` turns `Menu_PC.ButtonWhite` (a
small white dot). In the converted maps, the versus maps of the Aqua, Bank, Club, Deft, Fact, Miss, Muse, Orph,
Pola, Rive, Stat, Stee and Ware families (and the addon MoonD) have one to three floors and a camera looking straight
down (FOV 45); the TME and TSP tutorials, PyroD, SkysP and ZiopD/ZiopS have floors but no camera (all zero); the
other maps (Cine, Clar, Kraus, Moun, Pana, Sanc, Sche, Shad, SkysD, Slau, Stro, Term, Vert, Offs, ...) have none.

## Rules

Opening:

1. DisplayStrategicMap does nothing on a dedicated server's own console and on a tutorial map; otherwise it stops
   the player's controls (`StopAllControls`) and opens `StrategicMap_InGame`. The interaction states
   (`s_InteractWithObject` and the states under it) ignore the key.
2. The In-Game Menu's Strategic Map button opens `StrategicMap` over the menu (the tutorial layouts have no such
   button). Its Back returns to the menu.
3. `StrategicMap_InGame` closes every page (`CloseAll`) on a press of Escape, the right mouse button, the pad's back
   or cancel, or the key bound to DisplayStrategicMap.

The page (`InitComponent`, then a 0.5 s repeating timer):

4. The legend label shows the level's `LegendMapInBrief`.
5. Up and Down show only when the level has more than one floor.
6. A level with no floors hides the picture and the "you are here" marker and shows floor 0. Otherwise (not a
   dedicated server) the native places the marker with `DesiredFloorIndex` -1 (the player's own floor becomes the
   shown floor) and the picture shows that floor's texture.
7. Every timer tick flips `bFade`: bright draws the "you are here" marker in (255, 255, 255) and the mates in
   (254, 183, 75); dim draws them in (49, 63, 49) and (99, 60, 1). Then the marker is placed again with
   `DesiredFloorIndex` = the shown floor (the floor stays where the player put it), and the mates are placed again.
8. Up shows the next floor when there is one; Down the previous one. A change places the marker and the mates again
   and swaps the picture.
9. Placing the mates (`UpdateTeamMatesPositions`): first `UpdateMinimapParams` is sent to the server, then the GRI's
   player list is walked; a PRI counts when it has a team, the same team index as the player's, is not the player's
   own and its MinimapYaw is not -1. The first two such PRIs take the two mate slots in order (a slot whose mate is
   not shown stays hidden; a third mate is never drawn). The positions read are the ones the server sent before,
   since the request's answer arrives later.
10. `UpdateMinimapParams` (reliable, client to server) walks every player controller: one with a pawn and a PRI gets
    MinimapX/Y/Z from the pawn's location (truncated) and MinimapYaw from its yaw, plus one when the yaw did not
    change (so it is sent again), and 0 instead of -1. One without a pawn or PRI sets the *caller's* MinimapYaw to
    -1, not its own; its own fields keep their last values (a dead mate keeps showing where he died until a later
    request changes something).

Placing a marker (natives 1531 and 1868; Reloaded rewrote 1868 with the same projection, rule 21 of
spec/native/reloaded-hooks.md):

11. A position's floor is found by its height against the floors' FloorZ.
12. The position is projected with the snapshot camera: the stored rows hold the camera's axes in their columns
    (forward = the rows' X components, right = their Y, up = their Z). With `d` the position minus the camera
    position, `depth = d . forward`, `h = depth * tan(FOV / 2)`: across `u = 0.5 + 0.5 (d . right) / h`, down
    `v = 0.5 - 0.5 (d . up) / h`, both 0..1 over the picture.
13. The marker shows only when it is inside the picture (0..1 on both axes); the mate marker also only on the shown
    floor (Reloaded: it returns true only then). The marker turns to the yaw as seen through the camera's right and
    up axes (on the stock maps right is world +X, so the turn is the yaw itself).
14. Robustness (rl:fix.f7_crash, enh:fix.strategic_map): the stock native reads the pawn, the level and the floor list
    unchecked, and F7 or a team mate on a map without a strategic map crashed the game. Reloaded and Enhanced guard
    every one of them.

Layout (`def:GUI.StrategicMap`): title "Strategic map"; the picture at 100, 112, 256 x 256 units; the "you are here"
marker 20 units and a mate 32; Up at 0.1, 0.7 and Down at 0.1, 0.75 (0.09 x 0.075 of the screen); the legend at
0.6, 0.2 (0.4 x 0.475) in the small label style.

## Rewrite

- `game/Scripts/UI/StrategicMapModel.cs`: `StrategicMapData` (the floors, camera and legend read from the converted
  map; loading never throws) and `StrategicMapModel` (rules 5 to 13, Godot-free).
- `game/Scripts/UI/StrategicMapScreen.cs`: the page and the picture with its markers. Original look: the original's
  marker textures; modern look: a dot with a view cone and a ringed dot, in the decoded colours. A floor without a
  converted picture shows "No map picture"; a map without floors shows the page with that text and no buttons.
- `game/Scripts/Play/PlayMenu.cs`, `UI/InGameMenu.cs`, `UI/InGameMenuModel.cs`: rules 1 to 3 (`OpenStrategicMap`,
  `StrategicMapBack`, `StrategicMapKey`; the page from the menu returns to it on Back, Escape or the right mouse
  button).
- `src/OpenScct.Game/SBase/SPlayerController.StrategicMap.cs`: the state part of rule 1 and the request of rule 9
  (sent through the combat call `UpdateMinimapParams`); `src/OpenScct.Core/Engine/PlayerReplicationInfo.Minimap.cs`:
  rule 10.
- Every input is guarded (rule 14): no map folder, no floors, a floor without a picture, no camera or a zero one
  (the TME, TSP, PyroD, SkysP and Ziop maps: pictures without markers), a position behind the camera, no pawn, no PRI, no team, no GRI.
- The page is laid out with the original's arrangement (picture, legend on the right, Up above Down at the
  picture's lower left) in the front end's page frame rather than at the original's 640 x 480 positions, which put
  the floor buttons over the picture's corner.
- Content: the maps step writes each floor's picture to `ui/strategicmap/floor<n>.png` of the map folder; the floor
  heights and the camera stay in `sim/gameplay.json`. The ui step exports `Menu_PC.YouAreHereTexPC` and
  `Menu_PC.ButtonWhite`.

Additions:

- The Lobby Room has a Strategic Map page button: the selected map's pictures, floors and legend, without markers.
  The original's lobby had none.
- A floor caption ("Floor 1 / 3") above Up and Down.

## Evidence

- `local/re-out/uc/GUI/StrategicMap.uc`, `StrategicMap_InGame.uc`, `SSMenu_LANMainPage.uc` (Controls[1] opens
  'StrategicMap'), `Browser_Keys.uc` (Alias 38).
- `local/re-out/uc/SBase/SPlayerController.uc` DisplayStrategicMap and the `s_InteractWithObject` ignores list.
- `local/re-out/uc/Engine/PlayerReplicationInfo.uc` UpdateMinimapParams; `Engine/LevelInfo.uc` SnapCameraFloorStruct.
- The install's `Default_kbd_0.ini` and `Default_kbd_1.ini`: `IK_F7=DisplayStrategicMap`.
- `Menu_PC.utx` StrategicMapMenu: the two TexRotators (UOffset, VOffset 32) and their textures.
- The converted maps' `sim/gameplay.json` levelInfo: MapFloors and SnapshotCamera of every stock map.
- Rule 12's axes: the AquaD actors projected both ways onto `aqa_minimap_0`; with the columns as axes they fall
  inside the numbered rooms (the rows as axes turn the map a quarter and put them outside).

## Open questions

- Natives 1531 and 1868 of the stock game could not be read (the code at their addresses is packed in the file);
  rules 11 to 13 come from Reloaded's description of its 1868 and the data. Unknown: whether the floor test is
  "highest FloorZ at or below the height" (the rewrite's choice) or uses another threshold, whether `depth` is the
  true depth (the rewrite's choice) or TargetDistance (they differ by a few percent), and whether 1531 hides the
  "you are here" marker on another floor (the rewrite hides it).
- The pad's back and cancel keys of rule 3 are not mapped.
