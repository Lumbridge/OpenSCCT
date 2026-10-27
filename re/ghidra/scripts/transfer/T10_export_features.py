# Exports per-function matching features of the current program (editor or game) for the name
# transfer: entry, current name and its source, size, call targets, referenced strings, data
# references, read-only constants, an address-free instruction token stream, and the C++ vtables
# found from constructor stores. Output goes to local/re-out/c/transfer/<id>.features.json, which is
# local only: it holds instruction tokens taken from the binary.
# Run read-only: analyzeHeadless <project> SVM -process <program> -noanalysis -readOnly
# @category SVM.transfer
# @runtime PyGhidra

import json
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

OUT = os.path.join(svm.REPO_DIR, 'local', 're-out', 'c', 'transfer')
MAX_SLOTS = 600


def vtable_starts(base, image, blocks):
    """Probable vtable starts: 'mov dword ptr [reg], imm32' stores into read-only data whose first
    slot points at code, which is what a constructor does."""
    text = [(lo, hi) for name, lo, hi, x in blocks if x]
    rdata = [(lo, hi) for name, lo, hi, x in blocks if name.startswith('.rdata')]

    def inr(v, ranges):
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
                if inr(t, rdata) and 0 <= t - base < len(image) - 4:
                    first = struct.unpack_from('<I', image, t - base)[0]
                    if inr(first, text):
                        starts.add(t)
            i += 1
    return starts


def source_key(sym):
    s = str(sym.getSource())
    return {'USER_DEFINED': 'user', 'IMPORTED': 'imported', 'ANALYSIS': 'analysis', 'DEFAULT': 'default'}.get(s, s)


def operand_token(ins, i, lo, hi):
    from ghidra.program.model.scalar import Scalar
    from ghidra.program.model.lang import Register
    from ghidra.program.model.address import Address
    parts = []
    for o in ins.getOpObjects(i):
        if isinstance(o, Register):
            parts.append(str(o.getName()))
        elif isinstance(o, Scalar):
            v = o.getUnsignedValue()
            if lo <= v < hi:
                parts.append('A')
            elif v < 0x10000:
                parts.append('%x' % v)
            else:
                parts.append('I')
        elif isinstance(o, Address):
            parts.append('A')
        else:
            parts.append('?')
    return ','.join(parts)


ctx = svm.setup(globals())
program = ctx.program
os.makedirs(OUT, exist_ok=True)
listing = program.getListing()
fm = program.getFunctionManager()
mem = program.getMemory()
base = program.getImageBase().getOffset()
lo, image, blocks = svm.loaded_image(program)
hi = lo + len(image)
rdata = [(blo, bhi) for name, blo, bhi, x in blocks if name.startswith('.rdata')]
code = [(blo, bhi) for name, blo, bhi, x in blocks if x]


def in_ranges(v, ranges):
    for a, b in ranges:
        if a <= v < b:
            return True
    return False


PRINTABLE = set(range(0x20, 0x7f)) | {9, 10, 13}


def decode_string(t):
    """Text of a NUL-terminated UTF-16LE or 8-bit string at address t, or None. The editor is an
    8-bit build and the game a UTF-16 one, so only the text is kept."""
    o = t - lo
    if o < 0 or o >= len(image) - 2:
        return None
    if image[o] in PRINTABLE and image[o + 1] == 0:
        chars = []
        while o + 1 < len(image) and len(chars) < 400:
            c = image[o] | (image[o + 1] << 8)
            if c == 0:
                break
            if c > 0x7e or (c < 0x20 and c not in (9, 10, 13)):
                return None
            chars.append(chr(c))
            o += 2
        return ''.join(chars)[:200] if len(chars) >= 2 else None
    chars = []
    while o < len(image) and len(chars) < 400:
        c = image[o]
        if c == 0:
            break
        if c not in PRINTABLE:
            return None
        chars.append(chr(c))
        o += 1
    return ''.join(chars)[:200] if len(chars) >= 3 else None


def resolve(f):
    seen = 0
    while f is not None and f.isThunk() and seen < 8:
        f = f.getThunkedFunction(False)
        seen += 1
    return f


from ghidra.program.model.scalar import Scalar  # noqa: E402

