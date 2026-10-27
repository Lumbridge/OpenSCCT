using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.GitHub;

/// <summary>Where releases come from. A token is only needed while the repository is private.</summary>
public sealed record ReleaseSource(string Owner, string Repo, string? Token)
{
    public const string DefaultOwner = "Lumbridge";
    public const string DefaultRepo = "OpenSCCT";

    public bool Authenticated => !string.IsNullOrEmpty(Token);
}

/// <summary>
/// Reads the release list of a repository with conditional requests. The last answer and its ETag are cached on
/// disk, so a check that finds nothing new costs no rate limit (GitHub does not count 304 answers), and the launcher
/// still shows the last known releases when it is offline or rate limited (60 requests an hour without a token).
/// </summary>
public sealed class GitHubReleaseClient
{
    public const string ApiBase = "https://api.github.com";
    public const int PageSize = 30;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ReleaseSource _source;
    private readonly string _cacheDir;
    private readonly TimeProvider _time;

    public GitHubReleaseClient(HttpClient http, ReleaseSource source, string cacheDir, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(cacheDir);
        _http = http;
        _source = source;
        _cacheDir = cacheDir;
        _time = time ?? TimeProvider.System;
    }

    public string ReleasesUrl =>
        $"{ApiBase}/repos/{Uri.EscapeDataString(_source.Owner)}/{Uri.EscapeDataString(_source.Repo)}/releases?per_page={PageSize.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The release list, newest first as GitHub returns it, drafts removed.</summary>
    public async Task<ReleaseFeed> GetReleasesAsync(CancellationToken cancel)
    {
        CacheEntry? cached = LoadCache();
        DateTimeOffset now = _time.GetUtcNow();

        if (cached?.RateLimitedUntil is { } until && until > now)
        {
            return FromCache(cached, until, "GitHub rate limit reached; showing the last known releases.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        AddApiHeaders(request, "application/vnd.github+json");
        if (cached?.ETag is { Length: > 0 } etag && EntityTagHeaderValue.TryParse(etag, out EntityTagHeaderValue? tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return FromCache(cached, null, "GitHub could not be reached (" + ex.Message + ").");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return FromCache(cached, null, "GitHub did not answer in time.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                return new ReleaseFeed(Visible(cached.Releases), ReleaseFeedSource.NotModified, null, null);
            }

            if (RateLimit.IsRateLimited(response, now, out DateTimeOffset reset))
            {
                if (cached is not null)
                {
                    cached.RateLimitedUntil = reset;
                    SaveCache(cached);
                }

                string hint = _source.Authenticated ? string.Empty : " Add a token in Settings for a higher limit.";
                return FromCache(cached, reset, "GitHub rate limit reached until " + reset.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) + "." + hint);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                string why = _source.Authenticated
                    ? $"Repository {_source.Owner}/{_source.Repo} not found, or the token cannot read it."
                    : $"Repository {_source.Owner}/{_source.Repo} not found. A private repository needs a token in Settings.";
                return FromCache(cached, null, why);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return FromCache(cached, null, "GitHub refused the token (401). Check it in Settings.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return FromCache(cached, null, $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            string body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            List<ReleaseInfo> releases;
            try
            {
                releases = JsonSerializer.Deserialize<List<ReleaseInfo>>(body, Json) ?? [];
            }
            catch (JsonException ex)
            {
                return FromCache(cached, null, "GitHub sent a release list that could not be read (" + ex.Message + ").");
            }

            var entry = new CacheEntry
            {
                ETag = response.Headers.ETag?.ToString(),
                Releases = releases,
                FetchedAt = now,
            };
            SaveCache(entry);
            return new ReleaseFeed(Visible(releases), ReleaseFeedSource.Network, null, null);
        }
    }

    /// <summary>
    /// One release by its tag (GET /releases/tags/{tag}), for a release older than the first page of the list.
    /// Null when there is none, it is a draft, or GitHub cannot be asked.
    /// </summary>
    public async Task<ReleaseInfo?> GetReleaseByTagAsync(string tag, CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        string url = $"{ApiBase}/repos/{Uri.EscapeDataString(_source.Owner)}/{Uri.EscapeDataString(_source.Repo)}/releases/tags/{Uri.EscapeDataString(tag)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddApiHeaders(request, "application/vnd.github+json");
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            ReleaseInfo? release = JsonSerializer.Deserialize<ReleaseInfo>(body, Json);
            return release is { Draft: false } ? release : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancel.IsCancellationRequested))
        {
            return null;
        }
    }

    /// <summary>A request for an asset's bytes through the API, which also works for a private repository.</summary>
    public HttpRequestMessage CreateAssetRequest(ReleaseAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string url = !string.IsNullOrEmpty(asset.Url) ? asset.Url : asset.BrowserDownloadUrl ?? throw new ArgumentException("Asset has no URL.", nameof(asset));
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // HttpClient drops the Authorization header when GitHub redirects to its storage host, as it should.
        AddApiHeaders(request, "application/octet-stream");
        return request;
    }

    private void AddApiHeaders(HttpRequestMessage request, string accept)
    {
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (request.Headers.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.ParseAdd("OpenSCCT-Launcher");
        }

        if (_source.Authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _source.Token);
        }
    }

    private static List<ReleaseInfo> Visible(IEnumerable<ReleaseInfo> releases) => releases.Where(r => !r.Draft).ToList();

    private static ReleaseFeed FromCache(CacheEntry? cached, DateTimeOffset? reset, string problem) =>
        cached is null
            ? new ReleaseFeed([], ReleaseFeedSource.None, reset, problem)
            : new ReleaseFeed(Visible(cached.Releases), ReleaseFeedSource.Cache, reset, problem);

    // The ETag depends on who asks, so anonymous and token answers are cached apart.
    private string CachePath =>
        Path.Combine(_cacheDir, $"releases-{Safe(_source.Owner)}-{Safe(_source.Repo)}-{(_source.Authenticated ? "auth" : "anon")}.json");

    private static string Safe(string s) => string.Concat(s.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));

    private CacheEntry? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(CachePath), Json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveCache(CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            AtomicFile.WriteAllText(CachePath, JsonSerializer.Serialize(entry, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written only costs a full request next time.
        }
    }

    internal sealed class CacheEntry
    {
        public string? ETag { get; set; }

        public DateTimeOffset FetchedAt { get; set; }

        public DateTimeOffset? RateLimitedUntil { get; set; }

        public List<ReleaseInfo> Releases { get; set; } = [];
    }
}

/// <summary>Reads GitHub's rate limit answers: 403 or 429 with no requests left, or a Retry-After.</summary>
public static class RateLimit
{
    public static bool IsRateLimited(HttpResponseMessage response, DateTimeOffset now, out DateTimeOffset reset)
    {
        ArgumentNullException.ThrowIfNull(response);
        reset = now;
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return false;
        }

        if (response.Headers.RetryAfter is { } retry)
        {
            reset = retry.Date ?? now + (retry.Delta ?? TimeSpan.FromMinutes(1));
            return true;
        }

        if (Header(response, "X-RateLimit-Remaining") is "0")
        {
            reset = long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out long epoch)
                ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                : now + TimeSpan.FromMinutes(5);
            return true;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            reset = now + TimeSpan.FromMinutes(1);
            return true;
        }

        return false;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
}
