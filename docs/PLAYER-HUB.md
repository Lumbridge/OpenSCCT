# Player hub

The master hosts the public OpenSCCT website at `/` and the separate administration portal at `/admin`.
Both ship with `svm-master`; no JavaScript build service, separate web host, or browser-held game invite key is needed.
The public and admin pages share the launcher's dark green palette and off-white **Open** / bright green **SCCT** wordmark.

## For players

- **Download:** install the launcher from the linked public release, let it install the game and required game files,
  then use Join on the website's Servers page or Find a Session in the game.
- **Servers:** browse listed games and hubs, their maps, modes, regions, activity and available spaces. Unlisted
  servers and private connection/administrative details are not published by the website.
  **Join** opens the installed app through `openscct://join/<server-id>`. On Windows, run launcher 0.8.26 or later
  once to register this link handler; game 0.8.58 or later receives the request. Browser approval may be required.
  On Linux and Steam Deck, the 0.8.58+ Flatpak registers the same handler through its desktop entry.
  The link contains only the public server ID. The game uses its own saved sign-in and asks for a server password
  when needed. Browser sign-in alone does not sign in the game. An active game is not interrupted; the request
  waits for the main menu. If no app opens, the page links to installation/update instructions.
- **Players:** find a named player, see their rank, lifetime stats, achievements, unlocks and recorded rounds.
- **Leaderboards:** compare lifetime XP, wins or kills. Private, unnamed and actively banned accounts are excluded.
  These are lifetime totals from counted rounds, not seasonal rankings or an estimate of missing historical data.
- **Matches:** browse recorded rounds and their team scoreboards. Uncounted rounds are labelled.
- **Changelog:** read recent published updates at `/changelog`, also linked from the main navigation, footer and
  download page. Release notes come from the public GitHub releases, refresh at most every ten minutes, and keep
  the last successful results if GitHub is temporarily unavailable. Drafts, prereleases and content storage releases
  are excluded; the full release archive remains linked on GitHub.
- **My account:** sign in with the same Discord identity used in the game. Claim/change your player name, manage
  website privacy and sign-ins, prestige when eligible, and select your unlocked cosmetics and title.
  Use **Sign out** in the header on any page to end this browser's player session, including on mobile.
  Signed-in accounts on the admin allowlist also see an **Admin** link to the separately authenticated portal.

The existing name reservation/cooldown, prestige and unlock rules apply on the website. No web form grants XP,
unlocks or an administrative role. Prestige resets the current level and level-specific unlocks under the same
rules as the in-game action; read its confirmation before submitting.

### Profile privacy

Named accounts are public by default, including existing accounts. Set your profile to private in My account to
remove it from player search and leaderboards, hide the profile and personal match list, and replace your identity
with an anonymous label on public match pages. Visibility is checked when the page is requested, including old
rounds. Private identities are not embedded in hidden HTML fields or links. You can still see your own profile,
and administrators retain access for moderation. An administrator can hide a profile separately; the owner cannot
override that moderation setting by choosing public.

This controls the website. It does not conceal your name from other people in a game or edit existing round
recordings. The website does not offer public replay downloads. No Discord ID, IP address, previous-name history,
session token, invite key, synced controls or audit trail is included in a public player response.

### Appearance synchronization

The updated client synchronizes spy cosmetics, merc cosmetics and the displayed title independently of other
settings. Changes refresh in menus and lobbies every 30 seconds and before joining. Appearance stays unchanged
during a round. Website edits do not replace key bindings, audio settings, or display preferences.
Other players see a changed title on your next join. Cosmetics can update in your current lobby or hub.

Each appearance has a server revision. A save based on an older revision is rejected rather than silently
overwriting a newer selection; reload the current appearance or explicitly reapply your selection. Items and
titles must still be unlocked when the save occurs. Prestige can make a previously selected item unavailable.

