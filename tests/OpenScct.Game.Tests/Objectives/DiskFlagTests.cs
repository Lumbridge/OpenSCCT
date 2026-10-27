using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/disk-flag.md
public sealed class DiskFlagTests
{
    private static readonly FVector Home = new(-4452.3f, 261.6f, -1052f);
    private static readonly FVector ZoneAt = new(-1575f, 1700f, -92f);

    private sealed class Setup
    {
        public required ObjWorld W;
        public required SFlag Flag;
        public required SFlagDropZone Zone;
        public required SObjective Objective;

        public SPickupInteraction Pickup => Flag.PickupInteraction!;

        public SFlagDropZoneInteraction Drop => (SFlagDropZoneInteraction)Zone.InteractionObj!;
    }

    // The SteeE disk: home on its rack facing -X, one drop zone, ResetTime 15 (values from the spec table).
    private static Setup Make()
    {
        var w = new ObjWorld();
        SFlag? flag = null;
        SFlagDropZone? zone = null;
        SObjective? objective = null;
        w.Load(x =>
        {
            zone = x.Place(new SFlagDropZone { fInteractionRadius = 50f, fInteractionHeight = 30f, TriggerName = "Secret Files" }, ZoneAt);
            flag = new SFlag { ResetTime = 15, TriggerName = "Secret Files", fInteractionRadius = 50f, fInteractionHeight = 30f };
            flag.DropZone[0] = zone;
            x.Place(flag, Home, new FRotator(0, -32768, 0));
            objective = new SObjective { ObjectiveName = "Secret Files" };
            objective.Triggers.Add(flag);
            x.Place(objective, default);
        });
        objective!.Reset();
        return new Setup { W = w, Flag = flag!, Zone = zone!, Objective = objective };
    }

    // The disk taken from home by a spy through the menu, the pickup animation and its end.
    private static TestPC Carrier(Setup s)
    {
        TestPC spy = s.W.AddPlayer(spy: true, Home + new FVector(-67f, 0f, 0f), new FRotator(0, 0, 0));
        ObjWorld.Touch(spy, s.Pickup);
        Assert.Equal(EMenuRelease.InteractionInitSent, ObjWorld.UseEntry(spy, s.Pickup));
        Assert.True(spy.IsInState(K.s_FlagAndDropZone_GoTo));
        Assert.Equal(EFlagWalkEnd.Pickup, DiskCarryRules.WalkEnd(spy));
        spy.GotoState(K.s_Pickup);
        DiskCarryRules.AnimNotifyPickup((SBasePawn)spy.Pawn!);
        spy.GotoState(K.s_PlayerWalkingSt0H);
        return spy;
    }

    // The disk dropped by its carrier and come to rest at a spot away from home.
    private static void DropAt(Setup s, TestPC carrier, FVector at)
    {
        DiskCarryRules.DiscardFlag(carrier);
        s.Flag.SetLocation(at);
        s.Flag.eventStoppedMoving();
    }

    // ---- setup (rules 1 to 7) -----------------------------------------------------------------------------------

    [Fact]
    public void PostBeginPlay_puts_the_disk_on_its_rack_with_a_pickup_entry()
    {
        Setup s = Make();

        Assert.NotNull(s.Flag.Server);
        Assert.Equal(new AssetRef("SGameplayObjects.Extraction_SM.Server"), s.Flag.Server!.StaticMesh);
        Assert.True(s.Flag.Server.bBlockPlayers);
        Assert.Equal(SFlag.DiskMesh, s.Flag.StaticMesh);
        Assert.Equal(Home, s.Flag.InitialLocation);
        Assert.Null(s.Flag.DefDropZone); // a run-time spawn of a bNoDelete class fails
        Assert.True(s.Pickup.bInteractInFront);
        Assert.Equal((5f + 50f, 16f + 10f), (s.Pickup.ItRadius, s.Pickup.ItHeight));
        Assert.Equal(Usable.U_SPAWNOFFENSE, s.Pickup.UsableBy);
    }

    [Fact]
    public void Drop_zone_has_its_lid_and_a_closed_drop_entry()
    {
        Setup s = Make();

        Assert.NotNull(s.Zone.Cover);
        Assert.Equal(SFlagDropZone.LidMesh, s.Zone.Cover!.StaticMesh);
        Assert.Equal(ZoneAt + new FVector(-10f, 0f, 10f), s.Zone.Cover.Location);
        Assert.Equal(new FRotator(20000, 0, 0), s.Zone.Cover.RotationRate);
        Assert.True(s.Zone.bBlockActors);
        Assert.False(s.Zone.bBlockPlayers);
        Assert.Equal(25, s.Drop.iPriority);
        Assert.False(s.Drop.bUsable);
        Assert.True(s.Drop.bInteractInFront);
        Assert.Equal((50f, 30f), (s.Drop.CollisionRadius, s.Drop.CollisionHeight));
    }

