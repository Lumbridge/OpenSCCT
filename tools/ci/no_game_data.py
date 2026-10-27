#!/usr/bin/env python3
"""Fails when game files, decompiled output or local-only data would end up in git.

Modes:
  (default)    every file git tracks or would add (git ls-files --cached --others --exclude-standard),
               read from the working tree
  --staged     the staged content of files added, copied, modified or retyped in the index (pre-commit)
  --all DIR    every file under DIR, whether git knows it or not (release folders, exports, temp trees)

Rules:
  local-path      path under local/, .content/, tools/godot/, re/.venv-ghidra/ or game/.godot/
  ghidra-project  Ghidra project or archive (.gpr, .gzf, anything inside a *.rep folder)
  game-extension  original game file type (.sdc .usx .utx .utc .ukx .uax .uas .mux .u .int)
  media           DDS, glTF/GLB, OGG, WAV or image files (by extension or content) outside fixtures/,
                  docs/images/ and game/branding/ (our own art)
  size            file larger than 5 MB
  ue-package      content starts with the UE package tag C1 83 2A 9E
  pe-binary       Windows executable or DLL (the game, the editor and Reloaded.Core are PE files)
  decompiled      text that looks like decompiled or disassembled code: tool banners, Ghidra
                  variable and function names, UnrealScript class bodies outside spec/, or address
                  plus mnemonic listings

The text heuristics do not count Ghidra names in spec/ (specs may cite addresses) or in
re/ghidra/annotations/ (the tracked home of exported names and types). Hand-written samples for
decompiler tests can opt out of the text heuristics only, with a comment line near the top that
holds nothing but the marker (see SYNTHETIC_MARKER_RE). Every other rule still applies to them.

Exit codes: 0 clean, 1 problems found, 2 usage or git error.
"""

from __future__ import annotations

import argparse
import fnmatch
import os
import re
import stat
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import IO, Callable, Iterable, Iterator, Sequence

MAX_BYTES = 5 * 1024 * 1024
HEAD_BYTES = 8192

LOCAL_PREFIXES = ("local/", ".content/", "tools/godot/", "re/.venv-ghidra/", "game/.godot/")

GAME_EXTENSIONS = frozenset(
    {".sdc", ".usx", ".utx", ".utc", ".ukx", ".uax", ".uas", ".mux", ".u", ".int"}
)
GHIDRA_EXTENSIONS = frozenset({".gpr", ".gzf"})
MEDIA_EXTENSIONS = frozenset(
    {".dds", ".glb", ".gltf", ".ogg", ".wav", ".png", ".jpg", ".jpeg", ".tga", ".bmp"}
)
# game/branding/ holds our own art (drawn by tools/branding/make_branding.py), never game files.
MEDIA_ALLOWED_PREFIXES = ("fixtures/", "docs/images/", "game/branding/")

SPEC_PREFIXES = ("spec/",)
ANNOTATION_PREFIXES = ("re/ghidra/annotations/",)

UE_PACKAGE_MAGIC = bytes((0xC1, 0x83, 0x2A, 0x9E))

# A comment line holding only "no-game-data: synthetic" in the first 2 KB of a file.
SYNTHETIC_MARKER_RE = re.compile(
    r"^[ \t]*(?:#|//|/\*|\*|--|;)[ \t]*no-game-data:[ \t]*synthetic[ \t]*(?:\*/)?[ \t]*$", re.MULTILINE
)
MARKER_WINDOW = 2048

# Tool banners. Each must start a line (after comment characters) or be a tool-specific phrase.
BANNER_RE = re.compile(
    r"^\W{0,6}(?:de-?compiled|disassembled)\s+(?:by|with|using)\b"
    r"|generated\s+by\s+Eliot\.UELib"
    r"|Hex-Rays\s+decompiler"
    r"|<!DOCTYPE\s+PROGRAM\s+SYSTEM\s+\"program_dtd\">"
    r"|typedef\s+unsigned\s+char\s+undefined\s*;",
    re.IGNORECASE | re.MULTILINE,
)

# Names Ghidra or IDA give to things they could not name. These also show up in notes and indexes.
NAME_TOKEN_RE = re.compile(
    r"\bundefined[1-8]\b"
    r"|\b(?:FUN|DAT|thunk_FUN)_[0-9A-Fa-f]{6,16}\b"
    r"|\bPTR_(?:FUN_|DAT_)?[0-9A-Fa-f]{6,16}\b"
    r"|\bsub_[0-9A-Fa-f]{6,16}\b"
    r"|\b_(?:BYTE|WORD|DWORD|QWORD)\b"
)

