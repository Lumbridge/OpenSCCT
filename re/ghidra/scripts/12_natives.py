# GAME: creates and names the native exec thunks of every UnrealScript native function.
#
# Sources:
# * the reflection dump of the running game (the Bridge's natives_all.txt; path from the first
#   script argument, SVM_NATIVES_TXT or 'natives' in local/re-inputs.json): native index, class,
#   function and the UFunction's Func address;
# * the game's own registration stores 'mov [GNatives + 4 * index], func', which also give the
#   original address of natives that Reloaded.Core.dll replaces at runtime (their dump address lies
#   in the DLL), and of registered natives the dump has no name for;
# * local/re-out/editor.natives.json from 21_editor_natives.py, which names some of those by index.
# Each native becomes <CppClass>::exec<Function> in a class namespace (A prefix for actors, U for
# the rest, from System/_PC_/*.u). Folded thunks get the lowest index as the name and a label per
# other native. Report: local/re-out/game.natives-report.json; editor/game address pairs per
# native index (anchors for name transfer) go to local/re-out/native-anchors.json.
# @category SVM
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', 'lib'))
import svm_ghidra as svm  # noqa: E402
import natives_lib  # noqa: E402

CREATED_MAP = 'SVM.createdNativeFunctions'

ctx = svm.setup(globals(), expect={'game'})
if ctx is None:
    println('SVM natives: not the game, skipped')
