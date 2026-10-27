using System.Collections.Generic;
using System.Globalization;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>The state of a match pad, as its sign and floor markings show it.</summary>
public enum HubPadState : byte
{
    /// <summary>The server cannot run the pad's match (its map is not installed).</summary>
    Off = 0,

    /// <summary>Nobody on the pad.</summary>
    Idle = 1,

    /// <summary>Players on the pad, not enough for the match yet.</summary>
    Waiting = 2,

    /// <summary>Enough players: the countdown runs.</summary>
    Countdown = 3,

    /// <summary>The match was started and its players are on their way (shown for a few seconds).</summary>
    Starting = 4,
}

/// <summary>
/// The hub's session lines: text on the control channel as the online session's (<see cref="SessionProtocol"/>), each
/// starting <c>#svm hub </c>. Chat uses the control channel's chat messages, as in a match.
/// <code>
/// server -> client
///   hub welcome &lt;version&gt; &lt;yourId&gt; &lt;maxPlayers&gt; &lt;server name&gt;
///   hub side &lt;team&gt;                 the side the player now plays (0 spies, 1 mercs)
///   hub msg &lt;text&gt;                  a system line for the chat box (the message of the day, refusals)
///   hub sign &lt;id&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;RRGGBBAA&gt; &lt;scale&gt; &lt;yaw&gt; &lt;width&gt; &lt;height&gt; &lt;text&gt;   a world text (HubSigns; \n breaks a line)
///   hub unsign &lt;id&gt;
///   hub pad &lt;id&gt; &lt;state&gt; &lt;seconds&gt; &lt;map&gt; &lt;mode&gt; &lt;teamSize&gt; &lt;spies&gt; &lt;spySlots&gt; &lt;mercs&gt; &lt;mercSlots&gt;
///           &lt;loX&gt; &lt;loY&gt; &lt;hiX&gt; &lt;hiY&gt; &lt;floorZ&gt; &lt;title&gt;      a pad's match and state (to everyone, on change)
///   hub queue &lt;padId|-&gt; &lt;side|-1&gt; &lt;text&gt;        the player's own place in a pad's queue ("-": in none)
///   hub match &lt;padId&gt; &lt;port&gt; &lt;team&gt; &lt;password&gt; &lt;map&gt; &lt;mode&gt; &lt;host|-&gt;   go and play: join that session
/// client -> server
///   hub side &lt;0|1&gt;                 play the other side (respawns at the plaza)
///   hub respawn                       back to the plaza
/// login option ?HubReturn=&lt;padId&gt;: a player back from that pad's match, spawned beside the pad.
/// </code>
/// </summary>
public static class HubProtocol
{
    /// <summary>The command every hub line carries first.</summary>
    public const string Command = "hub";

    /// <summary>Version of this line set (2: signs, pads, queues and matches; 3: signs mounted on boards).</summary>
    public const int Version = 3;

    /// <summary>The login option a player back from a pad's match carries.</summary>
    public const string ReturnOption = "HubReturn";

    public static string Welcome(int playerId, int maxPlayers, string serverName) =>
        SessionProtocol.Line(Command, "welcome", Version, playerId, maxPlayers, serverName);

    public static string Side(int team) => SessionProtocol.Line(Command, "side", team);

    public static string Message(string text) => SessionProtocol.Line(Command, "msg", text);

    public static string Respawn() => SessionProtocol.Line(Command, "respawn");

    public static string Sign(HubSign sign) => SessionProtocol.Line(Command, "sign", sign.Id, sign.Position.X, sign.Position.Y, sign.Position.Z,
        HubSigns.Hex(sign.Rgba), sign.Scale, sign.Face.Yaw, sign.Face.Width, sign.Face.Height, HubSigns.EncodeText(sign.Text));

    public static string Unsign(string id) => SessionProtocol.Line(Command, "unsign", id);

    public static string Pad(HubPadInfo pad) => SessionProtocol.Line(Command, "pad", pad.Id, (int)pad.State, pad.Seconds, pad.Map, (int)pad.Mode,
        pad.TeamSize, pad.Spies, pad.SpySlots, pad.Mercs, pad.MercSlots, pad.LoX, pad.LoY, pad.HiX, pad.HiY, pad.FloorZ, pad.Title);

