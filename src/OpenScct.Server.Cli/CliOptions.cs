using System;
using System.Globalization;

namespace OpenScct.Server.Cli;

/// <summary>Command line of svm-server.</summary>
internal sealed record CliOptions
{
    /// <summary>Default game port (no clash with the original game or the Reloaded Editor test slots).</summary>
    public const int DefaultPort = 28888;

    public const string Usage = """
        Usage: svm-server [options]

        Online server (lobby, rotation, master listing, admin console; docs/ONLINE.md):
          --config <file>   run the online server described by this JSON file
          --online          run the online server with the default settings (SteeE Story on port 28888)
          --write-config <file>  write an example config file and exit
          --name <text>     with --online: the server's name
          --master <url>    with --online: list on this master (never the original's or Reloaded's)
          --key <text>      with --master: the master's invite key

        Hub (an OpenSCCT addition; docs/HUB.md):
          --hub             run a hub server on the Hub map (up to 50 players unless --max says; UDP port 28886 unless --port says)
          --hub --config <file>  take the hub's name, port, maxPlayers, motd, maps and master from this
                            JSON file (the online server's format; rotation and lobby fields are ignored)
          --name <text>     with --hub: the hub's name
          --motd <text>     with --hub: a line shown to each player who joins
          --master <url>    with --hub: list the hub on this master (in its hub list)
          --key <text>      with --master: the master's invite key
          --server-key <k>  with --master: the trusted-server key the master's admin gave (an official hub)
          --unlisted        with --master: register but stay off the list (join by address or id)
          --region <text>   with --master: where the hub stands (EU, US...), shown in the hub list
          --no-interest     with --hub: replicate without interest management (for comparison)
          --report <s>      with --hub or --hub-bots: seconds between measurement lines (default 10)
          --report-file <f> with --hub: also append the measurement lines to this file
          --hub-bots <n>    run n hub bot clients against --connect (or 127.0.0.1:--port) and report
          --connect <addr>  with --hub-bots: the hub's address
          --threads <n>     with --hub-bots: threads the bots run on (default 4)

        Single map server (tests and measurements):
          --port <n>        UDP port to listen on (default 28888)
          --max <n>         maximum players (default 4)
          --map <name>      map to run (default TestBox)
          --rate <hz>       replication rate, 1 to 60 (default: 60 Enhanced; Stock 20, 35 with --lanplay)
          --net <profile>   network profile: enhanced (default: every tick, a send budget per client)
                            or stock (the original's 20/35 Hz and shared send budget)
          --seed <n>        match seed (default 1)
          --seconds <n>     stop after n seconds (default: run until Ctrl+C)
          --maps <dir>      also look for SVMap folders here (before the default places)
          --lanplay         LAN tick cap (35 Hz) for the frequency gate, LAN net speed rows
          --loopback-only   listen on 127.0.0.1 and ::1 only
          --game <options>  run the SBase game rules with these URL options; "story" is SteeE Story
                            (?GameMode=5?TimeLimit=10?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3)
          --start <n>       with --game: players that must be in before the match starts (default 2)
          --update-gate <g> frequency gate of the replication pass: guarded (default), decoded,
                            stochastic or always
          --bots <script>   with --game: also run four bot clients in this process over UDP on
                            localhost (scripts: spies, mercs, timeout) and print a report at the end
          --help            show this text
        """;

    public const string StoryOptions = "?GameMode=5?TimeLimit=10?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3";

    public int Port { get; init; } = DefaultPort;

    public int MaxPlayers { get; init; } = 4;

    public string Map { get; init; } = MapCatalog.TestBox;

    /// <summary><c>--rate</c>: replication rate, or 0 for the network profile's rate.</summary>
    public int NetRate { get; init; }

    /// <summary><c>--net stock|enhanced</c>, or null when not given (Enhanced for a match, the config's for online).</summary>
    public OpenScct.Net.NetProfile? NetProfile { get; init; }

    public ulong Seed { get; init; } = 1;

    /// <summary>Stop after this many seconds; 0 runs until Ctrl+C.</summary>
    public double Seconds { get; init; }

    public bool LoopbackOnly { get; init; }

    /// <summary>Extra SVMap folder searched first.</summary>
    public string? Maps { get; init; }

    /// <summary>The dedicated server runs on a LAN (the original's -lanplay switch).</summary>
    public bool LanPlay { get; init; }

    /// <summary>URL options of the SBase game rules, or null for the M0 walkers.</summary>
    public string? Game { get; init; }

    public int PlayersToStart { get; init; } = 2;

    public OpenScct.Net.NetUpdateGate UpdateGate { get; init; } = OpenScct.Net.NetUpdateGate.DecodedGuarded;

    /// <summary>Bot script to run in process (spies, mercs, timeout), or null.</summary>
    public string? Bots { get; init; }

    public bool Help { get; init; }

    /// <summary>The online server's config file, or null.</summary>
    public string? Config { get; init; }

    /// <summary>Run the online server (from <see cref="Config"/> or the defaults).</summary>
    public bool Online { get; init; }

    /// <summary>Write an example config here and exit.</summary>
    public string? WriteConfig { get; init; }

    public string? Name { get; init; }

    public string? MasterUrl { get; init; }

    public string? MasterKey { get; init; }

    /// <summary>The trusted-server key for the master (<c>--server-key</c>).</summary>
    public string? ServerKey { get; init; }

