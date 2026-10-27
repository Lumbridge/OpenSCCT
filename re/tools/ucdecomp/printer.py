"""UnrealScript text for token trees, structured statements and declarations."""

from __future__ import annotations

import struct
from typing import List, Optional, Tuple

from . import tokens as T
from .bytecode import Expr, f32_bits
from .loader import (Class, Const, Enum, Field, Function, Property, State, Struct,
                     CPF_Parm, CPF_ReturnParm, FUNC_Final, FUNC_Latent, FUNC_Operator, FUNC_PreOperator)
from .structure import (S, SBreak, SContinue, SDo, SExpr, SFor, SForEach, SGoto, SIf, SLabel,
                        SReturn, SSwitch, SWhile)
from .universe import Universe

ATOM = 0
UNARY = 1
IND = "    "

# Equality/inequality precedence for struct compares (as declared for the built-in operators).
PREC_EQ = 24
PREC_NE = 26


def fmt_float(v: float) -> str:
    """Shortest decimal text that reads back as the same 32-bit float."""
    if v != v or v in (float("inf"), float("-inf")):
        return "0.0 /* %r */" % v
    bits = struct.pack("<f", v)
    s = "%.9g" % v
    for n in range(1, 10):
        cand = "%.*g" % (n, v)
        if struct.pack("<f", float(cand)) == bits:
            s = cand
            break
    if "e" in s:
        fixed = ("%.12f" % v).rstrip("0")
        if struct.pack("<f", float(fixed)) == bits and len(fixed) <= 24:
            s = fixed
    if "." not in s and "e" not in s:
        s += ".0"
    if s.endswith("."):
        s += "0"
    return s


def quote_string(b: bytes) -> str:
    s = b.decode("latin1")
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


