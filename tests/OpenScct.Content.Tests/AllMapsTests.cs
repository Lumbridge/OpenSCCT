using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenScct.Content.Json;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Content.Tests;

/// <summary>
/// Local only: every converted map under <c>.content/svmap</c> (or <c>$SVM_CONTENT/svmap</c>) through the reader and
/// the loader. Skipped when no converted maps are present (CI has no game data). For each map: the sim hash equals
/// the manifest's (the pipeline's), the derived indexes rebuild to the stored ones with the 1.1 rules, every
/// reference and class table entry resolves, gameplay.json round-trips canonically, and the map loads twice, with
/// the default registry (Core and Engine classes) and with the game's (SBase classes and the device ports), with
/// every actor spawned. The report (actor counts, unknown classes, unapplied props, class default differences and
/// timings) goes to the test output and to <c>local/content/all-maps-load.md</c>.
/// </summary>
public sealed class AllMapsTests
{
    private readonly ITestOutputHelper _out;

    public AllMapsTests(ITestOutputHelper output) => _out = output;

    private sealed record MapRun(string Map, int Records, int Ges, string Format, MapLoadResult Default, MapLoadResult Game,
        int Attached, int AttachMissing, int BaseCandidates, int BspBounds, double ReadMs, double DeriveMs, double LoadDefaultMs,
        double LoadGameMs, List<string> Problems);

    /// <summary>The registry the game uses: generated Core, Engine and SBase classes, then the device ports.</summary>
    public static ActorClassRegistry GameRegistry()
    {
        ActorClassRegistry registry = ActorClassRegistry.Create(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);
        DeviceClasses.Register(registry);
        return registry;
    }

