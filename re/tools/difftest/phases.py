"""Phases of a recorded run: the stretches with one controller state and one Physics mode, with their timing,
motion and animation, and how the runs of one scenario agree on them.

    python -I -B re/tools/difftest/phases.py local/difftest/golden-traversal/traversal.spy-ledge-grab [--json]
    python -I -B re/tools/difftest/phases.py local/difftest/golden-traversal            # every scenario folder

Per phase: controller state, Physics, first and last frame, duration (TimeSeconds from the phase's first frame
to the next phase's first frame), start and end Location, net horizontal and vertical displacement and their
rates, the channel-0 sequences seen, the root-motion lock modes seen and the GE held (traces with the traversal
extras). Across runs: whether the phase lists match, and for each phase the spread (max - min) of its start
frame, duration and displacement. The numbers come from game-derived traces and stay under local/.
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import trace as tr  # noqa: E402


def phases(frames):
    """[{cs, phys, f0, f1, t0, t1, ...}] over the played frames (f >= 0); t1 is the next phase's start time (or
    the last frame's time for the last phase)."""
    played = [fr for fr in frames if fr['f'] >= 0]
    groups = []
    for fr in played:
        key = (fr.get('cs'), fr.get('phys'))
        if not groups or groups[-1][0] != key:
            groups.append((key, []))
        groups[-1][1].append(fr)
    out = []
    for i, ((cs, phys), fs) in enumerate(groups):
        # A phase ends where the next one starts: its end time and position are the next phase's first sample.
        stop = groups[i + 1][1][0] if i + 1 < len(groups) else fs[-1]
        first = fs[0]
        p = {'cs': cs, 'phys': phys, 'f0': first['f'], 'f1': fs[-1]['f'], 't0': first['t'], 't1': stop['t']}
        out.append(p)
        p['seconds'] = p['t1'] - p['t0']
        a, b = first['loc'], stop['loc']
        p['loc0'], p['loc1'] = a, b
        p['dxy'] = math.hypot(b[0] - a[0], b[1] - a[1])
        p['dz'] = b[2] - a[2]
        p['rateXY'] = p['dxy'] / p['seconds'] if p['seconds'] > 0 else 0.0
        p['rateZ'] = p['dz'] / p['seconds'] if p['seconds'] > 0 else 0.0
        seqs = []
        for fr in fs:
            ch0 = next((c for c in fr.get('anim') or [] if c[0] == 0), None)
            if ch0 and (not seqs or seqs[-1] != ch0[1]):
                seqs.append(ch0[1])
        p['anim0'] = seqs
        locks = sorted({(fr.get('rm') or {}).get('mode', 0) for fr in fs if fr.get('rm') is not None})
        if locks:
            p['lock'] = locks
        ges = sorted({(fr.get('tv') or {}).get('ge') for fr in fs if fr.get('tv')} - {None})
        if ges:
            p['ge'] = ges
    return out


def describe(p):
    extra = ''
    if 'lock' in p:
        extra += ' lock %s' % p['lock']
    if 'ge' in p:
        extra += ' ge %s' % p['ge']
    return ('f%4d-%4d %6.3fs %-30s phys %2d  dxy %7.1f dz %7.1f  (%6.1f, %6.1f uu/s)  %s%s' % (
        p['f0'], p['f1'], p['seconds'], p['cs'], p['phys'], p['dxy'], p['dz'], p['rateXY'], p['rateZ'],
        ' > '.join(p['anim0'][:6]), extra))


def compare(runs):
    """{'same': bool, 'sequences': {run: [(cs, phys), ...]}, 'spread': [{cs, phys, f0, seconds, dxy, dz}]}: the
    spread (max - min over the runs) of each phase when every run went through the same phases."""
    seqs = {n: [(p['cs'], p['phys']) for p in ps] for n, ps in runs.items()}
    same = len({json.dumps(s) for s in seqs.values()}) <= 1
    out = {'same': same, 'sequences': seqs}
    if same and runs:
        n_ph = len(next(iter(runs.values())))
        spread = []
        for i in range(n_ph):
            row = {'cs': next(iter(runs.values()))[i]['cs'], 'phys': next(iter(runs.values()))[i]['phys']}
            for key in ('f0', 'seconds', 'dxy', 'dz'):
                vals = [ps[i][key] for ps in runs.values()]
                row[key] = [min(vals), max(vals)]
            spread.append(row)
        out['spread'] = spread
    return out


def folder_report(folder):
    runs = {}
    for path in sorted(glob.glob(os.path.join(folder, 'run*.jsonl.gz'))):
        _, frames, _ = tr.read(path)
        runs[os.path.basename(path).split('.')[0]] = phases(frames)
    return runs, compare(runs)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('paths', nargs='+', help='scenario folders (run*.jsonl.gz inside), a golden root, or traces')
    ap.add_argument('--json', action='store_true')
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')
    folders = []
    for p in args.paths:
        if os.path.isdir(p) and glob.glob(os.path.join(p, 'run*.jsonl.gz')):
            folders.append(p)
        elif os.path.isdir(p):
            folders += sorted(d for d in glob.glob(os.path.join(p, '*')) if glob.glob(os.path.join(d, 'run*.jsonl.gz')))
    report = {}
    for folder in folders:
        runs, cmp_ = folder_report(folder)
        name = os.path.basename(os.path.normpath(folder))
        report[name] = {'runs': runs, 'compare': cmp_}
        if args.json:
            continue
        print('== %s: %d runs, phases %s' % (name, len(runs), 'the same' if cmp_['same'] else 'DIFFER'))
        for run, ps in runs.items():
            print('-- %s' % run)
            for p in ps:
                print('   ' + describe(p))
        if cmp_.get('spread'):
            print('-- spread over runs (max - min): start frame, seconds, dxy, dz')
            for row in cmp_['spread']:
                print('   %-30s phys %2d  f0 %4d..%4d  %.4f..%.4f s  dxy %.2f  dz %.2f' % (
                    row['cs'], row['phys'], row['f0'][0], row['f0'][1], row['seconds'][0], row['seconds'][1],
                    row['dxy'][1] - row['dxy'][0], row['dz'][1] - row['dz'][0]))
    if args.json:
        print(json.dumps(report, indent=1, default=str))


if __name__ == '__main__':
    main()