class Printer:
    def __init__(self, u: Universe, lp):
        self.u = u
        self.lp = lp
        self.func: Optional[Struct] = None       # function or state being printed
        self.cls: Optional[Class] = None
        self.latent_calls: List[str] = []

    # names --------------------------------------------------------------------------------------
    def nm(self, n) -> str:
        return self.lp.name_of(n[0])

    def oname(self, ref: int) -> str:
        return self.lp.obj_name(ref)

    def field(self, ref: int) -> Optional[Field]:
        return self.u.field(self.lp, ref)

    # types --------------------------------------------------------------------------------------
    def type_name(self, p: Property) -> str:
        k = p.kind
        lp = p.pkg
        if k == "ByteProperty":
            return lp.obj_name(p.ref1) if p.ref1 else "byte"
        if k == "IntProperty":
            return "int"
        if k == "BoolProperty":
            return "bool"
        if k == "FloatProperty":
            return "float"
        if k == "NameProperty":
            return "name"
        if k in ("StrProperty", "StringProperty"):
            return "string"
        if k == "ObjectProperty":
            return lp.obj_name(p.ref1)
        if k == "ClassProperty":
            meta = lp.obj_name(p.ref2)
            return "class" if meta == "Object" else "class<%s>" % meta
        if k == "StructProperty":
            return lp.obj_name(p.ref1)
        if k == "ArrayProperty":
            inner = lp.fields.get(p.ref1)
            t = self.type_name(inner) if isinstance(inner, Property) else "unknown"
            return "array<%s>" % (t + " " if t.endswith(">") else t)
        if k == "DelegateProperty":
            return "delegate<%s>" % lp.obj_name(p.ref1)
        if k == "PointerProperty":
            return "pointer"
        return k.replace("Property", "").lower()

    def type_desc(self, p: Optional[Field]):
        """Light type descriptor for inference: (kind, detail)."""
        if not isinstance(p, Property):
            return ("unknown", None)
        k = p.kind
        if k == "ByteProperty":
            en = self.u.field(p.pkg, p.ref1) if p.ref1 else None
            return ("enum", en) if isinstance(en, Enum) else ("byte", None)
        if k == "ObjectProperty":
            return ("object", self.u.field(p.pkg, p.ref1))
        if k == "ClassProperty":
            return ("class", self.u.field(p.pkg, p.ref2))
        if k == "StructProperty":
            return ("struct", self.u.field(p.pkg, p.ref1))
        if k == "ArrayProperty":
            return ("array", p.pkg.fields.get(p.ref1))
        return (k.replace("Property", "").lower(), None)

    def return_prop(self, f: Function) -> Optional[Property]:
        for m in self.u.members(f):
            if isinstance(m, Property) and m.flags & CPF_ReturnParm:
                return m
        return None

    def params_of(self, f: Function) -> List[Property]:
        return [m for m in self.u.members(f)
                if isinstance(m, Property) and m.flags & CPF_Parm and not m.flags & CPF_ReturnParm]

    def callee(self, e: Expr, ctx_obj: Optional[Expr] = None) -> Optional[Function]:
        if e.native_index is not None:
            return self.u.natives.get(e.native_index)
        if e.tok == T.EX_FinalFunction:
            f = self.field(e.vals[0])
            return f if isinstance(f, Function) else None
        if e.tok in (T.EX_VirtualFunction, T.EX_GlobalFunction):
            scope = None
            if ctx_obj is not None:
                d = self.expr_type(ctx_obj)
                if d[0] in ("object",) and isinstance(d[1], Struct):
                    scope = d[1]
                elif d[0] == "class" and isinstance(d[1], Struct):
                    scope = d[1]
            else:
                scope = self.func if (self.func is not None and e.tok == T.EX_VirtualFunction) else self.cls
            if scope is None:
                return None
            f = self.u.find_member(scope, self.nm(e.vals[0]), ("Function",))
            if f is None and scope is not self.cls and self.cls is not None and ctx_obj is None:
                f = self.u.find_member(self.cls, self.nm(e.vals[0]), ("Function",))
            return f if isinstance(f, Function) else None
        return None

    def expr_type(self, e: Expr):
        t = e.tok
        if t in (T.EX_LocalVariable, T.EX_InstanceVariable, T.EX_DefaultVariable):
            return self.type_desc(self.field(e.vals[0]))
        if t == T.EX_BoolVariable:
            return ("bool", None)
        if t in (T.EX_Context, T.EX_ClassContext):
            inner = e.vals[3]
            if inner.tok in (T.EX_VirtualFunction,):
                f = self.callee(inner, e.vals[0])
                return self.type_desc(self.return_prop(f)) if f else ("unknown", None)
            return self.expr_type(inner)
        if t == T.EX_StructMember:
            return self.type_desc(self.field(e.vals[0]))
        if t in (T.EX_ArrayElement, T.EX_DynArrayElement):
            d = self.expr_type(e.vals[1])
            if d[0] == "array":
                return self.type_desc(d[1])
            return d
        if t == T.EX_DynamicCast:
            return ("object", self.field(e.vals[0]))
        if t == T.EX_MetaCast:
            return ("class", self.field(e.vals[0]))
        if t == T.EX_Self:
            return ("object", self.cls)
        if t == T.EX_ByteConst:
            return ("byte", None)
        if t in (T.EX_VirtualFunction, T.EX_FinalFunction, T.EX_GlobalFunction) or e.native_index is not None:
            f = self.callee(e)
            return self.type_desc(self.return_prop(f)) if f else ("unknown", None)
        return ("unknown", None)

    # expressions --------------------------------------------------------------------------------
    def enum_text(self, e: Expr, want) -> Optional[str]:
        """A byte constant shown as an enum value when the expected type is an enum."""
        if want is None or want[0] != "enum" or not isinstance(want[1], Enum):
            return None
        if e.tok == T.EX_PrimitiveCast and e.vals[0] == 0x3A:
            e = e.vals[1]
        if e.tok in (T.EX_Context, T.EX_ClassContext):
            return None     # printed by ex() as Obj.EnumValue
        if e.tok == T.EX_ByteConst and 0 <= e.vals[0] < len(want[1].values):
            return want[1].values[e.vals[0]]
        return None

    def scope_constant(self, obj: Expr, inner: Expr) -> Optional[str]:
        """Name for a constant reached through an object (Obj.NAME compiles to a context around the
        literal): a const of that value in the object's class chain, else an enum value."""
        d = self.expr_type(obj)
        if d[0] not in ("object", "class") or not isinstance(d[1], Struct):
            return None
        value = inner.vals[0]
        consts, enums = [], []
        cur = d[1]
        guard = 0
        while cur is not None and guard < 64:
            guard += 1
            for m in self.u.members(cur):
                if isinstance(m, Const) and _const_int(m.value) == value:
                    consts.append(m.name)
                elif isinstance(m, Enum) and inner.tok == T.EX_ByteConst and value < len(m.values):
                    enums.append(m)
            cur = self.u.super_of(cur)
        if len(consts) == 1:
            return consts[0]
        if consts:
            return None
        if len(enums) == 1:
            return enums[0].values[value]
        if not enums and inner.tok == T.EX_ByteConst:
            # enum value names are global to the compiler: the only enum anywhere that is large enough
            big = self.u.enums_larger_than(value)
            if len(big) == 1:
                return big[0].values[value]
        return None

    def x(self, e: Expr, want=None) -> str:
        return self.ex(e, want)[0]

    def ex(self, e: Expr, want=None) -> Tuple[str, int]:
        en = self.enum_text(e, want)
        if en is not None:
            return en, ATOM
        t = e.tok
        v = e.vals
        if t in (T.EX_LocalVariable, T.EX_InstanceVariable, T.EX_NativeParm):
            return var_name(self.oname(v[0])), ATOM
        if t == T.EX_DefaultVariable:
            return "default." + var_name(self.oname(v[0])), ATOM
        if t == T.EX_Return:
            return ("return" if v[0].tok == T.EX_Nothing else
                    "return " + self.x(v[0], implicit(self.type_desc(self.return_prop(self.func)))
                                       if isinstance(self.func, Function) else None)), ATOM
        if t == T.EX_Stop:
            return "stop", ATOM
        if t == T.EX_Assert:
            return "assert(%s)" % self.x(v[1]), ATOM
        if t == T.EX_Nothing:
            return "", ATOM
        if t == T.EX_GotoLabel:
            return "goto " + self.x(v[0]), ATOM
        if t == T.EX_EatString:
            return self.ex(v[0])
        if t in (T.EX_Let, T.EX_LetBool, T.EX_LetDelegate):
            d = self.expr_type(v[0])
            return "%s = %s" % (self.x(v[0]), self.x(v[1], implicit(d))), 40
        if t in (T.EX_ArrayElement, T.EX_DynArrayElement):
            return "%s[%s]" % (self.wrap(v[1], ATOM), self.x(v[0], ("int", None, "implicit"))), ATOM
        if t == T.EX_New:
            args = [self.x(a) for a in v[:3]]
            while args and not args[-1]:
                args.pop()
            head = "new" + ("(%s)" % ", ".join(args) if args else "")
            return "%s %s" % (head, self.wrap(v[3], ATOM)), UNARY
        if t in (T.EX_Context, T.EX_ClassContext):
            obj = self.wrap(v[0], ATOM)
            inner = v[3]
            if inner.tok in (T.EX_ByteConst, T.EX_IntConstByte, T.EX_IntConst):
                # an enum value named through an object (Obj.EnumValue compiles to a context constant)
                en = self.enum_text(inner, want) if inner.tok == T.EX_ByteConst else None
                if en is None:
                    en = self.scope_constant(v[0], inner)
                return "%s.%s" % (obj, en if en is not None else "%s /* constant */" % self.x(inner)), ATOM
            itxt = self.call_text(inner, v[0]) if inner.tok == T.EX_VirtualFunction else self.x(inner)
            if t == T.EX_ClassContext and inner.tok != T.EX_DefaultVariable:
                return "%s.static.%s" % (obj, itxt), ATOM
            return "%s.%s" % (obj, itxt), ATOM
        if t == T.EX_MetaCast:
            return "class<%s>(%s)" % (self.oname(v[0]), self.x(v[1])), ATOM
        if t == T.EX_DynamicCast:
            return "%s(%s)" % (self.oname(v[0]), self.x(v[1])), ATOM
        if t == T.EX_Self:
            return "self", ATOM
        if t == T.EX_Skip:
            return self.ex(v[1], want)
        if t in (T.EX_VirtualFunction, T.EX_FinalFunction, T.EX_GlobalFunction, T.EX_DelegateFunction):
            return self.call_text(e), ATOM
        if t == T.EX_IntConst:
            return str(v[0]), ATOM
        if t == T.EX_FloatConst:
            return fmt_float(f32_bits(v[0])), ATOM
        if t == T.EX_StringConst:
            return quote_string(v[0]), ATOM
        if t == T.EX_UnicodeStringConst:
            s = "".join(chr(c) for c in v[0])
            return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"', ATOM
        if t == T.EX_ObjectConst:
            return self.object_literal(v[0]), ATOM
        if t == T.EX_NameConst:
            return "'%s'" % self.nm(v[0]), ATOM
        if t == T.EX_RotationConst:
            return "rot(%d,%d,%d)" % tuple(v), ATOM
        if t == T.EX_VectorConst:
            return "vect(%s,%s,%s)" % tuple(fmt_float(f32_bits(a)) for a in v), ATOM
        if t in (T.EX_ByteConst, T.EX_IntConstByte):
            return str(v[0]), ATOM
        if t == T.EX_IntZero:
            return "0", ATOM
        if t == T.EX_IntOne:
            return "1", ATOM
        if t == T.EX_True:
            return "true", ATOM
        if t == T.EX_False:
            return "false", ATOM
        if t == T.EX_NoObject:
            return "None", ATOM
        if t == T.EX_BoolVariable:
            return self.ex(v[0])
        if t == T.EX_Iterator:
            return "foreach " + self.x(v[0]), ATOM
        if t in (T.EX_StructCmpEq, T.EX_StructCmpNe):
            p = PREC_EQ if t == T.EX_StructCmpEq else PREC_NE
            op = "==" if t == T.EX_StructCmpEq else "!="
            return "%s %s %s" % (self.wrap(v[1], p), op, self.wrap(v[2], p - 1)), p
        if t == T.EX_StructMember:
            return "%s.%s" % (self.wrap(v[1], ATOM), self.oname(v[0])), ATOM
        if t == T.EX_DynArrayLength:
            return "%s.Length" % self.wrap(v[0], ATOM), ATOM
        if t in (T.EX_DynArrayInsert, T.EX_DynArrayRemove):
            return "%s.%s(%s, %s)" % (self.wrap(v[0], ATOM), "Insert" if t == T.EX_DynArrayInsert else "Remove",
                                      self.x(v[1]), self.x(v[2])), ATOM
        if t == T.EX_PrimitiveCast:
            name, ty = T.CASTS.get(v[0], ("Cast%02X" % v[0], "cast%02X" % v[0]))
            if want is not None and len(want) > 2 and v[0] in WIDENING and WIDENING[v[0]] == want[0]:
                # a widening conversion the compiler inserts by itself for this destination type
                return self.ex(v[1])
            return "%s(%s)" % (ty, self.x(v[1])), ATOM
        if t == T.EX_DelegateProperty:
            return self.nm(v[0]), ATOM
        if t == T.EX_IteratorNext:
            return "/* IteratorNext */", ATOM
        if t == T.EX_IteratorPop:
            return "/* IteratorPop */", ATOM
        if t == T.EX_LabelTable:
            return "/* labels */", ATOM
        if t == T.EX_Jump:
            return "goto J0x%04X" % v[0], ATOM
        if t == T.EX_JumpIfNot:
            return "if (!(%s)) goto J0x%04X" % (self.x(v[1]), v[0]), ATOM
        if t == T.EX_Switch:
            return "switch (%s)" % self.x(v[1]), ATOM
        if t == T.EX_Case:
            return ("default:" if v[0][0] == 0xFFFF else "case %s:" % self.x(v[0][1])), ATOM
        if t == T.EX_EndParmValue:
            return "", ATOM
        if e.native_index is not None:
            return self.native_text(e, want)
        return "/* %s */" % T.mnemonic(t), ATOM

    def wrap(self, e: Expr, max_prec: int, want=None) -> str:
        s, p = self.ex(e, want)
        return "(%s)" % s if p > max_prec else s

    def args_text(self, args: List[Expr], f: Optional[Function]) -> str:
        params = self.params_of(f) if f else []
        out = []
        for n, a in enumerate(args):
            want = implicit(self.type_desc(params[n])) if n < len(params) else None
            out.append(self.x(a, want))
        while out and out[-1] == "" and args[len(out) - 1].tok == T.EX_Nothing:
            out.pop()
        return ", ".join(out)

    def call_text(self, e: Expr, ctx_obj: Optional[Expr] = None) -> str:
        t = e.tok
        v = e.vals
        if t == T.EX_VirtualFunction:
            f = self.callee(e, ctx_obj)
            self._note_latent(f)
            return "%s(%s)" % (self.nm(v[0]), self.args_text(v[1], f))
        if t == T.EX_GlobalFunction:
            f = self.callee(e)
            return "Global.%s(%s)" % (self.nm(v[0]), self.args_text(v[1], f))
        if t == T.EX_DelegateFunction:
            return "%s(%s)" % (self.nm(v[1]), self.args_text(v[2], None))
        if t == T.EX_FinalFunction:
            f = self.field(v[0])
            name = self.oname(v[0])
            if isinstance(f, Function):
                self._note_latent(f)
                prefix = self.super_prefix(f)
                return "%s%s(%s)" % (prefix, name, self.args_text(v[1], f))
            return "%s(%s)" % (name, self.args_text(v[1], None))
        return self.x(e)

    def _note_latent(self, f: Optional[Function]):
        if f is not None and f.func_flags & FUNC_Latent:
            self.latent_calls.append(f.name)

    def super_prefix(self, f: Function) -> str:
        """FinalFunction calls a non-final function only for Super / Super(Class) calls."""
        if f.func_flags & FUNC_Final or self.func is None:
            return ""
        owner = self._owner_struct(self.func)
        if owner is None or self._owner_struct(f) is owner:
            return ""
        # what plain Super.Name would bind to: the super state chain, then the class's super chain
        chain = []
        if isinstance(owner, State) and not isinstance(owner, Class):
            st = self.u.super_of(owner)
            guard = 0
            while isinstance(st, State) and guard < 32:
                chain.append(st)
                st = self.u.super_of(st)
                guard += 1
            oc = self.u.owner_class(owner)
            start = self.u.super_of(oc) if oc else None
        else:
            start = self.u.super_of(owner)
        while start is not None and len(chain) < 64:
            chain.append(start)
            start = self.u.super_of(start)
        for s in chain:
            for m in self.u.members(s):
                if isinstance(m, Function) and m.name.lower() == f.name.lower():
                    if m is f:
                        return "super."
                    oc = self.u.owner_class(f)
                    return "super(%s)." % (oc.name if oc else "?")
        oc = self.u.owner_class(f)
        return "super(%s)." % (oc.name if oc else "?")

    def _owner_struct(self, f: Field) -> Optional[Struct]:
        if isinstance(f, (State, Class)) and not isinstance(f, Function):
            return f
        o = f.pkg.fields.get(f.outer)
        return o if isinstance(o, Struct) else None

    def native_text(self, e: Expr, want=None) -> Tuple[str, int]:
        f = self.u.natives.get(e.native_index)
        args = e.params
        if f is None:
            return "Native%d(%s)" % (e.native_index, ", ".join(self.x(a) for a in args)), ATOM
        self._note_latent(f)
        params = self.params_of(f)
        if f.func_flags & FUNC_Operator:
            op = f.friendly_name
            spaced = op[:1].isalpha()
            if f.func_flags & FUNC_PreOperator and len(args) == 1:
                s = self.wrap(args[0], UNARY)
                if not spaced and s[:1] in "+-!~" and op[-1:] in "+-!~":
                    s = "(%s)" % s          # keep '- -x' from reading as '--x'
                return ("%s %s" % (op, s) if spaced else op + s), UNARY
            if len(args) == 1:
                return self.wrap(args[0], UNARY) + (" " + op if spaced else op), UNARY
            if len(args) == 2:
                p = f.oper_prec or 20
                wl = wr = None
                a0, a1 = args
                # enum comparisons: show the constant side as an enum value
                da = self.expr_type(_uncast(a0))
                db = self.expr_type(_uncast(a1))
                if da[0] == "enum" and _is_byte_const(a1):
                    wr = da
                    a0 = _uncast(a0)
                    a1 = _uncast(a1)
                elif db[0] == "enum" and _is_byte_const(a0):
                    wl = db
                    a1 = _uncast(a1)
                    a0 = _uncast(a0)
                if wl is None and wr is None and params:
                    wl = self.type_desc(params[0])
                    wr = self.type_desc(params[1]) if len(params) > 1 else None
                return "%s %s %s" % (self.wrap(a0, p, wl), op, self.wrap(a1, p - 1, wr)), p
        return "%s(%s)" % (f.name, self.args_text(args, f)), ATOM

    def object_literal(self, ref: int) -> str:
        if ref == 0:
            return "None"
        cls = self.lp.class_of_ref(ref) or "Object"
        path = self.lp.path_of(ref)
        if cls == "Class":
            return "class'%s'" % path
        return "%s'%s'" % (cls, path)

    # statements ---------------------------------------------------------------------------------
    def stmt_text(self, e: Expr) -> str:
        if e.tok == T.EX_Return and isinstance(self.func, Function):
            return self.x(e)
        return self.x(e)

    def block(self, tree: List[S], depth: int, out: List[str], state_code: bool = False):
        for k, s in enumerate(tree):
            self.one(s, depth, out, state_code)

    def one(self, s: S, depth: int, out: List[str], state_code: bool):
        ind = IND * depth
        if isinstance(s, SLabel):
            out.append((IND * max(depth - 1, 0)) + s.name + ":")
        elif isinstance(s, SExpr):
            e = s.expr
            if e.tok in (T.EX_Nothing, T.EX_LabelTable, T.EX_EndParmValue):
                return
            mark = len(self.latent_calls)
            txt = self.stmt_text(e)
            if txt:
                tail = "  // latent" if state_code and len(self.latent_calls) > mark else ""
                out.append(ind + txt + ";" + tail)
        elif isinstance(s, SReturn):
            out.append(ind + self.x(s.ret) + ";")
        elif isinstance(s, SBreak):
            out.append(ind + "break;")
        elif isinstance(s, SContinue):
            out.append(ind + "continue;")
        elif isinstance(s, SGoto):
            if s.jump.tok == T.EX_JumpIfNot:
                out.append(ind + "if (!(%s)) goto %s;" % (self.x(s.jump.vals[1]), s.label))
            else:
                out.append(ind + "goto %s;" % s.label)
        elif isinstance(s, SIf):
            self.if_chain(s, depth, out, state_code, "if")
        elif isinstance(s, SWhile):
            out.append(ind + "while (%s)" % self.x(s.head.vals[1]))
            self.braced(s.body, depth, out, state_code)
        elif isinstance(s, SFor):
            init = self.x(s.init.expr) if isinstance(s.init, SExpr) else ""
            out.append(ind + "for (%s; %s; %s)" % (init, self.x(s.head.vals[1]), self.x(s.inc)))
            self.braced(s.body, depth, out, state_code)
        elif isinstance(s, SDo):
            out.append(ind + "do")
            self.braced(s.body, depth, out, state_code)
            out.append(ind + "until (%s);" % self.x(s.tail.vals[1]))
        elif isinstance(s, SSwitch):
            sw = s.head.vals[1]
            d = self.expr_type(_uncast(sw))
            out.append(ind + "switch (%s)" % self.x(sw))
            out.append(ind + "{")
            for case, body in s.cases:
                if case.vals[0][0] == 0xFFFF:
                    out.append(ind + IND + "default:")
                else:
                    out.append(ind + IND + "case %s:" % self.x(case.vals[0][1], d))
                self.block(body, depth + 2, out, state_code)
            out.append(ind + "}")
        elif isinstance(s, SForEach):
            out.append(ind + "foreach %s" % self.x(s.head.vals[0]))
            self.braced(s.body, depth, out, state_code)
        else:  # pragma: no cover
            out.append(ind + "/* %s */" % type(s).__name__)

    def braced(self, body: List[S], depth: int, out: List[str], state_code: bool):
        ind = IND * depth
        out.append(ind + "{")
        self.block(body, depth + 1, out, state_code)
        out.append(ind + "}")

    def if_chain(self, s: SIf, depth: int, out: List[str], state_code: bool, kw: str):
        ind = IND * depth
        out.append(ind + "%s (%s)" % (kw, self.x(s.head.vals[1])))
        self.braced(s.then, depth, out, state_code)
        if s.else_jump is not None:
            orelse = s.orelse or []
            if len(orelse) == 1 and isinstance(orelse[0], SIf):
                # else if
                inner: List[str] = []
                self.if_chain(orelse[0], depth, inner, state_code, "else if")
                out.extend(inner)
            else:
                out.append(ind + "else")
                self.braced(orelse, depth, out, state_code)


# Conversions the compiler applies by itself when the destination type is known (assignment, argument,
# return value, array index): cast code -> destination kind. Narrowing and string conversions stay
# explicit, and so does every operator operand (the cast there can pick the operator).
WIDENING = {0x3A: "int", 0x3C: "float", 0x3F: "float"}


def implicit(want):
    if want is None:
        return None
    return (want[0], want[1], "implicit")


def var_name(name: str) -> str:
    """A delegate's hidden property __Name__Delegate is written as Name in source."""
    if name.startswith("__") and name.endswith("__Delegate") and len(name) > 12:
        return name[2:-10]
    return name


def _const_int(text: str) -> Optional[int]:
    t = text.strip()
    try:
        return int(t, 16) if t.lower().startswith("0x") else int(t)
    except ValueError:
        return None


def _is_byte_const(e: Expr) -> bool:
    e = _uncast(e)
    if e.tok in (T.EX_Context, T.EX_ClassContext):
        e = e.vals[3]
    return e.tok == T.EX_ByteConst


def _uncast(e: Expr) -> Expr:
    if e.tok == T.EX_PrimitiveCast and e.vals[0] == 0x3A:
        return e.vals[1]
    return e
