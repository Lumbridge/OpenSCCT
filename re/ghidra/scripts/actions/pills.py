"""Reads the bone collision capsules and spheres ("pills") of skeletal meshes from a game package.

Plain Python on top of tools/scctpkg. The layout follows the editor's USkeletalMesh serializer (see
spec/native/hit-zones.md): after the reference skeleton come the default animation reference, the
skeletal depth, two bone tables, two name arrays, a raw 0x30-byte table, then (package version
above 124) the collision points (bone and offset), the capsules and the spheres. Capsule: point A,
point B, radius, pill tag and, from licensee version 9, a priority. Sphere: point, radius, pill tag
and the same priority.

The result holds data read from the game's packages, so it is written only under local/:

    python re/ghidra/scripts/actions/pills.py "<game dir>" [SPerso] [ATT_01 DEF_01]
    -> local/re-out/c/actions/pills-<package>.json
"""

import json
import os
import struct
import sys
from pathlib import Path

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
sys.path.insert(0, os.path.join(REPO, 'tools', 'scctpkg'))

from scctpkg.package import Package, Reader, read_compact  # noqa: E402
from scctpkg.properties import read_properties, skip_state_frame  # noqa: E402
from scctpkg.skeletalmesh import _find_skeleton  # noqa: E402


def read_pills(pkg, e):
    d = pkg.data[e.offset:e.offset + e.size]
    r = Reader(d, 0, pkg, base=e.offset)
    skip_state_frame(r, e.flags)
    read_properties(r)
    _, bones, end = _find_skeleton(d, pkg, e.offset, r.p)
    r = Reader(d, end, pkg, base=e.offset)
    r.compact()                      # default animation
    r.i32()                          # skeletal depth
    n = r.compact(); r.raw(n * 16)   # FVector + int per entry
    n = r.compact(); r.raw(n * 4)    # two words per entry
    for _ in range(2):               # two FName arrays
        n = r.compact()
        for _ in range(n):
            r.compact()              # name table index (empty in the character meshes)
    n = r.compact(); r.raw(n * 0x30)
    caps, sph = [], []
    prio = pkg.licensee >= 9
    points = []
    if pkg.version > 124:
        n = r.compact()
        for _ in range(n):               # collision points: bone and an offset in the bone's space
            b = r.i32()
            points.append({'bone': bones[b].name if 0 <= b < len(bones) else b,
                           'offset': [round(r.f32(), 3) for _ in range(3)]})
        n = r.compact()
        for _ in range(n):
            a, b = r.i32(), r.i32()
            rad = r.f32()
            tag = r.i32()
            pri = r.i32() if prio else 1
            caps.append({'pointA': a, 'pointB': b,
                         'boneA': points[a]['bone'] if 0 <= a < len(points) else a,
                         'boneB': points[b]['bone'] if 0 <= b < len(points) else b,
                         'radius': round(rad, 3), 'tag': tag, 'priority': pri})
        n = r.compact()
        for _ in range(n):
            a = r.i32()
            rad = r.f32()
            tag = r.i32()
            pri = r.i32() if prio else 1
            sph.append({'point': a, 'bone': points[a]['bone'] if 0 <= a < len(points) else a, 'radius': round(rad, 3),
                        'tag': tag, 'priority': pri})
    return {'mesh': e.name, 'bones': len(bones), 'points': points, 'capsules': caps, 'spheres': sph,
            'version': pkg.version, 'licensee': pkg.licensee}


def main():
    game = Path(sys.argv[1])
    package = sys.argv[2] if len(sys.argv) > 2 else 'SPerso'
    meshes = sys.argv[3:] or None
    pkg = Package.load(game / 'Packages' / 'Animations' / ('%s.ukx' % package))
    out = []
    for e in pkg.exports_of_class('SkeletalMesh'):
        if e.size <= 0 or (meshes and e.name not in meshes):
            continue
        try:
            out.append(read_pills(pkg, e))
        except Exception as ex:  # noqa: BLE001 - report and continue
            out.append({'mesh': e.name, 'error': '%s: %s' % (type(ex).__name__, ex)})
    dest = Path(REPO) / 'local' / 're-out' / 'c' / 'actions' / ('pills-%s.json' % package)
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(out, indent=1), encoding='utf-8')
    for m in out:
        print(m['mesh'], m.get('error') or ('%d capsules, %d spheres' % (len(m['capsules']), len(m['spheres']))))


if __name__ == '__main__':
    main()
