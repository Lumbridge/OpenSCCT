using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OpenScct.Coverage;

internal enum ProvenanceAttributeKind
{
    Uc,
    UcNative,
    PortOf,
}

/// <summary>One provenance attribute found in an assembly's metadata.</summary>
/// <param name="Value">The first constructor argument: the [Uc] path, the [UcNative] name or the [PortOf] id.</param>
/// <param name="Location">The member carrying it, for example <c>OpenScct.Game.SBase.SInterrupteur.Trigger</c>.</param>
internal sealed record FoundAttribute(
    ProvenanceAttributeKind Kind,
    string Value,
    string? Sha1,
    string? Spec,
    string Assembly,
    string Location);

/// <summary>What one assembly holds for the coverage report.</summary>
/// <param name="GeneratedClasses">Top-level types with a nested <c>UcMeta</c> class: the class skeletons that
/// OpenScct.CodeGen emitted.</param>
internal sealed record ScannedAssembly(
    string Path,
    string Name,
    IReadOnlyList<FoundAttribute> Attributes,
    IReadOnlyList<GeneratedClass> GeneratedClasses);

/// <summary>A class skeleton emitted by OpenScct.CodeGen, with the states its <c>UcMeta</c> lists (one property each).</summary>
internal sealed record GeneratedClass(string Namespace, string Name, IReadOnlyList<string> States);

/// <summary>
/// Reads the OpenScct.Provenance attributes out of built assemblies with System.Reflection.Metadata. Assemblies are
/// never loaded, so any build configuration or target works.
/// </summary>
internal static class AttributeScanner
{
    private const string ProvenanceNamespace = "OpenScct.Provenance";

