using OpenScct.Core;
using OpenScctGame.Render;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SmokeTemporalHistoryTests
{
    private static SmokeTemporalView View => new(42, default, new(1, 0, 0), new(0, 0, 1), new(0, 1, 0),
        1, 1, 2, 100, 1000, 0, 40, 30, 32, 5, 1, 123);

    [Fact]
    public void StableCameraAndSmallMotionReuseTheirOwnHistory()
    {
        var history = new SmokeTemporalHistory();
        Assert.False(history.Update(1, View, out _));
        Assert.True(history.Update(1, View, out SmokeTemporalView previous));
        Assert.Equal(View, previous);
        SmokeTemporalView moved = View with { Eye = new FVector(10, 0, 0), Time = 2.04f, Near = 90, Far = 990 };
        Assert.True(history.Update(1, moved, out previous));
        Assert.Equal(View, previous);
        Assert.Equal(2, history.AcceptedViews); Assert.Equal(1, history.RejectedViews);
    }

    [Theory]
    [InlineData("camera")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("slices")]
    [InlineData("quality")]
    [InlineData("vision")]
    [InlineData("geometry")]
    [InlineData("fov_x")]
    [InlineData("fov_y")]
    [InlineData("rewind")]
    [InlineData("stall")]
    [InlineData("translation_cut")]
    [InlineData("rotation_cut")]
    [InlineData("roll_cut")]
    public void DiscontinuityRejectsThePreviousView(string change)
    {
        var history = new SmokeTemporalHistory(); history.Update(1, View, out _);
        SmokeTemporalView changed = change switch
        {
            "camera" => View with { Camera = 43 },
            "width" => View with { Width = 41 },
            "height" => View with { Height = 31 },
            "slices" => View with { Slices = 64 },
            "quality" => View with { Quality = 2 },
            "vision" => View with { Vision = 36 },
            "geometry" => View with { Geometry = 124 },
            "fov_x" => View with { ProjectionX = 1.1f },
            "fov_y" => View with { ProjectionY = 1.1f },
            "rewind" => View with { Time = 1.99f },
            "stall" => View with { Time = 2.2f },
            "translation_cut" => View with { Eye = new FVector(51, 0, 0) },
            "rotation_cut" => View with { Forward = new FVector(1, 0, 0), Right = new FVector(0, -1, 0) },
            "roll_cut" => View with { Up = new FVector(1, 0, 0), Right = new FVector(0, 0, -1) },
            _ => throw new Xunit.Sdk.XunitException("Unknown fixture change"),
        };
        Assert.False(history.Update(1, changed, out SmokeTemporalView previous));
        Assert.Equal(View, previous);
        Assert.True(history.Update(1, changed, out previous));
        Assert.Equal(changed, previous);
    }

    [Fact]
    public void CameraSlotsDoNotBorrowEachOthersHistoryAndRetirementDropsIt()
    {
        var history = new SmokeTemporalHistory(); history.Update(1, View, out _);
        Assert.False(history.Update(2, View, out _));
        Assert.True(history.Update(1, View, out _));
        history.Remove(1); Assert.False(history.Update(1, View, out _));
        Assert.True(history.Update(2, View, out _));
        history.Clear(); Assert.False(history.Update(2, View, out _));
    }

    [Fact]
    public void LowQualityDoesNotAccumulateHistory()
    {
        var history = new SmokeTemporalHistory();
        SmokeTemporalView low = View with { Quality = 0 };
        Assert.False(history.Update(1, low, out _));
        Assert.False(history.Update(1, low, out _));
        Assert.Equal(0, history.AcceptedViews);
    }
}
