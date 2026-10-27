"""Helpers shared by the re/ghidra/scripts post-scripts (run under PyGhidra).

Scripts call setup(globals()) first. It puts re/tools on sys.path, loads the binaries lock and
identifies the current program by its executable SHA-256, refusing any build that is not locked.
"""

import json
import os
import sys

LIB_DIR = os.path.dirname(os.path.abspath(__file__))
RE_DIR = os.path.dirname(os.path.dirname(LIB_DIR))
REPO_DIR = os.path.dirname(RE_DIR)
TOOLS_DIR = os.path.join(RE_DIR, 'tools')
ANNOTATIONS_DIR = os.path.join(RE_DIR, 'ghidra', 'annotations')
OUT_DIR = os.path.join(REPO_DIR, 'local', 're-out')
BASELINE_MAP = 'SVM.baselineComments'
AUTO_NAMES_MAP = 'SVM.scriptNames'
GUARD_NAMES_MAP = 'SVM.guardNames'
NATIVE_NAMES_MAP = 'SVM.nativeNames'

if TOOLS_DIR not in sys.path:
    sys.path.insert(0, TOOLS_DIR)

import binlock  # noqa: E402


class Context:
    def __init__(self, program, monitor, lock, target):
        self.program = program
        self.monitor = monitor
        self.lock = lock
        self.target = target          # lock entry of this program
        self.id = target['id']

    def out_path(self, suffix):
        os.makedirs(OUT_DIR, exist_ok=True)
        return os.path.join(OUT_DIR, '%s.%s' % (self.id, suffix))

    def write_report(self, suffix, data):
        path = self.out_path(suffix)
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(data, f, indent=1, sort_keys=True)
            f.write('\n')
        return path

    def addr(self, value):
        return self.program.getAddressFactory().getDefaultAddressSpace().getAddress(value)


def setup(g, expect=None):
    """Identify the current program against the lock. expect: allowed lock ids, or None for any."""
    program = g['currentProgram']
    lock = binlock.load_lock()
    sha = str(program.getExecutableSHA256() or '')
    target = binlock.find_by_sha256(lock, sha)
    if target is None:
        raise binlock.BuildMismatch('program %s has sha256 %s, which is not in re/binaries.lock.json; refusing to run'
                                    % (program.getName(), sha))
    base = program.getImageBase().getOffset()
    locked_base = int(target.get('pe', {}).get('imageBase', '0'), 16)
    if locked_base and base != locked_base:
        raise binlock.BuildMismatch('%s is loaded at %#x but the lock says %#x' % (target['id'], base, locked_base))
    if expect is not None and target['id'] not in expect:
        return None
    return Context(program, g['monitor'], lock, target)


INPUTS_FILE = os.path.join(REPO_DIR, 'local', 're-inputs.json')


def input_path(key, env, args=None):
    """Path of an external input: the first script argument, else the env variable, else the
    'key' entry of local/re-inputs.json (a machine-specific, untracked file). None when unset."""
    if args:
        return args[0]
    if os.environ.get(env):
        return os.environ[env]
    if os.path.isfile(INPUTS_FILE):
        with open(INPUTS_FILE, encoding='utf-8') as f:
            value = json.load(f).get(key)
        if isinstance(value, list):
            value = next((v for v in value if os.path.isfile(v)), None)
        return value
    return None


def hexaddr(value):
    return '0x%08x' % value


# Memory --------------------------------------------------------------------------------------

def block_bytes(program, block):
    """All bytes of an initialized memory block as Python bytes."""
    import jpype
    size = int(block.getSize())
    arr = jpype.JArray(jpype.JByte)(size)
    block.getBytes(block.getStart(), arr)
    return bytes(memoryview(arr).cast('B'))


def loaded_image(program):
    """(base offset, bytes) covering every initialized block, zero-filled between blocks."""
    blocks = [b for b in program.getMemory().getBlocks() if b.isInitialized() and b.isLoaded()]
    lo = min(b.getStart().getOffset() for b in blocks)
    hi = max(b.getEnd().getOffset() + 1 for b in blocks)
    buf = bytearray(hi - lo)
    for b in blocks:
        data = block_bytes(program, b)
        start = b.getStart().getOffset() - lo
        buf[start:start + len(data)] = data
    return lo, bytes(buf), [(b.getName(), b.getStart().getOffset(), b.getEnd().getOffset() + 1,
                             bool(b.isExecute())) for b in blocks]


# Symbols -------------------------------------------------------------------------------------

def user_source():
    from ghidra.program.model.symbol import SourceType
    return SourceType.USER_DEFINED


def ensure_namespace(program, path, as_class=True):
    """Namespace for 'A::B' (each part a class namespace when as_class). Empty path is global."""
    from ghidra.app.util import NamespaceUtils
    from ghidra.program.model.symbol import SourceType
    st = program.getSymbolTable()
    ns = program.getGlobalNamespace()
    if not path:
        return ns
    for part in path.split('::'):
        existing = st.getNamespace(part, ns)
        if existing is None:
            if as_class:
                existing = st.createClass(ns, part, SourceType.USER_DEFINED)
            else:
                existing = st.createNameSpace(ns, part, SourceType.USER_DEFINED)
        elif as_class and existing.getSymbol().getSymbolType().toString() == 'Namespace':
            existing = NamespaceUtils.convertNamespaceToClass(existing)
        ns = existing
    return ns


