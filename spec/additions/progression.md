# Levels, prestige and titles (an OpenSCCT addition)

A Call of Duty 4 style rank for signed-in players: XP from counted rounds and achievements, 55 levels, 10 prestiges
each with its own emblem, a master prestige emblem at the very end, and titles to show under the name. Nothing here
is a port: there is no `[PortOf]` and no provenance tag. The numbers are our own and live in
`OpenScct.Net.Cosmetics.ProgressionK`.

## Rules

1. **Who has a rank.** Only signed-in accounts on a master with levels (schema v5). Guests, bots, LAN and offline
   play have none and show nothing.
2. **Which rounds give XP.** The same rounds that count for achievements (docs/ONLINE.md, "Achievements"): reported
   by a trusted server, with a winner and a signed-in player on each side. The master awards the XP in
   `AccountStore.ApplyResults`, after the achievements, so a round's new achievements add their XP to the same award.
3. **A round's XP** (`Progression.ForRound`), per player row:
   - score: `ScoreXp` (10) per point of round score, the score clamped to 0..`MaxRoundScore` (100);
   - finish: `FinishXp` (100);
   - win: `WinXp` (150) when the row's team won;
   - objectives: `ObjectiveXp` (50) per objective the spy took (the `spy.objectives` feat), at most
     `MaxObjectivesCounted` (10), because objectives score 0 points in the retail table;
   - achievements: `AchievementXp` (250) for a total to reach, `RoundAchievementXp` (500) for a one-round feat,
     `MetaAchievementXp` (1000) for Gilded, Gilded Ghost, Mastercraft and Mastercraft Ghost (`Progression.XpOf`).
4. **Levels.** Level 1 to `MaxLevel` (55). Going from level L to L+1 costs `FirstLevelXp + LevelStepXp * (L - 1)`
   (200, 250, 300 ...), 82,350 XP for a whole prestige (`PlayerRank.CapXp`). XP is kept within a prestige and never
   above the cap; the lifetime total keeps counting.
5. **Prestige.** At level 55 and below prestige 10 the player may prestige from the Account page
   (`POST /api/v1/account/prestige`): prestige + 1, XP 0 (level 1). Level-based pistols and finishes relock until
   their required levels are reached again. Achievements, mastery rewards, admin cosmetic grants, stats and titles stay.
   At prestige 10 level 55 the account has the master prestige emblem; there is no prestige 11.
6. **Titles** (`TitleCatalog`, ids permanent, bits append only):
   - one per prestige 1 to 10 (Operative ... Legend), and Splinter Cell for the master prestige;
   - level milestones 10, 25, 40 and 55 (Recruit, Regular, Veteran, Elite), from the best level ever reached; a
     prestiged account has them all;
   - achievement titles (Golden Gun, Gilded Ghost, Untouchable, The Wall, Reaper, Lifer, Phantom, Warden, Deadeye,
     Ghost in the Machine, Mastercraft, Petard, High Voltage, Mastercraft Ghost).

   The player picks one (or none) on the Account page; it is kept in `settings.json` as `title` and follows the
   account through the profile sync.
7. **Into a match.** The master's join ticket carries the packed rank (level and prestige) and the unlocked titles as
   trailing fields after the cosmetic unlocks (`PlayerTicket.Rank`, `PlayerTicket.Titles`; older servers skip them).
   The listen host uses its own account's rank and titles (`HostAccount.Rank`, `HostAccount.Titles`). The client
   sends its chosen title as `?SvmTitle=`; the server keeps it only when the ticket unlocks it
   (`PlayerRanks.Checked`). The result is one int (`PlayerRank.Pack`: level in bits 0-7, prestige in 8-15, title
   bit + 1 in 16-31).
8. **Shown to everyone.** The server passes `?SvmRank=` to the map login, `SGameInfo.Login` sets
   `PlayerReplicationInfo.SvmRank`, and `PlayerLookNet` replicates it to every client. The lobby gets it as its own
   session line, `rank <id> <packed>`, after the player line (older clients ignore unknown lines). The hub does the
   same from its ticket.

## Where it shows

- Account page: emblem, level, title, XP bar, XP to the next level, the Prestige button (press twice), the row of
  prestige emblems (reached ones lit), the title picker (locked titles say how to get them).
- Main menu: the emblem, level and title under "Signed in as".
- Lobby: the emblem, level and title before each name.
- Mission status (F8): the emblem and level before each name. Connected players (F10): a Rank column with the level
  tag ("P3 42"), as the page is bitmap text.
- Kill feed: "(P3 42) Name" for ranked players.
- Hub: the emblem beside the name tag and the level and title under it.
- Results: the emblem beside each name; after the master has the round, "+640 XP" with its parts, the bar,
  "Level up!", new titles and "you can prestige".
- Admin portal: a Rank column in Players; on a player, the rank, lifetime XP, best level, titles, the last round's
  XP, and Set rank. CLI: `svm-master admin rank <name|discord id> <prestige> <level>`; `whois` shows the rank.

## Pictures

`game/Scripts/UI/RankIcons.cs` draws every picture from SVG in code (no built content needed):

- level insignia: a shape and colour per five levels (circle, square, diamond, shield, hexagon, octagon, stars of 5
  to 12 points), one to five chevrons inside for the level within the five;
- prestige emblems: a star with 4 + prestige points in that prestige's colour round Sam Fisher's trifocal goggles,
  pips under them for the prestige within its five, an inner ring from prestige 5;
- master emblem: a 16-point gold star, double ring, gold goggles and a star.
