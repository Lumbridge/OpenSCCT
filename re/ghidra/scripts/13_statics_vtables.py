# GAME: labels the static UClass objects and the C++ vtables listed in the RE+ symbol table
# (scct_versus_symbols.json, a reflection dump of the running game).
#
# * statics: offset -> ['Class', 'Package.Name'] becomes <CppClass>::PrivateStaticClass.
# * vtables: offset -> the classes of live objects that used it. The table belongs to the nearest
#   native class of those objects (a script class has no C++ vtable of its own); it becomes
#   <CppClass>::vftable, and its slots are typed as pointers up to the next known vtable.
# Input path: the first script argument, SVM_SYMBOLS_JSON, or 'symbols' in local/re-inputs.json
# (a list there means the first file that exists).
# @category SVM
# @runtime PyGhidra

import json
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', 'lib'))
import svm_ghidra as svm  # noqa: E402
import upkg  # noqa: E402

MAX_SLOTS = 600


def vtable_bounds(base, image, blocks):
    """Probable vtable starts: constant stores 'mov dword ptr [reg], imm32' into read-only data whose
    first slot points at code (what a constructor does)."""
    text = [(lo, hi) for name, lo, hi, x in blocks if x]
    rdata = [(lo, hi) for name, lo, hi, x in blocks if name.startswith('.rdata')]

    def in_ranges(v, ranges):
        return any(lo <= v < hi for lo, hi in ranges)

    starts = set()
    for lo, hi in text:
        i = lo - base
        end = hi - base - 6
        while True:
            i = image.find(b'\xC7', i, end)
            if i < 0:
                break
            if image[i + 1] <= 0x07 and image[i + 1] not in (0x04, 0x05):
                t = struct.unpack_from('<I', image, i + 2)[0]
                if in_ranges(t, rdata) and 0 <= t - base < len(image) - 4:
                    first = struct.unpack_from('<I', image, t - base)[0]
                    if in_ranges(first, text):
                        starts.add(t)
            i += 1
    return starts


ctx = svm.setup(globals(), expect={'game'})
if ctx is None:
    println('SVM statics/vtables: not the game, skipped')
else:
    program = ctx.program
    path = svm.input_path('symbols', 'SVM_SYMBOLS_JSON', list(getScriptArgs()))
    if not path or not os.path.isfile(path):
        println('SVM statics/vtables: symbol table not found; skipped (names come from the annotations file)')
    else:
        with open(path, encoding='utf-8') as f:
            sym = json.load(f)
        base = program.getImageBase().getOffset()
        if int(sym['base'], 16) != base or sym.get('image', '').lower() != 'scct versus':
            raise svm.binlock.BuildMismatch('%s is for %s at %s, not this program' % (path, sym.get('image'), sym['base']))

        pc = os.path.join(svm.binlock.install_dir(), 'System', '_PC_')
        for e in ctx.lock['files']:
            if e['id'].startswith('pc:'):
                svm.binlock.verify_file(os.path.join(pc, e['path'].split('/')[-1]), e['id'], ctx.lock)
        hierarchy = upkg.class_hierarchy(pc)
        statics = {base + int(k, 16): v[1] for k, v in sym['statics'].items()}
        native = set(statics.values()) | {k for k, v in hierarchy.items() if v['native']}

        def nearest_native(full):
            seen = 0
            while full and seen < 64:
                if full in native or full not in hierarchy:
                    return full
                full = hierarchy[full]['super']
                seen += 1
            return None

        counts = {'statics': 0, 'staticsNew': 0, 'vtables': 0, 'vtablesNew': 0, 'vtableSlots': 0,
                  'slotPointersCreated': 0, 'sharedOwner': 0}
        problems = []
        mem = program.getMemory()
        for address in sorted(statics):
            full = statics[address]
            a = ctx.addr(address)
            if not mem.contains(a):
                problems.append({'address': svm.hexaddr(address), 'name': full, 'problem': 'outside image'})
                continue
            ns = svm.ensure_namespace(program, upkg.cpp_class_name(hierarchy, full))
            _, how = svm.ensure_label(program, a, 'PrivateStaticClass', ns, primary=True)
            counts['statics'] += 1
            counts['staticsNew'] += how == 'set'

        lo, image, blocks = svm.loaded_image(program)
        if lo != base:
            raise RuntimeError('first block does not start at the image base')
        tables = {base + int(k, 16): v for k, v in sym['vtables'].items()}
        bounds = vtable_bounds(base, image, blocks) | set(tables)
        code = [(blo, bhi) for name, blo, bhi, x in blocks if x]
        owners_seen = {}
        listing = program.getListing()
        from ghidra.program.model.data import PointerDataType
        ptr = PointerDataType()
        for address in sorted(tables):
            users = sorted(tables[address])
            owners = sorted({nearest_native(c) for c in users} - {None})
            if not owners:
                problems.append({'address': svm.hexaddr(address), 'users': users, 'problem': 'no native owner'})
                continue
            if len(owners) > 1:
                problems.append({'address': svm.hexaddr(address), 'users': users, 'owners': owners,
                                 'problem': 'several native owners; named after the first'})
            owner = owners[0]
            cpp = upkg.cpp_class_name(hierarchy, owner)
            if cpp in owners_seen:
                counts['sharedOwner'] += 1
                problems.append({'address': svm.hexaddr(address), 'owner': cpp, 'problem': 'owner already has a '
                                 'vtable at %s' % svm.hexaddr(owners_seen[cpp])})
            owners_seen.setdefault(cpp, address)
            a = ctx.addr(address)
            _, how = svm.ensure_label(program, a, 'vftable', svm.ensure_namespace(program, cpp), primary=True)
            counts['vtables'] += 1
            counts['vtablesNew'] += how == 'set'
            text = 'vtable of %s, used by: %s' % (cpp, ', '.join(users))
            if listing.getComment(svm.comment_type('plate'), a) != text:
                svm.set_comment(program, a, 'plate', text)

            for slot in range(MAX_SLOTS):
                sa = address + slot * 4
                if slot and sa in bounds:
                    break
                target = struct.unpack_from('<I', image, sa - base)[0]
                if not any(clo <= target < chi for clo, chi in code):
                    break
                counts['vtableSlots'] += 1
                slot_addr = ctx.addr(sa)
                if listing.getDefinedDataAt(slot_addr) is None and listing.getInstructionContaining(slot_addr) is None:
                    try:
                        listing.createData(slot_addr, ptr)
                        counts['slotPointersCreated'] += 1
                    except Exception as exc:  # noqa: BLE001
                        problems.append({'address': svm.hexaddr(sa), 'problem': 'pointer not created: %s' % exc})
                        break

        report = {'source': path, 'counts': counts, 'problems': problems}
        out = ctx.write_report('statics-vtables-report.json', report)
        println('SVM statics/vtables: %s, %d problems -> %s' % (counts, len(problems), out))
