"""The gameplay effect classes of the script packages and every asset they draw -> shared/fx/.

The game's effects are actors of SBase and Engine classes spawned during play: Emitter subclasses (the smoke, chaff,
gas, explosion, spark, muzzle smoke and casing emitters), projectiles and placed gadgets drawn as static meshes
(grenades, sticky cams, mines), muzzle flash meshes, impact effects with their decal materials, decal projectors and
flash lights. Their behaviour is in the class defaults (shared/classes.json); what this step adds is what the class
defaults only name:

- effects.json: for every effect class, its kind and, for an Emitter, its sub-emitters: the subobjects of the class
  in its script package (SBase.SSmokeEmitter.SSmokeEmitter0...), each with its class (Engine.SpriteEmitter,
  Engine.MeshEmitter...), the properties it stores (the runtime lays them over that class's defaults from
  classes.json, as a map's fx.json does), the PNG of its Texture and the mesh of its StaticMesh. Also the asset tables:
  every texture or material an effect names (object path -> PNG and the blending the material asks for) and every
  static mesh (object path -> glTF file and node).
- textures/*.png: mip 0 of each texture (a material is resolved to the texture it draws, as the map export does).
- meshes/<Package>.glb and meshes/<Package>.materials.json: each static mesh package an effect names, exported whole
  in the map render format (SEffectStatic muzzle flashes, SGameplayObjects grenades, cams and mines, ...).
- skins: the pawn skins effects put on (SBasePawn.BeginStickySkin's Stickess shaders): each Shader's Diffuse PNG and
  its SelfIllumination / SelfIlluminationMask, a TexPanner's texture PNG with the pan (unit direction and rate).

Layout (svm.fx/1):

    {"format": "svm.fx/1",
     "classes": {"SBase.SSmokeEmitter": {"super": "Engine.Emitter", "kind": "emitter",
                 "emitters": [{"object": "SBase.SSmokeEmitter.SSmokeEmitter0", "class": "Engine.SpriteEmitter",
                               "props": {...}, "texture": "textures/sfx_smoke_grenade2.png"}],
                 "assets": {"StaticMesh": "...", "Skins": [...], ...}}},
     "textures": {"sfx.Emitter.smoke_grenade2": {"png": "textures/....png", "width": 256, "height": 256,
                                                 "material": {"additive": false, ...}}},
     "meshes": {"SEffectStatic.Muzzle.MuzzleFlash": {"glb": "meshes/SEffectStatic.glb", "node": "SEffectStatic_MuzzleFlash"}},
     "stats": {...}}

Everything here is converted from the user's install: it stays under .content/.
"""

from __future__ import annotations

import json
import shutil
import tempfile
from pathlib import Path

from .. import canonical
from ..sources.typedprops import Notes, bag_to_json, default_resolver

FORMAT = "svm.fx/1"

# Effect class families, by the class they derive from (first match names the kind).
EFFECT_BASES = (
    ("Engine.Emitter", "emitter"),
    ("Engine.Projectile", "projectile"),
    ("SBase.SMuzzleFlash", "muzzle"),
    ("Engine.SImpactEffectBase", "impact"),
    ("Engine.Projector", "projector"),
    ("SBase.SFlashLight", "light"),
    ("SBase.SFlashGen", "light"),
    ("SBase.SCoronaLight", "light"),
    ("SBase.SFireTrace", "tracer"),
    ("SBase.SMines", "mine"),
    ("SBase.SStickyCam", "gadget"),
    ("SBase.SNoiseEmitter", "gadget"),
    ("SBase.SProjMarqueurSpy", "gadget"),
    ("SBase.SBackPack", "gadget"),
    ("SBase.SGasMask", "gadget"),
    ("SBase.SLaserBeam", "beam"),
    ("SBase.STazerEmitter", "beam"),
    ("SBase.SMetalHotSpot", "decal"),
    ("SBase.SFlaque", "decal"),
)

# Shaders SBasePawn.BeginStickySkin puts on a shocked pawn (Skins 0 and 1 of the spy, 0 and 2 of the merc).
STICKY_SKINS = (
    "SPersoTextures.alpha_ATT02.Stickess_ATT02_body",
    "SPersoTextures.alpha_ATT02.Stickess_ATT02_head",
    "SPersoTextures.alpha_DEF01.Stickess_DEF01_body",
)

# Properties of an effect class whose assets are drawn (static meshes, skins, textures, materials).
# LightOn and LightOff are the skins SMineProx swaps into Skins[1] at run time (its light on and off).
ASSET_PROPS = ("StaticMesh", "Skins", "Texture", "ProjTexture", "ImpactMaterial", "MaterialHeat", "OverlayMaterial", "LightOn",
               "LightOff")

