using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The spy's chaff grenade: the burst, the jamming field and what it switches off (spec/equipment/chaff-grenade.md).</summary>
public sealed class ChaffGrenadeTests
{
    private static readonly Name s_Malfunction = new("s_Malfunction");

    private static (SPlayerController PC, SBasePawn Merc, SGoggle Goggle) Merc(CombatScene Scene, FVector At)
    {
        (SPlayerController pc, SBasePawn merc) = Scene.Player(false, At);
        pc.myHUD = Scene.Level.SpawnActor(new SHud(), At, default);
        SGoggle goggle = Scene.Level.SpawnActor(new SGoggle(), At, default)!;
        goggle.SetOwner(pc);
        goggle.SPC = pc;
        pc.Goggle = goggle;
        return (pc, merc, goggle);
    }

    private static SProjChaffe Burst(CombatScene Scene, FVector At)
    {
        SProjChaffe chaff = Scene.Level.SpawnActor(new SProjChaffe(), At, default)!;
        chaff.Explode(At, K.ChaffExplodeNormal);
        return chaff;
    }

    [Fact]
    public void TheFieldLastsTenSecondsAndJamsWithinThreeHundredUnits()
    {
        var chaff = new SProjChaffe();
        Assert.Equal(10f, chaff.TempsBrouillage);
        Assert.Equal(300, chaff.Range);
        Assert.Equal(2f, chaff.TimeToDisipation);
        Assert.Equal(5, new SGadChaffeGrenade().Charge);
    }

    [Fact]
    public void TheBurstHidesTheGrenadeAndStartsTheField()
    {
        CombatScene scene = GadgetKit.Scene();
        SProjChaffe chaff = Burst(scene, new FVector(0f, 0f, 50f));
        Assert.True(chaff.Exploded);
        Assert.True(chaff.bHidden);
        Assert.Equal(GadgetStates.s_Brouillage, chaff.GetStateName());
        Assert.Single(GadgetKit.All<SChaffeEmitter>(scene));
        chaff.Explode(chaff.Location, K.ChaffExplodeNormal);
        Assert.Single(GadgetKit.All<SChaffeEmitter>(scene));
    }

    [Fact]
    public void TheGrenadeGoesWhenTheFieldEnds()
    {
        CombatScene scene = GadgetKit.Scene();
        SProjChaffe chaff = Burst(scene, new FVector(0f, 0f, 50f));
        scene.Run(9.8f);
        Assert.False(chaff.bDeleteMe);
        scene.Run(0.4f);
        Assert.True(chaff.bDeleteMe);
    }

    [Fact]
    public void AMercInTheFieldHasHisHudJammedAndHisGogglesReset()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _, SGoggle goggle) = Merc(scene, new FVector(200f, 0f, 100f));
        goggle.SwitchMode(1);
        goggle.eventDown();
        Assert.Equal(SGoggle.REN_MotionTrackingVision, goggle.CurrentMode);
        Burst(scene, new FVector(0f, 0f, 100f));
        scene.World.Step();
        Assert.True(pc.IsHudPerturbed);
        Assert.Equal(2f, ((SHud)pc.myHUD!).HudPerturbationLength);
        Assert.Equal(SGoggle.REN_DynLight, goggle.CurrentMode);
    }

    [Fact]
    public void AMercOutsideTheRangeOrBehindAWallIsNotJammed()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(100f));
        (SPlayerController far, _, _) = Merc(scene, new FVector(-400f, 0f, 100f));
        (SPlayerController walled, _, _) = Merc(scene, new FVector(200f, 200f, 100f));
        Burst(scene, new FVector(0f, 0f, 100f));
        scene.World.Step();
        Assert.False(far.IsHudPerturbed);
        Assert.False(walled.IsHudPerturbed);
    }

    [Fact]
    public void SpiesAreNotJammed()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController spy, _) = scene.Player(true, new FVector(100f, 0f, 100f));
        spy.myHUD = scene.Level.SpawnActor(new SHud(), default, default);
        Burst(scene, new FVector(0f, 0f, 100f));
        scene.World.Step();
        Assert.False(spy.IsHudPerturbed);
    }

    [Fact]
    public void ATorchInRangeMalfunctionsForFiveSeconds()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(150f, 0f, 100f));
        SGadTorche torch = scene.Give(new SGadTorche(), merc);
        torch.GiveTo(merc);
        Burst(scene, new FVector(0f, 0f, 100f));
        Assert.Equal(s_Malfunction, torch.GetStateName());
        scene.Run(5.2f);
        Assert.NotEqual(s_Malfunction, torch.GetStateName());
    }

    [Fact]
    public void ATorchOutOfRangeKeepsWorking()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(600f, 0f, 100f));
        SGadTorche torch = scene.Give(new SGadTorche(), merc);
        torch.GiveTo(merc);
        Burst(scene, new FVector(0f, 0f, 100f));
        Assert.NotEqual(s_Malfunction, torch.GetStateName());
    }

    [Fact]
    public void AMineInRangeIsSwitchedOffForTheFieldAndArmsAgainAfter()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = scene.Level.SpawnActor(new SMineProx(), new FVector(100f, 0f, 50f), default)!;
        scene.World.Step();
        Assert.Equal(GadgetStates.s_Activated, mine.GetStateName());
        Burst(scene, new FVector(0f, 0f, 50f));
        Assert.Equal(GadgetStates.s_LockedByChaff, mine.GetStateName());
        Assert.Equal(mine.LightOff, mine.CurrentSkin);
        scene.Run(10.2f);
        Assert.Equal(GadgetStates.s_Activated, mine.GetStateName());
        Assert.Equal(mine.LightOn, mine.CurrentSkin);
    }

    [Fact]
    public void AMineOutsideTheRangeStaysArmed()
    {
        CombatScene scene = GadgetKit.Scene();
        SMineProx mine = scene.Level.SpawnActor(new SMineProx(), new FVector(400f, 0f, 50f), default)!;
        scene.World.Step();
        Burst(scene, new FVector(0f, 0f, 50f));
        Assert.Equal(GadgetStates.s_Activated, mine.GetStateName());
    }
}
