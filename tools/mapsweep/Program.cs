using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Core;
using OpenScct.Server;

namespace OpenScct.MapSweep;

/// <summary>
/// <c>dotnet run --project tools/mapsweep -c Release -- [--maps=SteeE,SteeD|enhanced|default|host|all] [--seconds=240]
/// [--parallel=3] [--seed=1] [--out=local/test-results/mapsweep]</c>: sweeps the converted maps (docs/MAPS.md) and
/// writes one text and one JSON report per map plus <c>summary.md</c> into the output folder (under local/).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string mapsArg = "all";
        float seconds = 240f;
        int parallel = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 4));
        ulong seed = 1;
        string? outDir = null;
        string? mode = null;
        string? modes = null;
        int bots = 3;
        OpenScctGame.Play.Session.PlaySide side = OpenScctGame.Play.Session.PlaySide.Spy;
        foreach (string a in args)
        {
            string value = a.Contains('=', StringComparison.Ordinal) ? a[(a.IndexOf('=', StringComparison.Ordinal) + 1)..] : string.Empty;
            if (a.StartsWith("--maps=", StringComparison.Ordinal))
            {
                mapsArg = value;
            }
            else if (a.StartsWith("--seconds=", StringComparison.Ordinal))
            {
                seconds = float.Parse(value, CultureInfo.InvariantCulture);
            }
            else if (a.StartsWith("--parallel=", StringComparison.Ordinal))
            {
                parallel = Math.Max(1, int.Parse(value, CultureInfo.InvariantCulture));
            }
            else if (a.StartsWith("--seed=", StringComparison.Ordinal))
            {
                seed = ulong.Parse(value, CultureInfo.InvariantCulture);
            }
            else if (a.StartsWith("--out=", StringComparison.Ordinal))
            {
                outDir = value;
            }
            else if (a.StartsWith("--place=", StringComparison.Ordinal))
            {
                return PlaceRun.Run(value);
            }
            else if (a == "--catalog")
            {
                return CatalogTable.Print();
            }
            else if (a.StartsWith("--probe=", StringComparison.Ordinal))
            {
                return NavProbe.Run(value);
            }
            else if (a.StartsWith("--bots=", StringComparison.Ordinal))
            {
                bots = int.Parse(value, CultureInfo.InvariantCulture);
            }
            else if (a.StartsWith("--side=", StringComparison.Ordinal))
            {
                side = value == "merc" ? OpenScctGame.Play.Session.PlaySide.Merc : OpenScctGame.Play.Session.PlaySide.Spy;
            }
            else if (a.StartsWith("--mode=", StringComparison.Ordinal))
            {
                mode = value;
            }
            else if (a.StartsWith("--modes=", StringComparison.Ordinal))
            {
                modes = value;
            }
            else
            {
                Console.Error.WriteLine("unknown argument " + a);
                return 2;
            }
        }

        List<string> maps = SelectMaps(mapsArg);
        if (maps.Count == 0)
        {
            Console.Error.WriteLine("no converted maps match '" + mapsArg + "' (convert them into .content/svmap first)");
            return 2;
        }

        List<(string Map, string? Mode)> jobs = Jobs(maps, mode, modes);
        bool perMode = modes is not null;
        string root = RepoRoot();
        outDir ??= Path.Combine(root, "local", "test-results", "mapsweep");
        Directory.CreateDirectory(outDir);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"sweeping {jobs.Count} runs on {maps.Count} maps, {seconds:F0} s each, {parallel} at a time, into {outDir}"));
        var reports = new MapSweepReport[jobs.Count];
        object gate = new();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
        {
            (string map, string? runMode) = jobs[i];
            var sweeper = new MapSweeper(new MapSweepOptions { Map = map, Mode = runMode, Seconds = seconds, Seed = seed, Side = side, Bots = bots });
            MapSweepReport r;
            try
            {
                r = sweeper.Run(line =>
                {
                    lock (gate)
                    {
                        Console.WriteLine(line);
                    }
                });
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                r = new MapSweepReport { Map = map, Mode = runMode ?? MapSweepOptions.ModeOf(map), LoadError = "sweep failed: " + MapSweeper.Describe(e) };
            }

            reports[i] = r;
            string file = perMode ? map + "-" + r.Mode : map;
            File.WriteAllText(Path.Combine(outDir, file + ".txt"), r + Environment.NewLine);
            File.WriteAllText(Path.Combine(outDir, file + ".json"), JsonSerializer.Serialize(r, JsonOptions));
            lock (gate)
            {
                Console.WriteLine(r);
            }

            GC.Collect();
        });

        File.WriteAllText(Path.Combine(outDir, "summary.md"), Summary(reports));
        Console.WriteLine("summary: " + Path.Combine(outDir, "summary.md"));
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// The maps of <c>--maps</c>: <c>all</c> every installed map of the catalog (menu, training and test maps too),
    /// <c>host</c> the host list, <c>enhanced</c> the Enhanced versions (the E maps), <c>default</c> the default maps
    /// (no variant word: the D maps and the like), or a comma list.
    /// </summary>
    private static List<string> SelectMaps(string arg)
    {
        MapCatalog catalog = MapCatalog.CreateDefault();
        var installed = catalog.GetCatalog().Where(e => e.Folder is not null).ToList();
        var codes = installed.Select(e => e.Id).Where(id => id.Length == 5).OrderBy(id => id, StringComparer.Ordinal).ToList();
        return arg.ToLowerInvariant() switch
        {
            "all" => installed.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            "host" => catalog.GetHostMaps().Select(e => e.Id).ToList(),
            "enhanced" => installed.Where(e => e.Kind == MapKind.Versus && e.VariantWord is "Enhanced" or "Enh").Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            "default" => installed.Where(e => e.Kind == MapKind.Versus && e.VariantWord is null).Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            _ => arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        };
    }

    /// <summary>
    /// The runs: one per map in <c>--mode</c> (or the map's kind), or with <c>--modes=all</c> one per mode that plays
    /// on the map (the catalog's <see cref="MapModeInfo.Playable"/>; a front end map is loaded once), or with
    /// <c>--modes=Story,DiskHunt</c> those modes where they play.
    /// </summary>
    private static List<(string Map, string? Mode)> Jobs(List<string> maps, string? mode, string? modes)
    {
        var jobs = new List<(string, string?)>();
        if (modes is null)
        {
            jobs.AddRange(maps.Select(m => (m, mode)));
            return jobs;
        }

        MapCatalog catalog = MapCatalog.CreateDefault();
        var wanted = modes.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : modes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string map in maps)
        {
            MapCatalogEntry? e = catalog.GetCatalogEntry(map);
            if (e is null || e.Kind == MapKind.Menu)
            {
                jobs.Add((map, "Load"));
                continue;
            }

            foreach (EGameMode m in e.PlayableModes)
            {
                string key = MapCatalog.ModeKey(m);
                if (wanted is null || wanted.Contains(key))
                {
                    jobs.Add((map, key));
                }
            }
        }

        return jobs;
    }

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "OpenSCCT.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? Directory.GetCurrentDirectory();
    }

    private static string Summary(IEnumerable<MapSweepReport> reports)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Map | Mode | Status | Load s | Markers | Starts | Objectives | End | Issues |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (MapSweepReport r in reports)
        {
            string issues = string.Join("; ", r.Issues.Where(i => i.Area != "class").Select(i => i.ToString().Replace("|", "/", StringComparison.Ordinal)));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {r.Map} | {r.Mode} | {r.Status} | {r.LoadSeconds:F1} | {r.UnknownActors} | {r.PlayerStarts} | {r.ObjectivesTaken}/{r.ObjectivesNeeded} of {r.Objectives} | {(r.Ended ? r.Reason + " " + r.SimSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s" : "-")} | {(r.LoadError ?? issues)} |"));
        }

        return sb.ToString();
    }
}
