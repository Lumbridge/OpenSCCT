using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.CodeGen.Json;
using OpenScct.CodeGen.Model;
using OpenScct.CodeGen.Plan;

namespace OpenScct.CodeGen.Emit;

/// <summary>
/// Writes the body of a class's generated SetDefaults: one assignment per property whose effective default differs
/// from the parent class's (a missing value counts as zero, None or empty). Values are written exactly: floats
/// round-trip their binary32 value, enums use their member names, assets and classes keep their paths.
/// </summary>
internal sealed class DefaultsWriter
{
    private readonly UcModel _model;
    private readonly Planner _plan;
    private readonly Report _report;

    public DefaultsWriter(UcModel model, Planner plan, Report report)
    {
        _model = model;
        _plan = plan;
        _report = report;
    }

    /// <summary>The statements of SetDefaults for <paramref name="c"/>, without the base call.</summary>
    public List<string> Statements(ClassPlan c)
    {
        var lines = new List<string>();
        if (!_model.Defaults.TryGetValue(c.Class.Path, out Dictionary<string, JValue>? mine))
        {
            if (c.Class.IsScript)
            {
                _report.Add("Missing data", $"{c.Class.Path}: no class defaults");
            }

            return lines;
        }

        Dictionary<string, JValue>? parent = c.Super is null ? null : _model.Defaults.GetValueOrDefault(c.Super.Class.Path);

        // Every property of the chain, base class first, in declaration order. A property that shadows an
        // ancestor's property of the same name takes the default (defaults are matched by name, nearest first).
        var props = new List<(ClassPlan Owner, FieldPlan Field)>();
        foreach (ClassPlan x in c.Chain().Reverse())
        {
            foreach (FieldPlan f in x.Fields)
            {
                props.RemoveAll(p => string.Equals(p.Field.Var.Name, f.Var.Name, StringComparison.OrdinalIgnoreCase));
                props.Add((x, f));
            }
        }

        var known = new HashSet<string>(props.Select(p => p.Field.Var.Name), StringComparer.OrdinalIgnoreCase);
        foreach (string key in mine.Keys.Order(StringComparer.Ordinal))
        {
            if (!known.Contains(key))
            {
                _report.Add("Defaults not applied", $"{c.Class.Path}.{key}: no such property in the class chain");
            }
        }

        foreach ((ClassPlan owner, FieldPlan f) in props)
        {
            UcVar v = f.Var;
            bool declaredHere = ReferenceEquals(owner, c);
            JValue? value = mine.GetValueOrDefault(v.Name);

            // A property declared here starts from zero: a parent value of the same name belongs to a shadowed one.
            JValue? inherited = declaredHere ? null : parent?.GetValueOrDefault(v.Name);

            // A hand-written field with an initializer keeps that value unless the generated code overwrites it, so
            // the declaring class always writes the decoded value.
            bool forceAtDeclaration = declaredHere && f.Hand is { HasInitializer: true } && f.CsType is CsKeyword { Keyword: not "string" } or CsNamed { Kind: "enum" or "struct" };
            string where = c.Class.Path + "." + v.Name;
            if (v.ArrayDim > 1)
            {
                WriteStaticArray(c, f, value, inherited, where, lines);
                continue;
            }

            if (Same(value, inherited, v.Type) && !forceAtDeclaration)
            {
                continue;
            }

            if (!f.Generated && !f.Assignable)
            {
                if (!IsZero(value, v.Type) || !IsZero(inherited, v.Type))
                {
                    _report.Add("Defaults not applied", $"{where}: {f.SkipReason}");
                }

                continue;
            }

            string target = f.CsName;
            if (IsLocalizedString(v))
            {
                lines.Add($"{target} = {Text(c)}.Get({CSharp.String(c.Class.Path)}, {CSharp.String(v.Name)}, {target});");
                continue;
            }

            if (v.Type is UArray { Inner: UString } && (v.Flags & LocalizedFlag) != 0)
            {
                lines.Add($"{Text(c)}.Fill({target}, {CSharp.String(c.Class.Path)}, {CSharp.String(v.Name)});");
                continue;
            }

            if (v.Type is UArray a)
            {
                lines.Add(ListAssignment(c, f, a, value, where));
                continue;
            }

            var extra = new List<string>();
            if (Render(value, v.Type, c, where, target, extra) is { } expr)
            {
                lines.Add($"{target} = {expr};");
                lines.AddRange(extra);
            }
        }

        return lines;
    }