funcs = []
it = fm.getFunctions(True)
n = 0
while it.hasNext():
    f = it.next()
    n += 1
    if n % 2000 == 0:
        println('features: %d functions' % n)
    entry = f.getEntryPoint().getOffset()
    sym = f.getSymbol()
    src = source_key(sym)
    rec = {'a': entry, 'src': src, 'size': int(f.getBody().getNumAddresses()), 'thunk': bool(f.isThunk())}
    if src in ('user', 'imported'):
        rec['name'] = str(f.getName(True))
    callees, strings, drefs, consts, toks = [], [], [], [], []
    ninstr = 0
    ins_it = listing.getInstructions(f.getBody(), True)
    while ins_it.hasNext():
        ins = ins_it.next()
        ninstr += 1
        mn = str(ins.getMnemonicString())
        ops = [operand_token(ins, i, lo, hi) for i in range(ins.getNumOperands())]
        toks.append(mn + (' ' + ';'.join(ops) if ops else ''))
        ft = ins.getFlowType()
        seen_targets = set()
        for ref in ins.getReferencesFrom():
            to = ref.getToAddress()
            if not to.isMemoryAddress():
                continue
            t = to.getOffset()
            rt = ref.getReferenceType()
            if rt.isCall() or (rt.isJump() and ft.isJump() and not ft.isConditional()):
                g = fm.getFunctionAt(to)
                if g is not None:
                    g2 = resolve(g)
                    tgt = g2.getEntryPoint().getOffset() if g2 is not None and not g2.isExternal() else t
                    if rt.isCall() or tgt != entry:
                        callees.append(tgt)
                    continue
                if rt.isCall():
                    callees.append(t)
                continue
            if rt.isData() or rt.isRead() or rt.isWrite() or rt.isIndirect():
                seen_targets.add(t)
        # Scalars that point into the image but not at code (pushed string or table addresses).
        for i in range(ins.getNumOperands()):
            for o in ins.getOpObjects(i):
                if isinstance(o, Scalar):
                    v = o.getUnsignedValue()
                    if lo <= v < hi and not in_ranges(v, code):
                        seen_targets.add(v)
                    elif v >= 0x100 and not (lo <= v < hi):
                        consts.append('i%x' % v)
        for t in sorted(seen_targets):
            if in_ranges(t, code):
                continue
            text = decode_string(t)
            if text is not None:
                strings.append(text)
                continue
            if in_ranges(t, rdata) and 0 <= t - lo < len(image) - 8:
                v = struct.unpack_from('<I', image, t - lo)[0]
                if in_ranges(v, code):
                    drefs.append(t)
                else:
                    consts.append('%08x' % v)
                continue
            drefs.append(t)
    rec['n'] = ninstr
    rec['calls'] = callees
    if strings:
        rec['str'] = strings
    if drefs:
        rec['d'] = drefs
    if consts:
        rec['c'] = consts
    rec['t'] = '\n'.join(toks)
    funcs.append(rec)

# Vtables: slots from each start up to the next start or the first non-code pointer.
starts = sorted(vtable_starts(base, image, blocks))
start_set = set(starts)
vtables = []
st = program.getSymbolTable()
for s in starts:
    slots = []
    p = s
    while len(slots) < MAX_SLOTS and p - lo + 4 <= len(image):
        if p != s and p in start_set:
            break
        v = struct.unpack_from('<I', image, p - lo)[0]
        if not in_ranges(v, code):
            break
        slots.append(v)
        p += 4
    names = [str(x.getName(True)) for x in st.getSymbols(ctx.addr(s)) if str(x.getSource()) == 'USER_DEFINED']
    vt = {'a': s, 'slots': slots}
    if names:
        vt['names'] = names
    vtables.append(vt)

out = os.path.join(OUT, '%s.features.json' % ctx.id)
with open(out, 'w', encoding='utf-8', newline='\n') as fh:
    json.dump({'id': ctx.id, 'program': str(program.getName()), 'base': base,
               'blocks': blocks, 'functions': funcs, 'vtables': vtables}, fh)
println('features: %s %d functions, %d vtables -> %s' % (ctx.id, len(funcs), len(vtables), out))
