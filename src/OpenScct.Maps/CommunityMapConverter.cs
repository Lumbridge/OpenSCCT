using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Maps;

/// <summary>Builds one verified community source map in a disposable staging folder.</summary>
public static class CommunityMapConverter
{
    public static async Task<string> BuildAsync(CommunityBuildContext context, string code, string addons, string output,
        IProgress<PackProgress>? progress, CancellationToken cancel)
    {
        if (!ContentIndex.IsSafeCode(code) || SteamVersusMaps.Contains(code) || code.Contains(','))
            throw new InvalidDataException("Invalid community map code: " + code);
        cancel.ThrowIfCancellationRequested();
        string stage = Path.GetFullPath(output);
        foreach (string input in new[] { context.SteamVersusFolder, context.ContentRoot, addons })
        {
            if (Contains(input, stage) || Contains(stage, input))
                throw new InvalidOperationException("Map staging must be separate from Steam, the active build and the downloaded source.");
        }
        string source = MapPaths.Under(addons, $"Packages/Maps/{code}.sdc");
        if (!File.Exists(source)) throw new FileNotFoundException("The source download is missing the selected map.", source);
        Directory.CreateDirectory(stage);
        if (Directory.EnumerateFileSystemEntries(stage).Any())
            throw new InvalidOperationException("The community map needs an empty staging folder.");

        // Map generation needs just these two indices. The runtime reads the referenced waves and class data
        // from the active Steam build. No writable links into that build or into Steam are required.
        foreach (string file in new[] { "shared/classes.json", "shared/audio/audio.json" })
        {
            string dest = Path.Combine(stage, file);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(Path.Combine(context.ContentRoot, file), dest);
        }
        string? lastLine = null;
        progress?.Report(new PackProgress(0, 2, true));
        int result = await context.Tool.RunAsync(["maps", "audio-map", "--no-deps", "--no-cache", "--maps", code,
            "--game", Path.GetFullPath(context.SteamVersusFolder), "--addons", Path.GetFullPath(addons), "--out", stage],
            stage, line =>
            {
                if (line.Contains("failed", StringComparison.OrdinalIgnoreCase)) Interlocked.Exchange(ref lastLine, line);
                if (line.StartsWith("maps ", StringComparison.Ordinal) && line.Contains("built", StringComparison.Ordinal))
                    progress?.Report(new PackProgress(1, 2, true));
            }, cancel, cleanEnvironment: true).ConfigureAwait(false);
        if (result != 0) throw new InvalidDataException(lastLine ?? $"The converter failed to build {code} (exit {result}).");
        string map = Path.Combine(stage, "svmap", code);
        foreach (string file in new[] { "manifest.json", "sim/gameplay.json", "sim/collision.bin", "render/world.glb", "audio/audio.json" })
            if (new FileInfo(Path.Combine(map, file)) is not { Exists: true, Length: > 0 })
                throw new InvalidDataException($"The converted map {code} is missing {file}.");
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(map, "manifest.json"), cancel).ConfigureAwait(false));
        string? consumed = manifest.RootElement.GetProperty("source").GetProperty("original").GetProperty("sha256").GetString();
        using var stream = File.OpenRead(source);
        string expected = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false));
        if (!string.Equals(expected, consumed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"A different {code}.sdc in the Steam installation shadowed the selected source.");
        progress?.Report(new PackProgress(2, 2, true));
        return map;
    }

    private static bool Contains(string root, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
