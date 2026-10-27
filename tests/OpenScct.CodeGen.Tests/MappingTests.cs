using System;
using System.Collections.Generic;
using System.Globalization;
using CsCheck;
using OpenScct.CodeGen.Emit;
using OpenScct.CodeGen.Mapping;
using OpenScct.CodeGen.Model;
using Xunit;

namespace OpenScct.CodeGen.Tests;

/// <summary>Literal writing, type text parsing, the type namer and the replication condition parser.</summary>
public sealed class MappingTests
{
    [Theory]
    [InlineData(0.1f, "0.1f")]
    [InlineData(1f, "1f")]
    [InlineData(-42.5f, "-42.5f")]
    [InlineData(0.001f, "0.001f")]
    [InlineData(3.1415927f, "3.1415927f")]
    [InlineData(1e-45f, "1E-45f")]
    [InlineData(float.MaxValue, "3.4028235E+38f")]
    public void FloatLiteralsAreShortestRoundTrip(float value, string expected)
    {
        Assert.Equal(expected, CSharp.Float(value));
    }

    [Fact]
    public void FloatLiteralSpecialValues()
    {
        Assert.Equal("-0f", CSharp.Float(-0f));
        Assert.Equal("0f", CSharp.Float(0f));
        Assert.Equal("float.NaN", CSharp.Float(float.NaN));
        Assert.Equal("float.PositiveInfinity", CSharp.Float(float.PositiveInfinity));
        Assert.Equal("float.NegativeInfinity", CSharp.Float(float.NegativeInfinity));
    }

