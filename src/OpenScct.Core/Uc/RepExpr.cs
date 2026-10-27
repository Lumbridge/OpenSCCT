using System;
using System.Globalization;
using System.Text;

namespace OpenScct.Core.Uc;

/// <summary>Node kinds of a replication condition.</summary>
public enum RepExprKind : byte
{
    /// <summary>A bool literal (<see cref="RepExpr.Value"/> is 0 or 1).</summary>
    Bool,

    /// <summary>An int literal.</summary>
    Int,

    /// <summary>An enum literal: <see cref="RepExpr.EnumType"/>, <see cref="RepExpr.Name"/> and its <see cref="RepExpr.Value"/>.</summary>
    Enum,

    /// <summary>The None object reference.</summary>
    None,

    /// <summary>A property of the actor being replicated, by <see cref="RepExpr.Name"/>.</summary>
    Property,

    /// <summary>A property of the object <see cref="RepExpr.Left"/> refers to (for example <c>Base.bWorldGeometry</c>).</summary>
    Member,

    /// <summary>A replication input the net driver supplies per connection, see <see cref="RepNetVar"/>.</summary>
    NetVar,
    Not,
    And,
    Or,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
}

/// <summary>
/// The values a replication condition reads from the net driver rather than from plain properties: the roles as
/// the replicating side sees them, the per-connection flags and the level's net mode.
/// </summary>
public enum RepNetVar : byte
{
    Role,
    RemoteRole,
    bNetInitial,
    bNetOwner,
    bNetDirty,

    /// <summary><c>Level.NetMode</c>.</summary>
    NetMode,
}

/// <summary>
/// A replication condition as data: an immutable expression tree of property references, net inputs, literals
/// and boolean and comparison operators. The generator builds these from the replication blocks; an interpreter
/// (OpenScct.Net) evaluates them. <c>And</c> and <c>Or</c> short-circuit, as in UnrealScript.
/// </summary>
public sealed class RepExpr
{
    private RepExpr(RepExprKind kind, RepExpr? left = null, RepExpr? right = null, string? name = null,
        string? enumType = null, int value = 0, RepNetVar netVar = default)
    {
        Kind = kind;
        Left = left;
        Right = right;
        Name = name;
        EnumType = enumType;
        Value = value;
        NetVar = netVar;
    }

    public RepExprKind Kind { get; }

    /// <summary>Operand of Not, left operand of binary nodes, target object of Member.</summary>
    public RepExpr? Left { get; }

    /// <summary>Right operand of binary nodes.</summary>
    public RepExpr? Right { get; }

    /// <summary>Property name (Property, Member) or enum member name (Enum).</summary>
    public string? Name { get; }

    /// <summary>Enum type name for Enum literals, for example <c>ENetRole</c>.</summary>
    public string? EnumType { get; }

    /// <summary>Value of Bool, Int and Enum literals.</summary>
    public int Value { get; }

    /// <summary>Which net input a NetVar node reads.</summary>
    public RepNetVar NetVar { get; }

    public static readonly RepExpr True = new(RepExprKind.Bool, value: 1);

    public static readonly RepExpr False = new(RepExprKind.Bool, value: 0);

    public static readonly RepExpr Null = new(RepExprKind.None);

    public static RepExpr Bool(bool value) => value ? True : False;

    public static RepExpr Int(int value) => new(RepExprKind.Int, value: value);

    public static RepExpr Enum(string enumType, string member, int value)
    {
        ArgumentException.ThrowIfNullOrEmpty(enumType);
        ArgumentException.ThrowIfNullOrEmpty(member);
        return new RepExpr(RepExprKind.Enum, name: member, enumType: enumType, value: value);
    }

    public static RepExpr Prop(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new RepExpr(RepExprKind.Property, name: name);
    }

