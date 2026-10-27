using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.CodeGen.Emit;
using OpenScct.CodeGen.HandWritten;
using OpenScct.CodeGen.Model;

namespace OpenScct.CodeGen.Plan;

/// <summary>
/// Decides what to generate: which classes, enums and structs, their C# names and namespaces, the C# type of every
/// property, and which members already exist by hand. Everything is ordered so the output never depends on input
/// order or hashing.
/// </summary>
internal sealed class Planner
{
    private readonly UcModel _model;
    private readonly HandWrittenCode _hand;
    private readonly GeneratorOptions _options;
    private readonly Report _report;
    private readonly Dictionary<string, PackageTarget> _targets;

    public Planner(UcModel model, HandWrittenCode hand, GeneratorOptions options, Report report)
    {
        _model = model;
        _hand = hand;
        _options = options;
        _report = report;
        _targets = options.Targets.ToDictionary(t => t.Package, StringComparer.Ordinal);
    }

    public SortedDictionary<string, ClassPlan> Classes { get; } = new(StringComparer.Ordinal);

    public SortedDictionary<string, EnumPlan> Enums { get; } = new(StringComparer.Ordinal);

    public SortedDictionary<string, StructPlan> Structs { get; } = new(StringComparer.Ordinal);

    /// <summary>Static array sizes used inside structs (FixedArrayN types to generate).</summary>
    public SortedSet<int> InlineArraySizes { get; } = new();

    public TypeNamer Namer { get; private set; } = null!;

    public PackageTarget? TargetOf(string package) => _targets.GetValueOrDefault(package);

    public void Run()
    {
        SelectTypes();
        NameTypes();
        BuildNamer();
        LinkClasses();
        foreach (StructPlan s in Structs.Values)
        {
            PlanStructMembers(s);
        }

        foreach (ClassPlan c in Classes.Values.OrderBy(c => c.Depth).ThenBy(c => c.Class.Path, StringComparer.Ordinal))
        {
            PlanClass(c);
        }

        CheckHandWrittenDescendants();
        foreach (ClassPlan c in Classes.Values)
        {
            BuildShadowing(c);
        }
    }

    // ---- selection -------------------------------------------------------------------------------------------

    private void SelectTypes()
    {
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        var enums = new SortedSet<string>(StringComparer.Ordinal);
        var structs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (UcClass c in _model.Classes.Values.Where(c => c.IsScript && _targets.ContainsKey(c.Package)))
        {
            classes.Add(c.Path);
            foreach (string e in c.EnumNames)
            {
                string path = c.Path + "." + e;
                if (_model.Enums.ContainsKey(path))
                {
                    enums.Add(path);
                }
                else
                {
                    _report.Add("Missing data", $"enum {path} is declared but has no reflection record; not generated");
                }
            }

            foreach (string s in c.StructNames)
            {
                string path = c.Path + "." + s;
                if (_model.Structs.ContainsKey(path))
                {
                    structs.Add(path);
                }
                else
                {
                    _report.Add("Missing data", $"struct {path} is declared but has no reflection record; not generated");
                }
            }
        }

        // Closure over referenced types: intrinsic classes (with their supers), structs and enums.
        bool changed = true;
        while (changed)
        {
            changed = false;
            var types = new List<UType>();
            foreach (string cp in classes)
            {
                types.AddRange(_model.Classes[cp].Vars.Select(v => v.Type));
            }

            foreach (string sp in structs.ToList())
            {
                UcStructDef s = _model.Structs[sp];
                types.AddRange(s.Members.Select(m => m.Type));
                if (s.SuperPath is not null && _model.Structs.ContainsKey(s.SuperPath) && structs.Add(s.SuperPath))
                {
                    changed = true;
                }
            }

            foreach (UType t in types)
            {
                changed |= Reference(t, classes, enums, structs);
            }
        }

        foreach (string cp in classes)
        {
            UcClass c = _model.Classes[cp];
            if (!c.IsScript)
            {
                _report.Add("Intrinsic classes generated as field stubs", cp);
            }
        }

        _selectedClasses = classes;
        _selectedEnums = enums;
        _selectedStructs = structs;
    }

    private SortedSet<string> _selectedClasses = new(StringComparer.Ordinal);
    private SortedSet<string> _selectedEnums = new(StringComparer.Ordinal);
    private SortedSet<string> _selectedStructs = new(StringComparer.Ordinal);

