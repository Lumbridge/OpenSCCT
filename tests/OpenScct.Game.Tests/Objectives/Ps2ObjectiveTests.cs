using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Content.Collision;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class Ps2ObjectiveTests
{
    private static readonly AssetRef Vial = new("PS2_Objectives.OriginalVial");
    private static readonly FVector ConsoleAt = new(100f, 200f, 300f);
    private static readonly FRotator ConsoleRotation = new(0, 16384, 0);

    private static (ObjWorld World, SFlag Flag, SComputerObjectiveTrigger Console, SFlagDropZone Zone, SObjective Objective) Make()
    {
        var world = new ObjWorld();
        SFlag? flag = null;
        SComputerObjectiveTrigger? console = null;
        SFlagDropZone? zone = null;
        SObjective? objective = null;
        world.Load(w =>
        {
            console = w.Place(new SComputerObjectiveTrigger { bPS2Objective = true, bHidden = false }, ConsoleAt, ConsoleRotation);
            zone = w.Place(new SFlagDropZone
            {
                bPS2Objective = true,
                StaticMesh = AssetRef.None,
                bHidden = true,
                CollisionRadius = 100f,
                CollisionHeight = 100f,
            }, new FVector(1000f, 1000f, 300f));
            flag = new SFlag { bPS2Objective = true, Ps2Computer = console, StaticMesh = Vial, ResetTime = 15 };
            flag.DropZone[0] = zone;
            w.Place(flag, default);
            objective = new SObjective { ObjectiveName = "Original PS2 extraction" };
            objective.Triggers.Add(flag);
            w.Place(objective, default);
        });
        objective!.Reset();
        return (world, flag!, console!, zone!, objective);
    }

    [Fact]
    public void Original_vial_and_touch_volume_survive_begin_play_and_round_reset()
    {
        var (_, flag, console, zone, _) = Make();
        Assert.Equal(Vial, flag.StaticMesh);
        Assert.Null(flag.Server);
        Assert.NotNull(flag.DefDropZone);
        Assert.True(flag.DefDropZone!.bPS2Objective);
        Assert.Equal((100f, 100f), (flag.DefDropZone.CollisionRadius, flag.DefDropZone.CollisionHeight));
        Assert.Null(flag.DefDropZone.Cover);
        Assert.Null(flag.DefDropZone.InteractionObj);
        Assert.Equal(ConsoleAt + new FVector(0f, 0f, 25f), flag.Location);
        Assert.Equal(ConsoleRotation, flag.Rotation);
        Assert.Equal(flag.Location, flag.InitialLocation);
        Assert.Equal(ConsoleAt + new FVector(0f, 80f, 0f), flag.PickupInteraction!.Location);
        Assert.Equal((byte)5, console.Ps2VisualState);
        Assert.Null(zone.Cover);
        Assert.Null(zone.InteractionObj);
        Assert.True(zone.bHidden);
        Assert.Equal((100f, 100f), (zone.CollisionRadius, zone.CollisionHeight));
        flag.Reset();
        Assert.Equal(Vial, flag.StaticMesh);
    }

    [Fact]
    public void Alarm_retracts_vial_then_unlock_restores_it_and_preserves_original_mesh()
    {
        var (world, flag, console, _, _) = Make();
        flag.eventLock();
        Assert.False(flag.bPickable);
        Assert.False(flag.PickupInteraction!.bCanPickup);
        Assert.Equal((byte)6, console.Ps2VisualState);
        flag.eventTick(1f);
        Assert.Equal(ConsoleAt.Z + 10f, flag.Location.Z);
        flag.eventUnLock();
        Assert.Equal((byte)5, console.Ps2VisualState);
        world.Run(1.01f);
        Assert.True(flag.bPickable);
        Assert.True(flag.PickupInteraction.bCanPickup);
        world.Run(2f);
        Assert.InRange(flag.Location.Z, ConsoleAt.Z + 25f, ConsoleAt.Z + 25.3f);
        Assert.Equal(Vial, flag.StaticMesh);
    }

    [Fact]
    public void Spy_carries_vial_and_touching_destination_completes_extraction_without_PC_case_animation()
    {
        var (world, flag, _, zone, objective) = Make();
        TestPC spy = world.AddPlayer(true, flag.Location);
        DiskCarryRules.PickupFlag(spy, flag);
        Assert.Same(flag, ((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Equal(Vial, flag.StaticMesh);
        Assert.Equal(SFlag.Ps2CarryOffset, flag.RelativeLocation);
        Assert.Equal(new Name("B R Thigh"), flag.AttachmentBone);
        Assert.Equal(SFlag.Ps2CarryRotation, flag.RelativeRotation);
        zone.eventTouch(spy.Pawn!);
        Assert.True(objective.ObjectiveTaken);
        Assert.Null(((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Null(flag.FlagCarrier);
        Assert.Null(zone.Cover);
        Assert.Equal(Vial, flag.StaticMesh);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dropped_source_vial_returns_by_timeout_or_merc_touch_without_restoring_PC_mesh(bool mercReturns)
    {
        var (world, flag, console, _, _) = Make();
        TestPC spy = world.AddPlayer(true, flag.Location);
        DiskCarryRules.PickupFlag(spy, flag);
        DiskCarryRules.DiscardFlag(spy);
        FVector dropped = ConsoleAt + new FVector(300f, 100f, 0f);
        flag.SetLocation(dropped);
        flag.eventStoppedMoving();
        Assert.Null(((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Null(flag.FlagCarrier);
        Assert.True(flag.AttachmentBone.IsNone);
        Assert.False(flag.bPickable);
        if (mercReturns)
        {
            TestPC merc = world.AddPlayer(false, dropped);
            ObjWorld.Touch(merc, flag);
            Assert.Null(((SBasePawn)merc.Pawn!).CarriedFlag);
        }
        else world.Run(flag.ResetTime + .1f);
        Assert.Equal(ConsoleAt + new FVector(0f, 0f, 25f), flag.Location);
        Assert.Equal(Vial, flag.StaticMesh);
        Assert.Equal(Vial, flag.InitialStaticMesh);
        Assert.Equal(Vial, flag.Ps2SourceStaticMesh);
        Assert.True(flag.PickupInteraction!.bCanPickup);
        Assert.Equal((byte)5, console.Ps2VisualState);
    }

    [Fact]
    public void Another_spy_touches_dropped_source_vial_and_uses_original_right_thigh()
    {
        var (world, flag, _, _, _) = Make();
        TestPC first = world.AddPlayer(true, flag.Location);
        DiskCarryRules.PickupFlag(first, flag);
        DiskCarryRules.DiscardFlag(first);
        FVector dropped = ConsoleAt + new FVector(300f, 100f, 0f);
        flag.SetLocation(dropped);
        flag.eventStoppedMoving();
        TestPC second = world.AddPlayer(true, dropped);
        ObjWorld.Touch(second, flag);
        Assert.Same(flag, ((SBasePawn)second.Pawn!).CarriedFlag);
        Assert.Equal(new Name("B R Thigh"), flag.AttachmentBone);
        Assert.Equal(SFlag.Ps2CarryOffset, flag.RelativeLocation);
        Assert.Equal(SFlag.Ps2CarryRotation, flag.RelativeRotation);
        Assert.Equal(Vial, flag.StaticMesh);
    }

    [Fact]
    public void Source_material_slots_and_animation_direction_do_not_modify_PC_screen_slot()
    {
        var (_, _, console, _, _) = Make();
        AssetRef active = new("PS2_Objectives.Active");
        AssetRef inactive = new("PS2_Objectives.Inactive");
        AssetRef screen = new("PS2_Objectives.SlotOne");
        console.Skins.Clear();
        console.Skins.Add(AssetRef.None);
        console.Skins.Add(screen);
        console.Ps2SkinActive = active;
        console.Ps2SkinInactive = inactive;
        console.Ps2CtfAnimation = new Name("SourceCtf");
        console.Ps2SetVisualState(6, 4f);
        Assert.Equal(inactive, console.Skins[2]);
        Assert.Equal(screen, console.Skins[1]);
        Assert.Equal(new Name("SourceCtf"), console.Ps2AnimationSequence);
        Assert.True(console.Ps2AnimationBackward);
        Assert.True(console.Ps2AnimationContinue);
        console.Ps2SetVisualState(5, 4f);
        Assert.Equal(active, console.Skins[2]);
        Assert.False(console.Ps2AnimationBackward);
    }

    [Fact]
    public void Collision_overlap_delivers_carried_vial_without_direct_touch_dispatch()
    {
        var (world, flag, _, zone, objective) = Make();
        TestPC spy = world.AddPlayer(true, zone.Location - new FVector(300f, 0f, 0f));
        DiskCarryRules.PickupFlag(spy, flag);
        CollisionWorld collision = CollisionWorld.Build(new CollisionDataBuilder().Build());
        collision.BindLevel(world.Level);
        var physics = new PhysicsDriver(collision);
        world.Level.PhysicsDriver = physics;
        Assert.False(objective.ObjectiveTaken);
        physics.MoveActor(spy.Pawn!, new FVector(300f, 0f, 0f));
        Assert.True(objective.ObjectiveTaken);
        Assert.Null(((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Equal(Vial, flag.StaticMesh);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_Use_menu_enters_source_pickup_states_and_standspot_before_carrying(bool crouched)
    {
        var (world, flag, _, _, _) = Make();
        var pc = world.Level.Spawn<SPlayerController>()!;
        pc.RemoteRole = ENetRole.ROLE_None;
        var pawn = world.Level.Spawn<SPawnAttaque>(ConsoleAt + new FVector(0f, 100f, 40f), new FRotator(0, -16384, 0))!;
        pawn.Health = 100; pawn.Controller = pc; pc.Pawn = pawn; pc.SBPawn = pawn;
        pc.SPawnType = pawn.SPawnType;
        pc.PlayerReplicationInfo = new PlayerReplicationInfo { Team = world.Gri.Teams[0], Stats = new USPlayerStats() };
        world.Level.Info.LocalPlayerController = pc;
        InteractionMenuRules.EnsureManager(pc);
        pc.GotoState(K.s_PlayerWalkingSt0H);
        pawn.bIsCrouched = crouched;
        var entry = flag.PickupInteraction!;
        Assert.Equal((50f, 50f), (entry.CollisionRadius, entry.CollisionHeight));
        ObjWorld.Touch(pc, entry);
        Assert.Equal(EMenuRelease.InteractionInitSent, ObjWorld.UseEntry(pc, entry));
        Assert.True(pc.IsInState(K.s_Pickup_GoTo));
        Assert.False(pawn.bUseAdditionnalPhysicsForRepos);
        Assert.Equal(ConsoleAt + new FVector(0f, 80f, 40f + (crouched ? 27.5f : 0f)), pawn.vVandammePos);
        pc.eventTopSynchro();
        Assert.True(pc.IsInState(K.s_Pickup));
        Assert.Equal(crouched ? SBasePawn.SA_PICKUP_CR_UP : SBasePawn.SA_PICKUP_ST_UP, pawn.sAction);
        Assert.Same(flag, pawn.CarriedFlag);
        pc.GotoState(K.s_PlayerWalkingSt0H);
        Assert.Same(pawn, flag.Base);
        Assert.Equal(SFlag.Ps2CarryOffset, flag.RelativeLocation);
        Assert.Equal(SFlag.Ps2CarryRotation, flag.RelativeRotation);
        Assert.Equal(Vial, flag.StaticMesh);
    }
}
