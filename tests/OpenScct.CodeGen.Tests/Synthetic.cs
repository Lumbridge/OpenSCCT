using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenScct.CodeGen.HandWritten;
using OpenScct.CodeGen.Plan;

namespace OpenScct.CodeGen.Tests;

/// <summary>
/// Builds small, made-up generator inputs in the shapes of the decompiler index, the reflection dump and the class
/// defaults: a Core package with Object, an Engine package with Actor, Material and Interaction, and a Demo package
/// with two actor classes. Nothing here comes from the game.
/// </summary>
internal sealed class Synthetic
{
    public static readonly PackageTarget Demo = new("Demo", "OpenScct.Game.Demo", "src/OpenScct.Game/Generated/Demo", "OpenScct.Game");

    public static readonly GeneratorOptions Options = new() { Targets = [PackageTarget.Core, PackageTarget.Engine, Demo] };

    private readonly JsonArray _ucClasses = new();
    private readonly JsonArray _rClasses = new();
    private readonly JsonArray _rProps = new();
    private readonly JsonArray _rEnums = new();
    private readonly JsonArray _rStructs = new();
    private readonly JsonArray _rConsts = new();
    private readonly JsonArray _rFunctions = new();
    private readonly JsonObject _effective = new();
    private readonly JsonObject _rawClasses = new();
    private readonly Dictionary<string, int> _nextOffset = new(StringComparer.Ordinal);

