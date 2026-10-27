"""Multichannel maps (.mux, Packages/Sounds/MC_*.mux): which mono stream feeds which speaker.

An INI text: a [Channels] section with FL, FR, BL, BR, Center and LFE keys, each naming a stream (empty: silent).
The PC banks hold one stereo stream named after the multichannel Sound instead of the per-speaker streams, so the
map is kept as data (the Sound's channel mask says which keys are used).
"""

from __future__ import annotations

from .sound import CHANNELS


def read_mux(text: str) -> dict[str, str]:
    out: dict[str, str] = {}
    section = None
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith(";"):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1].strip().lower()
            continue
        if section != "channels" or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        for c in CHANNELS:
            if c.lower() == key.lower():
                out[c] = value.strip()
    return {c: out[c] for c in CHANNELS if c in out}
