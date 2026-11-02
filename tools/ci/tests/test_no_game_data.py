"""Tests for tools/ci/no_game_data.py on synthetic files in temp folders.

Samples that would trip the guard are assembled at run time, so this file stays clean itself.
"""

from __future__ import annotations

import os
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

CI_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CI_DIR))

import no_game_data as ngd  # noqa: E402

MB = 1024 * 1024
needs_git = pytest.mark.skipif(shutil.which("git") is None, reason="git is not installed")


def write(base: Path, rel: str, data: bytes | str) -> Path:
    path = base / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(data, str):
        data = data.encode("utf-8")
    path.write_bytes(data)
    return path


def rules_for(base: Path, rel: str, data: bytes | str) -> list[str]:
    """Writes one file and returns the rules it breaks."""
    write(base, rel, data)
    _, findings = ngd.scan(ngd.iter_tree_entries(base))
    return sorted(f.rule for f in findings if f.path == rel)


def all_findings(base: Path) -> list[ngd.Finding]:
    return ngd.scan(ngd.iter_tree_entries(base))[1]


# Synthetic samples


def ghidra_c(functions: int = 3) -> str:
    """A fake decompiled C listing with the usual Ghidra names."""
    out = []
    for i in range(functions):
        addr = 0x10A00000 + i * 0x40
        out.append(f"undefined4 {'FUN'}_{addr:08x}(int *{'param'}_1, int {'param'}_2)")
        out.append("{")
        out.append(f"  undefined4 {'u'}Var1;")
        out.append(f"  int {'i'}Var2;")
        out.append(f"  {'i'}Var2 = *(int *)({'param'}_1 + 0x1a4);")
        out.append(f"  {'u'}Var1 = {'FUN'}_{addr + 0x1000:08x}({'i'}Var2, {'DAT'}_{0x10cc9e80 + i:08x});")
        out.append(f"  return {'u'}Var1;")
        out.append("}")
    return "\n".join(out) + "\n"


def ghidra_names_only(count: int) -> str:
    """A list of unnamed functions, like an index or notes file, with no decompiled bodies."""
    return "\n".join(f'{{"addr": "0x{0x10A00000 + i:08x}", "name": "{"FUN"}_{0x10A00000 + i:08x}"}}' for i in range(count))


def ghidra_bodies_only(count: int) -> str:
    return "\n".join(f"{'u'}Var{i} = {'param'}_{i % 4 + 1};" for i in range(count)) + "\n"


def unrealscript_class(name: str = "SFakeThing") -> str:
    lines = [
        f"{'class'} {name} {'extends'} Actor;",
        "",
        "var float Speed;",
        "",
        f"{'simulated'} function PostBeginPlay()",
        "{",
        "    Super.PostBeginPlay();",
        "}",
        "",
        "default" + "properties",
        "{",
        "    Speed=1.0",
        "}",
    ]
    return "\n".join(lines) + "\n"


def unrealscript_functions(count: int) -> str:
    lines = []
    for i in range(count):
        lines += [f"{'simulated'} function Step{i}(float Delta)", "{", f"    {'local'} vector Dir;", "    Dir = Velocity;", "}"]
    return "\n".join(lines) + "\n"


def disassembly(lines: int) -> str:
    return "\n".join(f"{0x10900000 + i * 4:08x}  8b 4c 24 04   {'mov'} ecx, dword ptr [esp+4]" for i in range(lines)) + "\n"


def pe_header() -> bytes:
    data = bytearray(0x200)
    data[0:2] = b"MZ"
    data[0x3C:0x40] = (0x80).to_bytes(4, "little")
    data[0x80:0x84] = b"PE\0\0"
    return bytes(data)


CLEAN_CS = """namespace OpenScct.Core;

/// <summary>Plain code that the guard must leave alone.</summary>
public static class Example
{
    public static int Add(int a, int b) => a + b;
}
"""

CLEAN_MD = """# Notes

This file talks about the decompiler, defaultproperties blocks and simulated functions in prose.
Ghidra calls unnamed functions FUN_ plus an address, and leaves unknown types as undefined.
"""


# Clean input


