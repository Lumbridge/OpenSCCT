"""The ui step's work: Magma packages, their atlases, the Magma fonts and strings into shared/ui/.

    shared/ui/manifest.json                 svm.ui/1: what was written and what was missing
    shared/ui/magma/<Package>.json          svm.magma/1 (mgm.py), one per Menus/<Package>.mgm
    shared/ui/textures/<Package>/<Tex>.png  atlases the package's materials use, from Packages/Textures/<Package>_mgm.utx
    shared/ui/textures/SHudTextures/*.png   the SHud canvas atlases the HUD draws outside Magma (ER markers, icons)
    shared/ui/fonts/<Font>.json, <Font>_<n>.png   svm.font/1 glyph tables and flipped atlas pages (mft.py)
    shared/ui/strings.json                  System/_PC_/Magma.int, MagmaDynamic.int, Engine.int, GUI.int (strings.py)

Everything here is game-derived and stays under .content/.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from .. import canonical
from . import mft, mgm, strings

UI_FORMAT = "svm.ui/1"
FONT_FORMAT = "svm.font/1"

# Magma packages of Menus/: the in-game pages (Hud), the gadget icon materials (Options) and the shared popups (common).
PACKAGES = ("Hud", "Options", "common")
# SHud canvas atlases: Tex_ER (MN_boutons: ER markers, brackets, GPO diamond), the gadget icon fallback, the
# weapon icons of the deathmatch respawn weapon (Tex_All_Weapons) and the spy binoculars' objective markers
# (Tex_BinocularA_Arrow, Tex_BinocularA_All); the merc motion tracker's lock corner, sweep and friend marker
# (SMotionTrackingVision cadre, balayage, TexLaserSentry).
SHUD_TEXTURES = ("MN_boutons", "Icones_Gadgets", "Icones_Weapons", "Att_Arrow", "BinocularA_all", "Vision_lock",
                 "balayage_H", "Vision_friend")
# The PC menus' own textures (GUI pages, not Magma): the page background, its corner brackets and frames
# (GUIContent New_Menu; GUIPage defaults), and the PC styles' buttons, boxes and the voice icon (Menu_PC; sSTY_Pc*);
# the equipment pages' background, side portraits and pad-button hints (Edit_Profile: fond02, MN_ATTperso,
# MN_DEFperso, bouton01-04); the strategic map's markers (StrategicMapMenu YouAreHereTexRotator and TeamMate turn
# YouAreHereTexPC and ButtonWhite); the tutorials' pictures (Tutorials_PC: the tips' SPY_ / SENTRY_Tuto01-09
# (Tutorials_Tuto), the HUD legends' screenshots (View_Hud_Spy, View_Hud_Sentry) and the keyboard (View_KeyboardConfigOne)).
MENU_TEXTURES = {
    "GUIContent": ("fond", "loading", "MN_cadre01", "MN_cadre02", "MN_cadre03", "MN_cadre06", "MN_losange",
                   "MN_petitrond", "fond02", "MN_ATTperso", "MN_DEFperso"),
    "Menu_PC": ("buton_select", "buton_validate", "buton_push", "TABS_NOselect", "Saquare_backd", "Saquare_BOX",
                "CommunicatorON", "Disable", "scroll_bar", "Vert01", "noir01",
                "bouton01", "bouton02", "bouton03", "bouton04", "YouAreHereTexPC", "ButtonWhite"),
    "Tutorials_PC": tuple(f"{side}_Tuto{i:02d}" for side in ("SPY", "SENTRY") for i in range(1, 10)) +
                    ("SPY_Tutorial2_User_Interface", "SENTRY_Tutorial2_User_Interface", "clavier"),
}
# Magma.int and MagmaDynamic.int: the pages' texts; Engine.int: the key names ([BaseGUIController]
# LocalizedKeyName[n] by EInputKey) the pages' key help and tokens show; GUI.int: the menus' texts, among them the
# English names of the key bindings ([Browser_Keys] KeyLabel[n], [ExtraKeyRows]) the options show instead of the
# layout's command lines (whose aliases are French: AliasHaut, AliasGauche...).
STRING_FILES = ("Magma", "MagmaDynamic", "Engine", "GUI")


@dataclass
class Sources:
    game: Path

    def mgm(self, package: str) -> Path:
        return self.game / "Menus" / f"{package}.mgm"

    def texture_package(self, package: str) -> Path:
        return self.game / "Packages" / "Textures" / f"{package}_mgm.utx"

    def menu_textures(self, package: str) -> Path:
        return self.game / "Packages" / "Textures" / f"{package}.utx"

    @property
    def shud_textures(self) -> Path:
        return self.game / "Packages" / "Textures" / "SHudTextures.utx"

    def font(self, name: str) -> Path:
        return self.game / "Menus" / "Fonts" / f"{name}.mft"

    def strings(self, name: str) -> Path:
        """<name>.int, or the build language's file when the install has it (svmbuild --language)."""
        from ..sources.env import import_formats  # noqa: PLC0415

        import_formats()
        import scctformats.classdefaults as cd  # noqa: PLC0415

        root = self.game / "System" / "_PC_"
        return cd.localization_file(root, name) or root / f"{name}.int"

    def all_inputs(self) -> list[Path]:
        files = [self.mgm(p) for p in PACKAGES] + [self.texture_package(p) for p in PACKAGES]
        files.append(self.shud_textures)
        files += [self.menu_textures(p) for p in MENU_TEXTURES]
        fonts = self.game / "Menus" / "Fonts"
        if fonts.is_dir():
            files += sorted(fonts.glob("*.mft"))
        files += [self.strings(s) for s in STRING_FILES]
        return files


