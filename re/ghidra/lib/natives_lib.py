"""Native function helpers shared by 12_natives.py (game) and 21_editor_natives.py (editor).

Two sources of native names:
* the reflection dump of the running game (the Bridge's natives_all.txt): native index, Package.Class.Function
  and the UFunction's Func address in the game;
* the static registration code in each binary, which fills GNatives[index] = &Class::execFunction.
  The game inlines it as 'mov dword ptr [GNatives + 4 * index], func'; the editor calls
  GRegisterNative(index, func). The index is the same in both builds, so it ties the two together.
"""

import collections
import os
import re
import struct

import svm_ghidra as svm
import upkg

LINE = re.compile(r'^\s*(\d+)\s+0x([0-9a-fA-F]+)\s+(\S+)\s+flags\s+0x([0-9a-fA-F]+)\s+func\s+0x([0-9a-fA-F]+)')
MAX_NATIVES = 4096


def natives_path(args):
    """The reflection dump: script argument, SVM_NATIVES_TXT, or 'natives' in local/re-inputs.json."""
    return svm.input_path('natives', 'SVM_NATIVES_TXT', args)


def load_natives(path, hierarchy):
    """List of natives from the reflection dump, each with its C++ class name."""
    natives = []
    with open(path, encoding='utf-8') as f:
        for n, line in enumerate(f, 1):
            if not line.strip():
                continue
            m = LINE.match(line)
            if not m:
                raise ValueError('%s:%d: unrecognised line %r' % (path, n, line.rstrip()))
            package, cls, name = m.group(3).split('.')
            full_class = package + '.' + cls
            natives.append({'index': int(m.group(1)), 'full': m.group(3), 'class': full_class, 'function': name,
                            'flags': int(m.group(4), 16), 'address': int(m.group(5), 16),
                            'cpp': upkg.cpp_class_name(hierarchy, full_class)})
    return natives


def verified_hierarchy(ctx):
    """Class hierarchy from System/_PC_/*.u after checking each package against the lock."""
    pc = os.path.join(svm.binlock.install_dir(), 'System', '_PC_')
    for e in ctx.lock['files']:
        if e['id'].startswith('pc:'):
            svm.binlock.verify_file(os.path.join(pc, e['path'].split('/')[-1]), e['id'], ctx.lock)
    return upkg.class_hierarchy(pc)


def game_registrations(image, base, code_ranges, known):
    """(GNatives address, {index: function}) from inlined 'mov dword ptr [GNatives + 4 * i], func' stores.

    known maps function address -> native index for natives we already know; the GNatives address
    is the one most of them agree on.
    """
    stores = []
    for lo, hi in code_ranges:
        for m in re.finditer(rb'\xC7\x05(....)(....)', image[lo - base:hi - base], re.S):
            dst, src = struct.unpack('<II', m.group(1) + m.group(2))
            stores.append((lo + m.start(), dst, src))
    votes = collections.Counter()
    for at, dst, src in stores:
        if src in known:
            votes[dst - 4 * known[src]] += 1
    if not votes:
        return None, {}, []
    gnatives, n = votes.most_common(1)[0]
    if n < len(known) // 2:
        return None, {}, []
    regs, conflicts = {}, []
    for at, dst, src in stores:
        if gnatives <= dst < gnatives + 4 * MAX_NATIVES and (dst - gnatives) % 4 == 0:
            idx = (dst - gnatives) // 4
            if idx in regs and regs[idx][0] != src:
                conflicts.append((idx, regs[idx][0], src))
            regs.setdefault(idx, (src, at))
    return gnatives, regs, conflicts


