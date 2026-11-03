using OpenScctGame.Audio;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SoundPriorityTests
{
    [Fact]
    public void OccludedZonesMakeDistantVoicesEasierToReplace()
    {
        float clear = SoundRules.Priority(500f * 500f, 10f, 0.8f, 0);
        float occluded = SoundRules.Priority(500f * 500f, 10f, 0.8f, 0, SoundRules.AmbientZoneFactor(2));
        Assert.Equal(0.6f, clear, 5);
        Assert.Equal(AudioK.PriorityFloor * 0.8f, occluded, 5);
        Assert.True(occluded < SoundRules.Priority(700f * 700f, 10f, 0.8f, 0));
    }

    [Fact]
    public void DisconnectedZonesNeverBecomeUnlimitedPriority()
    {
        float unreachable = SoundRules.AmbientZoneFactor(255);
        Assert.Equal(0f, unreachable);
        Assert.Equal(AudioK.PriorityFloor, SoundRules.Priority(100f, 10f, 1f, 0, unreachable));
        Assert.Equal(1f, SoundRules.Priority(100f, 0f, 1f, 0, unreachable));
        Assert.Equal(1f, SoundRules.Priority(0f, 10f, 1f, 0, unreachable));
    }

    [Fact]
    public void OcclusionPreservesFlatMusicAndStreamedPriorityBonuses()
    {
        Assert.Equal(3f + AudioK.PriorityFloor, SoundRules.Priority(100f, 10f, 1f,
            AudioK.FlagMusic | AudioK.Flag2D, 0f));
    }
}
