"""The map catalog's game data (docs/MAPS.md "The map catalog"): what the menus list besides each SVMap folder.

- the original's host map list order: the map files of Packages/Maps as the game's list builder sees them
  (GUIController.GetMapList: file order, Entry and Lobby skipped, TME** and TSP** skipped);
- Reloaded's defaults from System/Reloaded.Core.dll: the playlists (name, description, maps), the map name table
  and the variant words. They are read from the static initialisers that build them: each pushes the address of
  its strings in order (x86 `push imm32`), so the strings come back in the order the code builds the lists;
- the map list picture, the loading screens and the briefing map of each map, from <Map>-i.utc.

The playlist reader follows the layout of the initialisers in Reloaded V4.0: the maps of the first playlist
(Standard) are built into a shared list before the playlist names, and that list is used again by the last one
(Merc Deathmatch, the only playlist for Merc Deathmatch); every other playlist pushes its name, its description and
then its maps.
"""

from __future__ import annotations

import re
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable

MAP_CODE_RE = re.compile(r"^[A-Z][a-z]{3}[A-Z][A-Z]?$|^T(?:SP|ME)\d\d$")

# GUIController.GetMapList skips these (game 0x109e31b0; wildcard patterns of FUN_109e3130, '*' any one char).
MENU_MAPS = ("Entry", "Lobby")
TRAINING_PATTERNS = ("TME**.sd*", "TSP**.sd*")

# <Map>-i.utc textures: the map list picture (LevelInfo.GetLevelPicture loads "%s-i.Menu"), the loading screens of
# the spies (ATT) and the mercs (DEF), and the briefing map.
IMAGES = {"menu": "Menu", "loading-spy": "LoadingATT0", "loading-merc": "LoadingDEF0", "briefing": "Briefing"}

# The playlist that reuses the first playlist's maps and is the Merc Deathmatch one.
MERC_DEATHMATCH_PLAYLIST = "Merc Deathmatch"


def wildcard_match(pattern: str, name: str) -> bool:
    """The list builder's match: same length, '*' matches any one character, ASCII letters compared upper case."""
    if len(pattern) != len(name):
        return False
    return all(p == "*" or n == "*" or p.upper() == n.upper() for p, n in zip(pattern, name))


def original_map_order(map_files: Iterable[str]) -> list[str]:
    """Map codes in the original host list order: file listing order (case-insensitive), menu and training maps out."""
    codes = []
    for f in map_files:
        stem, _, ext = f.rpartition(".")
        if not stem or not ext.lower().startswith("sd"):
            continue
        if any(stem.lower() == m.lower() for m in MENU_MAPS):
            continue
        if any(wildcard_match(p, f) for p in TRAINING_PATTERNS):
            continue
        if stem not in codes:
            codes.append(stem)
    return sorted(codes, key=lambda c: c.upper())


def training_maps(map_files: Iterable[str]) -> dict[str, list[str]]:
    """The tutorial maps per side (spy TSP, merc TME), in name order."""
    stems = sorted({f.rpartition(".")[0] for f in map_files if "." in f}, key=str.upper)
    return {"spy": [s for s in stems if wildcard_match("TSP**", s)],
            "merc": [s for s in stems if wildcard_match("TME**", s)]}


# ---- Reloaded.Core.dll -------------------------------------------------------------------------------------------

