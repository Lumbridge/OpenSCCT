using System;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Demo;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class DemoChaseCameraTests
{
    [Fact]
    public void FollowsRecordedViewWhileTheBodyAndWeaponFaceElsewhere()
    {
        var pawn = Pawn(viewPitch: -2000, viewYaw: 16384);
        pawn.Rotation = new FRotator(0, 32768, 0);
        pawn.iDesiredYawPitch = Pack(4000, 49152);
        var camera = new DemoChaseCamera();

        ViewSample view = camera.ViewFor(pawn, 42, 85f, 260f);

        Assert.Equal(new FRotator(-2000, 16384, 0), view.Rotation);
        Assert.True(view.Location.Y < pawn.Location.Y);
        Assert.True(view.Location.Z > pawn.Location.Z + pawn.EyeHeight);
        Assert.InRange((Eye(pawn) - view.Location).Size(), 259.99f, 260.01f);
        Assert.InRange(((Eye(pawn) - view.Location).SafeNormal() | view.Rotation.Vector()), 0.9999f, 1.0001f);
        Assert.Equal(42, view.Tick);
        Assert.Equal(85f, view.Fov);
        Assert.True(view.Valid);
    }

    [Fact]
    public void ZeroRecordedViewIsNotReplacedByTheBodyOrAim()
    {
        var pawn = Pawn();
        pawn.Rotation = new FRotator(0, 16384, 0);
        pawn.iDesiredYawPitch = Pack(2000, 32768);
        var camera = new DemoChaseCamera();

        ViewSample view = camera.ViewFor(pawn, 1, 85f, 260f);

        Assert.Equal(FRotator.Zero, view.Rotation);
        Near(Eye(pawn) + new FVector(-260f, 0f, 0f), view.Location);
    }

    [Fact]
    public void RecordedTurnsAndMovementKeepTheCameraBehindThePlayer()
    {
        var pawn = Pawn();
        var camera = new DemoChaseCamera();
        ViewSample initial = camera.ViewFor(pawn, 1, 85f, 260f);
        pawn.Location += new FVector(500f, -100f, 40f);
        pawn.iYawPitchFromView = Pack(0, 16384);

        ViewSample turned = camera.ViewFor(pawn, 2, 85f, 260f);

        Near(Eye(pawn) + new FVector(0f, -260f, 0f), turned.Location);
        Assert.Equal(16384, turned.Rotation.Yaw);
        Assert.NotEqual(initial.Location, turned.Location);
    }

    [Fact]
    public void HeldOrbitRetainsItsOffsetAsTheRecordedPlayerTurns()
    {
        var pawn = Pawn(viewPitch: -1000, viewYaw: 16000);
        var camera = new DemoChaseCamera { Orbiting = true };
        camera.Turn(pawn, 100f, -50f);
        Assert.Equal(new FRotator(0, 18000, 0), camera.RotationFor(pawn));

        pawn.iYawPitchFromView = Pack(2000, 32000);
        camera.Advance(2f);

        Assert.True(camera.RotationFor(pawn).EqualsWrapped(new FRotator(3000, 34000, 0)));
    }

    [Fact]
    public void ReleasingOrbitSmoothlyReturnsEvenWithoutPlaybackTicks()
    {
        var pawn = Pawn();
        var camera = new DemoChaseCamera { Orbiting = true };
        camera.Turn(pawn, 800f, -200f);
        camera.Orbiting = false;

        int previousYaw = camera.RotationFor(pawn).Yaw;
        for (int frame = 0; frame < 30; frame++)
        {
            camera.Advance(1f / 60f);
            int yaw = camera.RotationFor(pawn).Yaw;
            Assert.InRange(yaw, 0, previousYaw);
            previousYaw = yaw;
        }

        Assert.InRange(previousYaw, 1, 80);
        camera.Advance(1f);
        Assert.Equal(FRotator.Zero, camera.RotationFor(pawn));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void RecenterUsesElapsedRealTimeAtEveryFrameRate(int fps)
    {
        var pawn = Pawn(viewPitch: -2000, viewYaw: 12000);
        var once = new DemoChaseCamera { Orbiting = true };
        var frames = new DemoChaseCamera { Orbiting = true };
        once.Turn(pawn, 1200f, 300f);
        frames.Turn(pawn, 1200f, 300f);
        once.Orbiting = frames.Orbiting = false;

        once.Advance(0.5f);
        for (int frame = 0; frame < fps / 2; frame++) frames.Advance(1f / fps);

        FRotator expected = once.RotationFor(pawn);
        FRotator actual = frames.RotationFor(pawn);
        Assert.InRange(Math.Abs(expected.Yaw - actual.Yaw), 0, 1);
        Assert.InRange(Math.Abs(expected.Pitch - actual.Pitch), 0, 1);
    }

    [Fact]
    public void RecenterTakesTheShortWayAfterOrbitingMoreThanHalfATurn()
    {
        var pawn = Pawn();
        var camera = new DemoChaseCamera { Orbiting = true };
        camera.Turn(pawn, 3000f, 0f);
        Assert.Equal(-5536, camera.RotationFor(pawn).Yaw);
        camera.Orbiting = false;

        camera.Advance(1f / 60f);

        Assert.InRange(camera.RotationFor(pawn).Yaw, -5535, -1);
    }

    [Theory]
    [InlineData(-15000, 1000f, -16000)]
    [InlineData(15000, -1000f, 16000)]
    public void OrbitPitchStopsBeforeEitherPole(int pitch, float mouseY, int expected)
    {
        var pawn = Pawn(viewPitch: pitch);
        var camera = new DemoChaseCamera { Orbiting = true };

        camera.Turn(pawn, 0f, mouseY);

        Assert.Equal(expected, camera.RotationFor(pawn).Pitch);
        Assert.True(camera.RotationFor(pawn).Vector().X > 0f);
    }

    [Fact]
    public void ResetRemovesOrbitAndMouseMotionOutsideOrbitIsIgnored()
    {
        var pawn = Pawn(viewPitch: -3000, viewYaw: 5000);
        var camera = new DemoChaseCamera { Orbiting = true };
        camera.Turn(pawn, 700f, 100f);

        camera.Reset();
        camera.Turn(pawn, 900f, 400f);

        Assert.False(camera.Orbiting);
        Assert.Equal(new FRotator(-3000, 5000, 0), camera.RotationFor(pawn));
    }

    private static SPawnAttaque Pawn(int viewPitch = 0, int viewYaw = 0) => new()
    {
        Location = new FVector(100f, 200f, 300f),
        EyeHeight = 60f,
        iYawPitchFromView = Pack(viewPitch, viewYaw),
    };

    private static int Pack(int pitch, int yaw) => ((pitch & 0xFFFF) << 16) | (yaw & 0xFFFF);

    private static FVector Eye(SBasePawn pawn) => pawn.Location + new FVector(0f, 0f, pawn.EyeHeight);

    private static void Near(FVector expected, FVector actual) => Assert.InRange((expected - actual).Size(), 0f, 0.01f);
}
