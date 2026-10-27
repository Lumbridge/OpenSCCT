using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using OpenScct.CodeGen.Json;

namespace OpenScct.CodeGen.Model;

/// <summary>
/// Builds the <see cref="UcModel"/> from the decompiler index (<c>uc-index.json</c>), the reflection dumps
/// (<c>reflect/game.json</c> preferred, <c>reflect/editor.json</c> for what the game dump lacks) and the class
/// defaults (<c>classdefaults_localized.json</c> for values, <c>classdefaults.json</c> for class flags).
/// </summary>
internal sealed class ModelReader
{
    private static readonly Dictionary<string, uint> FunctionFlagBits = new(StringComparer.Ordinal)
    {
        ["Final"] = 0x1, ["Defined"] = 0x2, ["Iterator"] = 0x4, ["Latent"] = 0x8, ["PreOperator"] = 0x10,
        ["Singular"] = 0x20, ["Net"] = 0x40, ["NetReliable"] = 0x80, ["Simulated"] = 0x100, ["Exec"] = 0x200,
        ["Native"] = 0x400, ["Event"] = 0x800, ["Operator"] = 0x1000, ["Static"] = 0x2000, ["NoExport"] = 0x4000,
        ["Const"] = 0x8000, ["Invariant"] = 0x10000, ["Public"] = 0x20000, ["Private"] = 0x40000,
        ["Protected"] = 0x80000, ["Delegate"] = 0x100000, ["0x1000000"] = 0x1000000,
    };

    private static readonly Dictionary<string, uint> PropertyFlagBits = new(StringComparer.Ordinal)
    {
        ["Edit"] = 0x1, ["Const"] = 0x2, ["Input"] = 0x4, ["ExportObject"] = 0x8, ["OptionalParm"] = 0x10,
        ["Net"] = 0x20, ["EditConstArray"] = 0x40, ["Parm"] = 0x80, ["OutParm"] = 0x100, ["SkipParm"] = 0x200,
        ["ReturnParm"] = 0x400, ["CoerceParm"] = 0x800, ["Native"] = 0x1000, ["Transient"] = 0x2000,
        ["Config"] = 0x4000, ["Localized"] = 0x8000, ["Travel"] = 0x10000, ["EditConst"] = 0x20000,
        ["GlobalConfig"] = 0x40000, ["OnDemand"] = 0x100000, ["New"] = 0x200000, ["NeedCtorLink"] = 0x400000,
        ["NoExport"] = 0x800000, ["EditInline"] = 0x4000000, ["EdFindable"] = 0x8000000,
        ["EditInlineUse"] = 0x10000000, ["Deprecated"] = 0x20000000, ["EditInlineNotify"] = 0x40000000,
        ["Flag80000000"] = 0x80000000, ["0x80000000"] = 0x80000000,
    };

    private static readonly Dictionary<string, uint> StateFlagBits = new(StringComparer.Ordinal)
    {
        ["Editable"] = 0x1, ["Auto"] = 0x2, ["Simulated"] = 0x4,
    };

    private static readonly Dictionary<string, uint> ClassFlagBits = new(StringComparer.Ordinal)
    {
        ["Abstract"] = 0x1, ["Compiled"] = 0x2, ["Config"] = 0x4, ["Transient"] = 0x8, ["Parsed"] = 0x10,
        ["Localized"] = 0x20, ["SafeReplace"] = 0x40, ["RuntimeStatic"] = 0x80, ["NoExport"] = 0x100,
        ["Placeable"] = 0x200, ["PerObjectConfig"] = 0x400, ["NativeReplication"] = 0x800,
        ["EditInlineNew"] = 0x1000, ["CollapseCategories"] = 0x2000,
    };

    private readonly Dictionary<string, JsonElement> _reflectClasses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _reflectProps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<JsonElement>> _reflectPropsByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _reflectFunctions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _reflectEnums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _reflectStructs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<JsonElement>> _reflectConstsByOwner = new(StringComparer.Ordinal);

    /// <summary>Notes for the report (missing data, fallbacks).</summary>
    public List<string> Notes { get; } = new();

