# EDITOR: names the native exec thunks from their GRegisterNative(index, func) calls.
#
# Native indices are the same in the game and the editor, so the game's reflection dump (see
# 12_natives.py) names each registered editor thunk exactly. This runs after 20_editor_guards.py and
# takes precedence: a thunk's guard string is sometimes copied from a neighbouring native or belongs
# to an inlined callee, so when the guard name differs it is kept as a plate comment line.
# Registered natives the dump does not know (editor-only packages, bytecode handlers) keep their
# guard name if they have one. Writes local/re-out/editor.natives.json (index -> address and name)
# for 12_natives.py, and local/re-out/editor.natives-report.json.
# @category SVM
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', 'lib'))
import svm_ghidra as svm  # noqa: E402
import natives_lib  # noqa: E402

ctx = svm.setup(globals(), expect={'editor'})
if ctx is None:
    println('SVM editor natives: not the editor, skipped')
else:
    program = ctx.program
    fm = program.getFunctionManager()
    base, image, blocks = svm.loaded_image(program)
    code = [(lo, hi) for name, lo, hi, x in blocks if x]
    grn, regs, undecoded = natives_lib.editor_registrations(program, image, base, code)
    if grn is None:
        raise RuntimeError('GRegisterNative not found in the editor')
    f_grn, _ = svm.ensure_function(program, monitor, ctx.addr(grn))
    svm.set_function_name(f_grn, 'GRegisterNative', program.getGlobalNamespace())

    path = natives_lib.natives_path(list(getScriptArgs()))
    by_index = {}
    if path and os.path.isfile(path):
        by_index = {n['index']: n for n in natives_lib.load_natives(path, natives_lib.verified_hierarchy(ctx))}
    else:
        println('SVM editor natives: %s not found; only guard names are recorded' % path)

    pm = program.getUsrPropertyManager()
    guard_map = pm.getStringPropertyMap(svm.GUARD_NAMES_MAP)
    native_map = pm.getStringPropertyMap(svm.NATIVE_NAMES_MAP) or pm.createStringPropertyMap(svm.NATIVE_NAMES_MAP)
    groups = {}
    for idx, (func, site) in regs.items():
        groups.setdefault(func, []).append(idx)

    counts = {'registrations': len(regs), 'undecodedCalls': len(undecoded), 'addresses': len(groups),
              'namedByIndex': 0, 'guardOnly': 0, 'unnamed': 0, 'guardDiffers': 0, 'renamed': 0, 'keptUser': 0,
              'labels': 0, 'folded': 0, 'failed': 0}
    kept, differs, failures = [], [], []
    registry = {}
    for func in sorted(groups):
        idxs = sorted(groups[func])
        a = ctx.addr(func)
        f = fm.getFunctionAt(a)
        guard = guard_map.getString(a) if guard_map is not None else None
        group = [by_index[i] for i in idxs if i in by_index]
        if group:
            primary = min(group, key=lambda n: n['index'])
            wanted = '%s::exec%s' % (primary['cpp'], primary['function'])
            extra = []
            if guard and guard != wanted and guard not in ['%s::exec%s' % (n['cpp'], n['function']) for n in group]:
                extra.append('Guard string: %s' % guard)
                counts['guardDiffers'] += 1
                differs.append({'address': svm.hexaddr(func), 'index': primary['index'], 'name': wanted,
                                'guard': guard})
            f, how = natives_lib.name_group(ctx, func, group, counts, kept, extra)
            if f is None:
                counts['failed'] += 1
                failures.append({'address': svm.hexaddr(func), 'indices': idxs, 'problem': how})
                continue
            counts['namedByIndex'] += 1
            native_map.add(f.getEntryPoint(), wanted)
            name = wanted
        else:
            if f is None:
                f, how = svm.ensure_function(program, monitor, a)
            name = guard
            counts['guardOnly' if guard else 'unnamed'] += 1
        for i in idxs:
            registry[str(i)] = {'address': svm.hexaddr(func), 'name': name, 'guard': guard}

    with open(os.path.join(svm.OUT_DIR, 'editor.natives.json'), 'w', encoding='utf-8', newline='\n') as fh:
        json.dump({'gregisterNative': svm.hexaddr(grn), 'natives': dict(sorted(registry.items(), key=lambda kv: int(kv[0])))},
                  fh, indent=1)
        fh.write('\n')
    report = {'gregisterNative': svm.hexaddr(grn), 'counts': counts, 'guardDiffers': differs,
              'undecodedCalls': [svm.hexaddr(u) for u in undecoded], 'failures': failures, 'keptUserNames': kept}
    out = ctx.write_report('natives-report.json', report)
    println('SVM editor natives: %s -> %s' % (counts, out))
