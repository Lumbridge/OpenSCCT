using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenScct.Net.Scoreboards;

/// <summary>Private local provenance; none of this metadata is uploaded as a match report.</summary>
public sealed record ArchivedScoreboard
{
    public int Version { get; init; } = 1;
    public RoundScoreboard Scoreboard { get; init; } = new();
    public int LocalParticipantId { get; init; } = -1;
    public string Master { get; init; } = string.Empty;
    public long AccountId { get; init; }
    public IReadOnlyList<long> FetchedAccounts { get; init; } = [];
    public bool PlayedLocally { get; init; }
    public bool ImportedReplay { get; init; }
    public bool LeftEarly { get; init; }
    public string? ReplayPath { get; init; }
}

/// <summary>Atomic, independently readable scoreboard files. Replay cleanup never touches this directory.</summary>
public sealed class ScoreboardArchive(string directory)
{
    private const long MaxFileBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    public string DirectoryPath { get; } = Path.GetFullPath(directory);

    public static string Identity(ArchivedScoreboard entry)
    {
        if (Guid.TryParse(entry.Scoreboard.RoundId, out Guid round)) return "round:" + round.ToString("N");
        if (entry.Scoreboard.MatchId is > 0 && !string.IsNullOrWhiteSpace(entry.Master))
            return "master:" + NormaliseMaster(entry.Master) + ":" + entry.Scoreboard.MatchId;
        throw new InvalidDataException("The scoreboard has no stable round identity.");
    }

    public static string NormaliseMaster(string value)
    {
        string trimmed = value.Trim().TrimEnd('/');
        // HTTP origins are case-insensitive, but a master hosted under a path can distinguish /A from /a.
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri.TrimEnd('/') : trimmed;
    }

    private string FilePath(ArchivedScoreboard entry) => Path.Combine(DirectoryPath,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Identity(entry)))).ToLowerInvariant() + ".json");

    public IReadOnlyList<ArchivedScoreboard> ReadAll(Action<string>? unreadable = null)
    {
        lock (_gate)
        {
            if (!Directory.Exists(DirectoryPath)) return [];
            var records = new List<ArchivedScoreboard>();
            foreach (string file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
            {
                try { records.Add(Read(file)); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
                { unreadable?.Invoke(Path.GetFileName(file) + ": " + e.Message); }
            }
            return records.OrderByDescending(e => e.Scoreboard.EndedUtc ?? e.Scoreboard.StartedUtc).ToArray();
        }
    }

    public static bool VisibleTo(ArchivedScoreboard entry, string master, long accountId) =>
        entry.PlayedLocally || entry.ImportedReplay || CanQuery(entry, master, accountId);

    /// <summary>The current account has already received this round from this master.</summary>
    public static bool CanQuery(ArchivedScoreboard entry, string master, long accountId) =>
        accountId > 0 && !string.IsNullOrWhiteSpace(entry.Master) &&
        NormaliseMaster(entry.Master) == NormaliseMaster(master) &&
        (entry.AccountId == accountId || entry.FetchedAccounts.Contains(accountId));

    public ArchivedScoreboard Save(ArchivedScoreboard entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            Validate(entry);
            Directory.CreateDirectory(DirectoryPath);
            string destination = FilePath(entry);
            if (File.Exists(destination))
            {
                ArchivedScoreboard? previous = null;
                try { previous = Read(destination); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { }
                if (previous is not null) entry = Merge(previous, entry);
            }
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry, Json);
            if (bytes.Length > MaxFileBytes) throw new InvalidDataException("The scoreboard exceeds the archive size limit.");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); stream.Flush(flushToDisk: true); }
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return entry;
        }
    }

    public static ArchivedScoreboard Merge(ArchivedScoreboard before, ArchivedScoreboard incoming)
    {
        Validate(before);
        Validate(incoming);
        if (Identity(before) != Identity(incoming)) throw new ArgumentException("Cannot merge different rounds.");
        if (before.Master.Length > 0 && incoming.Master.Length > 0 && NormaliseMaster(before.Master) != NormaliseMaster(incoming.Master))
            throw new ArgumentException("Cannot merge round references from different masters.");
        RoundScoreboard a = before.Scoreboard, b = incoming.Scoreboard;
        bool preferIncoming = b.Completed && !a.Completed || b.Completed == a.Completed &&
            (b.ElapsedSeconds > a.ElapsedSeconds || b.ElapsedSeconds == a.ElapsedSeconds && b.Sequence >= a.Sequence);
        RoundScoreboard chosen = preferIncoming ? b : a;
        int local = before.PlayedLocally && before.LocalParticipantId >= 0 ? before.LocalParticipantId : incoming.LocalParticipantId;
        bool left = before.LeftEarly || incoming.LeftEarly;
        if (left && local >= 0)
            chosen = chosen with { Rows = chosen.Rows.Select(r => r.ParticipantId == local ? r with { LeftEarly = true } : r).ToArray() };
        string master = incoming.Master.Length > 0 ? incoming.Master : before.Master;
        long[] accounts = new[] { before, incoming }
            .Where(e => master.Length > 0 && NormaliseMaster(e.Master) == NormaliseMaster(master))
            .SelectMany(e => e.FetchedAccounts.Append(e.AccountId)).Where(id => id > 0).Distinct().Order().ToArray();
        if (accounts.Length > 256) throw new InvalidDataException("Too many account associations for one scoreboard.");
        return incoming with
        {
            Scoreboard = chosen with { Master = b.Master ?? a.Master, MatchId = b.MatchId ?? a.MatchId, DemoId = b.DemoId ?? a.DemoId },
            LocalParticipantId = local,
            PlayedLocally = before.PlayedLocally || incoming.PlayedLocally,
            ImportedReplay = before.ImportedReplay || incoming.ImportedReplay,
            LeftEarly = left,
            Master = master,
            AccountId = incoming.AccountId > 0 ? incoming.AccountId : before.AccountId,
            FetchedAccounts = accounts,
            ReplayPath = incoming.ReplayPath ?? before.ReplayPath,
        };
    }

    private static ArchivedScoreboard Read(string file)
    {
        if (new FileInfo(file).Length > MaxFileBytes) throw new InvalidDataException("Archive record is too large.");
        var entry = JsonSerializer.Deserialize<ArchivedScoreboard>(File.ReadAllBytes(file), Json)
            ?? throw new InvalidDataException("Empty scoreboard record.");
        Validate(entry);
        return entry;
    }

    private static void Validate(ArchivedScoreboard entry)
    {
        if (entry.Version != 1 || entry.Master is null || entry.AccountId < 0 ||
            entry.FetchedAccounts is not { Count: <= 256 } accounts || accounts.Any(id => id <= 0) || accounts.Distinct().Count() != accounts.Count ||
            entry.Scoreboard is not { SchemaVersion: 1, Rows: not null, RoundId: not null, Map: not null, Server: not null } board ||
            !double.IsFinite(board.ElapsedSeconds) || board.ElapsedSeconds < 0 ||
            board.AsOfSeconds is { } asOf && (!double.IsFinite(asOf) || asOf < 0 || asOf > board.ElapsedSeconds) ||
            board.Rows.Count > 256 || board.Rows.Any(p => p is null || p.Name is null || p.StatsVersion is < 0 or > 1 ||
                p.Stats?.Count > 256 || p.Stats?.Any(s => s.Key.Length > 128 || s.Value < 0) == true))
            throw new InvalidDataException("Unsupported scoreboard record.");
        _ = Identity(entry);
    }
}