    private bool Reference(UType t, SortedSet<string> classes, SortedSet<string> enums, SortedSet<string> structs)
    {
        switch (t)
        {
            case UArray a:
                return Reference(a.Inner, classes, enums, structs);
            case UEnum e when _model.Enums.ContainsKey(e.EnumPath) && _targets.ContainsKey(PackageOfPath(e.EnumPath)):
                return enums.Add(e.EnumPath);
            case UStruct s when _model.Structs.ContainsKey(s.StructPath) && _targets.ContainsKey(PackageOfPath(s.StructPath)):
                return structs.Add(s.StructPath);
            case UObjectRef o:
            {
                // The referenced class itself must be a non-content class of a generated package; its supers are
                // generated whatever they are, so the C# hierarchy stays whole.
                UcClass? referenced = _model.FindClass(o.ClassPath);
                if (referenced is null || !_targets.ContainsKey(referenced.Package) || IsAsset(referenced.Path) ||
                    referenced.Path == "Core.Class")
                {
                    return false;
                }

                bool changed = false;
                for (UcClass? c = referenced; c is not null; c = c.SuperPath is null ? null : _model.FindClass(c.SuperPath))
                {
                    if (!_targets.ContainsKey(c.Package) || c.Path == _options.RootClass)
                    {
                        break;
                    }

                    changed |= classes.Add(c.Path);
                }

                return changed;
            }

            default:
                return false;
        }
    }

    private static string PackageOfPath(string path) => path[..path.IndexOf('.', StringComparison.Ordinal)];

    public bool IsAsset(string classPath)
    {
        if (_options.AssetExceptions.Any(e => _model.IsA(classPath, e)))
        {
            return false;
        }

        return _options.AssetRoots.Any(r => _model.IsA(classPath, r));
    }

    public bool IsActorClass(string classPath) => _model.IsA(classPath, _options.ActorClass);

    // ---- names -----------------------------------------------------------------------------------------------

    private void NameTypes()
    {
        foreach (string cp in _selectedClasses)
        {
            UcClass c = _model.Classes[cp];
            bool actor = IsActorClass(cp);
            string name = cp == _options.RootClass ? "UObject" : actor ? c.Name : "U" + c.Name;
            Classes[cp] = new ClassPlan
            {
                Class = c,
                Target = _targets[c.Package],
                CsName = name,
                IsActor = actor,
                IsRoot = cp == _options.RootClass,
            };
        }

        foreach (string ep in _selectedEnums)
        {
            UcEnumDef e = _model.Enums[ep];
            PackageTarget target = _targets[e.Package];
            HandType? hand = _hand.Find(target.Namespace, e.Name);
            if (hand is not null)
            {
                CheckHandEnum(e, hand);
            }

            Enums[ep] = new EnumPlan { Def = e, Target = target, CsName = e.Name, HandWritten = hand is not null };
        }

        foreach (string sp in _selectedStructs)
        {
            UcStructDef s = _model.Structs[sp];
            PackageTarget target = _targets[s.Package];
            if (_options.MappedStructs.TryGetValue(sp, out string? mapped))
            {
                int dot = mapped.LastIndexOf('.');
                string ns = mapped[..dot];
                string name = mapped[(dot + 1)..];
                HandType? hand = _hand.Find(ns, name);
                if (hand is null)
                {
                    _report.Add("Errors", $"struct {sp} maps to {mapped}, which the runtime does not declare");
                }
                else
                {
                    var expected = _model.AllStructMembers(s).Select(m => m.Name).ToList();
                    var actual = hand.Members.Where(m => m.Kind == HandMemberKind.Field && !m.IsStatic).Select(m => m.Name).ToList();
                    if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                    {
                        _report.Add("Errors", $"struct {sp} has members {string.Join(", ", expected)} but {mapped} declares {string.Join(", ", actual)}");
                    }
                }

                Structs[sp] = new StructPlan { Def = s, Target = target, CsNamespace = ns, CsName = name, HandWritten = true, Mapped = true };
                continue;
            }

            string csName = "F" + s.Name;
            bool existing = _hand.Find(target.Namespace, csName) is not null;
            if (existing)
            {
                _report.Add("Hand-written types used instead of generated ones", $"{target.Namespace}.{csName} for struct {sp}");
            }

            Structs[sp] = new StructPlan { Def = s, Target = target, CsNamespace = target.Namespace, CsName = csName, HandWritten = existing };
        }

        // Duplicate C# names within a namespace (also ignoring case: the files would clash on Windows).
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<(string Ns, string Name, string What)> all =
            Classes.Values.Select(c => (c.Target.Namespace, c.CsName, c.Class.Path))
                .Concat(Enums.Values.Where(e => !e.HandWritten).Select(e => (e.Target.Namespace, e.CsName, e.Def.Path)))
                .Concat(Structs.Values.Where(s => !s.HandWritten).Select(s => (s.CsNamespace, s.CsName, s.Def.Path)));
        foreach ((string ns, string name, string what) in all)
        {
            string key = ns + "." + name;
            if (seen.TryGetValue(key, out string? other))
            {
                _report.Add("Errors", $"{what} and {other} both map to {key}");
            }
            else
            {
                seen[key] = what;
            }
        }
    }

