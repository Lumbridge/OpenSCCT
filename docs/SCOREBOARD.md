# Scoreboards and match history

[Documentation](README.md)

Press **F10** to toggle the scoreboard. **Hold scoreboard** is a separate, initially unbound action under
Options → Controls, available for keyboard, mouse and controller. Tab keeps its vision-mode binding. The
scoreboard starts closed each round and leaves movement, aiming and firing active. Menus, chat and focus loss
close it. The former compact HUD roster and its layout/settings have been replaced.

Both teams appear in separate tables, with your team first and your row highlighted. Names, rank, ping, lives,
kills/deaths and completed objectives are public. New combat counters refresh **every 30 seconds**, using samples
with a **server-enforced minimum 30-second delay**. The Next update countdown follows the server's refresh
deadline; counters display zero until an eligible sample is released. Public rows continue updating each second.
This limits immediate confirmation but can still reveal past hidden interactions. Full totals
are released when the server ends the round. Pure spectators receive the same delayed feed.

| Side | Live combat columns | Additional final breakdown |
| --- | --- | --- |
| Spy | Merc neutralisations, camera sleeps, shocker stuns, successful flashes | Head jumps and knockout/kill outcomes, grab KOs, neck snaps, choke kills, smoke sleeps, headshot stuns, objective capture time, chaff jams, tracking-bullet hits, mine and environmental kills |
| Mercenary | Damage dealt, mine kills, grenade kills, headshot kills | Weapon/scoped/hip-fire kills, accuracy, finishers, taser/charge/berserk hits, marks, trap catches, bombs secured, disks returned, backpack resupplies, environmental kills |

Neutralisations count enemy kills plus nonlethal camera, smoke, grab and head-jump knockouts. A knockout followed
by a kill counts twice; a lethal head jump counts once. Stuns and flashes are separate. Headshot stuns are a
subset of shocker stuns. Flashes count affected enemies, not grenades thrown. Damage counts enemy health
removed, excluding friendly damage and overkill. Categories overlap; do not add every breakdown column together.
An em dash means unavailable, rather than a verified zero.

## After a game

The results screen and lobby **Last game → View scoreboard** use the same detailed view as **Demos → Match
history**. Select a player for their full breakdown. Returning to a lobby or joining it later preserves access
to its last completed round without interrupting the lobby.

History combines local rounds and the signed-in account's reported rounds. Scoreboards are independent of demo
recording: offline, private/LAN and guest games are saved locally in `user://scoreboards`, with atomic writes.
Practice and unfinished sessions are labelled. An early-departure record contains only the delayed values the
client received; after a trusted server reports the completed round, account history can replace that partial
record while retaining the departure label. Private/offline/practice records are never uploaded as trusted
results. Reconnects retain one participant identity, and final boards retain departed players.

Local scoreboards and reported account scoreboards are retained permanently. Replay files still follow their
existing expiry rules, and detailed event timelines normally expire after 90 days. Deleting a replay does not
delete its scoreboard. A missing or incompatible replay disables playback without hiding the statistics.
Version 3 demos carry their round identity and final scoreboard, so copied recordings can display details
without replaying the match. Older recordings keep their existing playback support; missing statistics are
labelled unavailable. Already-pruned server history cannot be recovered.

## Implementation and rollout

`ServerHost.Scoreboards` owns the round ledger, immutable one-second samples and disclosure queue, including
offline listen hosts. `ClientSession` receives bounded, compressed control-message chunks and publishes only
complete snapshots. Historical lobby snapshots have a separate channel from current-match snapshots.
No current hidden combat counters are sent for a client to hide locally. The UI/archive consumes only client-owned
snapshots; it never reads a live server world from the render thread.

Round UUIDs link results, demos and local/account history. Account access remains limited to participants.
Master schema 11 keeps scoreboards while pruning auxiliary records in advancing bounded batches. Report
acceptance, storage and progression awards are one idempotent transaction. Departed participants gain history
access without changing existing career eligibility. Upgrade the master before enabling updated server report
producers. New clients fall back to public scores on servers without detailed-scoreboard support.

Focused checks cover stat attribution, disclosure boundaries, multiple clients/reconnects, atomic transfer,
archive replacement/corruption, idempotent awards, retention and legacy demo formats. The `scoreboard` and
`match-library` Godot smokes exercise input, 24-player layouts and shared details from 800×600 through 4K.
