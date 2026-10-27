"""Writes re/ghidra/annotations/transfer.json from the matcher output (plain Python 3.10).

Input: local/ghidra-work/transfer/match/matches.json (match_names.py without --holdout). For every
accepted pair whose editor function has a name and whose game function has none yet (in game.json,
physics.json or net.json), the editor name is carried over. Two corrections are applied:

* Copied guard strings: the editor names some overrides with another class's guard string (two
  editor functions share 'AActor::PreNetReceive'; AZoneInfo's replication list carries
  'AProjector::GetOptimizedRepList'). When both builds put the function in the vtables of one
  topmost class only, and the named class has a known vtable of its own that does not hold the
  function, the namespace becomes that topmost class.
* Class spelling: a guard class name that is not a known class but matches one ignoring case or with the
  U or A prefix added is replaced by it ('AEMitter', 'GUIComponent').
* Folded functions are not named twice: a game address gets one name only.

Each function entry keeps the editor address and the acceptance rule, so a later run can be
compared or rolled back. Names, namespaces and short comments only; no code.
Run: python re/ghidra/scripts/transfer/write_transfer.py
"""

import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xfer  # noqa: E402

FORMAT = 'svm.ghidra.annotations/1'


def known_game_names():
    out = {}
    for ann in ('game', 'physics', 'net'):
        sec = xfer.annotation_programs(ann).get('game')
        if not sec:
            continue
        for fn in sec.get('functions', []):
            out[int(fn['address'], 16)] = xfer.full_name(fn)
        for lb in sec.get('labels', []):
            out.setdefault(int(lb['address'], 16), xfer.full_name(lb))
    return out


def class_tables(pid='game'):
    """(vtable address -> C++ class name, C++ class name -> super C++ class name) of a program."""
    path = os.path.join(xfer.REPO, 'local', 'reflect', '%s.json' % pid)
    data = xfer.load_json(path)
    classes = {c['path']: c for c in data['classes']}

    def is_actor(p):
        seen = 0
        while p and seen < 64:
            if p == 'Engine.Actor':
                return True
            p = classes.get(p, {}).get('super')
            seen += 1
        return False

    def cpp(p):
        return ('A' if is_actor(p) else 'U') + p.split('.')[-1]

    by_vt = collections.defaultdict(set)
    for p, c in classes.items():
        if c.get('vtable'):
            by_vt[int(c['vtable'], 16)].add(p)
    vt_class = {}
    for vt, paths in by_vt.items():
        # The vtable belongs to the topmost class among those that use it (script subclasses
        # share their native parent's table).
        top = [p for p in paths if classes[p].get('super') not in paths]
        if len(top) == 1:
            vt_class[vt] = cpp(top[0])
    supers = {}
    for p, c in classes.items():
        if c.get('super'):
            supers[cpp(p)] = cpp(c['super'])
    return vt_class, supers


def ancestors(cls, supers):
    out = []
    while cls in supers and len(out) < 64:
        cls = supers[cls]
        out.append(cls)
    return out


