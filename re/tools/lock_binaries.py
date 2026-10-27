"""Write or check re/binaries.lock.json.

    python -I re/tools/lock_binaries.py [--install DIR]          write the lock from the install
    python -I re/tools/lock_binaries.py --check [--install DIR]  verify the install against the lock

The lock pins the exact build every reverse-engineering script works on: the game exe, the editor,
Reloaded.Core.dll and every System/_PC_/*.u package. Only hashes and header facts are written,
never file content.
"""

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import binlock  # noqa: E402
import upkg  # noqa: E402

BUILD_NAME = 'Enhanced SCCT Versus v4.0 Public Beta 3'
PE_FILES = [
    ('game', 'System/SCCT Versus'),
    ('editor', 'System/ChaosTheory_Editor.exe'),
    ('reloaded_core', 'System/Reloaded.Core.dll'),
]


def collect(install):
    files = []
    for file_id, rel in PE_FILES:
        files.append((file_id, rel))
    pc = os.path.join(install, 'System', '_PC_')
    for fn in sorted(os.listdir(pc), key=str.lower):
        if fn.lower().endswith('.u'):
            files.append(('pc:' + fn, 'System/_PC_/' + fn))
    entries = []
    for file_id, rel in files:
        path = os.path.join(install, *rel.split('/'))
        e = {'id': file_id, 'path': rel, 'size': os.path.getsize(path), 'sha256': binlock.sha256_file(path)}
        pe = binlock.pe_info(path)
        if pe:
            e['pe'] = pe
        elif rel.lower().endswith('.u'):
            pkg = upkg.Package(path)
            e['package'] = {
                'version': pkg.version,
                'licensee': pkg.licensee,
                'flags': '%#x' % pkg.flags,
                'guid': pkg.guid_text(),
                'names': pkg.name_count,
                'imports': pkg.import_count,
                'exports': pkg.export_count,
            }
        entries.append(e)
    return {'format': binlock.LOCK_FORMAT, 'build': BUILD_NAME, 'files': entries}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--install', help='game install root (default: SVM_GAME_DIR or the reference install)')
    ap.add_argument('--check', action='store_true', help='verify the install against the existing lock')
    ap.add_argument('--lock', default=binlock.LOCK_PATH, help='lock file path')
    args = ap.parse_args()
    install = binlock.install_dir(args.install)

    if args.check:
        lock = binlock.load_lock(args.lock)
        bad = 0
        for e in lock['files']:
            path = os.path.join(install, *e['path'].split('/'))
            try:
                binlock.verify_file(path, e['id'], lock)
                print('ok        %s' % e['path'])
            except (OSError, binlock.BuildMismatch) as exc:
                bad += 1
                print('MISMATCH  %s: %s' % (e['path'], exc))
        print('%d of %d files match %s' % (len(lock['files']) - bad, len(lock['files']), lock['build']))
        return 1 if bad else 0

    lock = collect(install)
    text = json.dumps(lock, indent=2, sort_keys=False) + '\n'
    with open(args.lock, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print('wrote %s (%d files)' % (args.lock, len(lock['files'])))
    return 0


if __name__ == '__main__':
    sys.exit(main())
