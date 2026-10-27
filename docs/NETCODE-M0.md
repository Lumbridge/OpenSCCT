# Netcode M0

What `OpenScct.Net`, `OpenScct.Server` and `OpenScct.Server.Cli` did at milestone M0, how the wire format works, what the
tests measured, and what was a stand-in for the full UE2 model described in `docs/NETCODE.md`.

> Superseded in part. The packet, bunch, channel and transport layers below are still current. The
> replication layout, relevancy, function calls, send budget, maps and handshake content check are now the
> generated UE2 model of `docs/NETCODE.md`: the hand-written `MovementRepLayout` and `CubeWalkerRepLayout`
> are gone (the walker replicates with the generated Pawn layout plus `CubeWalkerNet`), the per-connection
> QueuedBits budget became the driver budget with the CONF* law, and `MapCatalog` loads SVMaps.

The M0 check is "4 loopback cube-walkers replicate": a server and 4 clients, each moving its own walker,
and every client sees all 4 walkers where the server has them. It passes on loopback and over simulated
internet links (see [Measured](#measured)).

## Layers

```
ServerHost / ClientSession / ListenServer        OpenScct.Server   (hosting, handshake, CubeWalker)
NetDriver                                        OpenScct.Net      (connections, replication pass, proxies)
NetConnection  ->  Channel: ControlChannel, ActorChannel
FOutBunch / FInBunch, PacketHeader, NetStats
RepLayout (MovementRepLayout), NetClassRegistry, ProxyMotion, NetTimeline
FBitWriter / FBitReader, NetQuantize
ITransport: LoopbackTransport, SimLinkTransport, LiteNetLibTransport, CompositeTransport
```

Names follow UE2 where there is a UE2 counterpart (`NetDriver` is UNetDriver, `NetConnection` is
UNetConnection, `ActorChannel` is UActorChannel, `FBitWriter`, `FOutBunch`, `EChannelType`,
`EConnectionState`), with the U prefix dropped like `OpenScct.Core` does for `Level` and `Actor`.

Everything in `OpenScct.Net` and `OpenScct.Server` is simulation code: deterministic, no system clock, no
`System.Random`. Time comes in from the host as microseconds (`IClock`): a `VirtualClock` in tests, a
`Stopwatch` clock in `svm-server`.

## Bit streams

`FBitWriter` and `FBitReader` write and read bits least significant first within each byte, like UE2.
A write that does not fit, or a read past the end, sets `IsError` instead of throwing.

| Call | Format |
|---|---|
| `WriteInt(value, max)` | UE2 SerializeInt: bits go out low first while the value so far plus the next bit can still be below `max`, so a maximum that is not a power of two can save the top bit |
| `WriteIntWrapped(value, max)` | low log2(max) bits of a wrapping sequence number (max a power of two) |
| `WriteIntPacked` / `WriteSignedPacked` | 7 bits per group, each group led by a "more follows" bit; zigzag for signed |
| `WriteQuantizedFloat(v, min, max, bits)` | linear over [min, max], rounded, up to 24 bits |
| `WritePackedVector` | components rounded to 1/16 uu, one shared bit count (5 bits) then each component biased to unsigned; a zero vector is 8 bits, a map-scale position about 10 bytes |
| `WriteCompressedRotator` | per axis a "non-zero" bit, then the low 16 bits (the UE2 compressed rotator layout with 16 bits instead of the high byte) |
| `WriteCompressedRotatorByte` | the UE2 form: per axis a "non-zero" bit, then the high byte |
| `WriteString` | UTF-8 with a packed byte count |

## Transports

`ITransport` moves whole datagrams to and from numbered endpoints (`EndpointId`, 1 and up). It may drop,
duplicate and reorder; everything else is done above it.

- `LoopbackTransport` (on a `LoopbackNetwork`): in process. Each connection has two single-producer
  single-consumer rings with preallocated slots, so the two ends can sit on different threads without locks
  and without allocating. A full ring drops, like a full socket buffer.
- `SimLinkTransport` (on a `SimLinkNetwork`): in process, single-threaded, driven by an `IClock`. A seeded
  `SimRandom` applies loss, duplication, jitter and reordering; the same seed and call order give the same
  deliveries. Profile latency and jitter are ping figures (round trip); each direction gets half.

  | Profile | Ping | Jitter | Loss | Duplicate | Reorder (extra delay) |
  |---|---|---|---|---|---|
  | A | 0 ms | 0 | 0% | 0% | 0% |
  | B | 60 ms | 0 | 1% | 0.5% | 0.5% (20 ms) |
  | C | 120 ms | +-20 ms | 2% | 1% | 1% (30 ms) |
  | D | 250 ms | 0 | 5% | 2% | 2% (50 ms) |

- `LiteNetLibTransport`: UDP through LiteNetLib's lightweight manager, unreliable delivery only, a fixed
  1232-byte MTU and datagrams of at most 1200 bytes. Events are collected on the caller's thread inside
  `TryReceive`. `Listen(port, max, loopbackOnly)` and `CreateClient(loopbackOnly)`; tests bind to
  127.0.0.1 and ::1 only.
- `CompositeTransport`: several transports seen as one by a server, for a listen server that takes its
  local player over loopback and remote players over UDP.

## Packets

Every packet is a header, zero or more bunches, then a single 1 bit. The receiver finds the end of the data
from the highest set bit of the last byte (the UE2 packet trailer).

| Field | Bits | Meaning |
|---|---|---|
| magic | 8 | 0xA7, drops stray datagrams early |
| Seq | 16 | sequence number of this packet |
| HasAck | 1 | the fields below are present |
| Ack | 16 | newest packet received from the other side |
| AckBits | 32 | bit i set: packet Ack-1-i was received too |
| EchoTime | 16 | the other side's SendTime of packet Ack |
| EchoDelay | 16 | how long packet Ack was held before this send (0.1 ms, saturating) |
| Tick | 32 | sender's simulation tick when the packet was built |
| SendTime | 16 | sender clock, 0.1 ms units, wrapping |
| bFlushComplete | 1 | last packet of a server replication pass that covered every relevant actor |

Rules:

- A packet is processed only when it is newer than every packet processed before. Late and duplicated
  packets are dropped whole and never acked, so an ack bit means "processed".
- A sent packet older than the newest ack with its bit clear is lost (NAK) at once. A packet with no ack
  after the ack timeout (`AckTimeout` 1 s, or 3 round trips if that is longer) is lost too.
- Round trip time: now minus EchoTime minus EchoDelay, smoothed (EWMA, 1/8) with a jitter estimate, in
  `NetStats` together with byte, packet, loss and resend counters.
- Bandwidth follows the UE2 QueuedBits rule: sending adds bits (UDP and IP headers included), time pays
  them off at the connection's net speed, and the server stops replicating to a saturated connection.
- Keep-alive: a connection with nothing to send still sends an empty packet after `KeepAliveTime`.
  Connections close after `ConnectionTimeout` (open) or `InitialConnectTimeout` (pending) of silence.

## Bunches and channels

A bunch is a piece of one channel's data. Header, in UE2 order: bControl (bOpen, bClose when set),
bReliable, channel index (`WriteInt` over `MAX_CHANNELS` 1023), the reliable sequence
(`WriteIntWrapped` over `MAX_CHSEQUENCE` 1024) when reliable, the channel type when reliable or opening,
and the payload length in bits.

- Reliable bunches are numbered per channel index; the counters live on the connection, so a reopened
  index carries on numbering and a stale copy can never reopen a closed channel. They are kept until acked,
  resent in the next packet when their packet is lost, and delivered in order: bunches ahead of a gap wait
  inside the channel. More than `RELIABLE_BUFFER` (128) unacked bunches on one channel closes the connection.
- Unreliable bunches are handled as they arrive. Data for a channel that is not open is ignored, like UE2.
- The first reliable bunch to arrive opens the channel, even when the open bunch itself is still on its way.
- A channel closes with a reliable close bunch; its index is freed when the close is acked. New channels
  take indexes round robin, so a just-closed index is not reused at once.
- A bunch must fit in one packet (no partial bunches yet).

### Control channel and handshake

Channel 0, one message per reliable bunch. Closing it closes the connection.

```
client                                  server
Hello{protocolHash, contentHash,  --->  checks both hashes and the player count
      name, netSpeed}
                                  <---  Welcome{playerId, map, serverTick, netRate}
                                        or Reject{reason, file} (connection lingers 2 s, then closes)
Join                              --->  spawns the player's CubeWalker, starts replicating
Text (either way, any time)
```

- The protocol hash covers the wire format version and every replicated class with its layout signature, in
  registration order (`NetClassRegistry.ComputeProtocolHash`).
- A hash mismatch is refused with the original wording, "Your game's version is not the same as the
  server's", and names the file that differs. M0 has a single content hash, so the file named is the
  server's map; a per-file manifest comes with `OpenScct.Content`.
- A full server answers "The server is full".

### Actor channels

The server opens one per relevant actor. The open bunch carries the class index, the net GUID, the role
the receiver takes (UE2 swaps Role and RemoteRole; an autonomous proxy is sent as a simulated proxy to
everyone but its owner), the spawn location and rotation, then every initial property. After that the
payload is a list of field records, each a rep index (`WriteInt` over properties plus functions) and the
value, as in UE2.

- Shadow state: each property is compared in quantised form (`RepLayout.Capture` turns values into ints),
  so a change below one step is not sent and the client ends up with exactly the shadow value.
- Property retirement (UE2): each property remembers the packet that last carried it. When that packet is
  lost and it carried the property unreliably, the property is marked dirty and goes out again with its
  current value.
- Open phase: until the open is acked, property bunches are reliable. After that, updates pause until the
  open-phase bunches are all acked, then go unreliable for good. Reliable and unreliable property data never
  overlap, so a late reliable copy cannot overwrite newer values.
- On a client, closing the channel destroys the actor.
- Clients may call server functions only on actors their connection owns (the connection's `Actor` or
  anything with it up the Owner chain); clients never send properties.

### Replication layout seam

`RepLayout` is the seam where generated layouts plug in. A layout gives its property count, shadow word
offsets, `ShouldReplicate(property, actor, RepFlags)` (bNetInitial, bNetOwner, bNetDirty, Role,
RemoteRole), `Capture`, `Write`, `Read`, and its server functions. M0 has one hand-written layout,
`MovementRepLayout` (Location, Rotation, Velocity, Physics, always replicated from the authority), and
`CubeWalkerRepLayout`, which adds the ServerMove function. Layouts generated from the decompiled replication
blocks will implement the same class, and `NetClasses.Create()` in `OpenScct.Server` becomes generated
registration.

## Server and client

- `ServerHost` ticks its `World` at 60 Hz: `TickDispatch` (receive, handshake, moves), `World.Step`, then
  `TickFlush` with replication when a flush is due. Flushes are spread evenly at `NetRate` (20 internet,
  35 LAN, up to 60). `RunRealtime(clock, token)` runs it on a fixed schedule and drops the backlog after a
  stall.
- `CubeWalker` is the M0 test pawn: a box walking on a plane with no collision, GroundSpeed 440,
  AccelRate 2048. Like a UE2 pawn driven by a remote player, the server moves it only when its moves arrive:
  `ServerMove` skips moves already applied and fills a gap (every copy of some moves lost) by repeating the
  next move for the missing ticks, up to 15.
- Moves: the client makes one move per tick and sends it unreliably on its walker's channel together with
  the 3 before it (`K.MoveResendCount` 4, as the original resends moves). Wire form: count-1 (2 bits), the
  newest TimeStamp (32 bits), then per move Forward and Strafe (8 bits each), ViewYaw (16), Buttons (8).
- `ClientSession` runs a `World` in NM_Client mode. Each tick: receive, send the newest moves, step the
  world, then show every replicated walker at a render time `InterpolationIntervals` (2) server send
  intervals behind the newest server tick.
- `ProxyMotion` keeps the replicated states with the server tick of their packet and interpolates between
  them (shortest way round for rotation); past the newest state it extrapolates with the replicated velocity
  for up to 6 ticks, then holds. When a flush-complete packet arrives without a bunch for an actor, the
  actor did not change at that tick, so its last state is held there too: a walker that stops and starts
  again does not slide across the pause.
- `NetTimeline` estimates the server tick from the packets: it follows the least delayed recent packet and
  moves at most 1/16 tick per tick, so displayed time stays smooth.
- `ListenServer`: the same `ServerHost` on its own thread, local players over `LoopbackTransport`, remote
  players optionally over a second transport through `CompositeTransport`.
- `svm-server --port 28888 --max 4 --map TestBox [--rate 20] [--seed 1] [--seconds n] [--loopback-only]`
  runs a headless server on UDP until Ctrl+C. On Windows it asks for 1 ms timer resolution while it runs.
  Only TestBox loads (`MapCatalog`, a stub with four player starts) until the SVMap loader is wired in.

## Constants

From the shipped `System/Default.ini` (`K`, all `[Provenance(Decoded)]`): NetServerMaxTickRate 20,
LanServerMaxTickRate 35, MaxClientRate 20000, ConfiguredInternetSpeed 10000, ConfiguredLanSpeed 20000,
ConnectionTimeout 15, InitialConnectTimeout 15, AckTimeout 1.0, KeepAliveTime 0.2, RelevantTimeout 5.
Stock UE2 limits (Estimated until read from the game exe): MAX_CHANNELS 1023, MAX_CHSEQUENCE 1024,
RELIABLE_BUFFER 128. The move resend count 4 comes from the SBase move functions. The same ini section also
has `maxAcceptableUnAckBunch=40` and the adaptive `CONF*` net speed settings, which are not used yet.

## Measured

From `tests/OpenScct.Integration.Tests` (virtual clock, 4 clients, 10 s measured after a 2 s warm-up, 9600
samples per run). "Displayed" is the interpolated position against the server's position at the render
time; "replication" is each replicated state against the server's position at the tick it is stamped with.

| Link | Displayed within 5 uu | Displayed max | Replication max | Server to client |
|---|---|---|---|---|
| Loopback, 20 Hz | 100% | 1.46 uu | 0.043 uu | 2.4 KB/s |
| Loopback, 35 Hz | 100% | 1.07 uu | 0.043 uu | 4.1 KB/s |
| Loopback, 60 Hz | 100% | 0.04 uu | 0.044 uu | 7.0 KB/s |
| SimLink B | 98.6% | 12.5 uu | 0.043 uu | 2.4 KB/s |
| SimLink C | 89.3% | 14.7 uu | 0.043 uu | 2.4 KB/s |
| SimLink D | 94.2% | 14.7 uu | 0.043 uu | 2.4 KB/s |

Bandwidth counts UDP and IP headers (28 bytes a packet); the limit is 12 KB/s. A server tick allocates
0 bytes after warm-up, on loopback and on SimLink C. Reliable control lines (60 from the server, 40 from
each client) arrive once and in order on B, C and D, and over a harsh link (20% loss, 30 ms jitter, 5%
duplicates, 10% reordering) for 12 random seeds.

Why B and C have relaxed tolerances: the server runs moves when they arrive, as UE2 does, so with jitter or
loss some server ticks apply no move and the next applies two. Under profile C, 15% of server ticks apply
none and 14% apply two. The server's path then has one-tick stalls of one move (about 7.3 uu) that
interpolation between 20 Hz states smooths over. The replication error stays at quantisation on every
profile, so what reaches the client is exact. B must keep 98% of samples within 5 uu and stay under 20 uu;
C keeps 85% within 5 uu, 99.5% within 15 uu and stays under 25 uu.

## Stand-ins and what UE2 parity still needs

- Packet format: UE2 sends 14-bit packet ids and acks as separate entries; M0 piggybacks 16-bit sequence
  numbers with a 32-bit ack field and the timing echo. UE2 also processes out-of-order packets; M0 drops
  them. Neither changes what reaches the game, but the UE2 behaviour should be checked in Ghidra
  (`UNetConnection::ReceivedPacket`) before deciding whether to match it.
- No partial bunches: every bunch must fit in one packet.
- No packet authentication or encryption: a spoofed packet with a valid header could push the receive
  sequence ahead and stall a connection until it times out. A per-connection salt from the handshake (or
  LiteNetLib's own connection check) is the planned guard.
- Relevancy: always relevant, owned or visible actors (`NetDriver.IsNetRelevantFor`). Still to port: UE2
  `IsNetRelevantFor` (line of sight, distance), priority sorting under the rate limit, `RelevantTimeout`
  before closing a channel, bNetTemporary and torn-off actors.
- Replication conditions are hand-written. Generated `RepLayout`s, object references by net GUID, map
  actors bound by SVMap index and the bNetDirty rules come with the decompiler output.
- RPCs: only the unreliable server function ServerMove. Full FUNC_Net, NetReliable and simulated semantics,
  and client functions, are still to do.
- Moves: no client prediction, saved moves or corrections yet; the local walker is interpolated like the
  others. The port of MyServerMove, BigServerMove and MyClientAdjustPosition (fErrorMax 1.73, Very, Short
  and Long forms, up to 100 saved moves) replaces ServerMove in M4. No move validation (speed checks) yet.
- `net.proxy=ue2` (apply replicated values directly, UE2 simulated proxy extrapolation) is not there yet;
  `ProxyMotionMode` has the slot.
- Net speed: the client's requested speed is clamped to MaxClientRate; the adaptive `CONF*` speed steps of
  the original are not ported.
- Handshake: one content hash; the per-file manifest (to name the differing file) comes with `OpenScct.Content`.
- `svm-server` uses `MapCatalog`, a TestBox stub. NAT punch-through, LAN broadcast, UPnP, the introducer,
  relay and master server list are later milestones.
- Physics: CubeWalker has no collision, and `ServerHost` sets no physics driver. Before one is set, the
  actor tick must skip physics on the server for pawns whose RemoteRole is ROLE_AutonomousProxy (the UE2
  rule), because ServerMove already moves them.
- Real-time loop: `RunRealtime` sleeps 1 ms at a time and yields for the last 2 ms; the listen-host jitter
  target (under 1 ms at p99) is not measured yet.

## Running

```
dotnet test --project tests/OpenScct.Net.Tests/OpenScct.Net.Tests.csproj
dotnet test --project tests/OpenScct.Integration.Tests/OpenScct.Integration.Tests.csproj
dotnet run --project src/OpenScct.Server.Cli -- --port 28888 --max 4 --map TestBox
```

To see the measured numbers, run the test executable with live output, for example
`tests/OpenScct.Integration.Tests/bin/Debug/net10.0/OpenScct.Integration.Tests.exe -showLiveOutput`.