    [Fact]
    public void EveryConvertedMapReadsDerivesAndLoads()
    {
        string[] maps = TestPaths.ConvertedMaps();
        if (maps.Length == 0)
        {
            Assert.Skip($"No converted maps under {Path.Combine(TestPaths.ContentRoot, "svmap")} (local only).");
        }

        var runs = new List<MapRun>();
        var total = Stopwatch.StartNew();
        var defaultLoader = new MapLoader();
        var gameLoader = new MapLoader(GameRegistry());
        foreach (string root in maps)
        {
            runs.Add(Run(root, defaultLoader, gameLoader));
        }

        total.Stop();
        string report = Report(runs, total.Elapsed.TotalSeconds);
        File.WriteAllText(Path.Combine(TestPaths.LocalDir("content"), "all-maps-load.md"), report);
        _out.WriteLine(report);

        var failures = runs.Where(r => r.Problems.Count > 0).Select(r => $"{r.Map}: {string.Join("; ", r.Problems)}").ToList();
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static MapRun Run(string root, MapLoader defaultLoader, MapLoader gameLoader)
    {
        string name = Path.GetFileName(root);
        var problems = new List<string>();
        var clock = Stopwatch.StartNew();
        SvMap map;
        try
        {
            map = SvMap.Load(root);
        }
        catch (SvMapFormatException e)
        {
            problems.Add($"read: {e.Message}");
            return new MapRun(name, 0, 0, "?", null!, null!, 0, 0, 0, 0, 0, 0, 0, 0, problems);
        }

        double readMs = clock.Elapsed.TotalMilliseconds;
        if (map.SimHash != map.Manifest.SimHash)
        {
            problems.Add($"sim hash {map.SimHash} differs from the manifest's {map.Manifest.SimHash}");
        }

        clock.Restart();
        problems.AddRange(map.Gameplay.CheckDerived());
        problems.AddRange(map.Gameplay.CheckReferences());
        if (map.Classes is null)
        {
            problems.Add("no sim/classes.json");
        }
        else
        {
            problems.AddRange(map.Classes.Check(map.Gameplay));
        }

        byte[] file = File.ReadAllBytes(Path.Combine(root, GameplayDoc.RelativePath));
        if (!CanonicalJson.Canonicalize(file).AsSpan().SequenceEqual(CanonicalJson.ToCanonicalBytes(map.Gameplay.ToJson())))
        {
            problems.Add("gameplay.json does not round-trip canonically through GameplayDoc.ToJson");
        }

        int bspBounds = CheckBspBounds(map.Collision, problems);
        double deriveMs = clock.Elapsed.TotalMilliseconds;

        clock.Restart();
        MapLoadResult loadedDefault = defaultLoader.Load(new World(1, ENetMode.NM_DedicatedServer), map);
        double loadDefaultMs = clock.Elapsed.TotalMilliseconds;
        Check("default", loadedDefault, map, problems);

        clock.Restart();
        MapLoadResult loadedGame = gameLoader.Load(new World(1, ENetMode.NM_DedicatedServer), map);
        double loadGameMs = clock.Elapsed.TotalMilliseconds;
        Check("game", loadedGame, map, problems);

        int attached = loadedGame.AttachTagBases.Count(b => b.Base is not null);
        int missing = loadedGame.AttachTagBases.Count(b => b.Base is null);
        return new MapRun(name, map.Gameplay.AllActors.Count, map.Traversal.Ges.Count, map.Gameplay.FormatVersion, loadedDefault, loadedGame,
            attached, missing, loadedGame.StartupBaseCandidates().Count, bspBounds, readMs, deriveMs, loadDefaultMs, loadGameMs, problems);
    }

    /// <summary>BSP collision bounds (decoded LeafHulls): hulls with planes that belong to no collider. Returns how many nodes have one.</summary>
    private static int CheckBspBounds(Collision.CollisionData data, List<string> problems)
    {
        var owned = new bool[data.Hulls.Length];
        foreach (Collision.Collider c in data.Colliders)
        {
            for (long h = c.HullStart; h < (long)c.HullStart + c.HullCount; h++)
            {
                owned[h] = true;
            }
        }

        int count = 0;
        foreach (Collision.CollisionBspNode n in data.BspNodes)
        {
            if (n.CollisionBound < 0)
            {
                continue;
            }

            count++;
            if (!data.TryGetCollisionBound(n, out Collision.CollisionHull hull) || hull.PlaneCount == 0)
            {
                problems.Add($"BSP collision bound {n.CollisionBound} is not a hull with planes");
                break;
            }

            if (owned[n.CollisionBound])
            {
                problems.Add($"BSP collision bound {n.CollisionBound} is a collider's hull");
                break;
            }
        }

        return count;
    }

    private static void Check(string which, MapLoadResult loaded, SvMap map, List<string> problems)
    {
        if (loaded.FailedSpawns != 0)
        {
            problems.Add($"{which}: {loaded.FailedSpawns} spawns failed");
        }

        int placed = map.Gameplay.Actors.Count;
        if (loaded.KnownCount + loaded.UnknownCount != placed)
        {
            problems.Add($"{which}: {loaded.KnownCount} + {loaded.UnknownCount} actors for {placed} records");
        }

        if (!loaded.Level.Info.bBegunPlay)
        {
            problems.Add($"{which}: the level did not begin play");
        }

        foreach (UnappliedProp p in loaded.Unapplied)
        {
            if (p.Reason is UnappliedReason.TypeMismatch or UnappliedReason.WrongActorClass)
            {
                problems.Add($"{which}: {p.ClassPath}.{p.Prop} {p.Reason} x{p.Count}");
            }
        }

        for (int i = 0; i < loaded.Actors.Count; i++)
        {
            Actor? a = loaded.Actors[i];
            ActorRecord record = map.Gameplay.AllActors[i];
            if (a is null || !ReferenceEquals(loaded.RecordOf(a), record))
            {
                problems.Add($"{which}: no actor for {record.Id}");
                break;
            }
        }
    }

    // ---- report --------------------------------------------------------------------------------------------

    private static string Report(List<MapRun> runs, double seconds)
    {
        var sb = new StringBuilder();
        CultureInfo c = CultureInfo.InvariantCulture;
        sb.AppendLine("# All converted maps through OpenScct.Content");
        sb.AppendLine();
        sb.AppendLine(c, $"{runs.Count} maps in {seconds:F1} s. Default: generated Core and Engine classes. Game: plus SBase and the device ports.");
        sb.AppendLine();
        sb.AppendLine("| Map | Problems | Actors | Default ported | Default markers | Game ported | Game markers | Unknown classes (game) | Unapplied props (game, distinct / actors) | AttachTag based / missing | Base candidates | BSP bounds | Read ms | Derive ms | Load ms (default / game) |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (MapRun r in runs)
        {
            if (r.Default is null)
            {
                sb.AppendLine(c, $"| {r.Map} | {r.Problems.Count} | | | | | | | | | | | | | |");
                continue;
            }

            int unapplied = r.Game.UnappliedProps.Count;
            int unappliedActors = r.Game.UnappliedProps.Sum(p => p.Value);
            sb.AppendLine(c, $"| {r.Map} | {r.Problems.Count} | {r.Records} | {r.Default.KnownCount} | {r.Default.UnknownCount} | {r.Game.KnownCount} | {r.Game.UnknownCount} | {r.Game.UnknownClasses.Count} | {unapplied} / {unappliedActors} | {r.Attached} / {r.AttachMissing} | {r.BaseCandidates} | {r.BspBounds} | {r.ReadMs:F0} | {r.DeriveMs:F0} | {r.LoadDefaultMs:F0} / {r.LoadGameMs:F0} |");
        }

        List<MapRun> ok = runs.Where(r => r.Default is not null).ToList();
        sb.AppendLine();
        sb.AppendLine(c, $"Totals: {ok.Sum(r => r.Records)} actors, {ok.Sum(r => r.BspBounds)} BSP collision bounds; default {ok.Sum(r => r.Default.KnownCount)} ported, {ok.Sum(r => r.Default.UnknownCount)} markers; game {ok.Sum(r => r.Game.KnownCount)} ported, {ok.Sum(r => r.Game.UnknownCount)} markers.");
        sb.AppendLine(c, $"Times: read {ok.Sum(r => r.ReadMs) / 1000:F1} s, derive and check {ok.Sum(r => r.DeriveMs) / 1000:F1} s, load default {ok.Sum(r => r.LoadDefaultMs) / 1000:F1} s, load game {ok.Sum(r => r.LoadGameMs) / 1000:F1} s.");

        sb.AppendLine();
        sb.AppendLine("## Classes (game registry)");
        sb.AppendLine();
        sb.AppendLine("Kind: Generated (generated class, ported code yes/no), Registered (explicit port), Marker (no C# class).");
        sb.AppendLine("Class defaults applied: class table values that differ from the C# class's generated defaults.");
        sb.AppendLine();
        sb.AppendLine("| Class | Maps | Actors | C# class | Kind | Ported code | Class defaults applied | Class defaults without a member |");
        sb.AppendLine("|---|---:|---:|---|---|---|---|---:|");
        var classes = new SortedDictionary<string, (int Maps, int Actors, MapClassReport Report)>(StringComparer.Ordinal);
        foreach (MapRun r in ok)
        {
            foreach (MapClassReport cr in r.Game.Classes)
            {
                classes[cr.ClassPath] = classes.TryGetValue(cr.ClassPath, out var v) ? (v.Maps + 1, v.Actors + cr.Count, v.Report) : (1, cr.Count, cr);
            }
        }

        foreach (KeyValuePair<string, (int Maps, int Actors, MapClassReport Report)> kv in classes)
        {
            MapClassReport cr = kv.Value.Report;
            string applied = cr.DefaultsApplied.Count == 0 ? "" : $"{cr.DefaultsApplied.Count}: {string.Join(", ", cr.DefaultsApplied.Take(12))}{(cr.DefaultsApplied.Count > 12 ? ", ..." : "")}";
            sb.AppendLine(c, $"| {kv.Key} | {kv.Value.Maps} | {kv.Value.Actors} | {cr.CSharpType.Name} | {cr.Kind} | {(cr.HasPortedCode ? "yes" : "no")} | {applied} | {cr.DefaultsUnapplied.Count} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Markers (default registry)");
        sb.AppendLine();
        sb.AppendLine("Classes outside Core and Engine have no C# class in the default registry. Their markers take the Actor fields of the class table; the class's own properties have no member.");
        sb.AppendLine();
        sb.AppendLine("| Class | Maps | Actors | Class defaults applied (Actor fields) | Class defaults without a member |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        var markers = new SortedDictionary<string, (int Maps, int Actors, MapClassReport Report)>(StringComparer.Ordinal);
        foreach (MapRun r in ok)
        {
            foreach (MapClassReport cr in r.Default.Classes)
            {
                if (cr.Kind == MapClassKind.Marker)
                {
                    markers[cr.ClassPath] = markers.TryGetValue(cr.ClassPath, out var v) ? (v.Maps + 1, v.Actors + cr.Count, v.Report) : (1, cr.Count, cr);
                }
            }
        }

        foreach (KeyValuePair<string, (int Maps, int Actors, MapClassReport Report)> kv in markers)
        {
            sb.AppendLine(c, $"| {kv.Key} | {kv.Value.Maps} | {kv.Value.Actors} | {kv.Value.Report.DefaultsApplied.Count} | {kv.Value.Report.DefaultsUnapplied.Count} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Unapplied props (game registry), all maps");
        sb.AppendLine();
        sb.AppendLine("| Class.Prop | Reason | Maps | Actors |");
        sb.AppendLine("|---|---|---:|---:|");
        var props = new SortedDictionary<(string, UnappliedReason), (int Maps, int Actors)>();
        foreach (MapRun r in ok)
        {
            foreach (UnappliedProp p in r.Game.Unapplied)
            {
                var key = ($"{p.ClassPath}.{p.Prop}", p.Reason);
                props[key] = props.TryGetValue(key, out var v) ? (v.Maps + 1, v.Actors + p.Count) : (1, p.Count);
            }
        }

        var byReason = new SortedDictionary<UnappliedReason, (int Props, int Actors)>();
        foreach (var kv in props)
        {
            byReason[kv.Key.Item2] = byReason.TryGetValue(kv.Key.Item2, out var v) ? (v.Props + 1, v.Actors + kv.Value.Actors) : (1, kv.Value.Actors);
        }

        foreach (var kv in byReason)
        {
            sb.AppendLine(c, $"| (all) | {kv.Key} | {kv.Value.Props} props | {kv.Value.Actors} |");
        }

        foreach (var kv in props.OrderByDescending(p => p.Value.Actors).ThenBy(p => p.Key.Item1, StringComparer.Ordinal))
        {
            sb.AppendLine(c, $"| {kv.Key.Item1} | {kv.Key.Item2} | {kv.Value.Maps} | {kv.Value.Actors} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Problems");
        sb.AppendLine();
        foreach (MapRun r in runs)
        {
            foreach (string p in r.Problems)
            {
                sb.AppendLine(c, $"- {r.Map}: {p}");
            }
        }

        return sb.ToString();
    }
}
