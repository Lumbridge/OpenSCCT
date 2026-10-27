"""renderpack step: client render assets that every map shares (shared/render/).

- textures/*.png: mip 0 of the engine and gadget textures the renderer draws with directly: the thermal lookup
  table, the grain, the sonar backdrop, the EMF environment map, the torch cookie, the attenuation ramps and the
  torch corona (TEXTURES below).
- vision.json: every HeatTextureModifier, SpecialVisionTextureModifier and EMFTextureModifier in the texture
  packages, keyed by object path (Package.Group.Name), with the heat and EMF textures it names (exported to
  textures/) and its flags after class defaults. The same entries are also keyed by the material at the bottom of the
  modifier chain (and a Shader's Diffuse), so a mesh whose converted material names that base finds them.
- gameplay/gameplay.glb, gameplay/materials.json, gameplay/textures/: the static meshes of SGameplayObjects.usx
  (class-default meshes of gameplay actors: first-aid cabinets, window panes, extinguishers, laser beams) in the map
  render format (materials.json like a map's, keyed by glTF material name).
- mips.json: the number of mip levels every Texture of the game stores (texture, static mesh and map packages), by
  object path: most world textures store one level, and the original draws those without mipmaps.
- render.json: format, the texture table (object path -> PNG), the gameplay mesh table (object path -> glTF node).

Everything here is converted from the user's install: it stays under .content/.
"""

from __future__ import annotations

import json
import shutil
import tempfile
from pathlib import Path

from .. import canonical
from .. import gamefiles
from ..graph import Context, Step, StepOutput
from ..sources import env

FORMAT = "svm.renderpack/1"

# Object path -> what the renderer uses it for (documentation only; the renderer looks textures up by path).
TEXTURES = {
    "Engine.heat_and_attenuation_2d": "thermal lookup table (LevelInfo.pThermalTexture_A)",
    "Engine.Noise0_gauss": "vision grain (LevelInfo.pNoise0Gauss)",
    "Engine.sonar_background": "motion and EMF backdrop (LevelInfo.pSoundVisionBackground)",
    "Echelon.EMFInvDarkBlue": "EMF environment map (LevelInfo.pEMFTexture)",
    "Echelon.flashlight": "merc torch cookie (Echelon.Torche ProjectedLightingTexture)",
    "Echelon.spot_bw": "default spot shadow cookie (DefaultEchelonLightingMaterial)",
    "Engine.linear_attenuation": "Echelon light attenuation, linear",
    "Engine.late_attenuation": "Echelon light attenuation, late",
    "Engine.Omni_Atten_2D": "heat source attenuation (LevelInfo.pOmniAtten2D)",
    "sfx.def_flare": "merc torch corona (SGadTorche Skins[0])",
    "sfx.gg_heat_64": "alternative thermal table",
    # The first frame comes with the vision modifiers (FX_pose_bomb_heat's heat and EMF texture).
    "SGameplayObjectsTex.FX_pose_bomb_2": "unarmed bomb spot ghost, second frame (FX_pose_bomb AnimNext)",
}

VISION_CLASSES = ("HeatTextureModifier", "SpecialVisionTextureModifier", "EMFTextureModifier")
# Class defaults of the modifiers (Engine.u): only SpecialVisionTextureModifier changes them.
VISION_DEFAULTS = {
    "HeatTextureModifier": {"bUseTextureAsHeat": False, "HeatTextureType": 0, "EMFIsOn": True,
                            "bInvertEMFTexture": False},
    "SpecialVisionTextureModifier": {"bUseTextureAsHeat": True, "HeatTextureType": 1, "EMFIsOn": True,
                                     "bInvertEMFTexture": True},
    "EMFTextureModifier": {"bUseTextureAsHeat": False, "HeatTextureType": 0, "EMFIsOn": True,
                           "bInvertEMFTexture": False},
}
GAMEPLAY_PACKAGE = "SGameplayObjects"


def _safe(name: str) -> str:
    return "".join(c if c.isalnum() or c in "_-" else "_" for c in name)


