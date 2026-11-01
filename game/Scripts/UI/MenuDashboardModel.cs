using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScctGame.UI;

/// <summary>A last-good response and its current availability, independent of the rendering code.</summary>
public sealed record MenuDashboardFeed<T>(T? Value = null, bool Loading = false, bool Unavailable = false) where T : class
{
    public bool Stale => Value is not null && Unavailable;
}

/// <summary>Deterministic dashboard data for isolated menu previews and smoke tests.</summary>
public sealed record MenuDashboardSnapshot
{
    public MenuDashboardFeed<MasterStatus> Status { get; init; } = new(Unavailable: true);
    public MenuDashboardFeed<ServerListResponse> Games { get; init; } = new(Unavailable: true);
    public MenuDashboardFeed<ServerListResponse> Hubs { get; init; } = new(Unavailable: true);
    public MenuDashboardFeed<ChangelogResponse> Changelog { get; init; } = new(Unavailable: true);
    public AccountInfo? Account { get; init; }
    public bool AccountStale { get; init; }
}

public sealed record MenuUnlock(string AchievementId, string Title, string RewardName, int Current, int Target)
{
    public float Fraction => Current / (float)Target;
}

public readonly record struct MenuActivity(long Players, int Sessions);

/// <summary>A bounded polling slot; resetting its identity drops late results without changing another server's view.</summary>
internal sealed class MenuDashboardRequest<T>(ulong interval) where T : class
{
    public MenuDashboardFeed<T> View { get; private set; } = new();
    private Task<T>? _task;
    private ulong _next;

    public void Reset()
    {
        ObserveDiscarded();
        _next = 0;
        View = new();
    }

    public bool Disable()
    {
        var disabled = new MenuDashboardFeed<T>(Unavailable: true);
        bool changed = View != disabled;
        ObserveDiscarded();
        _next = 0;
        View = disabled;
        return changed;
    }

    private void ObserveDiscarded()
    {
        if (_task is { } task)
            _ = task.ContinueWith(static t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        _task = null;
    }

    public bool Pump(ulong now, bool active, Func<Task<T>> start, Func<T, bool>? unavailable = null,
        Func<T?, T, T>? merge = null)
    {
        bool changed = false;
        if (_task is { IsCompleted: true } task)
        {
            _task = null;
            if (task.IsCompletedSuccessfully)
                View = new(merge?.Invoke(View.Value, task.Result) ?? task.Result,
                    Unavailable: unavailable?.Invoke(task.Result) == true);
            else
            {
                _ = task.Exception;
                View = new(View.Value, Unavailable: true);
            }
            _next = now + (View.Unavailable ? Math.Min(interval, 120_000UL) : interval);
            changed = true;
        }
        if (active && _task is null && now >= _next)
        {
            _task = start();
            View = View with { Loading = true };
            changed = true;
        }
        return changed;
    }
}

/// <summary>Presentation rules shared by the dashboard and its detail pages; no Godot dependency.</summary>
public static class MenuDashboardModel
{
    private static readonly Regex Links = new(@"!?\[([^\]]*)\]\([^\r\n)]*\)", RegexOptions.NonBacktracking);
    private static readonly Regex Html = new(@"<[^>]*>", RegexOptions.NonBacktracking);

    /// <summary>A restarted master may have lost its release cache while GitHub is unavailable.</summary>
    public static ChangelogResponse MergeChangelog(ChangelogResponse? previous, ChangelogResponse response) =>
        response.Unavailable && response.Entries.Count == 0 && previous is { Entries.Count: > 0 }
            ? previous with { Unavailable = true }
            : response;

    /// <summary>Readable release-note text for plain labels, never HTML or BBCode.</summary>
    public static string PlainNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return string.Empty;
        // Bound work even when a custom master returns unusually large release notes.
        string input = notes.Length > 65_536 ? notes[..65_536] : notes;
        input = WebUtility.HtmlDecode(Html.Replace(Links.Replace(input, "$1"), string.Empty));
        var lines = new List<string>();
        foreach (string raw in input.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal)) continue;
            if (line.Length > 0 && line.All(c => c is '-' or '*' or '_')) continue;
            line = line.TrimStart('#', '>').TrimStart();
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) ||
                line.StartsWith("+ ", StringComparison.Ordinal)) line = "• " + line[2..];
            line = line.Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("__", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
            if (line.Length > 0 || (lines.Count > 0 && lines[^1].Length > 0)) lines.Add(line);
        }
        return string.Join('\n', lines).Trim();
    }

    /// <summary>Prefer actual bullet highlights to release headings; use prose when no bullets exist.</summary>
    public static string Preview(string? notes, int maxLines = 3)
    {
        string[] lines = PlainNotes(notes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] bullets = lines.Where(l => l.StartsWith("• ", StringComparison.Ordinal)).ToArray();
        return string.Join('\n', (bullets.Length > 0 ? bullets : lines).Take(Math.Clamp(maxLines, 1, 5))
            .Select(l => l.Length > 160 ? l[..157].TrimEnd() + "…" : l));
    }

    public static MenuActivity Activity(ServerListResponse list, ServerKind kind)
    {
        ArgumentNullException.ThrowIfNull(list);
        ServerEntry[] sessions = list.Servers.Where(s => s.Kind == kind).ToArray();
        return new MenuActivity(sessions.Sum(s => (long)Math.Max(0, s.Players)), sessions.Length);
    }

    /// <summary>Closest reported cumulative achievement with a known cosmetic still locked on the account.</summary>
    public static MenuUnlock? NextUnlock(AccountInfo? account)
    {
        if (account is null) return null;
        var reported = account.Achievements.GroupBy(a => a.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var unlocked = new HashSet<string>(account.Unlocked, StringComparer.Ordinal);
        MenuUnlock? best = null;
        foreach (Achievement achievement in Achievements.All)
        {
            if (!reported.TryGetValue(achievement.Id, out AchievementInfo? progress) || progress.Earned is not null ||
                progress.Target <= 1 || progress.Current >= progress.Target) continue;
            CosmeticItem? reward = achievement.Rewards.Where(id => !unlocked.Contains(id))
                .Select(CosmeticCatalog.Find).FirstOrDefault(item => item is not null);
            if (reward is null) continue;
            var candidate = new MenuUnlock(achievement.Id, achievement.Title, reward.Name,
                Math.Max(0, progress.Current), progress.Target);
            // Strict greater-than retains catalogue order for ties; cross-products avoid float rounding ties.
            if (best is null || (long)candidate.Current * best.Target > (long)best.Current * candidate.Target) best = candidate;
        }
        return best;
    }
}