else:
    program = ctx.program
    path = natives_lib.natives_path(list(getScriptArgs()))
    if not path or not os.path.isfile(path):
        println('SVM natives: reflection dump %s not found; skipped (names come from the annotations file)' % path)
    else:
        hierarchy = natives_lib.verified_hierarchy(ctx)
        natives = natives_lib.load_natives(path, hierarchy)
        by_index = {n['index']: n for n in natives}
        mem = program.getMemory()
        base, image, blocks = svm.loaded_image(program)
        code = [(lo, hi) for name, lo, hi, x in blocks if x]

        in_image = [n for n in natives if mem.contains(ctx.addr(n['address']))]
        known = {n['address']: n['index'] for n in in_image}
        gnatives, regs, conflicts = natives_lib.game_registrations(image, base, code, known)

        # Final address per native index: the dump when it points into the image, else the registration.
        placed = {}
        replaced, unknown_regs, missing = [], [], []
        for n in natives:
            if mem.contains(ctx.addr(n['address'])):
                placed.setdefault(n['address'], []).append(dict(n, source='reflection'))
                if n['index'] in regs and regs[n['index']][0] != n['address']:
                    conflicts.append((n['index'], n['address'], regs[n['index']][0]))
            elif n['index'] in regs:
                orig = regs[n['index']][0]
                placed.setdefault(orig, []).append(dict(n, source='registration'))
                replaced.append({'index': n['index'], 'native': n['full'], 'runtime': svm.hexaddr(n['address']),
                                 'original': svm.hexaddr(orig)})
            else:
                missing.append({'index': n['index'], 'native': n['full'], 'runtime': svm.hexaddr(n['address'])})

        editor_reg = {}
        editor_path = os.path.join(svm.OUT_DIR, 'editor.natives.json')
        if os.path.isfile(editor_path):
            with open(editor_path, encoding='utf-8') as f:
                editor_reg = {int(k): v for k, v in json.load(f)['natives'].items()}
        for idx in sorted(set(regs) - set(by_index)):
            func = regs[idx][0]
            e = editor_reg.get(idx)
            if e and e.get('name') and '::' in e['name']:
                cls, fn = e['name'].split('::')
                if fn.startswith('exec'):
                    placed.setdefault(func, []).append({'index': idx, 'full': '(editor) ' + e['name'], 'cpp': cls,
                                                        'function': fn[4:], 'source': 'editor'})
                    continue
            unknown_regs.append({'index': idx, 'address': svm.hexaddr(func)})

        counts = {'natives': len(natives), 'reflectionInImage': len(in_image), 'replacedByReloaded': len(replaced),
                  'addedByReloaded': len(missing), 'registrations': len(regs), 'namedFromEditor': 0,
                  'registeredUnnamed': len(unknown_regs), 'addresses': len(placed), 'functionsExisting': 0,
                  'functionsCreated': 0, 'functionsSplit': 0, 'failed': 0, 'renamed': 0, 'keptUser': 0,
                  'labels': 0, 'folded': 0, 'conflicts': len(conflicts)}
        pm = program.getUsrPropertyManager()
        created = pm.getStringPropertyMap(CREATED_MAP) or pm.createStringPropertyMap(CREATED_MAP)
        failures, kept = [], []
        for address in sorted(placed):
            group = placed[address]
            extra = []
            if any(n['source'] == 'registration' for n in group):
                extra.append('Reloaded.Core.dll replaces this native at runtime; this is the original code.')
            if any(n['source'] == 'editor' for n in group):
                extra.append('Named from the editor build by native index (no reflection entry in the game).')
                counts['namedFromEditor'] += 1
            container = program.getFunctionManager().getFunctionContaining(ctx.addr(address))
            f, how = natives_lib.name_group(ctx, address, group, counts, kept, extra)
            if f is None:
                counts['failed'] += 1
                failures.append({'address': svm.hexaddr(address), 'natives': [n['full'] for n in group], 'problem': how})
                continue
            if how == 'existing':
                counts['functionsExisting'] += 1
            else:
                counts['functionsCreated' if how == 'created' else 'functionsSplit'] += 1
                note = how if container is None else '%s (inside %s)' % (how, container.getEntryPoint())
                created.add(f.getEntryPoint(), note)

        for u in unknown_regs:
            a = ctx.addr(int(u['address'], 16))
            f, how = svm.ensure_function(program, monitor, a)
            if f is not None:
                text = 'Registered as native %d; no reflection or editor name.' % u['index']
                if program.getListing().getComment(svm.comment_type('plate'), a) != text:
                    svm.set_comment(program, a, 'plate', text)
        if gnatives is not None:
            svm.ensure_label(program, ctx.addr(gnatives), 'GNatives', program.getGlobalNamespace(), primary=True)

        not_starts = []
        it = created.getPropertyIterator()
        while it.hasNext():
            a = it.next()
            not_starts.append({'address': svm.hexaddr(a.getOffset()), 'how': created.getString(a),
                               'name': program.getFunctionManager().getFunctionAt(a).getName(True)
                               if program.getFunctionManager().getFunctionAt(a) else None})

        anchors = []
        for idx, (func, _) in sorted(regs.items()):
            e = editor_reg.get(idx)
            if e:
                n = by_index.get(idx)
                anchors.append({'index': idx, 'name': ('%s::exec%s' % (n['cpp'], n['function'])) if n else e.get('name'),
                                'game': svm.hexaddr(func), 'editor': e['address']})
        if anchors:
            with open(os.path.join(svm.OUT_DIR, 'native-anchors.json'), 'w', encoding='utf-8', newline='\n') as f:
                json.dump({'pairs': anchors}, f, indent=1)
                f.write('\n')

        report = {
            'source': os.path.basename(path),
            'gnatives': svm.hexaddr(gnatives) if gnatives is not None else None,
            'counts': counts,
            'replacedByReloaded': replaced,
            'addedByReloaded': missing,
            'registeredUnnamed': unknown_regs,
            'conflicts': [{'index': i, 'a': svm.hexaddr(x), 'b': svm.hexaddr(y)} for i, x, y in conflicts],
            'notFunctionStartsAfterAnalysis': not_starts,
            'failures': failures,
            'keptUserNames': kept,
            'anchors': len(anchors),
        }
        out = ctx.write_report('natives-report.json', report)
        println('SVM natives: %s -> %s' % (counts, out))