# Tokens that only appear inside decompiled function bodies.
BODY_TOKEN_RE = re.compile(
    r"\b[a-z]{1,4}Var\d{1,3}\b"
    r"|\blocal_(?:res)?[0-9a-f]{1,5}\b"
    r"|\bparam_\d{1,2}\b"
    r"|\b(?:unaff|extraout|in)_[A-Z][A-Za-z0-9]{1,4}\b"
    r"|\b(?:CONCAT|ZEXT|SEXT|SUB|CARRY|SCARRY|SBORROW)\d{1,3}\("
    r"|\b(?:LAB|switchD|caseD)_[0-9A-Fa-f]{6,16}\b"
)

GHIDRA_WARNING_RE = re.compile(r"/\*\s*WARNING:\s+[A-Z]")

BODY_TOKEN_LIMIT = 12
TOKEN_LIMIT = 16

_UC_MODIFIERS = r"(?:(?:native(?:\(\d+\))?|final|static|singular|exec|private|protected|latent|iterator)[ \t]+)*"
UC_DEFAULTPROPERTIES_RE = re.compile(
    r"^[ \t]*defaultproperties[ \t]*(?:\r?\n[ \t]*)*\{", re.IGNORECASE | re.MULTILINE
)
UC_SIMULATED_RE = re.compile(
    r"^[ \t]*" + _UC_MODIFIERS + r"simulated[ \t]+" + _UC_MODIFIERS + r"(?:function|event|state)\b",
    re.IGNORECASE | re.MULTILINE,
)
UC_CLASS_RE = re.compile(r"^[ \t]*class[ \t]+\w+[ \t]+extends[ \t]+\w+", re.IGNORECASE | re.MULTILINE)
UC_REPLICATION_RE = re.compile(
    r"^[ \t]*replication[ \t]*(?:\r?\n[ \t]*)*\{", re.IGNORECASE | re.MULTILINE
)
UC_LOCAL_RE = re.compile(
    r"^[ \t]*local[ \t]+\w+(?:<\w+>)?[ \t]+\w+(?:[ \t]*,[ \t]*\w+)*[ \t]*;", re.MULTILINE
)
UC_BODY_LIMIT = 5

# Goto-form output of an UnrealScript bytecode decompiler: jump offsets, "return nothing", cast tokens.
_UC_TYPES = r"(?:Byte|Int|Float|Bool|String|Name|Object|Vector|Rotator)"
UC_BYTECODE_RE = re.compile(
    r"\bgoto @[0-9A-Fa-f]{4}\b|\breturn nothing\b|\b" + _UC_TYPES + "To" + _UC_TYPES + r"\("
)
UC_BYTECODE_LIMIT = 20

ASM_LINE_RE = re.compile(
    r"^[ \t]*(?:0x)?[0-9A-Fa-f]{6,16}(?::|[ \t])[ \t]*(?:[0-9A-Fa-f]{2}[ \t]+){0,15}[ \t]*"
    r"(?:mov|movzx|movsx|lea|push|pop|call|jmp|j[a-z]{1,3}|cmp|test|add|sub|xor|and|or|inc|dec"
    r"|ret|retn|nop|imul|idiv|shl|shr|sar|f[a-z]{2,5})\b",
    re.IGNORECASE | re.MULTILINE,
)
ASM_LINE_LIMIT = 20


class GuardError(Exception):
    """A git call or the command line went wrong."""


@dataclass(frozen=True)
class Entry:
    """One file to check. path is relative to the repo or tree root and uses forward slashes."""

    path: str
    size: int
    load: Callable[[int], bytes]
    is_link: bool = False


@dataclass(frozen=True)
class Finding:
    path: str
    rule: str
    message: str


def _starts_with_any(path: str, prefixes: Sequence[str]) -> bool:
    return any(path.startswith(prefix) for prefix in prefixes)


def _is_pe(head: bytes) -> bool:
    if len(head) < 0x40 or head[:2] != b"MZ":
        return False
    offset = int.from_bytes(head[0x3C:0x40], "little")
    return offset + 4 <= len(head) and head[offset : offset + 4] == b"PE\0\0"


