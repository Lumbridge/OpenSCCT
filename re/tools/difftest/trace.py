"""svm.trace/1: one recorded run as gzip-compressed JSON lines.

Line 1 is the header ({"format": "svm.trace/1", ...}: scenario, run, map and binary hashes, frame
rate, field offsets), then one line per frame ({"f": frame, "t": TimeSeconds, ...}), then a footer
({"end": true, ...}). Floats are written as the shortest text that reads back to the same float32,
so nothing is lost and the files stay small. tests/scenarios/README.md lists every field.

Also here: the metrics two runs are compared with (the same definitions as the C# comparator in
tests/OpenScct.DiffTest) and the noise envelope of a set of runs of one scenario.
"""
from __future__ import annotations

import gzip
import io
import itertools
import json
import math
import struct

FORMAT = 'svm.trace/1'
ENVELOPE_FORMAT = 'svm.envelope/1'


def f32(value):
    """The shortest decimal text of a float32 value, as a float that json writes exactly so."""
    if value is None or not math.isfinite(value):
        return value
    bits = struct.pack('<f', value)
    for digits in range(6, 10):
        text = '%.*g' % (digits, value)
        if struct.pack('<f', float(text)) == bits:
            return float(text)
    return float('%.9g' % value)


def _clean(value):
    if isinstance(value, float):
        return f32(value)
    if isinstance(value, (list, tuple)):
        return [_clean(v) for v in value]
    if isinstance(value, dict):
        return {k: _clean(v) for k, v in value.items()}
    return value


class TraceWriter:
    def __init__(self, path, header):
        self.path = path
        self.tmp = path + '.part'
        self.raw = open(self.tmp, 'wb')
        # mtime 0 so equal content gives equal bytes
        self.gz = gzip.GzipFile(filename='', mode='wb', fileobj=self.raw, mtime=0)
        self.out = io.TextIOWrapper(self.gz, encoding='utf-8', newline='\n')
        self.frames = 0
        self._line(dict({'format': FORMAT}, **header))

    def _line(self, obj):
        self.out.write(json.dumps(obj, separators=(',', ':'), ensure_ascii=False))
        self.out.write('\n')

    def frame(self, sample):
        """Floats in samples are float32 values read from the game: written exactly."""
        self._line(_clean(sample))
        self.frames += 1

    def close(self, footer):
        import os
        self._line(dict({'end': True, 'frames': self.frames}, **footer))
        self.out.close()
        self.gz.close()
        self.raw.close()
        os.replace(self.tmp, self.path)


def read(path):
    """(header, frames, footer) of a trace file."""
    with gzip.open(path, 'rt', encoding='utf-8') as f:
        lines = [json.loads(line) for line in f if line.strip()]
    if not lines or lines[0].get('format') != FORMAT:
        raise ValueError('%s is not a %s file' % (path, FORMAT))
    footer = lines[-1] if lines[-1].get('end') else None
    frames = lines[1:-1] if footer else lines[1:]
    return lines[0], frames, footer


# ---- comparison -----------------------------------------------------------------------------------

def _series(frames, key):
    t0 = frames[0]['t']
    return [(fr['t'] - t0, fr[key]) for fr in frames if fr.get(key) is not None and fr['f'] >= 0]


def _interp(series, t):
    """Linear interpolation of a [(time, vector)] series at time t (clamped at the ends)."""
    if t <= series[0][0]:
        return series[0][1]
    if t >= series[-1][0]:
        return series[-1][1]
    lo, hi = 0, len(series) - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if series[mid][0] <= t:
            lo = mid
        else:
            hi = mid
    (ta, a), (tb, b) = series[lo], series[hi]
    w = 0.0 if tb == ta else (t - ta) / (tb - ta)
    return [x + (y - x) * w for x, y in zip(a, b)]


def _dist(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def metrics(a_frames, b_frames):
    """Run b against run a, on a's frame times from frame 0 (times taken relative to each run's
    frame 0, b interpolated linearly): RMS and maximum of the location distance, the location
    distance at the last common time, RMS of the velocity difference."""
    a0 = [fr for fr in a_frames if fr['f'] >= 0]
    b0 = [fr for fr in b_frames if fr['f'] >= 0]
    if len(a0) < 2 or len(b0) < 2:
        return None
    la, lb = _series(a0, 'loc'), _series(b0, 'loc')
    va, vb = _series(a0, 'vel'), _series(b0, 'vel')
    end = min(la[-1][0], lb[-1][0])
    d_loc, d_vel = [], []
    for (t, pa), (_, ua) in zip(la, va):
        if t > end + 1e-9:
            break
        d_loc.append(_dist(pa, _interp(lb, t)))
        d_vel.append(_dist(ua, _interp(vb, t)))
    rms = lambda xs: math.sqrt(sum(x * x for x in xs) / len(xs)) if xs else 0.0  # noqa: E731
    return {'locRms': rms(d_loc), 'locMax': max(d_loc), 'locEnd': _dist(_interp(la, end), _interp(lb, end)),
            'velRms': rms(d_vel), 'samples': len(d_loc), 'seconds': end}


def state_sequence(frames, key):
    """The states a run went through, consecutive repeats removed."""
    return [k for k, _ in itertools.groupby(fr.get(key) for fr in frames if fr['f'] >= 0)]


def envelope(runs):
    """Noise envelope of the runs ({name: frames}) of one scenario: every pair compared, the worst
    of each metric kept, the medoid run (smallest summed location RMS to the others) named as the
    reference, and whether the pawn and controller went through the same states."""
    names = sorted(runs)
    pairs = []
    total = {n: 0.0 for n in names}
    for a, b in itertools.combinations(names, 2):
        m = metrics(runs[a], runs[b])
        if m is None:
            continue
        pairs.append(dict({'a': a, 'b': b}, **{k: round(v, 4) if isinstance(v, float) else v for k, v in m.items()}))
        total[a] += m['locRms']
        total[b] += m['locRms']
    worst = {}
    for key in ('locRms', 'locMax', 'locEnd', 'velRms'):
        worst[key] = max((p[key] for p in pairs), default=0.0)
    states = {}
    for key, label in (('ps', 'pawn'), ('cs', 'controller'), ('phys', 'physics')):
        seqs = {n: state_sequence(runs[n], key) for n in names}
        states[label] = {'same': len({json.dumps(s) for s in seqs.values()}) <= 1, 'runs': seqs}
    return {'format': ENVELOPE_FORMAT, 'runs': names, 'reference': min(names, key=lambda n: total[n]) if names else None,
            'pairs': pairs, 'envelope': worst, 'states': states}