    public static string Queue(string? padId, int side, string text) => SessionProtocol.Line(Command, "queue", padId ?? "-", side, text);

    public static string Match(HubMatchOffer offer) => SessionProtocol.Line(Command, "match", offer.PadId, offer.Port, offer.Team, offer.Password,
        offer.Map, (int)offer.Mode, offer.Host ?? "-");

    /// <summary>Splits a hub line into its verb and fields (a name or message keeps its spaces). False for any other line.</summary>
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
            "welcome" => 4,
            "sign" => 10,
            "pad" => 16,
            "queue" => 3,
            "match" => 7,
            _ => 1,
        };
        if (rest.Length > 0 && SessionProtocol.TryParse(SessionProtocol.Prefix + "x " + rest, out _, out List<string> split, count))
        {
            fields = split;
        }

        return true;
    }

    public static float Float(IReadOnlyList<string> fields, int index, float fallback = 0f) =>
        index < fields.Count && float.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
}

/// <summary>A pad's match and state as clients get it (<c>hub pad</c>).</summary>
public sealed record HubPadInfo
{
    public required string Id { get; init; }

    public HubPadState State { get; init; }

    /// <summary>Whole seconds left of the countdown (0 outside it).</summary>
    public int Seconds { get; init; }

    /// <summary>The map code (SteeE); the client looks it up in its own catalog.</summary>
    public required string Map { get; init; }

    public OpenScct.Core.EGameMode Mode { get; init; }

    public int TeamSize { get; init; }

    public int Spies { get; init; }

    public int SpySlots { get; init; }

    public int Mercs { get; init; }

    public int MercSlots { get; init; }

    public float LoX { get; init; }

    public float LoY { get; init; }

    public float HiX { get; init; }

    public float HiY { get; init; }

    public float FloorZ { get; init; }

    /// <summary>The map's name as the menus show it.</summary>
    public string Title { get; init; } = string.Empty;

    public static HubPadInfo? Parse(IReadOnlyList<string> f)
    {
        if (f.Count < 15)
        {
            return null;
        }

        return new HubPadInfo
        {
            Id = f[0],
            State = (HubPadState)SessionProtocol.Int(f, 1),
            Seconds = SessionProtocol.Int(f, 2),
            Map = f[3],
            Mode = (OpenScct.Core.EGameMode)SessionProtocol.Int(f, 4),
            TeamSize = SessionProtocol.Int(f, 5),
            Spies = SessionProtocol.Int(f, 6),
            SpySlots = SessionProtocol.Int(f, 7),
            Mercs = SessionProtocol.Int(f, 8),
            MercSlots = SessionProtocol.Int(f, 9),
            LoX = HubProtocol.Float(f, 10),
            LoY = HubProtocol.Float(f, 11),
            HiX = HubProtocol.Float(f, 12),
            HiY = HubProtocol.Float(f, 13),
            FloorZ = HubProtocol.Float(f, 14),
            Title = SessionProtocol.Text(f, 15),
        };
    }
}

/// <summary>
/// A queued player's ticket to a pad's match (<c>hub match</c>): the session's UDP port on the hub's host (or on
/// <see cref="Host"/>), the side he plays, the session's password (the join token: only the players sent this line
/// get in), and the match.
/// </summary>
public sealed record HubMatchOffer(string PadId, int Port, int Team, string Password, string Map, OpenScct.Core.EGameMode Mode, string? Host)
{
    public static HubMatchOffer? Parse(IReadOnlyList<string> f)
    {
        if (f.Count < 6)
        {
            return null;
        }

        string host = SessionProtocol.Text(f, 6);
        return new HubMatchOffer(f[0], SessionProtocol.Int(f, 1), SessionProtocol.Int(f, 2) == 1 ? 1 : 0, f[3], f[4],
            (OpenScct.Core.EGameMode)SessionProtocol.Int(f, 5), host is "" or "-" ? null : host);
    }
}
