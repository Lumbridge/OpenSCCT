using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenScct.Master;
using OpenScct.Master.Player;
using OpenScct.Master.Web;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

/// <summary>Local account ledger and fake GitHub responses; these checks never create public issues.</summary>
public sealed class BugReportTests
{
    private const string Token = "test-server-only-token";
    private const string Title = "Desert Eagle reload stops early";
    private const string Body = "Steps: select the sidearm and reload.\nExpected: reload finishes.\nActual: reload stops.";

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(long seconds) => _now = _now.AddSeconds(seconds);
    }

    private sealed record Request(string Method, string Uri, string Authorization, string Accept, string ApiVersion, string UserAgent, string Body);

    private sealed class GitHubHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<Request> _requests = [];
        public Func<CancellationToken, Task<HttpResponseMessage>> Reply { get; set; } = _ => Task.FromResult(Created());
        public Request[] Requests { get { lock (_gate) return [.. _requests]; } }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new Request(request.Method.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString() ?? "",
                string.Join(",", request.Headers.Accept), request.Headers.TryGetValues("X-GitHub-Api-Version", out var versions) ? versions.Single() : "",
                request.Headers.UserAgent.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken));
            lock (_gate) _requests.Add(captured);
            return await Reply(cancellationToken);
        }
    }

    private sealed class Ledger : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "openscct-report-" + Guid.NewGuid().ToString("N"));
        public Clock Time { get; } = new();
        public AccountStore Store { get; private set; }
        public long AccountId { get; }
        public string Database { get; }
        public MasterOptions Options { get; } = new() { BugReports = new() { GitHubToken = Token } };

        public Ledger()
        {
            Directory.CreateDirectory(_directory);
            Database = Path.Combine(_directory, "accounts.db");
            Store = AccountStore.Open(Database, Time);
            AccountId = Store.GetOrCreate("11111111111111111").Id;
        }

        public string Submission() => Time.GetUtcNow().ToUnixTimeSeconds().ToString("x16", CultureInfo.InvariantCulture) +
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

        public void Reopen()
        {
            Store.Dispose();
            Store = AccountStore.Open(Database, Time);
        }

        public void Dispose()
        {
            Store.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class NoReleaseNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }

    private sealed class Website : IAsyncDisposable
    {
        public Ledger Ledger { get; } = new();
        public GitHubHandler GitHub { get; } = new();
        private readonly HttpClient _github;
        private readonly HttpClient _releases = new(new NoReleaseNetwork());
        private WebApplication? _app;
        public HttpClient Http { get; private set; } = null!;
        public string Csrf { get; private set; } = "";
        public string Session { get; private set; } = "";
        public int DiscordRequests;

        public Website() => _github = new HttpClient(GitHub);

        public async Task Start()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();
            var portal = new PlayerPortal(Ledger.Options, Ledger.Store, new ServerRegistry(Ledger.Options, Ledger.Time), _ => IPAddress.Loopback,
                new ReleaseChangelog(_releases), new GitHubBugReports(Ledger.Options, Ledger.Store, _github));
            portal.Map(_app);
            _app.MapGet("/auth/discord/callback", (Func<HttpContext, Task<IResult>>)portal.CallbackAsync);
            _app.MapPost("/fake-discord/oauth2/token", () =>
            {
                Interlocked.Increment(ref DiscordRequests);
                return Results.Json(new { access_token = "test-discord-access-token" });
            });
            _app.MapGet("/fake-discord/users/@me", () =>
            {
                Interlocked.Increment(ref DiscordRequests);
                return Results.Json(new { id = "11111111111111111" });
            });
            await _app.StartAsync(TestContext.Current.CancellationToken);
            string address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Ledger.Options.Discord.ApiUrl = address + "/fake-discord";
            Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(address) };
        }

        public void SignIn(long? account = null)
        {
            Session = Ledger.Store.CreatePlayerWebSession(account ?? Ledger.AccountId);
            Csrf = Ledger.Store.PlayerWebSession(Session)!.Csrf;
            Http.DefaultRequestHeaders.Remove("Cookie");
            Http.DefaultRequestHeaders.Add("Cookie", "svm_player=" + Session);
        }

        public Dictionary<string, string> Fields() => new(StringComparer.Ordinal)
        {
            ["csrf"] = Csrf, ["area"] = "game", ["title"] = "Door closes while entering", ["version"] = "0.8.74", ["platform"] = "Windows 11",
            ["steps"] = "1. Start Aquarius.\n2. Walk through the door.", ["expected"] = "The door stays open.", ["actual"] = "The door closes.", ["evidence"] = "",
        };

        public Task<HttpResponseMessage> Post(string action, Dictionary<string, string> fields) =>
            Http.PostAsync("/help/report-bug/" + action, new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            Http?.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            _github.Dispose();
            _releases.Dispose();
            Ledger.Dispose();
        }
    }

    private sealed class ChunkedForm(string form) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Encoding.UTF8.GetBytes(form)).AsTask();
    }

    private static HttpResponseMessage Created(long number = 123, string? url = null) => new(HttpStatusCode.Created)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { number, html_url = url ?? GitHubBugReports.IssuesUrl + "/" + number })),
    };

    [Fact]
    public async Task CreatesTheReviewedIssueWithServerCredentialsAndReturnsOnlyAProjectReceipt()
    {
        using var ledger = new Ledger();
        using var handler = new GitHubHandler();
        using var client = new HttpClient(handler);
        var reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        string id = ledger.Submission();

        BugReportResult result = await reports.SubmitAsync(ledger.AccountId, id, Title, Body, TestContext.Current.CancellationToken);

        Assert.Equal(BugReportStatus.Created, result.Status);
        Assert.Equal(GitHubBugReports.IssuesUrl + "/123", result.IssueUrl);
        Assert.Equal(id, result.SubmissionId);
        Request request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://api.github.com/repos/Lumbridge/OpenSCCT/issues", request.Uri);
        Assert.Equal("Bearer " + Token, request.Authorization);
        Assert.Contains("application/vnd.github+json", request.Accept, StringComparison.Ordinal);
        Assert.Equal("2026-03-10", request.ApiVersion);
        Assert.StartsWith("OpenSCCT-Website-BugReports/", request.UserAgent, StringComparison.Ordinal);
        using JsonDocument payload = JsonDocument.Parse(request.Body);
        Assert.Equal(Title, payload.RootElement.GetProperty("title").GetString());
        Assert.Equal(Body, payload.RootElement.GetProperty("body").GetString());
        Assert.DoesNotContain(Token, result.ToString(), StringComparison.Ordinal);
        Assert.Equal(BugReportStatus.Existing, reports.Lookup(ledger.AccountId, id)!.Status);
        Assert.Null(reports.Lookup(ledger.Store.GetOrCreate("22222222222222222").Id, id));
    }

    [Fact]
    public async Task ConcurrentClicksAndANewFormWithTheSameContentMakeOnePost()
    {
        using var ledger = new Ledger();
        using var handler = new GitHubHandler();
        using var client = new HttpClient(handler);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Reply = async ct => { started.SetResult(); await finish.Task.WaitAsync(ct); return Created(); };
        var reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        string id = ledger.Submission();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Task<BugReportResult> sending = reports.SubmitAsync(ledger.AccountId, id, Title, Body, ct);
        await started.Task.WaitAsync(ct);
        BugReportResult concurrent = await reports.SubmitAsync(ledger.AccountId, id, Title, Body, ct);
        finish.SetResult();
        Assert.Equal(BugReportStatus.Created, (await sending).Status);
        Assert.Equal(BugReportStatus.Pending, concurrent.Status);
        BugReportResult duplicate = await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), Title, Body, ct);
        Assert.Equal(BugReportStatus.Existing, duplicate.Status);
        Assert.Equal(id, duplicate.SubmissionId);
        Assert.Single(handler.Requests);
        Assert.Equal(BugReportStatus.Rejected, (await reports.SubmitAsync(ledger.AccountId, id, "Different report", Body, ct)).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CooldownAndDailyLimitSurviveAStoreRestart()
    {
        using var ledger = new Ledger();
        using var handler = new GitHubHandler();
        using var client = new HttpClient(handler);
        var reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.Equal(BugReportStatus.Created, (await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), "First report", Body, ct)).Status);
        Assert.Equal(BugReportStatus.RateLimited, (await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), "Too soon", Body, ct)).Status);
        for (int i = 2; i <= 3; i++)
        {
            ledger.Time.Advance(61);
            Assert.Equal(BugReportStatus.Created, (await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), "Report " + i, Body, ct)).Status);
        }
        ledger.Reopen();
        reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        ledger.Time.Advance(61);
        Assert.Equal(BugReportStatus.RateLimited, (await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), "Fourth report", Body, ct)).Status);
        Assert.Equal(3, handler.Requests.Length);
        ledger.Time.Advance(86400);
        Assert.Equal(BugReportStatus.Created, (await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), "Next day", Body, ct)).Status);
        Assert.Equal(4, handler.Requests.Length);
    }

    [Fact]
    public async Task LostResponseStaysUnknownAndDoesNotRetryAcrossRestartsOrFreshForms()
    {
        using var ledger = new Ledger();
        using var handler = new GitHubHandler { Reply = _ => throw new TaskCanceledException("private upstream timeout text") };
        using var client = new HttpClient(handler);
        var reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        string id = ledger.Submission();
        CancellationToken ct = TestContext.Current.CancellationToken;
        BugReportResult first = await reports.SubmitAsync(ledger.AccountId, id, Title, Body, ct);
        Assert.Equal(BugReportStatus.Unknown, first.Status);
        Assert.DoesNotContain("private upstream", first.Message!, StringComparison.Ordinal);

        ledger.Reopen();
        reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        Assert.Equal(BugReportStatus.Unknown, (await reports.SubmitAsync(ledger.AccountId, id, Title, Body, ct)).Status);
        Assert.Equal(id, (await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), Title, Body, ct)).SubmissionId);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(201, "https://attacker.example/123", (int)BugReportStatus.Unknown)]
    [InlineData(201, "https://github.com/another/repo/issues/123", (int)BugReportStatus.Unknown)]
    [InlineData(302, "https://attacker.example/redirect", (int)BugReportStatus.Rejected)]
    [InlineData(401, "private-upstream-token-detail", (int)BugReportStatus.Rejected)]
    [InlineData(500, "private-upstream-error", (int)BugReportStatus.Unknown)]
    public async Task UntrustedUpstreamResponsesNeverBecomeBrowserLinks(int status, string upstream, int expected)
    {
        using var ledger = new Ledger();
        using var handler = new GitHubHandler { Reply = _ => Task.FromResult(status == 201 ? Created(url: upstream) :
            new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(upstream) }) };
        using var client = new HttpClient(handler);
        var reports = new GitHubBugReports(ledger.Options, ledger.Store, client);

        BugReportResult result = await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), Title, Body, TestContext.Current.CancellationToken);

        Assert.Equal((BugReportStatus)expected, result.Status);
        Assert.Null(result.IssueUrl);
        Assert.DoesNotContain(upstream, result.ToString(), StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MissingCredentialsAndExpiredOrMalformedFormsNeverReachGitHub()
    {
        using var ledger = new Ledger();
        using var handler = new GitHubHandler();
        using var client = new HttpClient(handler);
        ledger.Options.BugReports.GitHubToken = "";
        var reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        string id = ledger.Submission();
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.False(reports.Enabled);
        Assert.Equal(BugReportStatus.Unavailable, (await reports.SubmitAsync(ledger.AccountId, id, Title, Body, ct)).Status);
        Assert.Null(reports.Lookup(ledger.AccountId, id));

        ledger.Options.BugReports.GitHubToken = Token;
        reports = new GitHubBugReports(ledger.Options, ledger.Store, client);
        ledger.Time.Advance(86401);
        Assert.Equal(BugReportStatus.Rejected, (await reports.SubmitAsync(ledger.AccountId, id, Title, Body, ct)).Status);
        Assert.Equal(BugReportStatus.Rejected, (await reports.SubmitAsync(ledger.AccountId, ledger.Submission(), "Title\nforged", Body, ct)).Status);
        Assert.Equal(BugReportStatus.Rejected, (await reports.SubmitAsync(ledger.AccountId, "not-a-receipt", Title, Body, ct)).Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task WebsiteRequiresExistingLoginAndCsrfBeforeReviewingOrSubmitting()
    {
        await using var website = new Website();
        await website.Start();
        string signedOut = await website.Http.GetStringAsync("/help/report-bug", TestContext.Current.CancellationToken);
        Assert.Contains("You do not need a GitHub account", signedOut, StringComparison.Ordinal);
        Assert.Contains("/auth/player/login?returnTo=%2Fhelp%2Freport-bug", signedOut, StringComparison.Ordinal);
        using HttpResponseMessage anonymous = await website.Post("review", website.Fields());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        website.SignIn();
        Dictionary<string, string> fields = website.Fields();
        fields["csrf"] = "wrong-csrf";
        using HttpResponseMessage expired = await website.Post("review", fields);
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        fields.Remove("csrf");
        using HttpResponseMessage direct = await website.Post("submit", fields);
        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        Assert.Empty(website.GitHub.Requests);
    }

    [Fact]
    public async Task ReviewEscapesTheReportAndConsentPublishesThatContentOnceWithAnOwnerOnlyReceipt()
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        Dictionary<string, string> fields = website.Fields();
        fields["title"] = "<Door> fails @someone";
        fields["actual"] = "</textarea><script>alert(1)</script>\n```text\n@someone\n![image](https://attacker.example/image)";
        using HttpResponseMessage review = await website.Post("review", fields);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        string html = await review.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, html, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111111111111", html, StringComparison.Ordinal);
        Assert.DoesNotContain(website.Session, html, StringComparison.Ordinal);
        Assert.Empty(website.GitHub.Requests);
        string shownTitle = WebUtility.HtmlDecode(Regex.Match(html, "<article[^>]*><h2>(.*?)</h2>").Groups[1].Value);
        Assert.NotEmpty(shownTitle);
        Dictionary<string, string> submission = HiddenFields(html);

        using HttpResponseMessage unconfirmed = await website.Post("submit", submission);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Empty(website.GitHub.Requests);
        using HttpResponseMessage edit = await website.Post("edit", submission);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Contains(WebUtility.HtmlEncode(fields["actual"]), await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Empty(website.GitHub.Requests);

        submission["public"] = "yes";
        using HttpResponseMessage sent = await website.Post("submit", submission);
        Assert.Equal(HttpStatusCode.SeeOther, sent.StatusCode);
        string receiptPath = sent.Headers.Location!.OriginalString;
        using HttpResponseMessage duplicate = await website.Post("submit", submission);
        Assert.Equal(HttpStatusCode.SeeOther, duplicate.StatusCode);
        Assert.Equal(receiptPath, duplicate.Headers.Location!.OriginalString);
        Request outbound = Assert.Single(website.GitHub.Requests);
        using JsonDocument payload = JsonDocument.Parse(outbound.Body);
        Assert.Equal(shownTitle, payload.RootElement.GetProperty("title").GetString());
        string body = payload.RootElement.GetProperty("body").GetString()!;
        Assert.Contains(fields["steps"], body, StringComparison.Ordinal);
        Assert.Contains(fields["expected"], body, StringComparison.Ordinal);
        Assert.Contains(fields["actual"], body, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111111111111", body, StringComparison.Ordinal);
        Assert.DoesNotContain(website.Session, body, StringComparison.Ordinal);
        string receipt = await website.Http.GetStringAsync(receiptPath, TestContext.Current.CancellationToken);
        Assert.Contains(GitHubBugReports.IssuesUrl + "/123", receipt, StringComparison.Ordinal);

        website.SignIn(website.Ledger.Store.GetOrCreate("22222222222222222").Id);
        using HttpResponseMessage otherOwner = await website.Http.GetAsync(receiptPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, otherOwner.StatusCode);
        Assert.DoesNotContain(GitHubBugReports.IssuesUrl + "/123", await otherOwner.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationAndRestrictionsKeepUnsuitableReportsOffGitHub()
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        foreach ((string key, string value) in new[] { ("version", ""), ("area", "unknown"), ("title", new string('x', 161)), ("steps", new string('x', 3001)) })
        {
            Dictionary<string, string> fields = website.Fields();
            fields[key] = value;
            using HttpResponseMessage response = await website.Post("review", fields);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        website.Ledger.Store.AddBan(website.Ledger.AccountId, null, null, "Report restriction", null);
        using HttpResponseMessage bannedGet = await website.Http.GetAsync("/help/report-bug", TestContext.Current.CancellationToken);
        using HttpResponseMessage bannedPost = await website.Post("review", website.Fields());
        Assert.Equal(HttpStatusCode.Forbidden, bannedGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bannedPost.StatusCode);
        Assert.Empty(website.GitHub.Requests);
    }

    [Fact]
    public async Task ChunkedRequestsCannotBypassTheReportBodyLimit()
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        using var content = new ChunkedForm("csrf=" + website.Csrf + "&steps=" + new string('x', 140 * 1024));
        content.Headers.ContentType = new("application/x-www-form-urlencoded");
        using HttpResponseMessage response = await website.Http.PostAsync("/help/report-bug/review", content, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            "An oversized chunked form must receive a bounded client error, not an application failure.");
        Assert.Empty(website.GitHub.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FormParserLimitsReturnAClientErrorBeforeCreatingAnIssue(bool tooManyValues)
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        Dictionary<string, string> fields = website.Fields();
        if (tooManyValues)
            for (int i = 0; i < 20; i++) fields["extra" + i] = "small";
        else fields["steps"] = new string('x', 12001);
        using var content = new FormUrlEncodedContent(fields);
        Assert.True(content.Headers.ContentLength < 128 * 1024);
        using HttpResponseMessage response = await website.Http.PostAsync("/help/report-bug/review", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(website.GitHub.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredSessionOrCsrfPreservesOnlyEscapedReportFields(bool expiredSession)
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        Dictionary<string, string> fields = website.Fields();
        fields["actual"] = "</textarea><script>alert('draft')</script>\nThe door still closes.";
        fields["csrf"] = "private-submitted-csrf-canary";
        fields["session"] = "private-session-field-canary";
        fields["access_token"] = "private-access-token-canary";
        fields["GitHubToken"] = "private-github-field-canary";
        fields["submission"] = "private-submission-field-canary";
        if (expiredSession) website.Ledger.Store.EndPlayerWebSession(website.Session);

        using HttpResponseMessage response = await website.Post("review", fields);

        Assert.Equal(expiredSession ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string draft = ReportDraft(html);
        Assert.Contains(fields["actual"], draft, StringComparison.Ordinal);
        Assert.Contains(fields["steps"], draft, StringComparison.Ordinal);
        Assert.Contains(fields["title"], draft, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert('draft')</script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, html, StringComparison.Ordinal);
        Assert.DoesNotContain(website.Session, html, StringComparison.Ordinal);
        Assert.DoesNotContain(website.Csrf, draft, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111111111111", draft, StringComparison.Ordinal);
        Assert.Contains(expiredSession ? "/auth/player/login?returnTo=%2Fhelp%2Freport-bug" : "href=\"/help/report-bug\"", html, StringComparison.Ordinal);
        Assert.Empty(website.GitHub.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingOrUnknownSendPreservesTheDraftAndChecksStatusWithoutReposting(bool pending)
    {
        await using var website = new Website();
        await website.Start();
        website.SignIn();
        CancellationToken ct = TestContext.Current.CancellationToken;
        Dictionary<string, string> fields = website.Fields();
        fields["actual"] = "</textarea><script>alert('uncertain')</script>\nPlease preserve this exact text.";
        using HttpResponseMessage review = await website.Post("review", fields);
        Dictionary<string, string> submission = HiddenFields(await review.Content.ReadAsStringAsync(ct));
        submission["public"] = "yes";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpResponseMessage>? sending = null;
        website.GitHub.Reply = pending ? async token =>
        {
            started.TrySetResult();
            await finish.Task.WaitAsync(token);
            return Created();
        } : _ => throw new HttpRequestException("private-upstream-response-canary");
        try
        {
            if (pending)
            {
                sending = website.Post("submit", submission);
                await started.Task.WaitAsync(ct);
            }
            using HttpResponseMessage uncertain = await website.Post("submit", submission);
            Assert.Equal(HttpStatusCode.OK, uncertain.StatusCode);
            Assert.Null(uncertain.Headers.Location);
            string html = await uncertain.Content.ReadAsStringAsync(ct);
            string draft = ReportDraft(html);
            Assert.Contains(fields["actual"], draft, StringComparison.Ordinal);
            Assert.Contains(fields["steps"], draft, StringComparison.Ordinal);
            Assert.DoesNotContain("<script>alert('uncertain')</script>", html, StringComparison.Ordinal);
            Assert.DoesNotContain(Token, html, StringComparison.Ordinal);
            Assert.DoesNotContain(website.Session, html, StringComparison.Ordinal);
            Assert.DoesNotContain(website.Csrf, draft, StringComparison.Ordinal);
            Assert.DoesNotContain(submission["submission"], draft, StringComparison.Ordinal);
            Assert.DoesNotContain("private-upstream", html, StringComparison.Ordinal);
            string receiptPath = "/help/report-bug/sent/" + submission["submission"];
            Assert.Contains("href=\"" + receiptPath + "\"", html, StringComparison.Ordinal);
            Assert.Contains("Check report status", html, StringComparison.Ordinal);
            using HttpResponseMessage status = await website.Http.GetAsync(receiptPath, ct);
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.Single(website.GitHub.Requests);
        }
        finally
        {
            finish.TrySetResult();
            if (sending is not null) (await sending).Dispose();
        }
    }

    private static string ReportDraft(string html)
    {
        Match textarea = Regex.Match(html, "<textarea[^>]*id=\"report-draft\"[^>]*readonly[^>]*>(.*?)</textarea>", RegexOptions.Singleline);
        Assert.True(textarea.Success, "A readonly copy of the report draft must remain visible.");
        return WebUtility.HtmlDecode(textarea.Groups[1].Value);
    }

    [Fact]
    public async Task DiscordRetryKeepsOnlyTheNonceBoundReportDestination()
    {
        await using var website = new Website();
        await website.Start();
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpResponseMessage login = await website.Http.GetAsync("/auth/player/login?returnTo=%2Fhelp%2Freport-bug", ct);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        string[] cookies = login.Headers.GetValues("Set-Cookie").ToArray();
        string returnCookie = Assert.Single(cookies, cookie => cookie.StartsWith("svm_player_return=", StringComparison.Ordinal));
        Assert.Contains("httponly", returnCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", returnCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=600", returnCookie, StringComparison.OrdinalIgnoreCase);
        string state = QueryHelpers.ParseQuery(login.Headers.Location!.Query)["state"].ToString();
        website.Http.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(cookie => cookie.Split(';')[0])));
        using HttpResponseMessage invalid = await website.Http.GetAsync("/auth/discord/callback?state=player.wrong-state&code=fake-code", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("/auth/player/login?returnTo=%2Fhelp%2Freport-bug", await invalid.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(0, website.DiscordRequests);
        using HttpResponseMessage cancelled = await website.Http.GetAsync("/auth/discord/callback?state=" + Uri.EscapeDataString(state) + "&error=access_denied", ct);
        Assert.Equal(HttpStatusCode.BadRequest, cancelled.StatusCode);
        string retry = await cancelled.Content.ReadAsStringAsync(ct);
        Assert.Contains("/auth/player/login?returnTo=%2Fhelp%2Freport-bug", retry, StringComparison.Ordinal);
        Assert.Contains(cancelled.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("svm_player_return=;", StringComparison.Ordinal));

        website.Http.DefaultRequestHeaders.Remove("Cookie");
        using HttpResponseMessage arbitrary = await website.Http.GetAsync("/auth/player/login?returnTo=https%3A%2F%2Fattacker.example", ct);
        string[] accountCookies = arbitrary.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(accountCookies, cookie => cookie.StartsWith("svm_player_return=;", StringComparison.Ordinal));
        state = QueryHelpers.ParseQuery(arbitrary.Headers.Location!.Query)["state"].ToString();
        website.Http.DefaultRequestHeaders.Add("Cookie", string.Join("; ", accountCookies.Select(cookie => cookie.Split(';')[0])));
        using HttpResponseMessage accountRetry = await website.Http.GetAsync("/auth/discord/callback?state=" + Uri.EscapeDataString(state) + "&error=access_denied", ct);
        string accountHtml = await accountRetry.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("attacker.example", accountHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("returnTo=", accountHtml, StringComparison.Ordinal);

        // A report cookie from an older login cannot change the destination of a fresh account login.
        website.Http.DefaultRequestHeaders.Remove("Cookie");
        using HttpResponseMessage fresh = await website.Http.GetAsync("/auth/player/login", ct);
        string freshStateCookie = Assert.Single(fresh.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("svm_player_state=", StringComparison.Ordinal));
        state = QueryHelpers.ParseQuery(fresh.Headers.Location!.Query)["state"].ToString();
        website.Http.DefaultRequestHeaders.Add("Cookie", freshStateCookie.Split(';')[0] + "; " + returnCookie.Split(';')[0]);
        using HttpResponseMessage staleReturn = await website.Http.GetAsync("/auth/discord/callback?state=" + Uri.EscapeDataString(state) + "&error=access_denied", ct);
        Assert.DoesNotContain("returnTo=", await staleReturn.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(0, website.DiscordRequests);
        Assert.Empty(website.GitHub.Requests);
    }

    [Fact]
    public async Task DiscordSuccessKeepsTheStrictSessionAndSameSiteHopBackToTheReport()
    {
        await using var website = new Website();
        await website.Start();
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpResponseMessage login = await website.Http.GetAsync("/auth/player/login?returnTo=%2Fhelp%2Freport-bug", ct);
        string state = QueryHelpers.ParseQuery(login.Headers.Location!.Query)["state"].ToString();
        website.Http.DefaultRequestHeaders.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0])));
        using HttpResponseMessage callback = await website.Http.GetAsync("/auth/discord/callback?state=" + Uri.EscapeDataString(state) + "&code=test-code", ct);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Equal(2, website.DiscordRequests);
        string hop = await callback.Content.ReadAsStringAsync(ct);
        Assert.Contains("content=\"0;url=/help/report-bug\"", hop, StringComparison.Ordinal);
        Assert.Contains("href=\"/help/report-bug\"", hop, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, hop, StringComparison.Ordinal);
        Assert.DoesNotContain("test-discord-access-token", hop, StringComparison.Ordinal);
        string sessionCookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("svm_player=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        website.Http.DefaultRequestHeaders.Remove("Cookie");
        website.Http.DefaultRequestHeaders.Add("Cookie", sessionCookie.Split(';')[0]);
        string form = await website.Http.GetStringAsync("/help/report-bug", ct);
        Assert.Contains("action=\"/help/report-bug/review\"", form, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, form, StringComparison.Ordinal);
        Assert.Empty(website.GitHub.Requests);
    }

    private static Dictionary<string, string> HiddenFields(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match input in Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\">"))
            fields[WebUtility.HtmlDecode(input.Groups[1].Value)] = WebUtility.HtmlDecode(input.Groups[2].Value);
        return fields;
    }
}
