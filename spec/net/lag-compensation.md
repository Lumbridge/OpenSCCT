# Lag compensation and fire prediction (OpenSCCT's design of two Reloaded features)

Units: none of its own. An OpenSCCT design after Reloaded's lag compensation and client fire prediction
(`spec/reloaded/FEATURES.md`, sections net.lagcomp and net.fireprediction); the shot spread follows Reloaded's FRand
hook, specced in `spec/native/reloaded-hooks.md` rule 1.

## Purpose

In the original a client's merc gun or spy pistol shot is traced on the server against the server's present copy of
the other players, so a shooter on a laggy link has to lead his target by his ping plus the view delay, and he sees
the bullet's impact a round trip after he fired. Reloaded adds two things: a server-side lag compensation switch
(`!lagcomp [on|off|reset|<ceiling>]`, "lagcomp: on - ceiling {}ms") and client fire prediction (impacts and their
sounds drawn by the shooter at once; its server half replaces the first two FRand calls of `TraceFire` and
`TraceFireWithPos` with a hash both ends can compute). Only those observable parts are Reloaded's; how the rewrite does
it is described here in our own terms.

## Data

- **Pawn history (server).** Per pawn, a ring of the last 64 world ticks: the tick, the pawn's location and its hit
  pills for that tick (the shapes the bone trace tests, from the same pose source). A pawn is recorded every tick it
  exists, so its ticks are contiguous; a destroyed pawn loses its history. Recording runs only while the switch is on;
  turning it off clears every history.
- **View time.** Server ticks times 256 (the client timeline's fixed point). A client's view time is the server time at
  which it shows other players: its render time when it interpolates proxies, its render time plus the interpolation
  delay when it dead-reckons them (dead reckoning walks proxies on to the newest server time the client knows).
- **Shot number.** Per weapon, on the owner's client: a counter of the merc gun or pistol shots it sent (starts at 0
  with the weapon actor). On the server: the last number received.
- **Settings.** Server: the switch (on by default, as in Reloaded) and the ceiling in ms (default 120, 0 to 500, both Reloaded's). Client: fire
  prediction (on by default).

## Rules

1. **Recording.** After every world step the server stores, for each pawn, its pills and location under the tick the
   next outgoing packets are stamped with.
2. **View time on calls.** Every combat call a client sends to the server carries the client's view time of the
   moment the call was made (also when the call waits for an actor channel and goes later). Calls to clients carry
   none.
3. **Rewind window.** When the server runs a client's call with the switch on, the target time is
   `now − clamp(now − view time, 0, ceiling)`; `now` is the newest recorded tick. A target at `now` (or no view time)
   means no rewind.
4. **Rewound pills.** During that call every bone trace tests each pawn (other than the shooter) with the pills it had
   at the target time: the recorded tick at or below it, blended with the next tick by the fixed fraction of the target
   (whole-number fraction over 256, so every run gives the same shapes), not blended when the two ticks differ in pill
   count or the pawn moved 200 uu or more between them (a respawn). A target older than the oldest kept tick uses the
   oldest; a pawn without history uses its live pose. The pawn's nearness test uses the rewound location. Nothing in
   the world is moved; world geometry, movers and every non-pill test stay live.
5. **Shot numbers.** The owner's client gives every merc gun and pistol shot the next number. The server accepts a
   number exactly one above the last it received as the shot's seed and stores it as the last; any other number is
   stored as the last but the shot uses the random stream (a resync, and no way to choose a spread by skipping).
6. **Hashed spread.** A seeded shot's two spread numbers are `hash(number, aim yaw, side tag, call) >> 8` times 2^-24,
   calls 0 and 1 (2 and 3 for the trace that goes on behind a fake ceiling); the merc side tag is `M` and the pistol's
   `S`; the aim yaw is the merc gun's accurate rotation yaw or the pistol orientation's yaw. Unseeded shots and every
   other random call use the random stream as before. The hash is a 32-bit multiply-xorshift mix; Reloaded's own mix
   is not reproduced (its inputs are only partly decoded).
7. **Prediction.** A client with fire prediction on, when it sends a merc rifle or SMG shot or a pistol shot, traces
   the same ray (rule 6, its own copy of the shooter and the aim) against its own world and the pawns as it shows them,
   and draws what the server's trace would add to the impact manager: for a world hit, the impact of the surface type
   (or, through a fake ceiling, the two holes and the trace behind it); for the pistol, the shocker impact (metal and
   water use the metal one), on a pawn hit too. A fake backdrop draws nothing. Impact rolls come from the same hash
   (calls 4 and up), not the random stream. The shot tells the server it was predicted.
8. **No double effects.** The server sends the impacts of a predicted shot to every remote player except the one who
   predicted it. Damage, hit statistics, tracers, blood and all pawn reactions come only from the server. The shotgun
   predicts nothing new (its owner already draws its pellet marks in the original).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| History length | 64 | ticks | estimated | a second at 60 Hz plus margin |
| Default ceiling | 120 | ms | decoded | Reloaded.Core 0x103603a4 (initial value) and `!lagcomp reset` 0x10076455 |
| Largest ceiling | 500 | ms | decoded | Reloaded.Core `!lagcomp <n>` 0x10076435 (below 0 gives 0, above 500 gives 500) |
| Reloaded's own default switch | on | | decoded | Reloaded.Core 0x103603a8 starts at 1; the rewrite starts with it on too |
| Teleport distance (no blend) | 200 | uu | estimated | |
| Spread mantissa | top 24 bits × 2^-24 | | decoded | Reloaded 0x100242f0 |
| Seeded random calls per trace | 2 | calls | decoded | Reloaded 0x100242f0 |

## Network

One field more on the client-to-server combat function (the view time behind a presence bit, 64 bits); the merc shot
call carries its number and the predicted bit in fields it did not use, the pistol shot call likewise. No new
messages; the server decides everything that matters.

## Edge cases

- A client controls its view time, so it can ask for any rewind up to the ceiling; that is the cost of the feature
  and why the ceiling exists. It cannot shoot into the future (rewind is never negative).
- When the two machines' traces disagree, the shooter sees his predicted impact and the others see the server's.
- Listen and local play: the host's own player is a client over loopback here, so it predicts and is compensated
  like any other (its view time is a few ticks old).

## Open questions

1. Whether Reloaded compensates only sticky cams (v3.0) or every shot (v4.0). Resolved part: the default ceiling
   (120 ms) and the range (0 to 500), decoded from the `!lagcomp` handler (Reloaded.Core 0x100758f0). That handler
   also shows Reloaded's chat rules, which the rewrite does not copy exactly: it only runs on the host machine
   (dedicated or listen; elsewhere "lagcomp: server-side setting"), plain `!lagcomp` toggles the switch, `reset`
   only sets the ceiling back and a number only sets the ceiling (neither turns it on). The rewind it applies is
   `clamp(round(shooter's delay in ms), 0, ceiling)` (Reloaded.Core 0x1000bfb0), as rule 3 here.
2. Whether Reloaded's prediction also predicts pawn hits (blood, hit sounds). The rewrite does not.
