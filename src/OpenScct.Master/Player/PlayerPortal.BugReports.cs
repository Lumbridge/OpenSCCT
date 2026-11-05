using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using OpenScct.Project;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Player;

internal sealed partial class PlayerPortal
{
    private readonly GitHubBugReports _bugReports = bugReports ?? new(options, store);
    private readonly ReportScreenshotDrafts _screenshotDrafts = new();
    private const int ReportBodyLimit = 4 * 1024 * 1024;
    private const int ReportQueryLimit = 4096;
    private sealed record ReportField(string Key, string Label, int Max, bool Required, bool Multiline, string Help);
    private static readonly ReportField[] ReportFields =
    [
        new("title", "Short description", 160, true, false, "For example: The game closes when I join a server."),
        new("version", "Game or launcher version", 120, false, false, "Required for game and launcher reports. You can find it in the app."),
        new("platform", "Operating system", 80, false, false, "For example: Windows 11 or SteamOS."),
        new("environment", "Game or launcher details (optional)", 2000, false, true, "The app may fill in its build, map, mode, side, session, player position and movement state. Check or edit them before sending."),
        new("steps", "How can we make it happen?", 3000, true, true, "List the steps. For game problems, include the map, mode, side and whether you were hosting or joining."),
        new("expected", "What should happen?", 2000, true, true, "Describe the result you expected."),
        new("actual", "What happened instead?", 3000, true, true, "Include any error message and how often the problem happens."),
        new("evidence", "Extra information (optional)", 2000, false, true, "Paste relevant error text or a link to a screenshot or video. Remove passwords and personal information."),
    ];

    private void MapBugReports(WebApplication app)
    {
        Get(app, ReportReturnPath, OpenBugReport);
        app.MapPost(ReportScreenshotDrafts.UploadRoute, (Func<HttpContext, Task<IResult>>)UploadReportScreenshot);
        Get(app, ReportScreenshot.Route + "{id}.png", c =>
        {
            string id = c.Request.RouteValues["id"]?.ToString() ?? "";
            if (!ReportScreenshot.ValidId(id) || !File.Exists(ReportScreenshot.PathFor(_options, id))) return Results.NotFound();
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            c.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
            return Results.File(ReportScreenshot.PathFor(_options, id), "image/png");
        });
        Get(app, ReportReturnPath + "/sent/{submissionId}", c => Current(c) is { } v
            ? BugReportReceipt(c, v, c.Request.RouteValues["submissionId"]?.ToString() ?? "") : BugReportSignIn(c));
        ReportPost(app, "/review", (c, v, f) => Task.FromResult(ReviewBugReport(c, v, f)));
        ReportPost(app, "/edit", (c, v, f) => Task.FromResult(BugReportForm(c, v, ReadReport(f))));
        ReportPost(app, "/submit", SubmitBugReport);
    }

    private async Task<IResult> UploadReportScreenshot(HttpContext c)
    {
        Secure(c);
        if (c.Request.ContentType != "image/png" || c.Request.ContentLength > ReportScreenshot.MaximumBytes)
            return Results.BadRequest();
        if (!_screenshotDrafts.Begin(_clientIp(c).ToString())) return Results.StatusCode(429);
        try
        {
            if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = ReportScreenshot.MaximumBytes;
            using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var body = new MemoryStream();
            byte[] buffer = new byte[16 * 1024];
            int count;
            while ((count = await c.Request.Body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + count > ReportScreenshot.MaximumBytes) return Results.BadRequest();
                body.Write(buffer, 0, count);
            }
            byte[]? png = ReportScreenshot.Validate(body.ToArray());
            return png is null ? Results.BadRequest() : Results.Json(new { capture = _screenshotDrafts.Add(png) });
        }
        catch (Exception error) when (error is IOException or BadHttpRequestException or OperationCanceledException)
        {
            return Results.BadRequest();
        }
        finally { _screenshotDrafts.End(); }
    }

