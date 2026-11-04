using System.IO;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class SecurityCameraShotTests
{
    [Fact]
    public void FactoryCameraSideShotsReachTheHeadInsteadOfItsMount()
    {
        string folder = Path.Combine(Movement.MovementTestKit.RepoRoot, ".content", "svmap", "FactE");
        Assert.SkipWhen(!Directory.Exists(folder), "FactE has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, folder);
        SCamera? camera = null;
        foreach (SCamera placed in rig.Level.AllActors<SCamera>())
        {
            camera = placed;
            break;
        }
        Assert.NotNull(camera);
        camera.Rotation.GetAxes(out _, out FVector side, out _);
        foreach (float sign in new[] { -1f, 1f })
        {
            var hit = TraceHitZones.Instance.TraceBone(rig.Pawn,
                camera.Location - side * (sign * 30f), camera.Location + side * (sign * 70f), true);
            Assert.True(hit.bHit);
            Assert.Same(camera, hit.Actor);
        }
    }

    [Fact]
    public void WallInFrontOfACameraStillStopsTheShocker()
    {
        DeviceScene scene = DeviceScene.Create();
        var camera = scene.Add(new SCamera(), new FVector(0, 0, 200));
        scene.Collider(camera, new FVector(-10, -8, -8), new FVector(30, 8, 8));
        var wall = scene.Add(new SGamePlayObject { StaticMesh = new AssetRef("Test.wall") }, new FVector(60, 0, 200));
        scene.Collider(wall, new FVector(-5, -100, -100), new FVector(5, 100, 100));
        scene.Begin();
        scene.Level.SetService<IHitZones>(TraceHitZones.Instance);
        var spy = scene.Spawn(new SPawnAttaque(), new FVector(1000, 1000, 1000));
        var pistol = scene.Spawn(new SStickyShocker());
        pistol.Instigator = spy;
        FVector start = camera.Location + new FVector(200, 0, 0);
        var hit = TraceHitZones.Instance.TraceBone(pistol, camera.Location, start, true);
        Assert.Same(wall, hit.Actor);
        FRotator aim = new(0, 32768, 0);
        FVector offset = SWeaponAttaque.GetAxesAndLocation(default, aim, out _, out _, out _);
        pistol.TraceFireWithPos(start - offset, aim, 0f, 0f, 0f);
        Assert.False(camera.IsInState("s_LockedByChaff"));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, -1)]
    public void ShockerHitsExposedCameraBodyFromEveryDirection(int x, int y, int z)
    {
        DeviceScene scene = DeviceScene.Create();
        var camera = scene.Add(new SCamera(), new FVector(0, 0, 200));
        // A camera body protrudes farther than the mount at the front, but is narrower at the sides.
        scene.Collider(camera, new FVector(-10, -8, -8), new FVector(30, 8, 8));
        scene.Begin();
        scene.Level.SetService<IHitZones>(TraceHitZones.Instance);
        Assert.NotNull(camera.WallSupport);
        var spy = scene.Spawn(new SPawnAttaque(), new FVector(1000, 1000, 1000));
        var pistol = scene.Spawn(new SStickyShocker());
        pistol.Instigator = spy;
        FVector direction = new(x, y, z);
        FVector start = camera.Location + direction * 100;
        var hit = TraceHitZones.Instance.TraceBone(pistol, camera.Location - direction * 100, start, true);
        Assert.True(hit.bHit);
        Assert.Same(camera, hit.Actor);
        FRotator aim = (-direction).Rotation();
        FVector offset = SWeaponAttaque.GetAxesAndLocation(default, aim, out _, out _, out _);
        pistol.TraceFireWithPos(start - offset, aim, 0f, 0f, 0f);
        Assert.True(camera.IsInState("s_LockedByChaff"));
    }
}
