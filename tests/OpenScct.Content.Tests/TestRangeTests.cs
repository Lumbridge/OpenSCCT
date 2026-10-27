using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Content.Collision;
using OpenScct.Content.Loading;

using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Content.Tests;

/// <summary>
/// The TestRange map made fresh by its generator (tools/testmap/generate.py) into a scratch folder, without the
/// game: the tracked station manifest is what the generator writes, the folder reads and loads with the game's
/// classes, and every station of the manifest is where the map's collision, GEs, zones, lights and actors say it is.
/// Skipped when Python with numpy and Pillow is not installed, unless SVM_REQUIRE_PYTHON is set.
/// </summary>
public sealed class TestRangeTests : IClassFixture<TestRangeTests.Generated>
{
    private const ETraceFlags World = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Others | ETraceFlags.TRACE_Movers;

    /// <summary>A trace stops short of the surface it hits (the UE2 pull-back); positions read from hits are within this.</summary>
    private const float Pull = 1f;

    private readonly Generated _g;

    public TestRangeTests(Generated generated) => _g = generated;

    /// <summary>One generation per test run.</summary>
    public sealed class Generated : IDisposable
    {
        private readonly ScratchDir _dir = new();

        public Generated()
        {
            Python = FindPython();
            if (Python is null)
            {
                return;
            }

            MapRoot = Path.Combine(_dir.Path, "TestRange");
            StationsPath = Path.Combine(_dir.Path, "stations.json");
            var psi = new ProcessStartInfo(Python)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = TestPaths.RepoRoot,
            };
            foreach (string a in new[] { "-B", Path.Combine(TestPaths.RepoRoot, "tools", "testmap", "generate.py"), "--out", MapRoot,
                         "--no-classes", "--stations-out", StationsPath })
            {
                psi.ArgumentList.Add(a);
            }

            using Process p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Output = stdout + stderr;
            ExitCode = p.ExitCode;
            if (ExitCode == 0)
            {
                Map = SvMap.Load(MapRoot);
                Physics = CollisionWorld.Build(Map.Collision);
                Manifest = JsonDocument.Parse(File.ReadAllBytes(StationsPath));
            }
        }

        public string? Python { get; }

        public string MapRoot { get; } = "";

        public string StationsPath { get; } = "";

        public string Output { get; } = "";

        public int ExitCode { get; } = -1;

        public SvMap? Map { get; }

        public CollisionWorld? Physics { get; }

        public JsonDocument? Manifest { get; }

        public void Dispose()
        {
            Manifest?.Dispose();
            _dir.Dispose();
        }

