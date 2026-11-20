using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Maps;

public sealed partial class ContentStore
{
    private async Task<string> BuildCommunityMapAsync(ContentMap map, SteamMapRecipe recipe, string staging,
        IProgress<PackProgress>? progress, CancellationToken cancel)
    {
        CommunityBuildContext context = CommunityBuildContext.Resolve(Root);
        foreach (string dependency in recipe.SteamFiles)
        {
            if (!File.Exists(Path.Combine(context.SteamVersusFolder, dependency)))
                throw new InvalidDataException("Steam is missing " + dependency + ". Verify Chaos Theory's files in Steam before building this map.");
        }

        string addons = Path.Combine(staging, "addons");
        long completed = 0;
        foreach (var file in recipe.Files)
        {
            cancel.ThrowIfCancellationRequested();
            string target = MapPaths.Under(addons, file.Destination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string installedSource = Path.Combine(context.SteamVersusFolder, file.Destination);
            if (File.Exists(installedSource))
            {
                if (new FileInfo(installedSource).Length != file.Size || Sha256Of(installedSource) != file.Sha256)
                    throw new InvalidDataException($"The installed {file.Destination} differs from the reviewed mod source. Use an unmodified Steam installation.");
                File.Copy(installedSource, target);
            }
            else
            {
                var transfer = progress is null ? null : new RecipeProgress(progress, completed, recipe.DownloadSize);
                string downloaded = await DownloadAsync(recipe.Download(file), transfer, cancel).ConfigureAwait(false);
                try { File.Move(downloaded, target); }
                finally { TryDelete(downloaded); }
            }
            completed += file.Size;
            progress?.Report(new PackProgress(completed, recipe.DownloadSize, false));
        }

        string output = await CommunityMapConverter.BuildAsync(context, map.Code, addons, Path.Combine(staging, "output"),
            progress, cancel).ConfigureAwait(false);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "manifest.json"), cancel).ConfigureAwait(false));
        var root = manifest.RootElement;
        if (!ContentIndex.Str(root, "map").Equals(map.Code, StringComparison.OrdinalIgnoreCase) ||
            !root.TryGetProperty("hashes", out var hashes) || ContentIndex.Str(hashes, "sim") is not { Length: 64 } hash ||
            !string.Equals(hash[..16], map.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The converted map does not match the reviewed gameplay version. Check your Steam files and builder version.");
        return output;
    }

    private sealed class RecipeProgress(IProgress<PackProgress> destination, long completed, long total) : IProgress<PackProgress>
    {
        public void Report(PackProgress value) => destination.Report(new PackProgress(completed + value.Received, total, false));
    }
}
