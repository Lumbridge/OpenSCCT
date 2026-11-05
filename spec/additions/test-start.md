# Test start

OpenSCCT addition for the map editor's Play From Camera. An offline play can name where its local player first
spawns, so a map maker tests the spot the editor's camera shows instead of walking there from a team start.

## The option

- The launch option `--start-at=X,Y,Z[,Yaw[,Pitch]]` goes with `--svm-play=<map>`. X, Y and Z are Unreal units; yaw
  and pitch are Unreal rotation units (65536 a turn) and default to 0. A value that does not read as three to five
  numbers is ignored like any unknown option.
- Only the offline play session reads it. Online hosts, dedicated servers and remote clients have no way to set a
  start, so it cannot be used to choose a spawn in a real match.

## The spawn

- The play session hands the start to the game rules (`SGameInfo.PendingTestStart`) for its local player after the
  login, before `PostLogin`, so it applies to the first spawn whether a Play Level login or the host's match start
  makes it. Bots spawn at the map's starts as usual.
- `RestartPlayer` still chooses a PlayerStart first. A player for whom no start is found stays unspawned, as without
  the option, and the chosen start stays the pawn's anchor and `LastStartSpot`.
- The pawn spawns at the point with the same floor probe as a start: a 200 uu box probe down, and 79 uu above the
  floor it finds. With no floor within 200 uu the pawn spawns at the point itself and falls. The map editor finds the
  floor itself first (an editor camera is usually far above it) and passes a point 96 uu above it.
- The pawn is upright with the start's yaw; the player's view gets the yaw and pitch.
- If the spot is blocked (the camera stood inside a wall or another pawn), the player spawns at the chosen start as
  usual.
- The start is used once. Later spawns (after death or a new round) choose a PlayerStart.
- The chosen start's `Event` is not triggered, since the player did not spawn there. Its sound, the teleport effect,
  the default inventory and the spawn shock of nearby enemies happen as for any spawn.

## Reporting

- When the client first sees each new pawn of the local player, the game prints
  `SVM_PLAY player <spy|merc> spawned at (X,Y,Z) yaw <Yaw>`. The editor's headless playtest reads it to report where
  the player stood.