@dataclass
class _Pe:
    data: bytes
    base: int
    sections: dict = field(default_factory=dict)  # name -> (va, vsize, raw, rawsize)

    @classmethod
    def load(cls, data: bytes) -> "_Pe":
        if data[:2] != b"MZ":
            raise ValueError("not a PE file")
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        if data[pe:pe + 4] != b"PE\0\0":
            raise ValueError("not a PE file")
        nsec = struct.unpack_from("<H", data, pe + 6)[0]
        opt = pe + 24
        base = struct.unpack_from("<I", data, opt + 28)[0]
        sopt = struct.unpack_from("<H", data, pe + 20)[0]
        secs = {}
        for i in range(nsec):
            o = opt + sopt + 40 * i
            name = data[o:o + 8].rstrip(b"\0").decode("ascii", "replace")
            vs, va, rs, ra = struct.unpack_from("<IIII", data, o + 8)
            secs[name] = (va, vs, ra, rs)
        return cls(data, base, secs)

    def cstr(self, va: int) -> str | None:
        """The ASCII string at a virtual address in .rdata, or None."""
        sva, _, ra, rs = self.sections.get(".rdata", (0, 0, 0, 0))
        off = va - self.base - sva + ra
        if not ra <= off < ra + rs:
            return None
        end = self.data.find(b"\0", off, off + 200)
        if end < 0:
            return None
        t = self.data[off:end]
        if 1 <= len(t) < 120 and all(32 <= c < 127 for c in t):
            return t.decode("ascii")
        return None

    def string_pushes(self) -> list[tuple[int, str]]:
        """(code address, string) of every `push imm32` in .text whose operand is an ASCII string, in code order."""
        va, _, ra, rs = self.sections[".text"]
        text = self.data[ra:ra + rs]
        out = []
        for m in re.finditer(rb"\x68", text):
            i = m.start()
            if i + 5 > len(text):
                break
            s = self.cstr(struct.unpack_from("<I", text, i + 1)[0])
            if s is not None:
                out.append((self.base + va + i, s))
        return out

    def string_slots(self, pushes: list[tuple[int, str]]) -> dict[int, int]:
        """For each string push: the frame slot the string is built into (code address of the push -> ebp offset).

        A string is built by `lea ecx, [ebp - X]` and a call to the string constructor around its push; the lea is
        the last one before the call that follows the push.
        """
        va, _, ra, rs = self.sections[".text"]
        text = self.data[ra:ra + rs]
        out = {}
        for a, _ in pushes:
            i = a - self.base - va
            call = text.find(bytes((0xE8,)), i + 5, i + 0x40)
            if call < 0:
                continue
            lea = text.rfind(bytes((0x8D, 0x8D)), max(0, i - 0x30), call)
            if lea < 0:
                continue
            out[a] = struct.unpack_from("<i", text, lea + 2)[0]
        return out


def _is_code(s: str) -> bool:
    return bool(MAP_CODE_RE.match(s))


def _is_description(s: str) -> bool:
    return " " in s and s.endswith(".") and len(s) > 20


# The table after the map names pairs the original's internal map codes (AQU03) with names.
_LEGACY_CODE_RE = re.compile(r"^[A-Z]{3}\d\d$|^Map\d\d$")

_NAME_RE = re.compile(r"^[A-Z][A-Za-z0-9']*(?: [A-Za-z0-9'][A-Za-z0-9']*)+$")

# Pushes of one initialiser sit close together; a longer gap ends a run.
_RUN_GAP = 0x100


@dataclass
class ReloadedDefaults:
    playlists: list[dict]
    names: dict[str, str]
    variant_words: list[str]


def _code_run(pushes: list[tuple[int, str]], i: int) -> int:
    """Index after the run of consecutive map code pushes starting at i."""
    j = i
    while j < len(pushes) and _is_code(pushes[j][1]) and (j == i or pushes[j][0] - pushes[j - 1][0] < _RUN_GAP):
        j += 1
    return j


