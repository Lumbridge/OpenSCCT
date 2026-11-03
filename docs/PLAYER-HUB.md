# Player hub

[Documentation](README.md)

The [player hub](https://master.lumbridge.co.uk) lists servers, profiles, leaderboards, recorded matches
and project updates. Sign in with the Discord identity you use in the game to manage your account and appearance.

This is the website. The in-game social space is covered in the [social hub guide](HUB.md).

## For players

| Page | What you can do |
| --- | --- |
| Download | Get the launcher and installation instructions. |
| Servers | Find listed games and hubs by map, mode, region and free space; join the installed game. |
| Players | Find a player and view rank, stats, achievements, unlocks and recorded rounds. |
| Leaderboards | Compare lifetime XP, wins or kills from counted rounds. |
| Matches | Browse recorded rounds and team scoreboards; uncounted rounds are labelled. |
| Improvements | Browse OpenSCCT features with screenshots and links to their details. |
| Updates | Read published release notes. |
| Help & contribute | Find guides, report bugs, suggest improvements and contribute changes. |
| My account | Manage your name, privacy, sign-ins, prestige, unlocked cosmetics and title. |

**Join** opens `openscct://join/<server-id>`. On Windows, run the launcher once to register the handler;
launcher 0.8.26 and game 0.8.58 or later are required. Flatpak 0.8.58+ registers it on Linux and Steam Deck.
The link contains only the public server ID. The game uses its own sign-in and asks for a password if needed.
Browser sign-in does not sign in the game. Active matches are not interrupted; joining waits for the main menu.
If no app opens, follow the page's installation/update link. Unlisted servers and private connection details
are not published.

Leaderboards exclude private, unnamed and actively banned accounts. They are lifetime totals, not seasons or
estimates of missing history. The **Sign-ins** page manages access to the game and website; it does not list
game sessions. **Sign out** in the header ends this browser's session. Allowlisted admins also see an **Admin**
link, which leads to a separately authenticated portal.

Names, prestige and unlocks follow the same rules as in the game. Prestige resets the current level and its
level-specific unlocks; read the confirmation before submitting. Website forms cannot grant XP or admin access.

The **Improvements** page at `/improvements` presents OpenSCCT features in six sections: matches and practice,
equipment, hubs, recordings, player records, and setup. Screenshots open at full size; search filters the
feature descriptions. Images are captured from the running game with its normal menus and gameplay.
Text, section links and images also work without JavaScript.
The old `/progress` address redirects to this page.

**Updates** reads stable public GitHub releases, refreshing at most every ten minutes. If GitHub is unavailable,
the last successful results remain visible. Drafts, prereleases and content-storage releases are excluded;
GitHub's full release archive stays linked.

### Help and contributions

The public `/help` page links to setup guides, GitHub issues and contribution instructions.
You do not need an account to read it. **Report a bug** opens a website form at `/help/report-bug`.
Sign in with the same Discord account you use in OpenSCCT, describe the problem, then review the
report before pressing **Send bug report**. You do not need a GitHub account. The website creates
the public GitHub issue and gives you a link to follow it.

Choose whether the problem concerns the game, launcher or website. Reports opened from the game
or launcher can prefill the version, operating system and available match details. Check or edit
these details, then describe how to reproduce the problem; reproduction steps are never
filled in automatically. Reports opened directly on the website start blank. Website reports
include the website's version, build and commit. Only the
report text you review is published. Your player name, Discord ID, account details, network
address and sign-in credentials are not added. Anything you type into the form will be public,
so leave out passwords and personal information. The form accepts text and links, not file uploads.

Each account can send up to three reports in 24 hours, at least one minute apart. Repeated sends
of the same report return its existing result. If GitHub does not confirm the outcome, the website
does not send it again automatically; check the issue list before trying again. A private receipt
records the account, a hash of the report, submission time, result and issue number for 30 days to
prevent duplicates and apply these limits. The full report text is not stored in that receipt.

Feature suggestions and pull requests still open directly on GitHub and require a GitHub account.
Your Discord sign-in does not sign you into GitHub. Server operators can enable the website form
with the [bug-report setup instructions](ONLINE.md#website-bug-reports).

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

The public website (`/`) and administration portal (`/admin`) ship with `svm-master`. No separate web
host or JavaScript build service is needed. Discord must be configured for accounts and sign-in. The existing Discord
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
The **Back to public site** button in the admin header returns to the player hub from any administration page.
Settings are validated and committed together. Public welcome and announcement text are distinct from the game's
message of the day. Plain text is encoded before display.

Server command history distinguishes queued, dispatched, response received, expired and interrupted commands.
Received output does not by itself mean the command succeeded. Pending/dispatched commands become interrupted
after a master restart and are never replayed automatically. The advanced console is still limited to trusted
servers and authorized administrators. Live refresh preserves form drafts, focus and filters.

### Maintaining the improvements page

Feature descriptions live in `src/OpenScct.Master/Web/GameAdditions.cs`; the page is rendered by
`Player/PlayerPortal.Improvements.cs`. Check claims against the owning code and documentation when updating them.
Describe what players can do in plain language, including conditions such as host settings or experimental support.
Keep the focus on OpenSCCT without competitor comparisons or completion percentages.

Screenshots live in `docs/images/website/showcase-*.png`. The `showcase-sources.txt` file beside them records their local source paths,
dimensions, hashes and capture details. Use actual gameplay and normal menu screens, with default weapon skins.
Do not use test fixtures, fabricated statistics or debug overlays. Keep captions accurate; never retouch the
screenshots to invent game state or remove unwanted elements after capture.
The master embeds these files under the existing `/web/assets/showcase-*` URLs.
The page does not run tests or read repository files on a request; its content and assets ship with the master.

## Deploying and rolling back

Database upgrades run transactionally and preserve existing accounts and history. The website's account,
privacy, appearance and administration features require the matching master schema.
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

Build `OpenSCCT.slnx` and `game/OpenScctGame.slnx`, then run the relevant account and website tests.
Use fictional local accounts for visual review; never seed demonstration data into production.
Automated checks do not replace these browser and account-flow checks:

- Review desktop and narrow layouts, keyboard focus, long names, empty lists and disconnected refreshes.
- Check Discord success, cancellation and expired/reused state; account ownership and player/admin separation.
- Check names, privacy on historical/direct/public pages, session revocation and invite-key enforcement.
- Check locked items, prestige eligibility, concurrent saves, first-sync accounts and older-master fallback.
- Check administrative actions, invalid durations, settings atomicity, command timeouts/restarts and audit entries.
- Confirm that updating a data panel leaves an edited form, selected filter and focused control intact.

See [Online services](ONLINE.md), [Launcher](LAUNCHER.md) and [Privacy](legal/PRIVACY.md).