@dataclass
class Report:
    packages: dict = field(default_factory=dict)
    fonts: dict = field(default_factory=dict)
    textures: dict = field(default_factory=dict)
    missing: list = field(default_factory=list)
    warnings: list = field(default_factory=list)


def _write_json(path: Path, obj) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical.dump_file(obj, ascii_keys=False))


def _texture_index(pkg) -> dict[str, object]:
    return {e.name.lower(): e for e in pkg.exports if e.class_name == "Texture"}


def _save_png(image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=False)


class _TextureSource:
    """Texture packages opened on first use (the scctpkg reader is imported lazily so synthetic tests need none)."""

    def __init__(self):
        self._packages: dict[Path, tuple[object, dict]] = {}

    def lookup(self, path: Path):
        if path not in self._packages:
            from ..paths import import_scctpkg  # noqa: PLC0415

            scctpkg = import_scctpkg()
            from scctpkg.package import Package  # noqa: PLC0415

            pkg = Package.load(path) if path.is_file() else None
            self._packages[path] = (pkg, _texture_index(pkg) if pkg is not None else {})
        return self._packages[path]

    def read(self, path: Path, name: str):
        pkg, index = self.lookup(path)
        export = index.get(name.lower())
        if pkg is None or export is None:
            return None
        from scctpkg.texture import read_texture  # noqa: PLC0415

        return read_texture(pkg, export)


def export_ui(game: Path, out_dir: Path) -> Report:
    src = Sources(Path(game))
    out_dir = Path(out_dir)
    report = Report()
    textures = _TextureSource()
    font_names: list[str] = []

    for package in PACKAGES:
        path = src.mgm(package)
        if not path.is_file():
            report.missing.append(f"Menus/{package}.mgm")
            continue
        doc = mgm.parse_mgm(path.read_bytes())
        for f in doc["fonts"]:
            if f and f not in font_names:
                font_names.append(f)
        written = []
        for tex in mgm.textures_used(doc):
            t = textures.read(src.texture_package(package), tex)
            if t is None or t.image is None:
                report.missing.append(f"{package}_mgm.{tex}")
                continue
            _save_png(t.image, out_dir / "textures" / package / f"{tex}.png")
            written.append({"name": tex, "width": t.width, "height": t.height})
        _write_json(out_dir / "magma" / f"{package}.json", doc)
        report.packages[package] = {"file": f"magma/{package}.json", "areas": len(doc["areas"]),
                                    "pages": sorted(doc["pages"]), "materials": len(doc["materials"])}
        report.textures[package] = written
        if doc.get("unresolved"):
            report.warnings.append(f"{package}: unresolved references {doc['unresolved'][:5]}")

    shud = []
    for tex in SHUD_TEXTURES:
        t = textures.read(src.shud_textures, tex)
        if t is None or t.image is None:
            report.missing.append(f"SHudTextures.{tex}")
            continue
        _save_png(t.image, out_dir / "textures" / "SHudTextures" / f"{tex}.png")
        shud.append({"name": tex, "width": t.width, "height": t.height})
    report.textures["SHudTextures"] = shud

    for package, names in MENU_TEXTURES.items():
        menu = []
        for tex in names:
            t = textures.read(src.menu_textures(package), tex)
            if t is None or t.image is None:
                report.missing.append(f"{package}.{tex}")
                continue
            _save_png(t.image, out_dir / "textures" / package / f"{tex}.png")
            menu.append({"name": tex, "width": t.width, "height": t.height})
        report.textures[package] = menu

    for name in font_names:
        path = src.font(name)
        if not path.is_file():
            report.missing.append(f"Menus/Fonts/{name}.mft")
            continue
        font = mft.parse_mft(path.read_bytes())
        key = mft.font_key(name)
        images = []
        for n in range(max(1, font.pages)):
            tex_name = f"{mft.texture_prefix(name)}{n}"
            t = None
            for package in PACKAGES:
                t = textures.read(src.texture_package(package), tex_name)
                if t is not None and t.image is not None:
                    break
            if t is None or t.image is None:
                report.missing.append(f"font page {tex_name}")
                break
            from PIL import Image  # noqa: PLC0415

            images.append(t.image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).convert("RGBA"))
        glyphs = mft.glyph_table(font, [im.size for im in images])
        pages = []
        padding = 0
        if images:
            # Repack with a transparent gap round every glyph so the client can sample exact cells.
            images, glyphs = mft.repack_glyphs(images, glyphs)
            padding = mft.GLYPH_PADDING
            for n, image in enumerate(images):
                _save_png(image, out_dir / "fonts" / f"{key}_{n}.png")
                pages.append({"file": f"{key}_{n}.png", "width": image.size[0], "height": image.size[1]})
        doc = {"format": FONT_FORMAT, "name": font.name, "size": font.size, "version": font.version,
               "padding": padding, "pages": pages, "glyphs": glyphs}
        _write_json(out_dir / "fonts" / f"{key}.json", doc)
        report.fonts[name] = {"file": f"fonts/{key}.json", "pages": len(pages), "glyphs": len(doc["glyphs"])}

    table = {}
    for name in STRING_FILES:
        path = src.strings(name)
        if not path.is_file():
            report.missing.append(f"System/_PC_/{path.name}")
            continue
        table[name] = strings.parse_int(path.read_bytes())
    _write_json(out_dir / "strings.json", table)

    manifest = {"format": UI_FORMAT, "packages": report.packages, "fonts": report.fonts, "textures": report.textures,
                "strings": "strings.json", "missing": sorted(report.missing), "warnings": report.warnings}
    _write_json(out_dir / "manifest.json", manifest)
    return report
