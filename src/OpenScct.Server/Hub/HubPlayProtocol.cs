using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>
/// The session lines of the hub's activities (the course, the aim range, emotes, the jukebox, the plaza ball, the
/// boards): an OpenSCCT addition (spec/additions/hub.md). Text on the control channel as <see cref="HubProtocol"/>'s,
/// each line starting <c>#svm hubplay </c>. Positions are UE units, whole numbers; times are milliseconds.
/// <code>
/// server -> client
///   hubplay board &lt;id&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;text&gt;     a score board (its lines joined by '|') and where it stands
///   hubplay mark &lt;kind&gt; &lt;id&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;radius&gt;   a spot the HUD draws and prompts at (gate, checkpoint, jukebox, goal, line)
///   hubplay run start &lt;checkpoints&gt;          the course clock started for you
///   hubplay run cp &lt;passed&gt; &lt;of&gt; &lt;ms&gt;        you passed a checkpoint (split time)
///   hubplay run finish &lt;ms&gt; &lt;rank&gt; &lt;best&gt;   your run's time, its place on the board (0 none), 1 for a personal best
///   hubplay run cancel &lt;reason&gt;
///   hubplay run fall &lt;falls&gt; &lt;checkpoint&gt;    you fell into the course's pit: back on checkpoint's deck (0 the start), the clock running
///   hubplay range start &lt;seconds&gt;              your range round started
///   hubplay range score &lt;points&gt; &lt;hits&gt; &lt;shots&gt; &lt;heads&gt; &lt;msLeft&gt;
///   hubplay range end &lt;score&gt; &lt;hits&gt; &lt;shots&gt; &lt;heads&gt; &lt;rank&gt; &lt;best&gt;
///   hubplay target &lt;id&gt; &lt;ownerPlayerId&gt; &lt;up&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;   a pop-up target rose (1) or fell (0)
///   hubplay emote &lt;playerId&gt; &lt;emote&gt;          a player near you emotes (playerId: his replication info's); emote "-": he stopped
///   hubplay jukebox &lt;track&gt; &lt;title&gt;           the jukebox's track
///   hubplay ball &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;vx&gt; &lt;vy&gt;        the plaza ball (velocity in uu/s, for the client to run it on)
///   hubplay toast &lt;text&gt;                       a notice for this player
///   hubplay fanfare &lt;text&gt;                     a new record near you: a chime and a notice
/// client -> server
///   hubplay emote &lt;emote&gt;     hubplay use (start a range round, next jukebox track)     hubplay cancel (stop your run or round)
/// </code>
/// </summary>
public static class HubPlayProtocol
{
    /// <summary>The command every activity line carries first.</summary>
    public const string Command = "hubplay";

    /// <summary>Line separator inside a board's text (session lines cannot carry control characters).</summary>
    public const char BoardLineSeparator = '|';

    public static string Board(string id, FVector at, string text) =>
        SessionProtocol.Line(Command, "board", id, Whole(at.X), Whole(at.Y), Whole(at.Z),
            text.Replace('|', '/').Replace("\r", string.Empty, System.StringComparison.Ordinal).Replace('\n', BoardLineSeparator));

    public static string Mark(string kind, string id, FVector at, float radius) =>
        SessionProtocol.Line(Command, "mark", kind, id, Whole(at.X), Whole(at.Y), Whole(at.Z), Whole(radius));

    public static string RunStart(int checkpoints) => SessionProtocol.Line(Command, "run", "start", checkpoints);

    public static string RunCheckpoint(int passed, int of, int ms) => SessionProtocol.Line(Command, "run", "cp", passed, of, ms);

    public static string RunFinish(int ms, int rank, bool best) => SessionProtocol.Line(Command, "run", "finish", ms, rank, best);

    public static string RunCancel(string reason) => SessionProtocol.Line(Command, "run", "cancel", reason);

    public static string RunFall(int falls, int checkpoint) => SessionProtocol.Line(Command, "run", "fall", falls, checkpoint);

    public static string RangeStart(int seconds) => SessionProtocol.Line(Command, "range", "start", seconds);

    public static string RangeScore(int points, int hits, int shots, int heads, int msLeft) =>
        SessionProtocol.Line(Command, "range", "score", points, hits, shots, heads, msLeft);

    public static string RangeEnd(int score, int hits, int shots, int heads, int rank, bool best) =>
        SessionProtocol.Line(Command, "range", "end", score, hits, shots, heads, rank, best);

    public static string Target(int id, int owner, bool up, FVector at) =>
        SessionProtocol.Line(Command, "target", id, owner, up, Whole(at.X), Whole(at.Y), Whole(at.Z));

    public static string Emote(int playerId, string emote) => SessionProtocol.Line(Command, "emote", playerId, emote);

    public static string EmoteStop(int playerId) => SessionProtocol.Line(Command, "emote", playerId, "-");

    public static string EmoteRequest(string emote) => SessionProtocol.Line(Command, "emote", emote);

    public static string Jukebox(int track, string title) => SessionProtocol.Line(Command, "jukebox", track, title);

    public static string Ball(FVector at, float vx, float vy) =>
        SessionProtocol.Line(Command, "ball", Whole(at.X), Whole(at.Y), Whole(at.Z), Whole(vx), Whole(vy));

    public static string Toast(string text) => SessionProtocol.Line(Command, "toast", text);

    public static string Fanfare(string text) => SessionProtocol.Line(Command, "fanfare", text);

    public static string Use() => SessionProtocol.Line(Command, "use");

    public static string Cancel() => SessionProtocol.Line(Command, "cancel");

    /// <summary>Splits an activity line into its verb and fields (a text field keeps its spaces). False for any other line.</summary>
    public static bool TryParse(string line, out string verb, out List<string> fields)
    {
        verb = string.Empty;
        fields = new List<string>();
        if (!SessionProtocol.TryParse(line, out string command, out List<string> head, 2) || command != Command || head.Count == 0)
        {
            return false;
        }

        verb = head[0];
        string rest = head.Count > 1 ? head[1] : string.Empty;
        int count = verb switch
        {
            "board" => 5,
            "toast" or "fanfare" => 1,
            "jukebox" => 2,
            "run" => rest.StartsWith("cancel", System.StringComparison.Ordinal) ? 2 : int.MaxValue,
            _ => int.MaxValue,
        };
        if (rest.Length > 0 && SessionProtocol.TryParse(SessionProtocol.Prefix + "x " + rest, out _, out List<string> split, count))
        {
            fields = split;
        }

        return true;
    }

    /// <summary>A board's text as lines.</summary>
    public static string[] BoardLines(string text) => text.Split(BoardLineSeparator);

    public static float Float(IReadOnlyList<string> fields, int index) =>
        index < fields.Count && float.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    private static int Whole(float v) => (int)System.MathF.Round(v);
}