    private void CheckHandEnum(UcEnumDef e, HandType hand)
    {
        if (hand.Kind != "enum")
        {
            _report.Add("Errors", $"enum {e.Path}: the runtime declares {hand.FullName} as a {hand.Kind}");
            return;
        }

        bool ok = hand.EnumMembers.Count == e.Members.Count;
        for (int i = 0; ok && i < e.Members.Count; i++)
        {
            (string name, string? value) = hand.EnumMembers[i];
            ok = name == e.Members[i] && (value is null || value.Trim() == i.ToString(CultureInfo.InvariantCulture));
        }

        if (!ok)
        {
            _report.Add("Errors", $"enum {e.Path}: hand-written {hand.FullName} does not match the original members");
        }
        else
        {
            _report.Add("Hand-written types used instead of generated ones", $"{hand.FullName} for enum {e.Path}");
        }
    }

    private void BuildNamer()
    {
        var known = new List<(string, string)>();
        foreach (HandType t in _hand.Types.Values)
        {
            known.Add((t.Namespace, t.Name));
        }

        known.AddRange(Classes.Values.Select(c => (c.Target.Namespace, c.CsName)));
        known.AddRange(Enums.Values.Select(e => (e.Target.Namespace, e.CsName)));
        known.AddRange(Structs.Values.Select(s => (s.CsNamespace, s.CsName)));
        known.AddRange(_options.Targets.Select(t => (t.Namespace, t.Package + "Package")));
        Namer = new TypeNamer(known);
    }

    private void LinkClasses()
    {
        foreach (ClassPlan c in Classes.Values)
        {
            if (c.Class.SuperPath is { } sp)
            {
                // A super class outside the generated packages (for example IpDrv.UdpLink) is replaced by its
                // nearest generated ancestor.
                for (string? p = sp; p is not null; p = _model.FindClass(p)?.SuperPath)
                {
                    if (Classes.TryGetValue(p, out ClassPlan? super))
                    {
                        c.Super = super;
                        if (p != sp)
                        {
                            _report.Add("Super class fallbacks", $"{c.Class.Path}: extends {sp}, which is not generated; derives from {p}");
                        }

                        break;
                    }
                }

                if (c.Super is null)
                {
                    _report.Add("Errors", $"class {c.Class.Path}: no generated ancestor for super class {sp}");
                }
            }

            c.Hand = _hand.Find(c.Target.Namespace, c.CsName);
            if (c.Hand is null)
            {
                continue;
            }

            if (c.Hand.Kind != "class")
            {
                _report.Add("Errors", $"class {c.Class.Path}: the runtime declares {c.Hand.FullName} as a {c.Hand.Kind}");
            }
            else if (!c.Hand.IsPartial)
            {
                _report.Add("Errors", $"class {c.Class.Path}: hand-written {c.Hand.FullName} must be declared partial");
            }

            _report.Add("Classes with a hand-written part", $"{c.Class.Path} ({c.Hand.FullName})");
        }

        // Only the generated part declares the base, unless the hand-written part already names one.
        foreach (ClassPlan c in Classes.Values)
        {
            if (c.Hand?.BaseText is { } baseText && c.Super is not null &&
                UType.ShortName(baseText.Replace("global::", string.Empty, StringComparison.Ordinal)) != c.Super.CsName)
            {
                _report.Add("Errors", $"class {c.Class.Path}: hand-written base {baseText} differs from {c.Super.CsName}");
            }
        }
    }