# Sub-emitter properties that name assets.
EMITTER_ASSET_PROPS = ("Texture", "StaticMesh", "TextureInThermicVisionPSX2")

STATIC_MESH_DIRS = ("StaticMeshes", "Maps")


def _safe(name: str) -> str:
    return "".join(c if c.isalnum() or c in "_-" else "_" for c in name)


def _assets_of(value) -> list[str]:
    """Object paths ({"$asset"}) inside a property value (a static array is {"$elements": {index: value}})."""
    out: list[str] = []
    if isinstance(value, dict):
        if "$asset" in value:
            out.append(value["$asset"])
        else:
            for v in value.values():
                out.extend(_assets_of(v))
    elif isinstance(value, list):
        for v in value:
            out.extend(_assets_of(v))
    return out


class _Packages:
    """Object lookup by full path (Package.Group.Name) over the install's packages."""

    def __init__(self, game: Path):
        from ..paths import import_scctpkg  # noqa: PLC0415

        import_scctpkg()
        from scctpkg.packageset import PackageSet  # noqa: PLC0415

        from ..sources.env import import_formats  # noqa: PLC0415

        self.cd = import_formats().classdefaults
        self.pset = PackageSet([Path(game) / "Packages", Path(game) / "System"])
        self._index: dict[str, dict] = {}

    def find(self, path: str):
        """(package, export) of an object path, or (None, None)."""
        if not path or "." not in path:
            return None, None
        top = path.split(".", 1)[0]
        try:
            pkg = self.pset.get(top)
        except Exception:  # an unreadable package: the object is reported missing
            return None, None
        if pkg is None:
            return None, None
        index = self._index.get(top.lower())
        if index is None:
            index = {self.cd.export_path(pkg, e).lower(): e for e in pkg.exports}
            self._index[top.lower()] = index
        e = index.get(path.lower())
        return (pkg, e) if e is not None else (pkg, None)


class _Textures:
    """Writes the PNG of each texture or material once, as textures/<Package>_<Name>.png."""

    def __init__(self, packages: _Packages, out_dir: Path):
        from scctpkg.materials import MaterialResolver  # noqa: PLC0415

        self.packages = packages
        self.resolver = MaterialResolver(packages.pset)
        self.dir = out_dir / "textures"
        self.dir.mkdir(parents=True, exist_ok=True)
        self.table: dict[str, dict | None] = {}

    def add(self, path: str) -> dict | None:
        if path in self.table:
            return self.table[path]
        entry = None
        pkg, e = self.packages.find(path)
        if e is not None and e.class_name not in ("StaticMesh", "Sound", "SoundGen", "Class", "MeshAnimation", "SkeletalMesh"):
            try:
                res = self.resolver.resolve(pkg, e.index + 1)
            except Exception:  # an unreadable material: reported missing
                res = None
            tex = res.texture if res is not None else None
            if tex is not None and tex.image is not None:
                png = f"textures/{_safe(path.split('.', 1)[0] + '_' + e.name)}.png"
                from scctpkg.export_gltf import _write_png  # noqa: PLC0415

                image = tex.image
                if res.opacity is not None and res.opacity.image is not None:
                    from scctpkg.export_gltf import _with_opacity  # noqa: PLC0415

                    image = _with_opacity(tex.image, res.opacity.image)
                _write_png(image, self.dir.parent / png)
                material = {"class": e.class_name}
                for flag in ("two_sided", "translucent", "masked", "unlit", "additive", "darken", "invisible"):
                    if getattr(res, flag, False):
                        material[flag] = True
                if res.modulate:
                    material["modulate"] = res.modulate
                if res.uv_anim:
                    material["uvAnim"] = res.uv_anim
                if res.flipbook:
                    from scctpkg.export_gltf import _flipbook_png  # noqa: PLC0415

                    flip = _flipbook_png(_safe(path.split('.', 1)[0] + '_' + e.name), res.flipbook, self.dir, {})
                    if flip:
                        material["flipbook"] = flip
                entry = {"png": png, "width": int(tex.width), "height": int(tex.height), "hasAlpha": bool(tex.has_alpha),
                         "material": material}
        self.table[path] = entry
        return entry


