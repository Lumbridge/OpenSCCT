using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Net;

namespace OpenScct.Server.Hub;

/// <summary>
/// A floating text in the hub world (an OpenSCCT addition): where (UE space, uu), what it says (lines split by
/// <c>\n</c>), its colour as 0xRRGGBBAA, and its size (1 is the standard sign, readable across a pad).
/// </summary>
public readonly record struct HubSign(string Id, FVector Position, string Text, uint Rgba, float Scale, HubSignFace Face = default);

/// <summary>
/// A sign mounted on a board: the board's yaw (UE units, the way its face points) and its face size (uu). The text then
/// lies on the board's face, fitted to it, instead of a billboard turning to the camera. <see cref="None"/>: a billboard.
/// </summary>
public readonly record struct HubSignFace(float Yaw, float Width, float Height)
{
    public static HubSignFace None => new(-1f, 0f, 0f);

    public bool Mounted => Yaw >= 0f && Width > 0f && Height > 0f;
}

/// <summary>
/// Server-driven world text for the hub (spec/additions/hub.md "Signs"): anything on the hub server (the match pads,
/// later the minigames' leaderboards) puts a text in the world with <see cref="Set"/> and takes it away with
/// <see cref="Remove"/>; every client within range of it draws it as a billboard facing his camera. Signs go to the
/// clients as <c>hub sign</c> / <c>hub unsign</c> session lines, and follow the hub's interest rules: a player is sent
/// a sign once his pawn comes within <see cref="SendDistance"/> of it (the distance new actors open at), then each
/// change while he stays within <see cref="KeepDistance"/>, and a removal once he walks past it.
/// <para>Single-threaded, like <see cref="HubServer"/>: call it from the hub's thread (its tick, a session line
/// handler, a minigame's tick). Setting the same text again sends nothing.</para>
/// <example><code>
/// hub.Signs.Set("Board.AimRange", new FVector(1920, 4500, 400), "AIM RANGE\n1. Ryan 41\n2. Bot 37", HubSigns.White);
/// hub.Signs.Remove("Board.AimRange");
/// </code></example>
/// </summary>
public sealed class HubSigns
{
    /// <summary>Longest text a sign keeps (a session line holds at most 512 bytes).</summary>
    public const int MaxTextLength = 360;

    /// <summary>Longest sign id; spaces become underscores.</summary>
    public const int MaxIdLength = 48;

    /// <summary>Lines a player is sent per pass at most (the rest follow on the next pass).</summary>
    public const int MaxLinesPerPass = 12;

    public const uint White = 0xFFFFFFFFu;

    private readonly Dictionary<string, Entry> _signs = new(StringComparer.Ordinal);
    private readonly Dictionary<PlayerSlot, Dictionary<string, int>> _known = new();
    private readonly List<string> _drop = new();

    private sealed class Entry
    {
        public HubSign Sign;
        public int Version;
    }

    /// <param name="sendDistance">A player is sent a sign from this far (uu).</param>
    /// <param name="keepDistance">A player keeps a sign (and its changes) until this far.</param>
    public HubSigns(float sendDistance = 7000f, float keepDistance = 8000f)
    {
        SendDistance = sendDistance;
        KeepDistance = Math.Max(sendDistance, keepDistance);
    }

    public float SendDistance { get; }

    public float KeepDistance { get; }

    /// <summary>Every sign there is now.</summary>
    public IEnumerable<HubSign> All
    {
        get
        {
            foreach (Entry e in _signs.Values)
            {
                yield return e.Sign;
            }
        }
    }

    public int Count => _signs.Count;

    /// <summary>A colour as signs carry it.</summary>
    public static uint Rgba(byte r, byte g, byte b, byte a = 255) => (uint)(r << 24 | g << 16 | b << 8 | a);

    /// <summary>Puts up the sign <paramref name="id"/>, or changes it.</summary>
    public void Set(string id, FVector position, string text, uint rgba = White, float scale = 1f, HubSignFace face = default)
    {
        string key = CleanId(id);
        var sign = new HubSign(key, position, CleanText(text), rgba, Math.Clamp(scale, 0.1f, 10f), face.Mounted ? face : HubSignFace.None);
        if (_signs.TryGetValue(key, out Entry? e))
        {
            if (e.Sign == sign)
            {
                return;
            }

            e.Sign = sign;
            e.Version++;
            return;
        }

        _signs[key] = new Entry { Sign = sign, Version = 1 };
    }

