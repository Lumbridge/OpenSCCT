"""sim/traversal.json of a converted map, from scctformats.ge (the GE partition of the Level export).

Every GE in original list order: id GE<index>, kind, points (UE space), mask (the GE ID byte), platform, the
editor flag word and whether it is active at round start under LevelInfo.GEActivationMask. The search tree is not
copied: the C3 spike showed it is a pure function of the GE list, which the engine rebuilds.
"""

from __future__ import annotations

from ..svmap import LATEST_VERSION, ge_active
from .source import MapSource


def build_traversal(src: MapSource, activation_mask: int) -> tuple[dict, dict]:
    ges = []
    counts: dict[str, int] = {}
    part = src.partition
    for g in (part.ges if part is not None else []):
        ges.append({
            "id": f"GE{g.index}",
            "kind": g.kind,
            "points": [[float(c) for c in p] for p in g.points],
            "mask": int(g.id),
            "platform": int(g.platform),
            "flags": int(g.flags),
            "activeAtStart": ge_active(int(g.id), activation_mask),
            "src": {"kind": "level", "index": int(g.index)},
        })
        counts[g.kind] = counts.get(g.kind, 0) + 1
    doc = {"formatVersion": LATEST_VERSION, "map": src.name, "activationMask": int(activation_mask), "ges": ges}
    stats = {"ges": len(ges), "byKind": dict(sorted(counts.items())),
             "masked": sum(1 for g in ges if g["mask"]), "inactiveAtStart": sum(1 for g in ges if not g["activeAtStart"])}
    if part is not None:
        stats["partitionVersion"] = part.version
    return doc, stats
