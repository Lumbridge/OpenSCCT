using System;
using System.Globalization;
using System.Threading;
using OpenScct.Bots;
using OpenScct.Net;

namespace OpenScct.Server.Cli;

/// <summary>
/// <c>--bots</c>: four bot clients of a Story script in this process, each on its own UDP socket to the server's port
/// on localhost, ticked right after every server tick on the host thread with the same wall clock. The run stops
/// three seconds after the round ends.
/// </summary>
internal sealed class BotRun : IDisposable
{
    private readonly BotMatch _match;
    private readonly string _script;
    private readonly long _startMicros;
    private long _endedAt = -1;

    private BotRun(BotMatch match, string script, IClock clock)
    {
        _match = match;
        _script = script;
        Clock = clock;
        _startMicros = clock.NowMicroseconds;
    }

    public IClock Clock { get; }

    public static BotRun Start(string script, ServerHost host, MapCatalog maps, NetClassRegistry classes, int port, IClock clock)
    {
        string address = "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
        // As in the tests, the spies' script plays with terminals that do not block players: the converted terminal
        // collision keeps a walker short of the use spot (docs/NETCODE.md, "Bot matches").
        bool terminalsBlock = script != "spies";

        // The walk graphs first (tens of seconds on SteeE in a Debug build), before any bot connects.
        var nav = new NavCache();
        nav.Prepare(maps, host.Map.Name, terminalsBlock);
        var settings = new BotMatchSettings
        {
            Map = host.Map.Name, Bots = StoryScripts.Bots(script), Classes = classes, Maps = maps, Nav = nav,
            TerminalsBlockPlayers = terminalsBlock,
        };
        var match = new BotMatch(host, settings, _ => LiteNetLibTransport.CreateClient(loopbackOnly: true), address, clock.NowMicroseconds);
        return new BotRun(match, script, clock);
    }

    /// <summary>Ticks the bots after a server tick; cancels the run once the round has been over for 3 s.</summary>
    public void AfterServerTick(CancellationTokenSource stop)
    {
        long now = Clock.NowMicroseconds;
        _match.StepBots(now);
        if (_match.Ended && _endedAt < 0)
        {
            _endedAt = now;
        }

        if (_endedAt >= 0 && now - _endedAt > 3_000_000)
        {
            stop.Cancel();
        }
    }

    public string Report()
    {
        double seconds = (Clock.NowMicroseconds - _startMicros) / 1_000_000.0;
        (int team, OpenScct.Game.SBase.EEndReason reason) = StoryScripts.Expected(_script);
        return string.Create(CultureInfo.InvariantCulture,
            $"bots '{_script}' over UDP for {seconds:F1} s (expected winner {team}, {reason}):{Environment.NewLine}{_match.Report(seconds)}");
    }

    public void Dispose() => _match.Dispose();
}
