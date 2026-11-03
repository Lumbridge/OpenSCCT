using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenScct.Project;

internal enum ProjectDestination
{
    BugReport, FeatureRequest, Issues, Documentation, Contributing, Development, PullRequests, NewPullRequest, Help,
}

internal enum ReportSource { Game, Launcher, Website }
internal enum ReportSession { Menu, Offline, OnlineHost, OnlineClient, HubHost, HubClient, Replay }

/// <summary>Only these product details may enter a report URL. Never pass account, connection or log data.</summary>
internal sealed record ReportContext
{
    public ReportSource Source { get; init; }
    public string? Version { get; init; }
    public string? Build { get; init; }
    public string? Commit { get; init; }
    public string? Platform { get; init; }
    public string? LauncherVersion { get; init; }
    public string? InstalledGameVersion { get; init; }
    public string? Map { get; init; }
    public string? Mode { get; init; }
    public string? Side { get; init; }
    public ReportSession? Session { get; init; }
}

/// <summary>Shared source compiled into each app; no application or simulation dependencies.</summary>
internal static class ProjectLinks
{
    public const string Repository = "https://github.com/Lumbridge/OpenSCCT";
    public const string Website = "https://master.lumbridge.co.uk";
    private const int MaximumValueLength = 128;
    private const int MaximumUrlLength = 4096;

    public static string Url(ProjectDestination destination, ReportContext? context = null)
    {
        string url = destination switch
        {
            ProjectDestination.BugReport => Website + "/help/report-bug",
            ProjectDestination.FeatureRequest => Repository + "/issues/new?template=feature_request.yml",
            ProjectDestination.Issues => Repository + "/issues",
            ProjectDestination.Documentation => Repository + "/blob/main/docs/README.md",
            ProjectDestination.Contributing => Repository + "/blob/main/CONTRIBUTING.md",
            ProjectDestination.Development => Repository + "/blob/main/docs/DEVELOPMENT.md",
            ProjectDestination.PullRequests => Repository + "/pulls",
            ProjectDestination.NewPullRequest => Repository + "/compare?expand=1",
            ProjectDestination.Help => Website + "/help",
            _ => throw new ArgumentOutOfRangeException(nameof(destination)),
        };
        if (context is null || destination is not (ProjectDestination.BugReport or ProjectDestination.FeatureRequest)) return url;
        if (destination == ProjectDestination.BugReport)
        {
            string area = context.Source switch { ReportSource.Launcher => "launcher", ReportSource.Website => "website", _ => "game" };
            // Match the website form limits. Bound each detail instead of dropping match context when a URL is long.
            for (int limit = MaximumValueLength; ; limit /= 2)
            {
                string report = url + "?area=" + area + "&version=" + Uri.EscapeDataString(VersionSummary(context, limit)) +
                    "&platform=" + Uri.EscapeDataString(Clean(context.Platform, Math.Min(80, limit))) +
                    "&environment=" + Uri.EscapeDataString(EnvironmentDetails(context, limit));
                if (report.Length <= MaximumUrlLength) return report;
            }
        }
        string withEnvironment = url + "&environment=" + Uri.EscapeDataString(EnvironmentDetails(context));
        // Unusually long encoded map names must not prevent opening the form. Details remain available to copy.
        return withEnvironment.Length <= MaximumUrlLength ? withEnvironment : url;
    }

    public static string EnvironmentDetails(ReportContext context) => EnvironmentDetails(context, MaximumValueLength);

    private static string EnvironmentDetails(ReportContext context, int valueLength)
    {
        ArgumentNullException.ThrowIfNull(context);
        var lines = new List<string> { "Source: " + SourceName(context.Source) };
        string versionLabel = context.Source switch
        {
            ReportSource.Website => "Website version",
            ReportSource.Launcher => "Launcher version",
            _ => "Game version",
        };
        Add(versionLabel, context.Source == ReportSource.Launcher ? context.LauncherVersion ?? context.Version : context.Version);
        Add(context.Source == ReportSource.Website ? "Website build" : "Build", context.Build);
        Add("Commit", context.Commit);
        Add("Platform", context.Platform);
        if (context.Source == ReportSource.Launcher) Add("Installed game version", context.InstalledGameVersion);
        if (context.Source == ReportSource.Game)
        {
            Add("Map", context.Map);
            Add("Mode", context.Mode);
            Add("Side", context.Side);
            if (context.Session is { } session) Add("Session", session switch
            {
                ReportSession.Menu => "Menus",
                ReportSession.Offline => "Offline",
                ReportSession.OnlineHost => "Online host",
                ReportSession.OnlineClient => "Online client",
                ReportSession.HubHost => "Hub host",
                ReportSession.HubClient => "Hub client",
                ReportSession.Replay => "Replay",
                _ => null,
            });
        }
        return string.Join("\n", lines);

        void Add(string label, string? value)
        {
            string cleaned = Clean(value, valueLength);
            if (cleaned.Length > 0) lines.Add(label + ": " + cleaned);
        }
    }

    private static string VersionSummary(ReportContext context, int valueLength)
    {
        string version = Clean(context.Source == ReportSource.Launcher ? context.LauncherVersion ?? context.Version : context.Version, valueLength);
        return Clean(SourceName(context.Source) + (version.Length > 0 ? " " + version : " (version unavailable)"), 120);
    }

    private static string SourceName(ReportSource source) => source switch
    {
        ReportSource.Game => "OpenSCCT game",
        ReportSource.Launcher => "OpenSCCT launcher",
        ReportSource.Website => "OpenSCCT website",
        _ => "OpenSCCT",
    };

    private static string Clean(string? value, int maximumLength = MaximumValueLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new StringBuilder();
        foreach (Rune rune in value.Trim().EnumerateRunes())
        {
            if (result.Length + rune.Utf16SequenceLength > maximumLength) break;
            result.Append(Rune.IsControl(rune) ? " " : rune.ToString());
        }
        return result.ToString().Trim();
    }
}

/// <summary>Platform-independent failure handling; views keep the original operation status intact.</summary>
internal sealed class ProjectLinkActions(Func<string, bool> open, Action<string> copy)
{
    public string TargetUrl { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    public bool Open(ProjectDestination destination, ReportContext? context = null)
    {
        TargetUrl = ProjectLinks.Url(destination, context);
        try
        {
            if (open(TargetUrl)) { Message = string.Empty; return true; }
        }
        catch (Exception ex) when (Expected(ex)) { }
        Message = "Could not open your browser. Copy the link and open it in your browser.";
        return false;
    }

    public bool CopyLink() => Copy(TargetUrl, "Link copied.");
    public bool CopyDetails(ReportContext context) => Copy(ProjectLinks.EnvironmentDetails(context), "Details copied.");

    private bool Copy(string value, string success)
    {
        if (value.Length == 0) { Message = "Choose a link first."; return false; }
        try { copy(value); Message = success; return true; }
        catch (Exception ex) when (Expected(ex))
        {
            Message = "Could not copy. Select the text and copy it manually.";
            return false;
        }
    }

    private static bool Expected(Exception ex) => ex is ExternalException or InvalidOperationException or
        NotSupportedException or UnauthorizedAccessException;
}