    /// <summary>The default synthetic world.</summary>
    public static Synthetic Default()
    {
        var s = new Synthetic();
        s.Class("Core.Object", null, flags: 0x112);
        s.Var("Core.Object", "Name", "name", "NameProperty", flags: 0x21003);
        s.Struct("Core.Object.Vector", ("X", "FloatProperty", null), ("Y", "FloatProperty", null), ("Z", "FloatProperty", null));
        s.Struct("Core.Object.Rotator", ("Pitch", "IntProperty", null), ("Yaw", "IntProperty", null), ("Roll", "IntProperty", null));
        s.Struct("Core.Object.Color", ("B", "ByteProperty", null), ("G", "ByteProperty", null), ("R", "ByteProperty", null), ("A", "ByteProperty", null));
        s.Const("Core.Object.MaxDemo", "0x80000001");
        s.Const("Core.Object.Pi", "3.1415926535897932");

        s.Class("Engine.Actor", "Core.Object", flags: 0x813);
        s.Enum("Engine.Actor.ENetRole", "ROLE_None", "ROLE_DumbProxy", "ROLE_SimulatedProxy", "ROLE_AutonomousProxy", "ROLE_Authority");
        s.Enum("Engine.Actor.EDrawType", "DT_None", "DT_Sprite", "DT_Mesh");
        s.Var("Engine.Actor", "Location", "Vector", "StructProperty", structPath: "Core.Object.Vector", flags: 0x20);
        s.Var("Engine.Actor", "Role", "ENetRole", "ByteProperty", enumPath: "Engine.Actor.ENetRole", flags: 0x20);
        s.Var("Engine.Actor", "RemoteRole", "ENetRole", "ByteProperty", enumPath: "Engine.Actor.ENetRole", flags: 0x20);
        s.Var("Engine.Actor", "DrawType", "EDrawType", "ByteProperty", enumPath: "Engine.Actor.EDrawType", flags: 0x1);
        s.Var("Engine.Actor", "bHidden", "bool", "BoolProperty", flags: 0x21);
        s.Var("Engine.Actor", "DrawScale", "float", "FloatProperty", flags: 0x1);
        s.Var("Engine.Actor", "Tag", "name", "NameProperty", flags: 0x1);
        s.Var("Engine.Actor", "Owner", "Actor", "ObjectProperty", propertyClass: "Engine.Actor", flags: 0x20);
        s.Var("Engine.Actor", "Texture", "Material", "ObjectProperty", propertyClass: "Engine.Material", flags: 0x1);
        s.Var("Engine.Actor", "Skins", "array<Material>", "ArrayProperty", inner: ("ObjectProperty", "Engine.Material"), flags: 0x400001);
        s.Var("Engine.Actor", "MessageClass", "class<Actor>", "ClassProperty", metaClass: "Engine.Actor", flags: 0x0);
        s.Var("Engine.Actor", "Weights", "float", "FloatProperty", dim: 3, flags: 0x1);
        s.Var("Engine.Actor", "Delegate", "int", "IntProperty", flags: 0x0);
        s.Function("Engine.Actor", "Touch", flags: 0x20802, native: 0, parameters: [("Other", "Actor", 0x80)]);
        s.Function("Engine.Actor", "Spawn", flags: 0x22401, native: 278, returns: "Actor");
        s.Replication("Engine.Actor", true, 0, "Role == ROLE_Authority && (bNetInitial || !bHidden) && Owner != None", ("Location", false), ("DrawScale", false));
        s.Defaults("Engine.Actor", """{"Role": {"value": 4, "enum": "ROLE_Authority"}, "RemoteRole": {"value": 1, "enum": "ROLE_DumbProxy"}, "DrawScale": 1.0, "DrawType": {"value": 1, "enum": "DT_Sprite"}, "Texture": "Engine.S_Actor", "Weights": [null, 0.10000000149011612, null], "MessageClass": "Engine.Actor"}""");

        s.Class("Engine.Material", "Core.Object", flags: 0x3);
        s.Class("Engine.Interaction", "Core.Object", flags: 0x2);
        s.Var("Engine.Interaction", "bActive", "bool", "BoolProperty", flags: 0x0);
        s.Defaults("Engine.Interaction", """{"bActive": true}""");

        s.Class("Demo.Widget", "Engine.Actor", flags: 0x212);
        s.Struct("Demo.Widget.WidgetInfo", ("Count", "IntProperty", null), ("Steps", "FloatProperty", 4), ("Note", "StrProperty", null));
        s.Enum("Demo.Widget.EMode", "MODE_Off", "MODE_On");
        s.Var("Demo.Widget", "Count", "int", "IntProperty", flags: 0x21);
        s.Var("Demo.Widget", "Label", "string", "StrProperty", flags: 0x408001);
        s.Var("Demo.Widget", "Holder", "Interaction", "ObjectProperty", propertyClass: "Engine.Interaction", flags: 0x0);
        s.Var("Demo.Widget", "Info", "WidgetInfo", "StructProperty", structPath: "Demo.Widget.WidgetInfo", flags: 0x1);
        s.Var("Demo.Widget", "Mode", "EMode", "ByteProperty", enumPath: "Demo.Widget.EMode", flags: 0x1);
        s.Var("Demo.Widget", "Names", "array<name>", "ArrayProperty", inner: ("NameProperty", null), flags: 0x400001);
        s.Var("Demo.Widget", "default", "int", "IntProperty", flags: 0x0);
        s.Var("Demo.Widget", "Widget", "float", "FloatProperty", flags: 0x0);
        s.Function("Demo.Widget", "ServerPoke", flags: 0x10200C2, parameters: [("Amount", "int", 0x80), ("Why", "string", 0x490)]);
        s.Function("Demo.Widget", "Touch", flags: 0x20802, parameters: [("Other", "Actor", 0x80)]);
        s.State("Demo.Widget", "s_Idle", null, ["Auto"], ignores: ["Touch"], labels: [("Begin", 0), ("Loop", 12)], functions: ["BeginState"]);
        s.State("Demo.Widget", "s_Busy", "s_Idle", [], ignores: [], labels: [], functions: ["Tick"]);
        s.Replication("Demo.Widget", false, 0, "Role < ROLE_Authority", ("ServerPoke", true));
        s.Replication("Demo.Widget", true, 9, "bNetDirty && Level.NetMode != 0", ("Count", false));
        s.Const("Demo.Widget.WIDGET_BONE", "'B Head'");
        s.Const("Demo.Widget.WIDGET_HEIGHT", "34.0f");
        s.Defaults("Demo.Widget", """{"Role": {"value": 4, "enum": "ROLE_Authority"}, "RemoteRole": {"value": 2, "enum": "ROLE_SimulatedProxy"}, "DrawScale": 1.0, "DrawType": {"value": 1, "enum": "DT_Sprite"}, "Texture": "Engine.S_Actor", "Weights": [null, 0.10000000149011612, null], "MessageClass": "Engine.Actor", "Count": 3, "Label": "Say \"hi\"\nnow", "Mode": {"value": 1, "enum": "MODE_On"}, "Info": {"Count": 2, "Steps": [null, 1.5, null, null], "Note": "x"}, "Names": ["Alpha", "Beta"], "Skins": [null, "Demo.Skin1"]}""");

        s.Class("Demo.Gadget", "Demo.Widget", flags: 0x210);
        s.Var("Demo.Gadget", "DrawScale", "int", "IntProperty", flags: 0x0);
        s.Defaults("Demo.Gadget", """{"Role": {"value": 4, "enum": "ROLE_Authority"}, "RemoteRole": {"value": 2, "enum": "ROLE_SimulatedProxy"}, "DrawScale": 7, "DrawType": {"value": 2, "enum": "DT_Mesh"}, "Texture": "Engine.S_Actor", "Weights": [null, 0.10000000149011612, 2.5], "MessageClass": "Engine.Actor", "Count": 3, "Label": "Say \"hi\"\nnow", "Mode": {"value": 1, "enum": "MODE_On"}, "Info": {"Count": 2, "Steps": [null, 1.5, null, null], "Note": "x"}, "Names": ["Alpha", "Beta"], "Skins": [null, "Demo.Skin1"]}""");
        return s;
    }

