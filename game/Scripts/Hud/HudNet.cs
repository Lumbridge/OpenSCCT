using System;
using System.Globalization;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>Running totals of the local player's link to the server, read once a frame by <see cref="HudNet"/>.</summary>
public readonly record struct NetReadout(
    bool HasRtt,
    long RttMicros,
    long JitterMicros,
    long InBytes,
    long OutBytes,
    long InPackets,
    long InPacketsLost,
    long OutPackets,
    long OutPacketsLost,
    long Corrections,
    int ServerNetRate);

/// <summary>
/// Reloaded's network readout (rl:net.stats), drawn under the frame-rate readout: ping and jitter, packet loss each
/// way, bytes a second each way, position corrections a second and the server's send rate. Rates are worked out
/// over the last whole second.
/// </summary>
public sealed class HudNet
{
    [Provenance(ProvenanceKind.Estimated, "Reloaded's net readout layout and refresh are not decoded")]
    public const float Window = 1f;

    private NetReadout _from;
    private bool _hasFrom;
    private float _elapsed;
    private string _text = string.Empty;

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>The local player's link totals, or null when there is no connection.</summary>
    public Func<NetReadout?>? Source { get; set; }

    /// <summary>The last line drawn (empty before the first whole second).</summary>
    public string Text => _text;

    public void Draw(Vector2 screen, float dt, bool shown, bool belowFps)
    {
        DrawList.Clear();
        if (Source?.Invoke() is not { } now)
        {
            _hasFrom = false;
            _text = string.Empty;
            return;
        }

        // A new connection or controller restarts the counts.
        if (!_hasFrom || now.InPackets < _from.InPackets || now.Corrections < _from.Corrections)
        {
            _from = now;
            _hasFrom = true;
            _elapsed = 0f;
        }

        _elapsed += Math.Max(0f, dt);
        if (_elapsed >= Window)
        {
            _text = Format(_from, now, _elapsed);
            _from = now;
            _elapsed = 0f;
        }

        if (!shown || _text.Length == 0)
        {
            return;
        }

        float h = Mathf.Round(HudK.FpsTextHeight * screen.Y / MagmaRenderer.PageHeight);
        float y = belowFps ? 6f + h + 2f : 6f;
        DrawList.Items.Add(HudText.Item("Net", _text, new Rect2(0f, y, screen.X - 8f, h), h, MagmaAlign.Right, new Color(1f, 1f, 1f, 0.9f)));
    }

    /// <summary>One readout line from two totals <paramref name="seconds"/> apart.</summary>
    public static string Format(NetReadout from, NetReadout to, float seconds)
    {
        float s = Math.Max(seconds, 0.001f);
        long inPackets = to.InPackets - from.InPackets;
        long inLost = to.InPacketsLost - from.InPacketsLost;
        long outPackets = to.OutPackets - from.OutPackets;
        long outLost = to.OutPacketsLost - from.OutPacketsLost;
        float lossIn = inPackets + inLost > 0 ? 100f * inLost / (inPackets + inLost) : 0f;
        float lossOut = outPackets > 0 ? 100f * outLost / outPackets : 0f;
        float down = (to.InBytes - from.InBytes) / 1024f / s;
        float up = (to.OutBytes - from.OutBytes) / 1024f / s;
        float corrections = (to.Corrections - from.Corrections) / s;
        string ping = to.HasRtt ? (to.RttMicros / 1000).ToString(CultureInfo.InvariantCulture) : "-";
        string jitter = to.HasRtt ? (to.JitterMicros / 1000).ToString(CultureInfo.InvariantCulture) : "-";
        return string.Create(CultureInfo.InvariantCulture,
            $"ping {ping} ms  jitter {jitter}  loss in {lossIn:0.0}% out {lossOut:0.0}%  down {down:0.0} up {up:0.0} KB/s  corr {corrections:0.0}/s  {to.ServerNetRate} Hz");
    }
}