def test_clean_tree_passes(tmp_path: Path) -> None:
    write(tmp_path, "src/OpenScct.Core/Example.cs", CLEAN_CS)
    write(tmp_path, "docs/NOTES.md", CLEAN_MD)
    write(tmp_path, "README.md", "# Readme\n")
    write(tmp_path, "fixtures/testbox/floor.png", b"\x89PNG\r\n\x1a\n" + b"\0" * 32)
    write(tmp_path, "docs/images/layout.png", b"\x89PNG\r\n\x1a\n" + b"\0" * 32)
    assert all_findings(tmp_path) == []


def test_empty_file_passes(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "empty.txt", b"") == []


# Binary and path rules


def test_ue_package_magic_with_any_extension(tmp_path: Path) -> None:
    data = bytes((0xC1, 0x83, 0x2A, 0x9E)) + b"\x2c\x01" + b"\0" * 64
    assert rules_for(tmp_path, "data/blob.bin", data) == ["ue-package"]


def test_magic_only_counts_at_offset_zero(tmp_path: Path) -> None:
    data = b"\0\0\0\0" + bytes((0xC1, 0x83, 0x2A, 0x9E)) + b"\0" * 64
    assert rules_for(tmp_path, "data/blob.bin", data) == []


@pytest.mark.parametrize(
    "ext", [".sdc", ".usx", ".utx", ".utc", ".ukx", ".uax", ".uas", ".mux", ".u", ".int", ".SDC", ".Int"]
)
def test_game_extensions(tmp_path: Path, ext: str) -> None:
    assert rules_for(tmp_path, f"maps/SteeE{ext}", "text") == ["game-extension"]


def test_similar_extensions_are_fine(tmp_path: Path) -> None:
    for rel in ("notes.uc.md", "src/Interop.cs", "data/units.json", "script.ui", "x.integration"):
        assert rules_for(tmp_path, rel, "text") == [], rel


@pytest.mark.parametrize("rel", ["re/ghidra/scct.gpr", "re/ghidra/scct.gzf", "re/ghidra/scct.rep/idata/00/x.prp"])
def test_ghidra_project_files(tmp_path: Path, rel: str) -> None:
    assert rules_for(tmp_path, rel, "text") == ["ghidra-project"]


@pytest.mark.parametrize(
    "rel",
    [
        "local/traces/run1.json",
        ".content/maps/SteeE/manifest.json",
        "tools/godot/4.7.2-stable/readme.txt",
        "re/.venv-ghidra/pyvenv.cfg",
        "game/.godot/imported/x.import",
        "LOCAL/upper.txt",
    ],
)
def test_local_only_paths(tmp_path: Path, rel: str) -> None:
    assert rules_for(tmp_path, rel, "text") == ["local-path"]


@pytest.mark.parametrize("rel", ["docs/local/x.md", "relocal/x.txt", "tools/godotlike/x.txt", "content/x.json"])
def test_paths_that_only_look_local(tmp_path: Path, rel: str) -> None:
    assert rules_for(tmp_path, rel, "text") == []


@pytest.mark.parametrize("ext", [".dds", ".glb", ".ogg", ".wav", ".png", ".PNG", ".jpg", ".tga"])
def test_media_extensions_outside_allowed_dirs(tmp_path: Path, ext: str) -> None:
    assert rules_for(tmp_path, f"game/art/thing{ext}", b"x") == ["media"]


@pytest.mark.parametrize("prefix", ["fixtures/testbox/", "docs/images/"])
def test_media_allowed_in_fixtures_and_doc_images(tmp_path: Path, prefix: str) -> None:
    assert rules_for(tmp_path, f"{prefix}thing.dds", b"DDS " + b"\0" * 124) == []


@pytest.mark.parametrize(
    "data",
    [
        b"DDS " + b"\0" * 124,
        b"glTF\x02\0\0\0" + b"\0" * 16,
        b"OggS" + b"\0" * 24,
        b"RIFF\x24\0\0\0WAVEfmt " + b"\0" * 16,
        b"\x89PNG\r\n\x1a\n" + b"\0" * 16,
    ],
)
def test_media_content_with_a_neutral_extension(tmp_path: Path, data: bytes) -> None:
    assert rules_for(tmp_path, "data/payload.dat", data) == ["media"]


