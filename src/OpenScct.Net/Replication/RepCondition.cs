using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// The replication condition interpreter (spec/net/model.md rules 5 and 9, spec/native/net-replication.md rules
/// 13, 19.5 and 21): evaluates a <see cref="RepExpr"/> for one object with the net inputs of one connection.
/// UnrealScript semantics: <c>&amp;&amp;</c> and <c>||</c> short-circuit, a member of None reads as None (zero),
/// enums and bytes compare as numbers, references by identity.
/// <para>
/// <see cref="Evaluate"/> walks the tree and looks every property up by name; <see cref="RepCondition"/> is the same
/// interpreter with the property lookups resolved once, which the replication pass uses.
/// </para>
/// </summary>
public static class RepExprEvaluator
{
    /// <summary>Evaluates <paramref name="expr"/> for <paramref name="self"/> with <paramref name="flags"/>.</summary>
    [UcNative("UActorChannel::ReplicateActor", Exe = 0x109e1300, Editor = 0x11142700)]
    public static bool Evaluate(RepExpr expr, UObject self, in RepFlags flags, PropertyReaders readers)
    {
        ArgumentNullException.ThrowIfNull(expr);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(readers);
        return Value(expr, self, flags, readers).AsBool;
    }

    /// <summary>The value of <paramref name="expr"/>, for tests and diagnostics.</summary>
    public static RepValue Value(RepExpr expr, UObject self, in RepFlags flags, PropertyReaders readers)
    {
        switch (expr.Kind)
        {
            case RepExprKind.Bool:
                return RepValue.Bool(expr.Value != 0);
            case RepExprKind.Int:
            case RepExprKind.Enum:
                return RepValue.Int(expr.Value);
            case RepExprKind.None:
                return RepValue.None;
            case RepExprKind.Property:
                return readers.Find(self.GetUcClass(), expr.Name!) is { } reader ? reader(self) : RepValue.None;
            case RepExprKind.Member:
            {
                RepValue target = Value(expr.Left!, self, flags, readers);
                if (target.O is not UObject obj)
                {
                    return RepValue.None;
                }

                return readers.Find(obj.GetUcClass(), expr.Name!) is { } member ? member(obj) : RepValue.None;
            }

            case RepExprKind.NetVar:
                return NetVar(expr.NetVar, flags);
            case RepExprKind.Not:
                return RepValue.Bool(!Value(expr.Left!, self, flags, readers).AsBool);
            case RepExprKind.And:
                return RepValue.Bool(Value(expr.Left!, self, flags, readers).AsBool && Value(expr.Right!, self, flags, readers).AsBool);
            case RepExprKind.Or:
                return RepValue.Bool(Value(expr.Left!, self, flags, readers).AsBool || Value(expr.Right!, self, flags, readers).AsBool);
            default:
                return RepValue.Bool(Compare(expr.Kind, Value(expr.Left!, self, flags, readers), Value(expr.Right!, self, flags, readers)));
        }
    }

    internal static RepValue NetVar(RepNetVar netVar, in RepFlags flags) => netVar switch
    {
        RepNetVar.Role => RepValue.Int((int)flags.Role),
        RepNetVar.RemoteRole => RepValue.Int((int)flags.RemoteRole),
        RepNetVar.bNetInitial => RepValue.Bool(flags.bNetInitial),
        RepNetVar.bNetOwner => RepValue.Bool(flags.bNetOwner),
        RepNetVar.bNetDirty => RepValue.Bool(flags.bNetDirty),
        RepNetVar.NetMode => RepValue.Int((int)flags.NetMode),
        _ => RepValue.None,
    };

    internal static bool Compare(RepExprKind kind, in RepValue a, in RepValue b)
    {
        switch (kind)
        {
            case RepExprKind.Equal:
                return RepValue.AreEqual(a, b);
            case RepExprKind.NotEqual:
                return !RepValue.AreEqual(a, b);
        }

        int? order = RepValue.Order(a, b);
        if (order is not int o)
        {
            return false;
        }

        return kind switch
        {
            RepExprKind.Less => o < 0,
            RepExprKind.LessEqual => o <= 0,
            RepExprKind.Greater => o > 0,
            RepExprKind.GreaterEqual => o >= 0,
            _ => false,
        };
    }
}

/// <summary>
/// A replication condition bound to a class: the same interpreter as <see cref="RepExprEvaluator"/> with property
/// lookups resolved once (members of other objects are resolved per class of the object met, and cached).
/// Evaluation does not allocate. Names that resolve to no property read as None and are listed in
/// <see cref="Unresolved"/>.
/// </summary>
public sealed class RepCondition
{
    private readonly Node _root;

    private RepCondition(RepExpr expr, Node root, string[] unresolved)
    {
        Expr = expr;
        _root = root;
        Unresolved = unresolved;
    }

    /// <summary>A condition that is always true (hand-written extension members).</summary>
    public static readonly RepCondition Always = new(RepExpr.True, new Const(RepValue.Bool(true)), []);

    /// <summary>A condition that is always false (members the native lists never send).</summary>
    public static readonly RepCondition Never = new(RepExpr.False, new Const(RepValue.Bool(false)), []);

