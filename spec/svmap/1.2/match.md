# `sim/match.json` (SVMap 1.2, optional)

The map's player-count rules and win targets by team size. The server reads it at every round start
([match-size-rules.md](../../additions/match-size-rules.md) has the run-time rules); a map without the file plays
every actor at every count.

```json
{
  "format": "svm.match/1",
  "rules": [
    {"actor": "SObjective12", "players": [6, 0]},
    {"actor": "Mover40", "players": [0, 4], "outside": {"key": 0}, "inside": {"key": 1}},
    {"actor": "SAmmoStock3", "players": [8, 0], "outside": "hidden"}
  ],
  "win": [
    {"mission": "Match", "bySize": [1, 1, 2, 2, 3, 3, 3, 3, 0, 0, 0, 0]}
  ]
}
```

## `rules`

One entry per gated actor, in any order; an actor has at most one rule.

- `actor`: the id of an actor in `sim/gameplay.json` (any class). Ids match ignoring ASCII case.
- `players`: `[min, max]`, each `0` (no limit) or `1` to `24`; when both are set, min is not above max. The actor is
  in the match when the round's player count is inside the range.
- `outside` (optional): the actor's state when the count is outside the range: `"hidden"` or `{"key": n}` (a mover
  placed at key `n`, 0 to 23, and locked there). Never `"normal"`. Without it a mover goes to `{"key": 0}` and any
  other actor is `"hidden"`.
- `inside` (optional): the state inside the range: `"normal"` (the default), `"hidden"` or `{"key": n}`.

Key states are for movers only: the validator rejects them on other actors, and a reader that meets one anyway treats
it as `"hidden"`. For objectives and missions, outside their range always means left out of the round (they come out
of their missions' lists, see the run-time rules).

## `win`

Win targets by team size, one entry per mission at most.

- `mission`: the id of an `SMission` actor.
- `bySize`: 12 counts, `[0]` for 1v1 to `[11]` for 12v12; each `0` (keep the mission's own `MinimumObjectives`)
  or the completions the spies need, 1 to 255. A target never asks for more than the round can give.

## Validation

The validator checks the format id, ranges, key bounds, that every `actor` exists in `sim/gameplay.json` and every
`mission` is an `SMission` (its class or a subclass in `sim/classes.json`). The file holds no derived summary: what a
given count plays is computed by the one implementation (`OpenScct.Core.Match.MatchLayout`), which the server and the
editor share.

## Converted maps

RE+'s `SMatchSizeRules` (and its older names `SPlayerCountRules`, `SObjectivePlayers`), a class RE+ compiles into
the map package, become this file; their actors are not written to `sim/gameplay.json`:

- each `Subject` (or `Objective`) row with `MinPlayers`/`MaxPlayers` is a rule; a mover row is
  `"outside": {"key": 0}`, plus `"inside": {"key": 1}` when the row has `OpenInRange`;
- each `WinMission` row with `WinTarget[8]` (1v1 to 8v8) is a `win` entry, 9v9 to 12v12 padded with `0`.
