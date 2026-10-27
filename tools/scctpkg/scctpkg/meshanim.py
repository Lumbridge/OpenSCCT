"""UMeshAnimation reader for SCCT Versus (.ukx), reverse-engineered on this build.

    tagged properties (None)
    i32 Version (0)
    TArray<FNamedBone>: paired name, u32 flags, i32 (bone id, not a parent index)
    TArray<MotionChunk> Moves:
        12 bytes (unknown floats), f32 TrackTime (frame count), i32 StartBone, u32 Flags,
        TArray<i32> BoneIndices (into RefBones),
        TArray<AnalogTrack> AnimTracks:
            u32 Flags,
            TArray<i16[4]> KeyQuat  (x, y, z, w) / 32767
            TArray<i16[3]> KeyPos   / 32   (game units)
            TArray<u16>    KeyTime  (frame index)
        AnalogTrack RootTrack (usually empty)
    TArray<FMeshAnimSeq> AnimSeqs (see read_anim_seqs; layout probed at runtime)
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

import numpy as np

from .package import Export, Package, PackageError, Reader
from .properties import read_properties, skip_state_frame

POS_SCALE = 1.0 / 32.0
QUAT_SCALE = 1.0 / 32767.0


@dataclass
class Track:
    quats: np.ndarray      # (n, 4) float32 x, y, z, w
    positions: np.ndarray  # (m, 3) float32
    times: np.ndarray      # (k,) int frames


@dataclass
class MotionChunk:
    num_frames: float
    start_bone: int
    flags: int
    bone_indices: List[int]
    tracks: List[Track]


@dataclass
class AnimSeq:
    name: str
    start_frame: int
    num_frames: int
    rate: float
    groups: List[str] = field(default_factory=list)


@dataclass
class MeshAnimation:
    name: str
    bone_names: List[str]
    moves: List[MotionChunk]
    seqs: List[AnimSeq]


def _read_track(r: Reader) -> Track:
    r.u32()
    nq = r.compact()
    if nq < 0 or nq > 100000:
        raise PackageError(f"bad quat key count {nq} at {r.p}")
    q = np.frombuffer(r.raw(nq * 8), dtype=np.int16).reshape(nq, 4).astype(np.float32) * QUAT_SCALE if nq else np.zeros((0, 4), np.float32)
    npos = r.compact()
    if npos < 0 or npos > 100000:
        raise PackageError(f"bad pos key count {npos} at {r.p}")
    pos = np.frombuffer(r.raw(npos * 6), dtype=np.int16).reshape(npos, 3).astype(np.float32) * POS_SCALE if npos else np.zeros((0, 3), np.float32)
    nk = r.compact()
    if nk < 0 or nk > 100000:
        raise PackageError(f"bad key time count {nk} at {r.p}")
    times = np.frombuffer(r.raw(nk * 2), dtype=np.uint16).astype(np.int32) if nk else np.zeros((0,), np.int32)
    return Track(q, pos, times)


def read_mesh_animation(pkg: Package, e: Export) -> MeshAnimation:
    d = pkg.data[e.offset:e.offset + e.size]
    r = Reader(d, 0, pkg, base=e.offset)
    skip_state_frame(r, e.flags)
    read_properties(r)
    r.i32()  # version
    nb = r.compact()
    if nb <= 0 or nb > 512:
        raise PackageError(f"{e.name}: bad bone count {nb}")
    names = []
    for _ in range(nb):
        names.append(r.name())
        r.u32()
        r.i32()
    nm = r.compact()
    if nm < 0 or nm > 4096:
        raise PackageError(f"{e.name}: bad move count {nm}")
    moves: List[MotionChunk] = []
    for _ in range(nm):
        r.raw(12)
        track_time = r.f32()
        start_bone = r.i32()
        flags = r.u32()
        nbi = r.compact()
        if nbi < 0 or nbi > 512:
            raise PackageError(f"{e.name}: bad bone index count {nbi}")
        bidx = [r.i32() for _ in range(nbi)]
        nt = r.compact()
        if nt < 0 or nt > 512:
            raise PackageError(f"{e.name}: bad track count {nt}")
        tracks = [_read_track(r) for _ in range(nt)]
        _read_track(r)  # root track
        moves.append(MotionChunk(track_time, start_bone, flags, bidx, tracks))
    seqs = read_anim_seqs(r, len(moves))
    return MeshAnimation(e.name, names, moves, seqs)


def read_anim_seqs(r: Reader, expected: int) -> List[AnimSeq]:
    """FMeshAnimSeq (this build): f32 (blend/extra), paired name, TArray<name> Groups,
    i32 StartFrame, i32 NumFrames, TArray<{f32 Time, u16 pad, compact Notify}>, f32 Rate."""
    n = r.compact()
    if n < 0 or n > 4096:
        raise PackageError(f"bad anim seq count {n} at {r.p}")
    out: List[AnimSeq] = []
    for _ in range(n):
        r.f32()
        name = r.name()
        ng = r.compact()
        if ng < 0 or ng > 64:
            raise PackageError(f"bad group count {ng} at {r.p}")
        groups = [r.name() for _ in range(ng)]
        start = r.i32()
        frames = r.i32()
        nn = r.compact()
        if nn < 0 or nn > 512:
            raise PackageError(f"bad notify count {nn} at {r.p}")
        for _ in range(nn):
            r.f32()
            r.u16()
            r.compact()
        rate = r.f32()
        out.append(AnimSeq(name, start, frames, rate, groups))
    return out
