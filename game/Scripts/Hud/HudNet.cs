using System;
using System.Collections.Generic;
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
    int ServerNetRate,
    bool IsHost = false,
    int Clients = 0,
    int BestClientPingMs = 0,
    int WorstClientPingMs = 0,
    float BufferMs = 0,
    long BufferUnderflows = 0,
    long MovesReplayed = 0,
    float CorrectionDistance = 0,
    long AnimationGaps = 0,
    float SnapshotAgeMs = 0,
    long PoseResets = 0,
    long PredictionResets = 0,
    long StateGaps = 0);

/// <summary>
/// Reloaded's network readout (rl:net.stats), drawn under the frame-rate readout: ping and jitter, packet loss each
/// way (only with <see cref="ShowLoss"/>, Reloaded's <c>show_net_loss</c>), bytes a second each way, position
/// corrections a second and the server's send rate. A compact panel groups the metrics into measured, wrapped rows.
/// Rates are worked out over the last whole second; a host also sees its players' best and worst ping.
/// </summary>
public sealed class HudNet
{
    [Provenance(ProvenanceKind.Estimated, "Reloaded's net readout layout and refresh are not decoded")]
    public const float Window = 1f;

    private NetReadout _from;
    private bool _hasFrom;
    private float _elapsed;
    private string _text = string.Empty;
    private string _hostText = string.Empty;
    private readonly record struct Row(string Label, string Value, bool Alert = false);
    private List<Row> _rows = [];
    private MagmaFont? _font;
    private WhiteRegion? _white;
    private bool _layoutDirty = true;
    private Vector2 _drawnScreen;
    private bool _drawnBelowFps;

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>The local player's link totals, or null when there is no connection.</summary>
    public Func<NetReadout?>? Source { get; set; }

    /// <summary>The last readout, with one line per group (empty before the first whole second).</summary>
    public string Text => _text;

    /// <summary>The listen host's line (best and worst client ping), empty for a client or a host alone.</summary>
    public string HostText => _hostText;

    /// <summary>Show the packet loss each way (Reloaded's <c>show_net_loss</c>, off by default).</summary>
    public bool ShowLoss { get; set; }

    /// <summary>Uses the HUD's actual font metrics and atlas for the bounded readout panel.</summary>
    public void Configure(HudAssets assets)
    {
        _font = assets.Font(HudAssets.DefaultFont);
        _white = WhiteRegion.Find(assets);
        _layoutDirty = true;
    }

    public void Draw(Vector2 screen, float dt, bool shown, bool belowFps)
    {
        if (Source?.Invoke() is not { } now)
        {
            DrawList.Clear();
            _hasFrom = false;
            _text = string.Empty;
            _hostText = string.Empty;
            _rows.Clear();
            _layoutDirty = true;
            return;
        }

        // A new connection or controller restarts the counts.
        if (!_hasFrom || now.InPackets < _from.InPackets || now.Corrections < _from.Corrections)
        {
            _from = now;
            _hasFrom = true;
            _elapsed = 0f;
            _layoutDirty = true;
        }

        _elapsed += Math.Max(0f, dt);
        if (_elapsed >= Window)
        {
            _rows = FormatRows(_from, now, _elapsed, ShowLoss);
            _text = JoinRows(_rows);
            _hostText = FormatHost(now);
            if (_hostText.Length > 0) _rows.Add(new Row("HOST", _hostText));
            _layoutDirty = true;
            _from = now;
            _elapsed = 0f;
        }

        if (!shown || _text.Length == 0)
        {
            DrawList.Clear();
            return;
        }

        // Metrics change once a second. Keep the measured/wrapped rows and draw commands between samples;
        // the render loop only rebuilds them after data, viewport, font or FPS placement changes.
        if (!_layoutDirty && _drawnScreen == screen && _drawnBelowFps == belowFps && DrawList.Items.Count > 0)
            return;
        DrawList.Clear();
        _drawnScreen = screen;
        _drawnBelowFps = belowFps;
        _layoutDirty = false;

        float h = Mathf.Clamp(Mathf.Round(8f * screen.Y / MagmaRenderer.PageHeight), 11f, 22f);
        float fpsHeight = Mathf.Round(HudK.FpsTextHeight * screen.Y / MagmaRenderer.PageHeight);
        float y = belowFps ? 8f + fpsHeight + 4f : 8f;
        float padding = Mathf.Round(h * 0.55f), pitch = h + Mathf.Round(h * 0.25f);
        float width = Math.Min(screen.X - 16f, h * 28f);
        float x = screen.X - width - 8f;
        float labelWidth = Measure("TRAFFIC", h) + padding;
        float valuesWidth = Math.Max(h * 5f, width - 2f * padding - labelWidth);
        var lines = new List<(Row Row, string Text, bool First)>();
        foreach (Row row in _rows)
        {
            bool first = true;
            foreach (string line in Wrap(row.Value, valuesWidth, h))
            {
                lines.Add((row, line, first));
                first = false;
            }
        }

        float headerHeight = h - 2f;
        float height = padding * 2f + headerHeight + padding + lines.Count * pitch;
        if (_white is { } white)
        {
            DrawList.Items.Add(white.Quad("Net/Panel", new Vector2(x, y), new Vector2(width, height), new Color(0.025f, 0.04f, 0.055f, 0.8f)));
            DrawList.Items.Add(white.Quad("Net/Accent", new Vector2(x, y), new Vector2(2f, height), new Color(0.5f, 0.75f, 0.85f, 0.8f)));
        }
        Color labelColor = new(0.58f, 0.72f, 0.8f, 1f);
        DrawList.Items.Add(HudText.Item("Net/Header", "NETWORK", new Rect2(x + padding, y + padding, width - padding * 2f, headerHeight),
            headerHeight, MagmaAlign.Left, labelColor));
        DrawList.Items.Add(HudText.Item("Net/Window", "1s sample", new Rect2(x + padding, y + padding, width - padding * 2f, headerHeight),
            headerHeight, MagmaAlign.Right, labelColor));
        float lineY = y + padding + headerHeight + padding;
        int index = 0;
        foreach ((Row row, string text, bool first) in lines)
        {
            if (first)
                DrawList.Items.Add(HudText.Item("Net/" + row.Label, row.Label, new Rect2(x + padding, lineY, labelWidth, h), h, MagmaAlign.Left, labelColor));
            MagmaDrawItem value = HudText.Item("Net/Value" + index++, text,
                new Rect2(x + padding + labelWidth, lineY, valuesWidth, h), h, MagmaAlign.Left,
                row.Alert ? new Color(1f, 0.78f, 0.4f, 1f) : new Color(0.94f, 0.97f, 1f, 1f));
            // An exceptional single token (a very large counter) still stays inside the panel.
            value.XScale = Math.Min(1f, valuesWidth / Math.Max(1f, Measure(text, h)));
            value.TrackingPx *= value.XScale;
            DrawList.Items.Add(value);
            lineY += pitch;
        }
    }