    // ---- type mapping ----------------------------------------------------------------------------------------

    /// <summary>The C# type of a property type and its UcPropertyKind name.</summary>
    public (CsType Type, string Kind) Map(UType t, string where)
    {
        switch (t)
        {
            case UBool:
                return (CsKeyword.Bool, "Bool");
            case UByte:
                return (CsKeyword.Byte, "Byte");
            case UInt:
                return (CsKeyword.Int, "Int");
            case UFloat:
                return (CsKeyword.Float, "Float");
            case UName:
                return (new CsNamed("OpenScct.Core", "Name", false, "struct"), "Name");
            case UString:
                return (CsKeyword.String, "String");
            case UEnum e:
                if (Enums.TryGetValue(e.EnumPath, out EnumPlan? ep))
                {
                    return (ep.Type, "Enum");
                }

                _report.Add("Type fallbacks", $"{where}: enum {e.EnumPath} is not generated; mapped as byte");
                return (CsKeyword.Byte, "Byte");
            case UStruct s:
                if (Structs.TryGetValue(s.StructPath, out StructPlan? sp))
                {
                    return (sp.Type, "Struct");
                }

                throw new InvalidOperationException($"{where}: struct {s.StructPath} is outside the generated packages.");
            case UObjectRef o:
                return MapObject(o.ClassPath, where);
            case UClassRef:
                return (new CsNamed(TypeNamer.UcNamespace, "ClassRef", false, "struct"), "Class");
            case UDelegate:
                return (new CsNamed(TypeNamer.UcNamespace, "DelegateRef", false, "struct"), "Delegate");
            case UArray a:
            {
                (CsType inner, _) = Map(a.Inner, where);
                return (new CsList(inner), "Array");
            }

            default:
                throw new InvalidOperationException($"{where}: unresolved type {t}.");
        }
    }

    private (CsType, string) MapObject(string classPath, string where)
    {
        if (IsAsset(classPath))
        {
            return (new CsNamed(TypeNamer.UcNamespace, "AssetRef", false, "struct"), "Asset");
        }

        for (string? p = classPath; p is not null; p = _model.FindClass(p)?.SuperPath)
        {
            if (Classes.TryGetValue(p, out ClassPlan? c))
            {
                if (p != classPath)
                {
                    _report.Add("Type fallbacks", $"{where}: class {classPath} is not generated; typed as its ancestor {c.Class.Path}");
                }

                return (c.Type, "Object");
            }
        }

        _report.Add("Type fallbacks", $"{where}: class {classPath} is unknown; typed as UObject");
        return (Classes[_options.RootClass].Type, "Object");
    }

    private FieldPlan PlanVar(UcVar v, bool inStruct, string where)
    {
        (CsType element, string kind) = Map(v.Type, where);
        CsType type = element;
        if (v.ArrayDim > 1)
        {
            if (inStruct)
            {
                type = new CsInlineArray(v.ArrayDim, element);
                InlineArraySizes.Add(v.ArrayDim);
            }
            else
            {
                type = new CsArray(element);
            }
        }

        return new FieldPlan { Var = v, CsName = CSharp.Ident(v.Name), CsType = type, Kind = kind };
    }

    private void PlanStructMembers(StructPlan s)
    {
        foreach (UcVar m in _model.AllStructMembers(s.Def))
        {
            FieldPlan f = PlanVar(m, inStruct: true, s.Def.Path + "." + m.Name);
            if (f.CsName == s.CsName)
            {
                f.CsName += "_";
                _report.Add("Renamed members", $"{s.Def.Path}.{m.Name} -> {f.CsName} (same name as its struct)");
            }

            s.Members.Add(f);
        }
    }

    // ---- classes ---------------------------------------------------------------------------------------------