    public RepExpr Expr { get; }

    /// <summary>Property names the class did not have.</summary>
    public IReadOnlyList<string> Unresolved { get; }

    public bool IsConstant => _root is Const;

    public bool Evaluate(UObject self, in RepFlags flags) => _root.Eval(self, flags).AsBool;

    /// <summary>Binds <paramref name="expr"/> to the properties of <paramref name="cls"/>.</summary>
    public static RepCondition Bind(RepExpr expr, UcClassInfo cls, PropertyReaders readers)
    {
        ArgumentNullException.ThrowIfNull(expr);
        ArgumentNullException.ThrowIfNull(cls);
        ArgumentNullException.ThrowIfNull(readers);
        var unresolved = new List<string>();
        Node root = Build(expr, cls, readers, unresolved);
        return new RepCondition(expr, root, unresolved.ToArray());
    }

    public override string ToString() => Expr.ToString();

    private static Node Build(RepExpr e, UcClassInfo cls, PropertyReaders readers, List<string> unresolved)
    {
        switch (e.Kind)
        {
            case RepExprKind.Bool:
                return new Const(RepValue.Bool(e.Value != 0));
            case RepExprKind.Int:
            case RepExprKind.Enum:
                return new Const(RepValue.Int(e.Value));
            case RepExprKind.None:
                return new Const(RepValue.None);
            case RepExprKind.Property:
                if (readers.Find(cls, e.Name!) is { } reader)
                {
                    return new Prop(reader);
                }

                unresolved.Add(e.Name!);
                return new Const(RepValue.None);
            case RepExprKind.Member:
                return new Member(Build(e.Left!, cls, readers, unresolved), e.Name!, readers);
            case RepExprKind.NetVar:
                return new NetVarNode(e.NetVar);
            case RepExprKind.Not:
                return new NotNode(Build(e.Left!, cls, readers, unresolved));
            case RepExprKind.And:
                return new AndNode(Build(e.Left!, cls, readers, unresolved), Build(e.Right!, cls, readers, unresolved));
            case RepExprKind.Or:
                return new OrNode(Build(e.Left!, cls, readers, unresolved), Build(e.Right!, cls, readers, unresolved));
            default:
                return new CompareNode(e.Kind, Build(e.Left!, cls, readers, unresolved), Build(e.Right!, cls, readers, unresolved));
        }
    }

    private abstract class Node
    {
        public abstract RepValue Eval(UObject self, in RepFlags flags);
    }

    private sealed class Const(RepValue value) : Node
    {
        public override RepValue Eval(UObject self, in RepFlags flags) => value;
    }

    private sealed class Prop(Func<UObject, RepValue> reader) : Node
    {
        public override RepValue Eval(UObject self, in RepFlags flags) => reader(self);
    }

    private sealed class Member(Node left, string name, PropertyReaders readers) : Node
    {
        // Readers per class of the object met, newest first; replaced whole so concurrent readers stay safe.
        private volatile MemberEntry? _entries;

        public override RepValue Eval(UObject self, in RepFlags flags)
        {
            if (left.Eval(self, flags).O is not UObject obj)
            {
                return RepValue.None;
            }

            UcClassInfo cls = obj.GetUcClass();
            MemberEntry? head = _entries;
            for (MemberEntry? e = head; e is not null; e = e.Next)
            {
                if (ReferenceEquals(e.Class, cls))
                {
                    return e.Reader is null ? RepValue.None : e.Reader(obj);
                }
            }

            Func<UObject, RepValue>? reader = readers.Find(cls, name);
            _entries = new MemberEntry(cls, reader, head);
            return reader is null ? RepValue.None : reader(obj);
        }
    }

    private sealed record MemberEntry(UcClassInfo Class, Func<UObject, RepValue>? Reader, MemberEntry? Next);

    private sealed class NetVarNode(RepNetVar netVar) : Node
    {
        public override RepValue Eval(UObject self, in RepFlags flags) => RepExprEvaluator.NetVar(netVar, flags);
    }

    private sealed class NotNode(Node operand) : Node
    {
        public override RepValue Eval(UObject self, in RepFlags flags) => RepValue.Bool(!operand.Eval(self, flags).AsBool);
    }

    private sealed class AndNode(Node left, Node right) : Node
    {
        public override RepValue Eval(UObject self, in RepFlags flags) =>
            RepValue.Bool(left.Eval(self, flags).AsBool && right.Eval(self, flags).AsBool);
    }

    private sealed class OrNode(Node left, Node right) : Node
    {
        public override RepValue Eval(UObject self, in RepFlags flags) =>
            RepValue.Bool(left.Eval(self, flags).AsBool || right.Eval(self, flags).AsBool);
    }

    private sealed class CompareNode(RepExprKind kind, Node left, Node right) : Node
    {
        public override RepValue Eval(UObject self, in RepFlags flags) =>
            RepValue.Bool(RepExprEvaluator.Compare(kind, left.Eval(self, flags), right.Eval(self, flags)));
    }
}
