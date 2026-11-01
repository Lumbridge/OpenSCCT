# Player-configurable US lobbies

These two configuration templates run regular dedicated lobbies on one host, without a US hub. Deploy with the server moderation-permission fix and matching client UI so public configuration does not grant moderation rights.

- Each lobby accepts up to eight players, allowing 1v1, 2v2, 3v3 or 4v4 according to who joins and readies. Eight is a ceiling, not a launch requirement. Automatic launch requires at least two players and everyone playing to be ready. Auto balance keeps the sides within one player; it does not require even numbers or change map objectives/lives to match population.
- Every joined player can choose the map and match options through the lobby UI. Changes clear readiness so players must ready again. There is no elected lobby owner or voting mechanism.
- Public configuration allows match settings and return-to-lobby actions, but never kick, ban or server-wide voice bans. Moderation requires the listen host or an authenticated server admin. Setting an admin password currently disables public configuration.
- Bots are off by default. A single initial rotation entry preserves player-selected settings between series instead of forcing a multi-map rotation.
- The two processes use different UDP ports and separate ban/match-log files. LAN discovery is disabled. The master remains in London; game connections should reach the US host directly.

Before deployment, replace the invite-key placeholder in private copies, install the server's map/content dependencies, and allow inbound UDP 28888 and 28890. Start separate supervised processes using `svm-server --config <absolute-path-to-server-1.json>` and the corresponding second config. Keep credentials out of Git. These are templates, not provisioned services; no AWS resources have been created.

A $7 Lightsail instance is a trial candidate, not verified capacity for two populated 4v4 matches plus spectators. Validate both concurrent matches, map transitions, peak memory, tick times and CPU burst balance before relying on that size.
