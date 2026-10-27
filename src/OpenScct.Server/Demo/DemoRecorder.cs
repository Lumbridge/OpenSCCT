using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Net;

namespace OpenScct.Server;

/// <summary>
/// A demo being recorded on a server (docs/DEMOS.md): a hidden spectator connection whose packets go to a
/// <see cref="DemoWriter"/>. Every actor is relevant to it (other players' controllers excepted), so playback can
/// follow anyone. Started by <see cref="ServerHost.StartRecording"/>; it notes the players and puts deaths and
/// round changes on the seek bar as markers.
/// </summary>
public sealed class DemoRecorder
{
    private readonly ServerHost _host;
    private readonly Dictionary<int, int> _deaths = new();
    private bool _matchBegun;
    private int _scanCountdown;

    internal DemoRecorder(ServerHost host, DemoWriter writer, NetConnection connection)
    {
        _host = host;
        Writer = writer;
        Connection = connection;
    }

    public DemoWriter Writer { get; }

    public NetConnection Connection { get; }

    /// <summary>The game rules the recorder's spectator logged in to.</summary>
    internal OpenScct.Game.SBase.SGameInfo? Game { get; init; }

    /// <summary>The recorder's spectator controller (null until it logged in).</summary>
    public PlayerController? Controller { get; internal set; }

    public bool IsFinished => Writer.IsFinished;

    /// <summary>Adds a marker now (an online session's round end, for example).</summary>
    public void Mark(DemoMarkerKind kind, string text) => Writer.Mark(kind, text, (uint)_host.World.TickCount);

    /// <summary>Every 30 ticks: the players and their deaths (kill markers), and the match start.</summary>
    internal void Scan()
    {
        if (Writer.IsFinished || --_scanCountdown > 0)
        {
            return;
        }

        _scanCountdown = 30;
        uint tick = (uint)_host.World.TickCount;
        GameReplicationInfo? gri = _host.World.Level.Info.GRI ?? _host.World.Level.Info.Game?.GameReplicationInfo;
        if (gri is not null && gri.bMatchHasBegun != _matchBegun)
        {
            _matchBegun = gri.bMatchHasBegun;
            Writer.Mark(_matchBegun ? DemoMarkerKind.RoundStart : DemoMarkerKind.RoundEnd, _matchBegun ? "Round start" : "Round end", tick);
        }

        foreach (PlayerReplicationInfo pri in _host.World.Level.DynamicActors<PlayerReplicationInfo>())
        {
            if (pri.bDeleteMe || pri.bOnlySpectator)
            {
                continue;
            }

            Writer.NotePlayer(pri.PlayerID, pri.PlayerName, pri.Team?.TeamIndex ?? -1);
            if (_deaths.TryGetValue(pri.PlayerID, out int deaths) && pri.Deaths > deaths)
            {
                Writer.Mark(DemoMarkerKind.Kill, pri.PlayerName + " died", tick);
            }

            _deaths[pri.PlayerID] = pri.Deaths;
        }
    }

    /// <summary>
    /// Stops recording: the recorder's spectator leaves, the last packets are written and the file gets its index.
    /// Safe to call twice.
    /// </summary>
    public void Finish()
    {
        if (Writer.IsFinished)
        {
            return;
        }

        _scanCountdown = 0;
        Scan();
        Writer.Mark(DemoMarkerKind.MatchEnd, "End", (uint)_host.World.TickCount);
        if (Connection.State != EConnectionState.USOCK_Closed)
        {
            Connection.Close("Demo finished");
        }

        Writer.Finish();
    }
}