    private void WriteStaticArray(ClassPlan c, FieldPlan f, JValue? value, JValue? inherited, string where, List<string> lines)
    {
        if (!f.Generated && !f.Assignable)
        {
            if (!IsZero(value, f.Var.Type) || !IsZero(inherited, f.Var.Type))
            {
                _report.Add("Defaults not applied", $"{where}: {f.SkipReason}");
            }

            return;
        }

        for (int i = 0; i < f.Var.ArrayDim; i++)
        {
            JValue? mineAt = Element(value, i);
            JValue? parentAt = Element(inherited, i);
            if (Same(mineAt, parentAt, f.Var.Type))
            {
                continue;
            }

            string target = f.CsName + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (IsLocalizedString(f.Var))
            {
                lines.Add($"{target} = {Text(c)}.Get({CSharp.String(c.Class.Path)}, {CSharp.String(f.Var.Name)}, {CSharp.Int(i)}, {target});");
                continue;
            }

            var extra = new List<string>();
            if (Render(mineAt, f.Var.Type, c, where + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", target, extra) is { } expr)
            {
                lines.Add($"{target} = {expr};");
                lines.AddRange(extra);
            }
        }
    }

    /// <summary>
    /// A localized string property (CPF_Localized). Its text belongs to the game, so the generated code never holds
    /// it: SetDefaults reads it from the player's content at run time (<c>UcText</c>), keeping the inherited value
    /// when the content has none.
    /// </summary>
    private static bool IsLocalizedString(UcVar v) => (v.Flags & LocalizedFlag) != 0 && v.Type is UString;

    private const uint LocalizedFlag = 0x8000;

    private string Text(ClassPlan c) => _plan.Namer.Name(TypeNamer.UcNamespace, "UcText", c.Target.Namespace, c.Shadowing);

    /// <summary>Element <paramref name="i"/> of a static array value: a list (null entries unset) or an {index: value} object.</summary>
    private static JValue? Element(JValue? value, int i)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Kind == JKind.Array)
        {
            return i < value.Items.Count ? value.Items[i] : null;
        }

        if (value.Kind == JKind.Object)
        {
            return value.Get(i.ToString(CultureInfo.InvariantCulture));
        }

        return i == 0 ? value : null;
    }

    private string ListAssignment(ClassPlan c, FieldPlan f, UArray a, JValue? value, string where)
    {
        if (value is null || value.Kind == JKind.Null || (value.Kind == JKind.Array && value.Items.Count == 0))
        {
            return $"{f.CsName}.Clear();";
        }

        if (value.Kind != JKind.Array)
        {
            _report.Add("Defaults not applied", $"{where}: expected a list, found {value.Kind}");
            return $"// {where}: default not applied";
        }

        (CsType element, _) = _plan.Map(a.Inner, where);
        string elementType = _plan.Namer.Write(element, c.Target.Namespace, c.Shadowing, nullable: true);
        var items = new List<string>();
        for (int i = 0; i < value.Items.Count; i++)
        {
            var extra = new List<string>();
            string? item = Render(value.Items[i], a.Inner, c, where + "(" + i.ToString(CultureInfo.InvariantCulture) + ")", null, extra);
            if (item is null || extra.Count > 0)
            {
                _report.Add("Defaults not applied", $"{where}: element {i} has no expression form");
                return $"// {where}: default not applied";
            }

            items.Add(item);
        }

        string defaults = _plan.Namer.Name(OpenScct.CodeGen.Emit.TypeNamer.UcNamespace, "UcDefaults", c.Target.Namespace, c.Shadowing);
        return $"{defaults}.Set<{elementType}>({f.CsName}, {string.Join(", ", items)});";
    }

