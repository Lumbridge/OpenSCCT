# Map package layout (UModel and the cook)

What the original writes into a map package (`.sdc`, package version 300) for the level's BSP model, and how the
cook that turns an editor copy into a shipped map chooses what to keep. The converter's `scctpkg` model reader
follows this layout. [Lightmaps](../../docs/LIGHTMAPS.md) describes how the lighting stored here was computed.

Evidence: addresses are in the stock `ChaosTheory_Editor.exe` (image base 0x10e00000; the `editor` entry of
[re/binaries.lock.json](../../re/binaries.lock.json)). **Decoded** from that binary, then **measured**:

- Every Model export of the 572 distinct map packages of the installs we hold (shipped `Maps/` and editor `MapsEd/`
  copies) parses to its last byte in exactly one of the two layouts below, editor or cooked.
- The cooked layout alone parses all 3,346 Model exports of the 83 cooked Beta 3 packages (`Packages/Maps/*.sdc`:
  81 maps plus Entry and Lobby) to the last byte.

## Editor and cooked layout

1. Whether a package is written in the editor layout or the cooked layout depends on a run-time save target, the
   dword at offset 0x80 of the object at `*(0x11691D7C)` (written as `mov dword ptr [edx + 0x80], 0` at 0x10ee4f37):
   0 is the editor layout, 1 to 3 are cooked targets. Nothing
   in the file records it; a reader has to know where the package came from (decoded).
2. One editor Save writes both copies: `MapsEd/` with target 0, then `Maps/` with target 2 (0x10ee4d30, which
   calls 0x10ee4750) (decoded).
