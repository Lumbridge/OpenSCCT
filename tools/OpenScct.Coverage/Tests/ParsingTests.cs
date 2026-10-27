using System.IO;
using System.Linq;
using Xunit;

namespace OpenScct.Coverage.Tests;

public sealed class ParsingTests
{
    [Fact]
    public void ReadsBulletAndInlineUnitLists()
    {
        const string spec = """
            # A spec

            Units:
            - uc:SBase.SFoo:Trigger
            - uc:SBase.SFoo:s_Active.{BeginState, Timer}
            - def:SBase.SFoo (bFoo only)
            - rep:SBase.SFoo.bOn, nat:Engine.Actor.PlayAnim,
              uc:Engine.Actor:Reset.

            ## Purpose

            Not a unit: uc:SBase.SFoo:Ignored

            Units: eng:ASBasePawn::physWalking, `rl:input.pad`, uc:SBase.SFoo:Vibration_* (glob, skipped)
            """;
        var ids = SpecUnits.Parse(spec).ToList();
        Assert.Equal(
            [
                "uc:SBase.SFoo:Trigger", "uc:SBase.SFoo:s_Active.BeginState", "uc:SBase.SFoo:s_Active.Timer", "def:SBase.SFoo",
                "rep:SBase.SFoo.bOn", "nat:Engine.Actor.PlayAnim", "uc:Engine.Actor:Reset", "eng:ASBasePawn::physWalking", "rl:input.pad",
            ],
            ids.Select(i => i.Id));
        Assert.All(ids.Take(7), i => Assert.Equal(3, i.Line));
        Assert.Equal(14, ids[^1].Line);
    }

    [Fact]
    public void UnitsNoneHasNoIds()
    {
        Assert.Empty(SpecUnits.Parse("Units: none of its own. This file composes the others.\n"));
    }

