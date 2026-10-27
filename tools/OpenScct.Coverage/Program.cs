using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenScct.Coverage;

/// <summary>
/// Command line: <c>dotnet run --project tools/OpenScct.Coverage -- [options]</c>. Writes docs/progress/COVERAGE.md,
/// coverage.json and a history.csv row. Works without local data; verified status needs the local runner report.
/// </summary>
internal static class Program
{
    private const string Usage = """
        OpenScct.Coverage: coverage of the original game's units by the specs, the C# ports and the difftests.

        Options:
          --repo <dir>          repository root (default: the folder holding OpenSCCT.slnx above the current one)
          --inventory <file>    re/inventory/inventory.json
          --status <file>       coverage/status.yaml
          --assembly <file>     an assembly to scan; repeatable (default: each src/ project's newest bin output,
                                and game/'s Godot build when present; build the solution first)
          -c, --configuration <name>
                                read src/*/bin/<name> only (Debug or Release)
          --sim-report <file>   runner report (default: $SVM_DIFFTEST_SIM_OUT/report.json or
                                local/difftest/sim/report.json)
          --no-sim-report       ignore any runner report (what CI sees)
          --all-timings         a scenario passes only when every timing passes (default: the DtReplay gate)
          --out <dir>           output folder (default: docs/progress)
          --no-history          do not touch history.csv
          --check               write nothing; exit 1 when there are errors
          --strict              with --check, warnings fail too
        """;