def main():
    mpath = os.path.join(xfer.WORK, 'matches.json')
    data = xfer.load_json(mpath)
    game_feats = xfer.load_json(os.path.join(xfer.TRANSFER_OUT, 'game.features.json'))
    if xfer.RE_TOOLS not in sys.path:
        sys.path.insert(0, xfer.RE_TOOLS)
    import binlock  # noqa: E402
    lock = binlock.load_lock()
    lock_entry = next(f for f in lock['files'] if f['id'] == 'game')
    known = known_game_names()
    vt_class, supers = class_tables()
    evt_class, _ = class_tables('editor')
    editor_feats = xfer.load_json(os.path.join(xfer.TRANSFER_OUT, 'editor.features.json'))
    editor_homes = collections.defaultdict(set)   # editor function -> classes whose vtable holds it
    image = xfer.PeImage(binlock.locked_path('editor', lock=lock))
    for vt in editor_feats['vtables']:
        cls = evt_class.get(vt['a'])
        if not cls:
            continue
        for s in vt['slots']:
            t = s
            for _ in range(4):
                j = image.jump_target(t) if image is not None else None
                if j is None:
                    break
                t = j
            editor_homes[t].add(cls)
    slots_of = collections.defaultdict(set)   # game function -> classes whose vtable holds it
    vt_slots = {}
    for vt in game_feats['vtables']:
        vt_slots[vt['a']] = vt['slots']
        cls = vt_class.get(vt['a'])
        if cls:
            for i, s in enumerate(vt['slots']):
                slots_of[s].add((cls, i))
    class_vt = {c: a for a, c in vt_class.items()}
    known_classes = set(supers) | set(supers.values()) | set(vt_class.values())
    canonical = {c.lower(): c for c in known_classes}
    spelled = []

    functions, comments, namespaces = [], [], set()
    fixed = []
    skipped = collections.Counter()
    for rec in data['matches']:
        ga = int(rec['game'], 16)
        name = rec.get('editorName')
        if not name:
            skipped['unnamed editor'] += 1
            continue
        if ga in known or rec.get('gameName'):
            skipped['already named'] += 1
            continue
        ns, short = xfer.split_name(name)
        # Guard strings sometimes misspell the class ('AEMitter') or drop its prefix ('GUIComponent').
        if ns and ns not in known_classes:
            fixed_ns = canonical.get(ns.lower()) or canonical.get(('u' + ns).lower()) or canonical.get(('a' + ns).lower())
            if fixed_ns:
                spelled.append({'address': '0x%08x' % ga, 'editorName': name, 'name': fixed_ns + '::' + short})
                ns = fixed_ns
        homes = slots_of.get(ga, set())
        if ns and homes:
            owners = {c for c, _ in homes}
            tops = [c for c in owners if not any(a in owners for a in ancestors(c, supers))]
            # Both builds must put the function in the vtable of the same class (a slot list can run
            # past the end of a table into an interface table), and the named class must have a
            # known vtable of its own that does not hold the function.
            ea = int(rec['editor'], 16)
            if (len(tops) == 1 and tops[0] != ns and tops[0] in editor_homes.get(ea, ())
                    and ns not in editor_homes.get(ea, ()) and class_vt.get(ns) is not None
                    and ga not in vt_slots[class_vt[ns]]):
                top = tops[0]
                fixed.append({'address': '0x%08x' % ga, 'editorName': name, 'name': top + '::' + short})
                ns = top
        entry = {'address': '0x%08x' % ga, 'namespace': ns, 'name': short,
                 'editor': '0x' + rec['editor'], 'rule': rec['rule']}
        functions.append(entry)
        if ns:
            namespaces.add(ns)
        comments.append({'address': '0x%08x' % ga, 'type': 'plate',
                         'text': 'transfer: editor %s (%s)%s' % ('0x' + rec['editor'], rec['rule'],
                                                                  '' if ns == xfer.split_name(name)[0]
                                                                  else '; class corrected, editor guard says ' + name)})
    functions.sort(key=lambda f: f['address'])
    comments.sort(key=lambda c: c['address'])
    section = {'format': FORMAT, 'id': 'game', 'program': lock_entry['path'].split('/')[-1],
               'sha256': lock_entry['sha256'], 'imageBase': lock_entry['pe']['imageBase'],
               'counts': {'namespaces': len(namespaces), 'functions': len(functions), 'labels': 0,
                          'comments': len(comments)},
               'namespaces': [{'path': p, 'kind': 'class'} for p in sorted(namespaces)],
               'functions': functions, 'labels': [], 'comments': comments}
    out = {'format': FORMAT, 'id': 'transfer',
           'note': ('Editor names carried to the game build by re/ghidra/scripts/transfer/match_names.py; '
                    'precision and method in spec/native/ANSWERS-transfer.md. Per-program section in the '
                    'svm.ghidra.annotations/1 layout; apply with T70_apply_transfer.py.'),
           'programs': [section]}
    path = os.path.join(xfer.ANNOTATIONS, 'transfer.json')
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('{\n')
        for k in ('format', 'id', 'note'):
            fh.write(' %s: %s,\n' % (json.dumps(k), json.dumps(out[k])))
        fh.write(' "programs": [\n  {\n')
        keys = list(section.keys())
        for i, k in enumerate(keys):
            v = section[k]
            end = ',' if i < len(keys) - 1 else ''
            if isinstance(v, list):
                fh.write('   %s: [\n' % json.dumps(k))
                for j, item in enumerate(v):
                    fh.write('    %s%s\n' % (json.dumps(item), ',' if j < len(v) - 1 else ''))
                fh.write('   ]%s\n' % end)
            else:
                fh.write('   %s: %s%s\n' % (json.dumps(k), json.dumps(v), end))
        fh.write('  }\n ]\n}\n')
    with open(os.path.join(xfer.WORK, 'write-report.json'), 'w', encoding='utf-8', newline='\n') as fh:
        json.dump({'functions': len(functions), 'skipped': skipped, 'classFromVtable': fixed,
                   'classSpelling': spelled}, fh, indent=1)
    print('transfer: %d names (%d namespaces), %d class fixes from vtables, %d spelling fixes, skipped %s -> %s'
          % (len(functions), len(namespaces), len(fixed), len(spelled), dict(skipped), path))
    return 0


if __name__ == '__main__':
    sys.exit(main())
