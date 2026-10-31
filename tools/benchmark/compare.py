"""Compare matching FPS captures; original throughput uses the independent Present counter."""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('original')
    parser.add_argument('openscct')
    parser.add_argument('--before')
    parser.add_argument('--json-out')
    parser.add_argument('--require-parity', action='store_true')
    args = parser.parse_args()
    original = json.loads(Path(args.original).read_text())
    current = json.loads(Path(args.openscct).read_text())
    before = json.loads(Path(args.before).read_text()) if args.before else None
    if bool(original.get('gameplay')) != bool(current.get('gameplay')):
        parser.error('Cannot compare render-only and gameplay captures')
    for field in ('map', 'side'):
        if field in current and original.get(field) != current[field]:
            parser.error(field+' mismatch')
    if current.get('bots', 0):
        parser.error('Original Play Level has no equivalent bots')
    refs = {c['id']: c for c in original['cases']}
    olds = {c['id']: c for c in before['cases']} if before else {}
    if set(refs) != {c['id'] for c in current['cases']}:
        parser.error('Captures must contain the same camera IDs')
    rows = []
    print('| View | Original FPS | OpenSCCT FPS | vs original | Original 1% low | OpenSCCT 1% low |')
    print('|---|---:|---:|---:|---:|---:|')
    for case in current['cases']:
        ref = refs[case['id']]
        if (case['width'], case['height']) != (ref['width'], ref['height']):
            parser.error('Resolution mismatch for '+case['id'])
        fps = ref.get('presentFps', ref['fps'])
        row = dict(id=case['id'], originalFps=fps, openscctFps=case['fps'],
                   gainPercent=(case['fps']/fps-1)*100,
                   originalLow1Percent=ref['low1Percent'], openscctLow1Percent=case['low1Percent'])
        if case['id'] in olds:
            row['beforeFps'] = olds[case['id']]['fps']
            row['improvementPercent'] = (case['fps']/row['beforeFps']-1)*100
        rows.append(row)
        print(f"| {row['id']} | {fps:.0f} | {case['fps']:.0f} | {row['gainPercent']:+.1f}% | {ref['low1Percent']:.0f} | {case['low1Percent']:.0f} |")
    gain = (math.prod(r['openscctFps']/r['originalFps'] for r in rows)**(1/len(rows))-1)*100
    report = dict(format='svm.fps-comparison/1', original=str(Path(args.original).resolve()),
                  openscct=str(Path(args.openscct).resolve()), gameplay=bool(current.get('gameplay')),
                  geometricMeanGainPercent=gain, cases=rows)
    print(f'\nGeometric mean vs original: {gain:+.1f}%')
    if args.json_out:
        Path(args.json_out).write_text(json.dumps(report, indent=2))
    if args.require_parity and any(r['openscctFps'] < r['originalFps'] or r['openscctLow1Percent'] < r['originalLow1Percent'] for r in rows):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
