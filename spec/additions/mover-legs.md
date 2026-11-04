# Mover legs

OpenSCCT addition. A map can give each mover key its own travel time and easing: a platform that races along its
keys and crawls into the last one, a door that swings fast and settles slowly. The game's mover
([mover](../devices/mover.md)) has one `MoveTime` for every leg and one `MoverGlideType` for every key. The data is the map's `sim/movers.json`
([SVMap 1.2](../svmap/1.2/movers.md)); a mover the file does not list moves exactly as the game's.

Code: `src/OpenScct.Core/Engine/MoverLegs.cs` (the timing and the curves), `src/OpenScct.Core/Engine/Mover.Legs.cs`
(the hooks in the mover), `src/OpenScct.Content/Movers/MoversDoc.cs` (the file), `MapLoader` (gives each mover its
timing at load).

## Legs

A leg is the motion between two neighbouring keys, `n - 1` and `n`. It belongs to key `n`, and runs either way:

1. When `InterpolateTo` starts a leg between neighbouring keys and key `n` has a `time`, the leg takes that time
   instead of the one asked for (`MoveTime`, a special behaviour's `MoveTime`, or an `SLift`'s single-key move). This
   holds on a mover whose `MoveTime` is 0 too: its other legs stay jumps, the timed ones travel.
2. A move to the key the mover is at (the match-size rules' hold) is not a leg between neighbours, so it keeps the 0
   seconds asked for and stays a jump.
3. A leg between keys that are not neighbours keeps the time asked for. An `SLift` crossing several floors moves
   straight to the destination key in one leg of `MoveTime` x floors, so per-key times shape only its one-floor
   trips.
4. A leg reversed half way (the reversal in `InterpolateTo`) is the same leg, so it keeps its time.

## Easing

Each key is smooth or not: its own `smooth`, else the mover's glide (`MV_GlideByTime` is smooth). A leg eases where it
touches a smooth key. With `a` the share of the leg's time gone:

| Start key | End key | Position along the leg |
|---|---|---|
| smooth | smooth | `3a^2 - 2a^3` (the game's glide) |
| smooth | not | `a^2` (speeds up from rest) |
| not | smooth | `a(2 - a)` (slows to rest) |
| not | not | `a` (constant speed) |

The start key is the key the leg leaves (`PrevKeyNum`), the end key its target (`KeyNum`). Each shape run backwards is
the shape with its ends swapped, so a leg reversed half way carries on from where the mover is. A mover with every key
smooth, or every key not, moves exactly as the game's glide or constant speed.

## Network

Nothing new is replicated. Every client reads the same `sim/movers.json` (it is in the sim hash, so client and server
agree on it). When `SimInterpolate` starts a leg, the client takes the two keys it names (`.Z`), and when they are
neighbours and the later key has a `time`, the client runs the leg at that time and not at the replicated rate
(`.Y`, rate x 100): a vector goes out rounded to whole numbers, so the replicated rate of a 30 s leg would make it
last 33.3 s on the client. Other legs keep the replicated rate, as in the game. The easing comes from the same two
keys. A client that carries a leg on to the next key itself (`KeyFrameReached`) uses the same file, as the server
does.

Bots waiting at a door (`DoorAi`) allow for the door's longest leg, its own time or `MoveTime`.

## Export to `.sdc`

The original game and RE+ have no per-key timing. An exporter keeps the mover's own `MoveTime` and glide for every
leg and reports the movers whose keys it flattened.
