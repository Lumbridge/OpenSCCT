using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class Ps2InteractionFacingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(16384)]
    [InlineData(32768)]
    [InlineData(49152)]
    public void Ps2_console_remains_usable_when_crossing_its_offset_touch_cylinder(int yaw)
    {
        var (world, computer, entry) = Make(ps2: true, yaw);
        FVector front = computer.Rotation.Vector();
        var pc = world.AddPlayer(true, computer.Location + front * 110f, new FRotator(0, yaw + 32768, 0));
        foreach (float distance in new[] { 110f, 75f, 40f, 110f })
        {
            pc.Pawn!.Location = computer.Location + front * distance;
            Assert.True(entry.CanUse(pc));
        }

        // The touch geometry is unchanged; facing uses the case rather than moving or broadening the cylinder.
        Assert.True(FVector.Dist(computer.Location + front * 80f, entry.Location) < .001f);
    }

    [Fact]
    public void Ps2_console_uses_its_replicated_backlink_without_native_owner()
    {
        var (world, computer, entry) = Make(ps2: true);
        var pc = world.AddPlayer(true, computer.Location + new FVector(75f, 0, 0), new FRotator(0, 32768, 0));
        entry.SetOwner(null);
        Assert.True(entry.CanUse(pc));
    }

    [Fact]
    public void Ps2_console_still_rejects_back_side_away_facing_locked_and_inactive()
    {
        var (world, computer, entry) = Make(ps2: true);
        var pc = world.AddPlayer(true, computer.Location - new FVector(40f, 0, 0));
        Assert.False(entry.CanUse(pc));
        pc.Pawn!.Location = computer.Location + new FVector(40f, 0, 0);
        Assert.False(entry.CanUse(pc));
        pc.Pawn.Rotation = new FRotator(0, 32768, 0);
        Assert.True(entry.CanUse(pc));
        computer.bLocked = true;
        Assert.False(entry.CanUse(pc));
        computer.bLocked = false;
        computer.bActive = false;
        Assert.False(entry.CanUse(pc));
    }

    [Fact]
    public void Pc_console_keeps_interaction_origin_facing_rule()
    {
        var (world, computer, entry) = Make(ps2: false);
        // Move only the test entry to expose the two possible origins, without changing stock defaults.
        entry.Location = computer.Location + new FVector(80f, 0, 0);
        var pc = world.AddPlayer(true, computer.Location + new FVector(75f, 0, 0), new FRotator(0, 32768, 0));
        Assert.False(entry.CanUse(pc));
        pc.Pawn!.Location = computer.Location + new FVector(110f, 0, 0);
        Assert.True(entry.CanUse(pc));
    }

    private static (ObjWorld World, SComputerObjectiveTrigger Computer, SComputerInteraction Entry) Make(bool ps2, int yaw = 0)
    {
        var world = new ObjWorld();
        SComputerObjectiveTrigger? computer = null;
        world.Load(w => computer = w.Place(new SComputerObjectiveTrigger { bPS2Objective = ps2, bActive = true },
            new FVector(100, 200, 300), new FRotator(0, yaw, 0)));
        return (world, computer!, Assert.IsType<SComputerInteraction>(computer!.InteractionObj));
    }

    [Fact]
    public void Centered_panel_keeps_its_small_touch_cylinder_and_front_rule()
    {
        var world = new ObjWorld();
        SInterrupteur? panel = null;
        world.Load(w => panel = w.Place(new SInterrupteur
        {
            bInteractInFront = true, fInteractionRadius = 10, fInteractionHeight = 10,
            UsableBy = Usable.U_SPAWNOFFENSE,
        }, new FVector(100, 200, 300)));
        var entry = Assert.IsType<STriggerInteraction>(panel!.InteractionObj);
        var pc = world.AddPlayer(true, panel.Location + new FVector(40, 0, 0), new FRotator(0, 32768, 0));
        Assert.True(entry.CanUse(pc));
        Assert.Equal(panel.Location, entry.Location);
        Assert.Equal((10f, 10f), (entry.ItRadius, entry.ItHeight));
        pc.Pawn!.Location = panel.Location - new FVector(40, 0, 0);
        pc.Pawn.Rotation = default;
        Assert.False(entry.CanUse(pc));
    }
}
