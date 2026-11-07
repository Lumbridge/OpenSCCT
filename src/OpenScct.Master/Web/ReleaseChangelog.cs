using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Net.Discovery;

namespace OpenScct.Master.Web;

/// <summary>Public release notes shared by all visitors, with bounded requests and a last-good cache.</summary>
internal sealed class ReleaseChangelog
{
    public const string ReleasesUrl = "https://github.com/Lumbridge/OpenSCCT/releases";
    public const string EditorReleasesUrl = "https://github.com/Lumbridge/OpenSCCT-Editor/releases";
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly HttpClient _client;
    private readonly TimeProvider _time;
    private readonly bool _editor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ChangelogResponse _cached = new();
    private DateTimeOffset _refreshAfter;

    public ReleaseChangelog(HttpClient? client = null, TimeProvider? time = null)
        : this(client ?? Client, time ?? TimeProvider.System, editor: false) { }

    private ReleaseChangelog(HttpClient client, TimeProvider time, bool editor)
    {
        _client = client;
        _time = time;
        _editor = editor;
    }

    // Each product keeps its own last-good cache and retry interval; the game API remains game-only.
    public ReleaseChangelog CreateEditorFeed() => new(_client, _time, editor: true);

    private sealed record Release(string? TagName, string? Name, string? Body, DateTimeOffset? PublishedAt, bool Draft, bool Prerelease);

    public async Task<ChangelogResponse> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_time.GetUtcNow() < _refreshAfter) return _cached;
            try
            {
                // Do not tie the shared refresh to one visitor disconnecting. The client bounds it to five seconds.
                string repository = _editor ? "OpenSCCT-Editor" : "OpenSCCT";
                using HttpResponseMessage response = await _client.GetAsync("https://api.github.com/repos/Lumbridge/" + repository + "/releases?per_page=20", CancellationToken.None).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                Release[] releases = await response.Content.ReadFromJsonAsync<Release[]>(Json, CancellationToken.None).ConfigureAwait(false)
                    ?? throw new JsonException("Missing releases.");
                ChangelogEntry[] entries = releases.Where(r => !r.Draft && !r.Prerelease && r.PublishedAt.HasValue &&
                        r.TagName is { Length: > 1 and < 100 } tag && tag.StartsWith('v') && Version.TryParse(tag[1..], out _))
                    .OrderByDescending(r => r.PublishedAt)
                    .Take(12)
                    .Select(r => new ChangelogEntry
                    {
                        Tag = r.TagName!, Name = string.IsNullOrWhiteSpace(r.Name) ? (_editor ? "OpenSCCT Editor " : "OpenSCCT ") + r.TagName![1..] : r.Name,
                        Published = r.PublishedAt!.Value, Notes = r.Body ?? string.Empty,
                        Url = (_editor ? EditorReleasesUrl : ReleasesUrl) + "/tag/" + Uri.EscapeDataString(r.TagName!),
                    })
                    .ToArray();
                _cached = new ChangelogResponse { Entries = entries };
                _refreshAfter = _time.GetUtcNow().AddMinutes(10);
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException)
            {
                _cached = _cached with { Unavailable = true };
                _refreshAfter = _time.GetUtcNow().AddMinutes(2);
            }
            return _cached;
        }
        finally { _gate.Release(); }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSCCT-PlayerHub/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
