"""Writers of the audio content (docs/AUDIO.md):

shared/audio/                         the "audio" step
    audio.json                        svm.audio/1: every Sound of Packages/Sounds/*.uax by object path, the .mux
                                      channel maps, the sound references of the class defaults
    wav/<Package>/<Group>/<Name>.wav  the RIFF WAVE data of every non-streamed sound, as stored

svmap/<Map>/audio/                    the "audio-map" step
    audio.json                        svm.mapaudio/1: the bank index, every sound the map can play (sound id -> file,
                                      volume, radius, pitch, loop), the map's sound actors and the zones (ambience,
                                      I3DL2 reverb)
    streams/<Package>/<Name>.ogg      the Ogg Vorbis streams of the map's .uas bank, as stored
    wav/<Map>/<Group>/<Name>.wav      the sounds stored in the map package itself (a community map's music), as
                                      stored; their entries carry the full sound record and "root": "map"

The map's manifest.json gets the audio files and the audio group hash, and requires the shared audio pack.
"""

from __future__ import annotations

import re
from pathlib import Path

from .. import canonical
from . import mux, refs, sound, uas

LIBRARY_FORMAT = "svm.audio/1"
MAP_FORMAT = "svm.mapaudio/1"
LIBRARY_FILE = "audio.json"
SAFE = re.compile(r"[^A-Za-z0-9_.-]")


def sounds_dir(game: Path) -> Path:
    return Path(game) / "Packages" / "Sounds"


def package_files(game: Path) -> list[Path]:
    """The install's sound packages and those of the downloaded maps (gamefiles: the install's win)."""
    from ..gamefiles import files  # noqa: PLC0415

    return files(game, "Packages/Sounds", "*.uax")


def mux_files(game: Path) -> list[Path]:
    from ..gamefiles import files  # noqa: PLC0415

    return files(game, "Packages/Sounds", "*.mux")


def bank_file(game: Path, map_name: str) -> Path:
    """The map's bank, Packages/Sounds/<Map>.uas, matched ignoring case, the install's first, then the downloaded
    maps' (the install's path when missing)."""
    from ..gamefiles import find_file  # noqa: PLC0415

    return find_file(game, "Packages/Sounds", f"{map_name}.uas")


def safe_part(name: str) -> str:
    return SAFE.sub("_", name) or "_"


def wav_rel(path: str) -> str:
    return "wav/" + "/".join(safe_part(p) for p in path.split(".")) + ".wav"


def stream_rel(package: str, name: str) -> str:
    return f"streams/{safe_part(package)}/{safe_part(name)}.ogg"


def _r(x: float) -> float:
    return canonical.to_f32(float(x))


# ---- shared library -------------------------------------------------------------------------------------------


def read_packages(game: Path) -> tuple[dict[str, sound.SoundObject], dict[str, dict], list[str], list[str]]:
    """Every Sound of every .uax: (path -> object, package -> info, problems, identical duplicate exports)."""
    from ..paths import import_scctpkg  # noqa: PLC0415

    import_scctpkg()
    from scctpkg.package import Package  # noqa: PLC0415

    objects: dict[str, sound.SoundObject] = {}
    packages: dict[str, dict] = {}
    problems: list[str] = []
    seen: set[str] = set()
    duplicates: list[str] = []
    for f in package_files(game):
        pkg = Package.load(f)
        count = 0
        for e in pkg.exports:
            if e.class_name != "Sound":
                continue
            try:
                s = sound.decode_sound(pkg, e)
            except Exception as ex:  # noqa: BLE001 - one bad sound is reported, the rest convert
                problems.append(f"{e.full_name}: {type(ex).__name__}: {ex}")
                continue
            if s.path.lower() in seen:
                first = next(o for k, o in objects.items() if k.lower() == s.path.lower())
                if _same(first, s):
                    duplicates.append(s.path)   # an identical second export of the same path: the first one is used
                else:
                    problems.append(f"{s.path}: defined twice with different data")
                continue
            seen.add(s.path.lower())
            objects[s.path] = s
            count += 1
        packages[f.stem] = {"file": f"Packages/Sounds/{f.name}", "version": pkg.version, "sounds": count}
    return objects, packages, problems, sorted(duplicates)


def _same(a: sound.SoundObject, b: sound.SoundObject) -> bool:
    return (a.flags, a.source, a.gain, a.radius, a.channel_mask, a.data, [(e.name, e.value) for e in a.entries]) ==         (b.flags, b.source, b.gain, b.radius, b.channel_mask, b.data, [(e.name, e.value) for e in b.entries])