    [Fact]
    public void EveryFiniteFloatRoundTripsBitExactly()
    {
        Gen.Int.Select(BitConverter.Int32BitsToSingle).Where(float.IsFinite).Sample(f =>
        {
            string text = CSharp.Float(f);
            Assert.EndsWith("f", text, StringComparison.Ordinal);
            float back = float.Parse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture);
            return BitConverter.SingleToInt32Bits(back) == BitConverter.SingleToInt32Bits(f);
        }, iter: 20000);
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("Say \"hi\"", "\"Say \\\"hi\\\"\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("line\nnext\ttab\r", "\"line\\nnext\\ttab\\r\"")]
    [InlineData("caf\u00e9 \u2013", "\"caf\\u00E9 \\u2013\"")]
    [InlineData("\u0001", "\"\\u0001\"")]
    public void StringLiteralsAreEscaped(string value, string expected)
    {
        Assert.Equal(expected, CSharp.String(value));
    }

    [Fact]
    public void KeywordsAreEscaped()
    {
        Assert.Equal("@default", CSharp.Ident("default"));
        Assert.Equal("@class", CSharp.Ident("class"));
        Assert.Equal("Default", CSharp.Ident("Default"));
        Assert.Equal("bHidden", CSharp.Ident("bHidden"));
        Assert.True(CSharp.IsIdentifier("m_Value2"));
        Assert.False(CSharp.IsIdentifier("2abc"));
    }

    [Theory]
    [InlineData("true", "true", true)]
    [InlineData("true", "false", false)]
    [InlineData("false", null, true)]
    [InlineData("35f", "35.0", true)]
    [InlineData("0.1f", "0.10000000149011612", true)]
    [InlineData("2", "3", false)]
    [InlineData("ENetRole.ROLE_Authority", """{"value": 4, "enum": "ROLE_Authority"}""", true)]
    [InlineData("ENetRole.ROLE_None", """{"value": 4, "enum": "ROLE_Authority"}""", false)]
    public void ConstructorLiteralsCompareWithDecodedValues(string literal, string? decodedJson, bool agrees)
    {
        Json.JValue? decoded = decodedJson is null ? null : Json.JValue.Parse(decodedJson);
        Assert.Equal(agrees, Generator.Agrees(literal, decoded));
    }

    [Theory]
    [InlineData("bool", "bool")]
    [InlineData("BYTE", "byte")]
    [InlineData("string", "string")]
    [InlineData("class", "class")]
    [InlineData("array<int>", "array<int>")]
    [InlineData("array<class<Inventory> >", "array<class<Inventory> >")]
    [InlineData("class<Pawn>", "class<Pawn>")]
    [InlineData("delegate<OnClick>", "delegate<OnClick>")]
    [InlineData("SBasePawn", "SBasePawn")]
    public void TypeTextParsesBackToTheSameText(string text, string expected)
    {
        Assert.Equal(expected, TypeTextParser.Parse(text).UcText);
    }

    [Fact]
    public void TypeTextResolvesEnumsStructsAndClassesByShortName()
    {
        var model = new UcModel();
        model.Enums["Engine.Actor.EPhysics"] = new UcEnumDef { Path = "Engine.Actor.EPhysics" };
        model.Structs["Core.Object.Vector"] = new UcStructDef { Path = "Core.Object.Vector" };
        model.Classes["Engine.Pawn"] = new UcClass { Path = "Engine.Pawn", IsScript = true };
        var notes = new List<string>();
        Assert.Equal(new UEnum("Engine.Actor.EPhysics"), TypeTextParser.Resolve(TypeTextParser.Parse("EPhysics"), model, notes, "x"));
        Assert.Equal(new UStruct("Core.Object.Vector"), TypeTextParser.Resolve(TypeTextParser.Parse("vector"), model, notes, "x"));
        Assert.Equal(new UArray(new UObjectRef("Engine.Pawn")), TypeTextParser.Resolve(TypeTextParser.Parse("array<Pawn>"), model, notes, "x"));
        Assert.Equal(new UClassRef("Engine.Pawn"), TypeTextParser.Resolve(TypeTextParser.Parse("class<Pawn>"), model, notes, "x"));
        Assert.Empty(notes);
        Assert.Equal(new UObjectRef("Core.Object"), TypeTextParser.Resolve(TypeTextParser.Parse("Mystery"), model, notes, "x"));
        Assert.Single(notes);
    }

    [Fact]
    public void TypeNamerQualifiesOnlyWhenNeeded()
    {
        var namer = new TypeNamer([
            ("OpenScct.Core", "FVector"), ("OpenScct.Core.Engine", "Actor"), ("OpenScct.Core.Engine", "Info"), ("OpenScct.Game.SBase", "Info"),
            ("OpenScct.Core.Uc", "AssetRef"), ("OpenScct.Core", "Twin"), ("OpenScct.Core.Uc", "Twin"),
        ]);
        Assert.Equal("FVector", namer.Name("OpenScct.Core", "FVector", "OpenScct.Core.Engine"));
        Assert.Equal("FVector", namer.Name("OpenScct.Core", "FVector", "OpenScct.Game.SBase"));
        Assert.Equal("Actor", namer.Name("OpenScct.Core.Engine", "Actor", "OpenScct.Game.SBase"));
        Assert.Equal("global::OpenScct.Core.Engine.Info", namer.Name("OpenScct.Core.Engine", "Info", "OpenScct.Game.SBase"));
        Assert.Equal("Info", namer.Name("OpenScct.Game.SBase", "Info", "OpenScct.Game.SBase"));
        Assert.Equal("global::OpenScct.Core.Uc.Twin", namer.Name("OpenScct.Core.Uc", "Twin", "OpenScct.Game.SBase"));
        Assert.Equal("Twin", namer.Name("OpenScct.Core", "Twin", "OpenScct.Core.Engine"));
        var shadow = new HashSet<string>(StringComparer.Ordinal) { "Actor" };
        Assert.Equal("global::OpenScct.Core.Engine.Actor", namer.Name("OpenScct.Core.Engine", "Actor", "OpenScct.Game.SBase", shadow));
        Assert.Equal("List<Actor?>", namer.Write(new CsList(new CsNamed("OpenScct.Core.Engine", "Actor", true, "class")), "OpenScct.Game.SBase"));
        Assert.Equal("AssetRef[]", namer.Write(new CsArray(new CsNamed("OpenScct.Core.Uc", "AssetRef", false, "struct")), "OpenScct.Core.Engine"));
    }

    private sealed class Scope : ICondScope
    {
        private static readonly string[] Roles = ["ROLE_None", "ROLE_DumbProxy", "ROLE_SimulatedProxy", "ROLE_AutonomousProxy", "ROLE_Authority"];
        private static readonly HashSet<string> Props = new(StringComparer.OrdinalIgnoreCase) { "bHidden", "Base", "Physics", "bReplicateMovement" };

        public (string EnumType, int Value)? FindEnumMember(string member)
        {
            int i = Array.IndexOf(Roles, member);
            if (i >= 0)
            {
                return ("ENetRole", i);
            }

            return member == "PHYS_Falling" ? ("EPhysics", 2) : null;
        }

        public bool HasProperty(string name) => Props.Contains(name);
    }

    private static string Parse(string condition)
    {
        var warnings = new List<string>();
        string result = RepConditionParser.ToCSharp(RepConditionParser.Parse(condition, new Scope(), warnings));
        Assert.Empty(warnings);
        return result;
    }

    [Fact]
    public void ConditionPrecedenceFollowsUnrealScript()
    {
        Assert.Equal(
            "RepExpr.Or(RepExpr.And(RepExpr.Eq(RepExpr.Net(RepNetVar.RemoteRole), RepExpr.Enum(\"ENetRole\", \"ROLE_SimulatedProxy\", 2)), RepExpr.Net(RepNetVar.bNetInitial)), " +
            "RepExpr.And(RepExpr.Eq(RepExpr.Net(RepNetVar.RemoteRole), RepExpr.Enum(\"ENetRole\", \"ROLE_DumbProxy\", 1)), RepExpr.Eq(RepExpr.Prop(\"Physics\"), RepExpr.Enum(\"EPhysics\", \"PHYS_Falling\", 2))))",
            Parse("RemoteRole == ROLE_SimulatedProxy && bNetInitial || RemoteRole == ROLE_DumbProxy && Physics == PHYS_Falling"));
        Assert.Equal(
            "RepExpr.And(RepExpr.Or(RepExpr.Not(RepExpr.Prop(\"bHidden\")), RepExpr.Net(RepNetVar.bNetOwner)), RepExpr.Le(RepExpr.Net(RepNetVar.RemoteRole), RepExpr.Enum(\"ENetRole\", \"ROLE_SimulatedProxy\", 2)))",
            Parse("(!bHidden || bNetOwner) && RemoteRole <= ROLE_SimulatedProxy"));
    }

    [Fact]
    public void ConditionMemberAccessNoneAndNetMode()
    {
        Assert.Equal(
            "RepExpr.Or(RepExpr.Eq(RepExpr.Prop(\"Base\"), RepExpr.Null), RepExpr.Member(RepExpr.Prop(\"Base\"), \"bWorldGeometry\"))",
            Parse("Base == None || Base.bWorldGeometry"));
        Assert.Equal("RepExpr.Ne(RepExpr.Net(RepNetVar.NetMode), RepExpr.Int(3))", Parse("Level.NetMode != 3"));
        Assert.Equal("RepExpr.And(RepExpr.Net(RepNetVar.bNetDirty), RepExpr.False)", Parse("bNetDirty && false"));
    }

    [Fact]
    public void ConditionWarnsOnUnknownNamesAndRejectsGarbage()
    {
        var warnings = new List<string>();
        RepConditionParser.Parse("bMystery", new Scope(), warnings);
        Assert.Single(warnings);
        Assert.Throws<FormatException>(() => RepConditionParser.Parse("Role == ", new Scope(), []));
        Assert.Throws<FormatException>(() => RepConditionParser.Parse("Role $ 1", new Scope(), []));
        Assert.Throws<FormatException>(() => RepConditionParser.Parse("(Role == 1", new Scope(), []));
    }
}