    /// <summary>
    /// Reads every input. <paramref name="reflectDumps"/> are in priority order (the game dump first);
    /// a later dump only fills paths the earlier ones lack.
    /// </summary>
    public UcModel Read(JsonElement ucIndex, IReadOnlyList<JsonElement> reflectDumps, JsonElement? defaultsEffective,
        JsonElement? defaultsRaw)
    {
        foreach (JsonElement dump in reflectDumps)
        {
            IndexReflect(dump);
        }

        var model = new UcModel();
        var rawFlags = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (defaultsRaw is { } raw && raw.TryGetProperty("classes", out JsonElement rawClasses))
        {
            foreach (JsonProperty c in rawClasses.EnumerateObject())
            {
                if (c.Value.TryGetProperty("flags", out JsonElement f) && f.ValueKind == JsonValueKind.String)
                {
                    rawFlags[c.Name] = ParseHex(f.GetString()!);
                }
            }
        }

        foreach (JsonElement c in ucIndex.GetProperty("classes").EnumerateArray())
        {
            UcClass cls = ReadScriptClass(c, rawFlags);
            model.Classes[cls.Path] = cls;
        }

        // Intrinsic classes: in the reflection dumps but without script.
        foreach ((string path, JsonElement c) in _reflectClasses.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (model.Classes.ContainsKey(path))
            {
                continue;
            }

            model.Classes[path] = ReadIntrinsicClass(path, c);
        }

        foreach ((string path, JsonElement e) in _reflectEnums)
        {
            var def = new UcEnumDef { Path = path };
            foreach (JsonElement n in e.GetProperty("names").EnumerateArray())
            {
                def.Members.Add(n.GetString()!);
            }

            model.Enums[path] = def;
        }

        foreach ((string path, JsonElement s) in _reflectStructs)
        {
            var def = new UcStructDef
            {
                Path = path,
                SuperPath = Str(s, "super"),
                PropertiesSize = Int(s, "propertiesSize", 0),
            };
            def.Members.AddRange(OrderByLayout(ReadReflectMembers(path)));
            model.Structs[path] = def;
        }

        foreach (UcClass cls in model.Classes.Values)
        {
            foreach (UcVar v in cls.Vars)
            {
                v.Type = TypeTextParser.Resolve(v.Type, model, Notes, cls.Path + "." + v.Name);
            }
        }

        if (defaultsEffective is { } eff)
        {
            foreach (JsonProperty c in eff.EnumerateObject())
            {
                var values = new Dictionary<string, JValue>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty p in c.Value.EnumerateObject())
                {
                    values[p.Name] = JValue.From(p.Value);
                }

                model.Defaults[c.Name] = values;
            }
        }