    /// <summary>
    /// The C# expression for a value, or null when it cannot be written (reported). Inline-array members of structs
    /// are assigned by extra statements on <paramref name="target"/> (added to <paramref name="extra"/>).
    /// </summary>
    private string? Render(JValue? value, UType type, ClassPlan c, string where, string? target, List<string> extra)
    {
        string ns = c.Target.Namespace;
        switch (type)
        {
            case UBool:
                return CSharp.Bool(value is { Kind: JKind.Bool, Bool: true } || value is { Kind: JKind.Number, Number: not 0 });
            case UByte:
            case UInt:
                return CSharp.Int(IntOf(value, where));
            case UFloat:
                return CSharp.Float(FloatOf(value));
            case UName:
            {
                string s = value?.Kind == JKind.String ? value.String! : string.Empty;
                return s.Length == 0 || s.Equals("None", StringComparison.OrdinalIgnoreCase) ? "default" : CSharp.String(s);
            }

            case UString:
                return CSharp.String(value?.Kind == JKind.String ? value.String : string.Empty);
            case UEnum e:
                return RenderEnum(value, e, c, where);
            case UStruct s:
                return RenderStruct(value, s, c, where, target, extra);
            case UObjectRef o:
            {
                if (value is null || value.IsNull || (value.Kind == JKind.String && value.String!.Equals("None", StringComparison.OrdinalIgnoreCase)))
                {
                    return _plan.IsAsset(o.ClassPath) ? "default" : "null";
                }

                if (_plan.IsAsset(o.ClassPath) && value.Kind == JKind.String)
                {
                    return "new " + _plan.Namer.Name(TypeNamer.UcNamespace, "AssetRef", ns, c.Shadowing) + "(" + CSharp.String(value.String) + ")";
                }

                _report.Add("Defaults not applied", $"{where}: object reference {value} to a non-content class {o.ClassPath}");
                return null;
            }

            case UClassRef:
            {
                if (value is null || value.IsNull)
                {
                    return "default";
                }

                if (value.Kind == JKind.String)
                {
                    return "new " + _plan.Namer.Name(TypeNamer.UcNamespace, "ClassRef", ns, c.Shadowing) + "(" + CSharp.String(value.String) + ")";
                }

                _report.Add("Defaults not applied", $"{where}: class value {value}");
                return null;
            }

            case UDelegate:
                if (value is null || value.IsNull)
                {
                    return "default";
                }

                _report.Add("Defaults not applied", $"{where}: delegate value {value}");
                return null;
            default:
                _report.Add("Defaults not applied", $"{where}: no rendering for {type}");
                return null;
        }
    }

    private string? RenderEnum(JValue? value, UEnum e, ClassPlan c, string where)
    {
        int index;
        string? member = null;
        if (value is null || value.IsNull)
        {
            index = 0;
        }
        else if (value.Kind == JKind.Object)
        {
            index = (int)(value.Get("value")?.Number ?? 0);
            member = value.Get("enum")?.String;
        }
        else
        {
            index = IntOf(value, where);
        }

        if (!_plan.Enums.TryGetValue(e.EnumPath, out EnumPlan? plan))
        {
            return CSharp.Int(index);
        }

        string typeName = _plan.Namer.Write(plan.Type, c.Target.Namespace, c.Shadowing);
        if (index >= 0 && index < plan.Def.Members.Count)
        {
            if (member is not null && member != plan.Def.Members[index])
            {
                _report.Add("Default value warnings", $"{where}: enum value {index} is {plan.Def.Members[index]}, the defaults name {member}");
            }

            return typeName + "." + CSharp.Ident(plan.Def.Members[index]);
        }

        _report.Add("Default value warnings", $"{where}: enum value {index} is outside {plan.Def.Name}");
        return "(" + typeName + ")" + CSharp.Int(index);
    }