def _export_meshes(packages: _Packages, game: Path, wanted: set[str], out_dir: Path) -> tuple[dict, dict]:
    """Exports each static mesh package that holds a wanted mesh; returns (mesh table, per-package stats)."""
    from scctpkg import export_gltf  # noqa: PLC0415

    from ..writers.glb import pack_glb  # noqa: PLC0415

    by_package: dict[str, list[str]] = {}
    for path in sorted(wanted):
        by_package.setdefault(path.split(".", 1)[0], []).append(path)
    table: dict[str, dict] = {}
    stats: dict[str, dict] = {}
    mesh_dir = out_dir / "meshes"
    mesh_dir.mkdir(parents=True, exist_ok=True)
    for package, paths in sorted(by_package.items()):
        usx = None
        for sub in STATIC_MESH_DIRS:
            for ext in (".usx", ".sdc"):
                candidate = Path(game) / "Packages" / sub / f"{package}{ext}"
                if candidate.is_file():
                    usx = candidate
                    break
            if usx is not None:
                break
        if usx is None or usx.suffix != ".usx":
            stats[package] = {"missing": len(paths)}
            continue
        with tempfile.TemporaryDirectory(prefix="svm-fx-") as tmp:
            st = export_gltf.export_package(usx, packages.pset, Path(tmp))
            folder = Path(tmp) / package
            gltf = json.loads((folder / f"{package}.gltf").read_text(encoding="utf-8"))
            bin_path = folder / f"{package}.bin"
            binary = bin_path.read_bytes() if bin_path.exists() else b""
            scene = json.loads((folder / "scene.json").read_text(encoding="utf-8"))
            for b in gltf.get("buffers", []):
                b.pop("uri", None)
            # The glTF's image URIs are textures/<name>.png relative to the package folder: keep that layout.
            (mesh_dir / f"{package}.glb").write_bytes(pack_glb(gltf, binary))
            if (folder / "textures").is_dir():
                shutil.copytree(folder / "textures", mesh_dir / "textures", dirs_exist_ok=True)
            flags = scene.get("material_flags", {})
            materials = {m.get("name", ""): {"flags": flags.get(_safe(m.get("name", "")), {})} for m in gltf.get("materials", [])}
            doc = {"format": FORMAT, "package": package, "materials": materials,
                   "additive": scene.get("additive_materials", []), "modulate": scene.get("modulate_materials", []),
                   "failed": st.get("failed", [])}
            (mesh_dir / f"{package}.materials.json").write_bytes(canonical.dump_file(doc, ascii_keys=False))
            nodes = {n.get("name") for n in gltf.get("nodes", [])}
        found = 0
        for path in paths:
            node = _safe(f"{package}.{path.rsplit('.', 1)[-1]}")
            if node in nodes:
                table[path] = {"glb": f"meshes/{package}.glb", "node": node}
                found += 1
        stats[package] = {"meshes": st.get("meshes", 0), "wanted": len(paths), "found": found,
                          "failed": len(st.get("failed", []))}
    return table, stats


def effect_classes(db) -> dict[str, str]:
    """Effect class path -> kind, for every class of the script packages that derives from an effect base."""
    out: dict[str, str] = {}
    for path in sorted(db.classes):
        for base, kind in EFFECT_BASES:
            if db.find(base) and db.is_a(path, base):
                out[path] = kind
                break
    return out


def decode_subobject(db, packages: _Packages, path: str, notes: Notes) -> dict | None:
    """One sub-emitter of a class default (a subobject in the class's script package): its class and stored values."""
    from scctpkg.properties import skip_state_frame  # noqa: PLC0415

    pkg, e = packages.find(path)
    if e is None:
        return None
    cls = db.find(f"{e.class_package}.{e.class_name}")
    rec = db.record(cls) if cls else None
    if rec is None:
        return {"object": path, "class": f"{e.class_package}.{e.class_name}", "props": {}, "error": "unknown class"}
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    decoded = db.decoder.tagged(r, pkg, rec["_field"])
    return {"object": path, "class": cls, "props": bag_to_json(decoded, default_resolver, notes, path)}


def decode_object(db, packages: _Packages, path: str, notes: Notes) -> dict | None:
    """An object's class and stored properties (a material, a modifier), or None when it is not there."""
    return decode_subobject(db, packages, path, notes)


def skin_entry(db, packages: _Packages, textures: _Textures, path: str, notes: Notes) -> dict | None:
    """A Shader skin: its Diffuse PNG and its self-illumination layer (a TexPanner's texture and pan, or a texture)."""
    import math  # noqa: PLC0415

    shader = decode_object(db, packages, path, notes)
    if shader is None or shader["class"] != "Engine.Shader":
        return None
    props = shader["props"]
    entry: dict = {"object": path}
    diffuse = _assets_of(props.get("Diffuse"))
    if diffuse and textures.add(diffuse[0]):
        entry["diffuse"] = textures.table[diffuse[0]]["png"]
        entry["diffuseObject"] = diffuse[0]
    for prop, key in (("SelfIllumination", "selfIllum"), ("SelfIlluminationMask", "selfIllumMask")):
        refs = _assets_of(props.get(prop))
        if not refs:
            continue
        layer = decode_object(db, packages, refs[0], notes)
        target = refs[0]
        if layer is not None and layer["class"] == "Engine.TexPanner":
            inner = _assets_of(layer["props"].get("Material"))
            if inner:
                target = inner[0]
                d = layer["props"].get("PanDirection") or {}
                pitch = float(d.get("Pitch", 0)) * math.pi / 32768.0
                yaw = float(d.get("Yaw", 0)) * math.pi / 32768.0
                entry["pan"] = [round(math.cos(pitch) * math.cos(yaw), 6), round(math.cos(pitch) * math.sin(yaw), 6),
                                float(layer["props"].get("PanRate", 0.1))]
        if textures.add(target):
            entry[key] = textures.table[target]["png"]
    return entry


