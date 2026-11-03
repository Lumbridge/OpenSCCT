"""Keep issue form IDs stable and feature prefills compatible with GitHub."""

from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[3]


def test_report_prefill_fields_match_the_templates():
    source = (ROOT / "shared/ProjectLinks.cs").read_text(encoding="utf-8")
    assert '"&environment="' in source
    assert 'template=feature_request.yml' in source
    for filename, preserved in (
        ("bug_report.yml", {"version", "steps", "result", "evidence"}),
        ("feature_request.yml", {"problem", "proposal", "context"}),
    ):
        form = (ROOT / ".github/ISSUE_TEMPLATE" / filename).read_text(encoding="utf-8")
        fields = re.findall(r"^\s+id:\s*(\S+)\s*$", form, re.MULTILINE)
        assert len(fields) == len(set(fields)), "Issue form field IDs must be unique"
        assert preserved | {"environment"} <= set(fields)
        environment = next(block for block in re.split(r"\n  - type:", form) if "id: environment" in block)
        assert "required: true" not in environment, "Context must remain optional for reports opened directly on GitHub"
