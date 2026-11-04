# Mover legs

OpenSCCT addition. A map can give each mover key its own travel time and easing: a lift that crawls into a floor and
races between floors, a door that swings fast and settles slowly. The game's mover ([mover](../devices/mover.md)) has
one `MoveTime` for every leg and one `MoverGlideType` for every key. The data is the map's `sim/movers.json`
([SVMap 1.2](../svmap/1.2/movers.md)); a mover the file does not list moves exactly as the game's.

Code: `src/OpenScct.Core/Engine/MoverLegs.cs` (the timing and the curves), `src/OpenScct.Core/Engine/Mover.Legs.cs`
(the hooks in the mover), `src/OpenScct.Content/Movers/MoversDoc.cs` (the file), `MapLoader` (gives each mover its
timing at load).

## Legs

A leg is the motion between two neighbouring keys, `n - 1` and `n`. It belongs to key `n`, and runs either way:

1. When `InterpolateTo` starts a leg between neighbouring keys and key `n` has a `time`, the leg takes that time
   instead of the one asked for (`MoveTime`, a special behaviour's `MoveTime`, or an `SLift`'s single-key move).
2. A leg of 0 seconds (a jump to a key, as the match-size rules use to hold a mover) stays a jump.
3. A leg between keys that are not neighbours (an `SLift` crossing several floors, or a move to the key the mover is
   at) keeps the time asked for.
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

Nothing new is replicated. The server's leg rate already reaches clients in `SimInterpolate` (rate x 100), so a leg's
own time reaches them with it. Every client reads the same `sim/movers.json` (it is in the sim hash, so client and
server agree on it) and picks the easing from the two keys `SimInterpolate` names. A client that carries a leg on to
the next key itself (`KeyFrameReached`) uses the same file, as the server does.

## Export to `.sdc`

The original game and RE+ have no per-key timing. An exporter keeps the mover's own `MoveTime` and glide for every
leg and reports the movers whose keys it flattened.
