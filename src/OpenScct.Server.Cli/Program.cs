using System;
using System.Globalization;
using System.Text;
using System.Threading;
using OpenScct.Bots;
using OpenScct.Net;

namespace OpenScct.Server.Cli;

/// <summary>svm-server: a headless dedicated server on UDP (LiteNetLib).</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!CliOptions.TryParse(args, out CliOptions options, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(CliOptions.Usage);
            return 2;
        }

        if (options.Help)
        {
            Console.WriteLine(CliOptions.Usage);
            return 0;
        }

        // Reloaded's process switches, as the game takes them (rl:gfx.system).
        ProcessOptions.Apply(options.Priority, options.Affinity, done => Console.WriteLine("svm-server: " + done), Console.Error.WriteLine);

        if (options.WriteConfig is { } path)
        {
            System.IO.File.WriteAllText(path, ServerConfig.Example().ToJson());
            Console.WriteLine($"Wrote {path}. Edit it, then run: svm-server --config {path}");
            return 0;
        }

        if (options.IsOnline)
        {
            return RunOnline(options);
        }

        if (options.HubBots > 0)
        {
            return HubRun.RunBots(options);
        }

        if (options.Hub)
        {
            return HubRun.RunServer(options.MaxGiven ? options : options with { MaxPlayers = OpenScct.Server.Hub.HubK.DefaultMaxPlayers });
        }

        LiteNetLibTransport transport;
        try
        {
            // A few spare UDP peers so a full server can still tell newcomers that it is full.
            transport = LiteNetLibTransport.Listen(options.Port, options.MaxPlayers + 4, options.LoopbackOnly);
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e.Message);
            return 3;
        }

        using (transport)
        {
            ServerHost host;
            MapCatalog maps;
            NetClassRegistry classes = NetClasses.Create();
            try
            {
                MapCatalog defaults = MapCatalog.CreateDefault();
                var roots = new System.Collections.Generic.List<string>();
                if (options.Maps is { } extra)
                {
                    roots.Add(extra);
                }

                roots.AddRange(defaults.Roots);
                if (options.Bots is not null && options.Game is null)
                {
                    options = options with { Game = CliOptions.StoryOptions };
                }

                if (options.Bots is not null && options.Bots == "timeout" && options.Game == CliOptions.StoryOptions)
                {
                    options = options with { Game = CliOptions.StoryOptions.Replace("?TimeLimit=10", "?TimeLimit=1", StringComparison.Ordinal) };
                }

                maps = new MapCatalog(roots.ToArray());
                host = new ServerHost(transport, new ServerSettings
                {
                    Map = options.Map,
                    MaxPlayers = options.MaxPlayers,
                    NetRate = options.NetRate,
                    Profile = options.NetProfile ?? NetProfile.Enhanced,
                    MatchSeed = options.Seed,
                    Maps = maps,
                    Net = new OpenScct.Net.NetDriverSettings { LanPlay = options.LanPlay, IsLan = options.LanPlay, UpdateGate = options.UpdateGate },
                    GameOptions = options.Game,
                    PlayersToStart = options.Bots is not null ? 4 : options.PlayersToStart,
                    Classes = classes,
                });
            }
            catch (InvalidOperationException e)
            {
                Console.Error.WriteLine(e.Message);
                return 3;
            }

            using (host)
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

                Log(string.Create(CultureInfo.InvariantCulture,
                    $"{host.Map.Name} (content {host.Map.ContentHash:x16}) on UDP port {transport.LocalPort}, up to {options.MaxPlayers} players, replication {host.NetRate} Hz ({host.Settings.Profile})"));
                var status = new StatusPrinter();
                if (options.Bots is { } script)
                {
                    using var bots = BotRun.Start(script, host, maps, classes, transport.LocalPort, new StopwatchClock());
                    host.RunRealtime(bots.Clock, stop.Token, h =>
                    {
                        status.AfterTick(h);
                        bots.AfterServerTick(stop);
                    });
                    Log(bots.Report());
                }
                else
                {
                    host.RunRealtime(new StopwatchClock(), stop.Token, status.AfterTick);
                }

                Log(string.Create(CultureInfo.InvariantCulture,
                    $"stopped after {host.World.TickCount} ticks ({host.World.TimeSeconds:F1} s of game time)"));
            }
        }

        return 0;
    }

    private static int RunOnline(CliOptions options)
    {
        ServerConfig config;
        try
        {
            config = options.Config is { } file ? ServerConfig.Load(file) : new ServerConfig();
        }
        catch (Exception e) when (e is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }

        // Command line values win over the file's.
        if (options.Name is { } name)
        {
            config = config with { Name = name };
        }

        if (options.MasterUrl is { } url)
        {
            config = config with { Master = new MasterConfig { Url = url, InviteKey = options.MasterKey } };
        }

        if (options.Port != CliOptions.DefaultPort)
        {
            config = config with { Port = options.Port };
        }

        if (options.LoopbackOnly)
        {
            config = config with { LoopbackOnly = true };
        }

        if (options.Maps is { } maps)
        {
            config = config with { Maps = System.IO.Path.GetFullPath(maps) };
        }

        if (options.NetRate > 0)
        {
            config = config with { NetRate = options.NetRate };
        }

        if (options.NetProfile is { } profile)
        {
            config = config with { NetProfile = profile == NetProfile.Stock ? "stock" : "enhanced" };
        }

        return OnlineRun.Run(config, options.Config, options.Seconds);
    }

    private static void Log(string line) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{DateTime.Now:HH:mm:ss}] {line}"));

    /// <summary>Reports players joining and leaving, from the host thread.</summary>
    private sealed class StatusPrinter
    {
        private int _players;

        public void AfterTick(ServerHost host)
        {
            int count = host.Players.Count;
            if (count == _players)
            {
                return;
            }

            _players = count;
            var names = new StringBuilder();
            foreach (PlayerSlot slot in host.Players)
            {
                if (names.Length > 0)
                {
                    names.Append(", ");
                }

                names.Append(slot.Name).Append(" (").Append(slot.Connection.Stats.RttMicros / 1000).Append(" ms)");
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"{count} player(s): {names}"));
        }
    }
}
