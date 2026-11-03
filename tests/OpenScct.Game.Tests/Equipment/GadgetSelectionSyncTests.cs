using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class GadgetSelectionSyncTests
{
    [Theory]
    [InlineData("next", 1)]
    [InlineData("previous", 2)]
    [InlineData("slot", 2)]
    public void OwningClientSendsOneFinalSelectionAndAuthorityAppliesIt(string action, int expected)
    {
        var scene = GadgetKit.Scene();
        var (pc, pawn) = scene.Player(true, default);
        SGadget[] items = [GadgetKit.Gadget(scene, pawn, new SGadChaffeGrenade()),
            GadgetKit.Gadget(scene, pawn, new SGadSmokeGrenade()), GadgetKit.Gadget(scene, pawn, new SGadStickyCam())];
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        pawn.SelectedGadget = items[0];
        pawn.Role = ENetRole.ROLE_AutonomousProxy;
        switch (action)
        {
            case "next": PawnInventory.NextGadget(pawn); break;
            case "previous": PawnInventory.PrevGadget(pawn); break;
            default: PawnInventory.SelectGagdet(pawn, 2); break;
        }
        var sent = Assert.Single(net.ToServer, c => c.Call.Function == new Name("ServerSelectedGadget"));
        Assert.Same(items[expected], sent.Call.Actor1);
        Assert.Same(pawn, sent.Call.Target);
        pawn.Role = ENetRole.ROLE_Authority;
        pawn.SelectedGadget = items[0];
        pc.ReceiveCombatCall(sent.Call, true);
        Assert.Same(items[expected], pawn.SelectedGadget);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("unlisted")]
    [InlineData("pad-up")]
    [InlineData("dead")]
    [InlineData("old-pawn")]
    [InlineData("stunned")]
    public void SelectionPacketsCannotSelectAnUnavailableInventoryItem(string invalid)
    {
        var scene = GadgetKit.Scene();
        var (pc, pawn) = scene.Player(true, default);
        SGadget first = GadgetKit.Gadget(scene, pawn, new SGadChaffeGrenade());
        SGadget requested = GadgetKit.Gadget(scene, pawn, new SGadSmokeGrenade());
        if (invalid == "foreign")
        {
            (_, SBasePawn other) = scene.Player(true, new FVector(1000f, 0f, 0f));
            requested = GadgetKit.Gadget(scene, other, new SGadSmokeGrenade());
        }
        if (invalid == "unlisted") requested = scene.Give(new SGadSmokeGrenade(), pawn);
        if (invalid == "pad-up") requested = GadgetKit.Gadget(scene, pawn, new SGadTorcheLaser());
        if (invalid == "dead") pawn.Health = 0;
        if (invalid == "old-pawn") pc.Pawn = null;
        if (invalid == "stunned") pawn.bStickyShocking = true;
        pawn.SelectedGadget = first;
        pc.ReceiveCombatCall(new CombatCall(new Name("ServerSelectedGadget")) { Target = pawn, Actor1 = requested }, true);
        Assert.Same(first, pawn.SelectedGadget);
    }
}
