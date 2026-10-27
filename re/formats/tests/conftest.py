import sys
from pathlib import Path

# Make `import scctformats` work when pytest runs from the repo root or from re/formats.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
