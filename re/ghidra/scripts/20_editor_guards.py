# EDITOR: names functions from their 'Class::Function' guard strings.
#
# The editor build keeps the engine's guard()/unguard macros: each guarded function has a C++ try
# block whose catch(...) handler pushes the function's name string before rethrowing. So a guard
# string is referenced from a catch handler, not from the function body. We read the MSVC EH tables
# (the __ehhandler stub 'mov eax, FuncInfo; jmp __CxxFrameHandler', FuncInfo -> try blocks ->
# handlers) to map each handler back to the function whose prologue installs that stub, and name
# that function. When one function has several guard names (an inlined guarded callee), the
# outermost try block wins, which is the guard at function entry. References to the same strings
# from function bodies (allocation tags such as 'FArray::Add') are counted but never used for names.
# Report: local/re-out/editor.guards-report.json.
# @category SVM
# @runtime PyGhidra

import bisect
import os
import re
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

GUARD = re.compile(rb'([A-Za-z_][A-Za-z0-9_]*::~?[A-Za-z_][A-Za-z0-9_]*)\x00')
EH_MAGIC = (0x19930520, 0x19930521, 0x19930522)
HANDLER_WINDOW = 64


def scan_push_imm(image, base, ranges, wanted):
    """{value: [address of 'push imm32']} for imm32 values accepted by wanted, inside the code ranges."""
    hits = {}
    test = wanted.__contains__ if isinstance(wanted, (set, frozenset, dict)) else wanted
    for lo, hi in ranges:
        i = lo - base
        end = hi - base - 5
        while True:
            i = image.find(b'\x68', i, end)
            if i < 0:
                break
            v = struct.unpack_from('<I', image, i + 1)[0]
            if test(v):
                hits.setdefault(v, []).append(base + i)
            i += 1
    return hits


def parse_eh(image, base, code_ranges):
    """(stubs {stub: funcinfo}, handlers {handler: (stub, try index, tryLow, tryHigh, catch index)})."""
    size = len(image)

    def u32(off):
        return struct.unpack_from('<I', image, off)[0]

    stubs = {}
    for lo, hi in code_ranges:
        for m in re.finditer(rb'\xB8(....)\xE9', image[lo - base:hi - base], re.S):
            fi = struct.unpack('<I', m.group(1))[0] - base
            if 0 <= fi < size - 20 and u32(fi) in EH_MAGIC:
                stubs[lo + m.start()] = fi + base
    handlers = {}
    for stub, fi in stubs.items():
        _, _, _, ntry, ptry = struct.unpack_from('<IiIII', image, fi - base)
        if ntry > 256 or not 0 <= ptry - base < size:
            continue
        for t in range(ntry):
            low, high, _, ncatch, phandlers = struct.unpack_from('<iiiII', image, ptry - base + t * 20)
            if ncatch > 64 or not 0 <= phandlers - base < size:
                continue
            for c in range(ncatch):
                _, ptype, _, handler = struct.unpack_from('<IIiI', image, phandlers - base + c * 16)
                handlers[handler] = (stub, t, low, high, c, ptype)
    return stubs, handlers


def expected_entry(image, base, push):
    """Function entry implied by the EH prologue around 'push stub' at push, or None."""
    p = push - base
    if image[p - 5:p] == b'\x55\x8B\xEC\x6A\xFF':
        return push - 5
    if image[p - 8:p] == b'\x55\x8B\xEC\x83\xE4\xF8\x6A\xFF':
        return push - 8
    if image[p - 2:p] == b'\x6A\xFF':
        return push - 2
    return None


ctx = svm.setup(globals(), expect={'editor'})
if ctx is None:
    println('SVM guards: not the editor, skipped')
