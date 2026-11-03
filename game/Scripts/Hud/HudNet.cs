using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Options;

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
    private float _drawnTop;
    private RenderOptions _drawnOptions;
    private HudTelemetryVisual.EditorKey _drawnEditor;
    private bool _hasLayout;
    private NetReadout _sampleFrom, _sampleTo;
    private float _sampleSeconds;
    private float _lossIn, _lossOut;
    private float _issueRemaining;
    private IssueKind _intervalIssues, _activeIssues, _shownIssues;
    [Flags]
    private enum IssueKind { None = 0, Ping = 1, Jitter = 2, Loss = 4, Stale = 8, Underrun = 16, Prediction = 32, State = 64 }
    private double _clock;
    private float _historyElapsed;
    private const int HistoryCapacity = 301;
    private readonly HistorySample[] _history = new HistorySample[HistoryCapacity];
    private readonly float[] _graphValues = new float[HistoryCapacity];
    private int _historyStart, _historyCount;
    private readonly record struct HistorySample(double Time, float Ping, float Jitter, float Loss);
    private readonly record struct RenderOptions(string Mode, float X, float Y, float Scale, float Opacity,
        string Graph, float Seconds, float Hold, bool Detailed, bool Traffic, bool Loss,
        bool PingGraph, bool JitterGraph, bool LossGraph, float PingThreshold, float JitterThreshold, float LossThreshold);

    public MagmaDrawList DrawList { get; } = new();

    public NetworkTelemetrySettings Settings { get; set; } = new();

    /// <summary>The visible panel's actual screen bounds, empty when hidden.</summary>
    public Rect2 Bounds { get; private set; }
    public Rect2 EditorBaseBounds { get; private set; }
    public Rect2 EditorBounds { get; private set; }

    /// <summary>Allows the default upper-right layout to stack below the actual FPS panel, including its graph.</summary>
    public Rect2? FpsBounds { get; set; }

    public bool HasIssue { get; private set; }

    /// <summary>The local player's link totals, or null when there is no connection.</summary>
    public Func<NetReadout?>? Source { get; set; }

    /// <summary>The last readout, with one line per group. Link warnings can appear before the first rate sample.</summary>
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

    public void Draw(Vector2 screen, float dt, bool shown, bool belowFps, HudElementSettings? layout = null, bool editorPreview = false)
    {
        if (Source?.Invoke() is not { } now)
        {
            Reset();
            return;
        }

        // A new connection or controller restarts the counts.
        if (!_hasFrom || CountersReset(_from, now))
        {
            Reset();
            _from = now;
            _hasFrom = true;
        }

        dt = float.IsFinite(dt) ? Math.Max(0f, dt) : 0f;
        RenderOptions options = Options();
        var editor = HudTelemetryVisual.EditorKey.Of(layout, editorPreview);
        bool optionsChanged = options != _drawnOptions;
        if (optionsChanged) _layoutDirty = true;
        bool refreshRows = optionsChanged && _sampleSeconds > 0f;
        _clock += dt;
        _elapsed += dt;
        _issueRemaining = Math.Max(0f, _issueRemaining - dt);
        if (_elapsed >= Window)
        {
            _sampleFrom = _from;
            _sampleTo = now;
            _sampleSeconds = _elapsed;
            (_lossIn, _lossOut) = Loss(_from, now);
            _intervalIssues = IntervalIssues(_from, now);
            refreshRows = true;
            _from = now;
            _elapsed = 0f;
        }
        else if (optionsChanged && _sampleSeconds > 0f)
        {
            _intervalIssues = IntervalIssues(_sampleFrom, _sampleTo);
        }

        IssueKind activeIssues = _intervalIssues | LinkIssues(now) | CounterIssues(_from, now);
        HasIssue = activeIssues != IssueKind.None;
        if (HasIssue) _issueRemaining = Settings.IssueHoldSeconds;
        IssueKind shownIssues = HasIssue ? activeIssues : _issueRemaining > 0f ? _shownIssues : IssueKind.None;
        refreshRows |= activeIssues != _activeIssues || shownIssues != _shownIssues;
        _activeIssues = activeIssues;
        _shownIssues = shownIssues;
        if (refreshRows || (editorPreview && (_rows.Count == 0 || optionsChanged))) RefreshRows(now);
        _historyElapsed += dt;
        if (_historyCount == 0 || _historyElapsed >= 0.1f)
        {
            AddHistory(now);
            _historyElapsed %= 0.1f;
            if (Settings.Graph != "Off" && (Settings.ShowPingGraph || Settings.ShowJitterGraph || Settings.ShowLossGraph))
                _layoutDirty = true;
        }

        if (!editorPreview && (!shown || Settings.Opacity <= 0f || _text.Length == 0 ||
            (Settings.DisplayMode == "Issues" && !HasIssue && _issueRemaining <= 0f)))
        {
            DrawList.Clear();
            Bounds = EditorBaseBounds = EditorBounds = default;
            _hasLayout = false;
            _drawnOptions = options;
            return;
        }

        // Metrics change once a second. Keep the measured/wrapped rows and draw commands between samples;
        // the render loop only rebuilds them after data, viewport, font or FPS placement changes.
        // Once authored, this element is independent of another panel's visibility, graph or current height.
        float defaultTop = layout is not null || editorPreview
            ? HudTelemetryVisual.NetworkEditorTop(screen)
            : DefaultTop(screen, belowFps);
        if (!_layoutDirty && _drawnScreen == screen && _drawnTop == defaultTop && _drawnEditor == editor && _hasLayout)
            return;
        DrawList.Clear();
        _drawnScreen = screen;
        _drawnTop = defaultTop;
        _drawnOptions = options;
        _drawnEditor = editor;
        _hasLayout = true;
        _layoutDirty = false;

        float h = Mathf.Clamp(Mathf.Round(8f * screen.Y / MagmaRenderer.PageHeight), 11f, 22f);
        float y = 0f;
        float padding = Mathf.Round(h * 0.55f), pitch = h + Mathf.Round(h * 0.25f);
        float width = Math.Max(100f, Math.Min(screen.X - 16f, h * 28f));
        float x = 0f;
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
        int graphs = Settings.Graph == "Off" ? 0 :
            (Settings.ShowPingGraph ? 1 : 0) + (Settings.ShowJitterGraph ? 1 : 0) + (Settings.ShowLossGraph ? 1 : 0);
        float graphHeight = h * 3f;
        height += graphs * (graphHeight + pitch + padding);
        if (_white is { } white)
        {
            DrawList.Items.Add(white.Quad("Net/Panel", new Vector2(x, y), new Vector2(width, height), new Color(0.025f, 0.04f, 0.055f, 0.8f)));
            DrawList.Items.Add(white.Quad("Net/Accent", new Vector2(x, y), new Vector2(2f, height), new Color(0.5f, 0.75f, 0.85f, 0.8f)));
        }
        Color labelColor = new(0.58f, 0.72f, 0.8f, 1f);
        DrawList.Items.Add(HudText.Item("Net/Header", "NETWORK", new Rect2(x + padding, y + padding, width - padding * 2f, headerHeight),
            headerHeight, MagmaAlign.Left, labelColor));
        DrawList.Items.Add(HudText.Item("Net/Window", _sampleSeconds > 0f ? "1s sample" : "live link", new Rect2(x + padding, y + padding, width - padding * 2f, headerHeight),
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
        if (graphs > 0 && _white is { } graphWhite)
        {
            if (Settings.ShowPingGraph)
                DrawGraph(graphWhite, "Ping", "PING ms", 0, Math.Max(50f, Settings.PingThresholdMs),
                    new Color(0.35f, 0.85f, 1f), ref lineY);
            if (Settings.ShowJitterGraph)
                DrawGraph(graphWhite, "Jitter", "JITTER ms", 1, Math.Max(10f, Settings.JitterThresholdMs),
                    new Color(0.95f, 0.75f, 0.3f), ref lineY);
            if (Settings.ShowLossGraph)
                DrawGraph(graphWhite, "Loss", "LOSS % (worst direction)", 2, Math.Max(5f, Settings.LossThresholdPercent),
                    new Color(1f, 0.45f, 0.4f), ref lineY);
        }
        // Both percentage placement and editor edge clamping use an authored footprint. Changing issue/host rows
        // or wrapping numeric values therefore cannot move the top-left origin, even at middle/bottom positions.
        float referenceHeight = layout is not null || editorPreview
            ? padding * 2f + headerHeight + padding + ReferenceLines(valuesWidth, h) * pitch + graphs * (graphHeight + pitch + padding)
            : height;
        Vector2 referenceSize = new(width, referenceHeight);
        EditorBaseBounds = HudTelemetryVisual.Place(screen, referenceSize, Settings, defaultTop);
        EditorBounds = HudTelemetryVisual.PlaceEdited(DrawList, screen, new Rect2(0f, 0f, width, height),
            EditorBaseBounds, Settings.Opacity, layout, referenceSize);
        Bounds = layout?.Visible == false ? default : new Rect2(EditorBounds.Position, new Vector2(width, height) * EditorBounds.Size / referenceSize);

        void DrawGraph(WhiteRegion white, string name, string title, int metric, float minimumMax, Color color, ref float top)
        {
            int count = CopyHistory(metric, out float peak, out float startAge, out float endAge);
            float maximum = Math.Max(minimumMax, peak);
            string legend = string.Create(CultureInfo.InvariantCulture, $"{title}  0-{maximum:0.#}  / {Settings.GraphSeconds:0.#}s");
            MagmaDrawItem label = HudText.Item("Net/GraphLabel/" + name, legend,
                new Rect2(padding, top, width - 2f * padding, h), h, MagmaAlign.Left, color);
            label.XScale = Math.Min(1f, label.Rect.Size.X / Math.Max(1f, Measure(legend, h)));
            label.TrackingPx *= label.XScale;
            DrawList.Items.Add(label);
            top += pitch;
            Rect2 graph = new(padding, top, width - 2f * padding, graphHeight);
            DrawList.Items.Add(white.Quad("Net/GraphBackground/" + name, graph.Position, graph.Size, new Color(0f, 0f, 0f, 0.25f)));
            // A partial startup history occupies only its actual portion of the selected time window.
            float seconds = Math.Max(0.1f, Settings.GraphSeconds);
            Rect2 plot = new(graph.Position + new Vector2(graph.Size.X * (1f - startAge / seconds), 0f),
                new Vector2(graph.Size.X * Math.Max(0f, startAge - endAge) / seconds, graph.Size.Y));
            HudTelemetryVisual.Graph(DrawList, white, "Net/Graph/" + name, plot,
                _graphValues.AsSpan(0, count), maximum, Settings.Graph, color);
            top += graphHeight + padding;
        }
    }

    private float DefaultTop(Vector2 screen, bool belowFps)
    {
        if (FpsBounds is { } fps)
            return fps.Size.X > 0f && fps.Size.Y > 0f && fps.Position.Y <= 8f && fps.End.X >= screen.X - 9f
                ? fps.End.Y + 4f : 8f;
        return belowFps ? 12f + Mathf.Round(HudK.FpsTextHeight * screen.Y / MagmaRenderer.PageHeight) : 8f;
    }

    private int ReferenceLines(float width, float height)
    {
        // Geometry templates only: reserve expected wide values and all optional runtime rows, never sample data.
        int lines = Count("ping 999 ms  jitter 999 ms  999 Hz") +
            Count("recent: high ping  high jitter  packet loss  stale snapshots  buffer underrun  prediction reset  state gap") +
            Count("999 clients  best 999 ms  worst 999 ms");
        if (Settings.ShowTraffic) lines += Count("down 9999.9 KB/s  up 9999.9 KB/s");
        if (ShowLoss) lines += Count("in 100.0%  out 100.0%");
        if (Settings.Detailed)
            lines += Count("999 ms  age 999 ms  underruns 9999") + Count("corr 9999.9/s  adjust 9999.9 uu") +
                Count("9999 moves  anim gaps 9999  state gaps 9999") + Count("pose 9999  owner 9999");
        return lines;

        int Count(string value)
        {
            int count = 0;
            foreach (string _ in Wrap(value, width, height)) count++;
            return count;
        }
    }

    private RenderOptions Options() => new(Settings.DisplayMode, Settings.PositionX, Settings.PositionY, Settings.Scale,
        Settings.Opacity, Settings.Graph, Settings.GraphSeconds, Settings.IssueHoldSeconds, Settings.Detailed,
        Settings.ShowTraffic, ShowLoss, Settings.ShowPingGraph, Settings.ShowJitterGraph, Settings.ShowLossGraph,
        Settings.PingThresholdMs, Settings.JitterThresholdMs, Settings.LossThresholdPercent);

    private void Reset()
    {
        DrawList.Clear();
        Bounds = EditorBaseBounds = EditorBounds = default;
        _hasLayout = false;
        _hasFrom = false;
        _elapsed = _sampleSeconds = _issueRemaining = _historyElapsed = _lossIn = _lossOut = 0f;
        _clock = 0;
        _historyStart = _historyCount = 0;
        _intervalIssues = _activeIssues = _shownIssues = IssueKind.None;
        HasIssue = false;
        _text = _hostText = string.Empty;
        _rows.Clear();
        _layoutDirty = true;
    }

    private static bool CountersReset(NetReadout from, NetReadout to) =>
        to.InPackets < from.InPackets || to.OutPackets < from.OutPackets || to.InBytes < from.InBytes ||
        to.OutBytes < from.OutBytes || to.InPacketsLost < from.InPacketsLost || to.OutPacketsLost < from.OutPacketsLost ||
        to.Corrections < from.Corrections || to.BufferUnderflows < from.BufferUnderflows ||
        to.PredictionResets < from.PredictionResets || to.StateGaps < from.StateGaps;

    private IssueKind LinkIssues(NetReadout now)
    {
        IssueKind issues = IssueKind.None;
        if ((now.HasRtt && now.RttMicros / 1000f > Settings.PingThresholdMs) ||
            (now.IsHost && now.Clients > 0 && now.WorstClientPingMs > Settings.PingThresholdMs)) issues |= IssueKind.Ping;
        if (now.HasRtt && now.JitterMicros / 1000f > Settings.JitterThresholdMs) issues |= IssueKind.Jitter;
        if (now.BufferMs > 0f && now.SnapshotAgeMs > Math.Max(250f, now.BufferMs * 2f)) issues |= IssueKind.Stale;
        return issues;
    }

    private IssueKind IntervalIssues(NetReadout from, NetReadout to)
    {
        (float lossIn, float lossOut) = Loss(from, to);
        return CounterIssues(from, to) | (Math.Max(lossIn, lossOut) > Settings.LossThresholdPercent ? IssueKind.Loss : IssueKind.None);
    }

    private static IssueKind CounterIssues(NetReadout from, NetReadout to) =>
        (to.BufferUnderflows > from.BufferUnderflows ? IssueKind.Underrun : IssueKind.None) |
        (to.PredictionResets > from.PredictionResets ? IssueKind.Prediction : IssueKind.None) |
        (to.StateGaps > from.StateGaps ? IssueKind.State : IssueKind.None);

    private void RefreshRows(NetReadout now)
    {
        _rows = _sampleSeconds > 0f ? FormatRows(_sampleFrom, _sampleTo, _sampleSeconds, ShowLoss) : [LinkRow(now)];
        if (!Settings.ShowTraffic) _rows.RemoveAll(row => row.Label == "TRAFFIC");
        if (!Settings.Detailed) _rows.RemoveAll(row => row.Label is "BUFFER" or "SYNC" or "REPLAY" or "RESETS");
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i].Label == "LINK") _rows[i] = LinkRow(now);
        if (_shownIssues != IssueKind.None)
        {
            List<string> reasons = [];
            if ((_shownIssues & IssueKind.Ping) != 0) reasons.Add("high ping");
            if ((_shownIssues & IssueKind.Jitter) != 0) reasons.Add("high jitter");
            if ((_shownIssues & IssueKind.Loss) != 0) reasons.Add("packet loss");
            if ((_shownIssues & IssueKind.Stale) != 0) reasons.Add("stale snapshots");
            if ((_shownIssues & IssueKind.Underrun) != 0) reasons.Add("buffer underrun");
            if ((_shownIssues & IssueKind.Prediction) != 0) reasons.Add("prediction reset");
            if ((_shownIssues & IssueKind.State) != 0) reasons.Add("state gap");
            _rows.Insert(1, new Row("ISSUE", (HasIssue ? "" : "recent: ") + string.Join("  ", reasons), true));
        }
        _text = JoinRows(_rows);
        _hostText = FormatHost(now);
        if (_hostText.Length > 0) _rows.Add(new Row("HOST", _hostText, now.WorstClientPingMs > Settings.PingThresholdMs));
        _layoutDirty = true;
    }

    private Row LinkRow(NetReadout now)
    {
        string ping = now.HasRtt ? (now.RttMicros / 1000).ToString(CultureInfo.InvariantCulture) : "-";
        string jitter = now.HasRtt ? (now.JitterMicros / 1000).ToString(CultureInfo.InvariantCulture) : "-";
        return new Row("LINK", $"ping {ping} ms  jitter {jitter} ms  {now.ServerNetRate} Hz", LinkIssues(now) != IssueKind.None);
    }

    private static (float In, float Out) Loss(NetReadout from, NetReadout to)
    {
        long received = Math.Max(0, to.InPackets - from.InPackets), inLost = Math.Max(0, to.InPacketsLost - from.InPacketsLost);
        long sent = Math.Max(0, to.OutPackets - from.OutPackets), outLost = Math.Max(0, to.OutPacketsLost - from.OutPacketsLost);
        return (received + inLost > 0 ? Math.Clamp(100f * inLost / (received + inLost), 0f, 100f) : 0f,
            sent > 0 ? Math.Clamp(100f * outLost / sent, 0f, 100f) : 0f);
    }

    private void AddHistory(NetReadout now)
    {
        if (_historyCount == HistoryCapacity)
        {
            _historyStart = (_historyStart + 1) % HistoryCapacity;
            _historyCount--;
        }
        _history[(_historyStart + _historyCount++) % HistoryCapacity] = new HistorySample(_clock,
            now.HasRtt ? Math.Max(0f, now.RttMicros / 1000f) : (now.IsHost ? now.WorstClientPingMs : 0f),
            now.HasRtt ? Math.Max(0f, now.JitterMicros / 1000f) : 0f, Math.Max(_lossIn, _lossOut));
        while (_historyCount > 1 && _clock - _history[_historyStart].Time > 30d)
        {
            _historyStart = (_historyStart + 1) % HistoryCapacity;
            _historyCount--;
        }
    }

    private int CopyHistory(int metric, out float peak, out float startAge, out float endAge)
    {
        peak = 0f;
        startAge = endAge = 0f;
        if (_historyCount == 0) return 0;
        double first = Math.Max(_history[_historyStart].Time, _clock - Settings.GraphSeconds);
        double last = _history[(_historyStart + _historyCount - 1) % HistoryCapacity].Time;
        startAge = (float)(_clock - first);
        endAge = (float)(_clock - last);
        int count = Math.Clamp((int)Math.Ceiling((last - first) * 10d) + 1, 2, HistoryCapacity);
        if (last <= first)
        {
            // A point uses a short segment; it never claims a full window of data at startup.
            startAge = Math.Min(Settings.GraphSeconds, endAge + 0.05f);
        }
        int sampleIndex = 0;
        while (sampleIndex + 1 < _historyCount && _history[(_historyStart + sampleIndex + 1) % HistoryCapacity].Time <= first)
            sampleIndex++;
        for (int i = 0; i < count; i++)
        {
            double time = first + (last - first) * i / (count - 1);
            HistorySample sample = _history[(_historyStart + sampleIndex) % HistoryCapacity];
            float value = Value(sample);
            while (sampleIndex + 1 < _historyCount && _history[(_historyStart + sampleIndex + 1) % HistoryCapacity].Time <= time)
            {
                sampleIndex++;
                sample = _history[(_historyStart + sampleIndex) % HistoryCapacity];
                // Keep short spikes when several irregular samples fall into one time bucket.
                value = Math.Max(value, Value(sample));
            }
            _graphValues[i] = value;
            peak = Math.Max(peak, value);
        }
        return count;

        float Value(HistorySample sample) => metric == 0 ? sample.Ping : metric == 1 ? sample.Jitter : sample.Loss;
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
