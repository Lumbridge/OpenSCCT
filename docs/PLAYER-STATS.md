# Player stats

[Documentation](README.md)

Open **Stats** from the main menu or Account page. It shows career results, spy actions, mercenary combat,
gadgets, weapons, map wins and rank/XP progression. Sign in to load your account; **Refresh** retrieves the
latest totals. Only rounds accepted by the official-server progression rules count.

## Read the numbers

- Achievement thresholds do not cap career totals.
- Categories overlap: a headshot kill may also count as a weapon kill. Do not add categories together.
- Map wins combine variants of the same base map. Custom-map wins are not stored separately.
- An em dash means a value is unavailable or a ratio has no denominator.
- A failed refresh keeps the last loaded data and displays the error.

Use **Demos → Match history** to inspect individual rounds, including games without a replay or sign-in.
The [scoreboard guide](SCOREBOARD.md) explains the role-specific counters, live delay and permanent history.
Website profiles and privacy controls are
covered in the [player hub guide](PLAYER-HUB.md).

## Implementation and checks

The master returns career feat totals and base-map wins in `AccountInfo.Career`. Older masters omit it,
so detailed values remain unavailable while summary stats still work. Adding this field does not reset
tracking or require a database migration. Deploy compatible master and client versions to show details.

`StatsModelTests` covers ratios, legacy responses and tracked feat keys. `CareerStatsTests` checks stored
totals through the account API and JSON serialization. The `stats` Godot smoke checks category layouts at
1280 and 960 pixels; see [Testing](TESTING-PLAY.md) for running smokes.