    [Fact]
    public void Reset_sends_the_disk_home_touchable_and_takeable()
    {
        Setup s = Make();

        Assert.True(s.Flag.bPickable);
        Assert.True(s.Pickup.bCanPickup);
        Assert.True(s.Flag.bActive);
        Assert.True(s.Flag.bCollideActors);
        Assert.False(s.Flag.bBlockActors);
        Assert.False(s.Flag.bHidden);
        Assert.Equal(Home, s.Flag.Location);
        Assert.True(s.Flag.IsInState(K.s_Initial));
    }

    // ---- taking the disk from home (rules 8 to 15) ---------------------------------------------------------------

    [Fact]
    public void Spy_takes_the_disk_from_home_through_the_menu()
    {
        Setup s = Make();
        TestPC merc = s.W.AddPlayer(spy: false, new FVector(0f, 0f, 0f));
        TestPC spy = Carrier(s);
        var pawn = (SBasePawn)spy.Pawn!;

        Assert.Same(s.Flag, pawn.CarriedFlag);
        Assert.Null(pawn.HandItem);
        Assert.Same(pawn, s.Flag.FlagCarrier);
        Assert.True(s.Flag.FlagTaken);
        Assert.False(s.Flag.bActive);
        Assert.True(s.Flag.bTriggering);
        Assert.True(s.Flag.IsInState(K.s_PickedUp));
        Assert.Same(pawn, s.Flag.Base);
        Assert.Equal(K.BoneThigh, s.Flag.AttachmentBone);
        Assert.Equal(K.ThighRotationMenu, s.Flag.RelativeRotation);
        Assert.Contains(("FlagCarrier", (Actor)s.Flag, true), s.W.Net.Multicasts);
        Assert.Equal(2, s.W.Net.Count(K.MsgDataStolen));
        Assert.Same(spy, Assert.Single(s.W.Net.To(K.MsgGoToDropZone)));
        Assert.Same(merc, Assert.Single(s.W.Net.To(K.MsgRetrieveHardDrive)));
        Assert.True(s.Zone.bTriggering);
        Assert.True(s.Drop.bUsable);
        Assert.Equal(new FRotator(5000, 0, 0), s.Zone.Cover!.DesiredRotation);
        Assert.DoesNotContain(s.W.Net.Multicasts, m => m.Call == "Radar" && m.Flag); // no merc radar marker for the disk
    }

    [Fact]
    public void Stand_spot_is_67_in_front_of_the_disk_looking_at_it()
    {
        Setup s = Make();
        var pawn = new SPawnAttaque { Location = new FVector(0f, 0f, -1052f) };

        SFlag.GetStandSpot(s.Pickup, pawn, out FVector spot, out FVector facing);

        Assert.Equal(-4519.3f, spot.X, 2);
        Assert.Equal(261.6f, spot.Y, 2);
        Assert.Equal(-1052f, spot.Z);
        Assert.True(facing.X > 0.99f);
    }

    [Fact]
    public void A_disk_at_home_ignores_touches()
    {
        Setup s = Make();
        TestPC spy = s.W.AddPlayer(spy: true, Home);

        ObjWorld.Touch(spy, s.Flag);

        Assert.Null(((SBasePawn)spy.Pawn!).CarriedFlag);
    }

    [Fact]
    public void A_merc_may_not_use_the_pickup_entry()
    {
        Setup s = Make();
        TestPC merc = s.W.AddPlayer(spy: false, Home + new FVector(-67f, 0f, 0f), new FRotator(0, 0, 0));

        Assert.False(s.Pickup.CanUse(merc));
    }

    // ---- dropping and the dropped disk (rules 16 to 24) ----------------------------------------------------------

