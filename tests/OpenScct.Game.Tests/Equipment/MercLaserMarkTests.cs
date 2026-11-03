using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
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
    private sealed class RemoteConnection : UNetConnection { }
    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SBasePawn Spy, SGadTorcheLaser Laser) Setup(bool dedicatedBot = false)
    {
        var scene = new CombatScene(dedicatedBot ? ENetMode.NM_DedicatedServer : ENetMode.NM_ListenServer);
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        if (dedicatedBot) pc.Player = new OpenScct.Game.AI.AiViewport();
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
    public void DedicatedBotLaserMarksThroughSmokeWithNormalDwellTime()
    {
        var (scene, _, _, spy, laser) = Setup(dedicatedBot: true);
        scene.Level.SpawnActor(new SSmokeEmitter(), spy.Location, default);
        laser.bInUse = true;
        scene.Run(2.5f);
        Assert.False(spy.bSpyBulletOn);
        scene.Run(2f);
        Assert.True(spy.bSpyBulletOn);
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

    [Fact]
    public void HumanDedicatedLaserTracksAndMarksWithoutTrustingClientTargetState()
    {
        var (scene, pc, _, spy, laser) = Setup(dedicatedBot: true);
        pc.Player = new RemoteConnection();
        laser.bInUse = true;
        scene.Run(4.2f);
        Assert.Same(spy, laser.TargetPawn);
        Assert.True(spy.bSpyBulletOn);
        spy.bSpyBulletOn = false;
        RequestMark(pc, laser, spy);
        scene.Run(1f);
        Assert.False(spy.bSpyBulletOn); // No repeated mark for the same uninterrupted contact.
    }

    [Theory]
    [InlineData("off")]
    [InlineData("early")]
    [InlineData("out-of-range")]
    [InlineData("occluded")]
    [InlineData("holstered")]
    [InlineData("dead-owner")]
    [InlineData("old-pawn")]
    [InlineData("disabled")]
    public void OwnerRequestsCannotBypassTheAuthoritativeLaserRules(string invalid)
    {
        var (scene, pc, merc, spy, laser) = Setup(dedicatedBot: true);
        pc.Player = new RemoteConnection();
        laser.bInUse = invalid != "off";
        laser.NativeTickTorcheLaser(0f);
        switch (invalid)
        {
            case "out-of-range": spy.SetLocation(new FVector(50000f, 0f, 100f)); break;
            case "occluded": GadgetKit.Walls(scene, GadgetKit.WallAt(200f)); break;
            case "holstered": merc.WeaponLoaded = 0; break;
            case "dead-owner": merc.Health = 0; break;
            case "old-pawn": pc.Pawn = null; break;
            case "disabled": pc.GameReplicationInfo = scene.Level.SpawnActor(new GameReplicationInfo { bAvlb_MERC_Laser = false }, default, default); break;
        }

        RequestMark(pc, laser, spy);
        Assert.False(spy.bSpyBulletOn);
        if (invalid != "early")
        {
            scene.Run(5f);
            RequestMark(pc, laser, spy);
            Assert.False(spy.bSpyBulletOn);
        }
        else
        {
            // An early packet does not delay or suppress the server's eventual earned mark.
            scene.Run(4.2f);
            Assert.True(spy.bSpyBulletOn);
        }
    }

    [Fact]
    public void AnEarnedDwellCannotBeTransferredToAnotherPawn()
    {
        var (scene, pc, _, spy, laser) = Setup(dedicatedBot: true);
        pc.Player = new RemoteConnection();
        (_, SBasePawn otherSpy) = scene.Player(true, new FVector(400f, 500f, 100f));
        (_, SBasePawn teammate) = scene.Player(false, new FVector(400f, -500f, 100f));
        laser.bInUse = true;
        scene.Run(3f);
        RequestMark(pc, laser, otherSpy);
        RequestMark(pc, laser, teammate);
        Assert.False(otherSpy.bSpyBulletOn);
        Assert.False(teammate.bSpyBulletOn);
        scene.Run(1.2f);
        Assert.True(spy.bSpyBulletOn);
    }

    private static void RequestMark(SPlayerController pc, SGadTorcheLaser laser, SBasePawn target) =>
        pc.ReceiveCombatCall(new CombatCall(new Name("ThrowSpyBullet")) { Target = laser, Actor1 = target }, true);
}