def _media_kind(head: bytes) -> str | None:
    if head.startswith(b"DDS "):
        return "DDS texture"
    if head.startswith(b"glTF"):
        return "GLB model"
    if head.startswith(b"OggS"):
        return "OGG audio"
    if head.startswith(b"\x89PNG\r\n\x1a\n"):
        return "PNG image"
    if head.startswith(b"\xff\xd8\xff"):
        return "JPEG image"
    if head[:4] == b"RIFF" and head[8:12] == b"WAVE":
        return "WAV audio"
    return None


def _decode_text(data: bytes) -> str | None:
    """Returns the text of a text file, or None for binary content."""
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        try:
            return data.decode("utf-16")
        except UnicodeDecodeError:
            return None
    if b"\0" in data[:HEAD_BYTES]:
        return None
    return data.decode("utf-8", errors="replace")


def decompiled_reason(path: str, text: str) -> str | None:
    """Returns why text looks like decompiled or disassembled code, or None. path is lower case."""
    if SYNTHETIC_MARKER_RE.search(text[:MARKER_WINDOW]):
        return None
    banner = BANNER_RE.search(text)
    if banner:
        return f"decompiler banner {banner.group(0).strip()!r}"

    in_spec = _starts_with_any(path, SPEC_PREFIXES)
    if not _starts_with_any(path, ANNOTATION_PREFIXES):
        body = len(BODY_TOKEN_RE.findall(text))
        names = 0 if in_spec else len(NAME_TOKEN_RE.findall(text))
        if GHIDRA_WARNING_RE.search(text) and body + names >= 2:
            return "Ghidra decompiler warnings next to Ghidra variable or function names"
        if body >= BODY_TOKEN_LIMIT:
            return f"{body} Ghidra decompiler variables (uVar1, param_1, local_10...)"
        if body + names >= TOKEN_LIMIT:
            return f"{body + names} Ghidra or IDA names (undefined4, FUN_..., DAT_...)"

    if not in_spec and UC_DEFAULTPROPERTIES_RE.search(text):
        if UC_SIMULATED_RE.search(text) or UC_CLASS_RE.search(text) or UC_REPLICATION_RE.search(text):
            return "UnrealScript class body (defaultproperties block with class code)"
    if not in_spec:
        simulated = len(UC_SIMULATED_RE.findall(text))
        if simulated >= UC_BODY_LIMIT and len(UC_LOCAL_RE.findall(text)) >= UC_BODY_LIMIT:
            return "UnrealScript function bodies (simulated functions with local declarations)"

    bytecode = len(UC_BYTECODE_RE.findall(text))
    if bytecode >= UC_BYTECODE_LIMIT:
        return f"UnrealScript bytecode listing ({bytecode} jump offsets or cast tokens)"

    asm_lines = len(ASM_LINE_RE.findall(text))
    if asm_lines >= ASM_LINE_LIMIT:
        return f"disassembly listing ({asm_lines} address plus instruction lines)"
    return None


# Characters Windows does not allow in file names, and the private-use code points (U+F000 to U+F0FF) that Git for
# Windows and WSL write in their place. A name with any of them is almost always a shell slip, such as a '>' inside
# quoted code read as a redirect.
WINDOWS_RESERVED = set('<>:"|?*')


def bad_filename_reason(path: str) -> str | None:
    """Why a tracked path's name looks like a mistake, or None."""
    for part in PurePosixPath(path).parts:
        reserved = sorted({c for c in part if c in WINDOWS_RESERVED})
        mapped = sorted({f"U+{ord(c):04X}" for c in part if 0xF000 <= ord(c) <= 0xF0FF})
        if reserved or mapped:
            what = ", ".join([repr(c) for c in reserved] + mapped)
            return f"name contains characters Windows does not allow ({what}); likely a shell redirect slip"
        if part != part.strip() or part.endswith("."):
            return "name starts or ends with a space, or ends with a dot (not portable to Windows)"
    return None


