using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenScctGame;

/// <summary>
/// Options read from the user part of the command line, everything after "--":
/// <c>OpenSCCT.exe --headless -- --svm-smoke=boot</c> or <c>OpenSCCT.exe -- --svm-map=TestBox --svm-bots=3</c>.
/// </summary>
public sealed class LaunchOptions
{
    public const string SmokePrefix = "--svm-smoke=";
    public const string SmokeTimeoutPrefix = "--svm-smoke-timeout=";
    public const string MapPrefix = "--svm-map=";
    public const string MapsPrefix = "--svm-maps=";
    public const string BotsPrefix = "--svm-bots=";
    public const string PlayPrefix = "--svm-play=";
    public const string SidePrefix = "--side=";
    public const string ModePrefix = "--mode=";
    public const string RulesPrefix = "--rules=";
    public const string HostPrefix = "--svm-host=";
    public const string NetRatePrefix = "--svm-netrate=";
    public const string SimLinkPrefix = "--svm-simlink=";
    public const string GameRootPrefix = "--svm-game-root=";
    public const string ContentPrefix = "--svm-content=";
    public const string MapDownloadsPrefix = "--svm-map-downloads=";
    public const string PlayBotsPrefix = "--bots=";
    public const string DifficultyPrefix = "--difficulty=";
    public const string RecordFlag = "--svm-record";
    public const string DemoPrefix = "--svm-demo=";
    public const string PriorityPrefix = "--priority=";
    public const string AffinityPrefix = "--affinity=";
    public const double DefaultSmokeTimeoutSeconds = 120.0;
    public const int DefaultBots = 3;
    public const int MaxBots = 15;

    public static readonly LaunchOptions Empty = new();

    /// <summary>Name of the smoke test to run, or null for a normal start.</summary>
    public string? SmokeTest { get; private init; }

    /// <summary>Wall-clock limit for the smoke test before it counts as failed.</summary>
    public double SmokeTimeoutSeconds { get; private init; } = DefaultSmokeTimeoutSeconds;

    /// <summary>Map to host a local match on at start, skipping the menus; null for the normal flow.</summary>
    public string? Map { get; private init; }

    /// <summary>Extra folders searched for SVMap folders, first to last (<c>--svm-maps=dir</c>, repeatable).</summary>
    public IReadOnlyList<string> MapRoots { get; private init; } = [];

    /// <summary>Bot clients in a local match.</summary>
    public int Bots { get; private init; } = DefaultBots;

    /// <summary>Map to play offline through the listen server path (<c>--svm-play=SteeE</c>), or null.</summary>
    public string? PlayMap { get; private init; }

    /// <summary>The side of an offline play (<c>--side=spy|merc</c>).</summary>
    public string PlaySide { get; private init; } = "spy";

    /// <summary>The game mode of an offline play (<c>--mode=Story</c>).</summary>
    public string PlayMode { get; private init; } = "Story";

    /// <summary>Where the match rules come from (<c>--rules=map|playlevel</c>).</summary>
    public string PlayRules { get; private init; } = "map";

    /// <summary>How the listen server runs (<c>--svm-host=thread|inline</c>).</summary>
    public string PlayHost { get; private init; } = "thread";

    /// <summary>Replication rate of the listen server (<c>--svm-netrate=</c>), 0 for the default.</summary>
    public int PlayNetRate { get; private init; }

    /// <summary>
    /// A simulated bad link to the play host (<c>--svm-simlink=A|B|C|D</c>: 0, 60, 120 or 250 ms ping with growing
    /// loss), or null for none. Plays with the inline host.
    /// </summary>
    public string? PlaySimLink { get; private init; }

    /// <summary>The original game folder the key layouts are read from (<c>--svm-game-root=</c>), or null.</summary>
    public string? GameRoot { get; private init; }

