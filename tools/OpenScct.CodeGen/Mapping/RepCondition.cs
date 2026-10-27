using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenScct.CodeGen.Mapping;

/// <summary>Generator-side replication condition tree (mirrors OpenScct.Core.Uc.RepExpr, which it is emitted as).</summary>
internal abstract record CondNode;

internal sealed record CondBool(bool Value) : CondNode;

internal sealed record CondInt(int Value) : CondNode;

internal sealed record CondEnum(string EnumType, string Member, int Value) : CondNode;

internal sealed record CondNone : CondNode;

internal sealed record CondProp(string Name) : CondNode;

internal sealed record CondMember(CondNode Target, string Name) : CondNode;

/// <summary>Role, RemoteRole, bNetInitial, bNetOwner, bNetDirty or NetMode (Level.NetMode).</summary>
internal sealed record CondNetVar(string Var) : CondNode;

internal sealed record CondNot(CondNode Operand) : CondNode;

/// <summary>Op is And, Or, Eq, Ne, Lt, Le, Gt or Ge.</summary>
internal sealed record CondBinary(string Op, CondNode Left, CondNode Right) : CondNode;

/// <summary>Looks up names a condition uses: enum members and properties of the class that owns the block.</summary>
internal interface ICondScope
{
    /// <summary>The enum (short type name, value) that declares <paramref name="member"/>, or null.</summary>
    (string EnumType, int Value)? FindEnumMember(string member);

    /// <summary>True when the class chain declares a property named <paramref name="name"/>.</summary>
    bool HasProperty(string name);
}

/// <summary>
/// Parses replication conditions from the decompiler index into a <see cref="CondNode"/> tree. The grammar is the
/// subset replication blocks use: names, member access, enum literals, None, true, false, integers, !, comparisons,
/// &amp;&amp;, || and brackets. Precedence follows UnrealScript (comparisons bind tighter than &amp;&amp;, which binds tighter
/// than ||).
/// </summary>
internal sealed class RepConditionParser
{
    private static readonly HashSet<string> NetVars = new(StringComparer.Ordinal)
    {
        "Role", "RemoteRole", "bNetInitial", "bNetOwner", "bNetDirty",
    };

    private readonly ICondScope _scope;
    private readonly List<string> _tokens = new();
    private readonly List<string> _warnings;
    private int _pos;

    private RepConditionParser(string text, ICondScope scope, List<string> warnings)
    {
        _scope = scope;
        _warnings = warnings;
        Tokenize(text);
    }

    public static CondNode Parse(string text, ICondScope scope, List<string> warnings)
    {
        var parser = new RepConditionParser(text, scope, warnings);
        CondNode node = parser.ParseOr();
        if (parser._pos != parser._tokens.Count)
        {
            throw new FormatException($"Unexpected '{parser._tokens[parser._pos]}' in replication condition '{text}'.");
        }

        return node;
    }

