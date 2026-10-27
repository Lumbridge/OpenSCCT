using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The draw modes and a merc's motion tracker (spec/equipment/spy-shocker.md rule 16): the tracker locks pawns moving at
/// 150 uu/s or more and projectiles, nothing the draw mode changes. The laser's dot and beam are neither, and stay still;
/// a still spy is not locked with the laser on or off, a spy walking with the gun out at the captured 150.1 uu/s is
/// locked with the laser on or off.
/// </summary>
public sealed class ShockerMotionTrackingTests
{
    private static (CombatScene Scene, SMotionTrackingVision Tracker, SPawnAttaque Spy, SStickyShocker Pistol) Scene()
    {
        var scene = new CombatScene(ENetMode.NM_Client);
        scene.Level.SetService<ICollisionWorld>(new EmptyWorld());
        (SPlayerController merc, _) = scene.Player(false, new FVector(0f, 0f, 100f));
        (SPlayerController spyPc, SBasePawn pawn) = scene.Player(true, new FVector(1000f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        spy.WeaponDummy = pistol;
        spy.WeaponLoaded = 2;
        // The spy's own machine draws his beam; every machine draws his dot.
        scene.Level.Info.LocalPlayerController = spyPc;
        SMotionTrackingVision tracker = scene.Level.SpawnActor(new SMotionTrackingVision(), merc.Location, default, merc)!;
        tracker.PlayerOwner = merc;
        scene.Run(0.05f);
        return (scene, tracker, spy, pistol);
    }

    private static bool Locked(SMotionTrackingVision tracker, Actor a) => tracker.LockedObjects.Exists(l => ReferenceEquals(l.Actor, a));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStillSpyIsNotLockedWithTheLaserOnOrOff(bool laser)
    {
        (CombatScene scene, SMotionTrackingVision tracker, SPawnAttaque spy, SStickyShocker pistol) = Scene();
        pistol.bLaserOn = laser;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        SWeaponAttaque.ViewUpdate((SPlayerController)scene.Level.Info.LocalPlayerController!, spy.Location);
        Assert.Equal(laser, pistol.LaserProjector is not null);
        spy.Velocity = default;
        tracker.GetActors();
        Assert.Empty(tracker.LockedObjects);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWalkingArmedSpyIsLockedWithTheLaserOnOrOff(bool laser)
    {
        (_, SMotionTrackingVision tracker, SPawnAttaque spy, SStickyShocker pistol) = Scene();
        pistol.bLaserOn = laser;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        // The capture's armed walk after either draw: 150.1 uu/s.
        spy.Velocity = new FVector(150.1f, 0f, 0f);
        tracker.GetActors();
        Assert.True(Locked(tracker, spy));
        Assert.Single(tracker.LockedObjects);
        if (laser)
        {
            Assert.False(Locked(tracker, pistol.LaserProjector!));
            Assert.False(Locked(tracker, pistol.LaserBeam!));
        }
    }
}