    private IResult OpenBugReport(HttpContext c)
    {
        bool valid = BugReportReturnPath(c.Request.Path + c.Request.QueryString) is not null;
        string? problem = valid ? null : "The prefilled details were too long or could not be read. You can enter them in the report form.";
        if (Current(c) is not { } viewer) return BugReportSignIn(c, problem);
        Dictionary<string, string> values = valid ? ReadReportPrefill(c.Request.Query) : [];
        if (values.Remove("capture", out string? capture))
        {
            if (_screenshotDrafts.Find(capture) is { } png) values["screenshot"] = Convert.ToBase64String(png);
            else problem = "The captured screenshot has expired or is unavailable. Return to the game and choose Report a bug again to attach it.";
        }
        return BugReportForm(c, viewer, values, problem);
    }

    /// <summary>Only this local report route and its bounded, explicit prefills may survive Discord sign-in.</summary>
    private static string? BugReportReturnPath(string? requested)
    {
        if (requested == ReportReturnPath) return ReportReturnPath;
        if (requested is null || !requested.StartsWith(ReportReturnPath + "?", StringComparison.Ordinal) ||
            requested.Length - ReportReturnPath.Length > ReportQueryLimit ||
            requested.Contains('#') || requested.Contains('\\') || requested.Any(char.IsControl)) return null;
        var query = new QueryCollection(QueryHelpers.ParseQuery(requested[ReportReturnPath.Length..]));
        Dictionary<string, string> values = ReadReportPrefill(query);
        var canonical = new StringBuilder(ReportReturnPath);
        foreach (string key in new[] { "area", "version", "platform", "environment", "capture" })
            if (values.TryGetValue(key, out string? value) && value.Length > 0)
                canonical.Append(canonical.Length == ReportReturnPath.Length ? '?' : '&').Append(key).Append('=').Append(U(value));
        return canonical.Length <= ReportQueryLimit ? canonical.ToString() : null;
    }