    /// <summary>The default hand-written runtime: UObject, FVector, FRotator, ENetRole and part of Actor.</summary>
    public static HandWrittenCode DefaultHand()
    {
        var hand = new HandWrittenCode();
        hand.AddSource("""
            namespace OpenScct.Core;
            public partial class UObject { protected UObject() { SetDefaults(); } protected virtual void SetDefaults() { } }
            public struct FVector { public float X; public float Y; public float Z; }
            public struct FRotator { public int Pitch; public int Yaw; public int Roll; }
            public readonly struct Name { }
            """, "src/OpenScct.Core/Runtime.cs");
        hand.AddSource("""
            namespace OpenScct.Core.Engine;
            public enum ENetRole : byte { ROLE_None = 0, ROLE_DumbProxy = 1, ROLE_SimulatedProxy = 2, ROLE_AutonomousProxy = 3, ROLE_Authority = 4 }
            public abstract partial class Actor
            {
                protected Actor() { bHidden = true; }
                public FVector Location;
                public bool bHidden;
                public ENetRole Role = ENetRole.ROLE_Authority;
                public Name Tag { get; set; }
                public Actor? Owner { get; private set; }
                protected internal virtual void Touch(Actor Other) { }
            }
            """, "src/OpenScct.Core/Engine/Actor.cs");
        hand.AddSource("""
            namespace OpenScct.Core.Uc;
            public readonly struct AssetRef { }
            public readonly struct ClassRef { }
            public static class UcDefaults { }
            """, "src/OpenScct.Core/Uc/Support.cs");
        return hand;
    }

    public GeneratorInputs Inputs(HandWrittenCode? hand = null, bool reverse = false)
    {
        JsonArray ucClasses = reverse ? Reversed(_ucClasses) : _ucClasses;
        var uc = new JsonObject { ["packages"] = new JsonObject(), ["classes"] = ucClasses.DeepClone(), ["totals"] = new JsonObject() };
        var reflect = new JsonObject
        {
            ["format"] = "svm.reflect/1",
            ["classes"] = (reverse ? Reversed(_rClasses) : _rClasses).DeepClone(),
            ["properties"] = (reverse ? Reversed(_rProps) : _rProps).DeepClone(),
            ["enums"] = _rEnums.DeepClone(),
            ["structs"] = (reverse ? Reversed(_rStructs) : _rStructs).DeepClone(),
            ["consts"] = (reverse ? Reversed(_rConsts) : _rConsts).DeepClone(),
            ["functions"] = _rFunctions.DeepClone(),
        };
        var raw = new JsonObject { ["classes"] = _rawClasses.DeepClone() };
        return new GeneratorInputs
        {
            UcIndex = Parse(uc),
            Reflect = [Parse(reflect)],
            DefaultsEffective = Parse(_effective),
            DefaultsRaw = Parse(raw),
            Hand = hand ?? DefaultHand(),
        };
    }

    private static JsonArray Reversed(JsonArray a) => new(a.Reverse().Select(n => n!.DeepClone()).ToArray());

    private static JsonElement Parse(JsonNode node)
    {
        using JsonDocument doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.Clone();
    }

    private JsonObject UcClass(string path) =>
        _ucClasses.Select(c => c!.AsObject()).First(c => (string)c["package"]! + "." + (string)c["class"]! == path);

