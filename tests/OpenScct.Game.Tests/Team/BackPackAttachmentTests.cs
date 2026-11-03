using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Objectives;
using Xunit;

namespace OpenScct.Game.Tests.Team;

public sealed class BackPackAttachmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CarriedBackpackSurvivesSelectionChangesAndIsRemovedWithGadget(bool ownedAtSpawn)
    {
        var world = new ObjWorld();
        var pawn = (SBasePawn)world.AddPlayer(false, default).Pawn!;
        var gadget = world.Level.SpawnActor(new SGadBackPack(), default, default,
            ownedAtSpawn ? pawn : null, ownedAtSpawn ? pawn : null)!;
        gadget.GiveTo(pawn);
        var bag = Assert.IsType<SBackPack>(gadget.DummyBackPack);
        Assert.Same(pawn, bag.Instigator);
        Assert.Same(pawn, bag.Base);
        Assert.Equal(K.BackPackBone, bag.AttachmentBone);

        pawn.SelectedGadget = gadget;
        gadget.Load();
        gadget.Unload();
        pawn.SelectedGadget = null;
        gadget.GiveTo(pawn);
        Assert.Same(bag, gadget.DummyBackPack);
        Assert.False(bag.bHidden);
        Assert.False(bag.bDeleteMe);

        PawnInventory.DeleteGadget(pawn, gadget);
        Assert.True(bag.bDeleteMe);
    }
}
