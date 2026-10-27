"""Shared helpers for re/binaries.lock.json.

Every reverse-engineering script works on one exact build of the game. The lock file records the
SHA-256, size and PE header facts of each binary we study, and scripts call verify_file() or
verify_sha256() before touching anything so a different build is refused instead of silently
producing wrong names. Standard library only, so it also runs inside Ghidra's PyGhidra.
"""

import datetime
import hashlib
import json
import os
import struct

RE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO_DIR = os.path.dirname(RE_DIR)
LOCK_PATH = os.path.join(RE_DIR, 'binaries.lock.json')
LOCK_FORMAT = 'svm.binaries.lock/1'
DEFAULT_INSTALL = os.path.expanduser(r'~\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3')


class BuildMismatch(Exception):
    """A binary does not match the locked build."""


def install_dir(explicit=None):
    """The reference install: an explicit path, else SVM_GAME_DIR, else the default location."""
    return explicit or os.environ.get('SVM_GAME_DIR') or DEFAULT_INSTALL


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def pe_info(path):
    """Header facts of a PE32 image, or None if the file is not one."""
    with open(path, 'rb') as f:
        head = f.read(4096)
    if head[:2] != b'MZ' or len(head) < 0x40:
        return None
    e_lfanew = struct.unpack_from('<I', head, 0x3C)[0]
    if e_lfanew + 0x78 > len(head) or head[e_lfanew:e_lfanew + 4] != b'PE\0\0':
        return None
    machine, nsections, stamp = struct.unpack_from('<HHI', head, e_lfanew + 4)
    opt = e_lfanew + 24
    magic = struct.unpack_from('<H', head, opt)[0]
    if magic != 0x10B:
        raise BuildMismatch('%s: not a PE32 image (optional header magic %#x)' % (path, magic))
    entry = struct.unpack_from('<I', head, opt + 16)[0]
    image_base = struct.unpack_from('<I', head, opt + 28)[0]
    size_of_image = struct.unpack_from('<I', head, opt + 56)[0]
    checksum = struct.unpack_from('<I', head, opt + 64)[0]
    utc = datetime.datetime.fromtimestamp(stamp, tz=datetime.timezone.utc)
    return {
        'machine': '%#06x' % machine,
        'sections': nsections,
        'timeDateStamp': stamp,
        'timeDateStampUtc': utc.strftime('%Y-%m-%dT%H:%M:%SZ'),
        'imageBase': '%#010x' % image_base,
        'sizeOfImage': '%#x' % size_of_image,
        'entryPoint': '%#010x' % (image_base + entry),
        'checksum': '%#010x' % checksum,
    }


def load_lock(path=LOCK_PATH):
    with open(path, encoding='utf-8') as f:
        lock = json.load(f)
    if lock.get('format') != LOCK_FORMAT:
        raise BuildMismatch('%s: unknown lock format %r' % (path, lock.get('format')))
    return lock


def entry(lock, file_id):
    for e in lock['files']:
        if e['id'] == file_id:
            return e
    raise KeyError('no binary %r in the lock' % file_id)


def find_by_sha256(lock, sha256):
    sha256 = (sha256 or '').lower()
    for e in lock['files']:
        if e['sha256'] == sha256:
            return e
    return None


def verify_sha256(sha256, file_id, lock=None):
    """Raise BuildMismatch unless sha256 is the locked hash of file_id."""
    lock = lock or load_lock()
    e = entry(lock, file_id)
    if (sha256 or '').lower() != e['sha256']:
        raise BuildMismatch('%s: sha256 %s does not match the locked build %s (%s). Refusing to run on a '
                            'different build.' % (file_id, sha256, e['sha256'], lock.get('build')))
    return e


def verify_file(path, file_id, lock=None):
    """Hash path and check it against the lock entry for file_id."""
    lock = lock or load_lock()
    e = entry(lock, file_id)
    size = os.path.getsize(path)
    if size != e['size']:
        raise BuildMismatch('%s: size %d does not match the locked size %d (%s)' % (path, size, e['size'], file_id))
    return verify_sha256(sha256_file(path), file_id, lock)


def locked_path(file_id, install=None, lock=None):
    """Absolute path of a locked binary inside the install, verified against the lock."""
    lock = lock or load_lock()
    e = entry(lock, file_id)
    path = os.path.join(install_dir(install), *e['path'].split('/'))
    verify_file(path, file_id, lock)
    return path
