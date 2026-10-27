using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.CodeGen.Mapping;
using OpenScct.CodeGen.Model;
using OpenScct.CodeGen.Plan;

namespace OpenScct.CodeGen.Emit;

/// <summary>Writes the generated files from a finished <see cref="Planner"/>.</summary>
internal sealed class Emitter
{
    private readonly UcModel _model;
    private readonly Planner _plan;
    private readonly Report _report;
    private readonly DefaultsWriter _defaults;

    public Emitter(UcModel model, Planner plan, Report report)
    {
        _model = model;
        _plan = plan;
        _report = report;
        _defaults = new DefaultsWriter(model, plan, report);
    }

    /// <summary>Every generated file: repository-relative path to content.</summary>
    public SortedDictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public void Run()
    {
        foreach (int n in _plan.InlineArraySizes)
        {
            _plan.Namer.Add(TypeNamer.UcNamespace, "FixedArray" + n.ToString(CultureInfo.InvariantCulture));
        }

        foreach (ClassPlan c in _plan.Classes.Values)
        {
            Files[c.Target.OutputDir + "/" + c.CsName + ".g.cs"] = ClassFile(c);
            _report.Count("classes");
            _report.Count("properties", c.Fields.Count);
            _report.Count("properties generated", c.Fields.Count(f => f.Generated));
            _report.Count("functions", c.Class.Functions.Count + c.Class.States.Sum(s => s.Functions.Count));
            _report.Count("states", c.Class.States.Count);
            _report.Count("consts", c.Consts.Count);
        }

        foreach (PackageTarget t in _plan.Classes.Values.Select(c => c.Target).Distinct().OrderBy(t => t.Package, StringComparer.Ordinal))
        {
            Files[t.OutputDir + "/" + t.Package + "Package.g.cs"] = PackageFile(t);
            if (_plan.Enums.Values.Any(e => e.Target == t && !e.HandWritten))
            {
                Files[t.OutputDir + "/" + t.Package + ".Enums.g.cs"] = EnumsFile(t);
            }

            if (_plan.Structs.Values.Any(s => s.Target == t && !s.HandWritten))
            {
                Files[t.OutputDir + "/" + t.Package + ".Structs.g.cs"] = StructsFile(t);
            }
        }

        if (_plan.InlineArraySizes.Count > 0)
        {
            PackageTarget core = _plan.TargetOf("Core") ?? _plan.Classes.Values.First().Target;
            Files[core.OutputDir + "/FixedArrays.g.cs"] = FixedArraysFile();
        }

        _report.Count("enums", _plan.Enums.Values.Count(e => !e.HandWritten));
        _report.Count("structs", _plan.Structs.Values.Count(s => !s.HandWritten));
    }

    private static CodeWriter Begin(string ns)
    {
        var w = new CodeWriter();
        foreach (string line in CodeWriter.Header.TrimEnd('\n').Split('\n'))
        {
            w.Line(line);
        }

        w.Line("#nullable enable");
        w.Line();
        foreach (string import in TypeNamer.Imports)
        {
            w.Line("using " + import + ";");
        }

        w.Line();
        w.Line("namespace " + ns + ";");
        w.Line();
        return w;
    }

    // ---- classes ---------------------------------------------------------------------------------------------