    /// <summary>The region shown in the lists (<c>--region</c>), or null.</summary>
    public string? Region { get; init; }

    /// <summary>Register with the master but stay off its list.</summary>
    public bool Unlisted { get; init; }

    /// <summary>Run a hub server.</summary>
    public bool Hub { get; init; }

    /// <summary>Hub bots to run against <see cref="Connect"/>, or 0.</summary>
    public int HubBots { get; init; }

    public string? Connect { get; init; }

    public int Threads { get; init; } = 4;

    public bool NoInterest { get; init; }

    public double ReportSeconds { get; init; } = 10;

    public string? ReportFile { get; init; }

    public string? Motd { get; init; }

    /// <summary>--max was given.</summary>
    public bool MaxGiven { get; init; }

    /// <summary>--port was given (a hub otherwise takes its own default port, HubK.DefaultPort).</summary>
    public bool PortGiven { get; init; }

    /// <summary>The online server: a config file or <c>--online</c>, and not a hub (a hub reads the config itself).</summary>
    public bool IsOnline => (Online || Config is not null) && !Hub;

    public static bool TryParse(string[] args, out CliOptions options, out string? error)
    {
        options = new CliOptions();
        error = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help" or "-h" or "-?":
                    options = options with { Help = true };
                    continue;
                case "--loopback-only":
                    options = options with { LoopbackOnly = true };
                    continue;
                case "--lanplay":
                    options = options with { LanPlay = true };
                    continue;
                case "--online":
                    options = options with { Online = true };
                    continue;
                case "--hub":
                    options = options with { Hub = true };
                    continue;
                case "--no-interest":
                    options = options with { NoInterest = true };
                    continue;
                case "--unlisted":
                    options = options with { Unlisted = true };
                    continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                error = $"{arg} needs a value.";
                return false;
            }

            string value = args[++i];
            switch (arg)
            {
                case "--port" when TryInt(value, 1, 65535, out int port):
                    options = options with { Port = port, PortGiven = true };
                    break;
                case "--max" when TryInt(value, 1, 64, out int max):
                    options = options with { MaxPlayers = max, MaxGiven = true };
                    break;
                case "--map" when value.Length > 0:
                    options = options with { Map = value };
                    break;
                case "--maps" when value.Length > 0:
                    options = options with { Maps = value };
                    break;
                case "--rate" when TryInt(value, 1, 60, out int rate):
                    options = options with { NetRate = rate };
                    break;
                case "--net" when OpenScct.Net.NetProfiles.TryParse(value, out OpenScct.Net.NetProfile profile):
                    options = options with { NetProfile = profile };
                    break;
                case "--seed" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed):
                    options = options with { Seed = seed };
                    break;
                case "--seconds" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0:
                    options = options with { Seconds = seconds };
                    break;
                case "--game" when value.Length > 0:
                    options = options with { Game = string.Equals(value, "story", StringComparison.OrdinalIgnoreCase) ? StoryOptions : value };
                    break;
                case "--start" when TryInt(value, 1, 64, out int start):
                    options = options with { PlayersToStart = start };
                    break;
                case "--update-gate" when TryGate(value, out OpenScct.Net.NetUpdateGate gate):
                    options = options with { UpdateGate = gate };
                    break;
                case "--bots" when value is "spies" or "mercs" or "timeout":
                    options = options with { Bots = value };
                    break;
                case "--config" when value.Length > 0:
                    options = options with { Config = value };
                    break;
                case "--write-config" when value.Length > 0:
                    options = options with { WriteConfig = value };
                    break;
                case "--name" when value.Length > 0:
                    options = options with { Name = value };
                    break;
                case "--master" when value.StartsWith("http", StringComparison.OrdinalIgnoreCase):
                    options = options with { MasterUrl = value };
                    break;
                case "--key" when value.Length > 0:
                    options = options with { MasterKey = value };
                    break;
                case "--region" when value.Length is > 0 and <= 16:
                    options = options with { Region = value };
                    break;
                case "--server-key" when value.Length > 0:
                    options = options with { ServerKey = value };
                    break;
                case "--hub-bots" when TryInt(value, 1, 64, out int bots):
                    options = options with { HubBots = bots };
                    break;
                case "--connect" when value.Length > 0:
                    options = options with { Connect = value };
                    break;
                case "--threads" when TryInt(value, 1, 64, out int threads):
                    options = options with { Threads = threads };
                    break;
                case "--report" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double report) && report > 0:
                    options = options with { ReportSeconds = report };
                    break;
                case "--report-file" when value.Length > 0:
                    options = options with { ReportFile = value };
                    break;
                case "--motd" when value.Length > 0:
                    options = options with { Motd = value };
                    break;
                default:
                    error = $"Bad value '{value}' for {arg}.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryGate(string text, out OpenScct.Net.NetUpdateGate gate)
    {
        gate = text switch
        {
            "guarded" => OpenScct.Net.NetUpdateGate.DecodedGuarded,
            "decoded" => OpenScct.Net.NetUpdateGate.Decoded,
            "stochastic" => OpenScct.Net.NetUpdateGate.Stochastic,
            "always" => OpenScct.Net.NetUpdateGate.Always,
            _ => (OpenScct.Net.NetUpdateGate)255,
        };
        return gate != (OpenScct.Net.NetUpdateGate)255;
    }

    private static bool TryInt(string text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
}