    private float Measure(string text, float height) => _font?.Measure(text, height, trackingPx: HudText.Tracking * height) ?? text.Length * height * 0.7f;

    private IEnumerable<string> Wrap(string text, float width, float height)
    {
        string line = "";
        foreach (string metric in text.Split("  ", StringSplitOptions.RemoveEmptyEntries))
        {
            string next = line.Length == 0 ? metric : line + "  " + metric;
            if (line.Length > 0 && Measure(next, height) > width)
            {
                yield return line;
                line = metric;
            }
            else line = next;
        }
        if (line.Length > 0) yield return line;
    }

    /// <summary>The listen host's line: its connected players' best and worst ping (empty for a client or a host alone).</summary>
    public static string FormatHost(NetReadout now) =>
        now is { IsHost: true, Clients: > 0 }
            ? string.Create(CultureInfo.InvariantCulture,
                $"{now.Clients} clients  best {now.BestClientPingMs} ms  worst {now.WorstClientPingMs} ms")
            : string.Empty;

    /// <summary>Grouped readout lines from two totals <paramref name="seconds"/> apart.</summary>
    public static string Format(NetReadout from, NetReadout to, float seconds, bool showLoss = true) => JoinRows(FormatRows(from, to, seconds, showLoss));

    private static string JoinRows(List<Row> rows) => string.Join('\n', rows.ConvertAll(row => row.Label + "  " + row.Value));

    private static List<Row> FormatRows(NetReadout from, NetReadout to, float seconds, bool showLoss)
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
        List<Row> rows =
        [
            new("LINK", $"ping {ping} ms  jitter {jitter} ms  {to.ServerNetRate} Hz"),
            new("TRAFFIC", string.Create(CultureInfo.InvariantCulture, $"down {down:0.0} KB/s  up {up:0.0} KB/s")),
        ];
        if (showLoss)
            rows.Add(new Row("LOSS", string.Create(CultureInfo.InvariantCulture, $"in {lossIn:0.0}%  out {lossOut:0.0}%"), inLost > 0 || outLost > 0));
        if (to.BufferMs > 0)
            rows.Add(new Row("BUFFER", string.Create(CultureInfo.InvariantCulture,
                $"{to.BufferMs:0} ms  age {to.SnapshotAgeMs:0} ms  underruns {to.BufferUnderflows - from.BufferUnderflows}"), to.BufferUnderflows > from.BufferUnderflows));
        rows.Add(new Row("SYNC", string.Create(CultureInfo.InvariantCulture, $"corr {corrections:0.0}/s  adjust {to.CorrectionDistance:0.0} uu")));
        if (to.BufferMs > 0)
        {
            rows.Add(new Row("REPLAY", string.Create(CultureInfo.InvariantCulture, $"{to.MovesReplayed - from.MovesReplayed} moves  anim gaps {to.AnimationGaps - from.AnimationGaps}  state gaps {to.StateGaps - from.StateGaps}"),
                to.AnimationGaps > from.AnimationGaps || to.StateGaps > from.StateGaps));
            rows.Add(new Row("RESETS", string.Create(CultureInfo.InvariantCulture, $"pose {to.PoseResets - from.PoseResets}  owner {to.PredictionResets - from.PredictionResets}"),
                to.PoseResets > from.PoseResets || to.PredictionResets > from.PredictionResets));
        }
        return rows;
    }
}
