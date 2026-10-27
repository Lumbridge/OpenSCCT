using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The gadget natives (spec/equipment/gadget-natives.md), the default kits (loadouts.md), the gadget pawn actions
/// (execute-action.md) and the gadget calls over the network.
/// </summary>
public sealed class GadgetNativeTests
{
    private sealed class RemoteConnection : UNetConnection
    {
    }

    [Fact]
    public void AGadgetCanBeUsedAfterItsRefireDelayWhileItHasACharge()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        SGadChaffeGrenade chaff = GadgetKit.Gadget(scene, spy, new SGadChaffeGrenade());
        scene.Run(1f);
        Assert.False(chaff.CanUseGadget());
        scene.Run(1.1f);
        Assert.True(chaff.CanUseGadget());
        chaff.LastUsed = scene.Time;
        Assert.False(chaff.CanUseGadget());
        scene.Run(2.1f);
        chaff.Charge = 0;
        Assert.False(chaff.CanUseGadget());
    }

    [Fact]
    public void AnUnspentGadgetFailsTheChargeTest()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SGadGasMask mask = GadgetKit.Gadget(scene, merc, new SGadGasMask());
        scene.Run(1f);
        Assert.Equal(-10, mask.Charge);
        Assert.False(mask.CanUseGadget());
    }

    [Fact]
    public void ChargesGoDownWithoutAFloorAndUp()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        SGadStickyCam cams = GadgetKit.Gadget(scene, spy, new SGadStickyCam());
        cams.Charge = 1;
        cams.DecCharge();
        cams.DecCharge();
        Assert.Equal(-1, cams.Charge);
        cams.AddCharge(3);
        Assert.Equal(2, cams.Charge);
    }

    [Fact]
    public void AClientSpendingAChargeTellsTheServer()
    {
        CombatScene scene = GadgetKit.Scene();
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        (SPlayerController pc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        SGadStickyCam cams = GadgetKit.Gadget(scene, spy, new SGadStickyCam());
        cams.Role = ENetRole.ROLE_AutonomousProxy;
        cams.DecCharge();
        Assert.Equal(4, cams.Charge);
        Assert.Contains(net.ToServer, c => ReferenceEquals(c.From, pc) && c.Call.Function == new Name("ServerDecCharge") && c.Call.Int1 == 1);
    }

    [Fact]
    public void SelectNextSkipsPadUpGadgetsAndTheBomb()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SGadTorche torch = GadgetKit.Gadget(scene, merc, new SGadTorche(), Select: false);
        SGadBomb bomb = GadgetKit.Gadget(scene, merc, new SGadBomb(), Select: false);
        SGadGasMask mask = GadgetKit.Gadget(scene, merc, new SGadGasMask(), Select: false);
        Assert.True(torch.IsAPadUpGadget || !torch.bActivatable);
        Assert.Same(mask, torch.SelectNext());
        Assert.Same(mask, bomb.SelectNext());
        Assert.Same(mask, mask.SelectNext());
    }

    [Fact]
    public void TheDefaultKitsAreTheFourSpyAndFourMercGadgets()
    {
        Assert.Equal(
            ["SBase.SGadSmokeGrenade", "SBase.SGadChaffeGrenade", "SBase.SGadStickyCam", "SBase.SGadHeartBeatSensor"],
            Loadouts.SpyFixedGadgets[..4].Select(c => c.Path));
        Assert.Equal(
            ["SBase.SGadFragGrenade", "SBase.SGadMultiMinesLauncher", "SBase.SGadCamNetwork", "SBase.SGadGasMask"],
            Loadouts.MercFixedGadgets[..4].Select(c => c.Path));
    }

    [Fact]
    public void ABackpackShareGivesOneChargeOfTheSameGadget()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        SGadChaffeGrenade chaff = GadgetKit.Gadget(scene, spy, new SGadChaffeGrenade());
        chaff.Charge = 2;
        pc.TellClientToAddGadgetCharge(SGadget.GetUniqueIDByClass(new ClassRef("SBase.SGadChaffeGrenade")), 1);
        Assert.Equal(3, chaff.Charge);
    }

    [Fact]
    public void TheCamNetworkActionsMarkThePawnAsUsingTheNetwork()
    {
        CombatScene scene = GadgetKit.Scene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        Assert.True(GadgetActions.Execute(merc, SBasePawn.SA_CAMNETWORK_BG));
        Assert.True(merc.bUsingCamNetwork);
        Assert.True(GadgetActions.Execute(merc, SBasePawn.SA_CAMNETWORK_END));
        Assert.False(merc.bUsingCamNetwork);
        merc.IK_Type = IK_Type_.IKT_WEAPONLOADED;
        Assert.True(GadgetActions.Execute(merc, SBasePawn.SA_NO_IK));
        Assert.Equal(IK_Type_.IKT_NONE, merc.IK_Type);
        Assert.False(GadgetActions.Execute(merc, SBasePawn.SA_WALK_ST_0H));
    }

    [Fact]
    public void AClientCamRequestGoesToTheServerAndIsRunThere()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f));
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        (SPlayerController pc, SBasePawn spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        GadgetKit.Shocker(scene, spy);
        SGadStickyCam gad = GadgetKit.Gadget(scene, spy, new SGadStickyCam());
        scene.Run(1.5f);
        pc.Role = ENetRole.ROLE_AutonomousProxy;
        pc.ServerSpawnStickyCam(gad, gad.StickyCamClass, new FVector(297f, 0f, 160f), new FRotator(0, 32768, 0), -1, null);
        (PlayerController from, CombatCall call) = Assert.Single(net.ToServer);
        Assert.Same(pc, from);
        Assert.Empty(GadgetKit.All<SStickyCam>(scene));
        pc.Role = ENetRole.ROLE_Authority;
        pc.ReceiveCombatCall(call, true);
        SStickyCam cam = Assert.Single(GadgetKit.All<SStickyCam>(scene));
        Assert.Equal(new FVector(297f, 0f, 160f), cam.Location);
    }

    [Fact]
    public void ServerToOwnerGadgetCallsReachARemoteOwnerAndRunThere()
    {
        CombatScene scene = GadgetKit.Scene();
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SGadMultiMinesLauncher launcher = GadgetKit.Gadget(scene, merc, new SGadMultiMinesLauncher());
        launcher.Charge = 1;
        pc.Player = new RemoteConnection();
        SMineProx mine = scene.Level.SpawnActor(new SMineProx(), new FVector(100f, 0f, 100f), default)!;
        pc.ReloadMine(mine);
        Assert.Equal(1, launcher.Charge);
        (PlayerController to, CombatCall call) = Assert.Single(net.ToClients);
        Assert.Same(pc, to);
        pc.Player = null;
        pc.ReceiveCombatCall(call, false);
        Assert.Equal(2, launcher.Charge);
    }

    [Fact]
    public void AnOwnerCallOnAnotherPlayersGadgetIsRefused()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _) = scene.Player(false, new FVector(0f, 0f, 100f));
        (_, SBasePawn other) = scene.Player(false, new FVector(300f, 0f, 100f));
        SGadMultiMinesLauncher theirs = GadgetKit.Gadget(scene, other, new SGadMultiMinesLauncher());
        pc.ReceiveCombatCall(new CombatCall(new Name("SpawnMine"))
        {
            Target = theirs,
            Name1 = new Name("SBase.SMineProx"),
            Actor1 = other,
            Vector1 = new FVector(50f, 0f, 100f),
        }, true);
        Assert.Empty(GadgetKit.All<SMineProx>(scene));
    }
}