The master seeds appearance from existing synced settings. Older clients retain their existing settings format,
but legacy whole-profile uploads cannot overwrite managed appearance. An updated client connected to an older
master falls back to the original settings sync. Use the current client release for live web appearance editing.

## Administration and access

Discord must be configured on the master for accounts and the authenticated portals. The existing Discord
callback handles separate game, admin and `player.` browser flows. Player cookies never grant admin access, even
when the same person also belongs to the master's admin allowlist. The admin allowlist remains in server config.

Browser sessions store token hashes, use HttpOnly cookies (Secure on HTTPS), expire, and protect mutations with
CSRF checks. Player website OAuth states are short-lived and single-use. Game bearer tokens and browser cookies remain
separate authentication mechanisms. Browser traffic has an independent rate budget from game API traffic.

The game's `/api` routes still require the configured shared invite key. Public web pages and their refreshes use
separate, allowlisted projections. Do not put the invite key in JavaScript, URLs, cookies or HTML.

The appearance interface is additive: `GET` and `PUT /api/v1/account/appearance` require the game's existing
invite key and account bearer token. The update carries an expected revision, spy/merc item IDs and an optional
title. A stale revision returns 409; rejected selections are not saved. Reading or saving appearance also removes
selections that are no longer unlocked, such as level rewards lost through prestige.

### Administration

The admin portal includes an operations dashboard, global lookup, filtered account/server/match views, player
detail sections, individual/all-session revocation, explicit ban durations, and structured administrative audit
details. Ban/block/rank/grant operations require a reason; invalid durations are errors, not permanent bans.
Set rank has persistent **Prestige (0–10)** and **Rank (level 1–55)** labels, alongside the required reason.
Settings are validated and committed together. Public welcome and announcement text are distinct from the game's
message of the day. Plain text is encoded before display.

Server command history distinguishes queued, dispatched, response received, expired and interrupted commands.
Received output does not by itself mean the command succeeded. Pending/dispatched commands become interrupted
after a master restart and are never replayed automatically. The advanced console is still limited to trusted
servers and authorized administrators. Live refresh preserves form drafts, focus and filters.

## Deploying and rolling back

Schema version 7 adds player web sessions, OAuth states, profile visibility, revisioned appearance, administrative
command history and audit metadata. The upgrade runs transactionally; existing game accounts and history remain.
Back up the stopped service's program, configuration, database and signing key together before deploying.

Deploy the master with `tools/deploy/Deploy-Vps.ps1 -Services master`, then publish the compatible client through
the existing release workflow. The launcher version changes only when its own code changes. Verify the public
home/download pages, protected admin entry point, master status and release assets after publication.

An older master cannot open a newer schema. Rollback must stop the service and restore the matching old program
and database backup together. Remove SQLite WAL/SHM sidecars for the database before restoring the backup, so a
newer journal cannot be replayed into the restored database. The deploy script does this on automatic rollback.
Restoring an older database also discards account changes made since the backup; retain the failed database for
investigation if rollback is needed after users have started using the new service.

## Manual acceptance checklist

Build `OpenSCCT.slnx` and `game/OpenScctGame.slnx`. The current local workflow does not run automated test suites or
game smokes. Use fictional local accounts for visual review; never seed demonstration data into production.

- Review desktop and narrow layouts, keyboard focus, long names, empty lists and disconnected refreshes.
- Check Discord success, cancellation and expired/reused state; account ownership and player/admin separation.
- Check names, privacy on historical/direct/public pages, session revocation and invite-key enforcement.
- Check locked items, prestige eligibility, concurrent saves, first-sync accounts and older-master fallback.
- Check administrative actions, invalid durations, settings atomicity, command timeouts/restarts and audit entries.
- Confirm that updating a data panel leaves an edited form, selected filter and focused control intact.

See [Online services](ONLINE.md), [Launcher](LAUNCHER.md) and [Privacy](legal/PRIVACY.md).
