using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenScct.Server.Online;

/// <summary>Whether a player has the session's map, as its client checked it against the server's content hash.</summary>
public enum MapState : byte
{
    Unknown = 0,
    Ok = 1,
    Missing = 2,
    Differs = 3,
}

/// <summary>
/// Where the session's map can be downloaded: a map of the SCCT-Maps repository (its <paramref name="Id"/> there,
/// the <paramref name="Commit"/> the host downloaded it at and its files' <paramref name="Fingerprint"/>), so a
/// player without it can fetch the same version (the lobby line <c>mapsource</c>, docs/ONLINE.md).
/// </summary>
public sealed record MapSourceInfo(string Id, string Commit, string Fingerprint);

/// <summary>Flags of a lobby player line.</summary>
[Flags]
public enum SessionPlayerFlags
{
    None = 0,
    Ready = 1,
    Host = 2,
    Admin = 4,
    Spectator = 8,
    InGame = 16,
    Loading = 32,

    /// <summary>May change the setup, launch and kick (the host, an admin, anyone on an open dedicated server).</summary>
    Control = 64,

    /// <summary>The lobby's "Restrict Mic to Team" is on: the other team does not hear this player in the lobby.</summary>
    TeamVoice = 128,

    /// <summary>A server bot (Create Session's "Bots"): always ready, never talks, cannot be kicked.</summary>
    Bot = 256,

    /// <summary>A signed-in player: the name is the account's claimed name, checked with the master's ticket.</summary>
    Verified = 512,

    /// <summary>The host or an admin banned this player from voice (PlayerReplicationInfo.bVoiceBanned).</summary>
    VoiceBanned = 1024,
}

/// <summary>
/// The session protocol: lines of text on the control channel, starting with <see cref="Prefix"/>, as UE2's control
/// channel carries its own text commands. The lobby, travel and results run on it; chat uses the control channel's
/// chat messages. Each line is one command and its fields separated by single spaces; a last field may hold spaces
/// (names, messages). Lines stay under the control message's 512 bytes.
/// <code>
/// server -> client
///   hello &lt;version&gt; &lt;yourId&gt; &lt;dedicated&gt; &lt;maxPlayers&gt; &lt;server name&gt;
///   setup &lt;contentHex&gt; &lt;options&gt;           the match the lobby sets up (LobbySetup.ToOptions)
///   player &lt;id&gt; &lt;team&gt; &lt;flags&gt; &lt;ping&gt; &lt;mapState&gt; &lt;name&gt;
///   gone &lt;id&gt;
///   phase &lt;phase&gt; &lt;countdown tenths&gt;
///   msg &lt;text&gt;                                a system line for the lobby chat box
///   travel &lt;map&gt; &lt;contentHex&gt;               reconnect for the next map
///   row &lt;id&gt; &lt;team&gt; &lt;score&gt; &lt;kills&gt; &lt;deaths&gt; &lt;lives&gt; &lt;name&gt;
///   results &lt;winner&gt; &lt;reason&gt; &lt;taken&gt; &lt;needed&gt; &lt;gamesLeft&gt; &lt;map&gt;   (after its rows)
///   kicked &lt;reason&gt;
/// client -> server
///   team &lt;0|1|255&gt;    ready &lt;0|1&gt;    setup &lt;options&gt;    launch    lobby    teamvoice &lt;0|1&gt;
///   mapcheck &lt;map&gt; &lt;mapState&gt;    kick &lt;id&gt;    ban &lt;id&gt;    voiceban &lt;id&gt; &lt;0|1&gt;    admin &lt;password&gt;
///   cmd &lt;console command&gt;
/// </code>
/// </summary>
public static class SessionProtocol
{
    /// <summary>Marks a control text line as a session command.</summary>
    public const string Prefix = "#svm ";

    /// <summary>Version of this line set; a client refuses a server with another.</summary>
    public const int Version = 1;

    public const byte SpectatorTeam = 255;

    public static bool IsSession(string? line) => line is not null && line.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Builds a line from a command and its fields.</summary>
    public static string Line(string command, params object[] fields)
    {
        var sb = new StringBuilder(Prefix).Append(command);
        foreach (object f in fields)
        {
            sb.Append(' ').Append(Field(f));
        }

        return sb.ToString();
    }

    private static string Field(object value) => value switch
    {
        bool b => b ? "1" : "0",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Clean(value?.ToString()),
    };

    /// <summary>Text fields lose control characters (a line ends at the end of the message).</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(char.IsControl(c) ? ' ' : c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits a session line into its command and fields; the last field takes the rest of the line when
    /// <paramref name="fieldCount"/> is reached. False for a line that is not a session line.
    /// </summary>
    public static bool TryParse(string line, out string command, out List<string> fields, int fieldCount = int.MaxValue)
    {
        command = string.Empty;
        fields = new List<string>();
        if (!IsSession(line))
        {
            return false;
        }

        string rest = line[Prefix.Length..];
        int space = rest.IndexOf(' ', StringComparison.Ordinal);
        command = space < 0 ? rest : rest[..space];
        rest = space < 0 ? string.Empty : rest[(space + 1)..];
        while (rest.Length > 0)
        {
            if (fields.Count == fieldCount - 1)
            {
                fields.Add(rest);
                break;
            }

            int next = rest.IndexOf(' ', StringComparison.Ordinal);
            if (next < 0)
            {
                fields.Add(rest);
                break;
            }

            fields.Add(rest[..next]);
            rest = rest[(next + 1)..];
        }

        return command.Length > 0;
    }

    public static int Int(IReadOnlyList<string> fields, int index, int fallback = 0) =>
        index < fields.Count && int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    public static string Text(IReadOnlyList<string> fields, int index) => index < fields.Count ? fields[index] : string.Empty;

    public static string PhaseName(OpenScct.Net.Discovery.SessionPhase phase) => phase switch
    {
        OpenScct.Net.Discovery.SessionPhase.Lobby => "lobby",
        OpenScct.Net.Discovery.SessionPhase.Loading => "loading",
        OpenScct.Net.Discovery.SessionPhase.Match => "match",
        _ => "results",
    };

    public static OpenScct.Net.Discovery.SessionPhase ParsePhase(string text) => text switch
    {
        "loading" => OpenScct.Net.Discovery.SessionPhase.Loading,
        "match" => OpenScct.Net.Discovery.SessionPhase.Match,
        "results" => OpenScct.Net.Discovery.SessionPhase.Results,
        _ => OpenScct.Net.Discovery.SessionPhase.Lobby,
    };
}
