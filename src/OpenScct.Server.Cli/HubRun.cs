using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using OpenScct.Bots;
using OpenScct.Net;
using OpenScct.Server.Hub;
using OpenScct.Server.Online;

namespace OpenScct.Server.Cli;

/// <summary>
/// <c>--hub</c>: a hub server on UDP (an OpenSCCT addition, docs/HUB.md), logging every <c>--report</c> seconds its
/// players, tick time (p50, p99, max), CPU, memory and bytes sent per player. <c>--hub-bots n --connect address</c>:
/// a crowd of n hub bots in this process (split over <c>--threads</c> threads, each ticking its share at 60 Hz on the
/// wall clock) against a hub server, logging their bandwidth. Run both, in two processes, for the long soak.
/// </summary>
internal static class HubRun
{
    public static int RunServer(CliOptions options)
    {
        MasterLinkSettings? master;
        try
        {
            // A hub listens on its own default port (not the game's) unless --port or the config file names one.
            if (!options.PortGiven && options.Config is null)
            {
                options = options with { Port = HubK.DefaultPort };
            }

            (options, master) = WithConfig(options);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }

        LiteNetLibTransport transport;
        try
        {
            transport = LiteNetLibTransport.Listen(options.Port, options.MaxPlayers + 8, options.LoopbackOnly);
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            return 3;
        }

        using (transport)
        {
            HubServer hub;
            try
            {
                hub = new HubServer(transport, new HubSettings
                {
                    Name = options.Name ?? "OpenSCCT Hub",
                    MaxPlayers = options.MaxPlayers,
                    NetRate = options.NetRate,
                    Seed = options.Seed,
                    Maps = Catalog(options),
                    Classes = NetClasses.Create(),
                    InterestManagement = !options.NoInterest,
                    LanPlay = options.LanPlay,
                    Motd = options.Motd,
                    // The boards survive a restart in a small file next to the server.
                    ScoresPath = Path.Combine(AppContext.BaseDirectory, "hub-scores.json"),
                    // The hub's own bans (the admin's ban command), next to the server too.
                    Bans = new BanList(Path.Combine(AppContext.BaseDirectory, "hub-bans.json"), static () => DateTimeOffset.UtcNow),
                    MatchBots = OnlineServerBots.Factory(),
                    MatchClock = static () => new StopwatchClock(),
                    MatchLoopbackOnly = options.LoopbackOnly,
                    Master = master,
                    Dedicated = true,
                });
            }
            catch (InvalidOperationException e)
            {
                Console.Error.WriteLine(e.Message);
                return 3;
            }

            using (hub)
            using (TimerResolution.Request())
            using (var stop = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    stop.Cancel();
                };
                if (options.Seconds > 0)
                {
                    stop.CancelAfter(TimeSpan.FromSeconds(options.Seconds));
                }

                hub.Logged += Log;
                Log(hub.PadsSummary);
                Log(string.Create(CultureInfo.InvariantCulture,
                    $"hub {hub.Host.Map.Name} on UDP port {transport.LocalPort}, up to {options.MaxPlayers} players, replication {hub.Host.NetRate} Hz, interest {(options.NoInterest ? "off" : "on")}"));
                var meter = new ServerMeter(hub, options.ReportSeconds, options.ReportFile);
                RealtimeLoop.Run(new StopwatchClock(), stop.Token, now =>
                {
                    long t0 = Stopwatch.GetTimestamp();
                    hub.Tick(now);
                    meter.Tick(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, now);
                }, () => hub.IsIdle);
                meter.Flush(final: true);
                Log($"stopped: {hub.Status()}");
            }
        }

