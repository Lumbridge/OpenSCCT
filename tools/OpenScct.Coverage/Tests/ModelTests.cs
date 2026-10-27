using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace OpenScct.Coverage.Tests;

public sealed class ModelTests : IDisposable
{
    private readonly DirectoryInfo _repo = Directory.CreateTempSubdirectory("svm-coverage-");

    public ModelTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo.FullName, "spec", "devices"));
        File.WriteAllText(Path.Combine(_repo.FullName, "spec", "devices", "foo.md"), "# Foo\n\n## Rules\n");
    }

    public void Dispose() => _repo.Delete(recursive: true);

    private static FoundAttribute PortOf(string id, string? spec = "spec/devices/foo.md#rules", string loc = "OpenScct.Game.SBase.SFoo.Trigger") =>
        new(ProvenanceAttributeKind.PortOf, id, null, spec, "OpenScct.Game", loc);

    private static ScannedAssembly Assembly(IReadOnlyList<FoundAttribute> attrs, params GeneratedClass[] generated) =>
        new("OpenScct.Game.dll", "OpenScct.Game", attrs, generated);

    private CoverageInputs Inputs(
        IReadOnlyList<FoundAttribute>? attrs = null,
        string? status = null,
        IReadOnlyList<SpecUnitRef>? specs = null,
        IReadOnlyList<ScenarioInfo>? scenarios = null,
        IReadOnlyList<SimResult>? report = null,
        GeneratedClass[]? generated = null) => new()
    {
        RepoRoot = _repo.FullName,
        Inventory = Synthetic.Small(),
        Status = status is null ? StatusFile.Empty : StatusFile.Parse(new StringReader(status)),
        Specs = specs ?? [],
        Assemblies = [Assembly(attrs ?? [], generated ?? [])],
        Scenarios = scenarios ?? [],
        Report = report,
    };

    private static UnitState Unit(CoverageResult r, string id) => r.Units.Single(u => u.Id == id);

    [Fact]
    public void StatusesClimbFromSpecToVerified()
    {
        CoverageResult r = CoverageModel.Compute(Inputs(
            attrs: [PortOf("uc:SBase.SFoo:Trigger"), new FoundAttribute(ProvenanceAttributeKind.Uc, "SBase.SFoo.s_Active.Timer", "aa", null, "OpenScct.Game", "X.Timer")],
            specs: [new SpecUnitRef("uc:SBase.SFoo:Trigger", "spec/devices/foo.md", 3), new SpecUnitRef("uc:Engine.Actor:Reset", "spec/devices/foo.md", 3)],
            scenarios: [new ScenarioInfo("devices.foo", "tests/scenarios/devices/foo.scenario.json", ["uc:SBase.SFoo:Trigger", "uc:Engine.Actor:Reset"])],
            report: [new SimResult("devices.foo", "DtReplay", true), new SimResult("devices.foo", "Fixed", false)]));

        Assert.Empty(r.Errors);
        Assert.Equal(UnitStatus.Verified, Unit(r, "uc:SBase.SFoo:Trigger").Status);
        Assert.Equal(UnitStatus.Ported, Unit(r, "uc:SBase.SFoo:s_Active.Timer").Status);
        Assert.Equal(UnitStatus.Specced, Unit(r, "uc:Engine.Actor:Reset").Status);
        Assert.Contains(r.Warnings, w => w.Contains("uc:Engine.Actor:Reset is covered by the passing scenario", StringComparison.Ordinal));
        Assert.Equal(UnitStatus.Unported, Unit(r, "rep:SBase.SFoo.bOn").Status);
        Assert.Equal(true, r.ScenarioResults["devices.foo"]);
    }

    [Fact]
    public void AllTimingsMustPassWhenAsked()
    {
        var inputs = Inputs(
            attrs: [PortOf("uc:SBase.SFoo:Trigger")],
            scenarios: [new ScenarioInfo("devices.foo", "f", ["uc:SBase.SFoo:Trigger"])],
            report: [new SimResult("devices.foo", "DtReplay", true), new SimResult("devices.foo", "Fixed", false)]);
        CoverageResult strict = CoverageModel.Compute(new CoverageInputs
        {
            RepoRoot = inputs.RepoRoot,
            Inventory = Synthetic.Small(),
            Assemblies = inputs.Assemblies,
            Scenarios = inputs.Scenarios,
            Report = inputs.Report,
            AllTimings = true,
        });
        Assert.Equal(UnitStatus.Ported, Unit(strict, "uc:SBase.SFoo:Trigger").Status);
        Assert.Equal(false, strict.ScenarioResults["devices.foo"]);
    }

    [Fact]
    public void NothingIsVerifiedWithoutAReport()
    {
        CoverageResult r = CoverageModel.Compute(Inputs(
            attrs: [PortOf("uc:SBase.SFoo:Trigger")],
            scenarios: [new ScenarioInfo("devices.foo", "f", ["uc:SBase.SFoo:Trigger"])]));
        Assert.Equal(UnitStatus.Ported, Unit(r, "uc:SBase.SFoo:Trigger").Status);
        Assert.Null(r.ScenarioResults["devices.foo"]);
    }

    [Fact]
    public void OutOfScopeGlobsAndGeneratedDefaults()
    {
        const string status = """
            version: 1
            units: {}
            na:
              - globs: ["uc:SBase.SXBoxLiveRef:*", "nat:SBase.SXBoxLiveRef.*", "def:SBase.SXBoxLiveRef"]
                reason: Xbox Live.
              - globs: ["uc:SBase.SNothing:*"]
                reason: Matches nothing.
            """;
        CoverageResult r = CoverageModel.Compute(Inputs(
            status: status,
            generated: [new GeneratedClass("OpenScct.Game.SBase", "SFoo", ["s_Empty"]), new GeneratedClass("OpenScct.Game.SBase", "SXBoxLiveRef", []),
                new GeneratedClass("OpenScct.Core.Engine", "Actor", [])]));

        Assert.Equal(UnitStatus.Ported, Unit(r, "def:SBase.SFoo").Status);
        Assert.True(Unit(r, "def:SBase.SFoo").Generated);
        Assert.Equal(UnitStatus.Ported, Unit(r, "def:Engine.Actor").Status);
        Assert.Equal(UnitStatus.NotApplicable, Unit(r, "def:SBase.SXBoxLiveRef").Status);
        Assert.Equal(UnitStatus.NotApplicable, Unit(r, "uc:SBase.SXBoxLiveRef:LogonEvent").Status);
        Assert.Equal("Xbox Live.", Unit(r, "nat:SBase.SXBoxLiveRef.Logon").NaReason);
        Assert.Equal(3, r.NaMatches[1]);
        Assert.Contains(r.Warnings, w => w.Contains("glob uc:SBase.SNothing:* matches no unit", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Warnings, w => w.Contains("def:SBase.SXBoxLiveRef", StringComparison.Ordinal));
    }

    [Fact]
    public void PortedUnitsKeepTheirStatusUnderNa()
    {
        const string status = "version: 1\nna:\n  - globs: [\"uc:SBase.SXBoxLiveRef:*\"]\n    reason: Xbox Live.\n";
        CoverageResult r = CoverageModel.Compute(Inputs(status: status, attrs: [PortOf("uc:SBase.SXBoxLiveRef:LogonEvent")]));
        Assert.Equal(UnitStatus.Ported, Unit(r, "uc:SBase.SXBoxLiveRef:LogonEvent").Status);
        Assert.Contains(r.Warnings, w => w.Contains("matches n-a entry 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitUnitsOverrideGlobs()
    {
        const string status = """
            version: 1
            units:
              uc:Engine.Actor:Reset:
                status: verified
                evidence: measured
              uc:SBase.SFoo:Trigger:
                status: n-a
                reason: Not needed.
            """;
        CoverageResult r = CoverageModel.Compute(Inputs(status: status));
        Assert.Equal(UnitStatus.Verified, Unit(r, "uc:Engine.Actor:Reset").Status);
        Assert.Equal(UnitStatus.NotApplicable, Unit(r, "uc:SBase.SFoo:Trigger").Status);
        Assert.Equal("Not needed.", Unit(r, "uc:SBase.SFoo:Trigger").NaReason);
    }

    [Fact]
    public void WrongIdsAndSpecsAreReported()
    {
        CoverageResult r = CoverageModel.Compute(Inputs(
            attrs:
            [
                PortOf("uc:SBase.SFoo:Nope"),
                PortOf("uc:SBase.SFoo:Trigger", spec: "spec/devices/missing.md#rules"),
                PortOf("uc:SBase.SFoo:Trigger", spec: "spec/devices/foo.md#no-such-heading", loc: "Y"),
                PortOf("nat:AActor::physMovingBrush", loc: "Z"),
                new FoundAttribute(ProvenanceAttributeKind.UcNative, "AActor::Tick", null, null, "OpenScct.Game", "W"),
            ],
            specs: [new SpecUnitRef("uc:SBase.SFoo:Renamed", "spec/devices/foo.md", 3)],
            scenarios: [new ScenarioInfo("devices.foo", "f", ["uc:SBase.SFoo:Missing"])]));

        Assert.Equal(3, r.Errors.Count);
        Assert.Contains(r.Errors, e => e.Contains("uc:SBase.SFoo:Nope is not in the inventory", StringComparison.Ordinal));
        Assert.Contains(r.Errors, e => e.Contains("spec/devices/missing.md does not exist", StringComparison.Ordinal));
        Assert.Contains(r.Errors, e => e.Contains("uc:SBase.SFoo:Missing is not in the inventory", StringComparison.Ordinal));
        Assert.Contains(r.Warnings, w => w.Contains("#no-such-heading", StringComparison.Ordinal));
        Assert.Equal(["spec/devices/foo.md:3: uc:SBase.SFoo:Renamed"], r.UnknownSpecIds);
        Assert.Equal(["eng:AActor::Tick", "eng:AActor::physMovingBrush"], r.Extra.Select(e => e.Id));
        Assert.All(r.Extra, e => Assert.Equal(UnitStatus.Ported, e.Status));
    }

    [Fact]
    public void BucketsWeighByBytecode()
    {
        CoverageResult r = CoverageModel.Compute(Inputs(attrs: [PortOf("uc:SBase.SFoo:Trigger")],
            status: "version: 1\nna:\n  - globs: [\"*SXBoxLiveRef*\"]\n    reason: Xbox Live.\n"));
        Bucket b = Bucket.Total("all", r.Units.Where(u => u.Unit!.Package == "SBase"));
        Assert.Equal(8, b.Units);
        Assert.Equal(3, b.NaUnits);
        Assert.Equal(300 + 100 + 40, b.InScopeBytes);
        Assert.Equal(300.0 / 440, b.Weighted(UnitStatus.Ported)!.Value, 6);
        Assert.Equal(1.0 / 5, b.ByUnits(UnitStatus.Ported)!.Value, 6);
        Assert.Null(new Bucket("empty").Weighted(UnitStatus.Ported));
    }

    [Fact]
    public void ReportsAreStableAndTheJsonParses()
    {
        CoverageInputs inputs = Inputs(attrs: [PortOf("uc:SBase.SFoo:Trigger")],
            specs: [new SpecUnitRef("uc:Engine.Actor:Reset", "spec/devices/foo.md", 3)]);
        string md1 = ReportWriter.Markdown(CoverageModel.Compute(inputs));
        string md2 = ReportWriter.Markdown(CoverageModel.Compute(inputs));
        Assert.Equal(md1, md2);
        Assert.Contains("## SteeE slice", md1, StringComparison.Ordinal);
        Assert.DoesNotContain(((char)0x2014).ToString(), md1, StringComparison.Ordinal);

        using JsonDocument doc = JsonDocument.Parse(ReportWriter.Json(CoverageModel.Compute(inputs)));
        JsonElement units = doc.RootElement.GetProperty("units");
        Assert.Equal("ported", units.GetProperty("uc:SBase.SFoo:Trigger").GetProperty("status").GetString());
        Assert.Equal("specced", units.GetProperty("uc:Engine.Actor:Reset").GetProperty("status").GetString());
        Assert.False(units.TryGetProperty("rep:SBase.SFoo.bOn", out _));
    }

    [Fact]
    public void HistoryKeepsOneRowPerDateAndCommit()
    {
        CoverageResult r = CoverageModel.Compute(Inputs());
        string row1 = ReportWriter.HistoryRow(r, "2026-10-23", "abc");
        string h = ReportWriter.UpsertHistory(null, row1);
        Assert.StartsWith(ReportWriter.HistoryHeader + "\n", h, StringComparison.Ordinal);
        h = ReportWriter.UpsertHistory(h, row1.Replace(",abc,", ",abc,", StringComparison.Ordinal));
        Assert.Equal(2, h.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        h = ReportWriter.UpsertHistory(h, ReportWriter.HistoryRow(r, "2026-10-24", "abc"));
        h = ReportWriter.UpsertHistory(h, ReportWriter.HistoryRow(r, "2026-10-24", "def"));
        string[] lines = h.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Equal(ReportWriter.HistoryHeader.Split(',').Length, lines[1].Split(',').Length);
    }

    [Fact]
    public void ExtraUnitsGroupByClassOrArea()
    {
        Assert.Equal("eng:ASBasePawn", ReportWriter.ExtraGroup("eng:ASBasePawn::physWalking"));
        Assert.Equal("eng:Render", ReportWriter.ExtraGroup("eng:Render.View"));
        Assert.Equal("rl:input", ReportWriter.ExtraGroup("rl:input.pad"));
        Assert.Equal("fmt:svmap", ReportWriter.ExtraGroup("fmt:svmap"));
    }
}