    [Fact]
    public void ExpandsBraceGroups()
    {
        Assert.Equal(" uc:A.B:s.X uc:A.B:s.Y ", SpecUnits.ExpandBraces("uc:A.B:s.{X, Y}"));
        Assert.Equal("nat:SBase.SPlayerController.canStart, nat:SBase.SPlayerController.refuse",
            string.Join(", ", SpecUnits.ExpandBraces("nat:SBase.SPlayerController.{canStart, refuse}").Split(' ', System.StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void ResolvesUcPaths()
    {
        Inventory inv = Synthetic.Small();
        Assert.Equal("uc:SBase.SFoo:Trigger", UnitIds.FromUcPath(inv, "SBase.SFoo.Trigger").Id);
        Assert.Equal("uc:SBase.SFoo:Trigger", UnitIds.FromUcPath(inv, "SFoo.Trigger").Id);
        Assert.Equal("uc:SBase.SFoo:s_Active.Timer", UnitIds.FromUcPath(inv, "SBase.SFoo.s_Active.Timer").Id);
        Assert.Equal("uc:SBase.SFoo:s_Active@code", UnitIds.FromUcPath(inv, "SBase.SFoo.s_Active@code").Id);
        Assert.Equal("nat:Engine.Actor.PlayAnim", UnitIds.FromUcPath(inv, "Engine.Actor.PlayAnim").Id);
        Assert.Equal("uc:Engine.Engine:Init", UnitIds.FromUcPath(inv, "Engine.Init").Id);

        Resolved classMarker = UnitIds.FromUcPath(inv, "SBase.SFoo");
        Assert.Null(classMarker.Id);
        Assert.Null(classMarker.Error);
        Resolved stateMarker = UnitIds.FromUcPath(inv, "SBase.SFoo.s_Active");
        Assert.Null(stateMarker.Id);
        Assert.Null(stateMarker.Error);

        Assert.NotNull(UnitIds.FromUcPath(inv, "SBase.SFoo.s_Empty").Error);
        inv.AddState("SBase.SFoo", "s_Empty");
        Assert.Null(UnitIds.FromUcPath(inv, "SBase.SFoo.s_Empty").Error);

        Assert.NotNull(UnitIds.FromUcPath(inv, "SBase.SFoo.Missing").Error);
        Assert.NotNull(UnitIds.FromUcPath(inv, "SBar.Trigger").Error);
        Assert.NotNull(UnitIds.FromUcPath(inv, "SBase.SFoo.s_Other@code").Error);
    }

    [Fact]
    public void MapsNativeNames()
    {
        Inventory inv = Synthetic.Small();
        Assert.Equal("nat:Engine.Actor.PlayAnim", UnitIds.FromUcNative(inv, "AActor::execPlayAnim"));
        Assert.Equal("eng:AActor::Tick", UnitIds.FromUcNative(inv, "AActor::Tick"));
        Assert.Equal("eng:AActor::execMissing", UnitIds.FromUcNative(inv, "AActor::execMissing"));
    }

    [Fact]
    public void NormalisesIds()
    {
        Inventory inv = Synthetic.Small();
        Assert.Equal("def:SBase.SFoo", UnitIds.Normalize(inv, "def:SBase.SFoo", "t").Id);
        Assert.Equal("eng:Render.View", UnitIds.Normalize(inv, "eng:Render.View", "t").Id);

        Resolved cpp = UnitIds.Normalize(inv, "nat:APawn::physWalking", "t");
        Assert.Equal("eng:APawn::physWalking", cpp.Id);
        Assert.NotNull(cpp.Warning);
        Assert.Equal("nat:Engine.Actor.PlayAnim", UnitIds.Normalize(inv, "nat:AActor::execPlayAnim", "t").Id);

        Assert.NotNull(UnitIds.Normalize(inv, "uc:SBase.SFoo:Nope", "t").Error);
        Assert.NotNull(UnitIds.Normalize(inv, "xx:SBase.SFoo", "t").Error);
        Assert.NotNull(UnitIds.Normalize(inv, "SBase.SFoo", "t").Error);
    }

    [Fact]
    public void GlobsCrossDotsAndColons()
    {
        var g = new IdGlob("uc:*SXBoxLiveRef:*");
        Assert.True(g.IsMatch("uc:SBase.SXBoxLiveRef:LogonEvent"));
        Assert.False(g.IsMatch("nat:SBase.SXBoxLiveRef.Logon"));
        Assert.True(new IdGlob("def:Core.*Commandlet").IsMatch("def:Core.HelloWorldCommandlet"));
        Assert.False(new IdGlob("def:Core.*Commandlet").IsMatch("def:Core.CommandletX"));
        Assert.True(new IdGlob("nat:SBase.SFoo.(x)*").IsMatch("nat:SBase.SFoo.(x)y"));
    }

    [Fact]
    public void ReadsTheStatusFile()
    {
        const string yaml = """
            version: 1
            units:
              uc:SBase.SFoo:Trigger:
                status: verified
                evidence: a test
              def:SBase.SFoo:
                status: n-a
            na:
              - globs:
                  - "uc:SBase.SXBoxLiveRef:*"
                reason: >-
                  Not a target,
                  folded.
              - globs:
                  - "nat:*"
            """;
        StatusFile f = StatusFile.Parse(new StringReader(yaml));
        Assert.Equal(1, f.Version);
        Assert.Equal(2, f.Units.Count);
        Assert.Equal(UnitStatus.Verified, f.Units.Single(u => u.Id == "uc:SBase.SFoo:Trigger").Status);
        Assert.Equal("a test", f.Units.Single(u => u.Id == "uc:SBase.SFoo:Trigger").Evidence);
        Assert.Equal("Not a target, folded.", f.Na[0].Reason);
        Assert.Equal(2, f.Errors.Count);
        Assert.Contains(f.Errors, e => e.Contains("def:SBase.SFoo is n-a without a reason", System.StringComparison.Ordinal));
        Assert.Contains(f.Errors, e => e.Contains("na entry 2", System.StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnknownStatuses()
    {
        StatusFile f = StatusFile.Parse(new StringReader("version: 1\nunits:\n  uc:A.B:C:\n    status: done\nna: []\n"));
        Assert.Single(f.Errors);
        Assert.Empty(f.Units);
    }

    [Fact]
    public void AnchorsFollowHeadingRules()
    {
        var anchors = CoverageModel.HeadingAnchors("# Title\n## Rules\n```\n## Not a heading\n```\n### Ground speed (every walking PlayerMove)\n## Rules\n");
        Assert.Contains("rules", anchors);
        Assert.Contains("rules-1", anchors);
        Assert.Contains("ground-speed-every-walking-playermove", anchors);
        Assert.DoesNotContain("not-a-heading", anchors);
    }
}
