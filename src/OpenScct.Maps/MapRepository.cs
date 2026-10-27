using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Maps;

/// <summary>
/// The SCCT-Maps repository (github.com/Lumbridge/SCCT-Maps), read without a token: one API call for the file
/// tree (pinned to its commit), then raw files and release assets, which are not rate limited. Every download is
/// checked against its size and Git blob id from that tree.
/// </summary>
public sealed class MapRepository
{
    public const string Owner = "Lumbridge";
    public const string Repo = "SCCT-Maps";
    public const string Branch = "main";
    public static readonly string WebUrl = $"https://github.com/{Owner}/{Repo}";

    private readonly HttpClient _http;

    public MapRepository(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// The maps of the repository's newest commit (or of the commit <paramref name="reference"/>, which a host
    /// names so players fetch its exact version), with the original maps' names and the released ports.
    /// </summary>
    public async Task<MapIndex> FetchCatalogAsync(CancellationToken cancel, string reference = Branch)
    {
        if (reference != Branch)
        {
            MapPaths.ValidateHash(reference);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using HttpRequestMessage request = Request($"https://api.github.com/repos/{Owner}/{Repo}/git/trees/{reference}?recursive=1");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new IOException("GitHub's request limit was reached. Your downloaded maps still work; try again in an hour.");
            }

            response.EnsureSuccessStatusCode();
            MapIndex catalog = MapIndexParser.ParseTree(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));

            if (await RawTextAsync(catalog.Commit, "release/names.json", 64 * 1024, timeout.Token).ConfigureAwait(false) is { } names)
            {
                try
                {
                    catalog = MapIndexParser.ApplyNames(catalog, MapIndexParser.ParseNames(names));
                }
                catch (System.Text.Json.JsonException)
                {
                    // The folder names stay.
                }
            }

            if (await RawTextAsync(catalog.Commit, "asset-catalog.json", 16 * 1024 * 1024, timeout.Token).ConfigureAwait(false) is { } released)
            {
                catalog = MapIndexParser.Merge(catalog, MapIndexParser.ParseReleased(released, catalog.Commit));
            }

            return catalog with { Maps = MapIndexParser.Ordered(catalog.Maps) };
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new IOException("The map list did not load in time. Check your internet connection and try again.");
        }
    }

    /// <summary>The map's notes (README.md) at the catalogue's commit, or null.</summary>
    public Task<string?> NotesAsync(MapPackage map, CancellationToken cancel) => RawTextAsync(map.Commit, map.NotesPath, 1024 * 1024, cancel);

    /// <summary>
    /// Downloads one file to <paramref name="target"/> (created new), stopping at more bytes than listed, then checks
    /// its size and Git blob id. A file that fails a check is deleted.
    /// </summary>
    public async Task DownloadAsync(MapPackage map, MapFile file, string target, IProgress<long>? received, CancellationToken cancel)
    {
        string url = file.Source.StartsWith("releases/download/", StringComparison.Ordinal)
            ? WebUrl + "/" + Escape(file.Source)
            : RawUrl(map.Commit, file.Source);
        try
        {
            using (HttpRequestMessage request = Request(url))
            using (HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using Stream input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                byte[] buffer = new byte[128 * 1024];
                long length = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) != 0)
                {
                    length += read;
                    if (length > file.Size)
                    {
                        throw new InvalidDataException($"{file.Destination} is larger than the map list says.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    received?.Report(read);
                }

                if (length != file.Size)
                {
                    throw new InvalidDataException($"{file.Destination} did not download completely.");
                }
            }

            if (!string.Equals(MapPaths.GitBlobSha1(target), file.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{file.Destination} does not match the map list (damaged download).");
            }
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    private async Task<string?> RawTextAsync(string commit, string path, int maxBytes, CancellationToken cancel)
    {
        try
        {
            using HttpRequestMessage request = Request(RawUrl(commit, path));
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
            {
                return null;
            }

            await using Stream input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            byte[] buffer = new byte[maxBytes + 1];
            int length = 0, read;
            while (length < buffer.Length && (read = await input.ReadAsync(buffer.AsMemory(length), cancel).ConfigureAwait(false)) != 0)
            {
                length += read;
            }

            return length > maxBytes ? null : Encoding.UTF8.GetString(buffer, 0, length);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("OpenSCCT");
        return request;
    }

    private static string RawUrl(string commit, string path)
    {
        MapPaths.ValidateHash(commit);
        return $"https://raw.githubusercontent.com/{Owner}/{Repo}/{commit}/{Escape(path)}";
    }

    private static string Escape(string path)
    {
        MapPaths.ValidateRelative(path);
        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next cleanup of the downloads folder.
        }
    }
}
