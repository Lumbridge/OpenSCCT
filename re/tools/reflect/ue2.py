"""The reflection data of a running SCCT Versus engine (game or editor), read from memory.

Everything here only reads. `Engine(memory, profile)` loads the object table and the names,
then `reflect()` walks every class, struct, state, function, property, enum and constant, and
`class_defaults()` decodes the default property block a class keeps (UClass::Defaults).

Layout (32-bit, both builds; checked on the running editor and game, see docs/spikes/R0-reflection.md):

    UObject   +00 vtable  +04 Index  +0C StateFrame  +18 Outer  +1C ObjectFlags  +20 Name  +24 Class
    UField    +28 SuperField  +2C Next
    UStruct   +30 Children  +34 PropertiesSize  +38 FriendlyName  +3C Script data  +40 Script size
              +44 ScriptText  +48 CppText  +4C TextPos  +50 Line  +54 StructFlags
              +58 RefLink  +5C PropertyLink  +60 ConfigLink  +64 ConstructorLink
    UFunction +68 FunctionFlags  +6C Func  +70 iNative (u16)  +72 RepOffset (u16)
              +74 ParmsSize (u16)  +76 ReturnValueOffset (u16)  +78 OperPrecedence (u8)  +79 NumParms (u8)
    UState    +68 ProbeMask (u64)  +70 IgnoreMask (u64)  +78 StateFlags  +7C LabelTableOffset (u16)
    UClass    +8C ClassFlags  +90 ClassWithin  +94 ClassConfigName  +B0 Defaults {data, num, max}
              +D8 Dependencies {data, num, max} (12-byte entries)  +E4 PackageImports  +108 ClassConstructor
    UProperty +30 ArrayDim (u16)  +32 ElementSize (u16)  +34 PropertyFlags  +38 Category  +3C Offset
              +40 PropertyLinkNext  +44 ConfigLinkNext  +48 ConstructorLinkNext  +4C NextRef
              +60 RepOffset (u16)  +64 Enum / BitMask / Struct / Inner / PropertyClass / Function
              +68 MetaClass (ClassProperty)
    UEnum     +30 Names {data, num, max} of FName
    UConst    +30 Value (FString)
    FString   {data, num (with the terminator), max}: 8-bit characters in the editor, UTF-16 in the game

Names: the editor keeps a flat GNames array of FNameEntry* (entry +0 Index, +0C ANSI text). The
game has no flat array that was found; its FNameEntry blocks (+0 hash next, +4 hash previous,
+8 std::wstring with a 16-byte buffer or a pointer, +18 size, +1C capacity, +20 Index) are found
by scanning its heap (`scan_wide_names`).
"""
from __future__ import annotations

import math
import struct
from collections import Counter, defaultdict
from typing import Dict, Iterable, List, Optional, Tuple

# Where the two builds differ. The editor's UProperty carries 12 more bytes before RepOffset (its
# subclass fields start at +0x64, the game's at +0x58), and its UClass one more dword before
# ClassFlags; ClassReps and NetFields are only filled in the game.
PROFILES = {
    'editor': {'exe': 'chaostheory_editor', 'imageBase': 0x10E00000, 'gobjects': 0x11697B70,
               'names': ('array', 0x1169CFBC), 'char': 1,
               'class': {'flags': 0x8C, 'within': 0x90, 'config': 0x94, 'classReps': 0x98, 'netFields': 0xA4,
                         'defaults': 0xB0, 'dependencies': 0xD8, 'imports': 0xE4, 'constructor': 0x108},
               'property': {'repOwner': 0x50, 'repOffset': 0x60, 'repIndex': 0x62, 'extra': 0x64, 'extra2': 0x68}},
    'game': {'exe': 'scct versus', 'imageBase': 0x10900000, 'gobjects': 0x10CC9E80,
             'names': ('heap-scan', None), 'char': 2,
             'class': {'flags': 0x88, 'within': 0x8C, 'config': 0x90, 'classReps': 0x94, 'netFields': 0xA0,
                       'defaults': 0xAC, 'dependencies': 0xD4, 'imports': 0xE0, 'constructor': 0x104},
             'property': {'repOwner': 0x50, 'repOffset': 0x54, 'repIndex': 0x56, 'extra': 0x58, 'extra2': 0x5C}},
}

O_VTABLE, O_INDEX, O_STATEFRAME, O_OUTER, O_FLAGS, O_NAME, O_CLASS = 0x00, 0x04, 0x0C, 0x18, 0x1C, 0x20, 0x24
F_SUPER, F_NEXT = 0x28, 0x2C
S_CHILDREN, S_SIZE, S_FRIENDLY, S_SCRIPT, S_SCRIPTSIZE = 0x30, 0x34, 0x38, 0x3C, 0x40
S_SCRIPTTEXT, S_CPPTEXT, S_TEXTPOS, S_LINE, S_STRUCTFLAGS = 0x44, 0x48, 0x4C, 0x50, 0x54
S_REFLINK, S_PROPLINK, S_CONFIGLINK, S_CTORLINK = 0x58, 0x5C, 0x60, 0x64
FN_FLAGS, FN_FUNC, FN_INATIVE, FN_REPOFFSET, FN_PARMSSIZE, FN_RETOFFSET, FN_PRECEDENCE, FN_NUMPARMS = \
    0x68, 0x6C, 0x70, 0x72, 0x74, 0x76, 0x78, 0x79