    private string ClassFile(ClassPlan c)
    {
        string ns = c.Target.Namespace;
        CodeWriter w = Begin(ns);
        string kind = c.IsIntrinsic ? "an intrinsic class (no script; fields only)" : "a script class";
        w.Line("/// <summary>");
        w.Line($"/// {c.Class.Path}, {kind}{(c.Class.SuperPath is null ? string.Empty : ", extends " + c.Class.SuperPath)}. Generated");
        w.Line("/// properties, consts, class defaults and metadata; behaviour lives in hand-written partial files.");
        w.Line("/// </summary>");
        string modifiers = c.Class.IsAbstract ? "public abstract partial class " : "public partial class ";
        string baseText = c.Super is null ? string.Empty : " : " + _plan.Namer.Write(c.Super.Type, ns, c.TypeShadowing);
        w.Open(modifiers + c.CsName + baseText);

        bool first = true;
        foreach (FieldPlan f in c.Fields.Where(f => f.Generated))
        {
            first = false;
            string type = _plan.Namer.Write(f.CsType, ns, c.TypeShadowing, nullable: true);
            string mods = "public " + (f.New ? "new " : string.Empty) + (f.CsType is CsArray ? "readonly " : string.Empty);
            string init = f.Initializer is null ? string.Empty : " = " + f.Initializer;
            w.Line($"{mods}{type} {f.CsName}{init}; // {Comment(f)}");
        }

        if (c.Consts.Count > 0)
        {
            if (!first)
            {
                w.Line();
            }

            first = false;
            foreach (ConstPlan k in c.Consts)
            {
                w.Line("public " + (k.New ? "new " : string.Empty) + k.Declaration + ";");
            }
        }

        if (c.EmitSetDefaults)
        {
            List<string> statements = _defaults.Statements(c);
            _report.Count("default assignments", statements.Count);
            if (statements.Count > 0)
            {
                if (!first)
                {
                    w.Line();
                }

                first = false;
                w.Open("protected override void SetDefaults()");
                w.Line("base.SetDefaults();");
                foreach (string s in statements)
                {
                    w.Line(s);
                }

                w.Close();
            }
        }

        if (c.EmitGetUcClass)
        {
            if (!first)
            {
                w.Line();
            }

            first = false;
            w.Line("public override UcClassInfo GetUcClass() => UcMeta.Class;");
        }

        if (!first)
        {
            w.Line();
        }

        WriteMeta(w, c);
        w.Close();
        return w.ToString();
    }

    private static string Comment(FieldPlan f)
    {
        var parts = new List<string> { f.Var.TypeText + (f.Var.ArrayDim > 1 ? "[" + f.Var.ArrayDim.ToString(CultureInfo.InvariantCulture) + "]" : string.Empty) };
        parts.AddRange(FlagNames(f.Var.Flags));
        return string.Join(", ", parts);
    }

    private static readonly (uint Bit, string Name)[] CommentFlags =
    [
        (0x1, "Edit"), (0x2, "Const"), (0x4, "Input"), (0x20, "Net"), (0x1000, "Native"), (0x2000, "Transient"),
        (0x4000, "Config"), (0x8000, "Localized"), (0x10000, "Travel"), (0x20000, "EditConst"), (0x40000, "GlobalConfig"),
        (0x4000000, "EditInline"), (0x20000000, "Deprecated"),
    ];

    private static IEnumerable<string> FlagNames(uint flags) => CommentFlags.Where(f => (flags & f.Bit) != 0).Select(f => f.Name);

    private void WriteMeta(CodeWriter w, ClassPlan c)
    {
        string ns = c.Target.Namespace;
        w.Line($"/// <summary>Generated metadata of {c.Class.Path}: properties, unit manifest, states and replication.</summary>");
        w.Open("public static " + (c.IsRoot ? string.Empty : "new ") + "class UcMeta");
        w.Line("public static readonly UcClassInfo Class = new(");
        w.Indent();
        w.Line(CSharp.String(c.Class.Path) + ",");
        w.Line("typeof(" + c.CsName + "),");
        w.Line((c.Super is null ? "null" : _plan.Namer.Name(c.Super.Target.Namespace, c.Super.CsName, ns, c.Shadowing) + ".UcMeta.Class") + ",");
        w.Line("(UcClassFlags)" + CSharp.Hex(c.Class.Flags) + ",");
        w.Line(CSharp.String(c.Class.Config) + ",");
        w.Line(CSharp.Int(c.Class.PropertiesSize) + ",");
        w.Line((c.Factory ?? "null") + ",");

        WriteList(w, c.Fields.Select(f => PropertyInfo(f)), ",");
        WriteList(w, OrderFunctions(c.Class.Functions).Select(f => FunctionInfo(f)), ",");
        List<UcStateDef> states = OrderStates(c.Class.States);
        if (states.Count == 0)
        {
            w.Line("[],");
        }
        else
        {
            w.Line("[");
            w.Indent();
            foreach (UcStateDef s in states)
            {
                WriteState(w, s);
            }

            w.Outdent();
            w.Line("],");
        }

        WriteReplication(w, c, c.IsIntrinsic ? ", intrinsic: true);" : ");");

        w.Outdent();
        for (int i = 0; i < states.Count; i++)
        {
            string name = CSharp.Ident(states[i].Name);
            if (name is "Class")
            {
                continue;
            }

            if (i == 0)
            {
                w.Line();
            }

            w.Line($"public static UcStateInfo {name} => Class.States[{i.ToString(CultureInfo.InvariantCulture)}];");
        }

        w.Close();
    }