def editor_registrations(program, image, base, code_ranges):
    """(GRegisterNative address, {index: (function, call site)}) from 'GRegisterNative(index, func)' calls.

    The call is found by shape first (a call preceded by 'push imm' and 'mov dword ptr [esp+d], imm32'
    into code), then each call site is decoded from Ghidra's instructions.
    """
    in_code = lambda v: any(lo <= v < hi for lo, hi in code_ranges)  # noqa: E731
    stack_store = re.compile(rb'\xC7\x44\x24.(....)', re.S)
    votes = collections.Counter()
    for lo, hi in code_ranges:
        i = lo - base + 24
        end = hi - base - 5
        while True:
            i = image.find(b'\xE8', i, end)
            if i < 0:
                break
            window = image[i - 24:i]
            ms = [m for m in stack_store.finditer(window) if in_code(struct.unpack('<I', m.group(1))[0])]
            if ms and (b'\x68' in window or b'\x6A' in window):
                votes[base + i + 5 + struct.unpack_from('<i', image, i + 1)[0]] += 1
            i += 1
    if not votes:
        return None, {}, []
    grn, n = votes.most_common(1)[0]
    if n < 100:
        return None, {}, []
    listing = program.getListing()
    refs = program.getReferenceManager().getReferencesTo(svm_addr(program, grn))
    regs, undecoded = {}, []
    for ref in refs:
        if not ref.getReferenceType().isCall():
            continue
        call = listing.getInstructionAt(ref.getFromAddress())
        num = func = None
        ins = call.getPrevious() if call is not None else None
        for _ in range(6):
            if ins is None or (num is not None and func is not None):
                break
            mnem = ins.getMnemonicString()
            if mnem == 'PUSH' and num is None:
                s = ins.getScalar(0)
                if s is not None:
                    num = int(s.getUnsignedValue())
            elif mnem == 'MOV' and func is None and 'ESP' in str(ins.getDefaultOperandRepresentation(0)):
                s = ins.getScalar(1)
                if s is not None and in_code(int(s.getUnsignedValue())):
                    func = int(s.getUnsignedValue())
            ins = ins.getPrevious()
        if num is None or func is None or num >= MAX_NATIVES:
            undecoded.append(ref.getFromAddress().getOffset())
            continue
        regs.setdefault(num, (func, ref.getFromAddress().getOffset()))
    return grn, regs, undecoded


def svm_addr(program, value):
    return program.getAddressFactory().getDefaultAddressSpace().getAddress(value)


def name_group(ctx, address, group, counts, kept, plate_extra=None):
    """Create and name the function at address for a group of natives sharing it (lowest index first).

    Returns (function, how) like svm.ensure_function. Extra natives become labels, and a plate
    comment lists the whole group when the code is shared, plus any plate_extra lines.
    """
    program = ctx.program
    a = ctx.addr(address)
    f, how = svm.ensure_function(program, ctx.monitor, a)
    if f is None:
        return None, how
    group = sorted(group, key=lambda n: n['index'])
    primary = group[0]
    ns = svm.ensure_namespace(program, primary['cpp'])
    result = svm.set_function_name(f, 'exec' + primary['function'], ns)
    if result == 'set':
        counts['renamed'] = counts.get('renamed', 0) + 1
    elif result == 'kept-user':
        counts['keptUser'] = counts.get('keptUser', 0) + 1
        kept.append({'address': svm.hexaddr(address), 'name': f.getName(True),
                     'wanted': '%s::exec%s' % (primary['cpp'], primary['function'])})
    if f.getSignatureSource().toString() == 'DEFAULT':
        f.setCallingConvention('__thiscall')
    lines = []
    if len(group) > 1:
        counts['folded'] = counts.get('folded', 0) + 1
        for other in group[1:]:
            svm.ensure_label(program, a, 'exec' + other['function'], svm.ensure_namespace(program, other['cpp']))
            counts['labels'] = counts.get('labels', 0) + 1
        lines.append('Native thunk shared by %d UnrealScript natives (identical code folded):' % len(group))
        lines += ['%d %s' % (n['index'], n['full']) for n in group]
    else:
        lines.append('Native %d %s' % (primary['index'], primary['full']))
    if plate_extra:
        lines += plate_extra
    text = '\n'.join(lines)
    if program.getListing().getComment(svm.comment_type('plate'), a) != text:
        svm.set_comment(program, a, 'plate', text)
    return f, how