3. The two layouts differ in two places: what follows each FBspSurf
   ([BSP surfaces and polygons](#bsp-surfaces-and-polygons)) and an editor-only LightMaps array in the UModel tail
   ([UModel](#umodel)). Everything else in a Model export is the same in both.
4. Only editor autosaves are multi-chunk zlib containers, in chunks of 15,728,640 bytes. Shipped maps and editor
   copies are plain packages (measured).

## UModel

Order of a Model export (`UModel::Serialize`, 0x110D20E0):

```text
tagged properties (None-terminated)
FBox BoundingBox, FSphere BoundingSphere
Vectors, Points                    compact n, n x f32[3]
Nodes                              compact n, n x FBspNode
Surfs                              compact n, n x FBspSurf
Verts                              compact n, each compact pVertex, compact iSide
i32 NumSharedSides
i32 NumZones, NumZones x FZoneProperties
Polys                              object reference
Bounds                             compact n, n x FBox
LeafHulls                          compact n, n x i32
Leaves                             compact n, n x FLeaf
Lights                             compact n, n x light object reference
i32 RootOutside, i32 Linked
Sections                           compact n, n x FBspSection
LightMaps (editor layout only)     compact n, n x FLightMap
LightMapTextures                   compact n, n x FLightMapTexture
```

`compact` is the package's compact index. [SVMap collision](../svmap/1.1/collision.md) describes how the converter
uses the nodes and LeafHulls.

### FBspNode

`FBspNode` serialisation is at 0x110cdc80.

- **NodeFlags keeps only bits 0x1F on load** (the mask is applied at 0x110ce08a). Bit 0x20 is NF_IsNew; 0x40 and
  0x80 are scratch bits of the BSP filter. None of the three survives a load.
- The node's second FSphere is written from an uninitialised stack value; its bytes carry no meaning.
- Measured: no node carries any bit of 0xE0, neither the 178,101 nodes of the 83 cooked Beta 3 packages nor the 163,547
  of the 84 shipped world models. Bit 0x01 (NF_NotCsg) is set on 10,004 nodes and 0x04 on 9,169. 0x04 is
  NF_NotVisBlocking in stock Unreal Engine 2; that meaning is not confirmed for SCCT.
- Because 0x20 is masked off on load, the line check's "not a CSG node" test (`K.Bsp.NotCsgFlags` 0x21 in
  [K.Natives.cs](../../src/OpenScct.Physics/K.Natives.cs), [Physics](../../docs/PHYSICS.md#lines-against-the-world-bsp-and-static-meshes-decoded))
  is decided by NF_NotCsg alone on every loaded map.

### FZoneProperties

On disk: object reference ZoneActor, u64 Connectivity, f32 LastRenderTime.

### BSP surfaces and polygons

`FBspSurf` and `FPoly` both end with two lightmap scales: `LightMapScalePSX2`, then `LightMapScaleXBOX`. The second
is the one the PC build uses for its lightmap charts ([Lightmaps](../../docs/LIGHTMAPS.md#charts-and-atlases)). The
editor's surface properties page has `OnLightMapScaleChangedPSX2` and `OnLightMapScaleChangedXBOX` handlers
(`WPagePanRotScale`) and no PC variant.

What follows a surface's fixed fields depends on the layout (FBspSurf 0x110cd830):

- Editor layout: every surface is followed by its source brush face, an `FPoly` in world space, then an object
  reference Actor (the brush) and a compact iBrushPoly.
- Cooked layout: a surface is followed by an `FPoly` only when its PolyFlags has 0x0C000000 set (portal 0x04000000
  or mirror 0x08000000); other surfaces have nothing after the two scales.

An `FPoly` (0x110cbdf0) is compact NumVertices, f32[3] Base, Normal, TextureU, TextureV, NumVertices x f32[3],
u32 PolyFlags, object reference Actor, object reference Material, name ItemName, compact iLink, compact iBrushPoly,
f32 LightMapScalePSX2, f32 LightMapScaleXBOX.

Measured on the 111,460 surfaces of the 83 cooked Beta 3 packages: the two scales differ on 28,880, for example 32
against 256.

### FLeaf and the light list

- On disk an `FLeaf` is compact iZone, compact iPermeating, compact iVolumetric (0x110cb700). iVolumetric is read
  and discarded; in every shipped leaf it equals iPermeating. In memory a leaf is two shorts, iZone and iPermeating.
- `UModel.Lights` (Model+0xc8) holds one run of light references per leaf, starting at the leaf's iPermeating and
  ending at a None entry. The visibility build writes it (UnVisi.cpp light test, 0x1101dcb7). The lightmap bake
  takes a surface's lights from these runs, and run-time actor lighting reads them at 0x111a7300.

### FBspSection

A section is its vertex list (compact count, then 40-byte vertices: f32[3] position, f32[3] normal, f32 U, V,
f32 lightmap U, V) followed by the trailer (0x110cd3f0): i32, object reference Material, i32, u32 PolyFlags,
i32 iLightMapTexture.

### FLightMap (editor layout only)

An editor copy keeps the per-surface lightmap records between Sections and LightMapTextures; the cook drops them.
Each is 7 compact indices, an FMatrix (64 bytes), 3 FVectors, then compact k and k x {object reference Light,
compact b, b bytes, 7 x i32}, then an object reference and an i32.

### FLightMapTexture

0x110d0a80 and 0x110d0a10: object reference, TArray of i32 lightmap ids, u64, i32, two TLazyArray of u8, u8 Format,
i32 Width, i32 Height, i32. Shipped atlases are RGBA8, 512 x 512, or 256 x 256 where `CompressLightmaps` halved
them ([Lightmaps](../../docs/LIGHTMAPS.md#charts-and-atlases)).

## Cooked actor filter

The cook (0x10ee13b0) decides per actor whether it reaches the shipped map (0x10EDD1E0, with helpers 0x10EDACF0
and 0x10EDB890). It tests LightType before the brush and volume test, so an actor of any class whose LightType
passes is handled as a light. Which lights it keeps is listed in
[Lightmaps, "The cook drops lights"](../../docs/LIGHTMAPS.md#the-cook-drops-lights).

A light the filter drops but another object references is still written, as an orphan export outside
`Level.Actors` (measured: 824 in 168 cooked files).