def check_entry(entry: Entry) -> list[Finding]:
    """Applies every rule to one file."""
    findings: list[Finding] = []

    def add(rule: str, message: str) -> None:
        findings.append(Finding(entry.path, rule, message))

    low = entry.path.lower()
    pure = PurePosixPath(low)
    ext = pure.suffix

    for prefix in LOCAL_PREFIXES:
        if low.startswith(prefix):
            add("local-path", f"under {prefix}, which holds local-only data")
            break
    bad_name = bad_filename_reason(entry.path)
    if bad_name:
        add("bad-filename", bad_name)
    if any(part.endswith(".rep") for part in pure.parts[:-1]):
        add("ghidra-project", "inside a Ghidra project folder (*.rep)")
    if ext in GHIDRA_EXTENSIONS:
        add("ghidra-project", f"Ghidra project file ({ext})")
    if ext in GAME_EXTENSIONS:
        add("game-extension", f"original game file type ({ext})")
    media_allowed = _starts_with_any(low, MEDIA_ALLOWED_PREFIXES)
    media_reported = False
    if ext in MEDIA_EXTENSIONS and not media_allowed:
        add("media", f"{ext} file outside fixtures/, docs/images/ and game/branding/")
        media_reported = True
    if entry.size > MAX_BYTES:
        add("size", f"{entry.size / (1024 * 1024):.1f} MB, over the 5 MB limit")

    if entry.is_link:
        return findings

    data = entry.load(MAX_BYTES if entry.size <= MAX_BYTES else HEAD_BYTES)
    head = data[:HEAD_BYTES]
    if head.startswith(UE_PACKAGE_MAGIC):
        add("ue-package", "starts with the UE package tag C1 83 2A 9E")
    if _is_pe(head):
        add("pe-binary", "Windows executable or DLL")
    if not media_allowed and not media_reported:
        kind = _media_kind(head)
        if kind:
            add("media", f"{kind} content outside fixtures/, docs/images/ and game/branding/")

    if entry.size <= MAX_BYTES:
        text = _decode_text(data)
        if text is not None:
            reason = decompiled_reason(low, text)
            if reason:
                add("decompiled", reason)
    return findings


def _file_entry(full: Path, rel: str) -> Entry | None:
    """Builds an Entry for a file on disk, or None when it is missing or a folder (submodule)."""
    try:
        info = os.lstat(full)
    except FileNotFoundError:
        return None
    if stat.S_ISDIR(info.st_mode):
        return None
    if stat.S_ISLNK(info.st_mode):
        return Entry(rel, 0, lambda _n: b"", is_link=True)

    def load(n: int) -> bytes:
        with open(full, "rb") as handle:
            return handle.read(n)

    return Entry(rel, info.st_size, load)


def _git(root: Path, *args: str) -> bytes:
    try:
        proc = subprocess.run(["git", "-C", str(root), *args], capture_output=True, check=False)
    except FileNotFoundError as exc:
        raise GuardError("git was not found on PATH") from exc
    if proc.returncode != 0:
        detail = proc.stderr.decode("utf-8", errors="replace").strip()
        raise GuardError(f"git {' '.join(args)} failed: {detail}")
    return proc.stdout


def repo_root(start: Path) -> Path:
    out = _git(start, "rev-parse", "--show-toplevel")
    return Path(out.decode("utf-8", errors="surrogateescape").strip())


def _split_z(raw: bytes) -> list[str]:
    return [part.decode("utf-8", errors="surrogateescape") for part in raw.split(b"\0") if part]


