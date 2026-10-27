"""Splits a decompiled ExecuteAction into one file per switch case, with readable annotations.

Plain Python (no Ghidra). Input: the decompiler output of ASBasePawn::ExecuteAction written by
ghserve.py (local only). Output: local/re-out/c/actions/cases/<prog>/<first case>.c per case group,
tails.c for the shared code after the switch, and index.json. Everything written stays local.

Annotations, all appended as comments so the original text is untouched:
- case labels get the SA_* constant names from the decompiled SBasePawn script;
- integer literals that are FName indices (PlayAnim sequences, bone names, *Anim fields) get the name;
- bool dword updates and tests get the script bool names from the reflection dump masks;
- setPhysics and RootMotionAdditionnalPhysics values get the EPhysics names.

    python re/ghidra/scripts/actions/cases.py editor [decompiled.c]
"""

import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
LOCAL = os.path.join(REPO, 'local')
OUT_ROOT = os.path.join(LOCAL, 're-out', 'c', 'actions')

ADDR = {'editor': '11215980', 'game': None}


def load_sa_names():
    """SA_* constants of SBasePawn from the local decompiled script."""
    path = os.path.join(LOCAL, 're-out', 'uc', 'SBase', 'SBasePawn.uc')
    names = {}
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = re.match(r'\s*const\s+(SA_\w+)\s*=\s*(\d+)\s*;', line)
            if m:
                names.setdefault(int(m.group(2)), []).append(m.group(1))
    return names


def load_reflection(prog):
    with open(os.path.join(LOCAL, 'reflect', prog + '.json'), encoding='utf-8') as f:
        d = json.load(f)
    names = {int(k): v for k, v in d['nameTable'].items()}
    by_owner = {}
    for pr in d['properties']:
        by_owner.setdefault(pr['owner'], []).append(pr)
    bools = {}      # 'bits_<first>' -> [(mask, name)]
    name_fields = set()
    for owner, props in by_owner.items():
        per_off = {}
        for pr in props:
            if pr['kind'] == 'BoolProperty':
                per_off.setdefault(int(pr['offset']), []).append((int(pr['bitMask'], 16), pr['name']))
            elif pr['kind'] == 'NameProperty':
                name_fields.add(pr['name'])
        for off, bits in per_off.items():
            bits.sort()
            bools.setdefault('bits_' + bits[0][1], set()).update(bits)
    # bool dwords span owners (a subclass adds bits to the parent's dword): merge by first name
    merged = {k: sorted(v) for k, v in bools.items()}
    phys = None
    for e in d['enums']:
        if e['path'] == 'Engine.Actor.EPhysics':
            phys = e['names']
    return names, merged, name_fields, phys


def bit_names(bits, mask):
    return [n for m, n in bits if m & mask]


def annotate(line, names, bools, name_fields, phys):
    notes = []
    # bool updates: x->bits_A = x->bits_A | M   /  & M  / & M | N
    for m in re.finditer(r'(bits_\w+) = [\w>\-\.\[\]()]*?\1((?: [|&] 0x[0-9a-f]+| [|&] \d+)+)', line):
        field = m.group(1)
        bits = bools.get(field)
        if not bits:
            continue
        for op, val in re.findall(r'([|&]) (0x[0-9a-f]+|\d+)', m.group(2)):
            v = int(val, 0)
            if op == '|':
                notes.append('set ' + ','.join(bit_names(bits, v)))
            else:
                notes.append('clear ' + ','.join(bit_names(bits, ~v & 0xffffffff)))
    for m in re.finditer(r'(bits_\w+) & (0x[0-9a-f]+|\d+)\)', line):
        bits = bools.get(m.group(1))
        if bits and ' = ' not in line.split(m.group(1))[0][-4:]:
            notes.append('test ' + ','.join(bit_names(bits, int(m.group(2), 0))))
    # FName literals in name contexts
    is_name_ctx = ('PlayAnim' in line or 'SetBlendParams' in line or 'iVar3 = 0x' in line
                   or re.search(r'->(\w+) = 0x', line) and re.search(r'->(\w+) = 0x', line).group(1) in name_fields)
    if is_name_ctx:
        for lit in re.findall(r'\b0x[0-9a-f]+\b', line):
            v = int(lit, 16)
            if 0x100 <= v < 0x200000 and v in names:
                notes.append('%s=%s' % (lit, names[v]))
    m = re.search(r'setPhysics_10c\)\(this,(0x[0-9a-f]+|\d+),', line)
    if m and phys:
        v = int(m.group(1), 0)
        if v < len(phys):
            notes.append(phys[v])
    m = re.search(r"RootMotionAdditionnalPhysics = '\\x([0-9a-f]+)'", line) or \
        re.search(r"RootMotionAdditionnalPhysics = '\\(\d)'", line)
    if m and phys:
        v = int(m.group(1), 16)
        if v < len(phys):
            notes.append('RMAP ' + phys[v])
    if notes:
        return line + '   /* ' + '; '.join(notes) + ' */'
    return line


