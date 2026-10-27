using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScctGame.Match;

/// <summary>Settings for a <see cref="LocalMatch"/>.</summary>
public sealed record LocalMatchSettings
{
    /// <summary>Map name as the host's map catalog knows it.</summary>
    public string Map { get; init; } = MapCatalog.TestBox;

    /// <summary>Bot clients connected over loopback next to the local player.</summary>
    public int Bots { get; init; } = 3;

    public string PlayerName { get; init; } = "Player";

    public int MaxPlayers { get; init; } = 4;
}

/// <summary>
/// A listen server match in the game process: the dedicated server code on its own thread
/// (<see cref="ListenServer"/>), the local player and a number of bot clients, all over loopback. The clients
/// tick on the caller's thread on the same 60 Hz schedule as the server, from the same clock.
/// </summary>
public sealed class LocalMatch : IDisposable
{
    /// <summary>When the caller falls further behind than this (a stall), the client backlog is dropped.</summary>
    public const int MaxCatchUpTicks = 5;

    private readonly ClientSession[] _bots;
    private readonly long _start;
    private long _ticks;
    private bool _disposed;

    public LocalMatch(LocalMatchSettings? settings = null, IClock? clock = null)
    {
        Settings = settings ?? new LocalMatchSettings();
        ArgumentOutOfRangeException.ThrowIfNegative(Settings.Bots);
        Clock = clock ?? new StopwatchClock();
        var server = new ServerSettings
        {
            Map = Settings.Map,
            MaxPlayers = Math.Max(Settings.MaxPlayers, Settings.Bots + 1),
        };
        Server = new ListenServer(Clock, server);
        _start = Clock.NowMicroseconds;
        _bots = new ClientSession[Settings.Bots];
        try
        {
            LocalPlayer = Server.ConnectLocalPlayer(_start, new ClientSettings { PlayerName = Settings.PlayerName });
            for (int i = 0; i < _bots.Length; i++)
            {
                string name = "Bot" + (i + 1).ToString(CultureInfo.InvariantCulture);
                _bots[i] = Server.ConnectLocalPlayer(_start, new ClientSettings { PlayerName = name });
            }
        }
        catch
        {
            // Do not leave the server thread running behind a match that never existed.
            LocalPlayer?.Dispose();
            foreach (ClientSession? bot in _bots)
            {
                bot?.Dispose();
            }

            Server.Dispose();
            throw;
        }
    }

    public LocalMatchSettings Settings { get; }

    public IClock Clock { get; }

    public ListenServer Server { get; }

    public ClientSession LocalPlayer { get; }

    public IReadOnlyList<ClientSession> Bots => _bots;

    /// <summary>Input for the local player's next move, used when <see cref="LocalPilot"/> is not set.</summary>
    public CubeInput LocalInput { get; set; }

    /// <summary>Input for the local player per client tick, for scripted runs.</summary>
    public Func<long, CubeInput>? LocalPilot { get; set; }

    /// <summary>Client ticks run so far.</summary>
    public long TicksRun => _ticks;

    public bool IsStopped { get; private set; }

    /// <summary>The local player and the bots, all joined.</summary>
    public bool AllJoined
    {
        get
        {
            if (LocalPlayer.State != EClientState.Joined)
            {
                return false;
            }

            foreach (ClientSession bot in _bots)
            {
                if (bot.State != EClientState.Joined)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Runs the client ticks that are due by the clock. Returns how many ran.</summary>
    public int Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStopped)
        {
            return 0;
        }

        long now = Clock.NowMicroseconds;
        long due = (now - _start) * Sim.Hz / 1_000_000;
        if (due - _ticks > MaxCatchUpTicks)
        {
            _ticks = due - MaxCatchUpTicks;
        }

        int run = 0;
        while (_ticks < due)
        {
            TickClients(now);
            _ticks++;
            run++;
        }

        return run;
    }

    /// <summary>
    /// Stops the server thread, so <see cref="ListenServer.Host"/> may be read from this thread, then ticks the
    /// clients once more so they receive what the server sent last. No further ticks run.
    /// </summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStopped)
        {
            return;
        }

        IsStopped = true;
        Server.Stop();
        LocalPilot = null;
        LocalInput = default;
        TickClients(Clock.NowMicroseconds);
    }

    private void TickClients(long now)
    {
        LocalPlayer.Input = LocalPilot?.Invoke(LocalPlayer.LocalTick) ?? LocalInput;
        LocalPlayer.Tick(now);
        for (int i = 0; i < _bots.Length; i++)
        {
            ClientSession bot = _bots[i];
            bot.Input = IsStopped ? default : BotPilot.For(i + 1, bot.LocalTick);
            bot.Tick(now);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LocalPlayer.Dispose();
        foreach (ClientSession bot in _bots)
        {
            bot.Dispose();
        }

        Server.Dispose();
    }
}
