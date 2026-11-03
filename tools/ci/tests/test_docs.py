"""Documentation checks against small synthetic repositories."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import docs


def write(root: Path, name: str, text: str) -> None:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def test_relative_links_anchors_and_code_examples(tmp_path):
    write(tmp_path, "README.md", """# Start
[Guide](docs/A%20Guide.md#build--run)
[Repeated heading](docs/A%20Guide.md#build--run-1)
[Custom anchor][custom]
[custom]: docs/A%20Guide.md#details
<img src="icon.svg" alt="Icon">
`[example](missing.md)`
```md
[example](missing.md)
```
    [indented example](missing.md)
""")
    write(tmp_path, "docs/A Guide.md", '# Build & run\n# Build & run\n<a id="details"></a>\n[Home](../README.md)')
    write(tmp_path, "icon.svg", "<svg/>")
    assert docs.check_links(tmp_path, {"README.md", "docs/A Guide.md", "icon.svg"}) == []


def test_missing_wrong_case_and_untracked_targets_fail(tmp_path):
    write(tmp_path, "README.md", """# Start
[Wrong case](guide.md)
[Missing heading](Guide.md#absent)
[Local output](local/report.md)
[Website](https://example.com/missing)
""")
    write(tmp_path, "Guide.md", "# Guide")
    write(tmp_path, "local/report.md", "# Local output exists only on this machine")
    errors = docs.check_links(tmp_path, {"README.md", "Guide.md"})
    assert len(errors) == 3
    assert any("wrong-case target: guide.md" in error for error in errors)
    assert any("missing heading: Guide.md#absent" in error for error in errors)
    assert any("target: local/report.md" in error for error in errors)


def report():
    row = dict(units=10, na=2, specced=6, ported=4, verified=2, bytes=100,
               naBytes=20, speccedBytes=60, portedBytes=40, verifiedBytes=10)
    return {"summary": [dict(key="all", **row)], "packages": [dict(key="SBase", **row)]}


def test_metrics_use_in_scope_denominators_and_matching_history():
    history = [dict(date="2026-01-01", units="10", na_units="2", in_scope_bytes="80",
                    specced_units="6", ported_units="4", verified_units="2",
                    specced_w="75.00", ported_w="50.00", verified_w="12.50")]
    # A newer unrelated snapshot must not relabel these figures.
    history.append(dict(history[0], date="2026-02-01", ported_units="5"))
    history.append(dict(history[0], date="2026-03-01", ported_w="55.00"))
    block = docs.coverage_block(report(), history)
    assert "Snapshot: **2026-01-01**" in block
    assert "| 8 | 6 (75.0%) | 4 (50.0%) | 2 (25.0%) | 50.0% |" in block
    assert "date unavailable" in docs.coverage_block(report(), [])


def test_empty_scope_has_no_misleading_percentage():
    data = report()
    for row in data["summary"] + data["packages"]:
        row.update(na=10, naBytes=100)
    assert "| 0 | n/a | n/a | n/a | n/a |" in docs.coverage_block(data, [])


def test_cli_checks_without_writing_then_refreshes_only_metrics(tmp_path, monkeypatch):
    subprocess.run(["git", "init", "-q", str(tmp_path)], check=True)
    original = f"# Project\n\n{docs.START}\nstale\n{docs.END}\n\nKeep this text.\n"
    write(tmp_path, "README.md", original)
    write(tmp_path, "docs/progress/coverage.json", json.dumps(report()))
    write(tmp_path, "docs/progress/history.csv", "date,units,na_units,in_scope_bytes,specced_units,ported_units,verified_units\n")
    monkeypatch.setattr(docs, "ROOT", tmp_path)
    monkeypatch.setattr(sys, "argv", ["docs.py"])
    assert docs.main() == 1
    assert (tmp_path / "README.md").read_text() == original
    monkeypatch.setattr(sys, "argv", ["docs.py", "--write"])
    assert docs.main() == 0
    updated = (tmp_path / "README.md").read_text()
    assert updated.startswith("# Project\n\n" + docs.START)
    assert updated.endswith(docs.END + "\n\nKeep this text.\n")
    monkeypatch.setattr(sys, "argv", ["docs.py"])
    assert docs.main() == 0
    assert (tmp_path / "README.md").read_text() == updated
