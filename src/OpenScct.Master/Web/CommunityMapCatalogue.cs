using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenScct.Master.Web;

internal sealed record CommunityMap(string Code, string Title, string[] Modes, string Credits, string Source, long DownloadBytes);

/// <summary>The website reads the same curated source recipes as the desktop launchers.
/// The embedded release snapshot keeps first visits and offline installations usable.</summary>
internal sealed class CommunityMapCatalogue
{
    internal const string Feed = "https://raw.githubusercontent.com/OpenSCCT/OpenSCCT-Content/main/community.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 1024 * 1024 };
    private readonly object _gate = new();
    private CommunityMap[] _maps;
    private DateTimeOffset _refreshAfter;

    public CommunityMapCatalogue()
    {
        using var snapshot = typeof(CommunityMapCatalogue).Assembly.GetManifestResourceStream("community-maps.json")!;
        using var reader = new StreamReader(snapshot);
        _maps = Read(reader.ReadToEnd());
    }

    public IReadOnlyList<CommunityMap> Current()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow >= _refreshAfter)
            {
                _refreshAfter = DateTimeOffset.UtcNow.AddMinutes(10);
                _ = RefreshAsync();
            }
            return _maps;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            CommunityMap[] next = Read(await Client.GetStringAsync(Feed).ConfigureAwait(false));
            lock (_gate) _maps = next;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
            or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            lock (_gate) _refreshAfter = DateTimeOffset.UtcNow.AddMinutes(2);
        }
    }

    internal static CommunityMap[] Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != "openscct.content" || root.GetProperty("formatVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported community catalogue.");
        var result = new List<CommunityMap>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root.GetProperty("maps").EnumerateArray())
        {
            if (entry.GetProperty("category").GetString() != "community" || !entry.TryGetProperty("steamRecipe", out var recipe)) continue;
            string code = entry.GetProperty("code").GetString() ?? "";
            if (code.Length is < 1 or > 64 || !char.IsAsciiLetterOrDigit(code[0]) ||
                !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') || !seen.Add(code))
                throw new InvalidDataException("Invalid or duplicate map identifier.");
            string source = recipe.GetProperty("evidence").GetString() ?? "";
            if (!source.StartsWith("https://github.com/OpenSCCT/SCCT-Maps/blob/", StringComparison.Ordinal))
                throw new InvalidDataException("Unknown map source.");
            string title = entry.GetProperty("title").GetString() ?? code;
            string[] modes = entry.GetProperty("modes").EnumerateArray().Select(v => v.GetString() ?? "")
                .Select(m => m == "DiskHunt" ? "Disk Hunt" : m).ToArray();
            long bytes = recipe.GetProperty("files").EnumerateArray().Sum(f => f.GetProperty("size").GetInt64());
            result.Add(new(code, title, modes, recipe.GetProperty("credits").GetString() ?? "", source, bytes));
        }
        return result.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
