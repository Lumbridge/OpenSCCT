"""UMeshAnimation with the parts scctpkg.meshanim drops: sequence notifies, the root track and the leading float.

scctpkg/meshanim.py documents the layout. Its sequence reader reads each notify as `f32, u16, compact` and discards
it; the record is really FMeshAnimNotify:

    f32 Time          normalized over the sequence (0..1)
    name Function     paired name reference (two compacts; 00 00 is the name None)
    objref Notify     the UAnimNotify object (AnimNotify_Trigger, AnimNotify_Sound, AnimNotify_Script, ...)

so a notify whose Function is a script function name (not None) has a longer record than three bytes plus four. The
sequence record is: f32 (unknown, kept as `lead`), name, Groups, i32 StartFrame, i32 NumFrames, Notifys, f32 Rate.
Checked on sqt13_anm.ukx (every export consumed to its last byte, notify objects naming AnimNotify exports).

The motion chunk's RootTrack (empty in the shipped sets seen so far) is kept, and so is each sequence's root bone
track (bone index 0 of the chunk), which is what PHYS_RootMotion moves the actor by.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional

import numpy as np

from .env import import_formats


@dataclass
class Notify:
    time: float
    function: str
    notify_ref: int


@dataclass
class Sequence:
    name: str
    lead: float
    groups: List[str]
    start_frame: int
    num_frames: int
    rate: float
    notifies: List[Notify] = field(default_factory=list)


@dataclass
class Chunk:
    track_time: float
    start_bone: int
    flags: int
    bone_indices: List[int]
    tracks: list
    root_track: object


@dataclass
class Animation:
    name: str
    bones: List[str]
    chunks: List[Chunk]
    sequences: List[Sequence]


def read_animation(pkg, export) -> Animation:
    import_formats()
    from scctpkg import meshanim  # noqa: PLC0415
    from scctpkg.package import Reader  # noqa: PLC0415
    from scctpkg.properties import read_properties, skip_state_frame  # noqa: PLC0415

    d = pkg.data[export.offset:export.offset + export.size]
    r = Reader(d, 0, pkg, base=export.offset)
    skip_state_frame(r, export.flags)
    read_properties(r)
    r.i32()
    nb = r.compact()
    if not 0 < nb <= 512:
        raise ValueError(f"{export.name}: bad bone count {nb}")
    bones = []
    for _ in range(nb):
        bones.append(r.name())
        r.u32()
        r.i32()
    nm = r.compact()
    if not 0 <= nm <= 4096:
        raise ValueError(f"{export.name}: bad chunk count {nm}")
    chunks = []
    for _ in range(nm):
        r.raw(12)
        track_time = r.f32()
        start_bone = r.i32()
        flags = r.u32()
        nbi = r.compact()
        if not 0 <= nbi <= 512:
            raise ValueError(f"{export.name}: bad bone index count {nbi}")
        bidx = [r.i32() for _ in range(nbi)]
        nt = r.compact()
        if not 0 <= nt <= 512:
            raise ValueError(f"{export.name}: bad track count {nt}")
        tracks = [meshanim._read_track(r) for _ in range(nt)]
        root = meshanim._read_track(r)
        chunks.append(Chunk(track_time, start_bone, flags, bidx, tracks, root))
    n = r.compact()
    if not 0 <= n <= 4096:
        raise ValueError(f"{export.name}: bad sequence count {n}")
    seqs = []
    for _ in range(n):
        lead = r.f32()
        name = r.name()
        ng = r.compact()
        if not 0 <= ng <= 64:
            raise ValueError(f"{export.name}: bad group count {ng}")
        groups = [r.name() for _ in range(ng)]
        start = r.i32()
        frames = r.i32()
        nn = r.compact()
        if not 0 <= nn <= 4096:
            raise ValueError(f"{export.name}: bad notify count {nn}")
        notifies = []
        for _ in range(nn):
            t = r.f32()
            fn = r.name()
            ref = r.compact()
            notifies.append(Notify(t, fn, ref))
        rate = r.f32()
        seqs.append(Sequence(name, lead, groups, start, frames, rate, notifies))
    if r.remaining():
        raise ValueError(f"{export.name}: {r.remaining()} bytes left after the sequences")
    return Animation(export.name, bones, chunks, seqs)


def track_json(track, rate: Optional[float] = None) -> dict:
    """Keys of an analog track: quaternions (x, y, z, w), positions (uu) and key times (frames)."""
    q = np.asarray(track.quats, dtype=np.float32)
    p = np.asarray(track.positions, dtype=np.float32)
    t = np.asarray(track.times, dtype=np.int64)
    return {"rot": [[float(v) for v in row] for row in q], "pos": [[float(v) for v in row] for row in p],
            "time": [int(v) for v in t]}


def notify_seconds(t: float, frames: int, rate: float) -> float:
    """When a notify at normalized time t fires, in seconds of a sequence played at rate 1 (float32 maths)."""
    f32 = np.float32
    if rate <= 0:
        return 0.0
    return float(f32(f32(f32(t) * f32(frames)) / f32(rate)))