def resolve_entry(objects: dict[str, sound.SoundObject], by_name: dict[str, list[str]], owner: str,
                  name: str) -> str | None:
    """A list entry's sound: the object of that name in the owner's package, else the first in path order."""
    hits = by_name.get(name.lower(), [])
    for h in hits:
        if objects[h].package.lower() == owner.lower():
            return h
    return hits[0] if hits else None


def sound_json(s: sound.SoundObject, resolved: list[str | None], channel_map: dict | None) -> dict:
    flags = s.flags
    out: dict = {
        "kind": s.kind,
        "flags": flags,
        "volume": _r(s.gain),
        "radius": _r(s.radius),
        "pitch": 1.0,
        "loop": bool(flags & sound.FLAG_LOOP),
        "positional": not flags & sound.FLAG_2D,
    }
    if s.kind == "wave":
        w = s.wave
        out["file"] = wav_rel(s.path)
        out.update({"format": w.format_tag, "channels": w.channels, "rate": w.rate, "bits": w.bits,
                    "frames": w.frames, "duration": _r(w.duration)})
        if w.loops:
            out["loopStart"], out["loopEnd"] = w.loops[0]
    elif s.kind in ("stream", "multichannel"):
        out["stream"] = s.stream_name
    if s.entries:
        out["entries"] = [{"name": e.name, "sound": r, "value": e.value} for e, r in zip(s.entries, resolved)]
    if s.kind == "multichannel":
        out["channelMask"] = s.channel_mask
        out["speakers"] = sound.channel_names(s.channel_mask)
        if channel_map is not None:
            out["channelMap"] = channel_map
    if s.source and s.kind != "stream":
        out["source"] = s.source
    return out


def build_library(game: Path, out_dir: Path, db=None) -> dict:
    """Write shared/audio (out_dir) and return the build statistics."""
    objects, packages, problems, duplicates = read_packages(game)
    by_name: dict[str, list[str]] = {}
    for p in sorted(objects, key=str.lower):
        by_name.setdefault(objects[p].name.lower(), []).append(p)
    muxes: dict[str, dict] = {}
    for f in mux_files(game):
        muxes[f.stem.lower()] = {"file": f"Packages/Sounds/{f.name}",
                                 "channels": mux.read_mux(f.read_bytes().decode("latin-1"))}
    doc_sounds: dict[str, dict] = {}
    written: dict[str, str] = {}
    unresolved: list[str] = []
    kinds: dict[str, int] = {}
    used_mux = set()
    for path in sorted(objects, key=str.lower):
        s = objects[path]
        resolved = [resolve_entry(objects, by_name, s.package, e.name) for e in s.entries]
        unresolved += [f"{path} -> {e.name}" for e, r in zip(s.entries, resolved) if r is None]
        cmap = None
        if s.kind == "multichannel" and s.name.lower() in muxes:
            cmap = muxes[s.name.lower()]["channels"]
            used_mux.add(s.name.lower())
        doc_sounds[path] = sound_json(s, resolved, cmap)
        kinds[s.kind] = kinds.get(s.kind, 0) + 1
        if s.data is not None:
            rel = wav_rel(path)
            if rel.lower() in written:
                raise ValueError(f"{path} and {written[rel.lower()]} share the file {rel}")
            written[rel.lower()] = path
            target = out_dir / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(s.data)
    class_refs = refs.class_sound_refs(db, refs.SoundIndex(objects)) if db is not None else {}
    doc = {
        "format": LIBRARY_FORMAT,
        "packages": packages,
        "sounds": doc_sounds,
        "mux": {k: muxes[k] for k in sorted(muxes)},
        "classes": class_refs,
        "unresolved": unresolved,
        "duplicates": duplicates,
        "problems": problems,
    }
    (out_dir / LIBRARY_FILE).write_bytes(canonical.dump_file(doc))
    return {"packages": len(packages), "sounds": len(objects), "kinds": kinds, "waves": len(written),
            "mux": len(muxes), "muxUnused": sorted(set(muxes) - used_mux), "classes": len(class_refs),
            "unresolved": len(unresolved), "duplicates": len(duplicates), "problems": problems[:20]}


def load_library(shared_audio: Path) -> dict:
    return canonical.loads((Path(shared_audio) / LIBRARY_FILE).read_bytes())


# ---- per-map bank ---------------------------------------------------------------------------------------------