ST_PROBE, ST_IGNORE, ST_FLAGS, ST_LABELS = 0x68, 0x70, 0x78, 0x7C
P_DIM, P_SIZE, P_FLAGS, P_CATEGORY, P_OFFSET = 0x30, 0x32, 0x34, 0x38, 0x3C
P_LINKNEXT, P_CONFIGNEXT, P_CTORNEXT, P_NEXTREF = 0x40, 0x44, 0x48, 0x4C
E_NAMES = 0x30
K_VALUE = 0x30
OBJECT_HEADER = 0x28

# Flag names as in Unreal Engine 2 (UnObjBas.h / UnStack.h). The values are the engine's; the
# names are the usual UE2 ones, assumed to carry over to this build.
CPF = [(0x1, 'Edit'), (0x2, 'Const'), (0x4, 'Input'), (0x8, 'ExportObject'), (0x10, 'OptionalParm'),
       (0x20, 'Net'), (0x40, 'EditConstArray'), (0x80, 'Parm'), (0x100, 'OutParm'), (0x200, 'SkipParm'),
       (0x400, 'ReturnParm'), (0x800, 'CoerceParm'), (0x1000, 'Native'), (0x2000, 'Transient'),
       (0x4000, 'Config'), (0x8000, 'Localized'), (0x10000, 'Travel'), (0x20000, 'EditConst'),
       (0x40000, 'GlobalConfig'), (0x100000, 'OnDemand'), (0x200000, 'New'), (0x400000, 'NeedCtorLink'),
       (0x800000, 'NoExport'), (0x1000000, 'Button'), (0x2000000, 'CommentString'), (0x4000000, 'EditInline'),
       (0x8000000, 'EdFindable'), (0x10000000, 'EditInlineUse'), (0x20000000, 'Deprecated'),
       (0x40000000, 'EditInlineNotify')]
FUNC = [(0x1, 'Final'), (0x2, 'Defined'), (0x4, 'Iterator'), (0x8, 'Latent'), (0x10, 'PreOperator'),
        (0x20, 'Singular'), (0x40, 'Net'), (0x80, 'NetReliable'), (0x100, 'Simulated'), (0x200, 'Exec'),
        (0x400, 'Native'), (0x800, 'Event'), (0x1000, 'Operator'), (0x2000, 'Static'), (0x4000, 'NoExport'),
        (0x8000, 'Const'), (0x10000, 'Invariant'), (0x20000, 'Public'), (0x40000, 'Private'),
        (0x80000, 'Protected'), (0x100000, 'Delegate')]
CLASS = [(0x1, 'Abstract'), (0x2, 'Compiled'), (0x4, 'Config'), (0x8, 'Transient'), (0x10, 'Parsed'),
         (0x20, 'Localized'), (0x40, 'SafeReplace'), (0x80, 'RuntimeStatic'), (0x100, 'NoExport'),
         (0x200, 'Placeable'), (0x400, 'PerObjectConfig'), (0x800, 'NativeReplication'),
         (0x1000, 'EditInlineNew'), (0x2000, 'CollapseCategories'), (0x4000, 'ExportStructs'),
         (0x8000, 'IsAUProperty'), (0x10000, 'IsAUObjectProperty'), (0x20000, 'IsAUBoolProperty'),
         (0x40000, 'IsAUState'), (0x80000, 'IsAUFunction'), (0x100000, 'NeedsDefProps')]
STATE = [(0x1, 'Editable'), (0x2, 'Auto'), (0x4, 'Simulated')]
CPF_NET, CPF_PARM, CPF_RETURNPARM = 0x20, 0x80, 0x400
FUNC_NET, FUNC_NATIVE = 0x40, 0x400


def flag_names(value: int, table) -> List[str]:
    out = [name for bit, name in table if value & bit]
    rest = value & ~sum(bit for bit, _ in table)
    if rest:
        out.append('0x%x' % rest)
    return out


def hx(v: Optional[int]) -> Optional[str]:
    return None if v is None else '0x%08x' % v


class EngineError(Exception):
    pass


# ---- names ------------------------------------------------------------------------------------

