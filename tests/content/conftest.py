"""pytest setup for the content pipeline tests: python -m pytest tests/content"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "content"))

TESTBOX = REPO / "fixtures" / "testbox"


@pytest.fixture
def testbox_dir() -> Path:
    return TESTBOX


@pytest.fixture
def repo_root() -> Path:
    return REPO
