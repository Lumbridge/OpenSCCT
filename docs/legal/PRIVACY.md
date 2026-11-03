# OpenSCCT Privacy Policy

Last updated: 3 November 2026

This explains what OpenSCCT (the game, the launcher, the master server and the OpenSCCT Discord app) collects and
why. We keep it to what the game needs. We don't sell data, show ads or track you across other sites.

## Signing in with Discord (optional)

Signing in is optional; you can play offline, on LAN and on open servers without it. If you sign in, we ask Discord
for the `identify` scope only. We store:

- your **Discord user id**, to know which account is yours;
- the **player name** you choose, and your past names;
- your **stats, achievements, unlocks and synced game settings**;
- a **sign-in token** (we keep only a hash of it).

We don't ask for or store your email, password, friends list or servers.

## Player website

Named game accounts have a public player profile by default, including existing accounts. The website shows the
player name, rank, statistics, achievements, unlocks and recorded match history, and includes eligible public
accounts in lifetime leaderboards. Visitors do not need to sign in to see public pages.

Sign in to My account with Discord to make your profile private. This hides your profile and personal history,
removes you from player search and leaderboards, and anonymizes your identity on public website match scoreboards.
It applies to historical website pages as well as new ones. Administrators can still access records for moderation.
Website privacy does not hide your name during gameplay or change existing recordings.

Website sign-in uses necessary browser cookies for a session and short-lived sign-in state. We store only a hash
of each session token, along with its account, creation and last-used times and a form-protection token. You can
end individual game or website sign-ins, or sign out other sessions, from your account. We do not use advertising
or analytics cookies. Discord identifiers, connection addresses, session credentials, previous names, synced
controls and administrative records are not shown on public profile pages.

When you change cosmetics or your title on the website, we store your selection and its revision so the game can
synchronize it without overwriting unrelated settings. Account and moderation changes are audited to help resolve
conflicts and prevent misuse.

## Website bug reports

The website's bug-report form requires your existing Discord-linked OpenSCCT account. You review the report
before sending it. The title and report text are then published as a public issue in the OpenSCCT GitHub
repository. Website reports include the website's version, build and commit. Reports opened from the game
or launcher prefill the app version, build, commit and operating system. Game reports also include available
map, mode, side and host/client/offline context. Reports opened in a map also include the local player's
position, facing, velocity, movement state, physics and animation details captured when help opened.
Launcher reports distinguish launcher and installed game versions. You can edit these details and remove optional context before sending. Error messages and links are included only
when you enter them into the form.

Prefilled details travel in the report link and a necessary, short-lived browser cookie during Discord
sign-in. The cookie expires after ten minutes and is cleared when sign-in returns to the website.

We do not automatically add your player name, Discord ID, private account details, network address or sign-in
credentials to the issue. The issue is created by the GitHub account or app used by the server. Anything you
include in the report text becomes public, so do not include passwords or personal information. The form does
not upload files.

We keep a private receipt containing your OpenSCCT account ID, an opaque report ID, a hash of the report title
and body, the submission time, the result and the GitHub issue number. This prevents duplicate reports and
limits repeated submissions. The receipt does not contain the full report text. It expires after 30 days and
is removed by regular server cleanup; if the server was offline, cleanup resumes after startup. This does not
delete the public GitHub issue.

## When you play online

- The master server and game servers see your **IP address** to connect you. It's used for joining games, preventing
  abuse and enforcing bans.
- **Round recordings (demos)** of online rounds, including player names, may be kept for up to 7 days so signed-in
  players can watch them back.
- **Voice chat** is relayed live between players and is not recorded.
- Admin actions and bans are logged.

## Discord Rich Presence

If the Discord app is running, the game shows your current activity (menu, map, mode, team, server name, player
count) on your Discord profile, and lets friends join you. This goes to Discord on your own PC; it is not sent to us.
You can turn it off in Options → HUD → "Discord status".

## Sharing

Public profile and match information is visible to website visitors as described above. We also share data with
the services that run OpenSCCT (the server host and cloud storage for demos) and when required by law.
Discord's handling of your data is covered by
[Discord's Privacy Policy](https://discord.com/privacy).
Reports published to GitHub are also subject to
[GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## Keeping and deleting data

Account data is kept while your account exists. Demos are deleted after 7 days. To have your account and its data
deleted, open an issue at [github.com/Lumbridge/OpenSCCT/issues](https://github.com/Lumbridge/OpenSCCT/issues) (don't
post personal details there; we'll follow up). You can also remove the OpenSCCT app under Discord → User Settings →
Authorized Apps.

## Children

OpenSCCT is not meant for children under 13 (or the minimum age for Discord in your country).

## Changes

This policy may be updated. The date at the top shows the latest version.

See also the [Terms of Service](TERMS.md).