def iter_worktree_entries(root: Path) -> Iterator[Entry]:
    """Files git tracks or would add, read from the working tree. Deleted files are skipped."""
    seen: set[str] = set()
    for rel in _split_z(_git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard")):
        if rel in seen:
            continue
        seen.add(rel)
        entry = _file_entry(root / rel, rel)
        if entry is not None:
            yield entry


def _read_exact(stream: IO[bytes], size: int, keep: int) -> bytes:
    """Reads size bytes from stream and returns the first keep of them."""
    kept = bytearray()
    remaining = size
    while remaining > 0:
        chunk = stream.read(min(remaining, 1 << 20))
        if not chunk:
            raise GuardError("git cat-file ended early")
        if len(kept) < keep:
            kept.extend(chunk[: keep - len(kept)])
        remaining -= len(chunk)
    return bytes(kept)


def iter_staged_entries(root: Path) -> Iterator[Entry]:
    """Staged content of files that the next commit adds or changes."""
    raw = _git(
        root, "diff", "--cached", "--raw", "-z", "--no-abbrev", "--no-renames", "--diff-filter=ACMT"
    )
    parts = _split_z(raw)
    staged: list[tuple[str, str, str]] = []  # (path, mode, object id)
    for i in range(0, len(parts) - 1, 2):
        fields = parts[i].lstrip(":").split()
        if len(fields) < 5:
            raise GuardError(f"unexpected git diff output: {parts[i]!r}")
        staged.append((parts[i + 1], fields[1], fields[3]))
    if not staged:
        return

    proc = subprocess.Popen(
        ["git", "-C", str(root), "cat-file", "--batch"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
    )
    assert proc.stdin is not None and proc.stdout is not None
    try:
        for rel, mode, oid in staged:
            if mode == "160000":
                continue  # submodule commit, nothing to read
            if mode == "120000":
                yield Entry(rel, 0, lambda _n: b"", is_link=True)
                continue
            proc.stdin.write(oid.encode("ascii") + b"\n")
            proc.stdin.flush()
            header = proc.stdout.readline().split()
            if len(header) != 3 or header[1] != b"blob":
                raise GuardError(f"git cat-file could not read {rel}")
            size = int(header[2])
            keep = MAX_BYTES if size <= MAX_BYTES else HEAD_BYTES
            content = _read_exact(proc.stdout, size, keep)
            proc.stdout.read(1)  # newline after each object
            yield Entry(rel, size, lambda n, data=content: data[:n])
    finally:
        proc.stdin.close()
        proc.stdout.close()
        proc.wait()


def iter_tree_entries(base: Path, excludes: Sequence[str] = ()) -> Iterator[Entry]:
    """Every file under base except .git folders and paths matching an exclude glob."""

    def excluded(rel: str) -> bool:
        return any(fnmatch.fnmatchcase(rel, pattern) for pattern in excludes)

    for dirpath, dirnames, filenames in os.walk(base):
        rel_dir = Path(dirpath).relative_to(base).as_posix()
        prefix = "" if rel_dir == "." else rel_dir + "/"
        dirnames[:] = sorted(d for d in dirnames if d != ".git" and not excluded(prefix + d))
        for name in sorted(filenames):
            rel = prefix + name
            if excluded(rel):
                continue
            entry = _file_entry(Path(dirpath) / name, rel)
            if entry is not None:
                yield entry


def scan(entries: Iterable[Entry]) -> tuple[int, list[Finding]]:
    count = 0
    findings: list[Finding] = []
    for entry in entries:
        count += 1
        findings.extend(check_entry(entry))
    return count, findings


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="no_game_data",
        description="Fails when game files, decompiled output or local-only data would end up in git.",
    )
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--staged", action="store_true", help="check the staged content (pre-commit)")
    mode.add_argument("--all", metavar="DIR", help="check every file under DIR, ignoring git")
    parser.add_argument(
        "--root", metavar="DIR", help="repository to check (default: the one around the current directory)"
    )
    parser.add_argument(
        "--exclude",
        metavar="GLOB",
        action="append",
        default=[],
        help="with --all: skip paths matching GLOB (fnmatch, * also matches /); repeatable",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(errors="backslashreplace")  # type: ignore[union-attr]
    except AttributeError:
        pass
    parser = _parser()
    args = parser.parse_args(argv)
    try:
        if args.exclude and not args.all:
            raise GuardError("--exclude only works with --all")
        if args.all:
            base = Path(args.all)
            if not base.is_dir():
                raise GuardError(f"{base} is not a directory")
            where = str(base)
            entries: Iterable[Entry] = iter_tree_entries(base, args.exclude)
        else:
            root = repo_root(Path(args.root) if args.root else Path.cwd())
            where = f"{root} (staged)" if args.staged else str(root)
            entries = iter_staged_entries(root) if args.staged else iter_worktree_entries(root)
        count, findings = scan(entries)
    except GuardError as exc:
        print(f"no_game_data: {exc}", file=sys.stderr)
        return 2

    if not findings:
        print(f"no_game_data: {count} files checked in {where}, no problems")
        return 0
    files = len({f.path for f in findings})
    try:
        print(f"no_game_data: {len(findings)} problem(s) in {files} of {count} files in {where}")
        for finding in findings:
            print(f"  {finding.path}: [{finding.rule}] {finding.message}")
        print(
            "Game files, decompiled output and converted content stay in local/ or .content/ "
            "(see re/PROVENANCE.md)."
        )
    except (BrokenPipeError, OSError):
        pass  # output closed early (for example piped into head)
    return 1


if __name__ == "__main__":
    sys.exit(main())
