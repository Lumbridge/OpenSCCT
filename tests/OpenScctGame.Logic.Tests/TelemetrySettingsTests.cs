using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class TelemetrySettingsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OlderProfilesKeepTheirEnabledReadouts(bool fps, bool network)
    {
        GameSettings loaded = GameSettings.FromJson($$"""{"show_fps":{{fps.ToString().ToLowerInvariant()}},"show_net":{{network.ToString().ToLowerInvariant()}}}""");
        Assert.Equal(fps, loaded.ShowFps);
        Assert.Equal(network, loaded.ShowNet);
        Assert.Equal("Always", loaded.FpsTelemetry.DisplayMode);
        Assert.Equal("Always", loaded.NetworkTelemetry.DisplayMode);
        Assert.Equal("Off", loaded.FpsTelemetry.Graph);
        Assert.Equal("Off", loaded.NetworkTelemetry.Graph);
    }

    [Fact]
    public void ReadoutsPersistIndependentAppearanceGraphsAndThresholds()
    {
        GameSettings original = new()
        {
            ShowFps = true,
            ShowNet = true,
            ShowNetLoss = true,
            FpsTelemetry = new()
            {
                DisplayMode = "Issues", PositionX = 25f, PositionY = 75f, Scale = 125f, Opacity = 35f,
                Graph = "Bars", GraphSeconds = 12f, IssueHoldSeconds = 4.5f,
                ShowFrameTime = false, ShowPercentiles = false, ShowAverage = true, ShowPeakFrameTime = true,
                FrameTimeThresholdMs = 22f,
            },
            NetworkTelemetry = new()
            {
                PositionX = 80f, PositionY = 20f, Scale = 80f, Opacity = 65f,
                Graph = "Line", GraphSeconds = 20f, IssueHoldSeconds = 1.5f, Detailed = false, ShowTraffic = false,
                ShowPingGraph = false, ShowJitterGraph = false, ShowLossGraph = true,
                PingThresholdMs = 95f, JitterThresholdMs = 17f, LossThresholdPercent = 0.5f,
            },
        };
        GameSettings copy = original.Clone();
        Assert.Equal(original.ToJson(), copy.ToJson());
        copy.FpsTelemetry.Opacity = 0f;
        copy.NetworkTelemetry.PositionX = 0f;
        Assert.Equal(35f, original.FpsTelemetry.Opacity);
        Assert.Equal(80f, original.NetworkTelemetry.PositionX);
        Assert.True(copy.ShowFps);
        Assert.True(copy.ShowNet);
        Assert.True(copy.ShowNetLoss);
    }

    [Fact]
    public void NullGroupsAndInvalidValuesRecoverToUsableSettings()
    {
        GameSettings missing = GameSettings.FromJson("""{"fps_telemetry":null,"network_telemetry":null}""");
        Assert.Equal(100f, missing.FpsTelemetry.Scale);
        Assert.Equal(100f, missing.NetworkTelemetry.Opacity);

        GameSettings invalid = GameSettings.FromJson("""
            {"fps_telemetry":{"display_mode":"broken","graph":"broken","position_x":-10,"position_y":300,
                "scale":-1,"opacity":101,"graph_seconds":0,"issue_hold_seconds":-5,"frame_time_threshold_ms":0},
             "network_telemetry":{"position_x":200,"position_y":-10,"scale":900,"opacity":-1,
                "graph_seconds":999,"issue_hold_seconds":999,"ping_threshold_ms":0,"jitter_threshold_ms":9999,"loss_threshold_percent":0}}
            """);
        Assert.Equal("Always", invalid.FpsTelemetry.DisplayMode);
        Assert.Equal("Off", invalid.FpsTelemetry.Graph);
        Assert.Equal(0f, invalid.FpsTelemetry.PositionX);
        Assert.Equal(100f, invalid.FpsTelemetry.PositionY);
        Assert.Equal(50f, invalid.FpsTelemetry.Scale);
        Assert.Equal(100f, invalid.FpsTelemetry.Opacity);
        Assert.Equal(2f, invalid.FpsTelemetry.GraphSeconds);
        Assert.Equal(0f, invalid.FpsTelemetry.IssueHoldSeconds);
        Assert.Equal(1f, invalid.FpsTelemetry.FrameTimeThresholdMs);
        Assert.Equal(100f, invalid.NetworkTelemetry.PositionX);
        Assert.Equal(0f, invalid.NetworkTelemetry.PositionY);
        Assert.Equal(200f, invalid.NetworkTelemetry.Scale);
        Assert.Equal(0f, invalid.NetworkTelemetry.Opacity);
        Assert.Equal(30f, invalid.NetworkTelemetry.GraphSeconds);
        Assert.Equal(10f, invalid.NetworkTelemetry.IssueHoldSeconds);
        Assert.Equal(1f, invalid.NetworkTelemetry.PingThresholdMs);
        Assert.Equal(500f, invalid.NetworkTelemetry.JitterThresholdMs);
        Assert.Equal(0.1f, invalid.NetworkTelemetry.LossThresholdPercent);
    }

    [Fact]
    public void NonFiniteValuesUseSafeDefaultsBeforeSerialization()
    {
        var settings = new GameSettings();
        settings.FpsTelemetry.Scale = float.NaN;
        settings.FpsTelemetry.PositionX = float.PositiveInfinity;
        settings.FpsTelemetry.FrameTimeThresholdMs = float.NegativeInfinity;
        settings.NetworkTelemetry.Opacity = float.NaN;
        settings.NetworkTelemetry.JitterThresholdMs = float.PositiveInfinity;
        settings.Clamp();
        Assert.Equal(100f, settings.FpsTelemetry.Scale);
        Assert.Equal(100f, settings.FpsTelemetry.PositionX);
        Assert.Equal(33.3f, settings.FpsTelemetry.FrameTimeThresholdMs);
        Assert.Equal(100f, settings.NetworkTelemetry.Opacity);
        Assert.Equal(30f, settings.NetworkTelemetry.JitterThresholdMs);
        Assert.NotEmpty(settings.ToJson());
    }
}