def build_pack(game: Path, out_dir: Path) -> dict:
    from ..sources.classdb import ClassDB  # noqa: PLC0415

    game = Path(game)
    out_dir = Path(out_dir)
    db = ClassDB(game)
    packages = _Packages(game)
    textures = _Textures(packages, out_dir)
    notes = Notes()
    classes: dict[str, dict] = {}
    meshes_wanted: set[str] = set()
    missing: set[str] = set()

    def asset(path: str) -> None:
        pkg, e = packages.find(path)
        if e is None:
            missing.add(path)
            return
        if e.class_name == "StaticMesh":
            meshes_wanted.add(path)
        elif e.class_name not in ("Sound", "SoundGen", "Class"):
            if textures.add(path) is None:
                missing.add(path)

    for path, kind in effect_classes(db).items():
        defaults = db.defaults_json(path, notes=notes)
        entry: dict = {"super": db.classes[path].get("super"), "kind": kind}
        assets: dict = {}
        for prop in ASSET_PROPS:
            if prop in defaults:
                refs = _assets_of(defaults[prop])
                if refs:
                    assets[prop] = refs if prop == "Skins" else refs[0]
                    for ref in refs:
                        asset(ref)
        if assets:
            entry["assets"] = assets
        if kind == "emitter":
            subs = []
            for ref in _assets_of(defaults.get("Emitters", [])):
                sub = decode_subobject(db, packages, ref, notes)
                if sub is None:
                    missing.add(ref)
                    continue
                for prop in EMITTER_ASSET_PROPS:
                    refs = _assets_of(sub["props"].get(prop))
                    if not refs:
                        continue
                    asset(refs[0])
                    if prop == "Texture" and textures.table.get(refs[0]):
                        sub["texture"] = textures.table[refs[0]]["png"]
                    elif prop == "StaticMesh":
                        sub["mesh"] = refs[0]
                subs.append(sub)
            entry["emitters"] = subs
        classes[path] = entry

    # Sub-emitter classes without a Texture of their own draw their class default's (Engine.S_Emitter...).
    for cls in ("Engine.SpriteEmitter", "Engine.SparkEmitter", "Engine.BeamEmitter", "Engine.MeshEmitter", "Engine.TrailEmitter"):
        if db.find(cls):
            for ref in _assets_of(db.defaults_json(cls, notes=notes).get("Texture")):
                asset(ref)

    skins: dict[str, dict] = {}
    for path in STICKY_SKINS:
        skin = skin_entry(db, packages, textures, path, notes)
        if skin is None or "diffuse" not in skin or "selfIllum" not in skin:
            missing.add(path)
            continue
        skins[path] = skin

    mesh_table, mesh_stats = _export_meshes(packages, game, meshes_wanted, out_dir)
    for path in meshes_wanted:
        if path not in mesh_table:
            missing.add(path)
    texture_table = {k: v for k, v in sorted(textures.table.items()) if v}
    stats = {
        "classes": len(classes),
        "emitterClasses": sum(1 for c in classes.values() if c["kind"] == "emitter"),
        "subEmitters": sum(len(c.get("emitters", [])) for c in classes.values()),
        "textures": len(texture_table),
        "meshes": len(mesh_table),
        "missing": len(missing),
        "skins": len(skins),
        "kinds": dict(sorted({k: sum(1 for c in classes.values() if c["kind"] == k) for k in {c["kind"] for c in classes.values()}}.items())),
        "subEmitterClasses": dict(sorted(_count(s["class"] for c in classes.values() for s in c.get("emitters", [])).items())),
        "meshPackages": mesh_stats,
    }
    doc = {"format": FORMAT, "classes": classes, "skins": skins, "textures": texture_table, "meshes": dict(sorted(mesh_table.items())),
           "missing": sorted(missing), "stats": stats}
    (out_dir / "effects.json").write_bytes(canonical.dump_file(doc, ascii_keys=False))
    return {k: v for k, v in stats.items() if not isinstance(v, dict)} | {"subEmitterClasses": stats["subEmitterClasses"]}


def _count(items) -> dict:
    out: dict = {}
    for i in items:
        out[i] = out.get(i, 0) + 1
    return out
