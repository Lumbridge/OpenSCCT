using System;
using OpenScct.Game.Lighting;
using Xunit;

namespace OpenScct.Game.Tests.Lighting;

public sealed class ActorLightAttenuationTests
{
    [Fact]
    public void Cabinet_sphere_retains_light_beyond_nominal_radius()
    {
        // Native SetLight: R=525, near=220, far=472.5. Its negative intercept is clamped to .01,
        // then the slope is recomputed at the near sample. This is not direct cubic falloff.
        var fit = ActorLightRules.FitAttenuation(400, 180, 20);
        Assert.Equal(.01f, fit.Constant);
        Assert.InRange(fit.Linear, .00361f, .00364f);
        float beyondRadius = 1f / (fit.Constant + fit.Linear * 550);
        Assert.InRange(beyondRadius, .49f, .51f);
    }

    [Fact]
    public void Zero_radius_actor_keeps_attenuation_at_its_center()
    {
        var fit = ActorLightRules.FitAttenuation(250, 0, 19); // R=500, 2*S(.5)=1.
        Assert.Equal(1f, fit.Constant);
        Assert.Equal(0f, fit.Linear);
    }

    // Measured original SetLight outputs (game 0x10978cc0), spanning tiny and large actors,
    // near and distant lights. Inputs are distance, actor sphere radius and LightRadius.
    [Theory]
    [InlineData(855.757386f, 301.669006f, 25, .01f, .0153043261f)]
    [InlineData(107.961030f, 20.404152f, 20, .436504513f, .00118263857f)]
    [InlineData(20.432270f, 20.404152f, 20, .488981545f, .000484223594f)]
    [InlineData(295.913532f, 313.723083f, 1, .01f, .100880660f)]
    [InlineData(537.112906f, 14.896509f, 20, .01f, .138256669f)]
    public void Fit_matches_original_runtime_coefficients(float distance, float actorRadius, float lightRadius,
        float constant, float linear)
    {
        var actual = ActorLightRules.FitAttenuation(distance, actorRadius, lightRadius);
        Assert.InRange(MathF.Abs(actual.Constant - constant), 0, 2e-6f);
        Assert.InRange(MathF.Abs(actual.Linear - linear), 0, MathF.Abs(linear) * 2e-5f);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 10, 0)]
    [InlineData(10000, 5000, 20)]
    [InlineData(1, 2000, 255)]
    public void Clamped_native_fit_stays_finite_and_nonnegative(float distance, float radius, float lightRadius)
    {
        var fit = ActorLightRules.FitAttenuation(distance, radius, lightRadius);
        Assert.True(float.IsFinite(fit.Constant));
        Assert.True(float.IsFinite(fit.Linear));
        Assert.True(fit.Constant > 0);
        Assert.True(fit.Linear >= 0);
    }
}