def read_name_array(memory, address: int) -> Tuple[Dict[int, str], dict]:
    """The editor's GNames: TArray<FNameEntry*> at `address`; entry +0 Index, +0x0C text."""
    data, num, cap = memory.u32(address), memory.i32(address + 4), memory.i32(address + 8)
    if not data or num is None or not 0 < num <= 8_000_000:
        raise EngineError('no name array at 0x%08x (data %s, num %s)' % (address, hx(data), num))
    raw = memory.read(data, num * 4)
    entries = struct.unpack('<%dI' % (len(raw) // 4), raw)
    names, mismatched = {}, 0
    for i, e in enumerate(entries):
        if not e:
            continue
        b = memory.read(e, 12 + 64)
        if len(b) < 13:
            continue
        index = struct.unpack_from('<i', b)[0]
        end = b.find(b'\0', 12)
        text = b[12:end if end >= 0 else len(b)].decode('latin-1')
        if index != i:
            mismatched += 1
        names[i] = text
    return names, {'source': 'GNames array', 'address': hx(address), 'slots': num, 'max': cap,
                   'entries': len(names), 'indexMismatches': mismatched}


def _plausible_link(v: int) -> bool:
    return v == 0 or 0x10000 < v < 0x7FFFFFFF


def decode_wide_entry(raw: bytes, read) -> Optional[Tuple[int, str, int, int]]:
    """(index, text, link a, link b) of a std::wstring FNameEntry, or None."""
    if len(raw) < 0x24:
        return None
    link_a, link_b = struct.unpack_from('<II', raw, 0)
    size, capacity, index = struct.unpack_from('<III', raw, 0x18)
    if capacity > 1024 or size > capacity or size == 0 or (capacity & 7) != 7 or index > 4_000_000:
        return None
    if not _plausible_link(link_a) or not _plausible_link(link_b):
        return None
    if capacity < 8:
        text = raw[8:8 + size * 2 + 2]
    else:
        pointer = struct.unpack_from('<I', raw, 8)[0]
        if not 0x10000 < pointer < 0x7FFFFFFF:
            return None
        text = read(pointer, size * 2 + 2)
    if len(text) != size * 2 + 2 or text[-2:] != b'\0\0':
        return None
    try:
        s = text[:-2].decode('utf-16-le')
    except UnicodeDecodeError:
        return None
    if not all(32 <= ord(c) < 127 for c in s):
        return None
    return index, s, link_a, link_b


def scan_wide_names(memory, skip: Tuple[int, int] = (0, 0), max_region: int = 256 << 20):
    """The game's names, by scanning its private heap for FNameEntry blocks. An entry whose two
    hash links both point at other entries is 'strict'; one with a null link (an end of its hash
    chain) is accepted when its text checks out. Strict entries win a clash."""
    import numpy
    strict: Dict[int, str] = {}
    relaxed: Dict[int, str] = {}
    clashes = 0
    candidates = 0
    for base, size, _, kind in memory.regions():
        if size > max_region or skip[0] <= base < skip[1]:
            continue
        data = memory.read(base, size)
        usable = len(data) // 4 * 4
        if usable < 48:
            continue
        words = numpy.frombuffer(data[:usable], dtype=numpy.uint32)
        cap = words[7:]
        sz = words[6:len(words) - 1]
        hits = numpy.nonzero(((cap & 7) == 7) & (cap <= 1024) & (sz <= cap) & (sz > 0))[0]
        candidates += len(hits)
        for e in hits:
            start = int(e) * 4
            got = decode_wide_entry(data[start:start + 0x24], memory.read)
            if not got:
                continue
            index, text, a, b = got
            both = a and b and all(((memory.u32(x + 0x1C) or 0) & 7) == 7 for x in (a, b))
            if not both and ' ' in text:
                continue      # a space is only taken from an entry whose links check out
            table = strict if both else relaxed
            if index in table and table[index] != text:
                clashes += 1
                continue
            table[index] = text
    names = dict(relaxed)
    names.update(strict)
    return names, {'source': 'heap scan', 'strict': len(strict), 'relaxed': len(relaxed),
                   'entries': len(names), 'clashes': clashes, 'candidates': candidates}


# ---- the engine -------------------------------------------------------------------------------

class Engine:
    def __init__(self, memory, profile: str, names: Optional[Dict[int, str]] = None, name_info: Optional[dict] = None,
                 image_size: Optional[int] = None, min_objects: int = 1000):
        self.m = memory
        self.profile = profile
        p = PROFILES[profile]
        self.char = p['char']
        self.image_base = p['imageBase']
        self.image_size = image_size or 0
        self.gobjects = p['gobjects']
        self.cl = p['class']
        self.pl = p['property']
        self.checks: Dict[str, object] = {}
        self.min_objects = min_objects
        self._load_objects()
        if names is None:
            kind, address = p['names']
            if kind == 'array':
                names, name_info = read_name_array(memory, address)
            else:
                names, name_info = scan_wide_names(memory, (self.image_base, self.image_base + (self.image_size or 0x1000000)))
        self.names = names
        self.name_info = dict(name_info or {})
        unresolved = sum(1 for h in self.headers.values() if h[8] not in self.names)
        self.name_info['objectsWithUnresolvedName'] = unresolved
        self._paths: Dict[int, str] = {}
        self._meta: Dict[int, tuple] = {}
        self._enums: Dict[int, List[str]] = {}
        self._links: Dict[int, List[int]] = {}
        self._ctor: Dict[tuple, Optional[int]] = {}
        self.by_class: Dict[str, List[int]] = defaultdict(list)
        for o, h in self.headers.items():
            self.by_class[self.obj_name(h[9])].append(o)

    # objects
    def _load_objects(self):
        data, num, cap = self.m.u32(self.gobjects), self.m.i32(self.gobjects + 4), self.m.i32(self.gobjects + 8)
        if not data or num is None or not self.min_objects <= num < 4_000_000:
            raise EngineError('no object table at 0x%08x (data %s, num %s)' % (self.gobjects, hx(data), num))
        raw = self.m.read(data, num * 4)
        slots = struct.unpack('<%dI' % (len(raw) // 4), raw)
        self.headers: Dict[int, Tuple[int, ...]] = {}
        bad_index = 0
        for i, o in enumerate(slots):
            if not o:
                continue
            b = self.m.read(o, OBJECT_HEADER)
            if len(b) != OBJECT_HEADER:
                continue
            h = struct.unpack('<10I', b)
            if h[1] != i:
                bad_index += 1
            self.headers[o] = h
        self.slots = len(slots)
        self.checks['objectTable'] = {'address': hx(self.gobjects), 'data': hx(data), 'num': num, 'max': cap,
                                      'live': len(self.headers), 'indexMismatches': bad_index}

    def is_object(self, o: int) -> bool:
        return o in self.headers

    def obj_name(self, o: int) -> str:
        if not o:
            return 'None'
        h = self.headers.get(o)
        if h is None:
            return '?%08x' % o
        return self.names.get(h[8], '#%d' % h[8])

    def class_of(self, o: int) -> int:
        h = self.headers.get(o)
        return h[9] if h else 0

    def kind(self, o: int) -> str:
        return self.obj_name(self.class_of(o))

    def outer(self, o: int) -> int:
        h = self.headers.get(o)
        return h[6] if h else 0

    def path(self, o: int) -> Optional[str]:
        if not o:
            return None
        cached = self._paths.get(o)
        if cached is not None:
            return cached
        parts, x, guard = [], o, 0
        while x and guard < 32:
            parts.append(self.obj_name(x))
            x = self.outer(x)
            guard += 1
        p = '.'.join(reversed(parts))
        self._paths[o] = p
        return p

    def package(self, o: int) -> str:
        x, last, guard = o, o, 0
        while x and guard < 32:
            last, x = x, self.outer(x)
            guard += 1
        return self.obj_name(last)

    def in_image(self, a: int) -> bool:
        return bool(self.image_size) and self.image_base <= a < self.image_base + self.image_size

    def where(self, a: Optional[int]) -> Optional[str]:
        """`module+0xoffset` for an address in a loaded module other than the main image."""
        if not a or self.in_image(a):
            return None
        for base, size, name in getattr(self, 'modules', ()):
            if base <= a < base + size:
                return '%s+0x%x' % (name, a - base)
        return '?'

    def rva(self, a: Optional[int]) -> Optional[str]:
        return '0x%06x' % (a - self.image_base) if a and self.in_image(a) else None

    def name(self, index: Optional[int]) -> Optional[str]:
        if index is None:
            return None
        return self.names.get(index, '#%d' % index)

    def fields(self, struct_obj: int) -> List[int]:
        """The Children chain of a struct, class, state or function (declared fields, not inherited)."""
        out, f, seen = [], self.m.u32(struct_obj + S_CHILDREN) or 0, set()
        while f and f not in seen and len(out) < 20000:
            seen.add(f)
            out.append(f)
            f = self.m.u32(f + F_NEXT) or 0
        return out

    def link(self, struct_obj: int, head: int = S_PROPLINK, nxt: int = P_LINKNEXT) -> List[int]:
        out, f, seen = [], self.m.u32(struct_obj + head) or 0, set()
        while f and f not in seen and len(out) < 20000:
            seen.add(f)
            out.append(f)
            f = self.m.u32(f + nxt) or 0
        return out

    def fstring(self, address: int, limit: int = 4096) -> Optional[str]:
        data, num = self.m.u32(address), self.m.i32(address + 4)
        if not data or not num or num <= 0:
            return '' if num == 0 or not data else None
        n = min(num, limit)
        raw = self.m.read(data, n * self.char)
        try:
            text = raw.decode('utf-16-le' if self.char == 2 else 'latin-1', errors='replace')
        except Exception:
            return None
        return text.rstrip('\0') if n == num else text + '...'

    def tarray(self, address: int) -> Tuple[int, int, int]:
        b = self.m.read(address, 12)
        return struct.unpack('<3I', b) if len(b) == 12 else (0, 0, 0)

    # ---- one record per reflection object ----------------------------------------------------
    def describe_property(self, p: int) -> dict:
        m = self.m
        raw = m.read(p, self.pl['extra2'] + 4)
        if len(raw) < self.pl['extra2'] + 4:
            return {'path': self.path(p), 'kind': self.kind(p), 'error': 'unreadable'}
        dim, size, flags, category, offset = struct.unpack_from('<HHIIi', raw, P_DIM)
        kind = self.kind(p)
        rec = {'path': self.path(p), 'owner': self.path(self.outer(p)), 'name': self.obj_name(p), 'kind': kind,
               'offset': offset, 'elementSize': size, 'arrayDim': dim, 'flags': hx(flags),
               'flagNames': flag_names(flags, CPF)}
        if category and category in self.names and self.names[category] != 'None':
            rec['category'] = self.names[category]
        rep, rep_index = struct.unpack_from('<HH', raw, self.pl['repOffset'])
        if flags & CPF_NET:
            rec['repOffset'] = rep
            rec['repIndex'] = rep_index
            owner = struct.unpack_from('<I', raw, self.pl['repOwner'])[0]
            if owner:
                rec['repOwner'] = self.path(owner) if self.is_object(owner) else hx(owner)
        extra, extra2 = struct.unpack_from('<II', raw, self.pl['extra'])
        if kind == 'BoolProperty':
            rec['bitMask'] = hx(extra)
        elif kind == 'ByteProperty':
            rec['enum'] = self.path(extra) if extra else None
        elif kind == 'ObjectProperty':
            rec['propertyClass'] = self.path(extra)
        elif kind == 'ClassProperty':
            rec['propertyClass'] = self.path(extra)
            rec['metaClass'] = self.path(extra2)
        elif kind == 'StructProperty':
            rec['struct'] = self.path(extra)
        elif kind == 'ArrayProperty':
            rec['inner'] = self.path(extra)
            rec['innerKind'] = self.kind(extra) if extra else None
        elif kind == 'DelegateProperty':
            rec['function'] = self.path(extra) if extra and self.is_object(extra) else hx(extra)
        elif kind == 'FixedArrayProperty':
            rec['inner'] = self.path(extra)
            rec['count'] = extra2
        return rec

    def describe_function(self, f: int) -> dict:
        m = self.m
        raw = m.read(f + FN_FLAGS, 0x12)
        flags, func, inative, rep, parms, ret, prec, nparms = struct.unpack('<IIHHHHBB', raw[:18])
        params = []
        locals_ = 0
        for c in self.fields(f):
            k = self.kind(c)
            if k.endswith('Property'):
                pf = m.u32(c + P_FLAGS) or 0
                if pf & CPF_PARM:
                    params.append(self.obj_name(c) + ('!' if pf & CPF_RETURNPARM else ''))
                else:
                    locals_ += 1
        rec = {'path': self.path(f), 'owner': self.path(self.outer(f)), 'name': self.obj_name(f),
               'super': self.path(m.u32(f + F_SUPER) or 0),
               'flags': hx(flags), 'flagNames': flag_names(flags, FUNC), 'func': hx(func), 'funcRva': self.rva(func),
               'iNative': inative, 'parmsSize': parms, 'returnValueOffset': None if ret == 0xFFFF else ret,
               'operPrecedence': prec, 'numParms': nparms, 'scriptSize': m.i32(f + S_SCRIPTSIZE),
               'propertiesSize': m.i32(f + S_SIZE), 'params': params, 'locals': locals_,
               'friendlyName': self.name(m.u32(f + S_FRIENDLY))}
        if flags & FUNC_NET:
            rec['repOffset'] = rep
        if func and not self.in_image(func):
            rec['funcModule'] = self.where(func)
        return rec

    def describe_state(self, s: int) -> dict:
        m = self.m
        probe, ignore = m.u64(s + ST_PROBE), m.u64(s + ST_IGNORE)
        flags = m.u32(s + ST_FLAGS) or 0
        labels = m.u16(s + ST_LABELS)
        funcs = [self.obj_name(c) for c in self.fields(s) if self.kind(c) == 'Function']
        return {'path': self.path(s), 'owner': self.path(self.outer(s)), 'super': self.path(m.u32(s + F_SUPER) or 0),
                'probeMask': '0x%016x' % (probe or 0), 'ignoreMask': '0x%016x' % (ignore or 0),
                'stateFlags': hx(flags), 'stateFlagNames': flag_names(flags, STATE),
                'labelTableOffset': None if labels == 0xFFFF else labels, 'scriptSize': m.i32(s + S_SCRIPTSIZE),
                'functions': funcs}

    def describe_struct(self, s: int) -> dict:
        m = self.m
        props = [c for c in self.fields(s) if self.kind(c).endswith('Property')]
        return {'path': self.path(s), 'owner': self.path(self.outer(s)), 'super': self.path(m.u32(s + F_SUPER) or 0),
                'propertiesSize': m.i32(s + S_SIZE), 'structFlags': hx(m.u32(s + S_STRUCTFLAGS)),
                'scriptSize': m.i32(s + S_SCRIPTSIZE), 'properties': [self.obj_name(c) for c in props]}

    def describe_enum(self, e: int) -> dict:
        data, num, _ = self.tarray(e + E_NAMES)
        values = []
        if data and 0 < num < 4096:
            raw = self.m.read(data, num * 4)
            values = [self.name(v) for v in struct.unpack('<%dI' % (len(raw) // 4), raw)]
        return {'path': self.path(e), 'owner': self.path(self.outer(e)), 'names': values}

    def describe_const(self, k: int) -> dict:
        return {'path': self.path(k), 'owner': self.path(self.outer(k)), 'value': self.fstring(k + K_VALUE)}

    def constructor_vtable(self, address: int, depth: int = 0) -> Optional[int]:
        """The vtable a class constructor (UClass::ClassConstructor, InternalConstructor<T>) stores
        last into the new object: `mov dword ptr [reg], imm32` with imm32 an image address whose
        first slot is code. When the constructor only calls T::T, that call is followed (two levels;
        jmp thunks too). Needs capstone; None without it or when no store is found."""
        if not address or not self.in_image(address) or depth > 3:
            return None
        key = (address, depth)
        if key in self._ctor:
            return self._ctor[key]
        self._ctor[key] = None
        try:
            import capstone
        except ImportError:
            return None
        md = getattr(self, '_md', None)
        if md is None:
            md = self._md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        code = self.m.read(address, 0x800)
        found, last_call, this_reg = None, None, None
        for n, ins in enumerate(md.disasm(code, address)):
            if n > 400:
                break
            if n == 0 and ins.mnemonic == 'jmp' and ins.op_str.startswith('0x'):
                found = self.constructor_vtable(int(ins.op_str, 16), depth + 1)
                break
            target = ins.op_str.split(']')[0]
            if ins.mnemonic == 'mov' and target.startswith('dword ptr [') and '+' not in target and '-' not in target:
                imm = ins.op_str.split(', ')[-1]
                if imm.startswith('0x'):
                    v = int(imm, 16)
                    if self.in_image(v) and self.in_image(self.m.u32(v) or 0):
                        reg = target[len('dword ptr ['):]
                        # The first vtable store goes into the new object; later ones into the same
                        # register win (the most derived class), stores through other registers are
                        # member objects being built.
                        if this_reg is None:
                            this_reg = reg
                        if reg == this_reg:
                            found = v
            if ins.mnemonic == 'call' and ins.op_str.startswith('0x') and found is None:
                last_call = int(ins.op_str, 16)
            if ins.mnemonic == 'ret':
                break
        if found is None and last_call:
            found = self.constructor_vtable(last_call, depth + 1)
        self._ctor[key] = found
        return found

    def describe_class(self, c: int, vtables: Dict[int, Counter]) -> dict:
        m = self.m
        raw = m.read(c, self.cl['constructor'] + 4)
        if len(raw) < self.cl['constructor'] + 4:
            return {'path': self.path(c), 'error': 'unreadable'}
        u = lambda off: struct.unpack_from('<I', raw, off)[0]
        flags = u(self.cl['flags'])
        ddata, dnum, dmax = struct.unpack_from('<3I', raw, self.cl['defaults'])
        deps_data, deps_num, _ = struct.unpack_from('<3I', raw, self.cl['dependencies'])
        imp_data, imp_num, _ = struct.unpack_from('<3I', raw, self.cl['imports'])
        reps_data, reps_num, _ = struct.unpack_from('<3I', raw, self.cl['classReps'])
        net_data, net_num, _ = struct.unpack_from('<3I', raw, self.cl['netFields'])
        net_fields = []
        if net_data and 0 < net_num < 10000:
            b = m.read(net_data, net_num * 4)
            net_fields = [self.obj_name(v) for v in struct.unpack('<%dI' % (len(b) // 4), b)]
        imports = []
        if imp_data and 0 < imp_num < 1000:
            b = m.read(imp_data, imp_num * 4)
            imports = [self.name(v) for v in struct.unpack('<%dI' % (len(b) // 4), b)]
        deps = []
        if deps_data and 0 < deps_num < 100000:
            b = m.read(deps_data, deps_num * 12)
            for i in range(len(b) // 12):
                cls, deep, crc = struct.unpack_from('<3I', b, i * 12)
                deps.append(self.path(cls))
        children = self.fields(c)
        kinds = Counter(self.kind(x) for x in children)
        inst = vtables.get(c, Counter())
        ctor = u(self.cl['constructor'])
        ctor_vt = self.constructor_vtable(ctor)
        rec = {'path': self.path(c), 'name': self.obj_name(c), 'package': self.package(c), 'address': hx(c),
               'static': self.in_image(c), 'super': self.path(u(F_SUPER)), 'within': self.path(u(self.cl['within'])),
               'configName': self.name(u(self.cl['config'])), 'flags': hx(flags), 'flagNames': flag_names(flags, CLASS),
               'propertiesSize': struct.unpack_from('<i', raw, S_SIZE)[0], 'defaultsSize': dnum,
               'scriptSize': struct.unpack_from('<i', raw, S_SCRIPTSIZE)[0],
               'objectFlags': hx(u(O_FLAGS)), 'classVtable': hx(u(O_VTABLE)),
               'vtable': None, 'vtableRva': None, 'vtableSource': None, 'constructorVtable': hx(ctor_vt),
               'instanceVtable': hx(inst.most_common(1)[0][0]) if inst else None,
               'instances': sum(inst.values()), 'constructor': hx(ctor), 'constructorRva': self.rva(ctor),
               'probeMask': '0x%016x' % (m.u64(c + ST_PROBE) or 0), 'ignoreMask': '0x%016x' % (m.u64(c + ST_IGNORE) or 0),
               'packageImports': imports, 'dependencies': len(deps), 'classReps': reps_num,
               'netFields': net_fields,
               'properties': [self.obj_name(x) for x in children if self.kind(x).endswith('Property')],
               'functions': [self.obj_name(x) for x in children if self.kind(x) == 'Function'],
               'states': [self.obj_name(x) for x in children if self.kind(x) == 'State'],
               'childKinds': dict(sorted(kinds.items()))}
        if len(inst) > 1:
            rec['otherVtables'] = [hx(v) for v, _ in inst.most_common()[1:4]]
        return rec

    # ---- everything ----------------------------------------------------------------------------
    def reflect(self, log=lambda *_: None) -> dict:
        vtables: Dict[int, Counter] = defaultdict(Counter)
        for o, h in self.headers.items():
            vtables[h[9]][h[0]] += 1
        kinds = Counter(self.kind(o) for o in self.headers)
        classes = sorted(self.by_class.get('Class', []), key=self.path)
        out = {'classes': [], 'structs': [], 'states': [], 'functions': [], 'properties': [], 'enums': [], 'consts': []}
        for c in classes:
            out['classes'].append(self.describe_class(c, vtables))
        self._resolve_vtables(out['classes'])
        log('classes', len(classes))
        for s in self.by_class.get('Struct', []):
            out['structs'].append(self.describe_struct(s))
        for s in self.by_class.get('State', []):
            out['states'].append(self.describe_state(s))
        for f in self.by_class.get('Function', []):
            out['functions'].append(self.describe_function(f))
        log('functions', len(out['functions']))
        for k, objs in self.by_class.items():
            if k.endswith('Property'):
                out['properties'].extend(self.describe_property(p) for p in objs)
        log('properties', len(out['properties']))
        for e in self.by_class.get('Enum', []):
            out['enums'].append(self.describe_enum(e))
        for k in self.by_class.get('Const', []):
            out['consts'].append(self.describe_const(k))
        for key in out:
            out[key].sort(key=lambda r: r.get('path') or '')
        out['objectKinds'] = dict(kinds.most_common())
        self.checks.update(self.self_checks(out))
        return out

    def _resolve_vtables(self, records: List[dict]):
        """The C++ vtable of each class's instances: read from live instances when there are any;
        else taken from another class with the same ClassConstructor that has instances (the same
        constructor builds the same C++ type); else the store found in the constructor's code."""
        by_ctor: Dict[str, set] = defaultdict(set)
        for r in records:
            if r.get('instanceVtable') and not r.get('otherVtables') and r.get('constructor'):
                by_ctor[r['constructor']].add(r['instanceVtable'])
        for r in records:
            if r.get('instanceVtable') and not r.get('otherVtables'):
                r['vtable'], r['vtableSource'] = r['instanceVtable'], 'instances'
            elif len(by_ctor.get(r.get('constructor'), ())) == 1:
                r['vtable'], r['vtableSource'] = next(iter(by_ctor[r['constructor']])), 'sameConstructor'
            elif r.get('constructorVtable'):
                r['vtable'], r['vtableSource'] = r['constructorVtable'], 'constructorCode'
            if r['vtable']:
                r['vtableRva'] = self.rva(int(r['vtable'], 16))

    def self_checks(self, out: dict) -> dict:
        """Facts that must hold if the layout above is right; each is a count to read, not a pass."""
        props = out['properties']
        funcs = out['functions']
        classes = out['classes']
        net = [p for p in props if 'Net' in p['flagNames']]
        script = {c['path']: c.get('scriptSize') for c in classes}
        meta = self.by_class.get('Class', [])
        class_class = [c for c in meta if self.class_of(c) == c]
        return {
            'classClassSelf': [self.path(c) for c in class_class],
            'netProperties': len(net), 'netPropertiesWithRepOffset0': sum(1 for p in net if not p.get('repOffset')),
            'propertiesOffsetOutsideOwner': sum(1 for p in props if p['offset'] < 0 or p['offset'] > 0x100000),
            'classesDefaultsSizeNotPropertiesSize': sum(1 for c in classes if c.get('defaultsSize') != c.get('propertiesSize')),
            'nativeFunctions': sum(1 for f in funcs if 'Native' in f['flagNames']),
            'nativeFunctionsWithINative': sum(1 for f in funcs if 'Native' in f['flagNames'] and f['iNative']),
            'functionsWithFuncOutsideImage': dict(Counter((f.get('funcModule') or '?').split('+')[0]
                                                          for f in funcs if f.get('funcModule'))),
            'netFunctions': sum(1 for f in funcs if 'Net' in f['flagNames']),
            'netFunctionsWithRepOffset0': sum(1 for f in funcs if 'Net' in f['flagNames'] and not f.get('repOffset')),
            'functionsNumParmsMismatch': sum(1 for f in funcs if f['numParms'] != len(f['params'])),
            'staticClasses': sum(1 for c in classes if c['static']),
            'vtableSources': dict(Counter(c.get('vtableSource') or 'none' for c in classes)),
            'classesWithInstances': sum(1 for c in classes if c.get('instanceVtable')),
            'constructorCodeVtableAgreesWithInstances': sum(1 for c in classes if c.get('instanceVtable')
                                                            and c.get('constructorVtable') == c['instanceVtable']),
            'sameConstructorConflicts': len({c.get('constructor') for c in classes if c.get('instanceVtable')
                                             and not c.get('otherVtables')}) - len({(c.get('constructor'), c.get('instanceVtable'))
                                                                                  for c in classes if c.get('instanceVtable')
                                                                                  and not c.get('otherVtables')}),
            'netPropertiesRepOffsetPastClassScript': sum(1 for p in net if (p.get('repOffset') or 0) >=
                                                         (script.get(p['owner']) or 0) and p['owner'] in script),
            'netFunctionsRepOffsetPastClassScript': sum(1 for f in funcs if 'Net' in f['flagNames']
                                                        and f['owner'] in script
                                                        and (f.get('repOffset') or 0) >= (script.get(f['owner']) or 0)),
        }

    # ---- class defaults ------------------------------------------------------------------------
    def class_defaults(self, packages: Iterable[str], raw: bool = True, log=lambda *_: None) -> List[dict]:
        want = {p.lower() for p in packages}
        out = []
        for c in sorted(self.by_class.get('Class', []), key=self.path):
            if self.package(c).lower() not in want:
                continue
            data, num, _ = self.tarray(c + self.cl['defaults'])
            size = self.m.i32(c + S_SIZE)
            rec = {'path': self.path(c), 'super': self.path(self.m.u32(c + F_SUPER) or 0), 'propertiesSize': size,
                   'defaultsAddress': hx(data), 'defaultsSize': num}
            if not data or num <= 0 or num > 0x100000:
                rec['error'] = 'no defaults'
                out.append(rec)
                continue
            block = self.m.read(data, num)
            if len(block) != num:
                rec['error'] = 'defaults unreadable'
                out.append(rec)
                continue
            if raw:
                rec['raw'] = block.hex()
            values = {}
            for p in self.sorted_link(c):
                name = self.obj_name(p)
                owner = self.obj_name(self.outer(p))
                key = name if name not in values else '%s.%s' % (owner, name)
                try:
                    values[key] = self.decode_property(p, block, 0, depth=0)
                except Exception as error:   # keep going; the raw bytes are there
                    values[key] = {'error': str(error)}
            rec['values'] = values
            out.append(rec)
        log('class defaults', len(out))
        return out

    def _prop_meta(self, p: int):
        cached = self._meta.get(p)
        if cached:
            return cached
        raw = self.m.read(p, self.pl['extra2'] + 4)
        dim, size, flags, _, offset = struct.unpack_from('<HHIIi', raw, P_DIM)
        extra, extra2 = struct.unpack_from('<II', raw, self.pl['extra'])
        self._meta[p] = meta = (self.kind(p), dim, size, offset, extra, extra2)
        return meta

    def sorted_link(self, s: int) -> List[int]:
        """PropertyLink of a class or struct (inherited properties too), by offset."""
        cached = self._links.get(s)
        if cached is None:
            cached = self._links[s] = sorted(self.link(s), key=lambda x: self._prop_meta(x)[3])
        return cached

    def decode_property(self, p: int, block: bytes, base: int, depth: int):
        kind, dim, size, offset, extra, extra2 = self._prop_meta(p)
        if dim <= 1:
            return self.decode_value(kind, size, extra, extra2, block, base + offset, depth)
        return [self.decode_value(kind, size, extra, extra2, block, base + offset + i * size, depth) for i in range(dim)]

    def decode_value(self, kind, size, extra, extra2, block: bytes, at: int, depth: int):
        if at < 0 or at + max(size, 1) > len(block):
            return {'error': 'outside block'}
        if kind == 'BoolProperty':
            return bool(struct.unpack_from('<I', block, at)[0] & (extra or 1))
        if kind == 'ByteProperty':
            v = block[at]
            if extra and self.is_object(extra):
                names = self._enums.get(extra)
                if names is None:
                    names = self._enums[extra] = self.describe_enum(extra)['names']
                return {'value': v, 'enum': names[v] if v < len(names) else None}
            return v
        if kind == 'IntProperty':
            return struct.unpack_from('<i', block, at)[0]
        if kind == 'FloatProperty':
            v = struct.unpack_from('<f', block, at)[0]
            return v if math.isfinite(v) else repr(v)
        if kind == 'NameProperty':
            return self.name(struct.unpack_from('<I', block, at)[0])
        if kind in ('ObjectProperty', 'ClassProperty'):
            v = struct.unpack_from('<I', block, at)[0]
            if not v:
                return None
            return self.path(v) if self.is_object(v) else '?0x%08x' % v
        if kind == 'StrProperty':
            data, num = struct.unpack_from('<Ii', block, at)
            if not data or num <= 0:
                return ''
            raw = self.m.read(data, min(num, 4096) * self.char)
            return raw.decode('utf-16-le' if self.char == 2 else 'latin-1', errors='replace').rstrip('\0')
        if kind == 'StructProperty':
            if depth > 6 or not extra:
                return {'raw': block[at:at + size].hex()}
            out = {}
            for q in self.sorted_link(extra):
                out[self.obj_name(q)] = self.decode_property(q, block, at, depth + 1)
            return out
        if kind == 'ArrayProperty':
            data, num, cap = struct.unpack_from('<3I', block, at)
            if not num:
                return []
            if not extra or num > 100000:
                return {'count': num, 'data': hx(data)}
            ikind, idim, isize, _, iextra, iextra2 = self._prop_meta(extra)
            shown = min(num, 64)
            chunk = self.m.read(data, shown * isize)
            items = [self.decode_value(ikind, isize, iextra, iextra2, chunk, i * isize, depth + 1)
                     for i in range(len(chunk) // max(isize, 1))]
            return items if num <= 64 else {'count': num, 'first': items}
        if kind == 'DelegateProperty':
            obj, fname = struct.unpack_from('<II', block, at)
            return {'object': self.path(obj) if obj else None, 'function': self.name(fname)}
        return {'kind': kind, 'raw': block[at:at + size].hex()}
