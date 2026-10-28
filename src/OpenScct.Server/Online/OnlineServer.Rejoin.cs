using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

/// <summary>
/// A player who dropped out of a round (a crash, a lost connection, a quit), kept until the session goes back to the
/// lobby so he can come back on his side (docs/ONLINE.md, "Rejoining a round"). He is known again by his account, or
/// for a guest by his install id; the stats are those of the round he left (<see cref="Round"/>).
/// </summary>
internal sealed record DroppedPlayer
{
    public long? AccountId { get; init; }

    public string? InstallId { get; init; }

    /// <summary>The guest's proven player key; when known, he is matched by it instead of his install id.</summary>
    public string? PublicKey { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>The session side (0 spies, 1 mercs), as the lobby gave it (a revenge game's login swaps it).</summary>
    public byte Team { get; init; }

    /// <summary>The round he left (<see cref="OnlineServer.RoundsStarted"/>); his stats count only in that round.</summary>
    public int Round { get; init; }

    /// <summary>The stats below were read from his player (he left during the match).</summary>
    public bool HasStats { get; init; }

    public float Score { get; init; }

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public int Lives { get; init; }

    public bool Matches(long? accountId, string? installId, string? publicKey) =>
        accountId is not null ? AccountId == accountId
        : AccountId is null && (PublicKey is { } key ? key == publicKey : InstallId is { Length: > 0 } id && id == installId);
}

public sealed partial class OnlineServer
{
    private readonly List<DroppedPlayer> _dropped = new();

    /// <summary>
    /// Keeps a person who leaves a round (loading, match or results) so he can rejoin on his side: not a bot, a
    /// spectator, the listen host's own player, a kicked player, or anyone while the server closes.
    /// </summary>
    private void KeepDropped(SessionPlayer p, PlayerController? controller)
    {
        if (p.IsBot || p.IsSpectator || p.IsHost || p.Kicked || _closing || Phase == SessionPhase.Lobby ||
            (p.AccountId is null && p.InstallId is null))
        {
            return;
        }

        _dropped.RemoveAll(d => d.Matches(p.AccountId, p.InstallId, p.PublicKey));
        PlayerReplicationInfo? pri = Phase == SessionPhase.Match ? controller?.PlayerReplicationInfo : null;
        _dropped.Add(new DroppedPlayer
        {
            AccountId = p.AccountId, InstallId = p.InstallId, PublicKey = p.PublicKey, Name = p.Name, Team = p.Team, Round = RoundsStarted,
            HasStats = pri is not null, Score = pri?.Score ?? 0f, Kills = pri?.Kills ?? 0, Deaths = pri?.Deaths ?? 0,
            Lives = pri?.NumLives ?? 0,
        });
        Log($"{p.Name} dropped out of round {RoundsStarted}: his place is kept until the session is back in the lobby");
    }

    /// <summary>The dropped player a joining player is (by account, or install id for a guest), or null.</summary>
    private DroppedPlayer? FindDropped(long? accountId, string installId, string playerKey) =>
        Phase == SessionPhase.Lobby ? null
        : _dropped.Find(d => d.Matches(accountId, installId.Length > 0 ? installId : null, playerKey.Length > 0 ? playerKey : null));

    /// <summary>Accounts that dropped out of the round and may rejoin (the master offers them the rejoin at sign-in).</summary>
    private IReadOnlyList<long> RejoinAccounts()
    {
        var ids = new List<long>();
        foreach (DroppedPlayer d in _dropped)
        {
            if (d.AccountId is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// A rejoining player is in: during the match he gets back the score, kills, deaths and lives he left with (the
    /// side's lives when the series moved on to another game) and spawns, as a late joiner does (StartLateJoiner).
    /// </summary>
    private void Rejoined(SessionPlayer p, PlayerSlot slot)
    {
        if (p.Rejoin is not { } dropped)
        {
            return;
        }

        p.Rejoin = null;
        if (Phase != SessionPhase.Match || Host.Game is not { } game || slot.Controller is not PlayerController pc ||
            pc.PlayerReplicationInfo is not { bOnlySpectator: false } pri)
        {
            return;
        }

        if (dropped.HasStats && dropped.Round == RoundsStarted)
        {
            pri.Score = dropped.Score;
            pri.Kills = dropped.Kills;
            pri.Deaths = dropped.Deaths;
            pri.NumLives = dropped.Lives;
        }
        else
        {
            pri.NumLives = SGameInfo.TeamIndexOf(pc) == 0 ? game.MatchInfo?.NumLivesATT ?? 0 : game.MatchInfo?.NumLivesDEF ?? 0;
        }

        if (pri.NumLives > 0 && !game.bGameEnded)
        {
            game.eventRestartPlayer(pc);
        }

        game.UpdateTeamNumLives();
        Announce($"{p.Name} rejoined the round.");
    }
}
