using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Content;

/// <summary>
/// The step "Build game content from your install". Releases carry our code only; maps, characters, UI, audio and
/// effects are converted on the player's machine from their own Splinter Cell Chaos Theory install by the content
/// pipeline (content/svmbuild), which each build ships with its own Python in <c>contenttools\</c>
/// (tools/release/Build-ContentTools.ps1). The output goes to the launcher's <c>content\</c> folder; Play passes it to
/// the game as <c>--svm-content=</c>. See docs/LAUNCHER.md, "Game content".
/// </summary>
public static class ContentBuildPlan
{
    /// <summary>The builder inside a version folder.</summary>
    public const string ToolsFolder = "contenttools";

    /// <summary>
    /// What the game needs, in run order (classes first: the map and character readers need the class defaults it
    /// writes). Per-map steps are maps and audio-map.
    /// </summary>
    public static readonly IReadOnlyList<string> Steps = ["classes", "chars", "audio", "ui", "maps-catalog", "renderpack", "fx", "maps", "audio-map"];

    public static readonly IReadOnlyList<string> PerMapSteps = ["maps", "audio-map"];

    /// <summary>The builder's Python in <paramref name="versionDir"/>, or null when the build has none.</summary>
    public static string? Python(string versionDir)
    {
        string exe = Path.Combine(versionDir, ToolsFolder, "python", "python.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>Worker processes for the per-map steps: half the cores, at most 6 (each map holds a lot in memory).</summary>
    public static int DefaultJobs => Math.Clamp(Environment.ProcessorCount / 2, 1, 6);

    /// <summary>The text language the builder uses when none is picked: English (the original's <c>.int</c> files).</summary>
    public const string DefaultLanguage = "int";

    /// <summary>
    /// The languages the original ships text in (System\_PC_ .int English, .fra, .deu, .esp, .ita): the builder's
    /// <c>--language</c> code and the name shown. Maps, menus and messages take the picked language's files; text an
    /// install has no file for in that language stays English.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
        [("int", "English"), ("fra", "French"), ("deu", "German"), ("esp", "Spanish"), ("ita", "Italian")];

    /// <summary>A known language code (any case), else English.</summary>
    public static string NormaliseLanguage(string? code)
    {
        string c = (code ?? string.Empty).Trim().ToLowerInvariant();
        return Languages.Any(l => l.Code == c) ? c : DefaultLanguage;
    }

    /// <summary>The languages <paramref name="gamePath"/> has text in (its GUI file in that language); English always; every language while no install is set.</summary>
    public static IReadOnlyList<(string Code, string Name)> LanguagesIn(string? gamePath)
    {
        string? folder = GameLocator.Normalize(gamePath) is { } versus ? Path.Combine(versus, "System", "_PC_") : null;
        return Languages.Where(l => l.Code == DefaultLanguage || (folder is null || File.Exists(Path.Combine(folder, "GUI." + l.Code)))).ToList();
    }

    /// <summary>The arguments after python.exe.</summary>
    public static IReadOnlyList<string> Arguments(string gamePath, string contentRoot, int jobs, string language = DefaultLanguage)
    {
        ArgumentException.ThrowIfNullOrEmpty(gamePath);
        ArgumentException.ThrowIfNullOrEmpty(contentRoot);
        var args = new List<string> { "-u", "-m", "svmbuild" };
        args.AddRange(Steps);
        args.AddRange(["--maps", "all", "--game", gamePath, "--out", contentRoot, "--jobs", Math.Max(1, jobs).ToString(CultureInfo.InvariantCulture),
            "--report", Path.Combine(contentRoot, "build-report.json")]);
        // Only when not English: a builder from before --language existed still runs an English build.
        string lang = NormaliseLanguage(language);
        if (lang != DefaultLanguage)
        {
            args.AddRange(["--language", lang]);
        }

        return args;
    }

    /// <summary>A quick check that a folder looks like an original install (System\ and Packages\).</summary>
    public static bool LooksLikeOriginalInstall(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Directory.Exists(Path.Combine(path, "System"))
        && Directory.Exists(Path.Combine(path, "Packages"));
}

/// <summary>Progress of a content build: units done of the units planned, and the last line the builder printed.</summary>
public sealed record ContentProgress(int Done, int Total, string Line);

/// <summary>What was last built into the content folder (content-build.json), so the launcher knows whether to build again.</summary>
public sealed class ContentState
{
    public const string FileName = "content-build.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The game version whose builder made the content.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>The original install it was built from.</summary>
    [JsonPropertyName("game")]
    public string? Game { get; set; }

    /// <summary>The text language it was built in (null: English, content from before the setting).</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("maps")]
    public int Maps { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTimeOffset FinishedAt { get; set; }

    public static ContentState? Load(string contentRoot)
    {
        try
        {
            string path = Path.Combine(contentRoot, FileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<ContentState>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string contentRoot)
    {
        Directory.CreateDirectory(contentRoot);
        AtomicFile.WriteAllText(Path.Combine(contentRoot, FileName), JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Whether content built for <paramref name="version"/> from <paramref name="game"/> in <paramref name="language"/> is there and complete.</summary>
    public bool IsCurrent(string version, string? game, string? language = ContentBuildPlan.DefaultLanguage) =>
        Ok && string.Equals(Version, version, StringComparison.OrdinalIgnoreCase)
        && ContentBuildPlan.NormaliseLanguage(Language) == ContentBuildPlan.NormaliseLanguage(language)
        && string.Equals(Path.GetFullPath(Game ?? "."), Path.GetFullPath(game ?? "."), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs the content builder as a child process (no window), reads its progress from its output and writes
/// <see cref="ContentState"/> when it ends. The builder's cache (content\cache\) makes a rebuild after an update
/// fast: only steps whose code or inputs changed run again. Cancelling stops the builder and its workers.
/// </summary>
public static class ContentBuilder
{
    public static async Task<ContentState> RunAsync(string python, string version, string gamePath, string contentRoot, int jobs,
        IProgress<ContentProgress>? progress, Action<string>? log, CancellationToken cancel, string language = ContentBuildPlan.DefaultLanguage)
    {
        language = ContentBuildPlan.NormaliseLanguage(language);
        ArgumentException.ThrowIfNullOrEmpty(python);
        Directory.CreateDirectory(contentRoot);
        var info = new ProcessStartInfo(python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = contentRoot,
        };
        foreach (string arg in ContentBuildPlan.Arguments(gamePath, contentRoot, jobs, language))
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["PYTHONUNBUFFERED"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";

        var counter = new UnitCounter();
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        void OnLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            log?.Invoke(line);
            if (counter.Read(line))
            {
                progress?.Report(new ContentProgress(counter.Done, counter.Total, line.Trim()));
            }
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("The content builder did not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using (cancel.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }))
        {
            await exited.Task.ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        cancel.ThrowIfCancellationRequested();
        var state = new ContentState
        {
            Version = version,
            Game = gamePath,
            Language = language,
            Ok = process.ExitCode == 0,
            Maps = counter.Maps,
            Failed = counter.Failed,
            FinishedAt = DateTimeOffset.UtcNow,
        };
        state.Save(contentRoot);
        return state;
    }

    /// <summary>
    /// Counts finished units from the builder's lines: "plan classes chars ... maps 83 jobs 4" gives the total
    /// (one per global step, one per map and per-map step), then each result line starts with its step name.
    /// </summary>
    private sealed class UnitCounter
    {
        private readonly HashSet<string> _steps = new(StringComparer.Ordinal);

        public int Total { get; private set; } = 1;

        public int Done { get; private set; }

        public int Maps { get; private set; }

        public int Failed { get; private set; }

        public bool Read(string line)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            if (parts[0] == "plan")
            {
                // "maps" is both a step name and the count label: the label is the one followed by a number.
                int end = parts.Length;
                for (int i = 1; i + 1 < parts.Length; i++)
                {
                    if (parts[i] is "maps" or "jobs" && int.TryParse(parts[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                    {
                        end = Math.Min(end, i);
                        if (parts[i] == "maps")
                        {
                            Maps = n;
                        }
                    }
                }

                var steps = parts.Skip(1).Take(end - 1).ToList();

                _steps.UnionWith(steps);
                int perMap = steps.Count(ContentBuildPlan.PerMapSteps.Contains);
                Total = Math.Max(1, steps.Count - perMap + perMap * Math.Max(1, Maps));
                return true;
            }

            // "<step> <unit> <status> <seconds>s <key>"
            if (_steps.Contains(parts[0]) && parts.Length >= 4 && parts[3].EndsWith('s'))
            {
                Done = Math.Min(Total, Done + 1);
                if (parts[2] == "failed")
                {
                    Failed++;
                }

                return true;
            }

            return false;
        }
    }
}