    private string? RenderStruct(JValue? value, UStruct s, ClassPlan c, string where, string? target, List<string> extra)
    {
        if (!_plan.Structs.TryGetValue(s.StructPath, out StructPlan? sp))
        {
            _report.Add("Defaults not applied", $"{where}: struct {s.StructPath} is not generated");
            return null;
        }

        string typeName = _plan.Namer.Write(sp.Type, c.Target.Namespace, c.Shadowing);
        if (sp.Mapped)
        {
            // Hand-written runtime structs (FVector, FRotator, FCoords) take their members in order in a constructor.
            var args = new List<string>();
            foreach (FieldPlan m in sp.Members)
            {
                string? arg = Render(value?.Get(m.Var.Name), m.Var.Type, c, where + "." + m.Var.Name, null, extra);
                if (arg is null)
                {
                    return null;
                }

                args.Add(arg);
            }

            return "new " + typeName + "(" + string.Join(", ", args) + ")";
        }

        var inits = new List<string>();
        foreach (FieldPlan m in sp.Members)
        {
            JValue? mv = value?.Get(m.Var.Name);
            if (m.Var.ArrayDim > 1)
            {
                for (int i = 0; i < m.Var.ArrayDim; i++)
                {
                    JValue? ev = Element(mv, i);
                    if (IsZero(ev, m.Var.Type))
                    {
                        continue;
                    }

                    if (target is null)
                    {
                        _report.Add("Defaults not applied", $"{where}.{m.Var.Name}: inline array in a value without a target");
                        return null;
                    }

                    string elementTarget = target + "." + m.CsName + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    string? item = Render(ev, m.Var.Type, c, where + "." + m.Var.Name, elementTarget, extra);
                    if (item is null)
                    {
                        return null;
                    }

                    extra.Add(elementTarget + " = " + item + ";");
                }

                continue;
            }

            if (IsZero(mv, m.Var.Type) && m.CsType is not CsList)
            {
                continue;
            }

            if (m.Var.Type is UArray arr)
            {
                if (mv is null || mv.Kind != JKind.Array || mv.Items.Count == 0)
                {
                    continue;
                }

                (CsType element, _) = _plan.Map(arr.Inner, where);
                var items = new List<string>();
                foreach (JValue item in mv.Items)
                {
                    string? r = Render(item, arr.Inner, c, where + "." + m.Var.Name, null, extra);
                    if (r is null)
                    {
                        return null;
                    }

                    items.Add(r);
                }

                string list = _plan.Namer.Write(new CsList(element), c.Target.Namespace, c.Shadowing);
                inits.Add(m.CsName + " = new " + list + " { " + string.Join(", ", items) + " }");
                continue;
            }

            string? memberTarget = target is null ? null : target + "." + m.CsName;
            string? rendered = Render(mv, m.Var.Type, c, where + "." + m.Var.Name, memberTarget, extra);
            if (rendered is null)
            {
                return null;
            }

            inits.Add(m.CsName + " = " + rendered);
        }

        return inits.Count == 0 ? "new " + typeName + "()" : "new " + typeName + " { " + string.Join(", ", inits) + " }";
    }

    private int IntOf(JValue? value, string where)
    {
        if (value is null || value.IsNull)
        {
            return 0;
        }

        if (value.Kind == JKind.Bool)
        {
            return value.Bool ? 1 : 0;
        }

        if (value.Kind == JKind.Object && value.Get("value") is { } inner)
        {
            return IntOf(inner, where);
        }

        if (value.Kind != JKind.Number)
        {
            _report.Add("Default value warnings", $"{where}: expected a number, found {value}");
            return 0;
        }

        return (int)value.Number;
    }

    private static float FloatOf(JValue? value) => value is { Kind: JKind.Number } ? (float)value.Number : 0f;

    // ---- comparisons -----------------------------------------------------------------------------------------