def read_reloaded_defaults(data: bytes) -> ReloadedDefaults:
    """Playlists, map names and variant words from the static initialisers of Reloaded.Core.dll."""
    pe = _Pe.load(data)
    pushes = pe.string_pushes()
    # The shared first list: the first run of ten or more map codes followed by a playlist name and description.
    start = None
    for i in range(len(pushes)):
        end = _code_run(pushes, i)
        if end - i >= 10 and end + 1 < len(pushes) and not _is_code(pushes[end][1])                 and _is_description(pushes[end + 1][1]):
            start = i
            break
        if end > i:
            continue
    if start is None:
        raise ValueError("Reloaded.Core.dll: the playlist initialisers were not found")
    i = _code_run(pushes, start)
    shared = [s for _, s in pushes[start:i]]
    playlists: list[dict] = []
    # Each playlist: name, description, then its own maps unless the next push starts the name table
    # (a map code followed by a name) or another playlist.
    while i + 1 < len(pushes) and not _is_code(pushes[i][1]) and _is_description(pushes[i + 1][1]):
        name, desc = pushes[i][1], pushes[i + 1][1]
        i += 2
        end = _code_run(pushes, i)
        own = end - i >= 2 or (end - i == 1 and end < len(pushes) and _is_code(pushes[end][1]))
        maps = [s for _, s in pushes[i:end]] if own else []
        if own:
            i = end
        if not maps:
            maps = list(shared)
        mode = "MercDeathMatch" if name == MERC_DEATHMATCH_PLAYLIST else "Story"
        playlists.append({"name": name, "description": desc, "gameMode": mode, "maps": maps})
    if not playlists:
        raise ValueError("Reloaded.Core.dll: no playlists found")
    # Map names: the table right after the playlists is an array of (code, name) string pairs built into
    # consecutive frame slots (0x18 bytes each); the code builds them in any order, so pair them by slot.
    j = i + 1
    while j < len(pushes) and not _LEGACY_CODE_RE.match(pushes[j][1]) and pushes[j][0] - pushes[j - 1][0] < _RUN_GAP:
        j += 1
    slots = pe.string_slots(pushes[i:j])
    table = [(slots[a], s) for a, s in pushes[i:j] if a in slots]
    names: dict[str, str] = {}
    singles = []
    k = 0
    while k < len(table):
        # Two pushes into one slot are the two arguments of a pair constructor: the code is pushed last.
        if k + 1 < len(table) and table[k][0] == table[k + 1][0]:
            name, code = table[k][1], table[k + 1][1]
            k += 2
            if not _is_code(code) or _is_code(name):
                raise ValueError(f"Reloaded.Core.dll: map name pair out of step at {name!r}, {code!r}")
            names.setdefault(code, name)
            continue
        singles.append(table[k])
        k += 1
    singles.sort()
    for k in range(0, len(singles) - 1, 2):
        (sa, a), (sb, b) = singles[k], singles[k + 1]
        if sb - sa != 0x18 or _is_code(a) == _is_code(b):
            raise ValueError(f"Reloaded.Core.dll: map name table out of step at {a!r}, {b!r}")
        code, name = (a, b) if _is_code(a) else (b, a)
        names.setdefault(code, name)
    playlist_names = {p["name"] for p in playlists}
    # A pair built outside the table (VertX in V4.0, by a helper that builds the code and then the name into one
    # pair): a map code push followed closely by a name push.
    for k in range(len(pushes) - 1):
        (a, code), (b, name) = pushes[k], pushes[k + 1]
        if _is_code(code) and code not in names and 0 < b - a < 0x40 and _NAME_RE.match(name)                 and name not in playlist_names:
            names[code] = name
    # Variant words: the run of one-word pushes that ends with "Covert".
    words: list[str] = []
    for idx, (_, s) in enumerate(pushes):
        if s == "Covert":
            lo = idx
            while lo > 0 and " " not in pushes[lo - 1][1] and not _is_code(pushes[lo - 1][1])                     and pushes[lo][0] - pushes[lo - 1][0] < 0x80:
                lo -= 1
            words = [w for _, w in pushes[lo:idx + 1]]
            break
    return ReloadedDefaults(playlists, names, words)


# ---- <Map>-i.utc images -----------------------------------------------------------------------------------------

def export_images(info_package: Path, out_dir: Path) -> dict[str, dict]:
    """Writes the map's images as PNG under out_dir; {kind: {"file", "width", "height"}} of those it has."""
    from .paths import import_scctpkg  # noqa: PLC0415

    import_scctpkg()
    from scctpkg.package import Package  # noqa: PLC0415
    from scctpkg.texture import read_texture  # noqa: PLC0415

    pkg = Package.load(info_package)
    by_name = {e.name.lower(): e for e in pkg.exports if e.class_name == "Texture" and e.size > 0}
    written: dict[str, dict] = {}
    for kind, tex_name in IMAGES.items():
        e = by_name.get(tex_name.lower())
        if e is None:
            continue
        t = read_texture(pkg, e)
        if t.image is None:
            continue
        path = out_dir / f"{kind}.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        t.image.save(path, format="PNG", optimize=False)
        written[kind] = {"file": path.name, "width": t.width, "height": t.height}
    return written