        private static string? FindPython()
        {
            foreach (string exe in new[] { "python", "python3", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add("import numpy, PIL, jsonschema");
                    using Process? p = Process.Start(psi);
                    if (p is null)
                    {
                        continue;
                    }

                    p.WaitForExit(30_000);
                    if (p.HasExited && p.ExitCode == 0)
                    {
                        return exe;
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }

            return null;
        }
    }

    private void Need()
    {
        if (_g.Python is null)
        {
            if (Environment.GetEnvironmentVariable("SVM_REQUIRE_PYTHON") is { Length: > 0 })
            {
                Assert.Fail("SVM_REQUIRE_PYTHON is set but no Python with numpy, Pillow and jsonschema was found.");
            }

            Assert.Skip("Python with numpy, Pillow and jsonschema is not installed.");
        }

        Assert.True(_g.ExitCode == 0, "the generator failed: " + _g.Output);
    }

    private SvMap Map => _g.Map!;

    private CollisionWorld Physics => _g.Physics!;

    private IEnumerable<JsonElement> Stations() => _g.Manifest!.RootElement.GetProperty("stations").EnumerateArray();

    private JsonElement Station(string id) => Stations().Single(s => s.GetProperty("id").GetString() == id);

    private static FVector V(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());

    private static FVector V(float x, float y, float z) => new(x, y, z);

    /// <summary>Where a line from <paramref name="start"/> to <paramref name="end"/> first hits the world, or null.</summary>
    private FCheckResult? Trace(FVector start, FVector end)
    {
        return Physics.LineCheck(out FCheckResult hit, null, end, start, default, World) ? hit : null;
    }

    private float FloorUnder(FVector p, float above = 60f, float below = 400f)
    {
        FCheckResult? hit = Trace(p + V(0f, 0f, above), p - V(0f, 0f, below));
        Assert.True(hit is not null, $"no floor under {p}");
        return hit!.Value.Location.Z;
    }

    private static void Near(float expected, float actual, float within = Pull) =>
        Assert.True(MathF.Abs(expected - actual) <= within, $"expected {expected}, got {actual}");

    [Fact]
    public void TrackedManifestIsWhatTheGeneratorWrites()
    {
        Need();
        string tracked = Path.Combine(TestPaths.RepoRoot, "fixtures", "testrange", "stations.json");
        Assert.True(File.Exists(tracked), "fixtures/testrange/stations.json is missing: run python -B tools/testmap/generate.py");
        Assert.True(File.ReadAllBytes(tracked).AsSpan().SequenceEqual(File.ReadAllBytes(_g.StationsPath)),
            "fixtures/testrange/stations.json is stale: run python -B tools/testmap/generate.py");
    }

    [Fact]
    public void MapReadsDerivesAndLoadsWithTheGameClasses()
    {
        Need();
        Assert.Equal("TestRange", Map.Id);
        Assert.Equal(Map.Manifest.SimHash, Map.SimHash);
        Assert.Empty(Map.Gameplay.CheckDerived());
        Assert.Empty(Map.Gameplay.CheckReferences());
        Assert.Empty(Map.Collision.CheckActorNames(Map.Gameplay));
        MapLoadResult loaded = new MapLoader(AllMapsTests.GameRegistry()).Load(new World(1, ENetMode.NM_DedicatedServer), Map);
        Assert.Equal(0, loaded.FailedSpawns);
        Assert.True(loaded.Level.Info.bBegunPlay);
        Assert.Equal(Map.Gameplay.Actors.Count, loaded.KnownCount + loaded.UnknownCount);
        foreach (UnappliedProp p in loaded.Unapplied)
        {
            Assert.False(p.Reason is UnappliedReason.TypeMismatch or UnappliedReason.WrongActorClass, $"{p.ClassPath}.{p.Prop} {p.Reason}");
        }

        // Every placed actor of the manifest is in the map at its location, with its class.
        foreach (JsonProperty a in _g.Manifest!.RootElement.GetProperty("actors").EnumerateObject())
        {
            ActorRecord? r = Map.Gameplay.Actors.SingleOrDefault(x => x.Id == a.Name);
            Assert.True(r is not null, $"{a.Name} is not in gameplay.json");
            Assert.Equal(a.Value.GetProperty("class").GetString(), r!.Class);
        }
    }

    [Fact]
    public void EveryStationStandsOnItsFloor()
    {
        Need();
        foreach (JsonElement s in Stations())
        {
            FVector spot = V(s.GetProperty("spot"));
            float floor = FloorUnder(spot);
            Assert.True(MathF.Abs(floor - spot.Z) < Pull, $"{s.GetProperty("id")}: floor {floor} under its spot {spot}");
        }
    }

    [Fact]
    public void LedgesHaveTheirLipAtTheirHeightWithAWallUnderIt()
    {
        Need();
        foreach (JsonElement s in Stations().Where(s => s.GetProperty("kind").GetString() is "ledge" or "coopBoost" or "wallJump"))
        {
            string id = s.GetProperty("id").GetString()!;
            float h = s.GetProperty("height").GetSingle();
            GERecord ge = Map.Traversal.Find(s.GetProperty("ge").GetString()!) ?? throw new InvalidOperationException(id);
            Assert.Equal(GEKind.LG, ge.Kind);
            Assert.Equal(h, ge.P0.Z);
            Assert.Equal(h, ge.P1.Z);
            FVector mid = (ge.P0 + ge.P1) * 0.5f;
            Near(h, FloorUnder(mid + V(0f, 8f, 0f), 40f, 40f));
            FVector spot = V(s.GetProperty("spot"));
            FCheckResult? face = Trace(V(mid.X, spot.Y, h - 20f), V(mid.X, mid.Y + 40f, h - 20f));
            Assert.True(face is not null && MathF.Abs(face.Value.Location.Y - mid.Y) < Pull, $"{id}: no wall under the lip");
            Assert.Null(Trace(V(mid.X, spot.Y, h + 20f), V(mid.X, mid.Y + 40f, h + 20f)));
        }
    }

    [Fact]
    public void ClimbsAndBarsAreWhereTheirGesAre()
    {
        Need();
        foreach (string id in new[] { "Pipe512", "Ladder320" })
        {
            JsonElement s = Station(id);
            GERecord ge = Map.Traversal.Find(s.GetProperty("ge").GetString()!)!;
            float top = s.GetProperty("height").GetSingle();
            Assert.Equal(0f, ge.Points[0].Z);
            Assert.Equal(top, ge.Points[1].Z);
            // The top is flush with the floor beyond it, and the side point is 10 toward the climber's spot.
            Near(top, FloorUnder(ge.Points[1] + V(0f, 60f, 0f), 30f, 30f));
            FVector spot = V(s.GetProperty("spot"));
            Assert.True((ge.Points[2] - spot).Size() < (ge.Points[0] - spot).Size());
            Assert.Equal(10f, (ge.Points[2] - ge.Points[0]).Size(), 3);
        }

        JsonElement hoh = Station("Hoh277");
        GERecord bar = Map.Traversal.Find("Hoh277HOH")!;
        Assert.Equal(GEKind.HOH, bar.Kind);
        Near(277f, bar.P0.Z - FloorUnder(V(hoh.GetProperty("spot")) + V(100f, 0f, 0f)));

        GERecord fence = Map.Traversal.Find("Fence376FENCE")!;
        Assert.Equal(GEKind.FENCE, fence.Kind);
        FVector fspot = V(Station("Fence376").GetProperty("spot"));
        FCheckResult? face = Trace(fspot + V(0f, 0f, 150f), fspot + V(0f, 300f, 150f));
        Assert.True(face is not null && (face.Value.MaterialFlags & CollisionMaterialFlags.Climbable) != 0, "the fence face is not climbable");
        Near(fence.P0.Z, FloorUnder(fence.P0 + V(10f, 0f, 2f), 4f, 20f));

        GERecord[] vault = [Map.Traversal.Find("Vault96LGS")!, Map.Traversal.Find("Vault96LGN")!];
        Assert.All(vault, g => Assert.Equal(96f, g.P0.Z));
        Assert.Equal(32f, vault[1].P0.Y - vault[0].P0.Y);
    }

    [Fact]
    public void ZipLineHasItsLengthSlopeAndLanding()
    {
        Need();
        JsonElement s = Station("Zip1760");
        GERecord zl = Map.Traversal.Find("Zip1760ZL")!;
        Assert.Equal(GEKind.ZL, zl.Kind);
        FVector high = V(s.GetProperty("high"));
        FVector low = V(s.GetProperty("low"));
        // Low end first, as in every real map's ZL: the rider faces along P0 - P1 flattened (physLinear).
        Assert.Equal(low, zl.P0);
        Assert.Equal(high, zl.P1);
        Assert.Equal(s.GetProperty("length").GetSingle(), (high - low).Size(), 2);
        Assert.Equal(s.GetProperty("drop").GetSingle(), high.Z - low.Z, 2);
        Near(304f, high.Z - FloorUnder(V(high.X, high.Y, 400f)));
        Near(208f, low.Z - FloorUnder(V(low.X, low.Y, 200f)));
        Near(s.GetProperty("landingTop").GetSingle(), FloorUnder(V(low.X, low.Y, 200f)));
        // Nothing on the line between its ends, nor where the rider hangs (202 under it).
        Assert.Null(Trace(high + V(0f, -16f, 0f), low + V(0f, 16f, 0f)));
        Assert.Null(Trace(high + V(0f, -120f, -150f), low + V(0f, 300f, -150f)));
    }

    [Fact]
    public void ShaftStepsRampAndDropsHaveTheirSizes()
    {
        Need();
        JsonElement split = Station("SplitShaft192");
        FVector sp = V(split.GetProperty("spot")) + V(0f, 0f, 100f);
        FCheckResult? west = Trace(sp, sp - V(400f, 0f, 0f));
        FCheckResult? east = Trace(sp, sp + V(400f, 0f, 0f));
        Assert.True(west is not null && east is not null);
        Near(192f, east!.Value.Location.X - west!.Value.Location.X, 2 * Pull);
        Near(37.5f, sp.X - west.Value.Location.X);

        foreach (JsonElement s in Stations().Where(s => s.GetProperty("kind").GetString() == "step"))
        {
            FVector spot = V(s.GetProperty("spot"));
            Near(s.GetProperty("height").GetSingle(), FloorUnder(spot + V(0f, 228f, 0f), 100f, 100f));
        }

        JsonElement stairs = Station("Stairs16");
        FVector foot = V(stairs.GetProperty("spot"));
        for (int k = 0; k < stairs.GetProperty("count").GetInt32(); k++)
        {
            Near(16f * (k + 1), FloorUnder(foot + V(100f + (32f * k) + 16f, 0f, 0f), 200f, 200f));
        }

        JsonElement ramp = Station("Ramp384");
        FVector rf = V(ramp.GetProperty("spot"));
        FCheckResult? slope = Trace(rf + V(500f, 0f, 600f), rf + V(500f, 0f, -10f));
        Assert.True(slope is not null);
        Assert.Equal(768f / MathF.Sqrt((768f * 768f) + (384f * 384f)), slope!.Value.Normal.Z, 3);

        foreach (string id in new[] { "Drop384", "Drop600" })
        {
            JsonElement d = Station(id);
            FVector spot = V(d.GetProperty("spot"));
            float yaw = d.GetProperty("yaw").GetInt32() * MathF.PI * 2f / 65536f;
            FVector beyond = spot + (V(MathF.Round(MathF.Cos(yaw)), MathF.Round(MathF.Sin(yaw)), 0f) * 120f);
            Near(d.GetProperty("height").GetSingle(), spot.Z - FloorUnder(beyond, 10f, 800f));
        }
    }

    [Fact]
    public void WaterIsShallowWithItsSurfaceType()
    {
        Need();
        JsonElement w = Station("Water");
        FVector spot = V(w.GetProperty("spot"));
        FCheckResult? hit = Trace(spot + V(0f, 0f, 60f), spot - V(0f, 0f, 60f));
        Assert.True(hit is not null);
        Near(-16f, hit!.Value.Location.Z);
        Assert.Equal((byte)w.GetProperty("surface").GetInt32(), hit.Value.SurfaceType);
    }

    [Fact]
    public void PanelWallHasGlassAnOpenWindowAndABulletPassableGrating()
    {
        Need();
        JsonElement s = Station("MotionPanels");
        FVector eye = V(s.GetProperty("watchFrom")) + V(0f, 0f, 150f);
        float x = s.GetProperty("walkFrom")[0].GetSingle();
        float zMid = (s.GetProperty("openZ")[0].GetSingle() + s.GetProperty("openZ")[1].GetSingle()) / 2f;
        var hits = new Dictionary<string, FCheckResult?>();
        foreach (JsonProperty o in s.GetProperty("openings").EnumerateObject())
        {
            float y = (o.Value[0].GetSingle() + o.Value[1].GetSingle()) / 2f;
            hits[o.Name] = Trace(V(eye.X, y, zMid), V(x, y, zMid));
        }

        Assert.Null(hits["open"]);
        Assert.True(hits["glass"] is { } glass && Map.Collision.Colliders[glass.Collider].Kind == ColliderKind.Glass, "the glass opening has no glass");
        Assert.True(hits["grating"] is { } grating &&
            (Map.Collision.Colliders[grating.Collider].Flags & ColliderFlags.BulletGoesThrough) != 0, "the grating does not let bullets through");
        // The posts between the openings block the line.
        Assert.NotNull(Trace(V(eye.X, 432f, zMid), V(x, 432f, zMid)));
    }

    [Fact]
    public void GradientRoomIsItsOwnZoneAndBrightensTowardTheLitDoor()
    {
        Need();
        JsonElement g = Station("Gradient");
        int roomZone = Physics.GetZoneIndex(V(g.GetProperty("spot")) + V(0f, 0f, 50f));
        Assert.NotEqual(Physics.GetZoneIndex(V(4096f, 3072f, 50f)), roomZone);
        AiLightField field = LightField(roomZone);
        float last = -1f;
        var levels = new List<float>();
        foreach (JsonElement p in g.GetProperty("samples").EnumerateArray())
        {
            FVector at = V(p) + V(0f, 0f, 50f);
            Assert.Equal(roomZone, Physics.GetZoneIndex(at));
            float level = field.LevelAround(at, 35f);
            levels.Add(level);
            Assert.True(level >= last - 1e-4f, $"light falls at {at}: {string.Join(", ", levels)}");
            last = level;
        }

        Assert.True(levels[0] < 0.02f, $"the dark end is lit: {levels[0]}");
        Assert.True(levels[^1] > 0.25f, $"the lit end is dark: {levels[^1]}");
    }

    [Fact]
    public void StartsStandOverTheirFloorFacingTheRange()
    {
        Need();
        foreach (string id in new[] { "SpyStarts", "MercStarts" })
        {
            JsonElement s = Station(id);
            foreach (JsonElement start in s.GetProperty("starts").EnumerateArray())
            {
                FVector at = V(start);
                Near(86f, at.Z - FloorUnder(at, 0f, 200f));
                ActorRecord r = Map.Gameplay.Actors.Single(a => a.Class == "Engine.PlayerStart" && a.Location == at);
                int t = r.Props.TryGetValue("TeamNumber", out PropValue team) && team.TryGetInt(out int n) ? n : -1;
                Assert.Equal(s.GetProperty("team").GetInt32(), t);
            }
        }

        JsonElement open = Station("OpenFloor");
        FVector lo = V(open.GetProperty("lo"));
        FVector hi = V(open.GetProperty("hi"));
        for (float x = lo.X + 64f; x < hi.X; x += 256f)
        {
            for (float y = lo.Y + 64f; y < hi.Y; y += 256f)
            {
                Near(0f, FloorUnder(V(x, y, 0f), 400f, 50f));
            }
        }
    }

    /// <summary>The bots' light field of the room from render/lighting.json, as OpenScct.Bots reads it (zone ambient and own-zone lights).</summary>
    private AiLightField LightField(int roomZone)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_g.MapRoot, "render", "lighting.json")));
        var zones = new Dictionary<string, int>(StringComparer.Ordinal);
        var ambient = new Dictionary<int, AiRgb>();
        foreach (JsonElement z in doc.RootElement.GetProperty("zones").EnumerateArray())
        {
            int index = z.GetProperty("zoneIndex").GetInt32();
            zones[z.GetProperty("name").GetString()!] = index;
            JsonElement a = z.GetProperty("ambient");
            ambient[index] = new AiRgb(a[0].GetSingle() / 255f, a[1].GetSingle() / 255f, a[2].GetSingle() / 255f);
        }

        Assert.Equal(roomZone, zones["ZoneRoom"]);
        var lights = new List<AiLight>();
        foreach (JsonElement l in doc.RootElement.GetProperty("lights").EnumerateArray())
        {
            lights.Add(new AiLight(V(l.GetProperty("location")), default, l.GetProperty("brightness").GetSingle(), (byte)l.GetProperty("hue").GetInt32(),
                (byte)l.GetProperty("saturation").GetInt32(), l.GetProperty("radius").GetSingle(), (byte)l.GetProperty("cone").GetInt32(),
                l.GetProperty("effect").GetInt32(), zones[l.GetProperty("zone").GetString()!], l.GetProperty("own_zone_only").GetBoolean()));
        }

        return new AiLightField(lights, ambient, Physics.GetZoneIndex);
    }
}
