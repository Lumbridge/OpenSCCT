using System;
using System.Globalization;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Options;

namespace OpenScctGame.Hud;

/// <summary>
/// Frame-rate, frame-time and recent frame pacing telemetry, sampled even when its readout is hidden.
/// </summary>
public sealed class HudFps
{
    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 19: 5 s, at most 2048 frames")]
    public const int MaxFrames = 2048;

    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 19")]
    public const float Window = 5f;

    private const float RefreshSeconds = 0.25f;
    private const float GraphSampleSeconds = 0.05f;
    private const int GraphSamples = 602;
    private const int GraphColumns = 120;
    private readonly float[] _times = new float[MaxFrames];
    private readonly float[] _sorted = new float[MaxFrames];
    private readonly double[] _graphTimes = new double[GraphSamples];
    private readonly float[] _graphDurations = new float[GraphSamples];
    private readonly float[] _graphPeaks = new float[GraphSamples];
    private readonly float[] _graphValues = new float[GraphColumns];
    private int _start;
    private int _count;
    private float _sum;
    private int _graphStart;
    private int _graphCount;
    private double _clock;
    private float _bucketElapsed;
    private float _bucketPeak;
    private float _refreshElapsed;
    private float _issueRemaining;
    private bool _dirty = true;
    private bool _drawnIssue;
    private Vector2 _drawnScreen;
    private LayoutKey _drawnSettings;
    private MagmaFont? _font;
    private WhiteRegion? _white;

    private readonly record struct LayoutKey(string Display, float X, float Y, float Scale, float Opacity,
        string Graph, float GraphSeconds, float Threshold, bool FrameTime, bool Percentiles, bool Average, bool Peak);

    public MagmaDrawList DrawList { get; } = new();
    public FpsTelemetrySettings Settings { get; set; } = new();
    public Rect2 Bounds { get; private set; }

    /// <summary>The last readout: frames a second now, of the slowest 1 % and of the fastest 1 %.</summary>
    public (float Now, float Low, float High) Rates { get; private set; }
    public float AverageFps { get; private set; }
    public float PeakFrameTimeMs { get; private set; }
    public float FrameTimeMs => _last * 1000f;
    public bool HasIssue => FrameTimeMs > Settings.FrameTimeThresholdMs || _issueRemaining > 0f;

    public void Configure(HudAssets assets)
    {
        _font = assets.Font(HudAssets.DefaultFont);
        _white = WhiteRegion.Find(assets);
        _dirty = true;
    }

    public void Add(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return;
        }

        if (_count == MaxFrames)
        {
            _sum -= _times[_start];
            _start = (_start + 1) % MaxFrames;
            _count--;
        }

        _times[(_start + _count) % MaxFrames] = dt;
        _count++;
        _sum += dt;
        while (_count > 1 && _sum - _times[_start] >= Window)
        {
            _sum -= _times[_start];
            _start = (_start + 1) % MaxFrames;
            _count--;
        }

        _last = dt;
        _issueRemaining = Math.Max(0f, _issueRemaining - dt);
        if (FrameTimeMs > Settings.FrameTimeThresholdMs)
            _issueRemaining = Math.Max(0f, Settings.IssueHoldSeconds);

