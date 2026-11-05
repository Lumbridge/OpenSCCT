using System;
using System.Collections.Generic;

namespace OpenScct.Net.Scoreboards;

/// <summary>An immutable, server-authored round snapshot. Combat counters are delayed until the round ends.</summary>
public sealed record RoundScoreboard
{
    public int SchemaVersion { get; init; } = 1;
    public string RoundId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public string Map { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public int Mode { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? EndedUtc { get; init; }
    public double ElapsedSeconds { get; init; }
    public double? AsOfSeconds { get; init; }
    /// <summary>Round-relative server deadline for the next combat refresh; null for final or legacy snapshots.</summary>
    public double? NextCombatUpdateSeconds { get; init; }
    public bool Completed { get; init; }
    public bool Practice { get; init; }
    public bool Partial { get; init; }
    public int Winner { get; init; } = 255;
    public int Reason { get; init; } = 255;
    public int ObjectivesTaken { get; init; }
    public int ObjectivesNeeded { get; init; }
    public IReadOnlyList<ScoreboardPlayer> Rows { get; init; } = [];
    public string? Master { get; init; }
    public long? MatchId { get; init; }
    public string? DemoId { get; init; }
}

public sealed record ScoreboardPlayer
{
    public int ParticipantId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Team { get; init; }
    public int Score { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Lives { get; init; }
    public int Objectives { get; init; }
    public int Rank { get; init; }
    public int PingMs { get; init; }
    public bool Bot { get; init; }
    public bool LeftEarly { get; init; }
    public int StatsVersion { get; init; } = 1;
    /// <summary>Null until a sample is old enough to reveal; absent keys in versioned samples mean zero.</summary>
    public IReadOnlyDictionary<string, int>? Stats { get; init; }
}
