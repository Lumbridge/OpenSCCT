using OpenScct.Core;
using OpenScct.Game.Lighting;
using Xunit;

namespace OpenScct.Game.Tests.Lighting;

public sealed class ActorShadowLightingTests
{
    [Fact]
    public void Projector_points_towards_weighted_light_position()
    {
        var cache = new ActorShadowLighting();
        var light = new ActorShadowLight(new object(), new FVector(100, 0, 200), default, 100, 100, 0, 0, true, false, false);
        byte darkness = cache.Update([light], default, 40, default, 1, 1.7f, true, 0, (_, _) => true);
        Assert.True(darkness > 0);
        Assert.InRange(cache.Direction.X, .4471f, .4473f);
        Assert.InRange(cache.Direction.Z, .8943f, .8945f);
        Assert.Equal(0, cache.Direction.Y);
    }

    [Fact]
    public void Sunlight_casts_opposite_its_forward_direction_independent_of_world_position()
    {
        var cache = new ActorShadowLighting();
        var light = new ActorShadowLight(new object(), new FVector(900, 400, 2000), new FRotator(-16384, 0, 0), 100, 1, 0,
            ActorLightRules.LE_Sunlight, true, false, false);
        cache.Update([light], new FVector(100, 200, 300), 40, new FVector(100, 200, 300), 1, 1, true, 0, (_, _) => true);
        Assert.InRange(cache.Direction.Z, .999f, 1.001f);
        Assert.InRange(cache.Direction.X, -.001f, .001f);
        Assert.Equal((byte)100, cache.Darkness);
        cache.Update([light], default, 40, default, 2, 1, true, 1, (_, _) => true);
        Assert.Equal((byte)0, cache.Darkness);
        cache.Reset();
        Assert.Equal(new FVector(0, 0, 1), cache.Direction);
    }
}