    [Fact]
    public void Discarding_drops_the_disk_and_closes_the_zone_entry()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);
        s.W.Net.Messages.Clear();

        DiskCarryRules.DiscardFlag(spy);

        Assert.Null(((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Null(s.Flag.FlagCarrier);
        Assert.False(s.Flag.FlagTaken);
        Assert.True(s.Flag.bResetZeroValues);
        Assert.False(s.Flag.bTriggering);
        Assert.True(s.Flag.IsInState(K.s_Flying));
        Assert.Null(s.Flag.Base);
        Assert.Equal(1, s.W.Net.Count(K.MsgDataDropped));
        Assert.False(s.Zone.bTriggering);
        Assert.False(s.Drop.bUsable);
        Assert.Equal(new FRotator(5000, 0, 0), s.Zone.Cover!.DesiredRotation); // the lid stays open
    }

    [Fact]
    public void A_resting_disk_cannot_be_taken_from_the_menu_and_goes_home_after_ResetTime()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);
        DropAt(s, spy, new FVector(0f, 0f, 0f));

        Assert.True(s.Flag.bActive);
        Assert.False(s.Flag.bPickable);
        Assert.False(s.Pickup.bCanPickup);
        Assert.True(s.Flag.IsInState(K.s_Initial));

        s.W.Run(14.9f);
        Assert.NotEqual(Home, s.Flag.Location);
        s.W.Run(0.2f);
        Assert.Equal(Home, s.Flag.Location);
        Assert.True(s.Flag.bPickable);
        Assert.Equal(0, s.W.Net.Count(K.MsgDataSecured));
    }

    [Fact]
    public void A_merc_touch_returns_the_disk_and_credits_him()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);
        DropAt(s, spy, new FVector(0f, 0f, 0f));
        TestPC merc = s.W.AddPlayer(spy: false, new FVector(0f, 0f, 0f));

        ObjWorld.Touch(merc, s.Flag);

        Assert.Equal(Home, s.Flag.Location);
        Assert.Equal(2, s.W.Net.Count(K.MsgDataSecured));
        Assert.Equal(1, merc.PlayerReplicationInfo!.Stats!.Merc_SecureDisks);
    }

    [Fact]
    public void A_spy_touch_takes_the_resting_disk_at_once()
    {
        Setup s = Make();
        TestPC first = Carrier(s);
        DropAt(s, first, new FVector(0f, 0f, 0f));
        TestPC second = s.W.AddPlayer(spy: true, new FVector(0f, 0f, 0f));

        ObjWorld.Touch(second, s.Flag);

        var pawn = (SBasePawn)second.Pawn!;
        Assert.Same(s.Flag, pawn.CarriedFlag);
        Assert.Same(pawn, s.Flag.FlagCarrier);
        Assert.Equal(K.BoneThigh, s.Flag.AttachmentBone);
        Assert.Equal(K.ThighRotationTouch, s.Flag.RelativeRotation);
        Assert.True(second.IsInState(K.s_PlayerWalkingSt0H));
        Assert.True(s.Drop.bUsable);
    }

    [Fact]
    public void Dead_or_non_player_pawns_do_not_pick_up_or_return()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);
        DropAt(s, spy, new FVector(0f, 0f, 0f));
        TestPC dead = s.W.AddPlayer(spy: false, new FVector(0f, 0f, 0f));
        dead.Pawn!.Health = 0;
        var stray = s.W.Level.Spawn<SPAWNDEFENSE>()!;
        stray.Health = 100;

        ObjWorld.Touch(dead, s.Flag);
        stray.BeginTouch(s.Flag);

        Assert.NotEqual(Home, s.Flag.Location);
    }

    [Fact]
    public void A_disk_resting_above_its_home_spot_cannot_be_touched_but_still_goes_home()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);
        DropAt(s, spy, Home + new FVector(0f, 0f, 30f));
        TestPC merc = s.W.AddPlayer(spy: false, s.Flag.Location);

        ObjWorld.Touch(merc, s.Flag);
        Assert.Equal(0, s.W.Net.Count(K.MsgDataSecured));

        s.W.Run(15.2f);
        Assert.Equal(Home, s.Flag.Location);
    }

    [Fact]
    public void Timer_return_credits_the_carriers_killer()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);
        TestPC merc = s.W.AddPlayer(spy: false, new FVector(500f, 0f, 0f));
        DropAt(s, spy, new FVector(0f, 0f, 0f));
        s.Flag.CarrierKiller = merc;

        s.W.Run(15.2f);

        Assert.Equal(1, merc.PlayerReplicationInfo!.Stats!.Merc_SecureDisks);
        Assert.Null(s.Flag.CarrierKiller);
    }

    // ---- delivery (rules 25 to 30) ------------------------------------------------------------------------------

    private static TestPC CarrierAtZone(Setup s)
    {
        TestPC spy = Carrier(s);
        spy.Pawn!.Location = ZoneAt + new FVector(67f, 0f, 0f);
        spy.Pawn.Rotation = new FRotator(0, 32768, 0);
        ObjWorld.Touch(spy, s.Drop);
        return spy;
    }

    [Fact]
    public void Drop_entry_is_usable_only_by_a_carrier_of_a_listed_disk()
    {
        Setup s = Make();
        TestPC other = s.W.AddPlayer(spy: true, ZoneAt + new FVector(67f, 0f, 0f), new FRotator(0, 32768, 0));
        TestPC spy = CarrierAtZone(s);

        Assert.True(s.Drop.CanUse(spy));
        Assert.False(s.Drop.CanUse(other));
    }

    [Fact]
    public void Delivery_takes_the_objective_when_the_drop_animation_ends()
    {
        Setup s = Make();
        TestPC spy = CarrierAtZone(s);
        s.W.Net.Messages.Clear();

        Assert.Equal(EMenuRelease.InteractionInitSent, ObjWorld.UseEntry(spy, s.Drop));
        Assert.True(spy.IsInState(K.s_FlagAndDropZone_GoTo));
        Assert.Same(spy, s.Flag.ControllerWhoTakesIt);
        Assert.Equal(EFlagWalkEnd.PutInDropZone, DiskCarryRules.WalkEnd(spy));
        spy.GotoState(K.s_PutFlagInDropZone);
        Assert.Equal(K.BoneHand, s.Flag.AttachmentBone);
        Assert.False(s.Objective.ObjectiveTaken);

        spy.GotoState(K.s_PlayerWalkingSt0H);

        Assert.True(s.Objective.ObjectiveTaken);
        Assert.Null(((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Equal(1, s.Flag.finalDropZone);
        Assert.Equal(1, s.W.Net.Count(K.MsgDataCaptured));
        Assert.Equal(0, s.W.Net.Count(K.MsgDataDropped)); // taken: the throw says nothing
        Assert.Single(spy.PlayerReplicationInfo!.Stats!.Spy_ObjectivesTaken);

        s.W.Run(1.6f);
        Assert.Equal(s.Zone.Rotation, s.Zone.Cover!.DesiredRotation); // the lid closed 1.5 s after the use
    }

    [Fact]
    public void Damage_during_the_drop_cancels_the_delivery()
    {
        Setup s = Make();
        TestPC spy = CarrierAtZone(s);
        ObjWorld.UseEntry(spy, s.Drop);
        spy.GotoState(K.s_PutFlagInDropZone);

        DiskCarryRules.PutFlagTakeDamage(spy);

        Assert.True(spy.IsInState(K.s_PlayerWalkingSt0H));
        Assert.False(s.Objective.ObjectiveTaken);
        Assert.Same(s.Flag, ((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Equal(K.BoneThigh, s.Flag.AttachmentBone);
    }

    [Fact]
    public void A_zone_the_disk_does_not_list_refuses_the_delivery()
    {
        Setup s = Make();
        TestPC spy = CarrierAtZone(s);
        s.Flag.DropZone[0] = null;
        spy.PreviousState = K.s_PlayerWalkingCr0H;

        InteractionMenuRules.InteractionInit(spy, s.Drop, 0);

        Assert.True(spy.IsInState(K.s_PlayerWalkingCr0H));
        Assert.False(s.Flag.CheckDropZone(s.Zone));
    }

    [Fact]
    public void LeaveCarrier_drops_the_disk()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);

        s.Flag.LeaveCarrier();

        Assert.Null(((SBasePawn)spy.Pawn!).CarriedFlag);
        Assert.Null(s.Flag.FlagCarrier);
    }

    // ---- locks (rule 31) -----------------------------------------------------------------------------------------

    [Fact]
    public void Lock_at_home_closes_the_menu_and_UnLock_reopens_it_a_second_later()
    {
        Setup s = Make();

        s.Flag.eventLock();
        Assert.True(s.Flag.IsInState(K.s_TemporaryInactive));
        Assert.True(s.Flag.bLocked);
        Assert.False(s.Pickup.bCanPickup);

        s.Flag.eventUnLock();
        s.W.Run(0.9f);
        Assert.True(s.Flag.bLocked);
        s.W.Run(0.2f);

        Assert.False(s.Flag.bLocked);
        Assert.True(s.Flag.bPickable);
        Assert.True(s.Pickup.bCanPickup);
        Assert.True(s.Flag.IsInState(K.s_Initial));
    }

    [Fact]
    public void Lock_while_carried_waits_for_the_next_reset()
    {
        Setup s = Make();
        TestPC spy = Carrier(s);

        s.Flag.eventLock();
        Assert.True(s.Flag.bShouldLock);
        Assert.False(s.Flag.IsInState(K.s_TemporaryInactive));

        DropAt(s, spy, new FVector(0f, 0f, 0f));
        s.Flag.eventReset();

        Assert.True(s.Flag.IsInState(K.s_TemporaryInactive));
        Assert.False(s.Flag.bPickable);
    }
}
