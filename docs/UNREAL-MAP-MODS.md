# Custom maps for OpenSCCT Unreal

The Windows launcher can enable one local collection of compiled SVMaps. A collection can hold several maps. The Unreal game reads the collection in place each time it starts.

## Set up a collection

1. Build a map with [OpenSCCT Editor](https://github.com/OpenSCCT/OpenSCCT-Editor), or unpack a compiled map from its author.
2. Put each map in a folder named for its map ID. Give custom maps unique IDs so they can appear alongside the maps installed with the game.
3. In the launcher's **Mods** tab, select the Unreal edition, choose the folder containing the map folders, and enable the collection.
4. Start Unreal and open **Play offline** to select the map. To return to the standard map list, close the game, disable the collection, and start it again.

For example, choose `My Maps` for this collection.

```text
My Maps/
  Training/
    manifest.json
    sim/
      collision.bin
      gameplay.json
      traversal.json
    render/
      world.glb
```

Keep all other files the map build produced. The manifest's `map` field must match the folder name, up to 31 characters. The offline menu lists authored or converted Versus maps with a playable mode. Include player starts for both teams. The launcher checks the SVMap 1.x identity and required file layout and rejects collisions with installed game maps. The game checks simulation data when it opens the map. Multiplayer requires matching map data on the server and clients.

The launcher passes the collection as a quoted `--svm-maps` argument to the game. Disabling it removes the argument on the next launch. A game update can use the same collection because it lives outside the installed release. The format is documented in the [SVMap specification](../spec/svmap/1.0/README.md).