    public static int Main(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value.");
            switch (a)
            {
                case "--repo": o.Repo = Next(); break;
                case "--inventory": o.Inventory = Next(); break;
                case "--status": o.Status = Next(); break;
                case "--assembly": o.Assemblies.Add(Next()); break;
                case "--configuration":
                case "-c": o.Configuration = Next(); break;
                case "--sim-report": o.SimReport = Next(); break;
                case "--no-sim-report": o.NoSimReport = true; break;
                case "--all-timings": o.AllTimings = true; break;
                case "--out": o.Out = Next(); break;
                case "--no-history": o.NoHistory = true; break;
                case "--check": o.Check = true; break;
                case "--strict": o.Strict = true; break;
                case "-h":
                case "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option {a}.");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        o.Repo ??= FindRepoRoot(Directory.GetCurrentDirectory());
        if (o.Repo is null)
        {
            Console.Error.WriteLine("Cannot find the repository root (OpenSCCT.slnx); pass --repo.");
            return 2;
        }

        CoverageResult result;
        try
        {
            result = Run(o);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 2;
        }

        Bucket t = Bucket.Total("all", result.Units);
        Bucket s = Bucket.Total("slice", result.Units.Where(u => u.Unit!.Slice));
        Console.WriteLine(Summary("all units", t));
        Console.WriteLine(Summary("SteeE slice", s));
        Console.WriteLine($"{result.Errors.Count} errors, {result.Warnings.Count} warnings, {result.UnknownSpecIds.Count} spec ids not in the inventory.");
        foreach (string e in result.Errors)
        {
            Console.Error.WriteLine("error: " + e);
        }

        if (o.Check)
        {
            return result.Errors.Count > 0 || (o.Strict && result.Warnings.Count > 0) ? 1 : 0;
        }

        string outDir = o.Out ?? Path.Combine(o.Repo, "docs", "progress");
        Directory.CreateDirectory(outDir);
        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(outDir, "COVERAGE.md"), ReportWriter.Markdown(result), utf8);
        File.WriteAllText(Path.Combine(outDir, "coverage.json"), ReportWriter.Json(result), utf8);
        if (!o.NoHistory)
        {
            string history = Path.Combine(outDir, "history.csv");
            string row = ReportWriter.HistoryRow(result, DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), GitDescribe(o.Repo));
            File.WriteAllText(history, ReportWriter.UpsertHistory(File.Exists(history) ? File.ReadAllText(history) : null, row), utf8);
        }

        Console.WriteLine($"Wrote {Path.GetRelativePath(o.Repo, outDir).Replace('\\', '/')}/COVERAGE.md, coverage.json{(o.NoHistory ? "" : ", history.csv")}.");
        return 0;
    }

    internal static CoverageResult Run(Options o)
    {
        string repo = o.Repo ?? throw new InvalidOperationException("No repository root.");
        var errors = new List<string>();
        var warnings = new List<string>();
        Inventory inv = Inventory.Load(o.Inventory ?? Path.Combine(repo, "re", "inventory", "inventory.json"));

        string statusPath = o.Status ?? Path.Combine(repo, "coverage", "status.yaml");
        StatusFile status = File.Exists(statusPath) ? StatusFile.Load(statusPath) : StatusFile.Empty;
        errors.AddRange(status.Errors);

        List<string> paths = o.Assemblies.Count > 0 ? o.Assemblies.Select(Path.GetFullPath).ToList() : DiscoverAssemblies(repo, o.Configuration, warnings);
        var assemblies = new List<ScannedAssembly>();
        foreach (string p in paths)
        {
            if (AttributeScanner.Scan(p) is { } scanned)
            {
                assemblies.Add(scanned);
            }
        }

        if (assemblies.Count == 0)
        {
            warnings.Add("no assemblies found: build the solution first (dotnet build OpenSCCT.slnx); nothing counts as ported");
        }

        IReadOnlyList<SimResult>? report = null;
        string? reportPath = null;
        if (!o.NoSimReport)
        {
            reportPath = o.SimReport ?? DefaultSimReport(repo);
            report = Scenarios.ReadReport(reportPath, warnings);
        }

        return CoverageModel.Compute(new CoverageInputs
        {
            RepoRoot = repo,
            Inventory = inv,
            Status = status,
            Specs = SpecUnits.ReadAll(repo),
            Assemblies = assemblies.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(),
            Scenarios = Scenarios.ReadAll(repo, errors),
            Report = report,
            ReportPath = reportPath,
            AllTimings = o.AllTimings,
            InputErrors = errors,
            InputWarnings = warnings,
        });
    }

    /// <summary>
    /// Each src/ project's own assembly from its bin output (the newest build, or the given configuration's), and the
    /// Godot project's assembly when it was built. A build older than its sources is reported.
    /// </summary>
    internal static List<string> DiscoverAssemblies(string repo, string? configuration, List<string> warnings)
    {
        var byName = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string src = Path.Combine(repo, "src");
        if (Directory.Exists(src))
        {
            foreach (string dir in Directory.EnumerateDirectories(src).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(dir);
                string bin = configuration is null ? Path.Combine(dir, "bin") : Path.Combine(dir, "bin", configuration);
                string? dll = Newest(Directory.Exists(bin)
                    ? Directory.EnumerateFiles(bin, name + ".dll", SearchOption.AllDirectories)
                    : []);
                if (dll is not null)
                {
                    WarnIfStale(repo, dir, dll, warnings);
                    byName[name] = dll;
                }
            }
        }

        string godotBin = Path.Combine(repo, "game", ".godot", "mono", "temp", "bin");
        if (Directory.Exists(godotBin))
        {
            string? dll = Newest(Directory.EnumerateFiles(godotBin, "OpenScctGame.dll", SearchOption.AllDirectories));
            if (dll is not null)
            {
                WarnIfStale(repo, Path.Combine(repo, "game"), dll, warnings);
                byName["OpenScctGame"] = dll;
            }
        }

        return byName.Values.ToList();
    }

    private static string? Newest(IEnumerable<string> files) =>
        files.Where(f => !f.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    private static void WarnIfStale(string repo, string projectDir, string dll, List<string> warnings)
    {
        DateTime built = File.GetLastWriteTimeUtc(dll);
        DateTime newest = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        if (newest > built)
        {
            warnings.Add($"{SpecUnits.Relative(repo, dll)} is older than its sources; rebuild for an exact report");
        }
    }

    private static bool IsBuildOutput(string path)
    {
        string p = path.Replace('\\', '/');
        return p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal) ||
               p.Contains("/.godot/", StringComparison.Ordinal);
    }

    private static string DefaultSimReport(string repo)
    {
        string? env = Environment.GetEnvironmentVariable("SVM_DIFFTEST_SIM_OUT");
        string dir = env is { Length: > 0 } ? env : Path.Combine(repo, "local", "difftest", "sim");
        return Path.Combine(dir, "report.json");
    }

    private static string Summary(string label, Bucket b) => string.Create(CultureInfo.InvariantCulture,
        $"{label}: {b.Units} units ({b.NaUnits} n-a), {b.InScopeBytes} in-scope bytes; specced+ {b.UnitsAtLeast(UnitStatus.Specced)} " +
        $"({Bucket.Pct(b.Weighted(UnitStatus.Specced))} w), ported+ {b.UnitsAtLeast(UnitStatus.Ported)} ({Bucket.Pct(b.Weighted(UnitStatus.Ported))} w), " +
        $"verified {b.UnitsAtLeast(UnitStatus.Verified)} ({Bucket.Pct(b.Weighted(UnitStatus.Verified))} w)");

    private static string GitDescribe(string repo)
    {
        try
        {
            var psi = new ProcessStartInfo("git", ["-C", repo, "describe", "--always", "--dirty", "--abbrev=10"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using Process? p = Process.Start(psi);
            if (p is null)
            {
                return "unknown";
            }

            string text = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 && text.Length > 0 ? text : "unknown";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "unknown";
        }
    }

    private static string? FindRepoRoot(string start)
    {
        for (DirectoryInfo? d = new(start); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "OpenSCCT.slnx")))
            {
                return d.FullName;
            }
        }

        return null;
    }

    internal sealed class Options
    {
        public string? Repo { get; set; }

        public string? Inventory { get; set; }

        public string? Status { get; set; }

        public List<string> Assemblies { get; } = [];

        public string? Configuration { get; set; }

        public string? SimReport { get; set; }

        public bool NoSimReport { get; set; }

        public bool AllTimings { get; set; }

        public string? Out { get; set; }

        public bool NoHistory { get; set; }

        public bool Check { get; set; }

        public bool Strict { get; set; }
    }
}
