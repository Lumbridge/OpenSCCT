using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenScct.Launcher.GitHub;

/// <summary>One GitHub release, the fields the launcher uses from the REST API.</summary>
public sealed class ReleaseInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("assets")]
    public List<ReleaseAsset> Assets { get; set; } = [];
}

/// <summary>One file attached to a release.</summary>
public sealed class ReleaseAsset
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The display name GitHub shows instead of <see cref="Name"/>, when the uploader set one.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The API URL; with Accept: application/octet-stream it serves the file, also for a private repository.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

/// <summary>What a release check returned and where it came from.</summary>
public sealed record ReleaseFeed(IReadOnlyList<ReleaseInfo> Releases, ReleaseFeedSource Source, DateTimeOffset? RateLimitReset, string? Problem);

public enum ReleaseFeedSource
{
    /// <summary>Fresh from GitHub.</summary>
    Network,

    /// <summary>GitHub answered 304 Not Modified; the cached list is current and the call did not count against the rate limit.</summary>
    NotModified,

    /// <summary>GitHub could not be reached or refused (offline, rate limited, error); the last cached list is shown.</summary>
    Cache,

    /// <summary>Nothing reachable and nothing cached.</summary>
    None,
}
