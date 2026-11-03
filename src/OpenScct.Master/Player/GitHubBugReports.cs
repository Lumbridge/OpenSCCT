using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Master.Player;

internal enum BugReportStatus { Created, Existing, Pending, Unknown, Rejected, RateLimited, Unavailable }
internal sealed record BugReportResult(BugReportStatus Status, string? IssueUrl = null, long? IssueNumber = null,
    string? Message = null, string? SubmissionId = null);

/// <summary>Creates player-reviewed issues in the project repository; credentials never enter a browser response.</summary>
internal sealed class GitHubBugReports
{
    private const string IssuesApi = "https://api.github.com/repos/Lumbridge/OpenSCCT/issues";
    public const string IssuesUrl = "https://github.com/Lumbridge/OpenSCCT/issues";
    private static readonly HttpClient Client = CreateClient();
    private readonly AccountStore _store;
    private readonly HttpClient _client;
    private readonly string _token;

    public GitHubBugReports(MasterOptions options, AccountStore store, HttpClient? client = null)
    {
        _store = store;
        _client = client ?? Client;
        _token = options.BugReports.GitHubToken?.Trim() ?? string.Empty;
    }

    public bool Enabled => _token.Length is > 0 and <= 4096 && !HasTokenWhitespace(_token);

    /// <summary>Opaque receipt id with a time limit for submitting an unused form, not an authentication token.</summary>
    public static string NewSubmissionId() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString("x16", CultureInfo.InvariantCulture) +
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

    public BugReportResult? Lookup(long accountId, string submissionId) =>
        ValidId(submissionId) && _store.BugReport(accountId, submissionId) is { } report ? Result(report) : null;

    public async Task<BugReportResult> SubmitAsync(long accountId, string submissionId, string title, string body, CancellationToken cancellationToken)
    {
        if (!ValidId(submissionId) || string.IsNullOrWhiteSpace(title) || title.Length > 200 ||
            title.IndexOfAny(['\r', '\n', '\0']) >= 0 || string.IsNullOrWhiteSpace(body) || body.Length > 48_000 || body.Contains('\0'))
            return new(BugReportStatus.Rejected, Message: "The report is incomplete or too long. Check the form and try again.");

        string payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title + "\n" + body)));
        if (_store.BugReport(accountId, submissionId) is { } previous)
            return previous.PayloadHash == payloadHash ? Result(previous) :
                new(BugReportStatus.Rejected, Message: "This form was already used for a different report. Start a new report.");
        if (!Enabled) return new(BugReportStatus.Unavailable, Message: "Website bug reporting is temporarily unavailable. Please try again later.");
        long formTime = long.Parse(submissionId.AsSpan(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (formTime > _store.Now + 60 || formTime <= _store.Now - 86400)
            return new(BugReportStatus.Rejected, Message: "This report form has expired. Copy your text, open a new form and try again.");

        // Once reserved, finish recording the outcome even if the browser closes. Never repeat a POST when
        // its outcome is uncertain: GitHub may have accepted it before a connection or response was lost.
        cancellationToken.ThrowIfCancellationRequested();
        BugReportReservation reservation = _store.ReserveBugReport(accountId, submissionId, payloadHash);
        if (reservation.State == BugReportReservationState.Existing) return Result(reservation.Report!);
        if (reservation.State == BugReportReservationState.Conflict)
            return new(BugReportStatus.Rejected, Message: "This form has already been used. Start a new report.");
        if (reservation.State == BugReportReservationState.RateLimited)
            return new(BugReportStatus.RateLimited, Message: "Please wait before sending another report. Each account can send up to three reports in 24 hours, at least one minute apart.");

        StoredBugReportState storedState = StoredBugReportState.Unknown;
        long? issueNumber = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, IssuesApi);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("OpenSCCT-Website-BugReports/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            request.Content = JsonContent.Create(new { title, body });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await _client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("number", out JsonElement number) && number.ValueKind == JsonValueKind.Number &&
                    number.TryGetInt64(out long accepted) && accepted > 0 &&
                    json.RootElement.TryGetProperty("html_url", out JsonElement url) && url.ValueKind == JsonValueKind.String &&
                    string.Equals(url.GetString(), IssueUrl(accepted), StringComparison.Ordinal))
                {
                    issueNumber = accepted;
                    storedState = StoredBugReportState.Created;
                }
            }
            else if ((int)response.StatusCode is >= 300 and < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
            {
                // A definite rejection is safe to retry from a new form. Do not disclose upstream text.
                storedState = StoredBugReportState.Rejected;
            }
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or FormatException)
        {
            // Leave the reserved receipt as Unknown rather than risk creating an issue twice.
        }

        _store.FinishBugReport(accountId, submissionId, storedState, issueNumber);
        var completed = reservation.Report! with { State = storedState, IssueNumber = issueNumber };
        BugReportResult result = Result(completed);
        return storedState == StoredBugReportState.Created ? result with { Status = BugReportStatus.Created } : result;
    }

    private BugReportResult Result(StoredBugReport report) => report.State switch
    {
        StoredBugReportState.Created when report.IssueNumber is > 0 => new(BugReportStatus.Existing,
            IssueUrl(report.IssueNumber.Value), report.IssueNumber, SubmissionId: report.Id),
        StoredBugReportState.Rejected => new(BugReportStatus.Rejected, Message: "GitHub did not accept this report. Please try again later.", SubmissionId: report.Id),
        StoredBugReportState.Pending when _store.Now - report.Created < 30 => new(BugReportStatus.Pending,
            Message: "Your report is being sent. Refresh this page in a moment to see the result.", SubmissionId: report.Id),
        _ => new(BugReportStatus.Unknown, Message: "GitHub did not confirm whether the report was created. Check the existing reports before submitting it again.", SubmissionId: report.Id),
    };

    private static string IssueUrl(long number) => IssuesUrl + "/" + number.ToString(CultureInfo.InvariantCulture);

    private static bool ValidId(string id)
    {
        if (id.Length != 64) return false;
        foreach (char c in id) if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }

    private static bool HasTokenWhitespace(string token)
    {
        foreach (char c in token) if (char.IsWhiteSpace(c) || char.IsControl(c)) return true;
        return false;
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 256 * 1024,
    };
}