    private void PlanClass(ClassPlan c)
    {
        _options.SkipVars.TryGetValue(c.Class.Path, out string[]? skip);
        HashSet<string> inherited = InheritedMemberNames(c);
        foreach (UcVar v in c.Class.Vars)
        {
            string where = c.Class.Path + "." + v.Name;
            FieldPlan f = PlanVar(v, inStruct: false, where);
            c.Fields.Add(f);
            if (skip is not null && skip.Contains(v.Name, StringComparer.Ordinal))
            {
                f.Generated = false;
                f.NoMember = true;
                f.Assignable = false;
                f.SkipReason = "object internal kept by the runtime";
                _report.Add("Skipped script properties", $"{where}: {f.SkipReason}");
                continue;
            }

            if (FindHand(c, v.Name) is { } hand)
            {
                Bind(c, f, hand.Owner, hand.Member, where);
                continue;
            }

            if (f.CsName == c.CsName)
            {
                f.CsName += "_";
                _report.Add("Renamed members", $"{where} -> {f.CsName} (same name as its class)");
            }
            else if (c.Hand is not null && c.Hand.Named(f.CsName, StringComparison.Ordinal).Any())
            {
                f.CsName += "_";
                _report.Add("Renamed members", $"{where} -> {f.CsName} (the hand-written part has a member of that name)");
            }

            f.New = inherited.Contains(f.CsName);
            if (f.New)
            {
                _report.Add("Members declared new (hide an inherited member)", where);
            }

            f.Initializer = Initializer(f.CsType, c);
        }

        foreach (UcConst k in c.Class.Consts)
        {
            string where = c.Class.Path + "." + k.Name;
            if (FindHand(c, k.Name) is not null)
            {
                _report.Add("Skipped consts", $"{where}: declared by hand");
                continue;
            }

            if (c.Class.Vars.Any(v => string.Equals(v.Name, k.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _report.Add("Skipped consts", $"{where}: same name as a property");
                continue;
            }

            string? decl = ConstDeclaration(k, CSharp.Ident(k.Name));
            if (decl is null)
            {
                _report.Add("Skipped consts", $"{where}: value '{k.ValueText}' has no C# form");
                continue;
            }

            c.Consts.Add(new ConstPlan { Name = k.Name, CsName = CSharp.Ident(k.Name), Declaration = decl, New = inherited.Contains(k.Name) });
        }

        if (c.Hand is not null)
        {
            c.EmitSetDefaults = !c.Hand.Named("SetDefaults", StringComparison.Ordinal).Any();
            c.EmitGetUcClass = !c.Hand.Named("GetUcClass", StringComparison.Ordinal).Any();
        }

        if (c.IsRoot)
        {
            c.EmitSetDefaults = false;
            c.EmitGetUcClass = false;
        }

        HandMember? ctor = c.Hand?.ParameterlessConstructor;
        bool hasOtherCtors = c.Hand?.Members.Any(m => m.Kind == HandMemberKind.Constructor && !m.IsStatic && m.ParameterCount > 0) ?? false;
        bool publicCtor = ctor is null ? !hasOtherCtors : ctor.Accessibility == "public";
        c.Factory = !c.IsAbstract && publicCtor && !c.IsRoot ? $"static () => new {c.CsName}()" : null;
        if (c.Factory is null && !c.IsAbstract && !c.IsRoot)
        {
            _report.Add("Classes without a factory (no public parameterless constructor)", c.Class.Path);
        }
    }

    /// <summary>
    /// The hand-written field or property a script property binds to: one of the same name (ASCII case ignored) on
    /// the class itself, or on an ancestor when no script class in between declares that name (the hand-written
    /// member was placed higher than the script declares it). A script property that shadows an ancestor's
    /// property of the same name is its own member and does not bind.
    /// </summary>
    private static (ClassPlan Owner, HandMember Member)? FindHand(ClassPlan c, string name)
    {
        foreach (ClassPlan x in c.Chain())
        {
            if (!ReferenceEquals(x, c) && x.Class.Vars.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            HandMember? m = x.Hand?.Named(name, StringComparison.OrdinalIgnoreCase)
                .FirstOrDefault(m => m.Kind is HandMemberKind.Field or HandMemberKind.Property or HandMemberKind.Const);
            if (m is not null)
            {
                return (x, m);
            }
        }

        return null;
    }

    private void Bind(ClassPlan c, FieldPlan f, ClassPlan owner, HandMember m, string where)
    {
        f.Generated = false;
        f.Hand = m;
        f.HandOwner = owner;
        f.CsName = m.Name;
        if (!ReferenceEquals(owner, c))
        {
            _report.Add("Hand-written members declared on a different class than the script", $"{where}: hand-written on {owner.Class.Path}");
        }

        if (m.Name != f.Var.Name)
        {
            _report.Add("Hand-written members with different case", $"{where}: hand-written as {m.Name}");
        }

        string why = string.Empty;
        bool assignable = m.Kind switch
        {
            HandMemberKind.Field => !m.IsStatic && (!m.IsReadOnly || f.CsType is CsList or CsArray),
            HandMemberKind.Property => !m.IsStatic && m.SetterAccessibility is not null && !m.SetterIsInit,
            _ => false,
        };
        if (!assignable)
        {
            why = $"a {m.Kind.ToString().ToLowerInvariant()} that cannot be assigned";
        }
        else
        {
            string access = m.Kind == HandMemberKind.Property ? m.SetterAccessibility! : m.Accessibility;
            bool sameClass = ReferenceEquals(owner, c);
            bool sameAssembly = string.Equals(m.Assembly, c.Target.Assembly, StringComparison.Ordinal);
            assignable = access switch
            {
                "public" or "protected" or "protected internal" => true,
                "internal" => sameAssembly,
                "private protected" => sameAssembly,
                _ => sameClass,
            };
            if (!assignable)
            {
                why = $"its {access} accessor is not reachable from {c.CsName}";
            }
        }

        if (assignable && !SameType(m.TypeText, f.CsType))
        {
            assignable = false;
            why = $"its type {m.TypeText} differs from {Namer.Write(f.CsType, "OpenScct")}";
        }

        f.Assignable = assignable;
        f.SkipReason = assignable ? "declared by hand" : "declared by hand; defaults not assignable: " + why;
        _report.Add("Script properties declared by hand", $"{where} ({m.Kind.ToString().ToLowerInvariant()} {m.TypeText}{(assignable ? string.Empty : "; defaults not applied: " + why)})");
    }

    private bool SameType(string? handText, CsType generated)
    {
        if (handText is null)
        {
            return false;
        }

        return Normalize(handText) == Normalize(Namer.Write(generated, "OpenScct"));
    }

    private static string Normalize(string typeText)
    {
        string t = typeText.Replace("global::", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("?", string.Empty, StringComparison.Ordinal);
        var sb = new System.Text.StringBuilder();
        int start = 0;
        for (int i = 0; i <= t.Length; i++)
        {
            if (i == t.Length || t[i] is '<' or '>' or ',' or '[' or ']')
            {
                string segment = t[start..i];
                int dot = segment.LastIndexOf('.');
                sb.Append(dot >= 0 ? segment[(dot + 1)..] : segment);
                if (i < t.Length)
                {
                    sb.Append(t[i]);
                }

                start = i + 1;
            }
        }

        return sb.ToString();
    }

    private string? Initializer(CsType type, ClassPlan c)
    {
        switch (type)
        {
            case CsKeyword { Keyword: "string" }:
                return "\"\"";
            case CsList:
                return "new()";
            case CsArray { Element: CsKeyword { Keyword: "string" } } a:
                _ = a;
                return Namer.Name(TypeNamer.UcNamespace, "UcDefaults", c.Target.Namespace, c.Shadowing) + ".Strings(" + "{dim}" + ")";
            case CsArray a:
                return "new " + Namer.Write(a.Element, c.Target.Namespace, c.TypeShadowing, nullable: true) + "[{dim}]";
            default:
                return null;
        }
    }

    private static string? ConstDeclaration(UcConst k, string csName)
    {
        string v = k.ValueText.Trim();
        if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'')
        {
            return $"static readonly Name {csName} = new({CSharp.String(v[1..^1])})";
        }

        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
        {
            return $"const string {csName} = {CSharp.String(v[1..^1])}";
        }

        if (v is "true" or "True" or "false" or "False")
        {
            return $"const bool {csName} = {v.ToLowerInvariant()}";
        }

        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
        {
            return hex <= int.MaxValue
                ? $"const int {csName} = 0x{hex.ToString("X8", CultureInfo.InvariantCulture)}"
                : $"const int {csName} = unchecked((int)0x{hex.ToString("X8", CultureInfo.InvariantCulture)})";
        }

        if (int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int i))
        {
            return $"const int {csName} = {CSharp.Int(i)}";
        }

        string f = v.EndsWith('f') || v.EndsWith('F') ? v[..^1] : v;
        if (f.EndsWith('.'))
        {
            f += "0";
        }

        if (f.Length > 0 && (f.Contains('.', StringComparison.Ordinal) || f.Contains('e', StringComparison.OrdinalIgnoreCase)) &&
            float.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            return $"const float {csName} = {CSharp.Float(value)}";
        }

        return null;
    }

    /// <summary>Names declared in ancestors (generated or hand-written), which a new member of the same name would hide.</summary>
    private HashSet<string> InheritedMemberNames(ClassPlan c)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (ClassPlan? x = c.Super; x is not null; x = x.Super)
        {
            foreach (FieldPlan f in x.Fields)
            {
                if (f.Generated)
                {
                    names.Add(f.CsName);
                }
            }

            foreach (ConstPlan k in x.Consts)
            {
                names.Add(k.CsName);
            }

            if (x.Hand is not null)
            {
                foreach (HandMember m in x.Hand.Members)
                {
                    if (m.Kind != HandMemberKind.Constructor)
                    {
                        names.Add(m.Name);
                    }
                }
            }
        }

        return names;
    }

    /// <summary>
    /// A hand-written member of a class hides a generated member of an ancestor: that would be a warning (an error
    /// here) in hand-written code, so the ancestor's member is not generated and the report says so.
    /// </summary>
    private void CheckHandWrittenDescendants()
    {
        foreach (ClassPlan c in Classes.Values)
        {
            if (c.Hand is null)
            {
                continue;
            }

            foreach (HandMember m in c.Hand.Members)
            {
                if (m.Kind == HandMemberKind.Constructor || m.IsOverride || m.IsNew)
                {
                    continue;
                }

                for (ClassPlan? a = c.Super; a is not null; a = a.Super)
                {
                    FieldPlan? f = a.Fields.FirstOrDefault(f => f.Generated && f.CsName == m.Name);
                    if (f is not null)
                    {
                        f.Generated = false;
                        f.Assignable = false;
                        f.NoMember = true;
                        f.SkipReason = $"hidden by hand-written {c.Hand.FullName}.{m.Name}";
                        _report.Add("Errors", $"{a.Class.Path}.{f.Var.Name} not generated: hand-written {c.Hand.FullName}.{m.Name} would hide it");
                    }

                    ConstPlan? k = a.Consts.FirstOrDefault(k => k.CsName == m.Name);
                    if (k is not null)
                    {
                        a.Consts.Remove(k);
                        _report.Add("Errors", $"{a.Class.Path} const {k.Name} not generated: hand-written {c.Hand.FullName}.{m.Name} would hide it");
                    }
                }
            }
        }
    }

    private void BuildShadowing(ClassPlan c)
    {
        foreach (ClassPlan x in c.Chain())
        {
            foreach (FieldPlan f in x.Fields.Where(f => !f.NoMember))
            {
                c.Shadowing.Add(f.CsName);
            }

            foreach (ConstPlan k in x.Consts)
            {
                c.Shadowing.Add(k.CsName);
            }

            foreach (UcFunc fn in x.Class.Functions)
            {
                c.Shadowing.Add(fn.Name);
            }

            foreach (UcStateDef s in x.Class.States)
            {
                c.Shadowing.Add(s.Name);
                c.TypeShadowing.Add(s.Name);
                foreach (UcFunc fn in s.Functions)
                {
                    c.Shadowing.Add(fn.Name);
                }
            }

            if (x.Hand is not null)
            {
                foreach (HandMember m in x.Hand.Members.Where(m => m.Kind != HandMemberKind.Constructor))
                {
                    c.Shadowing.Add(m.Name);
                    if (m.Kind == HandMemberKind.NestedType)
                    {
                        c.TypeShadowing.Add(m.Name);
                    }
                }
            }

            c.Shadowing.Add("UcMeta");
            c.TypeShadowing.Add("UcMeta");
        }

        foreach (string s in c.TypeShadowing)
        {
            c.Shadowing.Add(s);
        }

        // Initializers were planned before shadowing was known; recompute them now.
        foreach (FieldPlan f in c.Fields.Where(f => f.Generated))
        {
            string? init = Initializer(f.CsType, c);
            f.Initializer = init?.Replace("{dim}", f.Var.ArrayDim.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