def test_media_reported_once(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "art/tex.dds", b"DDS " + b"\0" * 124) == ["media"]


def test_size_limit(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "data/at_limit.txt", b"a" * (5 * MB)) == []
    assert rules_for(tmp_path, "data/over_limit.txt", b"a" * (5 * MB + 1)) == ["size"]


def test_size_limit_applies_in_fixtures(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "fixtures/big.bin", b"\0" * (5 * MB + 1)) == ["size"]


@pytest.mark.parametrize("prefix", ["", "contenttools/repo/"])
def test_reviewed_art_requires_exact_path_size_and_hash(tmp_path: Path, prefix: str) -> None:
    path = "content/assets/sidearms/export/reviewed.png"
    data = b"\x89PNG\r\n\x1a\n" + b"a" * (5 * MB)
    approval = {path: {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}}
    write(tmp_path, prefix + path, data)
    assert ngd.scan(ngd.iter_tree_entries(tmp_path), approval)[1] == []
    write(tmp_path, prefix + path, data[:-1] + b"b")
    assert sorted(f.rule for f in ngd.scan(ngd.iter_tree_entries(tmp_path), approval)[1]) == ["media", "size"]
    write(tmp_path, prefix + path, data)
    write(tmp_path, prefix + path.replace("reviewed", "unreviewed"), data)
    findings = ngd.scan(ngd.iter_tree_entries(tmp_path), approval)[1]
    assert all("unreviewed" in f.path for f in findings)
    assert sorted(f.rule for f in findings) == ["media", "size"]


@pytest.mark.parametrize("kind", ["package", "executable"])
def test_reviewed_art_cannot_exempt_original_packages_or_executables(tmp_path: Path, kind: str) -> None:
    path = "content/assets/sidearms/export/payload.glb"
    data = ngd.UE_PACKAGE_MAGIC + b"\0" * 100 if kind == "package" else pe_header()
    write(tmp_path, path, data)
    approval = {path: {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}}
    assert [f.rule for f in ngd.scan(ngd.iter_tree_entries(tmp_path), approval)[1]] == ["ue-package" if kind == "package" else "pe-binary"]


def test_art_manifest_normalizes_path_case(tmp_path: Path) -> None:
    path = "content/assets/sidearms/export/OpenSCCT.png"
    data = b"\x89PNG\r\n\x1a\n" + b"x" * 100
    manifest = {"license": "CC-BY-4.0", "credits": "content/assets/sidearms/CREDITS.txt",
                "assets": {path: {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}}}
    write(tmp_path, path, data)
    assert ngd.scan(ngd.iter_tree_entries(tmp_path), ngd.load_art_manifest(json.dumps(manifest).encode()))[1] == []