else:
    program = ctx.program
    fm = program.getFunctionManager()
    base, image, blocks = svm.loaded_image(program)
    code = [(lo, hi) for name, lo, hi, x in blocks if x]
    data = [(lo, hi) for name, lo, hi, x in blocks if not x]

    # A guard string is found through its reference, so its start is exact even when the byte
    # before it happens to be a letter.
    strings = {}

    def is_guard_string(v):
        if v in strings:
            return True
        if not any(lo <= v < hi for lo, hi in data):
            return False
        m = GUARD.match(image, v - base, min(v - base + 256, len(image)))
        if m is None:
            return False
        strings[v] = m.group(1).decode('ascii')
        return True

    stubs, handlers = parse_eh(image, base, code)
    handler_starts = sorted(handlers)
    stub_pushes = scan_push_imm(image, base, code, set(stubs))
    refs = scan_push_imm(image, base, code, is_guard_string)

    # Guard name candidates per parent EH stub.
    per_stub = {}
    body_refs = {}
    for straddr, pushes in refs.items():
        for r in pushes:
            k = bisect.bisect_right(handler_starts, r) - 1
            if k >= 0 and r - handler_starts[k] < HANDLER_WINDOW:
                stub, t, low, high, c, ptype = handlers[handler_starts[k]]
                per_stub.setdefault(stub, []).append((low, -high, t, strings[straddr], r))
            else:
                body_refs.setdefault(strings[straddr], []).append(r)

    counts = {'guardStrings': len(strings), 'ehStubs': len(stubs),
              'ehHandlers': len(handlers), 'guardedFunctions': 0, 'named': 0, 'alreadyNamed': 0,
              'createdFunctions': 0, 'splitFunctions': 0, 'keptUser': 0, 'ambiguous': 0,
              'overloadNames': 0, 'unresolved': 0, 'bodyOnlyStrings': 0, 'distinctNames': 0,
              'nativeByIndex': 0}
    ambiguous, unresolved, kept, created = [], [], [], []
    assigned = {}
    pm = program.getUsrPropertyManager()
    guard_map = pm.getStringPropertyMap(svm.GUARD_NAMES_MAP) or pm.createStringPropertyMap(svm.GUARD_NAMES_MAP)
    native_map = pm.getStringPropertyMap(svm.NATIVE_NAMES_MAP)
    for stub in sorted(per_stub):
        cands = sorted(per_stub[stub])
        names = sorted({c[3] for c in cands})
        pushes = stub_pushes.get(stub, [])
        if len(pushes) != 1:
            unresolved.append({'stub': svm.hexaddr(stub), 'names': names, 'problem': '%d prologue pushes' % len(pushes)})
            continue
        push = pushes[0]
        entry = expected_entry(image, base, push)
        f = fm.getFunctionContaining(ctx.addr(push))
        if entry is not None and (f is None or f.getEntryPoint().getOffset() != entry):
            f, how = svm.ensure_function(program, monitor, ctx.addr(entry))
            if f is not None and how != 'existing':
                counts['createdFunctions' if how == 'created' else 'splitFunctions'] += 1
                created.append({'entry': svm.hexaddr(entry), 'how': how})
        if f is None:
            unresolved.append({'stub': svm.hexaddr(stub), 'names': names, 'problem': 'no function at the prologue'})
            continue
        chosen = cands[0][3]
        if len(names) > 1:
            counts['ambiguous'] += 1
            ambiguous.append({'entry': f.getEntryPoint().toString(), 'chosen': chosen, 'names': names})
        assigned.setdefault(chosen, []).append(f)
        guard_map.add(f.getEntryPoint(), chosen)
        counts['guardedFunctions'] += 1
        if native_map is not None and native_map.getString(f.getEntryPoint()) is not None:
            # Named exactly by native index (21_editor_natives.py); the guard is kept as evidence there.
            counts['nativeByIndex'] += 1
            continue
        cls, func = chosen.split('::')
        result = svm.set_function_name(f, func, svm.ensure_namespace(program, cls))
        if result == 'set':
            counts['named'] += 1
        elif result == 'same':
            counts['alreadyNamed'] += 1
        else:
            counts['keptUser'] += 1
            kept.append({'entry': f.getEntryPoint().toString(), 'name': f.getName(True), 'guard': chosen})

    counts['distinctNames'] = len(assigned)
    overloads = {n: sorted(f.getEntryPoint().toString() for f in fs) for n, fs in assigned.items() if len(fs) > 1}
    counts['overloadNames'] = len(overloads)
    # UnrealScript natives cannot be overloaded, so a shared exec guard is a copied guard() line.
    copied = sorted(n for n in overloads if n.split('::')[1].startswith('exec'))
    counts['copiedExecGuards'] = len(copied)
    counts['unresolved'] = len(unresolved)
    body_only = sorted(n for n in body_refs if n not in assigned)
    counts['bodyOnlyStrings'] = len(body_only)

    report = {
        'counts': counts,
        'ambiguous': ambiguous,
        'overloads': overloads,
        'copiedExecGuards': copied,
        'unresolved': unresolved,
        'keptUserNames': kept,
        'createdFunctions': created,
        'bodyReferences': {n: len(v) for n, v in sorted(body_refs.items())},
        'bodyOnly': body_only,
    }
    out = ctx.write_report('guards-report.json', report)
    println('SVM guards: %s -> %s' % (counts, out))
