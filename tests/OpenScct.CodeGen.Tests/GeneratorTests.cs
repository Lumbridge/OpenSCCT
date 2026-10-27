using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.CodeGen.Plan;
using Xunit;

namespace OpenScct.CodeGen.Tests;

/// <summary>The generator end to end on the synthetic inputs: names, hierarchy, type mapping, defaults, metadata.</summary>
public sealed class GeneratorTests
{
    private static GeneratorResult Run(bool reverse = false) =>
        Generator.Run(Synthetic.Default().Inputs(reverse: reverse), Synthetic.Options);

    private static string FileNamed(GeneratorResult r, string name) =>
        r.Files.Single(f => f.Key.EndsWith("/" + name, StringComparison.Ordinal)).Value;

    private static string[] Lines(string text) => text.Split('\n').Select(l => l.Trim()).ToArray();

    [Fact]
    public void FilesGoToThePackageFolders()
    {
        GeneratorResult r = Run();
        Assert.Contains("src/OpenScct.Core/Generated/Core/UObject.g.cs", r.Files.Keys);
        Assert.Contains("src/OpenScct.Core/Generated/Core/Core.Structs.g.cs", r.Files.Keys);
        Assert.Contains("src/OpenScct.Core/Generated/Engine/Actor.g.cs", r.Files.Keys);
        Assert.Contains("src/OpenScct.Core/Generated/Engine/UInteraction.g.cs", r.Files.Keys);
        Assert.Contains("src/OpenScct.Core/Generated/Engine/EnginePackage.g.cs", r.Files.Keys);
        Assert.Contains("src/OpenScct.Game/Generated/Demo/Widget.g.cs", r.Files.Keys);
        Assert.Contains("src/OpenScct.Game/Generated/Demo/Demo.Enums.g.cs", r.Files.Keys);
        Assert.Contains("src/OpenScct.Core/Generated/Core/FixedArrays.g.cs", r.Files.Keys);
        Assert.Empty(r.Report.Section("Errors"));
    }

    [Fact]
    public void ClassesKeepOriginalNamesAndHierarchy()
    {
        GeneratorResult r = Run();
        Assert.Contains("public partial class UObject", Lines(FileNamed(r, "UObject.g.cs")));
        Assert.Contains("public abstract partial class Actor : UObject", Lines(FileNamed(r, "Actor.g.cs")));
        Assert.Contains("public partial class UInteraction : UObject", Lines(FileNamed(r, "UInteraction.g.cs")));
        Assert.Contains("public partial class Widget : Actor", Lines(FileNamed(r, "Widget.g.cs")));
        Assert.Contains("public partial class Gadget : Widget", Lines(FileNamed(r, "Gadget.g.cs")));
        Assert.Contains("namespace OpenScct.Game.Demo;", Lines(FileNamed(r, "Widget.g.cs")));
        Assert.Contains("public static new class UcMeta", Lines(FileNamed(r, "Widget.g.cs")));
        Assert.Contains("public static class UcMeta", Lines(FileNamed(r, "UObject.g.cs")));
    }

    [Theory]
    [InlineData("Actor.g.cs", "public EDrawType DrawType; // EDrawType, Edit")]
    [InlineData("Actor.g.cs", "public float DrawScale; // float, Edit")]
    [InlineData("Actor.g.cs", "public AssetRef Texture; // Material, Edit")]
    [InlineData("Actor.g.cs", "public List<AssetRef> Skins = new(); // array<Material>, Edit")]
    [InlineData("Actor.g.cs", "public ClassRef MessageClass; // class<Actor>")]
    [InlineData("Actor.g.cs", "public readonly float[] Weights = new float[3]; // float[3], Edit")]
    [InlineData("Widget.g.cs", "public int Count; // int, Edit, Net")]
    [InlineData("Widget.g.cs", "public string Label = \"\"; // string, Edit, Localized")]
    [InlineData("Widget.g.cs", "public UInteraction? Holder; // Interaction")]
    [InlineData("Widget.g.cs", "public FWidgetInfo Info; // WidgetInfo, Edit")]
    [InlineData("Widget.g.cs", "public EMode Mode; // EMode, Edit")]
    [InlineData("Widget.g.cs", "public List<Name> Names = new(); // array<name>, Edit")]
    [InlineData("Widget.g.cs", "public int @default; // int")]
    [InlineData("Widget.g.cs", "public float Widget_; // float")]
    [InlineData("Gadget.g.cs", "public new int DrawScale; // int")]
    [InlineData("Demo.Structs.g.cs", "public FixedArray4<float> Steps; // float[4]")]
    [InlineData("Demo.Structs.g.cs", "public string Note = \"\"; // string")]
    [InlineData("Demo.Structs.g.cs", "public FWidgetInfo()")]
    [InlineData("Core.Structs.g.cs", "public partial record struct FColor")]
    [InlineData("Demo.Enums.g.cs", "MODE_On = 1,")]
    public void TypesMapAsSpecified(string file, string line)
    {
        Assert.Contains(line, Lines(FileNamed(Run(), file)));
    }