def annotate_files(prog, paths):
    """--annotate: writes annotated copies of other decompiled functions to cases/<prog>/helpers/."""
    names, bools, name_fields, phys = load_reflection(prog)
    out_dir = os.path.join(OUT_ROOT, 'cases', prog, 'helpers')
    os.makedirs(out_dir, exist_ok=True)
    for path in paths:
        with open(path, encoding='utf-8') as f:
            text = f.read().split('\n')
        out = [annotate(l, names, bools, name_fields, phys) if i else
               '// svm-actions cases.py annotated copy (local only): ' + os.path.basename(path)
               for i, l in enumerate(text)]
        with open(os.path.join(out_dir, os.path.basename(path)), 'w', encoding='utf-8', newline='\n') as f:
            f.write('\n'.join(out))
    print('annotated', len(paths), '->', out_dir)


def main():
    if len(sys.argv) > 2 and sys.argv[2] == '--annotate':
        annotate_files(sys.argv[1], sys.argv[3:])
        return
    prog = sys.argv[1] if len(sys.argv) > 1 else 'editor'
    src = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        OUT_ROOT, prog, '%s_ASBasePawn__ExecuteAction.c' % ADDR[prog])
    with open(src, encoding='utf-8') as f:
        lines = f.read().split('\n')
    sa = load_sa_names()
    names, bools, name_fields, phys = load_reflection(prog)
    out_dir = os.path.join(OUT_ROOT, 'cases', prog)
    os.makedirs(out_dir, exist_ok=True)

    start = next(i for i, l in enumerate(lines) if re.match(r'\s*switch\s*\(\s*Action\s*\)', l))
    indent = len(lines[start]) - len(lines[start].lstrip())
    case_re = re.compile(r'^' + ' ' * indent + r'case (0x[0-9a-f]+|\d+):\s*$')
    end = next(i for i in range(start + 1, len(lines)) if lines[i] == ' ' * indent + '}')
    groups = []
    cur = None
    for i in range(start + 1, end):
        m = case_re.match(lines[i])
        if m:
            v = int(m.group(1), 0)
            if cur is None or cur['body']:
                cur = {'cases': [], 'body': [], 'line': i + 1}
                groups.append(cur)
            cur['cases'].append(v)
            continue
        if re.match(r'^' + ' ' * indent + r'default:\s*$', lines[i]):
            cur = {'cases': ['default'], 'body': [], 'line': i + 1}
            groups.append(cur)
            continue
        if cur is not None:
            cur['body'].append(lines[i])
    banner = '// svm-actions cases.py output (local only): %s ExecuteAction' % prog
    label_at = {}
    for i, l in enumerate(lines):
        m = re.match(r'^\s*(\w+):\s*$', l)
        if m and not l.strip().startswith('case') and m.group(1) != 'default':
            label_at[m.group(1)] = i
    # the labels just before the common PlayAnim call (Channel, Sequence, rate ... held in variables)
    # that most cases jump to; following them adds nothing, so they are not expanded
    shared_tail = set()
    for i, l in enumerate(lines):
        if re.search(r'\(\(AActor \*\)this,Channel,', l):
            for j in range(max(0, i - 7), i):
                m = re.match(r'^\s*(\w+):\s*$', lines[j])
                if m:
                    shared_tail.add(m.group(1))

    def follow(label, depth=0, seen=None):
        """The code a goto lands on: from the label to the next break, return or goto (30 lines max)."""
        seen = seen if seen is not None else set()
        if label in seen or label not in label_at or label in shared_tail:
            return []
        seen.add(label)
        out = ['', '// ---- follows %s (line %d)' % (label, label_at[label] + 1)]
        nxt = []
        base = None
        for j in range(label_at[label] + 1, min(len(lines), label_at[label] + 61)):
            out.append(annotate(lines[j], names, bools, name_fields, phys))
            s = lines[j].strip()
            if not s or re.match(r'^\w+:$', s):
                continue
            ind = len(lines[j]) - len(lines[j].lstrip())
            if base is None:
                base = ind
            m = re.match(r'goto (\w+);', s)
            if m:
                nxt.append(m.group(1))
            if ind <= base and (s in ('break;', 'return;') or m or s.startswith('case ')):
                break
            if ind < base and s == '}':
                base = ind
        if depth < 2:
            for n in nxt:
                out += follow(n, depth + 1, seen)
        return out

    def ends(body):
        tail = [l.strip() for l in body if l.strip()]
        return bool(tail) and (tail[-1] in ('break;', 'return;') or tail[-1].startswith('goto '))

    for gi, g in enumerate(groups):
        k = gi
        while not ends(g['body'] if k == gi else groups[k]['body']) and k + 1 < len(groups):
            k += 1
            g.setdefault('falls', []).append(k)
    index = []
    for gi, g in enumerate(groups):
        for k in g.get('falls', []):
            g['body'] = g['body'] + ['', '// ---- falls through into case %s' % groups[k]['cases']] + groups[k]['body']
        label = '+'.join('%d %s' % (c, '/'.join(sa.get(c, ['?']))) if c != 'default' else 'default'
                         for c in g['cases'])
        first = g['cases'][0]
        fn = ('%03d.c' % first) if first != 'default' else 'default.c'
        body = [annotate(l, names, bools, name_fields, phys) for l in g['body']]
        gotos = sorted(set(re.findall(r'goto (\w+);', '\n'.join(g['body']))))
        extra = []
        seen = set()
        for t in gotos:
            extra += follow(t, 0, seen)
        with open(os.path.join(out_dir, fn), 'w', encoding='utf-8', newline='\n') as f:
            f.write(banner + ' case %s\n// %s\n' % (first, label))
            f.write('\n'.join(body + extra) + '\n')
        index.append({'file': fn, 'cases': g['cases'], 'names': [sa.get(c, ['?']) for c in g['cases']
                                                                if c != 'default'],
                      'line': g['line'], 'lines': len(g['body']), 'gotos': gotos})
    tail = [annotate(l, names, bools, name_fields, phys) for l in lines[end:]]
    head = [annotate(l, names, bools, name_fields, phys) for l in lines[:start + 1]]
    with open(os.path.join(out_dir, 'tails.c'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(banner + ' shared code after the switch\n')
        f.write('\n'.join(tail) + '\n')
    with open(os.path.join(out_dir, 'head.c'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(banner + ' prologue\n')
        f.write('\n'.join(head) + '\n')
    covered = sorted(c for g in groups for c in g['cases'] if c != 'default')
    with open(os.path.join(out_dir, 'index.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'source': os.path.relpath(src, REPO), 'groups': index, 'covered': covered}, f, indent=1)
    missing = [i for i in range(0, max(covered) + 1) if i not in covered]
    print('groups', len(groups), 'cases', len(covered), 'missing', missing)


if __name__ == '__main__':
    main()