    /// <summary>
    /// The content folder the launcher built from the player's install (<c>--svm-content=</c>, with shared/ and
    /// svmap/), or null (then SVM_CONTENT, then a checkout's .content).
    /// </summary>
    public string? ContentRoot { get; private init; }

    /// <summary>
    /// The maps downloaded from SCCT-Maps (<c>--svm-map-downloads=</c>, the launcher's maps folder), or null (then the
    /// launcher's default folder): where a map a lobby picks is fetched to (docs/LAUNCHER.md "Downloaded maps").
    /// </summary>
    public string? MapsRoot { get; private init; }

    /// <summary>Server-side bots of an offline play (<c>--bots=3</c> fills a 2v2 next to the player), 0 for none.</summary>
    public int PlayBots { get; private init; }

    /// <summary>The offline play bots' difficulty (<c>--difficulty=easy|normal|hard|expert</c>).</summary>
    public string PlayDifficulty { get; private init; } = "normal";

    /// <summary>Record the offline play (<c>--svm-record</c>) as a demo in the user's demos folder (docs/DEMOS.md).</summary>
    public bool PlayRecord { get; private init; }

    /// <summary>A demo file to watch at start (<c>--svm-demo=path</c>), or null.</summary>
    public string? DemoFile { get; private init; }

    /// <summary>Arguments that were not recognised, kept so callers can report them.</summary>
    public IReadOnlyList<string> Unknown { get; private init; } = [];

    /// <summary>The process priority asked for (rl:gfx.system <c>-priority</c>: idle, low, below, normal, medium, above, high), or null.</summary>
    public string? Priority { get; private init; }

    /// <summary>The CPUs the process may run on (rl:gfx.system <c>-affinity</c>: a mask such as 0xF, or a list such as 0,2-3), or null.</summary>
    public string? Affinity { get; private init; }