        return 0;
    }

    /// <summary>
    /// The hub's options with its config file (<c>--hub --config</c>) laid over them: the file's name, port, player
    /// limit, message of the day, maps folder, rate, seed and master. Then the master link from the file or the
    /// command line (<c>--master</c>, <c>--key</c>, <c>--server-key</c>, <c>--unlisted</c>, <c>--region</c>), or null.
    /// </summary>
    private static (CliOptions Options, MasterLinkSettings? Master) WithConfig(CliOptions options)
    {
        MasterConfig? fileMaster = null;
        if (options.Config is { } path)
        {
            ServerConfig config = ServerConfig.Load(path);
            string? maps = config.Maps is { Length: > 0 } m
                ? Path.GetFullPath(m, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".")
                : options.Maps;
            options = options with
            {
                Name = config.Name, Port = config.Port, MaxPlayers = config.MaxPlayers, MaxGiven = true,
                Motd = config.Motd ?? options.Motd, Maps = maps, NetRate = config.NetRate, Seed = config.Seed,
                LoopbackOnly = options.LoopbackOnly || config.LoopbackOnly, LanPlay = options.LanPlay || config.LanPlay,
            };
            fileMaster = config.Master;
        }

        MasterLinkSettings? master = null;
        if (options.MasterUrl is { Length: > 0 } url)
        {
            master = new MasterLinkSettings
            {
                Url = url, InviteKey = options.MasterKey, ServerKey = options.ServerKey, Listed = !options.Unlisted,
                Region = options.Region ?? string.Empty,
            };
        }
        else if (fileMaster is { Url.Length: > 0 } f)
        {
            master = new MasterLinkSettings
            {
                Url = f.Url, InviteKey = f.InviteKey, ServerKey = f.ServerKey, NatPunch = f.NatPunch, Relay = f.Relay,
                Listed = f.Listed && !options.Unlisted, Region = options.Region ?? f.Region ?? string.Empty,
            };
        }

        return (options, master);
    }

    public static int RunBots(CliOptions options)
    {
        string address = options.Connect ?? "127.0.0.1:" + options.Port.ToString(CultureInfo.InvariantCulture);
        MapCatalog maps = Catalog(options);
        NetClassRegistry classes = NetClasses.Create();
        int threads = Math.Clamp(options.Threads, 1, options.HubBots);
        var crowds = new List<HubSoak>();
        var clock = new StopwatchClock();
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        if (options.Seconds > 0)
        {
            stop.CancelAfter(TimeSpan.FromSeconds(options.Seconds));
        }

        Log($"{options.HubBots} hub bots on {threads} thread(s) to {address}: building the walk graph");
        var workers = new List<Thread>();
        var failures = new List<Exception>();
        int perThread = (options.HubBots + threads - 1) / threads;
        using (TimerResolution.Request())
        {
            for (int t = 0; t < threads; t++)
            {
                int count = Math.Min(perThread, options.HubBots - t * perThread);
                if (count <= 0)
                {
                    break;
                }

                // Each thread has its own walk graph: the graph's sweeps share the map's collision scratch.
                var crowd = new HubSoak(new HubSoakSettings
                {
                    Bots = count, Maps = maps, Classes = classes, Seed = options.Seed + (ulong)t,
                    NamePrefix = "Bot" + (char)('A' + t), JoinsPerSecond = 10f / threads,
                }, _ => LiteNetLibTransport.CreateClient(loopbackOnly: options.LoopbackOnly), address, clock.NowMicroseconds);
                crowds.Add(crowd);
                var worker = new Thread(() =>
                {
                    try
                    {
                        RealtimeLoop.Run(clock, stop.Token, crowd.Step);
                    }
                    catch (Exception e)
                    {
                        lock (failures)
                        {
                            failures.Add(e);
                        }

                        stop.Cancel();
                    }
                }) { IsBackground = true, Name = "hub bots " + t.ToString(CultureInfo.InvariantCulture) };
                workers.Add(worker);
            }

            long start = clock.NowMicroseconds;
            foreach (Thread w in workers)
            {
                w.Start();
            }

            while (!stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(options.ReportSeconds)))
            {
                Log(Summary(crowds, (clock.NowMicroseconds - start) / 1_000_000.0));
            }

            foreach (Thread w in workers)
            {
                w.Join();
            }

            Log("final: " + Summary(crowds, (clock.NowMicroseconds - start) / 1_000_000.0));
        }

        foreach (HubSoak c in crowds)
        {
            c.Dispose();
        }

        foreach (Exception e in failures)
        {
            Console.Error.WriteLine(e);
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private static string Summary(List<HubSoak> crowds, double seconds)
    {
        var all = new List<HubBotReport>();
        foreach (HubSoak c in crowds)
        {
            all.AddRange(c.Report(Math.Max(1.0, seconds)).Bots);
        }

        return new HubSoakReport(seconds, all).ToString();
    }

    private static MapCatalog Catalog(CliOptions options)
    {
        MapCatalog defaults = MapCatalog.CreateDefault();
        var roots = new List<string>();
        if (options.Maps is { } extra)
        {
            roots.Add(extra);
        }

        roots.AddRange(defaults.Roots);
        return new MapCatalog([.. roots]);
    }

    private static void Log(string line) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{DateTime.Now:HH:mm:ss}] {line}"));

    /// <summary>The server's measurements per report period: tick times, CPU, memory and bytes sent per player.</summary>
    private sealed class ServerMeter
    {
        private readonly HubServer _hub;
        private readonly double _period;
        private readonly string? _file;
        private readonly List<double> _ticks = new();
        private readonly List<double> _allTicks = new();
        private readonly Process _process = Process.GetCurrentProcess();
        private TimeSpan _cpu;
        private long _wall;
        private long _sent;
        private long _received;
        private long _periodStart = -1;
        private int _peakPlayers;
        private double _peakMemoryMb;
        private int _gen0 = GC.CollectionCount(0);
        private int _gen1 = GC.CollectionCount(1);
        private int _gen2 = GC.CollectionCount(2);
        private double _busyMs;

        public ServerMeter(HubServer hub, double period, string? file)
        {
            _hub = hub;
            _period = period;
            _file = file;
            _cpu = _process.TotalProcessorTime;
        }

        public void Tick(double ms, long now)
        {
            _ticks.Add(ms);
            _busyMs += ms;
            if (_periodStart < 0)
            {
                _periodStart = now;
                _wall = now;
            }

            if (now - _periodStart >= (long)(_period * 1_000_000))
            {
                Report(now);
                _periodStart = now;
            }
        }

        public void Flush(bool final)
        {
            if (final && _allTicks.Count > 0)
            {
                _allTicks.Sort();
                Write(string.Create(CultureInfo.InvariantCulture,
                    $"whole run: {_allTicks.Count} ticks, tick p50 {Pct(_allTicks, 0.5):F2} ms p99 {Pct(_allTicks, 0.99):F2} ms max {_allTicks[^1]:F2} ms, peak {_peakPlayers} players, peak working set {_peakMemoryMb:F0} MB"));
            }
        }

        private void Report(long now)
        {
            double seconds = (now - _wall) / 1_000_000.0;
            _wall = now;
            _process.Refresh();
            TimeSpan cpu = _process.TotalProcessorTime;
            double cpuShare = (cpu - _cpu).TotalSeconds / seconds * 100.0;
            _cpu = cpu;
            long sent = 0;
            long received = 0;
            foreach (NetConnection c in _hub.Host.Driver.ClientConnections)
            {
                sent += c.Stats.OutBytes + c.Stats.OutPackets * NetConnection.PacketOverheadBytes;
                received += c.Stats.InBytes + c.Stats.InPackets * NetConnection.PacketOverheadBytes;
            }

            int players = _hub.Players.Count;
            double down = players == 0 ? 0 : Math.Max(0, sent - _sent) / seconds / players;
            double up = players == 0 ? 0 : Math.Max(0, received - _received) / seconds / players;
            _sent = sent;
            _received = received;
            int slow = 0;
            foreach (double t in _ticks)
            {
                slow += t > 1000.0 / OpenScct.Core.Sim.Hz ? 1 : 0;
            }

            _ticks.Sort();
            double busy = _busyMs / 1000.0 / seconds * 100.0;
            _busyMs = 0;
            int g0 = GC.CollectionCount(0);
            int g1 = GC.CollectionCount(1);
            int g2 = GC.CollectionCount(2);
            string gcs = string.Create(CultureInfo.InvariantCulture, $"GCs {g0 - _gen0}/{g1 - _gen1}/{g2 - _gen2}");
            (_gen0, _gen1, _gen2) = (g0, g1, g2);
            double memory = _process.WorkingSet64 / (1024.0 * 1024.0);
            _peakPlayers = Math.Max(_peakPlayers, players);
            _peakMemoryMb = Math.Max(_peakMemoryMb, memory);
            Write(string.Create(CultureInfo.InvariantCulture,
                $"{players} players; tick p50 {Pct(_ticks, 0.5):F2} ms p99 {Pct(_ticks, 0.99):F2} ms max {_ticks[^1]:F2} ms, {slow} over 16.7 ms; ticks busy {busy:F0}% of the wall time, process CPU {cpuShare:F0}% of one core; {gcs}; working set {memory:F0} MB, managed {GC.GetTotalMemory(false) / (1024.0 * 1024.0):F0} MB; per player down {down:F0} B/s up {up:F0} B/s"));
            if (players > 0)
            {
                _allTicks.AddRange(_ticks);
            }

            _ticks.Clear();
        }

        private void Write(string line)
        {
            Log(line);
            if (_file is { } path)
            {
                File.AppendAllText(path, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}"));
            }
        }

        private static double Pct(List<double> sorted, double p) =>
            sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)(sorted.Count * p), 0, sorted.Count - 1)];
    }
}
