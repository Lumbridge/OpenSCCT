using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OpenScct.CodeGen.HandWritten;

internal enum HandMemberKind
{
    Field,
    Const,
    Property,
    Method,
    Event,
    NestedType,
    Constructor,
}

/// <summary>A member declared in hand-written code.</summary>
internal sealed class HandMember
{
    public required string Name { get; init; }

    public HandMemberKind Kind { get; init; }

    /// <summary>Type text as written (fields, properties, consts), or null.</summary>
    public string? TypeText { get; init; }

    public bool IsStatic { get; init; }

    public bool IsReadOnly { get; init; }

    public bool HasInitializer { get; init; }

    /// <summary>public, protected, internal, private, protected internal, private protected.</summary>
    public string Accessibility { get; init; } = "private";

    /// <summary>Accessibility of a property's setter or init accessor, or null without one.</summary>
    public string? SetterAccessibility { get; init; }

    public bool SetterIsInit { get; init; }

    public int ParameterCount { get; init; }

    /// <summary>True when the member is declared with override.</summary>
    public bool IsOverride { get; init; }

    /// <summary>True when the member is declared with new.</summary>
    public bool IsNew { get; init; }

    /// <summary>The project folder the declaring file is in (OpenScct.Core, OpenScct.Game...).</summary>
    public string Assembly { get; init; } = string.Empty;
}

/// <summary>One hand-written type (all partial parts merged).</summary>
internal sealed class HandType
{
    public required string Namespace { get; init; }

    public required string Name { get; init; }

    public string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;

    /// <summary>class, struct, record, record struct, interface or enum.</summary>
    public string Kind { get; set; } = "class";

    public bool IsPartial { get; set; }

    public bool IsAbstract { get; set; }

    public bool IsSealed { get; set; }

    public bool IsStatic { get; set; }

    /// <summary>The first base list entry as written (base class or interface), or null.</summary>
    public string? BaseText { get; set; }

    public List<HandMember> Members { get; } = new();

    /// <summary>Enum members with their value text (null when implicit).</summary>
    public List<(string Name, string? Value)> EnumMembers { get; } = new();

    /// <summary>Simple assignments in constructor bodies: member and value text.</summary>
    public List<(string Member, string Value)> ConstructorAssignments { get; } = new();

    public List<string> Files { get; } = new();

    public IEnumerable<HandMember> Named(string name, StringComparison comparison) =>
        Members.Where(m => m.Kind != HandMemberKind.Constructor && string.Equals(m.Name, name, comparison));

    /// <summary>The parameterless constructor, or null when the type declares none.</summary>
    public HandMember? ParameterlessConstructor =>
        Members.FirstOrDefault(m => m.Kind == HandMemberKind.Constructor && m.ParameterCount == 0 && !m.IsStatic);

    public override string ToString() => FullName;
}