class RenderPackStep(Step):
    name = "renderpack"
    version = 1
    deps = ()
    per_map = False
    needs_game = True
    description = "client render pack: vision and light textures, vision modifiers, gameplay object meshes (shared/render/)."

    def extra_code(self) -> list[Path]:
        return env.code_files()

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {"textures": sorted(TEXTURES), "gameplay": GAMEPLAY_PACKAGE}

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        out = {}
        for f in _mip_packages(ctx.game):
            out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        usx = ctx.game / "Packages" / "StaticMeshes" / f"{GAMEPLAY_PACKAGE}.usx"
        if usx.is_file():
            out[ctx.game_path(usx)] = ctx.hasher.sha256(usx)
        for f in env.script_package_files(ctx.game):
            out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        return out

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "shared" / "render"

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None
        stats = build_pack(ctx.game, out_dir)
        return StepOutput(stats=stats)


class _Exporter:
    """Writes textures once each, as textures/<Package>_<Name>.png, and remembers the object path of each."""

    def __init__(self, pset, out_dir: Path):
        self.pset = pset
        self.dir = out_dir / "textures"
        self.dir.mkdir(parents=True, exist_ok=True)
        self.by_path: dict[str, str | None] = {}

    def texture(self, pkg, export) -> str | None:
        from scctpkg.texture import read_texture  # noqa: PLC0415

        path = _object_path(pkg, export)
        if path in self.by_path:
            return self.by_path[path]
        uri = None
        if export.class_name == "Texture":
            try:
                tex = read_texture(pkg, export)
            except Exception:  # an undecodable texture is reported as missing, not fatal
                tex = None
            if tex is not None and tex.image is not None:
                uri = f"textures/{_safe(pkg.path.stem + '_' + export.name)}.png"
                tex.image.save(self.dir.parent / uri, optimize=False)
        self.by_path[path] = uri
        return uri


def _object_path(pkg, export) -> str:
    name = pkg.full_name(export.index + 1)
    return name if name.split(".", 1)[0].lower() == pkg.path.stem.lower() else f"{pkg.path.stem}.{name}"


def _resolve(pset, pkg, ref):
    if not isinstance(ref, int) or ref == 0:
        return None, None
    owner, export = pset.resolve(pkg, ref)
    return (owner, export) if owner is not None and export is not None else (None, None)


def _props(pkg, export) -> dict:
    from scctpkg.properties import read_properties, skip_state_frame  # noqa: PLC0415

    r = pkg.export_reader(export)
    skip_state_frame(r, export.flags)
    return read_properties(r)


def build_pack(game: Path, out_dir: Path) -> dict:
    from ..paths import import_scctpkg  # noqa: PLC0415

    import_scctpkg()
    from scctpkg.packageset import PackageSet  # noqa: PLC0415

    game = Path(game)
    pset = PackageSet(gamefiles.package_roots(game, "System"))
    tex = _Exporter(pset, out_dir)
    stats: dict = {}

    # ---- named textures -------------------------------------------------------------------------
    table: dict[str, str | None] = {}
    for path in sorted(TEXTURES):
        pkg_name, name = path.split(".", 1)
        pkg = pset.get(pkg_name)
        export = pkg.find_export(name, "Texture") if pkg is not None else None
        table[path] = tex.texture(pkg, export) if export is not None else None
    stats["textures"] = sum(1 for v in table.values() if v)
    missing = sorted(k for k, v in table.items() if not v)
    if missing:
        stats["missingTextures"] = missing

    # ---- vision modifiers -----------------------------------------------------------------------
    vision, bases = _vision_index(pset, tex, game)
    stats["visionModifiers"] = len(vision)
    stats["visionBases"] = len(bases)
    (out_dir / "vision.json").write_bytes(canonical.dump_file({"format": FORMAT, "modifiers": vision,
                                                                "bases": bases}, ascii_keys=False))

    # ---- stored mip levels ----------------------------------------------------------------------
    mips = _mip_index(pset, game)
    stats["mipTextures"] = len(mips)
    stats["singleLevel"] = sum(1 for v in mips.values() if v <= 1)
    (out_dir / "mips.json").write_bytes(canonical.dump_file({"format": FORMAT, "mips": mips}, ascii_keys=False))

    # ---- gameplay object meshes -----------------------------------------------------------------
    meshes = _gameplay_meshes(pset, game, out_dir / "gameplay")
    stats["gameplayMeshes"] = len(meshes)

    doc = {"format": FORMAT, "textures": table, "gameplayMeshes": meshes,
           "notes": {k: TEXTURES[k] for k in sorted(TEXTURES)}}
    (out_dir / "render.json").write_bytes(canonical.dump_file(doc, ascii_keys=False))
    stats["pngs"] = sum(1 for v in tex.by_path.values() if v)
    return stats