def bank_layout(game: Path, map_name: str) -> list[str]:
    """Files (relative to the map's audio folder) the map's build writes, from the bank index and the map
    package's own Sound exports."""
    rels = ["audio.json"]
    f = bank_file(game, map_name)
    if f.is_file():
        bank = uas.read_bank(f.read_bytes(), check_streams=False)
        rels += [stream_rel(e.package, e.name) for e in bank.entries]
    rels += [wav_rel(path) for path in map_package_waves(game, map_name)]
    return sorted(set(rels))


def _map_package(game: Path, map_name: str):
    from ..gamefiles import game_file  # noqa: PLC0415
    from ..paths import import_scctpkg  # noqa: PLC0415

    import_scctpkg()
    from scctpkg.package import Package  # noqa: PLC0415

    f = game_file(Path(game), f"Packages/Maps/{map_name}.sdc")
    return Package.load(f) if f.is_file() else None


def read_map_sounds(pkg) -> tuple[dict[str, sound.SoundObject], list[str]]:
    """The Sound exports stored in a map package itself: (path -> object, problems)."""
    objects: dict[str, sound.SoundObject] = {}
    problems: list[str] = []
    if pkg is None:
        return objects, problems
    for e in pkg.exports:
        if e.class_name != "Sound":
            continue
        try:
            s = sound.decode_sound(pkg, e)
        except Exception as ex:  # noqa: BLE001 - one bad sound is reported, the rest convert
            problems.append(f"{e.full_name}: {type(ex).__name__}: {ex}")
            continue
        objects[s.path] = s
    return objects, problems


def map_package_waves(game: Path, map_name: str) -> list[str]:
    """Paths of the map package's own sounds that carry wave data (one file each under audio/wav/)."""
    objects, _ = read_map_sounds(_map_package(game, map_name))
    return sorted((p for p, s in objects.items() if s.data is not None), key=str.lower)


def _map_sound_entry(path: str, info: dict, bank: uas.Bank | None, streams: dict) -> dict:
    out = {k: info[k] for k in ("kind", "volume", "radius", "pitch", "loop", "positional") if k in info}
    if info["kind"] == "wave":
        out["file"] = info["file"]
        out["root"] = "shared"
        out["duration"] = info.get("duration", 0.0)
    elif info["kind"] in ("stream", "multichannel"):
        package = path.split(".", 1)[0]
        key = f"{package}.{info['stream']}".lower()
        hit = streams.get(key)
        if hit is not None:
            out["file"] = hit["file"]
            out["root"] = "map"
            out["duration"] = hit["duration"]
        else:
            out["missing"] = True
    else:
        out["entries"] = info.get("entries", [])
    return out