        return model;
    }

    private void IndexReflect(JsonElement dump)
    {
        AddAll(dump, "classes", _reflectClasses);
        AddAll(dump, "functions", _reflectFunctions);
        AddAll(dump, "enums", _reflectEnums);
        AddAll(dump, "structs", _reflectStructs);
        if (dump.TryGetProperty("properties", out JsonElement props))
        {
            foreach (JsonElement p in props.EnumerateArray())
            {
                string path = p.GetProperty("path").GetString()!;
                if (_reflectProps.TryAdd(path, p))
                {
                    string owner = p.GetProperty("owner").GetString()!;
                    if (!_reflectPropsByOwner.TryGetValue(owner, out List<JsonElement>? list))
                    {
                        _reflectPropsByOwner[owner] = list = new List<JsonElement>();
                    }

                    list.Add(p);
                }
            }
        }

        if (dump.TryGetProperty("consts", out JsonElement consts))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (List<JsonElement> list in _reflectConstsByOwner.Values)
            {
                foreach (JsonElement c in list)
                {
                    seen.Add(c.GetProperty("path").GetString()!);
                }
            }

            foreach (JsonElement c in consts.EnumerateArray())
            {
                string path = c.GetProperty("path").GetString()!;
                if (!seen.Add(path))
                {
                    continue;
                }

                string owner = c.GetProperty("owner").GetString()!;
                if (!_reflectConstsByOwner.TryGetValue(owner, out List<JsonElement>? list))
                {
                    _reflectConstsByOwner[owner] = list = new List<JsonElement>();
                }

                list.Add(c);
            }
        }
    }

    private static void AddAll(JsonElement dump, string key, Dictionary<string, JsonElement> into)
    {
        if (!dump.TryGetProperty(key, out JsonElement items))
        {
            return;
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            into.TryAdd(item.GetProperty("path").GetString()!, item);
        }
    }

    private UcClass ReadScriptClass(JsonElement c, Dictionary<string, uint> rawFlags)
    {
        string package = c.GetProperty("package").GetString()!;
        string name = c.GetProperty("class").GetString()!;
        string path = package + "." + name;
        _reflectClasses.TryGetValue(path, out JsonElement rc);
        bool hasReflect = rc.ValueKind == JsonValueKind.Object;

        uint flags;
        if (rawFlags.TryGetValue(path, out uint fromDefaults))
        {
            flags = fromDefaults;
        }
        else if (hasReflect && Str(rc, "flags") is { } hex)
        {
            flags = ParseHex(hex);
        }
        else
        {
            flags = Bits(c.GetProperty("flags"), ClassFlagBits, path);
        }

        var cls = new UcClass
        {
            Path = path,
            SuperPath = Str(c, "super"),
            IsScript = true,
            Flags = flags,
            Config = Str(c, "config"),
            PropertiesSize = hasReflect ? Int(rc, "propertiesSize", 0) : 0,
            ReplicationSha1 = Str(c, "replication_sha1"),
            ReplicationScriptSize = Int(c, "replication_script_size", 0),
        };

        var vars = new List<UcVar>();
        foreach (JsonElement v in c.GetProperty("vars").EnumerateArray())
        {
            vars.Add(ReadScriptVar(path, v));
        }

        cls.Vars.AddRange(OrderVars(path, vars));

        foreach (JsonElement f in c.GetProperty("functions").EnumerateArray())
        {
            cls.Functions.Add(ReadFunction(path, null, f));
        }

        foreach (JsonElement s in c.GetProperty("states").EnumerateArray())
        {
            cls.States.Add(ReadState(path, s));
        }

        foreach (JsonElement e in c.GetProperty("enums").EnumerateArray())
        {
            cls.EnumNames.Add(e.GetString()!);
        }

        foreach (JsonElement s in c.GetProperty("structs").EnumerateArray())
        {
            cls.StructNames.Add(s.GetString()!);
        }

        foreach (JsonElement r in c.GetProperty("replication").EnumerateArray())
        {
            var block = new UcRepBlockDef
            {
                Reliable = r.GetProperty("reliable").GetBoolean(),
                Offset = Int(r, "offset", 0),
                Condition = r.GetProperty("condition").GetString()!,
            };
            JsonElement kinds = r.GetProperty("kinds");
            int i = 0;
            foreach (JsonElement m in r.GetProperty("members").EnumerateArray())
            {
                string kind = kinds[i++].GetString()!;
                block.Members.Add((m.GetString()!, kind == "function"));
            }

            cls.Replication.Add(block);
        }

        if (c.TryGetProperty("replication_inherited", out JsonElement inherited))
        {
            foreach (JsonElement m in inherited.EnumerateArray())
            {
                cls.ReplicationInherited.Add(m.GetString()!);
            }
        }

        AddConsts(cls);
        return cls;
    }

    private UcClass ReadIntrinsicClass(string path, JsonElement rc)
    {
        var cls = new UcClass
        {
            Path = path,
            SuperPath = Str(rc, "super"),
            IsScript = false,
            Flags = Str(rc, "flags") is { } hex ? ParseHex(hex) : 0,
            Config = Str(rc, "configName"),
            PropertiesSize = Int(rc, "propertiesSize", 0),
        };
        cls.Vars.AddRange(OrderByLayout(ReadReflectMembers(path)));
        AddConsts(cls);
        return cls;
    }

    private void AddConsts(UcClass cls)
    {
        if (!_reflectConstsByOwner.TryGetValue(cls.Path, out List<JsonElement>? consts))
        {
            return;
        }

        foreach (JsonElement k in consts.OrderBy(k => k.GetProperty("path").GetString(), StringComparer.Ordinal))
        {
            string path = k.GetProperty("path").GetString()!;
            cls.Consts.Add(new UcConst
            {
                Name = path[(path.LastIndexOf('.') + 1)..],
                ValueText = (Str(k, "value") ?? string.Empty).Trim(),
            });
        }
    }

    private UcVar ReadScriptVar(string classPath, JsonElement v)
    {
        string name = v.GetProperty("name").GetString()!;
        string typeText = v.GetProperty("type").GetString()!;
        int dim = Int(v, "dim", 1);
        _reflectProps.TryGetValue(classPath + "." + name, out JsonElement rp);
        bool hasReflect = rp.ValueKind == JsonValueKind.Object;
        UType type = hasReflect ? ResolveReflectType(rp) : TypeTextParser.Parse(typeText);
        if (!hasReflect)
        {
            Notes.Add($"{classPath}.{name}: no reflection record, type parsed from '{typeText}'.");
        }

        return new UcVar
        {
            Name = name,
            TypeText = typeText,
            Type = type,
            Flags = hasReflect && Str(rp, "flags") is { } hex ? ParseHex(hex) : Bits(v.GetProperty("flags"), PropertyFlagBits, classPath + "." + name),
            ArrayDim = hasReflect ? Int(rp, "arrayDim", dim) : dim,
            Category = Str(v, "category"),
            Offset = hasReflect ? Int(rp, "offset", -1) : -1,
            ElementSize = hasReflect ? Int(rp, "elementSize", 0) : 0,
            BitMask = hasReflect && Str(rp, "bitMask") is { } mask ? ParseHex(mask) : 0,
            RepOffset = v.TryGetProperty("rep_offset", out JsonElement ro) && ro.ValueKind == JsonValueKind.Number ? ro.GetInt32() : -1,
        };
    }

    private List<UcVar> ReadReflectMembers(string ownerPath)
    {
        var result = new List<UcVar>();
        if (!_reflectPropsByOwner.TryGetValue(ownerPath, out List<JsonElement>? props))
        {
            return result;
        }

        foreach (JsonElement p in props)
        {
            UType type = ResolveReflectType(p);
            result.Add(new UcVar
            {
                Name = p.GetProperty("name").GetString()!,
                TypeText = type.UcText,
                Type = type,
                Flags = Str(p, "flags") is { } hex ? ParseHex(hex) : 0,
                ArrayDim = Int(p, "arrayDim", 1),
                Category = Str(p, "category"),
                Offset = Int(p, "offset", -1),
                ElementSize = Int(p, "elementSize", 0),
                BitMask = Str(p, "bitMask") is { } mask ? ParseHex(mask) : 0,
                RepOffset = p.TryGetProperty("repOffset", out JsonElement ro) && ro.ValueKind == JsonValueKind.Number ? ro.GetInt32() : -1,
            });
        }

        return result;
    }

    /// <summary>Script declaration order: native layout order when every var has an offset, else the index order reversed.</summary>
    private List<UcVar> OrderVars(string classPath, List<UcVar> vars)
    {
        if (vars.Count == 0)
        {
            return vars;
        }

        if (vars.All(v => v.Offset >= 0))
        {
            List<UcVar> byLayout = OrderByLayout(vars);
            var reversed = Enumerable.Reverse(vars).Select(v => v.Name).ToList();
            if (!byLayout.Select(v => v.Name).SequenceEqual(reversed, StringComparer.Ordinal))
            {
                Notes.Add($"{classPath}: layout order differs from the reversed index order; layout order used.");
            }

            return byLayout;
        }

        Notes.Add($"{classPath}: some vars have no native offset; reversed index order used.");
        var result = new List<UcVar>(vars);
        result.Reverse();
        return result;
    }

    private static List<UcVar> OrderByLayout(List<UcVar> vars) =>
        vars.OrderBy(v => v.Offset).ThenBy(v => v.BitMask).ThenBy(v => v.Name, StringComparer.Ordinal).ToList();

    private UType ResolveReflectType(JsonElement p)
    {
        string kind = p.GetProperty("kind").GetString()!;
        switch (kind)
        {
            case "BoolProperty":
                return UBool.Instance;
            case "ByteProperty":
                return Str(p, "enum") is { } e ? new UEnum(e) : UByte.Instance;
            case "IntProperty":
                return UInt.Instance;
            case "FloatProperty":
                return UFloat.Instance;
            case "NameProperty":
                return UName.Instance;
            case "StrProperty":
                return UString.Instance;
            case "StructProperty":
                return new UStruct(Str(p, "struct") ?? throw Bad(p, "struct"));
            case "ObjectProperty":
                return new UObjectRef(Str(p, "propertyClass") ?? "Core.Object");
            case "ClassProperty":
                return new UClassRef(Str(p, "metaClass") ?? "Core.Object");
            case "DelegateProperty":
                return new UDelegate(Str(p, "function") ?? throw Bad(p, "function"));
            case "ArrayProperty":
            {
                string inner = Str(p, "inner") ?? throw Bad(p, "inner");
                if (!_reflectProps.TryGetValue(inner, out JsonElement ip))
                {
                    throw new InvalidOperationException($"Array {p.GetProperty("path").GetString()} has no inner property record {inner}.");
                }

                return new UArray(ResolveReflectType(ip));
            }

            default:
                throw new InvalidOperationException($"Property {p.GetProperty("path").GetString()} has unsupported kind {kind}.");
        }
    }

    private static InvalidOperationException Bad(JsonElement p, string field) =>
        new($"Property {p.GetProperty("path").GetString()} has no '{field}'.");

    private UcFunc ReadFunction(string classPath, string? state, JsonElement f)
    {
        string name = f.GetProperty("name").GetString()!;
        string funcPath = state is null ? classPath + "." + name : classPath + "." + state + "." + name;
        uint flags = _reflectFunctions.TryGetValue(funcPath, out JsonElement rf) && Str(rf, "flags") is { } hex
            ? ParseHex(hex)
            : Bits(f.GetProperty("flags"), FunctionFlagBits, funcPath);
        var func = new UcFunc
        {
            Name = name,
            Sha1 = f.GetProperty("sha1").GetString()!,
            Flags = flags,
            NativeIndex = Int(f, "native", 0),
            OperPrecedence = Int(f, "oper_prec", 0),
            RepOffset = f.TryGetProperty("rep_offset", out JsonElement ro) && ro.ValueKind == JsonValueKind.Number ? ro.GetInt32() : -1,
            ScriptSize = Int(f, "script_size", 0),
            Line = Int(f, "line", 0),
            ReturnType = Str(f, "return"),
        };
        foreach (JsonElement p in f.GetProperty("params").EnumerateArray())
        {
            string pname = p.GetProperty("name").GetString()!;
            uint pflags = _reflectProps.TryGetValue(funcPath + "." + pname, out JsonElement rp) && Str(rp, "flags") is { } phex
                ? ParseHex(phex)
                : Bits(p.GetProperty("flags"), PropertyFlagBits, funcPath + "." + pname);
            func.Params.Add(new UcParam { Name = pname, TypeText = p.GetProperty("type").GetString()!, Flags = pflags });
        }

        if (f.TryGetProperty("latent_calls", out JsonElement latent))
        {
            func.LatentCalls.AddRange(latent.EnumerateArray().Select(x => x.GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        }

        return func;
    }

    private UcStateDef ReadState(string classPath, JsonElement s)
    {
        string name = s.GetProperty("name").GetString()!;
        var state = new UcStateDef
        {
            Name = name,
            Extends = Str(s, "extends"),
            Flags = Bits(s.GetProperty("flags"), StateFlagBits, classPath + "." + name),
            Sha1 = s.GetProperty("sha1").GetString()!,
            ScriptSize = Int(s, "script_size", 0),
        };
        state.Ignores.AddRange(s.GetProperty("ignores").EnumerateArray().Select(x => x.GetString()!));
        foreach (JsonElement l in s.GetProperty("labels").EnumerateArray())
        {
            state.Labels.Add(new UcLabel { Name = l[0].GetString()!, Offset = l[1].GetInt32() });
        }

        state.Labels.Sort((a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : string.CompareOrdinal(a.Name, b.Name));
        foreach (JsonElement f in s.GetProperty("functions").EnumerateArray())
        {
            state.Functions.Add(ReadFunction(classPath, name, f));
        }

        if (s.TryGetProperty("latent_calls", out JsonElement latent))
        {
            state.LatentCalls.AddRange(latent.EnumerateArray().Select(x => x.GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        }

        return state;
    }

    private uint Bits(JsonElement names, Dictionary<string, uint> table, string where)
    {
        uint bits = 0;
        foreach (JsonElement n in names.EnumerateArray())
        {
            string flag = n.GetString()!;
            if (table.TryGetValue(flag, out uint bit))
            {
                bits |= bit;
            }
            else if (flag.StartsWith("0x", StringComparison.Ordinal))
            {
                bits |= ParseHex(flag);
            }
            else
            {
                Notes.Add($"{where}: unknown flag '{flag}' ignored.");
            }
        }

        return bits;
    }

    internal static uint ParseHex(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            t = t[2..];
        }

        return uint.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
}
