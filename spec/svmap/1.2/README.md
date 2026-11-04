# SVMap 1.2

A minor version of [SVMap 1.1](../1.1/README.md): every 1.1 file reads as before, and a 1.1 reader can load a 1.2 map.
It cannot play one as authored, though: `sim/match.json` changes the match, and a server that does not read it plays
every actor at every player count. Hosts serve 1.2 maps on builds that read 1.2.
Everything [1.1](../1.1/README.md) says holds unless this page says otherwise; the conventions are those of
[1.1](../1.1/conventions.md) and the collision layout is [1.1's](../1.1/collision.md).

```
<Map>/
  sim/
    match.json             (1.2, optional) player-count rules on actors and win targets by team size
  render/
    outfits.json           (1.2, optional) the teams' slot textures, replacement models and cosmetics policy
    chars/<Map>/           (1.2) also the outfit models the map ships
```

A map declares `formatVersion` `1.2.x` in `manifest.json`, `sim/gameplay.json` and `sim/traversal.json` when it has
one of the new files; converters keep writing 1.1 for maps without them. Schemas live in [schemas/](schemas/) with
`$id`s under `https://svmap.invalid/1.2/`; they are the 1.1 schemas with the new version.

## Documents

| File | Content |
|---|---|
| [match.md](match.md) | `sim/match.json`: rules, holds, win targets, how RE+ `SMatchSizeRules` converts |
| [outfits.md](outfits.md) | `render/outfits.json`: slots, models, goggles, cosmetics policy, how RE+ `SCharacterOutfits` converts |
| [CHANGELOG.md](CHANGELOG.md) | format history |

## Classes a map defines (`sim/classes.json`)

A map package may carry its own UnrealScript classes (RE+ tools compile into the map, map makers added scripts).
Converters decode them from the package itself, so their actors are written with full props like any other. Their
entries in `sim/classes.json` carry `"mapScript": true` and their defaults like any other class. OpenSCCT loads such
an actor as an inert actor (`UnportedActor`) that keeps its class path, its record with every prop and its class
defaults; none of its script runs. The RE+ families with a native form are not written as actors:
`SMatchSizeRules` becomes `sim/match.json`, `SCharacterOutfits` becomes `render/outfits.json`.
