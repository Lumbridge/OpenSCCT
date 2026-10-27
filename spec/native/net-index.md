# spec/native: networking (net-*)

Native engine behaviour behind the script networking of `spec/net/`, decoded from the game
executable and its sibling editor build. Specs are written in our own words; addresses are listed
in each file's Evidence section, names and comments in `re/ghidra/annotations/net.json`.

| File | Covers |
|---|---|
| [`net-driver.md`](net-driver.md) | Driver tick and flush, shared server send budget, saturation, bunches, acknowledgement and resend, timeouts, keep-alive, `CONF*` adaptive net speed, `resetConnectionPing`, server tick cap, datagram framing. |
| [`net-replication.md`](net-replication.md) | Server pass (dirty propagation, frequency gate, relevancy cadence, channel open and close, ordering), `ReplicateActor`, value encoding, object references, call routing with the SCCT broadcast flag, the four-slot channel cache, client receive and apply, role tick gates, `PackW`. |
| [`net-relevancy.md`](net-relevancy.md) | `IsNetRelevantFor` of the base class and every slice override, mapped through the game's vtables. |
| [`net-replists.md`](net-replists.md) | Native property list of every replicated slice class and where it differs from the script block. |
| [`net-moves.md`](net-moves.md) | Client move batching, message choice, saved moves, `View`/`DiversInfos`/old-move/action packing, server move processing, speed caps, corrections, reinit, big moves, PCRot. |
| [`net-proxy.md`](net-proxy.md) | Server send schedule for other players' pawns (dead reckoning), client proxy smoothing, rotation and aim smoothing, action playback, mover interpolation restart. |
| [`net-reloaded.md`](net-reloaded.md) | Reloaded.Core wrappers that touch the network or the move protocol. |
| [`ANSWERS-net.md`](ANSWERS-net.md) | Answers to open questions of `spec/net/*` for the spec owners to fold in. |

Main corrections to `spec/net/` (details in `ANSWERS-net.md`):

- The game has one send budget per driver, steered by the `CONF*` law, not one per connection.
- There is no priority sort; actors go in list order, gated by update frequency against the tick cap.
- Server calls reach non-owners only for the 20 functions with flag 0x01000000; `ChangeClientState`
  is not one of them.
- Native lists differ from the script in several places that matter for the port: no `Base` or
  later `bHidden`/vision updates from the Actor list, `Owner` to the owner only, `StaticMesh` from
  gameplay objects, `SkinState` and `FlagCarrier` always, no `iReplicatedVisionMode`.
