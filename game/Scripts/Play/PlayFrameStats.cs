using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenScctGame.Play;

/// <summary>Per-frame costs with bounded storage in ordinary play. Full histories are opt-in diagnostics.</summary>
public sealed class PlayFrameStats(bool captureHistory = false)
{
    private readonly List<double>? _sim = captureHistory ? [] : null;
    private readonly List<double>? _view = captureHistory ? [] : null;
    private double _simSum;
    private double _viewSum;
    private double _frameSum;
    private double _simMax;
    private double _viewMax;

    public long Frames { get; private set; }
    public long Ticks { get; internal set; }
    public double PilotMs { get; internal set; }
    public double LastSimMs { get; private set; }
    public double LastViewMs { get; private set; }

    /// <summary>Retained frame pairs (zero in ordinary play); the frame mean needs only its running sum.</summary>
    public int HistoryFrames => _sim?.Count ?? 0;

    internal void Add(double simMs, double viewMs, double frameMs)
    {
        LastSimMs = simMs;
        LastViewMs = viewMs;
        Frames++;
        _simSum += simMs;
        _viewSum += viewMs;
        _frameSum += frameMs;
        _simMax = Math.Max(_simMax, simMs);
        _viewMax = Math.Max(_viewMax, viewMs);
        _sim?.Add(simMs);
        _view?.Add(viewMs);
    }

    /// <summary>Full-session means/maxima and, when explicitly captured, exact full-session 95th percentiles.</summary>
    public string Summary() => string.Create(CultureInfo.InvariantCulture,
        $"frames={Frames} ticks={Ticks} pilot ms={PilotMs:F0} sim ms/frame mean={Mean(_simSum):F2} p95={Percentile(_sim)} max={_simMax:F2} view ms/frame mean={Mean(_viewSum):F2} p95={Percentile(_view)} max={_viewMax:F2} frame ms mean={Mean(_frameSum):F2}");

    private double Mean(double sum) => Frames > 0 ? sum / Frames : 0;

    private static string Percentile(List<double>? values)
    {
        if (values is null) return "not-captured";
        if (values.Count == 0) return "0.00";
        var sorted = new List<double>(values);
        sorted.Sort();
        return sorted[Math.Min(sorted.Count - 1, (int)(0.95 * sorted.Count))].ToString("F2", CultureInfo.InvariantCulture);
    }
}