        // Keep peaks, not averages, so short hitches survive decimation to the graph's pixel width.
        _clock += dt;
        _bucketElapsed += dt;
        _bucketPeak = Math.Max(_bucketPeak, FrameTimeMs);
        if (_bucketElapsed >= GraphSampleSeconds)
        {
            if (_graphCount == GraphSamples)
            {
                _graphStart = (_graphStart + 1) % GraphSamples;
                _graphCount--;
            }
            int index = (_graphStart + _graphCount++) % GraphSamples;
            _graphTimes[index] = _clock;
            _graphDurations[index] = _bucketElapsed;
            _graphPeaks[index] = _bucketPeak;
            _bucketElapsed = 0f;
            _bucketPeak = 0f;
        }
        _refreshElapsed += dt;
        if (_refreshElapsed >= RefreshSeconds)
        {
            _refreshElapsed = 0f;
            _dirty = true;
        }
    }

    private float _last;

    /// <summary>Works out <see cref="Rates"/> from the frames kept.</summary>
    public void Measure()
    {
        if (_count == 0)
        {
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            _sorted[i] = _times[(_start + i) % MaxFrames];
        }

        Array.Sort(_sorted, 0, _count);
        int onePercent = Math.Max(1, _count / 100);
        float slow = 0f, fast = 0f;
        for (int i = 0; i < onePercent; i++)
        {
            fast += _sorted[i];
            slow += _sorted[_count - 1 - i];
        }

        Rates = (1f / _last, onePercent / slow, onePercent / fast);
        AverageFps = _count / _sum;
        PeakFrameTimeMs = _sorted[_count - 1] * 1000f;
    }

    public void Draw(Vector2 screen, bool shown)
    {
        bool issue = HasIssue;
        if (!shown || _count == 0 || Settings.Opacity <= 0f || (Settings.DisplayMode == "Issues" && !issue))
        {
            DrawList.Clear();
            Bounds = default;
            return;
        }

        var key = new LayoutKey(Settings.DisplayMode, Settings.PositionX, Settings.PositionY, Settings.Scale,
            Settings.Opacity, Settings.Graph, Settings.GraphSeconds, Settings.FrameTimeThresholdMs,
            Settings.ShowFrameTime, Settings.ShowPercentiles, Settings.ShowAverage, Settings.ShowPeakFrameTime);
        if (!_dirty && _drawnScreen == screen && _drawnSettings == key && _drawnIssue == issue && DrawList.Items.Count > 0)
            return;
        _drawnScreen = screen;
        _drawnSettings = key;
        _drawnIssue = issue;
        _dirty = false;
        Measure();
        DrawList.Clear();

        float h = Mathf.Clamp(Mathf.Round(HudK.FpsTextHeight * screen.Y / MagmaRenderer.PageHeight), 11f, 28f);
        float padding = Mathf.Round(h * 0.4f);
        float pitch = h + Mathf.Round(h * 0.2f);
        string main = string.Create(CultureInfo.InvariantCulture, $"{Rates.Now:0} fps");
        if (Settings.ShowFrameTime) main += string.Create(CultureInfo.InvariantCulture, $"  {FrameTimeMs:0.0} ms");
        string percentiles = Settings.ShowPercentiles
            ? string.Create(CultureInfo.InvariantCulture, $"1% low {Rates.Low:0}  1% high {Rates.High:0}") : "";
        string extra = Settings.ShowAverage ? string.Create(CultureInfo.InvariantCulture, $"avg {AverageFps:0} fps") : "";
        if (Settings.ShowPeakFrameTime)
            extra += (extra.Length == 0 ? "" : "  ") + string.Create(CultureInfo.InvariantCulture, $"peak {PeakFrameTimeMs:0.0} ms");
        bool graph = Settings.Graph != "Off" && _white.HasValue;
        float width = Math.Max(MeasureText(main, h), Math.Max(MeasureText(percentiles, h), MeasureText(extra, h))) + padding * 2f;
        if (graph) width = Math.Max(width, h * 23f);
        float y = padding + pitch;
        if (percentiles.Length > 0) y += pitch;
        if (extra.Length > 0) y += pitch;
        float graphHeight = graph ? h * 3.2f : 0f;
        float graphY = y + padding;
        float height = y + padding + (graph ? graphHeight + pitch + padding : 0f);
        Rect2 source = new(Vector2.Zero, new Vector2(width, height));
        Color normal = new(0.94f, 0.97f, 1f, 1f);
        Color warning = new(1f, 0.78f, 0.4f, 1f);
        Color muted = new(0.58f, 0.72f, 0.8f, 1f);

        if (_white is { } background)
            DrawList.Items.Add(background.Quad("Fps/Panel", Vector2.Zero, source.Size, new Color(0.025f, 0.04f, 0.055f, 0.8f)));
        AddText("Fps", main, padding, padding, width - padding * 2f, h, issue ? warning : normal);
        y = padding + pitch;
        if (percentiles.Length > 0)
        {
            AddText("Fps/Percentiles", percentiles, padding, y, width - padding * 2f, h, muted);
            y += pitch;
        }
        if (extra.Length > 0) AddText("Fps/Extra", extra, padding, y, width - padding * 2f, h, normal);

        if (graph && _white is { } white)
        {
            float seconds = Math.Clamp(Settings.GraphSeconds, 2f, 30f);
            float maximum = FillGraph(seconds);
            // A minimum scale keeps small frame-time changes honest; larger hitches expand the scale.
            maximum = Math.Max(Math.Max(16.7f, Settings.FrameTimeThresholdMs), maximum);
            Rect2 graphRect = new(padding, graphY, width - padding * 2f, graphHeight);
            DrawList.Items.Add(white.Quad("Fps/Graph/Background", graphRect.Position, graphRect.Size, new Color(0f, 0f, 0f, 0.3f)));
            float thresholdY = graphRect.End.Y - graphRect.Size.Y * Math.Clamp(Settings.FrameTimeThresholdMs / maximum, 0f, 1f);
            DrawList.Items.Add(white.Quad("Fps/Graph/Threshold", new Vector2(graphRect.Position.X, thresholdY),
                new Vector2(graphRect.Size.X, 1f), new Color(1f, 0.78f, 0.4f, 0.5f)));
            HudTelemetryVisual.Graph(DrawList, white, "Fps/Graph", graphRect, _graphValues, maximum, Settings.Graph, new Color(0.5f, 0.8f, 0.95f, 1f));
            string caption = string.Create(CultureInfo.InvariantCulture, $"FRAME TIME  {seconds:0}s  0-{maximum:0.0} ms");
            AddText("Fps/Graph/Label", caption, padding, graphRect.End.Y + padding, graphRect.Size.X, h, muted);
        }

        Bounds = HudTelemetryVisual.Place(screen, source.Size, Settings, defaultTop: 6f);
        HudTelemetryVisual.Transform(DrawList, source, Bounds, Settings.Opacity);
    }

    private void AddText(string path, string text, float x, float y, float width, float height, Color color)
    {
        MagmaDrawItem item = HudText.Item(path, text, new Rect2(x, y, width, height), height, MagmaAlign.Left, color);
        item.XScale = Math.Min(1f, width / Math.Max(1f, MeasureText(text, height)));
        item.TrackingPx *= item.XScale;
        DrawList.Items.Add(item);
    }

    private float MeasureText(string text, float height) =>
        _font?.Measure(text, height, trackingPx: HudText.Tracking * height) ?? text.Length * height * 0.7f;

    private float FillGraph(float seconds)
    {
        Array.Clear(_graphValues);
        double begin = _clock - seconds;
        float maximum = 0f;
        for (int i = 0; i < _graphCount; i++)
        {
            int index = (_graphStart + i) % GraphSamples;
            if (_graphTimes[index] < begin) continue;
            int first = Math.Clamp((int)((_graphTimes[index] - _graphDurations[index] - begin) / seconds * GraphColumns), 0, GraphColumns - 1);
            int last = Math.Clamp((int)((_graphTimes[index] - begin) / seconds * GraphColumns), 0, GraphColumns - 1);
            for (int column = first; column <= last; column++)
                _graphValues[column] = Math.Max(_graphValues[column], _graphPeaks[index]);
            maximum = Math.Max(maximum, _graphPeaks[index]);
        }
        _graphValues[GraphColumns - 1] = Math.Max(_graphValues[GraphColumns - 1], _bucketPeak);
        return Math.Max(maximum, _bucketPeak);
    }
}
