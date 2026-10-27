using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Net;

/// <summary>
/// Parses a replication condition written as UnrealScript text (<c>bNetInitial &amp;&amp; Role == ROLE_Authority</c>)
/// into a <see cref="RepExpr"/>, with UnrealScript precedence (<c>!</c>, then <c>&lt;</c> <c>&lt;=</c> <c>&gt;</c>
/// <c>&gt;=</c>, then <c>==</c> <c>!=</c>, then <c>&amp;&amp;</c>, then <c>||</c>).
/// <c>Role</c>, <c>RemoteRole</c>, <c>bNetInitial</c>, <c>bNetOwner</c>, <c>bNetDirty</c> and
/// <c>Level.NetMode</c> are net inputs; enum members of ENetRole, ENetMode, EPhysics, EDrawType and ELightType are
/// literals; <c>A.B</c> reads property B of the object property A refers to; anything else names a property. Used
/// for the decoded native property lists and in tests.
/// </summary>
public static class RepConditionParser
{
    private static readonly Dictionary<string, (string Type, int Value)> s_enums = BuildEnums();

    public static RepExpr Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var p = new Parser(text);
        RepExpr e = p.ParseOr();
        p.SkipSpace();
        if (!p.AtEnd)
        {
            throw new FormatException($"Unexpected '{text[p.Pos..]}' in condition '{text}'.");
        }

        return e;
    }

    private static Dictionary<string, (string, int)> BuildEnums()
    {
        var map = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (Type t in new[] { typeof(ENetRole), typeof(ENetMode), typeof(EPhysics), typeof(EDrawType), typeof(ELightType) })
        {
            foreach (object v in Enum.GetValues(t))
            {
                map[Enum.GetName(t, v)!] = (t.Name, Convert.ToInt32(v, CultureInfo.InvariantCulture));
            }
        }

        return map;
    }

    private sealed class Parser(string text)
    {
        public int Pos;

        public bool AtEnd => Pos >= text.Length;

        public void SkipSpace()
        {
            while (Pos < text.Length && char.IsWhiteSpace(text[Pos]))
            {
                Pos++;
            }
        }

        private bool Take(string op)
        {
            SkipSpace();
            if (string.CompareOrdinal(text, Pos, op, 0, op.Length) == 0)
            {
                Pos += op.Length;
                return true;
            }

            return false;
        }

        public RepExpr ParseOr()
        {
            RepExpr left = ParseAnd();
            while (Take("||"))
            {
                left = RepExpr.Or(left, ParseAnd());
            }

            return left;
        }

        private RepExpr ParseAnd()
        {
            RepExpr left = ParseCompare();
            while (Take("&&"))
            {
                left = RepExpr.And(left, ParseCompare());
            }

            return left;
        }

        private RepExpr ParseCompare()
        {
            RepExpr left = ParseRelational();
            while (true)
            {
                if (Take("=="))
                {
                    left = RepExpr.Eq(left, ParseRelational());
                }
                else if (Take("!="))
                {
                    left = RepExpr.Ne(left, ParseRelational());
                }
                else
                {
                    return left;
                }
            }
        }

        private RepExpr ParseRelational()
        {
            RepExpr left = ParseUnary();
            while (true)
            {
                if (Take("<="))
                {
                    left = RepExpr.Le(left, ParseUnary());
                }
                else if (Take(">="))
                {
                    left = RepExpr.Ge(left, ParseUnary());
                }
                else if (Take("<"))
                {
                    left = RepExpr.Lt(left, ParseUnary());
                }
                else if (Take(">"))
                {
                    left = RepExpr.Gt(left, ParseUnary());
                }
                else
                {
                    return left;
                }
            }
        }

        private RepExpr ParseUnary()
        {
            if (Take("!"))
            {
                return RepExpr.Not(ParseUnary());
            }

            if (Take("("))
            {
                RepExpr inner = ParseOr();
                if (!Take(")"))
                {
                    throw new FormatException($"Missing ')' in condition '{text}'.");
                }

                return inner;
            }

            SkipSpace();
            int start = Pos;
            if (Pos < text.Length && (char.IsDigit(text[Pos]) || text[Pos] == '-'))
            {
                Pos++;
                while (Pos < text.Length && char.IsDigit(text[Pos]))
                {
                    Pos++;
                }

                return RepExpr.Int(int.Parse(text.AsSpan(start, Pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture));
            }

            string name = Identifier();
            if (Take("."))
            {
                string member = Identifier();
                if (string.Equals(name, "Level", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(member, "NetMode", StringComparison.OrdinalIgnoreCase))
                {
                    return RepExpr.Net(RepNetVar.NetMode);
                }

                return RepExpr.Member(Atom(name), member);
            }

            return Atom(name);
        }

        private string Identifier()
        {
            SkipSpace();
            int start = Pos;
            while (Pos < text.Length && (char.IsLetterOrDigit(text[Pos]) || text[Pos] == '_'))
            {
                Pos++;
            }

            if (Pos == start)
            {
                throw new FormatException($"Expected a name at {start} in condition '{text}'.");
            }

            return text[start..Pos];
        }

        private static RepExpr Atom(string name)
        {
            switch (name)
            {
                case "true":
                case "True":
                    return RepExpr.True;
                case "false":
                case "False":
                    return RepExpr.False;
                case "None":
                    return RepExpr.Null;
                case "Role":
                    return RepExpr.Net(RepNetVar.Role);
                case "RemoteRole":
                    return RepExpr.Net(RepNetVar.RemoteRole);
                case "bNetInitial":
                    return RepExpr.Net(RepNetVar.bNetInitial);
                case "bNetOwner":
                    return RepExpr.Net(RepNetVar.bNetOwner);
                case "bNetDirty":
                    return RepExpr.Net(RepNetVar.bNetDirty);
            }

            if (s_enums.TryGetValue(name, out (string Type, int Value) e))
            {
                return RepExpr.Enum(e.Type, name, e.Value);
            }

            return RepExpr.Prop(name);
        }
    }
}