    private static void WriteList(CodeWriter w, IEnumerable<string> items, string suffix)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            w.Line("[]" + suffix);
            return;
        }

        w.Line("[");
        w.Indent();
        foreach (string item in list)
        {
            w.Line(item + ",");
        }

        w.Outdent();
        w.Line("]" + suffix);
    }

    private static List<UcFunc> OrderFunctions(IEnumerable<UcFunc> functions) =>
        functions.OrderBy(f => f.Line).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();

    private static List<UcStateDef> OrderStates(IEnumerable<UcStateDef> states) =>
        states.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

    private static string PropertyInfo(FieldPlan f)
    {
        UcVar v = f.Var;
        string args = string.Join(", ",
            CSharp.String(v.Name),
            CSharp.String(v.TypeText),
            "UcPropertyKind." + f.Kind,
            "(UcPropertyFlags)" + CSharp.Hex(v.Flags),
            CSharp.Int(v.ArrayDim),
            CSharp.String(v.Category),
            CSharp.Int(v.Offset),
            CSharp.Int(v.ElementSize),
            CSharp.Int(v.RepOffset));
        if (f.NoMember)
        {
            args += ", csharpName: \"\"";
        }
        else if (f.CsName != v.Name)
        {
            args += ", csharpName: " + CSharp.String(f.CsName.TrimStart('@'));
        }

        if (f.Hand is not null)
        {
            args += ", handWritten: true";
        }

        return "new(" + args + ")";
    }

    private static string FunctionInfo(UcFunc f)
    {
        string parameters = f.Params.Count == 0
            ? "[]"
            : "[" + string.Join(", ", f.Params.Select(p => "new(" + CSharp.String(p.Name) + ", " + CSharp.String(p.TypeText) + ", (UcPropertyFlags)" + CSharp.Hex(p.Flags) + ")")) + "]";
        string args = string.Join(", ",
            CSharp.String(f.Name),
            CSharp.String(f.Sha1),
            "(UcFunctionFlags)" + CSharp.Hex(f.Flags),
            CSharp.Int(f.NativeIndex),
            CSharp.Int(f.ScriptSize),
            CSharp.String(f.ReturnType),
            parameters);
        if (f.RepOffset >= 0)
        {
            args += ", repOffset: " + CSharp.Int(f.RepOffset);
        }

        if (f.OperPrecedence != 0)
        {
            args += ", operPrecedence: " + CSharp.Int(f.OperPrecedence);
        }

        if (f.LatentCalls.Count > 0)
        {
            args += ", latentCalls: [" + string.Join(", ", f.LatentCalls.Select(CSharp.String)) + "]";
        }

        return "new(" + args + ")";
    }

    private static void WriteState(CodeWriter w, UcStateDef s)
    {
        string ignores = "[" + string.Join(", ", s.Ignores.Select(CSharp.String)) + "]";
        string labels = "[" + string.Join(", ", s.Labels.Select(l => "new(" + CSharp.String(l.Name) + ", " + CSharp.Int(l.Offset) + ")")) + "]";
        w.Line("new(");
        w.Indent();
        w.Line(string.Join(", ",
            CSharp.String(s.Name),
            CSharp.String(s.Extends),
            "(UcStateFlags)" + CSharp.Hex(s.Flags),
            ignores,
            labels,
            CSharp.String(s.Sha1),
            CSharp.Int(s.ScriptSize)) + ",");
        string tail = s.LatentCalls.Count > 0
            ? ", latentCalls: [" + string.Join(", ", s.LatentCalls.Select(CSharp.String)) + "]),"
            : "),";
        WriteList(w, OrderFunctions(s.Functions).Select(f => FunctionInfo(f)), tail);

        w.Outdent();
    }

    private void WriteReplication(CodeWriter w, ClassPlan c, string suffix)
    {
        UcClass cls = c.Class;
        if (cls.Replication.Count == 0 && cls.ReplicationInherited.Count == 0)
        {
            w.Line("null" + suffix);
            return;
        }

        var scope = new Scope(this, c);
        w.Line("new UcReplication(");
        w.Indent();
        w.Line(CSharp.String(cls.ReplicationSha1) + ", " + CSharp.Int(cls.ReplicationScriptSize) + ",");
        w.Line("[");
        w.Indent();
        foreach (UcRepBlockDef b in cls.Replication.OrderBy(b => b.Offset))
        {
            var warnings = new List<string>();
            CondNode cond = RepConditionParser.Parse(b.Condition, scope, warnings);
            foreach (string warning in warnings)
            {
                _report.Add("Replication warnings", $"{cls.Path} block at {b.Offset}: {warning}");
            }

            w.Line("new(");
            w.Indent();
            w.Line(CSharp.Bool(b.Reliable) + ", " + CSharp.Int(b.Offset) + ",");
            w.Line(RepConditionParser.ToCSharp(cond) + ",");
            var members = new List<string>();
            foreach ((string name, bool isFunction) in b.Members)
            {
                if (!isFunction)
                {
                    if (!_model.Chain(cls).Any(x => x.Vars.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                    {
                        _report.Add("Replication warnings", $"{cls.Path}: replicated variable {name} is not a property of the class chain");
                    }

                    members.Add("new(" + CSharp.String(name) + ", UcRepMemberKind.Variable)");
                    _report.Count("replicated variables");
                    continue;
                }

                UcFunc? f = _model.Chain(cls).SelectMany(x => x.Functions).FirstOrDefault(fn => fn.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (f is null)
                {
                    _report.Add("Replication warnings", $"{cls.Path}: replicated function {name} is not declared in the class chain");
                }
                else if ((f.Flags & 0x1000000) != 0)
                {
                    _report.Add("Functions with the to-all-clients flag (0x01000000)", $"{cls.Path}.{f.Name}");
                }

                members.Add("new(" + CSharp.String(name) + ", UcRepMemberKind.Function, (UcFunctionFlags)" + CSharp.Hex(f?.Flags ?? 0) + ")");
                _report.Count("replicated functions");
            }

            w.Line("[" + string.Join(", ", members) + "]),");
            w.Outdent();
        }

        w.Outdent();
        w.Line("],");
        w.Line("[" + string.Join(", ", cls.ReplicationInherited.Select(CSharp.String)) + "])" + suffix);
        w.Outdent();
    }

    private sealed class Scope : ICondScope
    {
        private readonly Emitter _owner;
        private readonly ClassPlan _class;

        public Scope(Emitter owner, ClassPlan c)
        {
            _owner = owner;
            _class = c;
        }

        public (string EnumType, int Value)? FindEnumMember(string member)
        {
            foreach (EnumPlan e in _owner._plan.Enums.Values)
            {
                int i = e.Def.Members.IndexOf(member);
                if (i >= 0)
                {
                    return (e.CsName, i);
                }
            }

            return null;
        }

        public bool HasProperty(string name) =>
            _owner._model.Chain(_class.Class).Any(x => x.Vars.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    // ---- packages, enums, structs ----------------------------------------------------------------------------

    private string PackageFile(PackageTarget t)
    {
        CodeWriter w = Begin(t.Namespace);
        string name = t.Package + "Package";
        var shadowing = new HashSet<string>(StringComparer.Ordinal) { "Info" };
        w.Line($"/// <summary>Every generated class of the {t.Package} package, sorted by class name.</summary>");
        w.Open("public static class " + name);
        w.Line($"public static readonly UcPackageInfo Info = new({CSharp.String(t.Package)}, [");
        w.Indent();
        foreach (ClassPlan c in _plan.Classes.Values.Where(c => c.Target == t).OrderBy(c => c.Class.Name, StringComparer.Ordinal))
        {
            w.Line(_plan.Namer.Name(t.Namespace, c.CsName, t.Namespace, shadowing) + ".UcMeta.Class,");
        }

        w.Outdent();
        w.Line("]);");
        w.Close();
        return w.ToString();
    }

    private string EnumsFile(PackageTarget t)
    {
        CodeWriter w = Begin(t.Namespace);
        bool first = true;
        foreach (EnumPlan e in _plan.Enums.Values.Where(e => e.Target == t && !e.HandWritten).OrderBy(e => e.CsName, StringComparer.Ordinal))
        {
            if (!first)
            {
                w.Line();
            }

            first = false;
            w.Line($"/// <summary>{e.Def.Path}.</summary>");
            w.Open($"public enum {e.CsName} : byte");
            for (int i = 0; i < e.Def.Members.Count; i++)
            {
                w.Line($"{CSharp.Ident(e.Def.Members[i])} = {i.ToString(CultureInfo.InvariantCulture)},");
            }

            w.Close();
        }

        return w.ToString();
    }

    private string StructsFile(PackageTarget t)
    {
        CodeWriter w = Begin(t.Namespace);
        bool first = true;
        foreach (StructPlan s in _plan.Structs.Values.Where(s => s.Target == t && !s.HandWritten).OrderBy(s => s.CsName, StringComparer.Ordinal))
        {
            if (!first)
            {
                w.Line();
            }

            first = false;
            string extends = s.Def.SuperPath is null ? string.Empty : $" Includes the members of {s.Def.SuperPath}.";
            w.Line($"/// <summary>{s.Def.Path}.{extends}</summary>");
            w.Open($"public partial record struct {s.CsName}");
            bool needsCtor = false;
            foreach (FieldPlan m in s.Members)
            {
                string type = _plan.Namer.Write(m.CsType, s.CsNamespace, null, nullable: true);
                string? init = m.CsType switch
                {
                    CsKeyword { Keyword: "string" } => "\"\"",
                    CsList => "new()",
                    _ => null,
                };
                needsCtor |= init is not null;
                w.Line($"public {type} {m.CsName}{(init is null ? string.Empty : " = " + init)}; // {Comment(m)}");
            }

            if (needsCtor)
            {
                w.Line();
                w.Open($"public {s.CsName}()");
                w.Close();
            }

            w.Close();
        }

        return w.ToString();
    }

    private string FixedArraysFile()
    {
        CodeWriter w = Begin(TypeNamer.UcNamespace);
        bool first = true;
        foreach (int n in _plan.InlineArraySizes)
        {
            if (!first)
            {
                w.Line();
            }

            first = false;
            string name = "FixedArray" + n.ToString(CultureInfo.InvariantCulture);
            string len = n.ToString(CultureInfo.InvariantCulture);
            w.Line($"/// <summary>A fixed-size inline array of {len} elements (UnrealScript static arrays inside structs).</summary>");
            w.Line($"[global::System.Runtime.CompilerServices.InlineArray({len})]");
            w.Open($"public struct {name}<T> : global::System.IEquatable<{name}<T>>");
            w.Line("private T _element0;");
            w.Line();
            w.Line($"public const int Length = {len};");
            w.Line();
            w.Line($"public static bool operator ==({name}<T> a, {name}<T> b) => a.Equals(b);");
            w.Line();
            w.Line($"public static bool operator !=({name}<T> a, {name}<T> b) => !a.Equals(b);");
            w.Line();
            w.Open($"public readonly bool Equals({name}<T> other)");
            w.Open("for (int i = 0; i < Length; i++)");
            w.Open("if (!EqualityComparer<T>.Default.Equals(this[i], other[i]))");
            w.Line("return false;");
            w.Close();
            w.Close();
            w.Line();
            w.Line("return true;");
            w.Close();
            w.Line();
            w.Line($"public override readonly bool Equals(object? obj) => obj is {name}<T> other && Equals(other);");
            w.Line();
            w.Line("// A fixed combination (System.HashCode is seeded per process).");
            w.Open("public override readonly int GetHashCode()");
            w.Line("int hash = 17;");
            w.Open("for (int i = 0; i < Length; i++)");
            w.Line("hash = unchecked((hash * 31) + EqualityComparer<T>.Default.GetHashCode(this[i]!));");
            w.Close();
            w.Line();
            w.Line("return hash;");
            w.Close();
            w.Close();
        }

        return w.ToString();
    }
}