    public static LaunchOptions Parse(IEnumerable<string> userArgs)
    {
        ArgumentNullException.ThrowIfNull(userArgs);
        string? smoke = null;
        string? map = null;
        double timeout = DefaultSmokeTimeoutSeconds;
        int bots = DefaultBots;
        var mapRoots = new List<string>();
        var unknown = new List<string>();
        string? play = null;
        string side = "spy";
        string mode = "Story";
        string rules = "map";
        string host = "thread";
        int netRate = 0;
        string? simLink = null;
        string? gameRoot = null;
        string? contentRoot = null;
        string? mapsRoot = null;
        int playBots = 0;
        string difficulty = "normal";
        bool record = false;
        string? demo = null;
        string? priority = null;
        string? affinity = null;
        foreach (string arg in userArgs)
        {
            if (arg.StartsWith(SmokePrefix, StringComparison.Ordinal))
            {
                smoke = arg[SmokePrefix.Length..];
            }
            else if (arg.StartsWith(SmokeTimeoutPrefix, StringComparison.Ordinal)
                && double.TryParse(arg[SmokeTimeoutPrefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && seconds > 0)
            {
                timeout = seconds;
            }
            else if (arg.StartsWith(MapPrefix, StringComparison.Ordinal) && arg.Length > MapPrefix.Length)
            {
                map = arg[MapPrefix.Length..];
            }
            else if (arg.StartsWith(MapsPrefix, StringComparison.Ordinal) && arg.Length > MapsPrefix.Length)
            {
                mapRoots.Add(arg[MapsPrefix.Length..]);
            }
            else if (arg.StartsWith(BotsPrefix, StringComparison.Ordinal)
                && int.TryParse(arg[BotsPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                && count <= MaxBots)
            {
                bots = count;
            }
            else if (arg.StartsWith(PlayPrefix, StringComparison.Ordinal) && arg.Length > PlayPrefix.Length)
            {
                play = arg[PlayPrefix.Length..];
            }
            else if (arg.StartsWith(SidePrefix, StringComparison.Ordinal) && arg.Length > SidePrefix.Length)
            {
                side = arg[SidePrefix.Length..].ToLowerInvariant();
            }
            else if (arg.StartsWith(ModePrefix, StringComparison.Ordinal) && arg.Length > ModePrefix.Length)
            {
                mode = arg[ModePrefix.Length..];
            }
            else if (arg.StartsWith(RulesPrefix, StringComparison.Ordinal) && arg.Length > RulesPrefix.Length)
            {
                rules = arg[RulesPrefix.Length..].ToLowerInvariant();
            }
            else if (arg.StartsWith(HostPrefix, StringComparison.Ordinal) && arg.Length > HostPrefix.Length)
            {
                host = arg[HostPrefix.Length..].ToLowerInvariant();
            }
            else if (arg.StartsWith(NetRatePrefix, StringComparison.Ordinal)
                && int.TryParse(arg[NetRatePrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int rate)
                && rate is > 0 and <= 60)
            {
                netRate = rate;
            }
            else if (arg.StartsWith(SimLinkPrefix, StringComparison.Ordinal)
                && arg[SimLinkPrefix.Length..].ToUpperInvariant() is "A" or "B" or "C" or "D")
            {
                simLink = arg[SimLinkPrefix.Length..].ToUpperInvariant();
            }
            else if (arg.StartsWith(GameRootPrefix, StringComparison.Ordinal) && arg.Length > GameRootPrefix.Length)
            {
                gameRoot = arg[GameRootPrefix.Length..];
            }
            else if (arg.StartsWith(ContentPrefix, StringComparison.Ordinal) && arg.Length > ContentPrefix.Length)
            {
                contentRoot = arg[ContentPrefix.Length..];
            }
            else if (arg.StartsWith(MapDownloadsPrefix, StringComparison.Ordinal) && arg.Length > MapDownloadsPrefix.Length)
            {
                mapsRoot = arg[MapDownloadsPrefix.Length..];
            }
            else if (arg.StartsWith(PlayBotsPrefix, StringComparison.Ordinal)
                && int.TryParse(arg[PlayBotsPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int playBotCount)
                && playBotCount <= MaxBots)
            {
                playBots = playBotCount;
            }
            else if (arg.StartsWith(DifficultyPrefix, StringComparison.Ordinal) && arg.Length > DifficultyPrefix.Length)
            {
                difficulty = arg[DifficultyPrefix.Length..].ToLowerInvariant();
            }
            else if (arg == RecordFlag)
            {
                record = true;
            }
            else if (arg.StartsWith(DemoPrefix, StringComparison.Ordinal) && arg.Length > DemoPrefix.Length)
            {
                demo = arg[DemoPrefix.Length..].Trim('"');
            }
            else if (arg.StartsWith(PriorityPrefix, StringComparison.Ordinal) && arg.Length > PriorityPrefix.Length)
            {
                priority = arg[PriorityPrefix.Length..].ToLowerInvariant();
            }
            else if (arg.StartsWith(AffinityPrefix, StringComparison.Ordinal) && arg.Length > AffinityPrefix.Length)
            {
                affinity = arg[AffinityPrefix.Length..];
            }
            else
            {
                unknown.Add(arg);
            }
        }

        return new LaunchOptions
        {
            SmokeTest = smoke,
            SmokeTimeoutSeconds = timeout,
            Map = map,
            MapRoots = mapRoots,
            Bots = bots,
            PlayMap = play,
            PlaySide = side,
            PlayMode = mode,
            PlayRules = rules,
            PlayHost = host,
            PlayNetRate = netRate,
            PlaySimLink = simLink,
            GameRoot = gameRoot,
            ContentRoot = contentRoot,
            MapsRoot = mapsRoot,
            PlayBots = playBots,
            PlayDifficulty = difficulty,
            PlayRecord = record,
            DemoFile = demo,
            Priority = priority,
            Affinity = affinity,
            Unknown = unknown,
        };
    }
}
