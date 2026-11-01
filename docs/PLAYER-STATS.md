# Player stats

Open Stats from the main menu or Account page. Seven categories cover career
results and ratios, spy actions and objectives, mercenary combat, gadgets,
primary weapons and sidearms, map wins, and rank / XP / unlock progression.

The page reads the signed-in account and refreshes on entry or with Refresh.
Only rounds accepted by the existing official-server progression rules count.
The master exposes its existing raw feat totals and base-map wins in the additive
`AccountInfo.Career` field; no database migration or tracking reset is needed.
Achievement thresholds do not cap the displayed career totals. Categories overlap
(e.g. a headshot kill can also be a weapon kill), so they should not be summed.
Base-map wins combine variants. Custom-map wins are not separately persisted.

Old masters omit Career: detailed values show an em dash, while summary stats
remain available. Ratios with no denominator also show an em dash. A failed
refresh reports the error while retaining the last available account snapshot.

Validation: StatsModelTests cover ratios, legacy responses and coverage of all
tracked feat keys; CareerStatsTests checks persisted totals through the account
API and JSON serialization. `--svm-smoke=stats` checks category layouts at 1280
and 960 pixels. Deploy the updated master along with the client to enable details.