    private void Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                _tokens.Add(text[start..i]);
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                _tokens.Add(text[start..i]);
                continue;
            }

            string two = i + 1 < text.Length ? text.Substring(i, 2) : string.Empty;
            if (two is "&&" or "||" or "==" or "!=" or "<=" or ">=")
            {
                _tokens.Add(two);
                i += 2;
                continue;
            }

            if (c is '!' or '<' or '>' or '(' or ')' or '.')
            {
                _tokens.Add(c.ToString());
                i++;
                continue;
            }

            throw new FormatException($"Unexpected character '{c}' in replication condition '{text}'.");
        }
    }

    private string? Peek => _pos < _tokens.Count ? _tokens[_pos] : null;

    private string Next() => _pos < _tokens.Count ? _tokens[_pos++] : throw new FormatException("Replication condition ends early.");

    private CondNode ParseOr()
    {
        CondNode left = ParseAnd();
        while (Peek == "||")
        {
            _pos++;
            left = new CondBinary("Or", left, ParseAnd());
        }

        return left;
    }

    private CondNode ParseAnd()
    {
        CondNode left = ParseEquality();
        while (Peek == "&&")
        {
            _pos++;
            left = new CondBinary("And", left, ParseEquality());
        }

        return left;
    }

    private CondNode ParseEquality()
    {
        CondNode left = ParseRelational();
        while (Peek is "==" or "!=")
        {
            string op = Next() == "==" ? "Eq" : "Ne";
            left = new CondBinary(op, left, ParseRelational());
        }

        return left;
    }

    private CondNode ParseRelational()
    {
        CondNode left = ParseUnary();
        while (Peek is "<" or "<=" or ">" or ">=")
        {
            string op = Next() switch
            {
                "<" => "Lt",
                "<=" => "Le",
                ">" => "Gt",
                _ => "Ge",
            };
            left = new CondBinary(op, left, ParseUnary());
        }

        return left;
    }

    private CondNode ParseUnary()
    {
        if (Peek == "!")
        {
            _pos++;
            return new CondNot(ParseUnary());
        }

        return ParsePrimary();
    }

    private CondNode ParsePrimary()
    {
        string t = Next();
        if (t == "(")
        {
            CondNode inner = ParseOr();
            if (Next() != ")")
            {
                throw new FormatException("Missing ')' in replication condition.");
            }

            return inner;
        }

        if (char.IsAsciiDigit(t[0]))
        {
            return new CondInt(int.Parse(t, NumberStyles.None, CultureInfo.InvariantCulture));
        }

        switch (t)
        {
            case "None":
                return new CondNone();
            case "true":
            case "True":
                return new CondBool(true);
            case "false":
            case "False":
                return new CondBool(false);
        }

        if (t == "Level" && Peek == "." && _pos + 1 < _tokens.Count && _tokens[_pos + 1] == "NetMode")
        {
            _pos += 2;
            return new CondNetVar("NetMode");
        }

        CondNode node;
        if (NetVars.Contains(t))
        {
            node = new CondNetVar(t);
        }
        else if (Peek != "." && _scope.FindEnumMember(t) is { } e && !_scope.HasProperty(t))
        {
            return new CondEnum(e.EnumType, t, e.Value);
        }
        else
        {
            if (!_scope.HasProperty(t))
            {
                _warnings.Add($"replication condition names '{t}', which is not a property of the class chain");
            }

            node = new CondProp(t);
        }

        while (Peek == ".")
        {
            _pos++;
            node = new CondMember(node, Next());
        }

        return node;
    }

    /// <summary>The C# expression that builds <paramref name="node"/> with OpenScct.Core.Uc.RepExpr.</summary>
    public static string ToCSharp(CondNode node)
    {
        var sb = new StringBuilder();
        Write(sb, node);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, CondNode node)
    {
        switch (node)
        {
            case CondBool b:
                sb.Append(b.Value ? "RepExpr.True" : "RepExpr.False");
                break;
            case CondInt i:
                sb.Append("RepExpr.Int(").Append(i.Value.ToString(CultureInfo.InvariantCulture)).Append(')');
                break;
            case CondEnum e:
                sb.Append("RepExpr.Enum(").Append(Emit.CSharp.String(e.EnumType)).Append(", ").Append(Emit.CSharp.String(e.Member))
                    .Append(", ").Append(e.Value.ToString(CultureInfo.InvariantCulture)).Append(')');
                break;
            case CondNone:
                sb.Append("RepExpr.Null");
                break;
            case CondProp p:
                sb.Append("RepExpr.Prop(").Append(Emit.CSharp.String(p.Name)).Append(')');
                break;
            case CondMember m:
                sb.Append("RepExpr.Member(");
                Write(sb, m.Target);
                sb.Append(", ").Append(Emit.CSharp.String(m.Name)).Append(')');
                break;
            case CondNetVar v:
                sb.Append("RepExpr.Net(RepNetVar.").Append(v.Var).Append(')');
                break;
            case CondNot n:
                sb.Append("RepExpr.Not(");
                Write(sb, n.Operand);
                sb.Append(')');
                break;
            case CondBinary bin:
                sb.Append("RepExpr.").Append(bin.Op).Append('(');
                Write(sb, bin.Left);
                sb.Append(", ");
                Write(sb, bin.Right);
                sb.Append(')');
                break;
            default:
                throw new InvalidOperationException($"Unknown condition node {node}.");
        }
    }
}
