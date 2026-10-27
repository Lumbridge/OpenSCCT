using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The merc laser's mark timing (spec/equipment/merc-laser.md rules 8 and 9): the target is stamped with the whole
/// second it was first lit and is marked once the beam stayed on it for more than TimeToMark seconds since that stamp
/// (3 to 4 s of contact); any tick off the target clears the stamp, so the count starts again. Playtest: marks took
/// 5.6 to 10.7 s or never because the smoke's held spy bot crept up to 30 uu away and was pulled back, which took the
/// client's beam off him for a tick or two every second.
/// </summary>
public sealed class MercLaserMarkTests
{
    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SBasePawn Spy, SGadTorcheLaser Laser) Setup()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        pc.MyDesiredRotation = new FRotator(0, 0, 0);
        (_, SBasePawn spy) = scene.Player(true, new FVector(400f, 0f, 100f));
        scene.Run(2.5f);
        var laser = GadgetKit.Gadget(scene, merc, new SGadTorcheLaser(), Select: false);
        return (scene, pc, merc, spy, laser);
    }

    [Fact]
    public void AStillSpyHeldInTheBeamIsMarkedAfterThreeToFourSeconds()
    {
        (CombatScene scene, _, _, SBasePawn spy, SGadTorcheLaser laser) = Setup();
        laser.bInUse = true;
        float start = scene.Time;
        while (!spy.bSpyBulletOn && scene.Time - start < 6f)
        {
            scene.Run(Sim.DeltaTime);
        }

        Assert.True(spy.bSpyBulletOn, "the spy was not marked in 6 s of contact");
        float took = scene.Time - start;
        Assert.InRange(took, 3f, 4.1f);
    }

    [Fact]
    public void ATickOffTheTargetStartsTheCountAgain()
    {
        (CombatScene scene, SPlayerController pc, _, SBasePawn spy, SGadTorcheLaser laser) = Setup();
        laser.bInUse = true;
        scene.Run(3f);
        Assert.Same(spy, laser.TargetPawn);
        int stamped = laser.TimeTarget;

        // One tick aimed away, as when the target jumps out of the beam.
        pc.MyDesiredRotation = new FRotator(0, 16384, 0);
        scene.Run(Sim.DeltaTime);
        Assert.Null(laser.TargetPawn);
        Assert.Equal(0, laser.TimeTarget);

        pc.MyDesiredRotation = new FRotator(0, 0, 0);
        scene.Run(Sim.DeltaTime);
        Assert.Same(spy, laser.TargetPawn);
        Assert.True(laser.TimeTarget > stamped, $"restamped {laser.TimeTarget} after {stamped}");

        scene.Run(2.5f);
        Assert.False(spy.bSpyBulletOn, "marked although the count started again 2.5 s ago");
    }
}