    private static Dictionary<string, string> ReadReportPrefill(IQueryCollection query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in query)
        {
            if (entry.Value.Count != 1) continue;
            string value = entry.Value[0] ?? string.Empty;
            if (entry.Key == "area")
            {
                if (value.Trim() is "game" or "launcher") values[entry.Key] = value.Trim();
                continue;
            }
            if (entry.Key == "capture")
            {
                if (ReportScreenshot.ValidId(value)) values[entry.Key] = value;
                continue;
            }
            int maximum = entry.Key switch { "version" => 120, "platform" => 80, "environment" => 2000, _ => 0 };
            if (maximum > 0) values[entry.Key] = NormalizeReportPrefill(value, maximum, entry.Key == "environment");
        }
        return values;
    }

    private static string NormalizeReportPrefill(string value, int maximum, bool multiline)
    {
        var clean = new StringBuilder();
        foreach (Rune rune in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim().EnumerateRunes())
        {
            string text = rune.Value is 0x2028 or 0x2029 ? (multiline ? "\n" : " ")
                : Rune.IsControl(rune) && !(multiline && rune.Value is '\n' or '\t') ? " " : rune.ToString();
            if (clean.Length + text.Length > maximum) break;
            clean.Append(text);
        }
        return clean.ToString().Trim();
    }

    private void ReportPost(WebApplication app, string suffix, Func<HttpContext, Viewer, IFormCollection, Task<IResult>> handler) =>
        app.MapPost(ReportReturnPath + suffix, (RequestDelegate)(async c =>
        {
            Secure(c);
            c.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = ReportBodyLimit;
            if (!c.Request.HasFormContentType || c.Request.ContentType?.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) != true ||
                c.Request.ContentLength > ReportBodyLimit)
            {
                await ReportInvalid(c, "This report could not be read. Go back and shorten the text or remove the screenshot before trying again.", 400).ExecuteAsync(c).ConfigureAwait(false);
                return;
            }
            IFormCollection form;
            try
            {
                form = await c.Request.ReadFormAsync(new FormOptions
                {
                    // The parser limits the encoded field; PNG/base64 and text limits still apply after decoding.
                    ValueCountLimit = 16, KeyLengthLimit = 40, ValueLengthLimit = ReportBodyLimit,
                    // Supported forms must stay in memory: the deployed service cannot write to system temp.
                    BufferBody = true, BufferBodyLengthLimit = ReportBodyLimit, MemoryBufferThreshold = ReportBodyLimit,
                }, c.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or BadHttpRequestException)
            {
                await ReportInvalid(c, "This report is too large or could not be read. Go back and shorten the text or remove the screenshot before trying again.", 400).ExecuteAsync(c).ConfigureAwait(false);
                return;
            }
            if (Current(c) is not { } viewer)
            {
                await ReportDraftRecovery(c, ReadReport(form), "Your sign-in expired. Nothing has been sent. Copy your draft below before signing in again.", 401, false)
                    .ExecuteAsync(c).ConfigureAwait(false);
                return;
            }
            if (ReportRestriction(c, viewer) is { } restriction)
            {
                await restriction.ExecuteAsync(c).ConfigureAwait(false);
                return;
            }
            string sent = form["csrf"].ToString();
            if (sent.Length != viewer.Session.Csrf.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(viewer.Session.Csrf)))
            {
                await ReportDraftRecovery(c, ReadReport(form), "This form has expired. Nothing has been sent. Copy your draft below, then open a new report form.", 403, true)
                    .ExecuteAsync(c).ConfigureAwait(false);
                return;
            }
            using (AccountStore.ActingAs("player:" + viewer.Account.Id.ToString(CultureInfo.InvariantCulture)))
                await (await handler(c, viewer, form).ConfigureAwait(false)).ExecuteAsync(c).ConfigureAwait(false);
        }));

    private IResult BugReportSignIn(HttpContext c, string? notice = null, int status = 200) =>
        Show(c, "Report a bug", "help", PageHeader("HELP", "Report a bug", "Use your existing OpenSCCT account to send a report.") +
            "<section class=\"panel report-page\"><h2>Sign in with Discord</h2>" +
            "<p>Use the same Discord account you use in OpenSCCT. You do not need a GitHub account.</p>" +
            "<p>You will review your report before it is published on GitHub. Your Discord ID and private account details are not included.</p>" +
            Link(LoginLink(BugReportReturnPath(c.Request.Path + c.Request.QueryString) ?? ReportReturnPath), "Sign in with Discord →", "button") +
            "<p class=\"account-side-note\">Already reported? " + Link(ProjectLinks.Url(ProjectDestination.Issues), "Browse existing reports ↗") + "</p></section>", notice, notice is not null, status);

    private IResult? ReportRestriction(HttpContext c, Viewer viewer) =>
        _store.ActiveBan(viewer.Account.Id, viewer.Account.DiscordId, _clientIp(c)) is null ? null :
            ReportInvalid(c, "This account cannot submit reports while it is restricted.", 403);

    private IResult ReportInvalid(HttpContext c, string message, int status) =>
        Show(c, "Report a bug", "help", PageHeader("HELP", "Report a bug", message) +
            Link(ReportReturnPath, "Open the report form", "button secondary"), status: status);

    private static string RecoverableDraft(Dictionary<string, string> values)
    {
        var text = new StringBuilder("Where it happened: ").Append(values["area"]).Append("\n\n");
        foreach (ReportField field in ReportFields)
            if (values[field.Key].Length > 0) text.Append(field.Label).Append("\n").Append(values[field.Key]).Append("\n\n");
        return "<label for=\"report-draft\">Your report draft<textarea id=\"report-draft\" readonly rows=\"14\">" + H(text.ToString().TrimEnd()) + "</textarea></label>";
    }

    private IResult ReportDraftRecovery(HttpContext c, Dictionary<string, string> values, string message, int status, bool signedIn) =>
        Show(c, "Keep your report draft", "help", PageHeader("HELP", "Keep your report draft", message) +
            "<section class=\"panel report-page form-stack\">" + RecoverableDraft(values) +
            Link(signedIn ? ReportReturnPath : LoginLink(ReportReturnPath), signedIn ? "Open a new report form" : "Sign in with Discord →", "button secondary") +
            "</section>", status: status);

    private IResult BugReportForm(HttpContext c, Viewer viewer, Dictionary<string, string> values, string? problem = null, int status = 200)
    {
        c.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (ReportRestriction(c, viewer) is { } restriction) return restriction;
        var body = new StringBuilder(PageHeader("HELP", "Report a bug", "Tell us what went wrong. You can review everything before sending."));
        body.Append("<div class=\"report-page\"><p>").Append(Link(ProjectLinks.Url(ProjectDestination.Issues), "Check existing reports ↗"))
            .Append(" to see whether someone has already reported it.</p>");
        if (!_bugReports.Enabled)
            body.Append("<p class=\"notice\">Sending reports is temporarily unavailable. You can fill in and review a report, but it cannot be sent yet.</p>");
        body.Append("<section class=\"panel\"><form class=\"form-stack\" method=\"post\" action=\"").Append(ReportReturnPath).Append("/review\">").Append(Csrf(viewer.Session.Csrf));
        body.Append("<label for=\"report-area\">Where did it happen?<select id=\"report-area\" name=\"area\">");
        string area = values.GetValueOrDefault("area", "game");
        foreach ((string key, string name) in new[] { ("game", "Game"), ("launcher", "Launcher"), ("website", "Website") })
            body.Append("<option value=\"").Append(key).Append('"').Append(area == key ? " selected" : "").Append('>').Append(name).Append("</option>");
        body.Append("</select></label>");
        foreach (ReportField field in ReportFields)
        {
            body.Append("<label for=\"report-").Append(field.Key).Append("\">").Append(field.Label)
                .Append(field.Required ? " <span class=\"field-help\">(required)</span>" : "");
            string attrs = $" id=\"report-{field.Key}\" name=\"{field.Key}\" maxlength=\"{field.Max}\" aria-describedby=\"report-{field.Key}-help\"" + (field.Required ? " required" : "");
            if (field.Multiline)
                body.Append("<textarea").Append(attrs).Append(" rows=\"4\">").Append(H(values.GetValueOrDefault(field.Key))).Append("</textarea>");
            else body.Append("<input type=\"text\"").Append(attrs).Append(" value=\"").Append(H(values.GetValueOrDefault(field.Key))).Append("\">");
            body.Append("<span class=\"field-help\" id=\"report-").Append(field.Key).Append("-help\">").Append(H(field.Help)).Append("</span></label>");
        }
        body.Append("<div class=\"form-stack\" data-report-screenshot><label for=\"report-screenshot-file\">Screenshot (optional)</label>")
            .Append("<p class=\"field-help\">A capture sent from the game is attached automatically. You can also choose a PNG. Check it for personal information before sharing. Maximum 2 MB. The image will be public with your report.</p>")
            .Append("<input type=\"file\" id=\"report-screenshot-file\" accept=\"image/png\"><input type=\"hidden\" name=\"screenshot\" value=\"")
            .Append(H(values.GetValueOrDefault("screenshot"))).Append("\"><p data-screenshot-status role=\"status\"></p>")
            .Append("<img class=\"report-screenshot\" data-screenshot-preview alt=\"Screenshot to include in the report\" hidden><button class=\"button secondary\" type=\"button\" data-remove-screenshot>Remove screenshot</button>")
            .Append("<noscript>Screenshot attachments need JavaScript. You can send a report without an image.</noscript></div>");
        body.Append("<p class=\"report-note\">Reports are public. Share only details needed to explain the problem. Do not include original game files or converted game assets.</p>")
            .Append("<button class=\"button\" type=\"submit\">Review report →</button></form></section></div>");
        return Show(c, "Report a bug", "help", body.ToString(), problem, problem is not null, status);
    }

    private static Dictionary<string, string> ReadReport(IFormCollection form)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["area"] = form["area"].ToString() };
        foreach (ReportField field in ReportFields)
            values[field.Key] = form[field.Key].ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        values["screenshot"] = form["screenshot"].ToString();
        return values;
    }

    private static string? ValidateReport(Dictionary<string, string> values)
    {
        if (values.GetValueOrDefault("screenshot", "").Length > 0 && ReportScreenshot.Decode(values["screenshot"]) is null)
            return "Choose a valid PNG screenshot under 2 MB, or remove it to continue.";
        if (values["area"] is not ("game" or "launcher" or "website")) return "Choose Game, Launcher or Website.";
        foreach (ReportField field in ReportFields)
        {
            string value = values[field.Key];
            if (field.Required && string.IsNullOrWhiteSpace(value)) return "Fill in “" + field.Label + "”.";
            if (value.Length > field.Max) return $"“{field.Label}” must be {field.Max} characters or fewer.";
            if (value.Any(ch => char.IsControl(ch) && ch is not ('\n' or '\t')) || (!field.Multiline && value.Contains('\n')))
                return "Remove unsupported characters from “" + field.Label + "”.";
        }
        return values["area"] != "website" && string.IsNullOrWhiteSpace(values["version"]) ? "Add the game or launcher version." : null;
    }

    private IResult ReviewBugReport(HttpContext c, Viewer viewer, IFormCollection form)
    {
        Dictionary<string, string> values = ReadReport(form);
        return ValidateReport(values) is { } problem ? BugReportForm(c, viewer, values, problem, 400) :
            BugReportReview(c, viewer, values, GitHubBugReports.NewSubmissionId());
    }

    private static string ReportTitle(Dictionary<string, string> values) => "[Bug] " + values["title"].Replace('@', '＠');

    private static IEnumerable<(string Heading, string Text)> ReportSections(Dictionary<string, string> values)
    {
        yield return ("Where it happened", values["area"] switch { "game" => "Game", "launcher" => "Launcher", _ => "Website" });
        if (values["version"].Length > 0) yield return ("Game or launcher version", values["version"]);
        if (values["platform"].Length > 0) yield return ("Operating system", values["platform"]);
        if (values["environment"].Length > 0) yield return ("Game or launcher details", values["environment"]);
        yield return ("Steps to reproduce", values["steps"]);
        yield return ("Expected result", values["expected"]);
        yield return ("Actual result", values["actual"]);
        if (values["evidence"].Length > 0) yield return ("Extra information", values["evidence"]);
        if (values["area"] == "website")
            yield return ("Website version", $"{MasterBuildInfo.Version}\nBuild: {MasterBuildInfo.BuildNumber}\nCommit: {MasterBuildInfo.Commit}");
    }

    private string ReportBody(Dictionary<string, string> values)
    {
        var body = new StringBuilder();
        foreach ((string heading, string text) in ReportSections(values))
        {
            // Literal blocks prevent user text from generating mentions, remote images or extra report sections.
            int longest = 0, run = 0;
            foreach (char ch in text) { run = ch == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
            string fence = new('`', Math.Max(3, longest + 1));
            body.Append("### ").Append(heading).Append("\n\n").Append(fence).Append("text\n").Append(text).Append('\n').Append(fence).Append("\n\n");
        }
        if (values.GetValueOrDefault("screenshot", "").Length > 0)
            body.Append("### Screenshot\n\n![Game screenshot](").Append(_options.PublicBaseUrl).Append(ReportScreenshot.Route)
                .Append(ReportScreenshot.Id(ReportScreenshot.Decode(values["screenshot"])!)).Append(".png)\n\n");
        return body.Append("Submitted through the OpenSCCT website.").ToString();
    }

    private IResult BugReportReview(HttpContext c, Viewer viewer, Dictionary<string, string> values, string submissionId,
        string? problem = null, int status = 200)
    {
        c.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var body = new StringBuilder(PageHeader("HELP", "Review your report", "This is what will be published in the OpenSCCT GitHub issue tracker."));
        body.Append("<div class=\"report-page\"><article class=\"panel report-preview\"><h2>").Append(H(ReportTitle(values))).Append("</h2>");
        foreach ((string heading, string text) in ReportSections(values))
            body.Append("<h3>").Append(H(heading)).Append("</h3><pre>").Append(H(text)).Append("</pre>");
        if (values.GetValueOrDefault("screenshot", "").Length > 0)
            body.Append("<h3>Screenshot</h3><img class=\"report-screenshot\" alt=\"Game screenshot to publish\" src=\"data:image/png;base64,")
                .Append(H(values["screenshot"])).Append("\">");
        body.Append("<p class=\"report-note\">Submitted through the OpenSCCT website.</p></article>")
            .Append("<form class=\"panel form-stack report-send\" method=\"post\" action=\"").Append(ReportReturnPath).Append("/submit\">")
            .Append(Csrf(viewer.Session.Csrf)).Append(Hidden("submission", submissionId));
        foreach ((string key, string value) in values) body.Append(Hidden(key, value));
        body.Append("<p>The report and any screenshot will be public. Your Discord ID and private account details will not be included.</p>")
            .Append("<label class=\"checkbox-label\"><input type=\"checkbox\" name=\"public\" value=\"yes\" required><span>I have checked this report and want to publish it.</span></label>")
            .Append("<div class=\"button-row\"><button class=\"button\" type=\"submit\"").Append(_bugReports.Enabled ? "" : " disabled")
            .Append(">Send bug report</button><button class=\"button secondary\" type=\"submit\" formaction=\"").Append(ReportReturnPath)
            .Append("/edit\" formnovalidate>Edit report</button></div>");
        if (!_bugReports.Enabled) body.Append("<p class=\"report-note\">Sending reports is temporarily unavailable. Your report has not been sent.</p>");
        body.Append("</form></div>");
        return Show(c, "Review your report", "help", body.ToString(), problem, problem is not null, status);
    }

    private async Task<IResult> SubmitBugReport(HttpContext c, Viewer viewer, IFormCollection form)
    {
        Dictionary<string, string> values = ReadReport(form);
        if (ValidateReport(values) is { } problem) return BugReportForm(c, viewer, values, problem, 400);
        string submissionId = form["submission"].ToString();
        if (form["public"] != "yes") return BugReportReview(c, viewer, values, submissionId, "Confirm that you want to publish the report.", 400);
        BugReportResult result = await _bugReports.SubmitAsync(viewer.Account.Id, submissionId, ReportTitle(values), ReportBody(values), c.RequestAborted,
            values.GetValueOrDefault("screenshot", "").Length > 0 ? ReportScreenshot.Decode(values["screenshot"]) : null).ConfigureAwait(false);
        if (result.Status is BugReportStatus.Unavailable or BugReportStatus.RateLimited or BugReportStatus.Rejected)
            return BugReportReview(c, viewer, values, GitHubBugReports.NewSubmissionId(), result.Message ?? "The report could not be sent. Your text is still here.",
                result.Status == BugReportStatus.RateLimited ? 429 : 503);
        if (result.Status is BugReportStatus.Unknown or BugReportStatus.Pending)
            return BugReportReceipt(c, viewer, result.SubmissionId ?? submissionId, values);
        c.Response.Headers.Location = ReportReturnPath + "/sent/" + (result.SubmissionId ?? submissionId);
        return Results.StatusCode(StatusCodes.Status303SeeOther);
    }

    private IResult BugReportReceipt(HttpContext c, Viewer viewer, string submissionId, Dictionary<string, string>? draft = null)
    {
        c.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        BugReportResult? result = _bugReports.Lookup(viewer.Account.Id, submissionId);
        if (result is null) return NotFound(c, "Report unavailable");
        bool success = result.Status is BugReportStatus.Created or BugReportStatus.Existing;
        string title = success ? "Bug report sent" : result.Status == BugReportStatus.Pending ? "Sending your report" : "Check your report";
        var body = new StringBuilder(PageHeader("HELP", title, success ? "Your report is now in the OpenSCCT issue tracker." :
            result.Status == BugReportStatus.Pending ? "Your report is being sent. Refresh this page in a moment." :
            "We could not confirm whether GitHub received the report. Check the issue list for it. This report will not be sent again automatically."));
        body.Append("<section class=\"panel report-page\">");
        if (success && result.IssueUrl is not null)
            body.Append(Link(result.IssueUrl, "View report #" + result.IssueNumber + " ↗", "button"))
                .Append("<p class=\"account-side-note\">Keep this link to follow the report. Reading it does not require a GitHub account.</p>");
        else
        {
            body.Append("<div class=\"button-row\">").Append(Link(ReportReturnPath + "/sent/" + submissionId, "Check report status", "button secondary"))
                .Append(Link(ProjectLinks.Url(ProjectDestination.Issues), "Browse reports ↗", "text-link")).Append("</div>");
            if (draft is not null) body.Append("<p>Keep a copy of your draft while you check the result.</p>").Append(RecoverableDraft(draft));
        }
        body.Append("<p>").Append(Link("/help", "Back to Help")).Append("</p></section>");
        return Show(c, title, "help", body.ToString());
    }
}