def build_map_audio(game: Path, map_name: str, audio_dir: Path, library: dict, src=None) -> dict:
    """Write svmap/<Map>/audio (audio_dir). src: an opened mapconv MapSource (actor references), or None."""
    audio_dir.mkdir(parents=True, exist_ok=True)
    lib_sounds: dict = library["sounds"]
    f = bank_file(game, map_name)
    bank = uas.read_bank(f.read_bytes()) if f.is_file() else None
    data = f.read_bytes() if bank is not None else b""
    streams: dict[str, dict] = {}
    stream_rows = []
    if bank is not None:
        for e in bank.entries:
            rel = stream_rel(e.package, e.name)
            target = audio_dir / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data[e.start:e.end])
            st = e.stream
            row = {"package": e.package, "name": e.name, "file": rel, "offset": e.start, "length": e.length,
                   "duration": _r(st.duration), "channels": st.channels, "rate": st.rate, "samples": st.samples,
                   "pages": len(st.pages), "badCrc": st.bad_crc, "serial": st.serials[0] if st.serials else 0}
            if st.errors:
                row["errors"] = st.errors
            stream_rows.append(row)
            streams[f"{e.package}.{e.name}".lower()] = row
    # Sounds stored in the map package itself (a community map's own music and effects): written under audio/wav/
    # and played from the map folder.
    own, own_problems = read_map_sounds(src.pkg if src is not None else _map_package(game, map_name))
    own_info: dict[str, dict] = {}
    if own:
        by_name: dict[str, list[str]] = {}
        for p in sorted(list(lib_sounds) + list(own), key=str.lower):
            by_name.setdefault(p.rsplit(".", 1)[-1].lower(), []).append(p)
        for path in sorted(own, key=str.lower):
            s = own[path]
            resolved = []
            for e in s.entries:
                hits = by_name.get(e.name.lower(), [])
                mine = [h for h in hits if h.split(".", 1)[0].lower() == s.package.lower()]
                resolved.append((mine or hits or [None])[0])
            info = sound_json(s, resolved, None)
            info["root"] = "map"
            if s.data is not None:
                target = audio_dir / wav_rel(path)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(s.data)
            own_info[path] = info
    sounds_all = {**lib_sounds, **own_info}
    found = refs.map_sound_refs(src, refs.SoundIndex(sounds_all)) if src is not None else \
        {"actors": [], "zones": [], "missing": {}}
    # Every sound the map can play: what its actors name, the streamed sounds of its bank's packages, and what
    # list sounds reach from those.
    wanted: list[str] = []
    for a in found["actors"]:
        wanted += list(a["sounds"].values())
        if "ambientSound" in a:
            wanted.append(a["ambientSound"])
    bank_packages = {p.lower() for p, _ in bank.packages} if bank is not None else set()
    for path, info in lib_sounds.items():
        if info["kind"] in ("stream", "multichannel") and path.split(".", 1)[0].lower() in bank_packages:
            wanted.append(path)
    wanted += list(own_info)
    seen: dict[str, dict] = {}
    queue = sorted(set(wanted), key=str.lower)
    while queue:
        path = queue.pop(0)
        if path in seen or path not in sounds_all:
            continue
        info = sounds_all[path]
        seen[path] = info if path in own_info else _map_sound_entry(path, info, bank, streams)
        for e in info.get("entries", []):
            if e.get("sound") and e["sound"] not in seen:
                queue.append(e["sound"])
    missing_streams = sorted(p for p, v in seen.items() if v.get("missing"))
    doc = {
        "format": MAP_FORMAT,
        "map": map_name,
        "bank": None if bank is None else {
            "file": f"Packages/Sounds/{f.name}", "size": bank.size,
            "packages": [{"name": p, "offset": o} for p, o in bank.packages],
            "unused": [[a, b] for a, b in bank.unused], "problems": bank.problems},
        "streams": stream_rows,
        "sounds": {k: seen[k] for k in sorted(seen, key=str.lower)},
        "actors": found["actors"],
        "zones": found["zones"],
        "zoneDist": refs.zone_distances(src) if src is not None else [],
        "missing": {"sounds": found["missing"], "streams": missing_streams},
    }
    if own_problems:
        doc["problems"] = own_problems
    (audio_dir / "audio.json").write_bytes(canonical.dump_file(doc))
    return {
        "streams": len(stream_rows),
        "streamMB": round(sum(r["length"] for r in stream_rows) / 1e6, 2),
        "badCrcPages": sum(r["badCrc"] for r in stream_rows),
        "bankProblems": len(bank.problems) if bank is not None else 0,
        "unusedBytes": sum(b - a for a, b in bank.unused) if bank is not None else 0,
        "sounds": len(seen),
        "actors": len(found["actors"]),
        "zones": len(found["zones"]),
        "reverbZones": sum(1 for z in found["zones"] if "effect" in z),
        "missingStreams": len(missing_streams),
        "missingRefs": len(found["missing"]),
        "noBank": bank is None,
        "ownSounds": len(own_info),
        "ownProblems": len(own_problems),
    }


def patch_manifest(map_dir: Path, audio_files: dict[str, dict], library_sha: str | None) -> bool:
    """Put the audio files (rel under audio/ -> {sha256, size}) and the audio group hash into the map's manifest.json,
    and the shared audio pack into its requires. Returns True when the file changed."""
    path = Path(map_dir) / "manifest.json"
    raw = path.read_bytes()
    m = canonical.loads(raw)
    files = {k: v for k, v in m.get("files", {}).items() if not k.startswith("audio/")}
    for rel, meta in audio_files.items():
        files["audio/" + rel] = {"sha256": meta["sha256"], "size": meta["size"]}
    m["files"] = files
    hashes = {k: v for k, v in m.get("hashes", {}).items() if k != "audio"}
    rows = [(k, v["sha256"]) for k, v in files.items() if k.startswith("audio/")]
    if rows:
        hashes["audio"] = canonical.group_hash(rows)
    m["hashes"] = hashes
    req = [r for r in m.get("requires", []) if r.get("pack") != "audio"]
    if library_sha and rows:
        req.append({"pack": "audio", "sha256": library_sha})
    m["requires"] = sorted(req, key=lambda r: r["pack"])
    out = canonical.dump_file(m)
    if out == raw:
        return False
    path.write_bytes(out)
    return True