def test_cosmetic_art_requires_credits_and_exact_source_hash(tmp_path: Path) -> None:
    path = 'content/assets/cosmetics/textures/woodland.png'
    data = b'\x89PNG\r\n\x1a\n' + b'original artwork' * 10
    manifest = {'license': 'Original generated OpenSCCT artwork',
                'credits': 'content/assets/sidearms/CREDITS.txt',
                'assets': {path: {'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}}}
    with pytest.raises(ngd.GuardError, match='cosmetic assets require their own credits'):
        ngd.load_art_manifest(json.dumps(manifest).encode())
    manifest['additional_credits'] = ['content/assets/cosmetics/CREDITS.txt']
    approval = ngd.load_art_manifest(json.dumps(manifest).encode())
    write(tmp_path, path, data)
    assert ngd.scan(ngd.iter_tree_entries(tmp_path), approval)[1] == []
    write(tmp_path, path, data[:-1] + b'x')
    assert [finding.rule for finding in ngd.scan(ngd.iter_tree_entries(tmp_path), approval)[1]] == ['media']


def test_large_file_still_gets_magic_check(tmp_path: Path) -> None:
    data = bytes((0xC1, 0x83, 0x2A, 0x9E)) + b"\0" * (6 * MB)
    assert rules_for(tmp_path, "data/huge.bin", data) == ["size", "ue-package"]


def test_pe_binary(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "System/SCCT Versus", pe_header()) == ["pe-binary"]


def test_mz_without_pe_signature_is_fine(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "notes/mz.txt", "MZ is a two letter prefix\n") == []


# Text heuristics


def test_ghidra_c_listing(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "notes/native.c", ghidra_c()) == ["decompiled"]


def test_ghidra_c_listing_renamed_to_markdown(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "docs/physics.md", ghidra_c()) == ["decompiled"]


def test_ghidra_warning_banner(tmp_path: Path) -> None:
    text = f"/* {'WARNING'}: Could not recover jumptable */\nvoid {'FUN'}_10a0f000(void) {{ {'u'}Var1 = 0; }}\n"
    assert rules_for(tmp_path, "notes/x.c", text) == ["decompiled"]


def test_a_few_ghidra_names_are_fine(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "docs/notes.md", ghidra_names_only(5)) == []


def test_many_ghidra_names_fail_outside_spec(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "docs/notes.md", ghidra_names_only(40)) == ["decompiled"]


def test_spec_may_cite_ghidra_names(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "spec/physics/ledge.md", ghidra_names_only(40)) == []


def test_spec_may_not_hold_decompiled_bodies(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "spec/physics/ledge.md", ghidra_bodies_only(20)) == ["decompiled"]


def test_annotations_may_hold_ghidra_names_and_types(tmp_path: Path) -> None:
    text = ghidra_names_only(60) + "\n" + "\n".join(f'"field_{i:x}": "{"undefined"}4"' for i in range(60))
    assert rules_for(tmp_path, "re/ghidra/annotations/natives.json", text) == []


@pytest.mark.parametrize(
    "banner",
    ["// Decompiled by ucdecomp 0.1", "/* Decompiled with some tool */", "# disassembled by sdis", " * " + "generated by Eliot" + ".UELib"],
)
def test_tool_banners(tmp_path: Path, banner: str) -> None:
    assert rules_for(tmp_path, "notes/out.txt", banner + "\nint x;\n") == ["decompiled"]


def test_banner_words_mid_sentence_are_fine(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "docs/x.md", "The functions are decompiled by our tool into local/ only.\n") == []


def test_utf16_text_is_checked(tmp_path: Path) -> None:
    data = ("// " + "Decompiled" + " by tool\nint x;\n").encode("utf-16")
    assert rules_for(tmp_path, "notes/wide.txt", data) == ["decompiled"]


def test_unrealscript_class(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "notes/SFakeThing.uc", unrealscript_class()) == ["decompiled"]


def test_unrealscript_class_in_spec_is_left_alone(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "spec/actors/SFakeThing.md", unrealscript_class()) == []


def test_defaultproperties_alone_is_fine(tmp_path: Path) -> None:
    text = "default" + "properties\n{\n    Speed=1.0\n}\n"
    assert rules_for(tmp_path, "notes/defaults.txt", text) == []


def test_unrealscript_function_bodies(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "notes/funcs.txt", unrealscript_functions(6)) == ["decompiled"]
    assert rules_for(tmp_path, "notes/few.txt", unrealscript_functions(2)) == []


def bytecode_listing(lines: int) -> str:
    rows = [f'"{i * 8:04x}  if !(({"Byte"}ToInt(Role) < 4)) {"goto"} @{i * 8 + 8:04x}",' for i in range(lines)]
    return "[\n" + "\n".join(rows) + '\n"0000  ' + "return" + ' nothing"]\n'


def test_unrealscript_bytecode_listing(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "data/dis.json", bytecode_listing(15)) == ["decompiled"]
    assert rules_for(tmp_path, "data/short.json", bytecode_listing(3)) == []


def test_disassembly_listing(tmp_path: Path) -> None:
    assert rules_for(tmp_path, "notes/listing.txt", disassembly(30)) == ["decompiled"]
    assert rules_for(tmp_path, "notes/short.txt", disassembly(5)) == []


def test_synthetic_marker_lifts_text_rules_only(tmp_path: Path) -> None:
    marker = "// " + "no-game-data" + ": synthetic\n"
    assert rules_for(tmp_path, "re/tools/ucdecomp/tests/sample.uc", marker + unrealscript_class()) == []
    assert rules_for(tmp_path, "re/tools/ucdecomp/tests/sample.utx", marker + unrealscript_class()) == ["game-extension"]


def test_synthetic_marker_must_be_its_own_comment_line(tmp_path: Path) -> None:
    text = "This file is not " + "no-game-data" + ": synthetic at all\n" + unrealscript_class()
    assert rules_for(tmp_path, "notes/x.uc", text) == ["decompiled"]


def test_binary_files_skip_text_rules(tmp_path: Path) -> None:
    data = b"\0\1\2" + ghidra_c().encode("utf-8")
    assert rules_for(tmp_path, "data/x.bin", data) == []


# Tree mode


def test_tree_mode_skips_git_folder_and_excludes(tmp_path: Path) -> None:
    write(tmp_path, ".git/objects/pack/x.pack", bytes((0xC1, 0x83, 0x2A, 0x9E)))
    write(tmp_path, "bin/Debug/app.dll", pe_header())
    write(tmp_path, "nested/bin/x.dll", pe_header())
    write(tmp_path, "keep.txt", "fine\n")
    assert [f.path for f in all_findings(tmp_path)] == ["bin/Debug/app.dll", "nested/bin/x.dll"]
    _, findings = ngd.scan(ngd.iter_tree_entries(tmp_path, ["bin", "*/bin"]))
    assert findings == []


@pytest.mark.skipif(os.name == "nt", reason="symlinks need extra rights on Windows")
def test_symlinks_are_not_followed(tmp_path: Path) -> None:
    target = write(tmp_path, "outside/game.u", "x")
    (tmp_path / "tree").mkdir()
    (tmp_path / "tree" / "link.txt").symlink_to(target)
    _, findings = ngd.scan(ngd.iter_tree_entries(tmp_path / "tree"))
    assert findings == []


def test_main_exit_codes(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    write(tmp_path, "ok.txt", "fine\n")
    assert ngd.main(["--all", str(tmp_path)]) == 0
    write(tmp_path, "maps/SteeE.sdc", "x")
    assert ngd.main(["--all", str(tmp_path)]) == 1
    out = capsys.readouterr().out
    assert "maps/SteeE.sdc: [game-extension]" in out
    assert ngd.main(["--all", str(tmp_path / "missing")]) == 2
    assert ngd.main(["--exclude", "x"]) == 2


def test_guard_sources_are_clean() -> None:
    """The guard and its tests must pass their own checks."""
    _, findings = ngd.scan(ngd.iter_tree_entries(CI_DIR, ["*__pycache__*", "*.pytest_cache*"]))
    assert findings == []


# Git modes


def git(repo: Path, *args: str) -> None:
    subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True)


@pytest.fixture
def repo(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    monkeypatch.setenv("GIT_CONFIG_GLOBAL", os.devnull)
    monkeypatch.setenv("GIT_CONFIG_NOSYSTEM", "1")
    root = tmp_path / "repo"
    root.mkdir()
    git(root, "init", "-q")
    git(root, "config", "core.autocrlf", "false")
    return root


def worktree_paths(repo: Path) -> list[tuple[str, str]]:
    _, findings = ngd.scan(ngd.iter_worktree_entries(repo))
    return sorted((f.path, f.rule) for f in findings)


def staged_paths(repo: Path) -> list[tuple[str, str]]:
    _, findings = ngd.scan(ngd.iter_staged_entries(repo))
    return sorted((f.path, f.rule) for f in findings)


@needs_git
def test_staged_reviewed_art_uses_its_complete_blob_and_staged_provenance(repo: Path) -> None:
    path = "content/assets/sidearms/export/reviewed.png"
    data = b"\x89PNG\r\n\x1a\n" + b"a" * (5 * MB)
    manifest = {"license": "CC-BY-4.0", "credits": "content/assets/sidearms/CREDITS.txt",
                "assets": {path: {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}}}
    art = write(repo, path, data)
    provenance = write(repo, ngd.ART_MANIFEST, json.dumps(manifest))
    git(repo, "add", path, ngd.ART_MANIFEST)
    # Working-tree edits cannot change what the staged check approves.
    art.write_bytes(data[:-1] + b"b")
    provenance.write_text("{}", encoding="utf-8")
    assert ngd.main(["--root", str(repo), "--staged"]) == 0
    git(repo, "add", path)
    assert ngd.main(["--root", str(repo), "--staged"]) == 1


@needs_git
def test_worktree_mode_sees_tracked_and_untracked_but_not_ignored(repo: Path) -> None:
    write(repo, ".gitignore", "ignored/\n")
    write(repo, "tracked.sdc", "x")
    write(repo, "untracked.ukx", "x")
    write(repo, "ignored/also.utx", "x")
    git(repo, "add", ".gitignore", "tracked.sdc")
    assert worktree_paths(repo) == [("tracked.sdc", "game-extension"), ("untracked.ukx", "game-extension")]


@needs_git
def test_worktree_mode_skips_deleted_files(repo: Path) -> None:
    path = write(repo, "gone.txt", "fine\n")
    git(repo, "add", "gone.txt")
    path.unlink()
    assert worktree_paths(repo) == []


@needs_git
def test_staged_mode_reads_the_index_not_the_working_tree(repo: Path) -> None:
    path = write(repo, "notes/native.c", ghidra_c())
    git(repo, "add", "notes/native.c")
    path.write_text("clean now\n", encoding="utf-8")
    write(repo, "untracked.sdc", "x")
    assert staged_paths(repo) == [("notes/native.c", "decompiled")]


@needs_git
def test_staged_mode_passes_clean_index_with_dirty_worktree(repo: Path) -> None:
    path = write(repo, "notes/native.c", "clean\n")
    git(repo, "add", "notes/native.c")
    path.write_text(ghidra_c(), encoding="utf-8")
    assert staged_paths(repo) == []


@needs_git
def test_staged_mode_after_first_commit_and_with_large_blob(repo: Path) -> None:
    write(repo, "a.txt", "a\n")
    git(repo, "add", "a.txt")
    git(repo, "-c", "user.name=t", "-c", "user.email=t@example.invalid", "commit", "-q", "-m", "init")
    write(repo, "a.txt", "changed\n")
    write(repo, "big.bin", bytes((0xC1, 0x83, 0x2A, 0x9E)) + b"\0" * (6 * MB))
    write(repo, "sub dir/x y.png", b"\x89PNG\r\n\x1a\n")
    git(repo, "add", "-A")
    git(repo, "rm", "-q", "--cached", "a.txt")
    assert staged_paths(repo) == [
        ("big.bin", "size"),
        ("big.bin", "ue-package"),
        ("sub dir/x y.png", "media"),
    ]


@needs_git
def test_main_in_git_modes(repo: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    write(repo, "ok.txt", "fine\n")
    git(repo, "add", "ok.txt")
    assert ngd.main(["--root", str(repo)]) == 0
    assert ngd.main(["--staged", "--root", str(repo)]) == 0
    write(repo, "local/trace.json", "{}")
    assert ngd.main(["--root", str(repo)]) == 1
    monkeypatch.chdir(repo / "local")
    assert ngd.main([]) == 1


@pytest.mark.parametrize("path", [
    ', t, f.getName(True) if f else None)}',
    'tools/out>log.txt',
    'docs/what?.md',
    'src/a:b.cs',
    'notes/trailing space ',
    'notes/ends.with.dot.',
])
def test_bad_filenames_are_rejected(path: str) -> None:
    assert ngd.bad_filename_reason(path) is not None


@pytest.mark.parametrize("path", [
    "src/OpenScct.Core/Engine/Actor.cs",
    "tools/scctpkg/patches/0001-sky-zone-backdrop-walls.patch",
    "local/build/Play SteeE - spy.cmd",
    "docs/spikes/R4-sim-vs-game.md",
])
def test_ordinary_filenames_pass(path: str) -> None:
    assert ngd.bad_filename_reason(path) is None