def _vision_index(pset, tex: _Exporter, game: Path) -> tuple[dict, dict]:
    """Every vision modifier in the texture packages, and the same entries keyed by the base material they wrap."""
    from scctpkg.package import PackageError  # noqa: PLC0415

    modifiers: dict[str, dict] = {}
    raw: dict[str, tuple] = {}
    for utx in gamefiles.files(game, "Packages/Textures", "*.utx"):
        try:
            pkg = pset.get(utx.stem)
        except PackageError:
            continue
        if pkg is None:
            continue
        for export in pkg.exports_of_class(*VISION_CLASSES):
            try:
                props = _props(pkg, export)
            except Exception:  # unreadable property block: skip the object
                continue
            raw[_object_path(pkg, export)] = (pkg, export, props)

    def texture_entry(pkg, ref):
        owner, export = _resolve(pset, pkg, ref)
        if export is None:
            return None, None
        if export.class_name != "Texture":
            return None, export.class_name
        return tex.texture(owner, export), "Texture"

    for path, (pkg, export, props) in sorted(raw.items()):
        flags = dict(VISION_DEFAULTS[export.class_name])
        for key in flags:
            if key in props:
                flags[key] = bool(props[key]) if isinstance(flags[key], bool) else int(props[key] or 0)
        entry: dict = {"class": export.class_name}
        if export.class_name in ("HeatTextureModifier", "SpecialVisionTextureModifier"):
            uri, kind = texture_entry(pkg, props.get("HeatTexture", 0))
            entry["heat"] = {"texture": uri, "kind": kind, "useTexture": flags["bUseTextureAsHeat"],
                             "type": flags["HeatTextureType"]}
        if export.class_name in ("EMFTextureModifier", "SpecialVisionTextureModifier"):
            uri, kind = texture_entry(pkg, props.get("EMFTexture", 0))
            entry["emf"] = {"texture": uri, "kind": kind, "invert": flags["bInvertEMFTexture"],
                            "on": flags["EMFIsOn"]}
        owner, inner = _resolve(pset, pkg, props.get("Material", 0))
        entry["material"] = _object_path(owner, inner) if inner is not None else None
        modifiers[path] = entry

    # Chains: a modifier's Material may be another modifier; the outer one's heat and EMF win, the inner fills gaps.
    def merged(path, depth=0):
        entry = modifiers.get(path)
        if entry is None or depth > 8:
            return {}, path
        inner, base = merged(entry["material"], depth + 1) if entry.get("material") in modifiers else ({}, entry.get("material"))
        out = dict(inner)
        for k in ("heat", "emf"):
            if k in entry:
                out[k] = entry[k]
        return out, base

    bases: dict[str, dict] = {}
    for path in sorted(modifiers):
        info, base = merged(path)
        modifiers[path] = {**modifiers[path], **{k: v for k, v in info.items() if k not in modifiers[path]}}
        if not base:
            continue
        keys = [base]
        diffuse = _shader_diffuse(pset, base)
        if diffuse:
            keys.append(diffuse)
        score = sum(1 for k in ("heat", "emf") if k in info)
        for key in keys:
            prev = bases.get(key)
            if prev is None or score > sum(1 for k in ("heat", "emf") if k in prev):
                bases[key] = {k: info[k] for k in ("heat", "emf") if k in info} | {"modifier": path}
    return modifiers, bases


