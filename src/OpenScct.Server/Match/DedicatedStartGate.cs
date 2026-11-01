using System;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using GameK = OpenScct.Game.GameFlow.K;

namespace OpenScct.Server;

/// <summary>
/// The match start of a dedicated server, which has no host loading page: the server side of Loading_Menu's
/// <c>s_Server</c> gate (spec/game/match-lifecycle.md rules 9 and 10) run by the host. Every second, once no client is
/// still connecting and at least <see cref="PlayersToStart"/> players are in, it checks that every player's PRI
/// reported waiting (each client's loading page sends <c>PlayerWaitingToPlayToServer(true)</c>); one second after
/// that holds it clears <c>bDelayedStart</c>, sets <c>GRI.bMatchHasBegun</c> and calls <c>StartMatch</c>, as
/// <c>StartMatchOnServer</c> does for a listen host.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "The dedicated host has no loading page; the start uses the listen host's gate with a player count")]
public sealed class DedicatedStartGate
{
    private readonly SGameInfo _game;
    private float _nextCheck = -1f;
    private float _startAt = -1f;
    private int _playersToStart;

    public DedicatedStartGate(SGameInfo game, int playersToStart)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _playersToStart = Math.Max(1, playersToStart);
    }

    /// <summary>
    /// Players that must be in (and waiting) before the match starts. An online session lowers it when an expected
    /// player does not come back after a map change.
    /// </summary>
    public int PlayersToStart
    {
        get => _playersToStart;
        set => _playersToStart = Math.Max(1, value);
    }

    /// <summary>True once the match has been started.</summary>
    public bool Started { get; private set; }

    /// <summary>Level time the match started at, or -1.</summary>
    public float StartedAt { get; private set; } = -1f;

    /// <summary>Called after every server tick with the number of clients still connecting.</summary>
    public void Tick(int pendingClients)
    {
        if (Started || _game.bDeleteMe)
        {
            return;
        }

        float now = _game.Level.TimeSeconds;
        if (_startAt >= 0f)
        {
            if (pendingClients != 0 || !AllWaiting())
            {
                _startAt = -1f;
            }
            else if (now >= _startAt)
            {
                Start();
            }

            return;
        }

        if (now < _nextCheck)
        {
            return;
        }

        _nextCheck = now + GameK.StartGateServerPoll;
        if (pendingClients == 0 && AllWaiting())
        {
            _startAt = now + GameK.StartGateServerPoll;
        }
    }

    private bool AllWaiting()
    {
        if (_game.GameReplicationInfo is not { } gri)
        {
            return false;
        }

        int players = 0;
        for (int i = 0; i < gri.PRIArray.Count; i++)
        {
            if (!gri.PRIArray[i].bOnlySpectator) players++;
            if (!gri.PRIArray[i].bWaitingPlayer)
            {
                return false;
            }
        }

        return players >= PlayersToStart;
    }

    private void Start()
    {
        Started = true;
        StartedAt = _game.Level.TimeSeconds;
        _game.bDelayedStart = false;
        if (_game.GameReplicationInfo is { } gri)
        {
            gri.bMatchHasBegun = true;
        }

        _game.StartMatch();
    }
}