    public void Class(string path, string? super, uint flags)
    {
        int dot = path.IndexOf('.', StringComparison.Ordinal);
        _ucClasses.Add(new JsonObject
        {
            ["package"] = path[..dot],
            ["class"] = path[(dot + 1)..],
            ["super"] = super,
            ["flags"] = new JsonArray(),
            ["config"] = "System",
            ["replication_script_size"] = 0,
            ["replication_sha1"] = null,
            ["replication"] = new JsonArray(),
            ["replication_inherited"] = new JsonArray(),
            ["consts"] = new JsonArray(),
            ["enums"] = new JsonArray(),
            ["structs"] = new JsonArray(),
            ["vars"] = new JsonArray(),
            ["functions"] = new JsonArray(),
            ["states"] = new JsonArray(),
        });
        _rClasses.Add(new JsonObject { ["path"] = path, ["super"] = super, ["flags"] = "0x" + flags.ToString("X8"), ["propertiesSize"] = 100 });
        _rawClasses[path] = new JsonObject { ["flags"] = "0x" + flags.ToString("X8") };
        _nextOffset[path] = 40;
    }

    public void Var(string classPath, string name, string type, string kind, string? structPath = null, string? enumPath = null,
        string? propertyClass = null, string? metaClass = null, (string Kind, string? Class)? inner = null, int dim = 1, uint flags = 0)
    {
        var v = new JsonObject { ["name"] = name, ["type"] = type, ["flags"] = new JsonArray() };
        if (dim > 1)
        {
            v["dim"] = dim;
        }

        if ((flags & 0x20) != 0)
        {
            v["rep_offset"] = 0;
        }

        // The decompiler index lists vars in reverse declaration order.
        UcClass(classPath)["vars"]!.AsArray().Insert(0, v);
        int offset = _nextOffset[classPath];
        _nextOffset[classPath] = offset + 4 * dim;
        _rProps.Add(Prop(classPath + "." + name, classPath, name, kind, offset, dim, flags, structPath, enumPath, propertyClass, metaClass, inner));
    }

    private JsonObject Prop(string path, string owner, string name, string kind, int offset, int dim, uint flags, string? structPath,
        string? enumPath, string? propertyClass, string? metaClass, (string Kind, string? Class)? inner)
    {
        var p = new JsonObject
        {
            ["path"] = path,
            ["owner"] = owner,
            ["name"] = name,
            ["kind"] = kind,
            ["offset"] = offset,
            ["elementSize"] = 4,
            ["arrayDim"] = dim,
            ["flags"] = "0x" + flags.ToString("X8"),
        };
        if (structPath is not null)
        {
            p["struct"] = structPath;
        }

        if (kind == "ByteProperty")
        {
            p["enum"] = enumPath;
        }

        if (propertyClass is not null)
        {
            p["propertyClass"] = propertyClass;
        }

        if (metaClass is not null)
        {
            p["metaClass"] = metaClass;
        }

        if (inner is { } i)
        {
            string innerPath = path + "." + name;
            p["inner"] = innerPath;
            p["innerKind"] = i.Kind;
            _rProps.Add(Prop(innerPath, path, name, i.Kind, 0, 1, 0, null, null, i.Class, null, null));
        }

        return p;
    }

    public void Struct(string path, params (string Name, string Kind, int? Dim)[] members)
    {
        string owner = path[..path.LastIndexOf('.')];
        string name = path[(path.LastIndexOf('.') + 1)..];
        if (_ucClasses.Any(c => (string)c!["package"]! + "." + (string)c["class"]! == owner))
        {
            UcClass(owner)["structs"]!.AsArray().Add(name);
        }

        _rStructs.Add(new JsonObject { ["path"] = path, ["owner"] = owner, ["super"] = null, ["propertiesSize"] = 4 * members.Length });
        int offset = 0;
        foreach ((string m, string kind, int? dim) in members)
        {
            _rProps.Add(Prop(path + "." + m, path, m, kind, offset, dim ?? 1, 0, null, null, null, null, null));
            offset += 4 * (dim ?? 1);
        }
    }

    public void Enum(string path, params string[] names)
    {
        string owner = path[..path.LastIndexOf('.')];
        UcClass(owner)["enums"]!.AsArray().Add(path[(path.LastIndexOf('.') + 1)..]);
        _rEnums.Add(new JsonObject { ["path"] = path, ["owner"] = owner, ["names"] = new JsonArray(names.Select(n => (JsonNode?)n).ToArray()) });
    }

