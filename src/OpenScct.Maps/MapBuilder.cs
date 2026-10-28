using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Maps;

/// <summary>
/// The content builder (content/svmbuild) and how to start it: a game build ships it in <c>contenttools\</c> with its
/// own Python (tools/release/Build-ContentTools.ps1); a checkout runs it from <c>content\</c> with the Python on PATH.
/// </summary>
public sealed record BuilderTool(string Python, string? WorkingDirectory)
{
    /// <summary>The folder of a game build that holds the builder.</summary>
    public const string ToolsFolder = "contenttools";

    /// <summary>The builder shipped in the game build at <paramref name="buildDir"/>, or null.</summary>
    public static BuilderTool? InBuild(string buildDir)
    {
        string python = Path.Combine(buildDir, ToolsFolder, "python", "python.exe");
        return File.Exists(python) ? new BuilderTool(python, null) : null;
    }

    /// <summary>The builder of a checkout (<c>&lt;repo&gt;\content\svmbuild</c>) run with "python" from PATH, or null.</summary>
    public static BuilderTool? InCheckout(string repoRoot) =>
        File.Exists(Path.Combine(repoRoot, "content", "svmbuild", "gamefiles.py"))
            ? new BuilderTool("python", Path.Combine(repoRoot, "content"))
            : null;

    /// <summary>Whether this builder reads downloaded maps (<c>--addons</c>, svmbuild/gamefiles.py).</summary>
    public bool SupportsAddons(string buildDir) =>
        WorkingDirectory is not null
        || File.Exists(Path.Combine(buildDir, ToolsFolder, "repo", "content", "svmbuild", "gamefiles.py"));

    /// <summary>
    /// Runs the builder with <paramref name="arguments"/> (after <c>-u -m svmbuild</c>), no window; every output line
    /// goes to <paramref name="onLine"/>. Cancelling stops it and its workers. Returns the exit code.
    /// </summary>
    public async Task<int> RunAsync(IEnumerable<string> arguments, string fallbackDirectory, Action<string>? onLine, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(Python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = WorkingDirectory ?? fallbackDirectory,
        };
        foreach (string arg in new[] { "-u", "-m", "svmbuild" }.Concat(arguments))
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["PYTHONUNBUFFERED"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        // "start <step> <unit>" lines as each unit begins real work (older builders ignore it).
        info.Environment["SVM_PROGRESS"] = "1";
        Directory.CreateDirectory(info.WorkingDirectory);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine?.Invoke(e.Data);
            }
        };
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
        return process.ExitCode;
    }
}

/// <summary>
/// Converts downloaded maps into the game's content while it runs (a player fetching the map a lobby picked): only
/// those maps, plus the shared steps that list or index them; the builder's cache keeps everything else.
/// </summary>
public static class MapBuilder
{
    /// <summary>The steps for a few new maps: the map list and pictures, the shared render index, the maps and their sound.</summary>
    public static readonly IReadOnlyList<string> Steps = ["maps-catalog", "renderpack", "maps", "audio-map"];

    /// <summary>The builder's arguments (after <c>-m svmbuild</c>) to convert <paramref name="codes"/>.</summary>
    public static IReadOnlyList<string> Arguments(string gamePath, string contentRoot, string addons, IEnumerable<string> codes, string? language)
    {
        var args = new List<string>(Steps)
        {
            "--maps", string.Join(',', codes), "--game", gamePath, "--out", contentRoot, "--addons", addons,
        };
        if (!string.IsNullOrEmpty(language) && language != ContentBuildFile.English)
        {
            args.AddRange(["--language", language]);
        }

        return args;
    }
}

/// <summary>
/// The launcher's record of the last content build (<c>content-build.json</c> in the content folder), read and
/// patched without the launcher's types: its language, and the downloaded maps it covers.
/// </summary>
public static class ContentBuildFile
{
    public const string FileName = "content-build.json";
    public const string English = "int";

    /// <summary>The text language the content was built in (English when unknown).</summary>
    public static string Language(string contentRoot) =>
        Read(contentRoot)?["language"]?.GetValue<string>() is { Length: > 0 } language ? language : English;

    /// <summary>
    /// After an in-game build of newly downloaded maps: when the content covered the downloaded maps as they were
    /// before (<paramref name="before"/>), it now covers them as they are (<paramref name="after"/>), so the launcher
    /// does not prepare the game files again for them.
    /// </summary>
    public static void AdvanceDownloadedMaps(string contentRoot, string before, string after)
    {
        if (Read(contentRoot) is not { } doc || doc["ok"]?.GetValue<bool>() != true)
        {
            return;
        }

        string recorded = doc["downloadedMaps"]?.GetValue<string>() ?? string.Empty;
        if (recorded != before)
        {
            return;
        }

        doc["downloadedMaps"] = after.Length > 0 ? after : null;
        MapPaths.WriteAllTextAtomic(Path.Combine(contentRoot, FileName), doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonObject? Read(string contentRoot)
    {
        try
        {
            string path = Path.Combine(contentRoot, FileName);
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