    /// <summary>The attributes and skeletons of one assembly, or null when the file is not a .NET assembly.</summary>
    public static ScannedAssembly? Scan(string path)
    {
        try
        {
            return ScanFile(path);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static ScannedAssembly? ScanFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            return null;
        }

        MetadataReader md = pe.GetMetadataReader();
        if (!md.IsAssembly)
        {
            return null;
        }

        string asmName = md.GetString(md.GetAssemblyDefinition().Name);
        var found = new List<FoundAttribute>();
        foreach (CustomAttributeHandle h in md.CustomAttributes)
        {
            CustomAttribute ca = md.GetCustomAttribute(h);
            (string ns, string name) = AttributeType(md, ca.Constructor);
            if (!string.Equals(ns, ProvenanceNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            ProvenanceAttributeKind? kind = name switch
            {
                "UcAttribute" => ProvenanceAttributeKind.Uc,
                "UcNativeAttribute" => ProvenanceAttributeKind.UcNative,
                "PortOfAttribute" => ProvenanceAttributeKind.PortOf,
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }

            CustomAttributeValue<string> value = ca.DecodeValue(TypeNameProvider.Instance);
            if (value.FixedArguments.Length == 0 || value.FixedArguments[0].Value is not string first)
            {
                continue;
            }

            string? sha1 = Named(value.NamedArguments, "Sha1");
            string? spec = Named(value.NamedArguments, "Spec");
            found.Add(new FoundAttribute(kind.Value, first, sha1, spec, asmName, Describe(md, ca.Parent)));
        }

        var generated = new List<GeneratedClass>();
        foreach (TypeDefinitionHandle th in md.TypeDefinitions)
        {
            TypeDefinition td = md.GetTypeDefinition(th);
            if (!td.IsNested || !md.StringComparer.Equals(td.Name, "UcMeta"))
            {
                continue;
            }

            TypeDefinition outer = md.GetTypeDefinition(td.GetDeclaringType());
            if (!outer.GetDeclaringType().IsNil)
            {
                continue;
            }

            var states = td.GetProperties().Select(p => md.GetString(md.GetPropertyDefinition(p).Name)).Order(StringComparer.Ordinal).ToList();
            generated.Add(new GeneratedClass(md.GetString(outer.Namespace), md.GetString(outer.Name), states));
        }

        return new ScannedAssembly(
            path,
            asmName,
            found.OrderBy(a => a.Location, StringComparer.Ordinal).ThenBy(a => a.Kind).ThenBy(a => a.Value, StringComparer.Ordinal).ToList(),
            generated.OrderBy(g => g.Namespace, StringComparer.Ordinal).ThenBy(g => g.Name, StringComparer.Ordinal).ToList());
    }

    private static string? Named(ImmutableArray<CustomAttributeNamedArgument<string>> args, string name)
    {
        foreach (CustomAttributeNamedArgument<string> a in args)
        {
            if (string.Equals(a.Name, name, StringComparison.Ordinal))
            {
                return a.Value as string;
            }
        }

        return null;
    }

    private static (string Namespace, string Name) AttributeType(MetadataReader md, EntityHandle ctor)
    {
        switch (ctor.Kind)
        {
            case HandleKind.MemberReference:
            {
                MemberReference mr = md.GetMemberReference((MemberReferenceHandle)ctor);
                if (mr.Parent.Kind == HandleKind.TypeReference)
                {
                    TypeReference tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
                    return (md.GetString(tr.Namespace), md.GetString(tr.Name));
                }

                if (mr.Parent.Kind == HandleKind.TypeDefinition)
                {
                    TypeDefinition td = md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent);
                    return (md.GetString(td.Namespace), md.GetString(td.Name));
                }

                return ("", "");
            }

            case HandleKind.MethodDefinition:
            {
                MethodDefinition m = md.GetMethodDefinition((MethodDefinitionHandle)ctor);
                TypeDefinition td = md.GetTypeDefinition(m.GetDeclaringType());
                return (md.GetString(td.Namespace), md.GetString(td.Name));
            }

            default:
                return ("", "");
        }
    }

    private static string Describe(MetadataReader md, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeDefinition:
                return TypeName(md, (TypeDefinitionHandle)parent);
            case HandleKind.MethodDefinition:
            {
                MethodDefinition m = md.GetMethodDefinition((MethodDefinitionHandle)parent);
                return TypeName(md, m.GetDeclaringType()) + "." + md.GetString(m.Name);
            }

            case HandleKind.FieldDefinition:
            {
                FieldDefinition f = md.GetFieldDefinition((FieldDefinitionHandle)parent);
                return TypeName(md, f.GetDeclaringType()) + "." + md.GetString(f.Name);
            }

            case HandleKind.PropertyDefinition:
            {
                PropertyDefinition p = md.GetPropertyDefinition((PropertyDefinitionHandle)parent);
                PropertyAccessors acc = p.GetAccessors();
                MethodDefinitionHandle any = !acc.Getter.IsNil ? acc.Getter : acc.Setter;
                string owner = any.IsNil ? "?" : TypeName(md, md.GetMethodDefinition(any).GetDeclaringType());
                return owner + "." + md.GetString(p.Name);
            }

            default:
                return parent.Kind.ToString();
        }
    }

    private static string TypeName(MetadataReader md, TypeDefinitionHandle h)
    {
        TypeDefinition td = md.GetTypeDefinition(h);
        string name = md.GetString(td.Name);
        TypeDefinitionHandle outer = td.GetDeclaringType();
        if (!outer.IsNil)
        {
            return TypeName(md, outer) + "." + name;
        }

        string ns = md.GetString(td.Namespace);
        return ns.Length > 0 ? ns + "." + name : name;
    }

    /// <summary>Decodes attribute blobs with type names as plain strings; only string and integer values are read.</summary>
    private sealed class TypeNameProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly TypeNameProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            TypeDefinition td = reader.GetTypeDefinition(handle);
            return reader.GetString(td.Namespace) + "." + reader.GetString(td.Name);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference tr = reader.GetTypeReference(handle);
            return reader.GetString(tr.Namespace) + "." + reader.GetString(tr.Name);
        }

        public string GetTypeFromSerializedName(string name) => name;

        // Enum arguments (ProvenanceKind) are 32-bit; the three attributes read here take none.
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => string.Equals(type, "System.Type", StringComparison.Ordinal);
    }
}