/// <summary>
/// The hand-written C# of the runtime, read as syntax only (no compilation, so it works while generated code is
/// stale or broken). The generator uses it to skip members that already exist and to find name clashes.
/// </summary>
internal sealed class HandWrittenCode
{
    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview, DocumentationMode.None);

    public Dictionary<string, HandType> Types { get; } = new(StringComparer.Ordinal);

    /// <summary>Reads every .cs file under the roots, skipping bin, obj and Generated folders.</summary>
    public static HandWrittenCode Load(IEnumerable<string> roots)
    {
        var code = new HandWrittenCode();
        var files = new List<string>();
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                string[] parts = rel.Split('/');
                if (parts.Any(p => p is "bin" or "obj" or "Generated"))
                {
                    continue;
                }

                files.Add(file);
            }
        }

        files.Sort(StringComparer.Ordinal);
        foreach (string file in files)
        {
            code.AddSource(File.ReadAllText(file), file);
        }

        return code;
    }

    public void AddSource(string text, string path)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, Options, path);
        var root = (CompilationUnitSyntax)tree.GetRoot();
        foreach (MemberDeclarationSyntax member in root.Members)
        {
            Visit(member, string.Empty, path);
        }
    }

    public HandType? Find(string ns, string name) => Types.GetValueOrDefault(ns.Length == 0 ? name : ns + "." + name);

    private void Visit(MemberDeclarationSyntax member, string ns, string path)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax n:
            {
                string inner = ns.Length == 0 ? n.Name.ToString() : ns + "." + n.Name;
                foreach (MemberDeclarationSyntax m in n.Members)
                {
                    Visit(m, inner, path);
                }

                break;
            }

            case BaseTypeDeclarationSyntax t:
                AddType(t, ns, path);
                break;
        }
    }

    private void AddType(BaseTypeDeclarationSyntax t, string ns, string path)
    {
        string name = t.Identifier.Text;
        string full = ns.Length == 0 ? name : ns + "." + name;
        if (!Types.TryGetValue(full, out HandType? type))
        {
            Types[full] = type = new HandType { Namespace = ns, Name = name };
        }

        type.Files.Add(path);
        type.Kind = t switch
        {
            RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => "record struct",
            RecordDeclarationSyntax => "record",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            EnumDeclarationSyntax => "enum",
            _ => "class",
        };
        SyntaxTokenList mods = t.Modifiers;
        type.IsPartial |= mods.Any(SyntaxKind.PartialKeyword);
        type.IsAbstract |= mods.Any(SyntaxKind.AbstractKeyword);
        type.IsSealed |= mods.Any(SyntaxKind.SealedKeyword);
        type.IsStatic |= mods.Any(SyntaxKind.StaticKeyword);
        if (t.BaseList is { Types.Count: > 0 } bases && type.BaseText is null)
        {
            // A hand-written partial may add interfaces (IName by convention); only a class base matters here.
            foreach (BaseTypeSyntax b in bases.Types)
            {
                string text = b.Type.ToString();
                if (!LooksLikeInterface(text))
                {
                    type.BaseText = text;
                    break;
                }
            }
        }

        if (t is EnumDeclarationSyntax e)
        {
            foreach (EnumMemberDeclarationSyntax m in e.Members)
            {
                type.EnumMembers.Add((m.Identifier.Text, m.EqualsValue?.Value.ToString()));
            }

            return;
        }

        if (t is not TypeDeclarationSyntax td)
        {
            return;
        }

        if (td is RecordDeclarationSyntax { ParameterList: { } recordParams })
        {
            foreach (ParameterSyntax p in recordParams.Parameters)
            {
                type.Members.Add(new HandMember
                {
                    Name = p.Identifier.Text,
                    Kind = HandMemberKind.Property,
                    TypeText = p.Type?.ToString(),
                    Accessibility = "public",
                    SetterAccessibility = "public",
                    SetterIsInit = true,
                });
            }
        }

        foreach (MemberDeclarationSyntax m in td.Members)
        {
            AddMember(type, m, AssemblyOf(path));
        }
    }

    /// <summary>The project a source file belongs to: the folder after <c>src/</c> (or <c>tests/</c>).</summary>
    public static string AssemblyOf(string path)
    {
        string[] parts = path.Replace("\\", "/", StringComparison.Ordinal).Split('/');
        for (int i = 0; i + 1 < parts.Length; i++)
        {
            if (parts[i] is "src" or "tests" or "tools")
            {
                return parts[i + 1];
            }
        }

        return string.Empty;
    }

    private static void AddMember(HandType type, MemberDeclarationSyntax m, string assembly)
    {
        // Explicit interface implementations (ref FVector IFoo.ZipP1 => ref ZipP1) expose a field the class already
        // has; they do not declare a member of the class, so the generated field must stay.
        if (m is BasePropertyDeclarationSyntax { ExplicitInterfaceSpecifier: not null } ||
            m is MethodDeclarationSyntax { ExplicitInterfaceSpecifier: not null })
        {
            return;
        }

        string access = Access(m.Modifiers, "private");
        bool isStatic = m.Modifiers.Any(SyntaxKind.StaticKeyword);
        bool isOverride = m.Modifiers.Any(SyntaxKind.OverrideKeyword);
        bool isNew = m.Modifiers.Any(SyntaxKind.NewKeyword);
        switch (m)
        {
            case FieldDeclarationSyntax f:
            {
                bool isConst = f.Modifiers.Any(SyntaxKind.ConstKeyword);
                foreach (VariableDeclaratorSyntax v in f.Declaration.Variables)
                {
                    type.Members.Add(new HandMember
                    {
                        Name = v.Identifier.Text,
                        Kind = isConst ? HandMemberKind.Const : HandMemberKind.Field,
                        TypeText = f.Declaration.Type.ToString(),
                        IsStatic = isStatic || isConst,
                        IsReadOnly = f.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) || isConst,
                        HasInitializer = v.Initializer is not null,
                        Accessibility = access,
                        IsOverride = isOverride,
                        IsNew = isNew,
                        Assembly = assembly,
                    });
                }

                break;
            }

            case PropertyDeclarationSyntax p:
            {
                AccessorDeclarationSyntax? setter = p.AccessorList?.Accessors.FirstOrDefault(a =>
                    a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration));
                type.Members.Add(new HandMember
                {
                    Name = p.Identifier.Text,
                    Kind = HandMemberKind.Property,
                    TypeText = p.Type.ToString(),
                    IsStatic = isStatic,
                    HasInitializer = p.Initializer is not null,
                    Accessibility = access,
                    IsOverride = isOverride,
                    IsNew = isNew,
                    Assembly = assembly,
                    SetterAccessibility = setter is null ? null : Access(setter.Modifiers, access),
                    SetterIsInit = setter?.IsKind(SyntaxKind.InitAccessorDeclaration) ?? false,
                });
                break;
            }

            case MethodDeclarationSyntax md:
                type.Members.Add(new HandMember
                {
                    Name = md.Identifier.Text,
                    Kind = HandMemberKind.Method,
                    IsStatic = isStatic,
                    Accessibility = access,
                    IsOverride = isOverride,
                    IsNew = isNew,
                    Assembly = assembly,
                    ParameterCount = md.ParameterList.Parameters.Count,
                });
                break;
            case EventFieldDeclarationSyntax ef:
                foreach (VariableDeclaratorSyntax v in ef.Declaration.Variables)
                {
                    type.Members.Add(new HandMember { Name = v.Identifier.Text, Kind = HandMemberKind.Event, Accessibility = access, IsNew = isNew, Assembly = assembly });
                }

                break;
            case EventDeclarationSyntax ed:
                type.Members.Add(new HandMember { Name = ed.Identifier.Text, Kind = HandMemberKind.Event, Accessibility = access, IsNew = isNew, Assembly = assembly });
                break;
            case BaseTypeDeclarationSyntax nested:
                type.Members.Add(new HandMember { Name = nested.Identifier.Text, Kind = HandMemberKind.NestedType, Accessibility = access, IsNew = isNew, Assembly = assembly });
                break;
            case DelegateDeclarationSyntax dd:
                type.Members.Add(new HandMember { Name = dd.Identifier.Text, Kind = HandMemberKind.NestedType, Accessibility = access, IsNew = isNew, Assembly = assembly });
                break;
            case ConstructorDeclarationSyntax c:
                type.Members.Add(new HandMember
                {
                    Name = c.Identifier.Text,
                    Kind = HandMemberKind.Constructor,
                    IsStatic = isStatic,
                    Accessibility = access,
                    IsOverride = isOverride,
                    IsNew = isNew,
                    Assembly = assembly,
                    ParameterCount = c.ParameterList.Parameters.Count,
                });
                if (!isStatic && c.Body is not null)
                {
                    foreach (StatementSyntax s in c.Body.Statements)
                    {
                        if (s is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax a } &&
                            a.IsKind(SyntaxKind.SimpleAssignmentExpression))
                        {
                            string? target = a.Left switch
                            {
                                IdentifierNameSyntax id => id.Identifier.Text,
                                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } ma => ma.Name.Identifier.Text,
                                _ => null,
                            };
                            if (target is not null)
                            {
                                type.ConstructorAssignments.Add((target, a.Right.ToString()));
                            }
                        }
                    }
                }

                break;
        }
    }

    private static string Access(SyntaxTokenList mods, string fallback)
    {
        bool pub = mods.Any(SyntaxKind.PublicKeyword);
        bool prot = mods.Any(SyntaxKind.ProtectedKeyword);
        bool inter = mods.Any(SyntaxKind.InternalKeyword);
        bool priv = mods.Any(SyntaxKind.PrivateKeyword);
        if (pub)
        {
            return "public";
        }

        if (prot && inter)
        {
            return "protected internal";
        }

        if (priv && prot)
        {
            return "private protected";
        }

        if (prot)
        {
            return "protected";
        }

        if (inter)
        {
            return "internal";
        }

        return priv ? "private" : fallback;
    }

    /// <summary>True for a base-list entry named like an interface: I followed by an upper-case letter and a
    /// lower-case letter (ISBasePawnPhysics, IActorPhysics). UnrealScript class names such as InternetInfo do not match.</summary>
    internal static bool LooksLikeInterface(string typeText)
    {
        string name = typeText;
        int lt = name.IndexOf('<', StringComparison.Ordinal);
        if (lt >= 0)
        {
            name = name[..lt];
        }

        int dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            name = name[(dot + 1)..];
        }

        return name.Length >= 3 && name[0] == 'I' && char.IsUpper(name[1]) && (char.IsLower(name[2]) || char.IsUpper(name[2]));
    }
}