    [Fact]
    public void MappedAndHandWrittenTypesAreNotGenerated()
    {
        GeneratorResult r = Run();
        string structs = FileNamed(r, "Core.Structs.g.cs");
        Assert.DoesNotContain("struct FVector", structs, StringComparison.Ordinal);
        Assert.DoesNotContain("struct FRotator", structs, StringComparison.Ordinal);
        string enums = FileNamed(r, "Engine.Enums.g.cs");
        Assert.DoesNotContain("enum ENetRole", enums, StringComparison.Ordinal);
        Assert.Contains("public enum EDrawType : byte", Lines(enums));
    }

    [Fact]
    public void HandWrittenMembersAreSkippedButKeptInMetadata()
    {
        string actor = FileNamed(Run(), "Actor.g.cs");
        string[] lines = Lines(actor);
        Assert.DoesNotContain(lines, l => l.StartsWith("public FVector Location", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("public bool bHidden", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(" Owner;", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("new(\"Location\", \"Vector\", UcPropertyKind.Struct", StringComparison.Ordinal) && l.EndsWith("handWritten: true),", StringComparison.Ordinal));
        string obj = FileNamed(Run(), "UObject.g.cs");
        Assert.Contains(Lines(obj), l => l.StartsWith("new(\"Name\", \"name\", UcPropertyKind.Name", StringComparison.Ordinal) && l.EndsWith("csharpName: \"\"),", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultsAreTheDifferencesFromTheParent()
    {
        GeneratorResult r = Run();
        string[] actor = Lines(FileNamed(r, "Actor.g.cs"));
        Assert.Contains("Role = ENetRole.ROLE_Authority;", actor);
        Assert.Contains("RemoteRole = ENetRole.ROLE_DumbProxy;", actor);
        Assert.Contains("DrawScale = 1f;", actor);
        Assert.Contains("DrawType = EDrawType.DT_Sprite;", actor);
        Assert.Contains("Texture = new AssetRef(\"Engine.S_Actor\");", actor);
        Assert.Contains("Weights[1] = 0.1f;", actor);
        Assert.Contains("MessageClass = new ClassRef(\"Engine.Actor\");", actor);
        Assert.DoesNotContain(actor, l => l.StartsWith("Weights[0]", StringComparison.Ordinal));

        string[] widget = Lines(FileNamed(r, "Widget.g.cs"));
        Assert.Contains("base.SetDefaults();", widget);
        Assert.Contains("RemoteRole = ENetRole.ROLE_SimulatedProxy;", widget);
        Assert.DoesNotContain(widget, l => l.StartsWith("DrawType =", StringComparison.Ordinal));
        Assert.DoesNotContain(widget, l => l.StartsWith("Role =", StringComparison.Ordinal));
        Assert.Contains("Count = 3;", widget);
        // Localized text is read at run time (docs/CODEGEN.md), never written as a literal.
        Assert.Contains(widget, l => l.StartsWith("Label = ", StringComparison.Ordinal) && l.EndsWith("UcText.Get(\"Demo.Widget\", \"Label\", Label);", StringComparison.Ordinal));
        Assert.DoesNotContain(widget, l => l.Contains("Say ", StringComparison.Ordinal));
        Assert.Contains("Mode = EMode.MODE_On;", widget);
        Assert.Contains("Info = new FWidgetInfo { Count = 2, Note = \"x\" };", widget);
        Assert.Contains("Info.Steps[1] = 1.5f;", widget);
        Assert.True(Array.IndexOf(widget, "Info.Steps[1] = 1.5f;") > Array.IndexOf(widget, "Info = new FWidgetInfo { Count = 2, Note = \"x\" };"));
        Assert.Contains("UcDefaults.Set<Name>(Names, \"Alpha\", \"Beta\");", widget);
        Assert.Contains("UcDefaults.Set<AssetRef>(Skins, default, new AssetRef(\"Demo.Skin1\"));", widget);

        string[] gadget = Lines(FileNamed(r, "Gadget.g.cs"));
        Assert.Contains("DrawScale = 7;", gadget);
        Assert.Contains("DrawType = EDrawType.DT_Mesh;", gadget);
        Assert.Contains("Weights[2] = 2.5f;", gadget);
        Assert.DoesNotContain(gadget, l => l.StartsWith("Weights[1]", StringComparison.Ordinal));
        Assert.DoesNotContain(gadget, l => l.StartsWith("Count =", StringComparison.Ordinal));

        string[] interaction = Lines(FileNamed(r, "UInteraction.g.cs"));
        Assert.Contains("bActive = true;", interaction);
    }

    [Fact]
    public void ConstsKeepTheirValues()
    {
        GeneratorResult r = Run();
        string[] obj = Lines(FileNamed(r, "UObject.g.cs"));
        Assert.Contains("public const int MaxDemo = unchecked((int)0x80000001);", obj);
        Assert.Contains("public const float Pi = 3.1415927f;", obj);
        string[] widget = Lines(FileNamed(r, "Widget.g.cs"));
        Assert.Contains("public static readonly Name WIDGET_BONE = new(\"B Head\");", widget);
        Assert.Contains("public const float WIDGET_HEIGHT = 34f;", widget);
    }

    [Fact]
    public void ReplicationConditionsAreEmittedAsData()
    {
        GeneratorResult r = Run();
        string[] actor = Lines(FileNamed(r, "Actor.g.cs"));
        Assert.Contains(
            "RepExpr.And(RepExpr.And(RepExpr.Eq(RepExpr.Net(RepNetVar.Role), RepExpr.Enum(\"ENetRole\", \"ROLE_Authority\", 4)), " +
            "RepExpr.Or(RepExpr.Net(RepNetVar.bNetInitial), RepExpr.Not(RepExpr.Prop(\"bHidden\")))), RepExpr.Ne(RepExpr.Prop(\"Owner\"), RepExpr.Null)),",
            actor);
        Assert.Contains("[new(\"Location\", UcRepMemberKind.Variable), new(\"DrawScale\", UcRepMemberKind.Variable)]),", actor);
        string[] widget = Lines(FileNamed(r, "Widget.g.cs"));
        Assert.Contains("RepExpr.Lt(RepExpr.Net(RepNetVar.Role), RepExpr.Enum(\"ENetRole\", \"ROLE_Authority\", 4)),", widget);
        Assert.Contains("[new(\"ServerPoke\", UcRepMemberKind.Function, (UcFunctionFlags)0x010200C2u)]),", widget);
        Assert.Contains("RepExpr.And(RepExpr.Net(RepNetVar.bNetDirty), RepExpr.Ne(RepExpr.Net(RepNetVar.NetMode), RepExpr.Int(0))),", widget);
        Assert.Contains("Demo.Widget.ServerPoke", r.Report.Section("Functions with the to-all-clients flag (0x01000000)"));
    }

    [Fact]
    public void UnitManifestAndStatesAreEmitted()
    {
        string[] widget = Lines(FileNamed(Run(), "Widget.g.cs"));
        string pokeSha = Synthetic.Sha("Demo.Widget.ServerPoke");
        Assert.Contains(widget, l => l.StartsWith($"new(\"ServerPoke\", \"{pokeSha}\", (UcFunctionFlags)0x010200C2u, 0, 10, null, [new(\"Amount\", \"int\", (UcPropertyFlags)0x00000080u), new(\"Why\", \"string\", (UcPropertyFlags)0x00000490u)], repOffset: 0)", StringComparison.Ordinal));
        string idleSha = Synthetic.Sha("Demo.Widget.s_Idle");
        Assert.Contains($"\"s_Idle\", null, (UcStateFlags)0x00000002u, [\"Touch\"], [new(\"Begin\", 0), new(\"Loop\", 12)], \"{idleSha}\", 30,", widget);
        Assert.Contains(widget, l => l.StartsWith("\"s_Busy\", \"s_Idle\", (UcStateFlags)0x00000000u, [], [],", StringComparison.Ordinal));
        Assert.Contains("public static UcStateInfo s_Busy => Class.States[0];", widget);
        Assert.Contains("public static UcStateInfo s_Idle => Class.States[1];", widget);
        Assert.Contains("static () => new Widget(),", widget);
        string[] actor = Lines(FileNamed(Run(), "Actor.g.cs"));
        Assert.Contains("null,", actor);
        Assert.Contains(actor, l => l.StartsWith("new(\"Spawn\", ", StringComparison.Ordinal) && l.Contains("(UcFunctionFlags)0x00022401u, 278, 10, \"Actor\", [])", StringComparison.Ordinal));
    }

    [Fact]
    public void OutputIsDeterministicAndIndependentOfInputOrder()
    {
        GeneratorResult a = Run();
        GeneratorResult b = Run();
        GeneratorResult reversed = Run(reverse: true);
        Assert.Equal(a.Files.Keys, b.Files.Keys);
        Assert.Equal(a.Files.Keys, reversed.Files.Keys);
        foreach ((string path, string content) in a.Files)
        {
            Assert.Equal(content, b.Files[path]);
            Assert.Equal(content, reversed.Files[path]);
        }

        Assert.Equal(a.Report.ToMarkdown(), reversed.Report.ToMarkdown());
    }

    [Fact]
    public void OutputUsesLfAndNoTrailingWhitespace()
    {
        foreach ((string path, string content) in Run().Files)
        {
            Assert.DoesNotContain('\r', content);
            Assert.DoesNotContain('\t', content);
            Assert.EndsWith("\n", content, StringComparison.Ordinal);
            Assert.False(content.EndsWith("\n\n", StringComparison.Ordinal), path);
            Assert.All(content.Split('\n'), l => Assert.Equal(l.TrimEnd(), l));
            Assert.StartsWith("// <auto-generated>", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReportExplainsTheDecisions()
    {
        Report report = Run().Report;
        Assert.Contains("Demo.Gadget.DrawScale", report.Section("Members declared new (hide an inherited member)"));
        Assert.Contains("Demo.Widget.Widget -> Widget_ (same name as its class)", report.Section("Renamed members"));
        Assert.Contains(report.Section("Hand-written constructor assignments that override decoded defaults"),
            l => l.StartsWith("Engine.Actor: bHidden = true", StringComparison.Ordinal));
        Assert.Contains("Engine.Actor.Owner (property Actor?)", report.Section("Script properties declared by hand"));
        Assert.True(report.GetCount("classes") >= 6);
    }

    [Fact]
    public void AHandWrittenMemberInASubclassStopsTheAncestorField()
    {
        Synthetic s = Synthetic.Default();
        var hand = Synthetic.DefaultHand();
        hand.AddSource("""
            namespace OpenScct.Game.Demo;
            public partial class Gadget { public int Count; }
            """, "src/OpenScct.Game/Demo/Gadget.cs");
        GeneratorResult r = Generator.Run(s.Inputs(hand), Synthetic.Options);
        Assert.DoesNotContain(Lines(FileNamed(r, "Widget.g.cs")), l => l.StartsWith("public int Count;", StringComparison.Ordinal));
        Assert.Contains(r.Report.Section("Errors"), l => l.Contains("Demo.Widget.Count not generated", StringComparison.Ordinal));
    }
}