def _mip_packages(game: Path) -> list[Path]:
    # The downloaded maps' packages too: their textures' stored mip levels and vision modifiers.
    root = "Packages/"
    files = gamefiles.files(game, root + "Textures", "*.utx") + gamefiles.files(game, root + "StaticMeshes", "*.usx")
    return files + gamefiles.files(game, root + "Maps", "*.sdc")


def _mip_index(pset, game: Path) -> dict[str, int]:
    """Stored mip levels of every Texture export (the count before the first level's data)."""
    from scctpkg.package import Package, PackageError  # noqa: PLC0415

    out: dict[str, int] = {}
    for f in _mip_packages(game):
        try:
            pkg = Package.load(f)
        except PackageError:
            continue
        for export in pkg.exports_of_class("Texture"):
            try:
                r = pkg.export_reader(export)
                from scctpkg.properties import read_properties, skip_state_frame  # noqa: PLC0415

                skip_state_frame(r, export.flags)
                read_properties(r)
                count = r.compact()
            except Exception:  # unreadable header: leave the texture out
                continue
            if 0 < count <= 32:
                out.setdefault(_object_path(pkg, export), count)
    return dict(sorted(out.items()))


def _shader_diffuse(pset, path: str) -> str | None:
    """Object path of a Shader's Diffuse, so a mesh material named after the diffuse texture finds its modifier."""
    pkg_name = path.split(".", 1)[0]
    pkg = pset.get(pkg_name)
    if pkg is None:
        return None
    name = path.rsplit(".", 1)[-1]
    export = pkg.find_export(name, "Shader")
    if export is None:
        return None
    try:
        props = _props(pkg, export)
    except Exception:  # unreadable shader: no alias
        return None
    owner, diffuse = _resolve(pset, pkg, props.get("Diffuse", 0))
    return _object_path(owner, diffuse) if diffuse is not None else None


def _gameplay_meshes(pset, game: Path, out_dir: Path) -> dict:
    from scctpkg import export_gltf  # noqa: PLC0415

    from ..writers.glb import pack_glb  # noqa: PLC0415

    usx = game / "Packages" / "StaticMeshes" / f"{GAMEPLAY_PACKAGE}.usx"
    if not usx.is_file():
        return {}
    out_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="svm-renderpack-") as tmp:
        st = export_gltf.export_package(usx, pset, Path(tmp))
        folder = Path(tmp) / GAMEPLAY_PACKAGE
        gltf = json.loads((folder / f"{GAMEPLAY_PACKAGE}.gltf").read_text(encoding="utf-8"))
        binary = (folder / f"{GAMEPLAY_PACKAGE}.bin").read_bytes() if (folder / f"{GAMEPLAY_PACKAGE}.bin").exists() else b""
        scene = json.loads((folder / "scene.json").read_text(encoding="utf-8"))
        for b in gltf.get("buffers", []):
            b.pop("uri", None)
        (out_dir / "gameplay.glb").write_bytes(pack_glb(gltf, binary))
        if (folder / "textures").is_dir():
            shutil.copytree(folder / "textures", out_dir / "textures", dirs_exist_ok=True)
        flags = scene.get("material_flags", {})
        materials = {}
        for m in gltf.get("materials", []):
            name = m.get("name", "")
            materials[name] = {"flags": flags.get(_safe(name), {})}
        doc = {"format": FORMAT, "package": GAMEPLAY_PACKAGE, "materials": materials,
               "additive": scene.get("additive_materials", []), "modulate": scene.get("modulate_materials", []),
               "failed": st.get("failed", [])}
        (out_dir / "materials.json").write_bytes(canonical.dump_file(doc, ascii_keys=False))
        nodes = {n.get("name") for n in gltf.get("nodes", [])}

    pkg = pset.get(GAMEPLAY_PACKAGE)
    meshes = {}
    for export in pkg.exports_of_class("StaticMesh"):
        node = _safe(f"{GAMEPLAY_PACKAGE}.{export.name}")
        if node in nodes:
            meshes[_object_path(pkg, export)] = node
    return dict(sorted(meshes.items()))