def namespace_path(ns):
    parts = []
    while ns is not None and not ns.isGlobal():
        parts.append(ns.getName())
        ns = ns.getParentNamespace()
    return '::'.join(reversed(parts))


def ensure_function(program, monitor, address):
    """Function starting at address, disassembling and creating it if needed.

    Returns (function or None, how) where how is 'existing', 'created', or a failure reason.
    """
    from ghidra.app.cmd.disassemble import DisassembleCommand
    from ghidra.app.cmd.function import CreateFunctionCmd
    from ghidra.program.model.address import AddressSet
    listing = program.getListing()
    fm = program.getFunctionManager()
    f = fm.getFunctionAt(address)
    if f is not None:
        return f, 'existing'
    if listing.getInstructionAt(address) is None:
        if listing.getDefinedDataContaining(address) is not None:
            return None, 'data'
        cmd = DisassembleCommand(address, None, True)
        cmd.applyTo(program, monitor)
        if listing.getInstructionAt(address) is None:
            return None, 'not-disassemblable'
    cmd = CreateFunctionCmd(address)
    if cmd.applyTo(program, monitor):
        f = fm.getFunctionAt(address)
        if f is not None:
            return f, 'created'
    # Inside another function's body: give the new function its own body and shrink the other.
    container = fm.getFunctionContaining(address)
    if container is not None:
        body = container.getBody()
        cut = AddressSet(address, body.getMaxAddress())
        try:
            container.setBody(body.subtract(cut))
            if CreateFunctionCmd(address).applyTo(program, monitor):
                f = fm.getFunctionAt(address)
                if f is not None:
                    CreateFunctionCmd.fixupFunctionBody(program, container, monitor)
                    return f, 'split'
        except Exception as exc:  # noqa: BLE001
            return None, 'split-failed: %s' % exc
    return None, 'create-failed'


def _auto_names(program):
    """Property map of the full names our scripts gave functions, keyed by entry point."""
    pm = program.getUsrPropertyManager()
    m = pm.getStringPropertyMap(AUTO_NAMES_MAP)
    return m if m is not None else pm.createStringPropertyMap(AUTO_NAMES_MAP)


def set_function_name(function, name, namespace, overwrite_user=False):
    """Name a function. Returns 'same', 'set' or 'kept-user'.

    A user-defined name is replaced only when one of our scripts set it (so re-runs can correct
    their own names) or when overwrite_user is given; a name someone typed in is kept.
    """
    from ghidra.program.model.symbol import SourceType
    program = function.getProgram()
    auto = _auto_names(program)
    entry = function.getEntryPoint()
    sym = function.getSymbol()
    if sym.getName() == name and function.getParentNamespace() == namespace:
        if sym.getSource() != SourceType.USER_DEFINED:
            function.setName(name, SourceType.USER_DEFINED)
        if auto.getString(entry) is None:
            auto.add(entry, function.getName(True))
        return 'same'
    if sym.getSource() == SourceType.USER_DEFINED and not overwrite_user:
        if auto.getString(entry) != function.getName(True):
            return 'kept-user'
    function.setParentNamespace(namespace)
    function.setName(name, SourceType.USER_DEFINED)
    auto.add(entry, function.getName(True))
    return 'set'


def ensure_label(program, address, name, namespace, primary=False):
    """Add a user label at address in namespace (no-op if it already exists)."""
    from ghidra.program.model.symbol import SourceType
    st = program.getSymbolTable()
    for s in st.getSymbols(address):
        if s.getName() == name and s.getParentNamespace() == namespace:
            if primary and not s.isPrimary():
                s.setPrimary()
            return s, 'same'
    s = st.createLabel(address, name, namespace, SourceType.USER_DEFINED)
    if primary:
        s.setPrimary()
    return s, 'set'


# Comments ------------------------------------------------------------------------------------

def comment_types():
    """[(key, CommentType)] for Ghidra 11.4+ (CommentType enum)."""
    from ghidra.program.model.listing import CommentType
    return [('eol', CommentType.EOL), ('pre', CommentType.PRE), ('post', CommentType.POST),
            ('plate', CommentType.PLATE), ('repeatable', CommentType.REPEATABLE)]


def comment_type(key):
    return dict(comment_types())[key]


def text_digest(text):
    import hashlib
    return hashlib.sha1(text.encode('utf-8')).hexdigest()[:16]


def iter_comments(program):
    """Yield (address, key, text) for every comment in the program."""
    listing = program.getListing()
    mem = program.getMemory()
    it = listing.getCommentAddressIterator(mem, True)
    types = comment_types()
    while it.hasNext():
        a = it.next()
        for key, ct in types:
            text = listing.getComment(ct, a)
            if text:
                yield a, key, str(text)


def set_comment(program, address, key, text):
    program.getListing().setComment(address, comment_type(key), text)