    /// <summary>True when a value is the zero value of its type (missing, None, empty, false, 0).</summary>
    public bool IsZero(JValue? value, UType type)
    {
        if (value is null || value.IsNull)
        {
            return true;
        }

        switch (type)
        {
            case UBool:
                return value.Kind == JKind.Bool ? !value.Bool : value.Kind == JKind.Number && value.Number == 0;
            case UByte:
            case UInt:
            case UEnum:
                return value.Kind == JKind.Object ? (value.Get("value")?.Number ?? 0) == 0 : value.Kind == JKind.Number && value.Number == 0;
            case UFloat:
                return value.Kind == JKind.Number && BitConverter.SingleToInt32Bits((float)value.Number) == 0;
            case UName:
                return value.Kind == JKind.String && (value.String!.Length == 0 || value.String.Equals("None", StringComparison.OrdinalIgnoreCase));
            case UString:
                return value.Kind == JKind.String && value.String!.Length == 0;
            case UObjectRef or UClassRef or UDelegate:
                return value.Kind == JKind.String && (value.String!.Length == 0 || value.String.Equals("None", StringComparison.OrdinalIgnoreCase));
            case UArray:
                return value.Kind == JKind.Array && value.Items.Count == 0;
            case UStruct s:
            {
                if (value.Kind != JKind.Object || !_model.Structs.TryGetValue(s.StructPath, out UcStructDef? def))
                {
                    return false;
                }

                foreach (UcVar m in _model.AllStructMembers(def))
                {
                    JValue? mv = value.Get(m.Name);
                    if (m.ArrayDim > 1)
                    {
                        for (int i = 0; i < m.ArrayDim; i++)
                        {
                            if (!IsZero(Element(mv, i), m.Type))
                            {
                                return false;
                            }
                        }
                    }
                    else if (!IsZero(mv, m.Type))
                    {
                        return false;
                    }
                }

                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>True when two values of <paramref name="type"/> are the same (missing equals zero).</summary>
    public bool Same(JValue? a, JValue? b, UType type)
    {
        bool za = IsZero(a, type);
        bool zb = IsZero(b, type);
        if (za || zb)
        {
            return za && zb;
        }

        switch (type)
        {
            case UBool:
                return true;
            case UByte:
            case UInt:
            case UEnum:
                return Number(a!) == Number(b!);
            case UFloat:
                return BitConverter.SingleToInt32Bits((float)a!.Number) == BitConverter.SingleToInt32Bits((float)b!.Number);
            case UName:
            case UObjectRef:
            case UClassRef:
                return string.Equals(a!.String, b!.String, StringComparison.OrdinalIgnoreCase);
            case UString:
                return string.Equals(a!.String, b!.String, StringComparison.Ordinal);
            case UArray arr:
                if (a!.Kind != JKind.Array || b!.Kind != JKind.Array || a.Items.Count != b.Items.Count)
                {
                    return false;
                }

                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (!Same(a.Items[i], b.Items[i], arr.Inner))
                    {
                        return false;
                    }
                }

                return true;
            case UStruct s:
            {
                if (!_model.Structs.TryGetValue(s.StructPath, out UcStructDef? def))
                {
                    return false;
                }

                foreach (UcVar m in _model.AllStructMembers(def))
                {
                    JValue? ma = a!.Get(m.Name);
                    JValue? mb = b!.Get(m.Name);
                    if (m.ArrayDim > 1)
                    {
                        for (int i = 0; i < m.ArrayDim; i++)
                        {
                            if (!Same(Element(ma, i), Element(mb, i), m.Type))
                            {
                                return false;
                            }
                        }
                    }
                    else if (!Same(ma, mb, m.Type))
                    {
                        return false;
                    }
                }

                return true;
            }

            default:
                return a!.ToString() == b!.ToString();
        }
    }

    private static double Number(JValue v) => v.Kind == JKind.Object ? v.Get("value")?.Number ?? 0 : v.Kind == JKind.Bool ? (v.Bool ? 1 : 0) : v.Number;
}
