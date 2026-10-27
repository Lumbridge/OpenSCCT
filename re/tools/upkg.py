"""Minimal reader for SCCT Versus package headers and tables (standard library only).

Covers just what the reverse-engineering scripts need: the package summary (version, licensee,
GUID), the name table, and the import and export tables. Format notes:

* Raw packages start with the tag 0x9E2A83C1. Cooked maps and textures wrap the package in a zlib
  container (uint32 uncompressed size, uint32 compressed size, zlib stream).
* Name entries are a compact length plus bytes, usually XOR-ed with (file offset & 0xFF), then a
  uint32 of flags.
* Every name reference in the tables is two compact indices: the package-local name index and a
  cooked global index that we skip.
* Import: class package name, class name, outer int32, object name.
* Export: class compact, super compact, outer int32, name, flags uint32, size compact, offset
  compact (only when size > 0).
"""

import os
import struct
import zlib

PACKAGE_TAG = 0x9E2A83C1
RF_NATIVE = 0x04000000


class PackageError(Exception):
    pass


def _compact(d, p):
    b = d[p]
    p += 1
    value = b & 0x3F
    negative = b & 0x80
    more = b & 0x40
    shift = 6
    while more:
        b = d[p]
        p += 1
        value |= (b & 0x7F) << shift
        shift += 7
        more = b & 0x80
    return (-value if negative else value), p


class Package:
    def __init__(self, path):
        self.path = path
        self.stem = os.path.splitext(os.path.basename(path))[0]
        with open(path, 'rb') as f:
            raw = f.read()
        self.compressed = False
        if len(raw) < 16:
            raise PackageError('%s: too small' % path)
        if struct.unpack_from('<I', raw, 0)[0] != PACKAGE_TAG:
            unc, comp = struct.unpack_from('<2I', raw, 0)
            data = zlib.decompress(raw[8:8 + comp])
            if len(data) != unc or struct.unpack_from('<I', data, 0)[0] != PACKAGE_TAG:
                raise PackageError('%s: not an Unreal package' % path)
            self.compressed = True
        else:
            data = raw
        self.data = data
        _, self.version, self.licensee, self.flags = struct.unpack_from('<IHHI', data, 0)
        (self.name_count, self.name_offset, self.export_count, self.export_offset,
         self.import_count, self.import_offset) = struct.unpack_from('<6i', data, 12)
        self.guid = data[36:52]
        self._read_names()
        self._read_imports()
        self._read_exports()

    def guid_text(self):
        a, b, c, d = struct.unpack('<4I', self.guid)
        return '%08X%08X%08X%08X' % (a, b, c, d)

    def _read_names(self):
        d = self.data
        n0, p0 = _compact(d, self.name_offset)
        first = d[p0:p0 + n0] if 0 < n0 <= 1024 else b''
        plain = len(first) > 1 and first[-1] == 0 and all(32 <= c < 127 for c in first[:-1])
        p = self.name_offset
        names = []
        for _ in range(self.name_count):
            n, p = _compact(d, p)
            if n <= 0 or n > 1024:
                raise PackageError('%s: bad name length %d at %d' % (self.path, n, p))
            chunk = d[p:p + n]
            if not plain:
                chunk = bytes(v ^ ((p + j) & 0xFF) for j, v in enumerate(chunk))
            names.append(chunk.rstrip(b'\0').decode('latin1'))
            p += n + 4
        self.names = names

    def _name(self, p):
        idx, p = _compact(self.data, p)
        _, p = _compact(self.data, p)
        if not 0 <= idx < len(self.names):
            raise PackageError('%s: bad name index %d' % (self.path, idx))
        return self.names[idx], p

    def _read_imports(self):
        d, p = self.data, self.import_offset
        self.imports = []
        for _ in range(self.import_count):
            class_package, p = self._name(p)
            class_name, p = self._name(p)
            outer = struct.unpack_from('<i', d, p)[0]
            p += 4
            name, p = self._name(p)
            self.imports.append({'class_package': class_package, 'class_name': class_name,
                                 'outer': outer, 'name': name})

    def _read_exports(self):
        d, p = self.data, self.export_offset
        self.exports = []
        for i in range(self.export_count):
            class_ref, p = _compact(d, p)
            super_ref, p = _compact(d, p)
            outer = struct.unpack_from('<i', d, p)[0]
            p += 4
            name, p = self._name(p)
            flags = struct.unpack_from('<I', d, p)[0]
            p += 4
            size, p = _compact(d, p)
            offset = 0
            if size > 0:
                offset, p = _compact(d, p)
            self.exports.append({'index': i, 'class_ref': class_ref, 'super_ref': super_ref, 'outer': outer,
                                 'name': name, 'flags': flags, 'size': size, 'offset': offset})

    def ref_name(self, ref):
        """(package, name) of an object reference, for top-level classes."""
        if ref == 0:
            return None
        if ref > 0:
            return self.stem, self.exports[ref - 1]['name']
        imp = self.imports[-ref - 1]
        outer = imp['outer']
        package = self.imports[-outer - 1]['name'] if outer < 0 else ''
        return package, imp['name']

    def classes(self):
        """Yield (class name, super (package, name) or None, export flags) for each class export."""
        for e in self.exports:
            if e['class_ref'] == 0:
                yield e['name'], self.ref_name(e['super_ref']), e['flags']


def class_hierarchy(pc_dir):
    """Map 'Package.Class' -> {'super': 'Package.Class' or None, 'native': bool} for every *.u in pc_dir."""
    result = {}
    for fn in sorted(os.listdir(pc_dir)):
        if not fn.lower().endswith('.u'):
            continue
        pkg = Package(os.path.join(pc_dir, fn))
        for name, sup, flags in pkg.classes():
            result['%s.%s' % (pkg.stem, name)] = {
                'super': '%s.%s' % sup if sup else None,
                'native': bool(flags & RF_NATIVE),
            }
    return result


def is_actor_class(hierarchy, full_name):
    """True when full_name is Engine.Actor or derives from it."""
    seen = 0
    while full_name and seen < 64:
        if full_name == 'Engine.Actor':
            return True
        node = hierarchy.get(full_name)
        if node is None:
            return False
        full_name = node['super']
        seen += 1
    return False


def cpp_class_name(hierarchy, full_name):
    """C++ name of a class: A prefix for actors, U for every other UObject class."""
    short = full_name.split('.')[-1]
    return ('A' if is_actor_class(hierarchy, full_name) else 'U') + short
