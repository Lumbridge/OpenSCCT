using System;
using Godot;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    private void TelemetryControls(GridContainer grid)
    {
        FpsTelemetrySettings fps = _edit.FpsTelemetry;
        Section(grid, "FPS & frame timing", "Frame time is the time between rendered frames. Lower is better; a spike can reveal a stutter. Set this readout's position, size and opacity in the HUD editor.");
        TelemetryDisplay(grid, "FPS display", "show_fps", fps, _edit.ShowFps, value => _edit.ShowFps = value);
        Check(grid, "Frame time (ms)", "fps_telemetry.show_frame_time", fps.ShowFrameTime, value => fps.ShowFrameTime = value);
        Check(grid, "1% low and high FPS", "fps_telemetry.show_percentiles", fps.ShowPercentiles, value => fps.ShowPercentiles = value);
        Check(grid, "Average FPS", "fps_telemetry.show_average", fps.ShowAverage, value => fps.ShowAverage = value);
        Check(grid, "Peak frame time (ms)", "fps_telemetry.show_peak_frame_time", fps.ShowPeakFrameTime, value => fps.ShowPeakFrameTime = value);
        Choice(grid, "Frame-time graph", "fps_telemetry.graph", ["Off", "Line", "Bars"], fps.Graph, value => fps.Graph = value);
        TelemetryHistory(grid, "fps_telemetry", fps);
        Slider(grid, "Frame time warning (ms)", "fps_telemetry.frame_time_threshold_ms", 1f, 1000f, 0.1f,
            fps.FrameTimeThresholdMs, value => fps.FrameTimeThresholdMs = value, "0.0", typed: true);
        TelemetryIssueHold(grid, "fps_telemetry", fps);

        NetworkTelemetrySettings net = _edit.NetworkTelemetry;
        Section(grid, "Network information", "Issues include high ping, jitter, packet loss and interrupted updates. A host also sees its players' ping range. Set this readout's position, size and opacity in the HUD editor.");
        TelemetryDisplay(grid, "Network display", "show_net", net, _edit.ShowNet, value => _edit.ShowNet = value);
        Check(grid, "Detailed network information", "network_telemetry.detailed", net.Detailed, value => net.Detailed = value);
        Check(grid, "Download and upload rates", "network_telemetry.show_traffic", net.ShowTraffic, value => net.ShowTraffic = value);
        Check(grid, "Packet loss percentages", "show_net_loss", _edit.ShowNetLoss, value => _edit.ShowNetLoss = value);
        Choice(grid, "Network graphs", "network_telemetry.graph", ["Off", "Line", "Bars"], net.Graph, value => net.Graph = value);
        Check(grid, "Graph ping", "network_telemetry.show_ping_graph", net.ShowPingGraph, value => net.ShowPingGraph = value);
        Check(grid, "Graph jitter", "network_telemetry.show_jitter_graph", net.ShowJitterGraph, value => net.ShowJitterGraph = value);
        Check(grid, "Graph packet loss", "network_telemetry.show_loss_graph", net.ShowLossGraph, value => net.ShowLossGraph = value);
        TelemetryHistory(grid, "network_telemetry", net);
        Slider(grid, "Ping warning (ms)", "network_telemetry.ping_threshold_ms", 1f, 1000f, 1f,
            net.PingThresholdMs, value => net.PingThresholdMs = value, "0", typed: true);
        Slider(grid, "Jitter warning (ms)", "network_telemetry.jitter_threshold_ms", 1f, 500f, 1f,
            net.JitterThresholdMs, value => net.JitterThresholdMs = value, "0", typed: true);
        Slider(grid, "Packet loss warning (%)", "network_telemetry.loss_threshold_percent", 0.1f, 100f, 0.1f,
            net.LossThresholdPercent, value => net.LossThresholdPercent = value, "0.0", typed: true);
        TelemetryIssueHold(grid, "network_telemetry", net);
    }

    private void TelemetryDisplay(GridContainer grid, string label, string key, TelemetrySettings settings, bool enabled, Action<bool> setEnabled)
    {
        Choice(grid, label, key, ["Never", "Always", "Issues"], enabled ? settings.DisplayMode : "Never", value =>
        {
            setEnabled(value != "Never");
            settings.DisplayMode = value == "Issues" ? "Issues" : "Always";
        }, value => value == "Issues" ? "Only during issues" : value);
    }

    private void TelemetryHistory(GridContainer grid, string key, TelemetrySettings settings) =>
        Slider(grid, "Graph history (seconds)", key + ".graph_seconds", 2f, 30f, 1f,
            settings.GraphSeconds, value => settings.GraphSeconds = value, "0");

    private void TelemetryIssueHold(GridContainer grid, string key, TelemetrySettings settings) =>
        Slider(grid, "Keep visible after an issue (seconds)", key + ".issue_hold_seconds", 0f, 10f, 0.5f,
            settings.IssueHoldSeconds, value => settings.IssueHoldSeconds = value, "0.0");

}
