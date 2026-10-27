using OpenScct.DiffTest.Measure;
using OpenScct.DiffTest.Model;
using Xunit;

namespace OpenScct.DiffTest.Tests;

public sealed class MeasurementTests
{
    private const double Dt = 1.0 / 60.0;

    [Fact]
    public void PlateauAndRampOfASyntheticWalk()
    {
        TraceRun run = Synthetic.Walk("movement.synthetic", 1, 240, 423.5, 3300, 180, _ => Dt);
        (double mean, double min, double max) = Locomotion.PlateauSpeed(run, 90, 180);
        Assert.Equal(423.5, mean, 6);
        Assert.Equal(min, max, 9);
        // 3300 uu/s^2 reaches 423.5 in 7.7 frames: the 8th frame is the first at full speed.
        (double seconds, int frames) = Locomotion.TimeToReach(run, 0, 0.999, 423.5);
        Assert.Equal(8, frames);
        Assert.Equal(8 * Dt, seconds, 6);
        Assert.Equal(3300, Locomotion.PeakAcceleration(run, 0, 30, slowing: false), 3);
        (double stop, int stopFrames) = Locomotion.TimeToSlow(run, 180, 0.0, 423.5);
        Assert.Equal(1, stopFrames);
        Assert.Equal(Dt, stop, 6);
        Assert.Equal(0, Locomotion.Distance2D(run, 181, 239), 9);
    }

    [Fact]
    public void ApexAirtimeAndGravityOfABallisticJump()
    {
        TraceRun run = Synthetic.Jump("movement.synthetic", 150, 30, 610, -1500, Dt);
        Locomotion.AirResult air = Locomotion.Air(run, 30);
        Assert.Equal(-1500, air.Gravity, 3);
        Assert.Equal(610, air.LaunchVz, 3);
        Assert.InRange(air.Apex, 120, 125);       // 610^2 / 3000 = 124.0, sampled per frame
        Assert.InRange(air.AirSeconds, 0.79, 0.83);
    }

    [Theory]
    [InlineData("spy", 300, 0)]
    [InlineData("spy", 600, 0)]
    [InlineData("spy", 790.6, 21)]
    [InlineData("spy", 1500, 100)]
    [InlineData("merc", 530, 14)]
    [InlineData("merc", 1000, 100)]
    [InlineData("merc", 1500, 190)]
    public void SpecFallDamage(string team, double height, int damage)
    {
        Assert.Equal(damage, Locomotion.SpecFallDamage(team, height));
    }
}