    public static RepExpr Member(RepExpr target, string name)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new RepExpr(RepExprKind.Member, left: target, name: name);
    }

    public static RepExpr Net(RepNetVar netVar) => new(RepExprKind.NetVar, netVar: netVar);

    public static RepExpr Not(RepExpr operand) => new(RepExprKind.Not, left: operand ?? throw new ArgumentNullException(nameof(operand)));

    public static RepExpr And(RepExpr left, RepExpr right) => Binary(RepExprKind.And, left, right);

    public static RepExpr Or(RepExpr left, RepExpr right) => Binary(RepExprKind.Or, left, right);

    public static RepExpr Eq(RepExpr left, RepExpr right) => Binary(RepExprKind.Equal, left, right);

    public static RepExpr Ne(RepExpr left, RepExpr right) => Binary(RepExprKind.NotEqual, left, right);

    public static RepExpr Lt(RepExpr left, RepExpr right) => Binary(RepExprKind.Less, left, right);

    public static RepExpr Le(RepExpr left, RepExpr right) => Binary(RepExprKind.LessEqual, left, right);

    public static RepExpr Gt(RepExpr left, RepExpr right) => Binary(RepExprKind.Greater, left, right);

    public static RepExpr Ge(RepExpr left, RepExpr right) => Binary(RepExprKind.GreaterEqual, left, right);

    private static RepExpr Binary(RepExprKind kind, RepExpr left, RepExpr right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new RepExpr(kind, left, right);
    }

    /// <summary>True when the condition reads <paramref name="netVar"/> anywhere.</summary>
    public bool Uses(RepNetVar netVar) =>
        (Kind == RepExprKind.NetVar && NetVar == netVar) || (Left?.Uses(netVar) ?? false) || (Right?.Uses(netVar) ?? false);

    /// <summary>A readable rendering for logs and test messages.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        Write(sb, 0);
        return sb.ToString();
    }

    private static int Precedence(RepExprKind kind) => kind switch
    {
        RepExprKind.Or => 1,
        RepExprKind.And => 2,
        RepExprKind.Equal or RepExprKind.NotEqual => 3,
        RepExprKind.Less or RepExprKind.LessEqual or RepExprKind.Greater or RepExprKind.GreaterEqual => 4,
        RepExprKind.Not => 5,
        _ => 6,
    };

    private void Write(StringBuilder sb, int parentPrecedence)
    {
        int p = Precedence(Kind);
        bool parens = p < parentPrecedence;
        if (parens)
        {
            sb.Append('(');
        }

        switch (Kind)
        {
            case RepExprKind.Bool:
                sb.Append(Value != 0 ? "true" : "false");
                break;
            case RepExprKind.Int:
                sb.Append(Value.ToString(CultureInfo.InvariantCulture));
                break;
            case RepExprKind.Enum:
            case RepExprKind.Property:
                sb.Append(Name);
                break;
            case RepExprKind.None:
                sb.Append("None");
                break;
            case RepExprKind.Member:
                Left!.Write(sb, 6);
                sb.Append('.').Append(Name);
                break;
            case RepExprKind.NetVar:
                sb.Append(NetVar == RepNetVar.NetMode ? "Level.NetMode" : NetVar.ToString());
                break;
            case RepExprKind.Not:
                sb.Append('!');
                Left!.Write(sb, 6);
                break;
            default:
                Left!.Write(sb, p);
                sb.Append(' ').Append(OperatorText(Kind)).Append(' ');
                Right!.Write(sb, p + 1);
                break;
        }

        if (parens)
        {
            sb.Append(')');
        }
    }

    private static string OperatorText(RepExprKind kind) => kind switch
    {
        RepExprKind.And => "&&",
        RepExprKind.Or => "||",
        RepExprKind.Equal => "==",
        RepExprKind.NotEqual => "!=",
        RepExprKind.Less => "<",
        RepExprKind.LessEqual => "<=",
        RepExprKind.Greater => ">",
        RepExprKind.GreaterEqual => ">=",
        _ => "?",
    };
}
