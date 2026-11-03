# US lobby configuration templates

These templates configure two dedicated lobbies on one host. They do not provision a host or
create a US hub. The master remains in London; players connect directly to the game host.
See the [online guide](../../../docs/ONLINE.md) for server setup.

## Lobby behaviour

- Each lobby accepts up to 24 players. Automatic start needs at least two players and everyone
  playing to be ready. Auto balance keeps the teams within one player.
- Any joined player can change the map and match options. A change clears readiness.
- Public configuration allows match settings and return-to-lobby actions. Kick, ban and
  server-wide voice bans require the listen host or an authenticated server admin. Setting
  an admin password currently disables public configuration.
- Bots are off. A single rotation entry preserves player-selected settings between series.
- The processes use separate UDP ports, ban files and match logs. LAN discovery is off.

## Deploy

1. Make private copies of the templates and replace the invite-key placeholder.
2. Install matching server and client versions with the moderation-permission fix, plus the
   server's map/content dependencies.
3. Allow inbound UDP ports 28888 and 28890.
4. Run each configuration as a separate supervised process:

```text
svm-server --config <absolute-path-to-server-1.json>
svm-server --config <absolute-path-to-server-2.json>
```

Keep credentials out of Git. Validate simultaneous matches, map changes, peak memory, tick
times and CPU capacity before choosing a production host size.
