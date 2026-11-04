# Match-size rules

OpenSCCT addition, after RE+'s `SMatchSizeRules`. A map can scale with the match: actors that exist only at some
player counts (an objective for big games, a shortcut door open only in small ones) and win targets per team size.
The data is the map's `sim/match.json` ([SVMap 1.2](../svmap/1.2/match.md)); the rules may sit on any actor.

## The player count

- The count is everyone in the match at round start: human players and bots, not spectators (a controller whose
  replication info is `bOnlySpectator` or `bIsSpectator` does not count).
- The server applies the rules at every round start (`SGameInfo.StartMatch`, before the game modes set up their
  missions), from the map's authored state each time, so players joining or leaving change the next round's
  layout, never the running one.
- The URL option `SvmMatchPlayers=<n>` (1 to 24) replaces the count; 0 or no option counts the players. An offline
  play passes it from the launch option `--players=<n>`, which the map editor's Play sets to its preview size.
- `GameReplicationInfo.MatchPlayers` (replicated) holds the count the round was laid out for.

## The layout

The prune is RE+'s, step for step, widened from 16 players and 8v8 to 24 players and 12v12. One implementation
(`OpenScct.Core.Match.MatchLayout`) serves the server, the editor's preview and its map check. For each top mission
(a mission no other mission lists), depth first up to 8 levels:

1. An objective or sub-mission whose range leaves out the count comes out of its mission's list and out of the
   round, with everything a removed mission still lists.
2. A sub-mission left with nothing comes out of its parent too.
3. Each mission asks for as many fewer completions as it lost (`MinimumObjectives` minus what the removed entries
   needed), never more than what is left can give, and never below 1 while something is left.
4. A win target for the match size (`TeamSize = clamp((count + 1) / 2, 1, 12)`) replaces that number, as far as the
   objectives left can reach.
5. `RandomObjectives` is clamped to what is left.

Top missions are never removed; a top mission with nothing left is a map error the editor's check reports.

## What happens to gated actors

- Objectives and missions outside their range are deactivated for the round and stay out: their `Reset` keeps them
  inactive.
- A mover is placed at its key before the round (outside its range key 0 unless the rule says otherwise; inside it
  works normally unless the rule names a key) and locked there: no trigger, alarm or `UnLock` moves it for the round.
- Any other actor outside its range (or `"hidden"` inside it) is hidden and loses its collision.

Every round start first puts back what the last one changed.

## Disk Hunt

A Disk Hunt mission with a win target for the match size plays that many disks (as far as it has disk triggers)
instead of the host's Active Disks setting.

## Network

The layout runs on the server only; clients see its result through the usual actor replication. The new replicated
`MatchPlayers` changes the network class layout, so builds with and without it do not join each other.