    public void Const(string path, string value)
    {
        _rConsts.Add(new JsonObject { ["path"] = path, ["owner"] = path[..path.LastIndexOf('.')], ["value"] = " " + value });
    }

    private int _line = 10;

    private JsonObject FunctionObject(string owner, string name, uint flags, int native, string? returns, (string Name, string Type, uint Flags)[]? parameters)
    {
        var ps = new JsonArray();
        foreach ((string pn, string pt, uint pf) in parameters ?? [])
        {
            ps.Add(new JsonObject { ["name"] = pn, ["type"] = pt, ["flags"] = new JsonArray() });
            _rProps.Add(new JsonObject
            {
                ["path"] = owner + "." + name + "." + pn,
                ["owner"] = owner + "." + name,
                ["name"] = pn,
                ["kind"] = "IntProperty",
                ["offset"] = 0,
                ["elementSize"] = 4,
                ["arrayDim"] = 1,
                ["flags"] = "0x" + pf.ToString("X8"),
            });
        }

        _rFunctions.Add(new JsonObject { ["path"] = owner + "." + name, ["flags"] = "0x" + flags.ToString("X8") });
        return new JsonObject
        {
            ["name"] = name,
            ["flags"] = new JsonArray(),
            ["native"] = native,
            ["oper_prec"] = 0,
            ["rep_offset"] = (flags & 0x40) != 0 ? 0 : null,
            ["script_size"] = 10,
            ["sha1"] = Sha(owner + "." + name),
            ["line"] = _line += 5,
            ["params"] = ps,
            ["locals"] = new JsonArray(),
            ["return"] = returns,
        };
    }

    public void Function(string classPath, string name, uint flags, int native = 0, string? returns = null,
        (string Name, string Type, uint Flags)[]? parameters = null)
    {
        UcClass(classPath)["functions"]!.AsArray().Add(FunctionObject(classPath, name, flags, native, returns, parameters));
    }

    public void State(string classPath, string name, string? extends, string[] flags, string[] ignores, (string Name, int Offset)[] labels, string[] functions)
    {
        var fs = new JsonArray();
        foreach (string f in functions)
        {
            fs.Add(FunctionObject(classPath + "." + name, f, 0x20002, 0, null, null));
        }

        UcClass(classPath)["states"]!.AsArray().Add(new JsonObject
        {
            ["name"] = name,
            ["extends"] = extends,
            ["flags"] = new JsonArray(flags.Select(f => (JsonNode?)f).ToArray()),
            ["ignores"] = new JsonArray(ignores.Select(f => (JsonNode?)f).ToArray()),
            ["functions"] = fs,
            ["labels"] = new JsonArray(labels.Select(l => (JsonNode?)new JsonArray(l.Name, l.Offset)).ToArray()),
            ["script_size"] = labels.Length == 0 ? 1 : 30,
            ["sha1"] = Sha(classPath + "." + name),
            ["latent_calls"] = labels.Length == 0 ? new JsonArray() : new JsonArray("Sleep"),
        });
    }

    public void Replication(string classPath, bool reliable, int offset, string condition, params (string Name, bool IsFunction)[] members)
    {
        JsonObject c = UcClass(classPath);
        c["replication_sha1"] = Sha(classPath + ".replication");
        c["replication_script_size"] = 20;
        c["replication"]!.AsArray().Add(new JsonObject
        {
            ["reliable"] = reliable,
            ["offset"] = offset,
            ["condition"] = condition,
            ["members"] = new JsonArray(members.Select(m => (JsonNode?)m.Name).ToArray()),
            ["kinds"] = new JsonArray(members.Select(m => (JsonNode?)(m.IsFunction ? "function" : "var")).ToArray()),
        });
    }

    public void Defaults(string classPath, string json)
    {
        _effective[classPath] = JsonNode.Parse(json);
    }

    /// <summary>A made-up 40-hex-digit hash derived from the text (stable, not a real digest).</summary>
    public static string Sha(string text)
    {
        uint h = 2166136261u;
        foreach (char c in text)
        {
            h = (h ^ c) * 16777619u;
        }

        var parts = new string[5];
        for (int i = 0; i < 5; i++)
        {
            h = (h ^ (uint)i) * 16777619u;
            parts[i] = h.ToString("x8");
        }

        return string.Concat(parts);
    }
}
