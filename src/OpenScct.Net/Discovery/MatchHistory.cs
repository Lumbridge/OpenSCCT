using System;
using System.Collections.Generic;

namespace OpenScct.Net.Discovery;

public sealed record MatchTimelineEvent(int Seconds, string Kind, string Text);
public sealed record MatchHistoryEntry
{
    public long Id { get; init; }
    public string? RoundId { get; init; }
    public DateTimeOffset Ended { get; init; }
    public string Map { get; init; } = "";
    public string Server { get; init; } = "";
    public int Mode { get; init; }
    public int Winner { get; init; }
    public int Reason { get; init; }
    public int DurationSeconds { get; init; }
    public int ObjectivesTaken { get; init; }
    public int ObjectivesNeeded { get; init; }
    public bool Counted { get; init; }
}
public sealed record MatchHistoryPage(IReadOnlyList<MatchHistoryEntry> Matches, bool HasMore);
public sealed record MatchHistoryDetails(MatchHistoryEntry Match, IReadOnlyList<ResultsReportRow> Players,
    IReadOnlyList<MatchTimelineEvent>? Events, bool TimelineTruncated)
{
    public OpenScct.Net.Scoreboards.RoundScoreboard? Scoreboard { get; init; }
    public DemoEntry? Demo { get; init; }
}
