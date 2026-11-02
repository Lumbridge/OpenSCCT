using System;
using System.Text.Json.Serialization;

namespace OpenScctGame.Options;

/// <summary>Independent placement, appearance and history for a performance readout.</summary>
public abstract class TelemetrySettings
{
    /// <summary>The existing show_fps/show_net switch enables the readout; this controls when it appears.</summary>
    [JsonPropertyName("display_mode")]
    public string DisplayMode { get; set; } = "Always";

    /// <summary>Percent of available horizontal travel: 0 left, 100 right.</summary>
    [JsonPropertyName("position_x")]
    public float PositionX { get; set; } = 100f;

    /// <summary>Percent of available vertical travel: 0 top, 100 bottom.</summary>
    [JsonPropertyName("position_y")]
    public float PositionY { get; set; }

    [JsonPropertyName("scale")]
    public float Scale { get; set; } = 100f;

    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 100f;

    [JsonPropertyName("graph")]
    public string Graph { get; set; } = "Off";

    [JsonPropertyName("graph_seconds")]
    public float GraphSeconds { get; set; } = 5f;

    [JsonPropertyName("issue_hold_seconds")]
    public float IssueHoldSeconds { get; set; } = 3f;

    public virtual void Clamp()
    {
        DisplayMode = DisplayMode == "Issues" ? "Issues" : "Always";
        Graph = Graph is "Line" or "Bars" ? Graph : "Off";
        PositionX = Range(PositionX, 0f, 100f, 100f);
        PositionY = Range(PositionY, 0f, 100f, 0f);
        Scale = Range(Scale, 50f, 200f, 100f);
        Opacity = Range(Opacity, 0f, 100f, 100f);
        GraphSeconds = Range(GraphSeconds, 2f, 30f, 5f);
        IssueHoldSeconds = Range(IssueHoldSeconds, 0f, 10f, 3f);
    }

    protected static float Range(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public sealed class FpsTelemetrySettings : TelemetrySettings
{
    [JsonPropertyName("show_frame_time")]
    public bool ShowFrameTime { get; set; } = true;

    [JsonPropertyName("show_percentiles")]
    public bool ShowPercentiles { get; set; } = true;

    [JsonPropertyName("show_average")]
    public bool ShowAverage { get; set; }

    [JsonPropertyName("show_peak_frame_time")]
    public bool ShowPeakFrameTime { get; set; }

    [JsonPropertyName("frame_time_threshold_ms")]
    public float FrameTimeThresholdMs { get; set; } = 33.3f;

    public FpsTelemetrySettings Clone() => (FpsTelemetrySettings)MemberwiseClone();

    public override void Clamp()
    {
        base.Clamp();
        FrameTimeThresholdMs = Range(FrameTimeThresholdMs, 1f, 1000f, 33.3f);
    }
}

public sealed class NetworkTelemetrySettings : TelemetrySettings
{
    [JsonPropertyName("detailed")]
    public bool Detailed { get; set; } = true;

    [JsonPropertyName("show_traffic")]
    public bool ShowTraffic { get; set; } = true;

    [JsonPropertyName("show_ping_graph")]
    public bool ShowPingGraph { get; set; } = true;

    [JsonPropertyName("show_jitter_graph")]
    public bool ShowJitterGraph { get; set; } = true;

    [JsonPropertyName("show_loss_graph")]
    public bool ShowLossGraph { get; set; } = true;

    [JsonPropertyName("ping_threshold_ms")]
    public float PingThresholdMs { get; set; } = 150f;

    [JsonPropertyName("jitter_threshold_ms")]
    public float JitterThresholdMs { get; set; } = 30f;

    [JsonPropertyName("loss_threshold_percent")]
    public float LossThresholdPercent { get; set; } = 2f;

    public NetworkTelemetrySettings Clone() => (NetworkTelemetrySettings)MemberwiseClone();

    public override void Clamp()
    {
        base.Clamp();
        PingThresholdMs = Range(PingThresholdMs, 1f, 1000f, 150f);
        JitterThresholdMs = Range(JitterThresholdMs, 1f, 500f, 30f);
        LossThresholdPercent = Range(LossThresholdPercent, 0.1f, 100f, 2f);
    }
}