    /// <summary>Changes only the text of a sign there already is (false when there is none).</summary>
    public bool SetText(string id, string text)
    {
        if (!_signs.TryGetValue(CleanId(id), out Entry? e))
        {
            return false;
        }

        Set(e.Sign.Id, e.Sign.Position, text, e.Sign.Rgba, e.Sign.Scale, e.Sign.Face);
        return true;
    }

    /// <summary>Takes the sign <paramref name="id"/> down for everyone. False when there was none.</summary>
    public bool Remove(string id)
    {
        string key = CleanId(id);
        if (!_signs.Remove(key))
        {
            return false;
        }

        foreach ((PlayerSlot slot, Dictionary<string, int> known) in _known)
        {
            if (known.Remove(key))
            {
                Send(slot, HubProtocol.Unsign(key));
            }
        }

        return true;
    }

    public bool TryGet(string id, out HubSign sign)
    {
        if (_signs.TryGetValue(CleanId(id), out Entry? e))
        {
            sign = e.Sign;
            return true;
        }

        sign = default;
        return false;
    }

    /// <summary>
    /// Sends <paramref name="slot"/> the signs he should now have from <paramref name="viewPoint"/> (his pawn), and the
    /// removals of those he walked away from. No view point (no pawn): nothing changes for him.
    /// </summary>
    internal void Update(PlayerSlot slot, FVector? viewPoint)
    {
        if (viewPoint is not { } eye)
        {
            return;
        }

        if (!_known.TryGetValue(slot, out Dictionary<string, int>? known))
        {
            known = new Dictionary<string, int>(StringComparer.Ordinal);
            _known[slot] = known;
        }

        int sent = 0;
        float send2 = SendDistance * SendDistance;
        float keep2 = KeepDistance * KeepDistance;
        foreach (Entry e in _signs.Values)
        {
            if (sent >= MaxLinesPerPass)
            {
                break;
            }

            float d2 = (e.Sign.Position - eye).SizeSquared();
            bool has = known.TryGetValue(e.Sign.Id, out int version);
            if ((!has && d2 <= send2) || (has && version != e.Version && d2 <= keep2))
            {
                known[e.Sign.Id] = e.Version;
                Send(slot, HubProtocol.Sign(e.Sign));
                sent++;
            }
        }

        _drop.Clear();
        foreach ((string id, int _) in known)
        {
            if (!_signs.TryGetValue(id, out Entry? e) || (e.Sign.Position - eye).SizeSquared() > keep2)
            {
                _drop.Add(id);
            }
        }

        foreach (string id in _drop)
        {
            known.Remove(id);
            Send(slot, HubProtocol.Unsign(id));
        }
    }

    /// <summary>Forgets what a player who left was sent.</summary>
    internal void Forget(PlayerSlot slot) => _known.Remove(slot);

    private static void Send(PlayerSlot slot, string line) => slot.Connection.ControlChannel?.Send(ControlMessage.TextLine(line));

    private static string CleanId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var sb = new StringBuilder(Math.Min(id.Length, MaxIdLength));
        foreach (char c in id.Trim())
        {
            if (sb.Length >= MaxIdLength)
            {
                break;
            }

            sb.Append(char.IsWhiteSpace(c) || char.IsControl(c) ? '_' : c);
        }

        return sb.ToString();
    }

    private static string CleanText(string? text)
    {
        string t = (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);
        return t.Length > MaxTextLength ? t[..MaxTextLength] : t;
    }

    /// <summary>A sign's text on the wire: <c>\n</c> for a line break, <c>\\</c> for a backslash.</summary>
    public static string EncodeText(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    public static string DecodeText(string wire)
    {
        var sb = new StringBuilder(wire.Length);
        for (int i = 0; i < wire.Length; i++)
        {
            char c = wire[i];
            if (c == '\\' && i + 1 < wire.Length)
            {
                char n = wire[++i];
                sb.Append(n == 'n' ? '\n' : n);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    internal static string Hex(uint rgba) => rgba.ToString("X8", CultureInfo.InvariantCulture);
}
