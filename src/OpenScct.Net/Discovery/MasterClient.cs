using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Net.Discovery;

/// <summary>A master request that failed, with a message a player can read.</summary>
public sealed class MasterException : Exception
{
    public MasterException(string message, int statusCode = 0, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public MasterException()
    {
    }

    public MasterException(string message)
        : base(message)
    {
    }

    public MasterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The HTTP status, or 0 when the master could not be reached.</summary>
    public int StatusCode { get; }
}

/// <summary>
/// The master's HTTP API for hosts (heartbeat, unlist) and browsers (list, join, status). Never points at the
/// original game's or the Reloaded patch's master; the base address always comes from our own settings.
/// </summary>
public sealed class MasterClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>A client for the master at <paramref name="baseUrl"/> (for example <c>https://master.example.org</c>).</summary>
    public MasterClient(string baseUrl, string? inviteKey = null, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);
        if (!Uri.TryCreate(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{baseUrl}' is not an http or https address.", nameof(baseUrl));
        }

        BaseUri = uri;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = uri;
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(8);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("svm/" + BuildInfo.Version);
        if (!string.IsNullOrEmpty(inviteKey))
        {
            _http.DefaultRequestHeaders.Add(MasterApi.KeyHeader, inviteKey);
        }
    }

    public Uri BaseUri { get; }

    /// <summary>The signed-in player's token, sent as <c>Authorization: Bearer</c> (account calls, and join for a ticket).</summary>
    public string? SessionToken { get; set; }

    /// <summary>A trusted server's key, sent with heartbeats and round results.</summary>
    public string? ServerKey { get; set; }

    /// <summary>The host's heartbeat; the answer carries the id and token to send next time.</summary>
    public Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<HeartbeatResponse>(HttpMethod.Post, MasterApi.HeartbeatPath, request, cancellationToken);

    /// <summary>Starts a sign-in: open the answer's page in the browser, then poll with its secret.</summary>
    public Task<LinkStartResponse> StartLinkAsync(CancellationToken cancellationToken = default) =>
        SendAsync<LinkStartResponse>(HttpMethod.Post, MasterApi.LinkPath, new { }, cancellationToken);

    /// <summary>Where a sign-in started with <see cref="StartLinkAsync"/> is.</summary>
    public Task<LinkPollResponse> PollLinkAsync(string code, string pollSecret, CancellationToken cancellationToken = default) =>
        SendAsync<LinkPollResponse>(HttpMethod.Post, MasterApi.LinkPollPath(code), new LinkPollRequest { PollSecret = pollSecret },
            cancellationToken);

    /// <summary>The signed-in account (needs <see cref="SessionToken"/>).</summary>
    public Task<AccountInfo> AccountAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AccountInfo>(HttpMethod.Get, MasterApi.AccountPath, null, cancellationToken);

    /// <summary>Claims or changes the account's name; the answer is the account after the change.</summary>
    public Task<AccountInfo> ClaimNameAsync(string name, CancellationToken cancellationToken = default) =>
        SendAsync<AccountInfo>(HttpMethod.Post, MasterApi.AccountNamePath, new NameRequest { Name = name }, cancellationToken);

    /// <summary>Prestiges the account (level 55 only); the answer is the account after it.</summary>
    public Task<AccountInfo> PrestigeAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AccountInfo>(HttpMethod.Post, MasterApi.PrestigePath, new { }, cancellationToken);

    /// <summary>Ends this sign-in on the master (the token stops working).</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = Request(HttpMethod.Post, MasterApi.SignOutPath, new { });
        using HttpResponseMessage response = await Send(message, cancellationToken).ConfigureAwait(false);
        await EnsureOk(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The account's saved settings, or null when it has none yet.</summary>
    public async Task<ProfileData?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = Request(HttpMethod.Get, MasterApi.ProfilePath, null);
        using HttpResponseMessage response = await Send(message, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureOk(response, cancellationToken).ConfigureAwait(false);
        return await Read<ProfileData>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Saves the account's settings; the master keeps whichever copy is newer and answers with it.</summary>
    public Task<ProfileData> PutProfileAsync(ProfileData profile, CancellationToken cancellationToken = default) =>
        SendAsync<ProfileData>(HttpMethod.Put, MasterApi.ProfilePath, profile, cancellationToken);

    public Task<AccountAppearance> AppearanceAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AccountAppearance>(HttpMethod.Get, MasterApi.AppearancePath, null, cancellationToken);

    /// <summary>Saves an appearance; validation/conflict results include the current version, without throwing.</summary>
    public async Task<AppearanceUpdateResult> UpdateAppearanceAsync(AppearanceUpdateRequest appearance, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = Request(HttpMethod.Put, MasterApi.AppearancePath, appearance);
        using HttpResponseMessage response = await Send(message, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.BadRequest))
        {
            await EnsureOk(response, cancellationToken).ConfigureAwait(false);
        }

        AppearanceUpdateResult result = await Read<AppearanceUpdateResult>(response, cancellationToken).ConfigureAwait(false);
        // Middleware and older masters can return an ordinary error object on this path. It is not an
        // appearance snapshot, and must never become an empty selection offered by the Reload action.
        if (result.Appearance is not { Revision: > 0 } || result.Appearance.Spy is null || result.Appearance.Merc is null ||
            (!response.IsSuccessStatusCode && result.Success))
        {
            throw new MasterException(result.Error ?? "The master returned an invalid appearance response.", (int)response.StatusCode);
        }

        return result;
    }

    /// <summary>
    /// A trusted server's finished round (needs <see cref="ServerKey"/>). The answer carries the stored round's id;
    /// it is empty (no id) from masters before demos.
    /// </summary>
    public async Task<ResultsResponse> ReportResultsAsync(ResultsReport report, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = Request(HttpMethod.Post, MasterApi.ResultsPath, report);
        using HttpResponseMessage response = await Send(message, cancellationToken).ConfigureAwait(false);
        await EnsureOk(response, cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ResultsResponse();
        }

        try
        {
            return JsonSerializer.Deserialize<ResultsResponse>(text, MasterApi.Json) ?? new ResultsResponse();
        }
        catch (JsonException)
        {
            return new ResultsResponse();
        }
    }

    /// <summary>A trusted server asks where to upload a round's demo (needs <see cref="ServerKey"/>).</summary>
    public Task<DemoUploadResponse> RequestDemoUploadAsync(DemoUploadRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<DemoUploadResponse>(HttpMethod.Post, MasterApi.DemoUploadPath, request, cancellationToken);

    /// <summary>A trusted server confirms a demo's upload; the demo is listed from then on.</summary>
    public async Task DemoDoneAsync(long demoId, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage message = Request(HttpMethod.Post, MasterApi.DemoDonePath(demoId), new { });
        using HttpResponseMessage response = await Send(message, cancellationToken).ConfigureAwait(false);
        await EnsureOk(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One page (from 0) of ready demos, newest first (needs <see cref="SessionToken"/>).</summary>
    public Task<DemoListResponse> DemosAsync(int page = 0, CancellationToken cancellationToken = default) =>
        SendAsync<DemoListResponse>(HttpMethod.Get,
            MasterApi.DemosPath + "?page=" + Math.Max(0, page).ToString(System.Globalization.CultureInfo.InvariantCulture), null, cancellationToken);

    /// <summary>A short-lived download link for a demo (needs <see cref="SessionToken"/>); fetch it with a plain GET.</summary>
    public Task<DemoDownloadResponse> DemoDownloadAsync(long demoId, CancellationToken cancellationToken = default) =>
        SendAsync<DemoDownloadResponse>(HttpMethod.Post, MasterApi.DemoDownloadPath(demoId), new { }, cancellationToken);

    /// <summary>
    /// The listed servers. <paramref name="protocol"/> keeps only servers of that build; <paramref name="content"/>
    /// keeps only servers whose map has one of these content hashes (null keeps every map). <paramref name="kind"/>
    /// picks game servers (the default, as older masters list) or hubs.
    /// </summary>
    public Task<ServerListResponse> ListAsync(ulong? protocol = null, IEnumerable<ulong>? content = null,
        CancellationToken cancellationToken = default, ServerKind kind = ServerKind.Game)
    {
        var query = new StringBuilder(MasterApi.ListPath.TrimStart('/'));
        char sep = '?';
        if (kind != ServerKind.Game)
        {
            query.Append(sep).Append("kind=").Append(MasterApi.KindText(kind));
            sep = '&';
        }

        if (protocol is { } p)
        {
            query.Append(sep).Append("protocol=").Append(MasterApi.Hex(p));
            sep = '&';
        }

        if (content is not null)
        {
            foreach (ulong c in content)
            {
                query.Append(sep).Append("content=").Append(MasterApi.Hex(c));
                sep = '&';
            }
        }

        return SendAsync<ServerListResponse>(HttpMethod.Get, query.ToString(), null, cancellationToken);
    }

    /// <summary>One server as the list shows it, listed or not (404 when the master no longer knows it).</summary>
    public Task<ServerEntry> ServerAsync(string serverId, CancellationToken cancellationToken = default) =>
        SendAsync<ServerEntry>(HttpMethod.Get, MasterApi.ServerPath(serverId), null, cancellationToken);

    /// <summary>How to reach one server (direct address, punch token, relay token).</summary>
    public Task<JoinResponse> JoinAsync(string serverId, CancellationToken cancellationToken = default) =>
        SendAsync<JoinResponse>(HttpMethod.Post, MasterApi.JoinPath(serverId), new { }, cancellationToken);

    /// <summary>Takes a server off the list now (it would lapse 90 s after its last heartbeat anyway).</summary>
    public async Task UnlistAsync(string serverId, string hostToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete, MasterApi.ServerPath(serverId).TrimStart('/') + "?hostToken=" +
            Uri.EscapeDataString(hostToken));
        using HttpResponseMessage response = await Send(message, cancellationToken).ConfigureAwait(false);
        await EnsureOk(response, cancellationToken).ConfigureAwait(false);
    }

    public Task<MasterStatus> StatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync<MasterStatus>(HttpMethod.Get, MasterApi.StatusPath, null, cancellationToken);

    /// <summary>Recent stable release notes; does not require a signed-in account.</summary>
    public Task<ChangelogResponse> ChangelogAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ChangelogResponse>(HttpMethod.Get, MasterApi.ChangelogPath, null, cancellationToken);

    private HttpRequestMessage Request(HttpMethod method, string path, object? body)
    {
        var message = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, body.GetType(), options: MasterApi.Json);
        }

        if (!string.IsNullOrEmpty(SessionToken))
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken);
        }

        if (!string.IsNullOrEmpty(ServerKey))
        {
            message.Headers.Add(MasterApi.ServerKeyHeader, ServerKey);
        }

        return message;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        where T : class
    {
        using HttpRequestMessage message = Request(method, path, body);
        using HttpResponseMessage response = await Send(message, cancellationToken).ConfigureAwait(false);
        await EnsureOk(response, cancellationToken).ConfigureAwait(false);
        return await Read<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(MasterApi.Json, cancellationToken).ConfigureAwait(false)
                ?? throw new MasterException("The master server sent an empty answer.", (int)response.StatusCode);
        }
        catch (JsonException e)
        {
            throw new MasterException("The master server sent an answer this build does not understand.", (int)response.StatusCode, e);
        }
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new MasterException($"Could not reach the master server at {BaseUri.Host}: {e.Message}", 0, e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MasterException($"The master server at {BaseUri.Host} did not answer in time.", 0, e);
        }
    }

    private static async Task EnsureOk(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? text = null;
        try
        {
            text = (await response.Content.ReadFromJsonAsync<MasterError>(MasterApi.Json, cancellationToken).ConfigureAwait(false))?.Error;
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        int status = (int)response.StatusCode;
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => text ?? "The master server is private and needs an invite key.",
            HttpStatusCode.Forbidden => text ?? "The master server refused the invite key.",
            HttpStatusCode.NotFound => text ?? "The server is no longer listed.",
            HttpStatusCode.TooManyRequests => "The master server is busy; try again in a moment.",
            _ => text ?? $"The master server answered {status}.",
        };
        throw new MasterException(message, status);
    }

    public void Dispose() => _http.Dispose();
}
