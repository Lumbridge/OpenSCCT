using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenScct.Server.Online;

/// <summary>One player's line of a logged round.</summary>
public sealed record MatchLogPlayer(string Name, byte Team, int Score, int Kills, int Deaths, int Lives, bool Bot, long? AccountId,
    string? PlayerKey, int PingMs);

/// <summary>What <see cref="MatchLog"/> writes for one round.</summary>
public sealed record MatchLogEntry
{
    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset EndedUtc { get; init; }

    public string Server { get; init; } = string.Empty;

    /// <summary>The session's round count (1 for the first round since the server started).</summary>
    public int Round { get; init; }

    public required LobbySetup Setup { get; init; }

    public required MatchResults Results { get; init; }

    public IReadOnlyList<MatchLogPlayer> Players { get; init; } = [];
}

/// <summary>
/// The server's match log (FEATURES rl:qol.matchlog, Reloaded's <c>[MatchInfo]</c> lines in ChaosTheory.log): one JSON
/// line per finished round, appended to a local file, in UTC. Each line has the start and end times and the duration,
/// the map and mode, the round's settings (time limit, lives, respawn, kill limit, disks, revenge, games in a row,
/// practice, Alternate Mode), the objectives taken and needed, the winner and the end reason in the end menu's words,
/// each side's total score, and every player (name, side, score, kills, deaths, lives left, bot, account, player key,
/// ping). Community tools read the file; nothing is sent anywhere.
/// </summary>
public sealed class MatchLog
{
    public MatchLog(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    public string Path { get; }

    /// <summary>Appends the round's line; false (with the error) when the file cannot be written.</summary>
    public bool Write(MatchLogEntry entry, out string? error)
    {
        ArgumentNullException.ThrowIfNull(entry);
        error = null;
        try
        {
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            File.AppendAllText(Path, ToJson(entry) + "\n", new UTF8Encoding(false));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>The round as one line of JSON.</summary>
    public static string ToJson(MatchLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LobbySetup s = entry.Setup;
        MatchResults r = entry.Results;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("utc", entry.EndedUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("started", entry.StartedUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteNumber("durationSeconds", Math.Max(0, (int)(entry.EndedUtc - entry.StartedUtc).TotalSeconds));
            w.WriteString("server", entry.Server);
            w.WriteNumber("round", entry.Round);
            w.WriteString("map", r.Map.Length > 0 ? r.Map : s.Map);
            w.WriteString("mode", LobbySetup.ModeName(s.Mode));

            w.WriteStartObject("settings");
            w.WriteNumber("timeLimit", s.TimeLimit);
            w.WriteNumber("spyLives", s.SpyLives);
            w.WriteNumber("mercLives", s.MercLives);
            w.WriteNumber("respawnSeconds", s.RespawnSeconds);
            w.WriteNumber("killLimit", s.ScoreLimit);
            w.WriteNumber("disks", s.Disks);
            w.WriteNumber("disksToWin", s.DisksToWin);
            w.WriteBoolean("revenge", s.Revenge);
            w.WriteNumber("gamesInARow", s.GamesInARow);
            w.WriteBoolean("practice", s.Practice);
            w.WriteBoolean("alternate", s.Alternate.On);
            w.WriteEndObject();

            w.WriteNumber("objectivesTaken", r.ObjectivesTaken);
            w.WriteNumber("objectivesNeeded", r.ObjectivesNeeded);
            w.WriteString("winner", r.WinningTeam switch { 0 => "spies", 1 => "mercenaries", _ => "none" });
            w.WriteString("endReason", MatchResults.ReasonText(r.WinningReason));
            w.WriteNumber("gamesLeft", r.GamesLeft);

            int spyScore = 0;
            int mercScore = 0;
            foreach (MatchLogPlayer p in entry.Players)
            {
                if (p.Team == 0)
                {
                    spyScore += p.Score;
                }
                else if (p.Team == 1)
                {
                    mercScore += p.Score;
                }
            }

            w.WriteStartObject("score");
            w.WriteNumber("spies", spyScore);
            w.WriteNumber("mercenaries", mercScore);
            w.WriteEndObject();

            w.WriteStartArray("players");
            foreach (MatchLogPlayer p in entry.Players)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("side", p.Team switch { 0 => "spies", 1 => "mercenaries", _ => "spectator" });
                w.WriteNumber("score", p.Score);
                w.WriteNumber("kills", p.Kills);
                w.WriteNumber("deaths", p.Deaths);
                w.WriteNumber("lives", p.Lives);
                w.WriteBoolean("bot", p.Bot);
                if (p.AccountId is { } account)
                {
                    w.WriteNumber("account", account);
                }

                if (p.PlayerKey is { } key)
                {
                    w.WriteString("key", key);
                }

                w.WriteNumber("pingMs", p.PingMs);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
