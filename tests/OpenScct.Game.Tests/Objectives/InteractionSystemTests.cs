using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/interaction-system.md
public sealed class InteractionSystemTests
{
    private static (ObjWorld w, SBasicGamePlayObject owner) WithOwner(float radius = -1f, float height = -1f, Usable usable = Usable.U_DEFAULT_USABILITY)
    {
        var w = new ObjWorld();
        var owner = w.Level.Spawn<SBasicGamePlayObject>(new FVector(100f, 0f, 0f))!;
        owner.CollisionRadius = 20f;
        owner.CollisionHeight = 30f;
        owner.fInteractionRadius = radius;
        owner.fInteractionHeight = height;
        owner.UsableBy = usable;
        return (w, owner);
    }

    private static T SpawnEntry<T>(ObjWorld w, Actor owner, FRotator rotation = default)
        where T : SInteractObject, new() =>
        ObjectiveNatives.Spawn<T>(owner, owner, owner.Location, rotation) ?? throw new System.InvalidOperationException();

    // ---- rule 3 and 4: the cylinder -----------------------------------------------------------------------------

    [Fact]
    public void Cylinder_uses_the_owners_interaction_size_when_set()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner(radius: 60f, height: 120f);
        var entry = SpawnEntry<STriggerInteraction>(w, owner);

        Assert.Equal(60f, entry.ItRadius);
        Assert.Equal(120f, entry.ItHeight);
        Assert.Equal(60f, entry.CollisionRadius);
        Assert.Equal(120f, entry.CollisionHeight);
        Assert.Same(owner, entry.Base);
    }

    [Fact]
    public void Cylinder_defaults_to_the_owner_collision_plus_150()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);

        Assert.Equal(20f + 150f, entry.ItRadius);
        Assert.Equal(30f + 150f, entry.ItHeight);
    }

    [Fact]
    public void Cylinder_size_is_applied_again_in_PostNetBeginPlay()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner(radius: 60f, height: 120f);
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        entry.CollisionRadius = 1f;
        entry.CollisionHeight = 1f;

        entry.eventPostNetBeginPlay();

        Assert.Equal(60f, entry.CollisionRadius);
        Assert.Equal(120f, entry.CollisionHeight);
    }

    [Theory]
    [InlineData(Usable.U_DEFAULT_USABILITY, Usable.U_BOTH)]
    [InlineData(Usable.U_SPAWNOFFENSE, Usable.U_SPAWNOFFENSE)]
    [InlineData(Usable.U_SPAWNDEFENSE, Usable.U_SPAWNDEFENSE)]
    [InlineData(Usable.U_NO_ONE, Usable.U_NO_ONE)]
    public void UsableBy_is_copied_from_the_owner_unless_default(Usable ownerValue, Usable expected)
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner(usable: ownerValue);
        var entry = SpawnEntry<STriggerInteraction>(w, owner);

        Assert.Equal(expected, entry.UsableBy);
    }

    [Fact]
    public void Pickup_and_drop_zone_entries_resize_after_the_generic_sizing()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner(radius: 50f, height: 30f);
        var pickup = SpawnEntry<SPickupInteraction>(w, owner);
        var drop = SpawnEntry<SFlagDropZoneInteraction>(w, owner);

        Assert.Equal((20f + 50f, 30f + 10f), (pickup.ItRadius, pickup.ItHeight));
        Assert.Equal((70f, 40f), (pickup.CollisionRadius, pickup.CollisionHeight));

        // The 100 x 100 cylinder of PostBeginPlay is replaced by ItRadius x ItHeight in PostNetBeginPlay.
        Assert.Equal((50f, 30f), (drop.ItRadius, drop.ItHeight));
        Assert.Equal((50f, 30f), (drop.CollisionRadius, drop.CollisionHeight));
        Assert.Same(owner, drop.MyOwner);
    }

    // ---- rules 7, 8 and 6: entering and leaving the menu -----------------------------------------------------

    [Fact]
    public void Touch_by_a_player_pawn_adds_the_entry_and_untouch_removes_it()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, new FVector(0f, 0f, 0f));

        ObjWorld.Touch(pc, entry);

        SInteractionManager menu = pc.IManager!;
        Assert.Equal(K.FixedEntries + 1, menu.NbObjects);
        Assert.Same(entry, menu.Interactions[K.FixedEntries]);
        Assert.Contains((entry, K.FixedEntries), w.Net.ClientAdds);

        pc.Pawn!.EndTouch(entry);

        Assert.Equal(K.FixedEntries, menu.NbObjects);
        Assert.Null(menu.Interactions[K.FixedEntries]);
        Assert.Contains(entry, w.Net.ClientRemoves);
    }

    [Fact]
    public void Touch_by_a_pawn_without_a_player_controller_does_nothing()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);
        var stray = w.Level.Spawn<SPawnAttaque>()!;

        stray.BeginTouch(entry);

        Assert.Equal(K.FixedEntries, pc.IManager!.NbObjects);
    }

    [Fact]
    public void Destroying_an_entry_removes_it_from_every_menu()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC a = w.AddPlayer(spy: true, default);
        TestPC b = w.AddPlayer(spy: false, default);
        a.IManager!.AddInteractionObj(entry);
        b.IManager!.AddInteractionObj(entry);

        entry.Destroy();

        Assert.Equal(-1, a.IManager.IndexOf(entry));
        Assert.Equal(-1, b.IManager.IndexOf(entry));
    }

    // ---- rule 9: the manager -----------------------------------------------------------------------------------

    [Fact]
    public void Manager_starts_with_the_five_fixed_entries()
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        SInteractionManager menu = pc.IManager!;

        Assert.Equal(5, menu.NbObjects);
        Assert.IsType<SBackPackInteraction>(menu.Interactions[0]);
        Assert.IsType<SRemoveSpyBulletInteraction>(menu.Interactions[1]);
        Assert.IsType<SHealMateInteraction>(menu.Interactions[2]);
        Assert.IsType<SShareObjectInteraction>(menu.Interactions[3]);
        Assert.IsType<SLeaveInteraction>(menu.Interactions[4]);
        Assert.Same(menu, menu.Interactions[4]!.Owner);
        Assert.Same(pc, menu.Interactions[0]!.Owner);
        Assert.Same(pc, menu.Owner);
        Assert.All(menu.Interactions[..5], e => Assert.False(e!.bCollideActors));
        Assert.All(menu.Interactions[..5], e => Assert.Equal(999999, e!.iPriority));
    }

    [Fact]
    public void Manager_is_spawned_once_and_kept()
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        SInteractionManager first = pc.IManager!;

        Assert.Same(first, InteractionMenuRules.EnsureManager(pc));
    }

    [Fact]
    public void Only_Exit_usable_does_not_open_the_menu()
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);

        Assert.False(pc.IManager!.HasUsableInteractions());
        pc.bUsing = 1;
        Assert.False(InteractionMenuRules.DoInteraction(pc, canInteract: true));
    }

    [Fact]
    public void Menu_list_holds_at_most_fifteen_entries_without_duplicates()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        TestPC pc = w.AddPlayer(spy: true, default);
        SInteractionManager menu = pc.IManager!;
        var first = SpawnEntry<STriggerInteraction>(w, owner);
        menu.AddInteractionObj(first);
        menu.AddInteractionObj(first);
        Assert.Equal(6, menu.NbObjects);

        for (int i = 0; i < 20; i++)
        {
            menu.AddInteractionObj(SpawnEntry<STriggerInteraction>(w, owner));
        }

        Assert.Equal(SInteractionManager.MAXITEM, menu.NbObjects);
    }

    // ---- rule 24 and open question 1: order and selection -------------------------------------------------------

    [Fact]
    public void Menu_order_is_lowest_priority_first_with_Exit_after_the_objects()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        TestPC pc = w.AddPlayer(spy: true, default);
        SInteractionManager menu = pc.IManager!;
        var terminal = SpawnEntry<STriggerInteraction>(w, owner);
        terminal.iPriority = 10;
        var sw = SpawnEntry<STriggerInteraction>(w, owner);
        sw.iPriority = 0;
        menu.AddInteractionObj(terminal);
        menu.AddInteractionObj(sw);

        Assert.True(menu.SelectFirstViewAbleItem());
        Assert.Same(sw, menu.GetCurrentInteraction());
        Assert.True(menu.SelectNextItem());
        Assert.Same(terminal, menu.GetCurrentInteraction());
        Assert.True(menu.SelectNextItem());
        Assert.IsType<SLeaveInteraction>(menu.GetCurrentInteraction());
        Assert.True(menu.SelectNextItem());
        Assert.Same(sw, menu.GetCurrentInteraction());
        Assert.True(menu.SelectPreviousItem());
        Assert.IsType<SLeaveInteraction>(menu.GetCurrentInteraction());
    }

    [Fact]
    public void Selection_skips_unusable_entries()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        TestPC pc = w.AddPlayer(spy: true, default);
        SInteractionManager menu = pc.IManager!;
        var off = SpawnEntry<STriggerInteraction>(w, owner);
        off.iPriority = 0;
        off.bUsable = false;
        var on = SpawnEntry<STriggerInteraction>(w, owner);
        on.iPriority = 5;
        menu.AddInteractionObj(off);
        menu.AddInteractionObj(on);

        menu.SelectFirstViewAbleItem();

        Assert.Same(on, menu.GetCurrentInteraction());
    }

    [Fact]
    public void CheckPriority_is_positive_when_the_new_entry_comes_first()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        TestPC pc = w.AddPlayer(spy: true, default);
        var low = SpawnEntry<STriggerInteraction>(w, owner);
        low.iPriority = 0;
        var high = SpawnEntry<STriggerInteraction>(w, owner);
        high.iPriority = 25;

        Assert.True(pc.IManager!.CheckPriority(high, low) > 0);
        Assert.True(pc.IManager.CheckPriority(low, high) < 0);
    }

    [Fact]
    public void Removing_an_entry_keeps_the_selection_on_the_same_object()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        TestPC pc = w.AddPlayer(spy: true, default);
        SInteractionManager menu = pc.IManager!;
        var a = SpawnEntry<STriggerInteraction>(w, owner);
        var b = SpawnEntry<STriggerInteraction>(w, owner);
        menu.AddInteractionObj(a);
        menu.AddInteractionObj(b);
        menu.SelectedInteractions = menu.IndexOf(b);

        menu.RemoveInteractionObj(a);

        Assert.Same(b, menu.GetCurrentInteraction());
        Assert.Equal(6, menu.NbObjects);
    }

    // ---- rule 23: CanUse ---------------------------------------------------------------------------------------

    [Fact]
    public void CanUse_needs_a_usable_entry_and_a_living_player()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);

        Assert.True(entry.CanUse(pc));
        entry.bUsable = false;
        Assert.False(entry.CanUse(pc));
        entry.bUsable = true;
        pc.Pawn!.Health = 0;
        Assert.False(entry.CanUse(pc));
        Assert.False(entry.CanUse(null));
    }

    [Theory]
    [InlineData(Usable.U_BOTH, true, true)]
    [InlineData(Usable.U_SPAWNOFFENSE, true, false)]
    [InlineData(Usable.U_SPAWNDEFENSE, false, true)]
    [InlineData(Usable.U_NO_ONE, false, false)]
    public void CanUse_filters_by_UsableBy(Usable usableBy, bool spyMay, bool mercMay)
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        entry.UsableBy = usableBy;
        TestPC spy = w.AddPlayer(spy: true, default);
        TestPC merc = w.AddPlayer(spy: false, default);

        Assert.Equal(spyMay, entry.CanUse(spy));
        Assert.Equal(mercMay, entry.CanUse(merc));
    }

    [Fact]
    public void CanUse_waits_for_the_reuse_delay_from_the_last_use()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        entry.iReusableTime = 20;
        TestPC pc = w.AddPlayer(spy: true, default);

        // iLastUsedTime starts at 0, so the first 20 s of the level are locked too.
        w.Run(19.4f);
        Assert.False(entry.CanUse(pc));
        w.Run(1.7f);
        Assert.True(entry.CanUse(pc));

        // Used at 21.1 s (stamped 21): refused 19.4 s later, accepted 20.9 s later (RE+ lab-power measurement).
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        Assert.Equal(21, entry.iLastUsedTime);
        w.Run(19.4f);
        Assert.False(entry.CanUse(pc));
        w.Run(1.5f);
        Assert.True(entry.CanUse(pc));
    }

    [Fact]
    public void CanUse_in_front_needs_the_player_on_the_front_side_facing_the_object()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner, new FRotator(0, 0, 0));
        entry.bInteractInFront = true;
        TestPC pc = w.AddPlayer(spy: true, default);
        Pawn pawn = pc.Pawn!;

        pawn.Location = owner.Location + new FVector(50f, 0f, 0f);
        pawn.Rotation = new FRotator(0, 32768, 0);
        Assert.True(entry.CanUse(pc));

        pawn.Rotation = new FRotator(0, 0, 0);
        Assert.False(entry.CanUse(pc));

        pawn.Location = owner.Location - new FVector(50f, 0f, 0f);
        pawn.Rotation = new FRotator(0, 0, 0);
        Assert.False(entry.CanUse(pc));
    }

    [Fact]
    public void Fixed_entries_show_Exit_always_and_mate_entries_only_with_a_living_mate()
    {
        var w = new ObjWorld();
        // The mate entries find their mate themselves (spec/objectives/team-interactions.md, mate search): a hurt
        // team mate 120 uu in front, facing the player.
        TestPC pc = w.AddPlayer(spy: true, default);
        TestPC mate = w.AddPlayer(spy: true, new FVector(120f, 0f, 0f), new FRotator(0, 32768, 0));
        SInteractionManager menu = pc.IManager!;
        var heal = (SMateInteraction)menu.Interactions[2]!;

        Assert.True(menu.Interactions[4]!.CanUse(pc));
        Assert.False(heal.CanUse(pc));
        mate.Pawn!.Health = 50;
        Assert.True(heal.CanUse(pc));
        Assert.Same(mate.Pawn, heal.Mate);
        mate.Pawn!.Health = 0;
        Assert.False(heal.CanUse(pc));
    }

    [Fact]
    public void Heal_mate_restores_the_class_health_and_cures_poison()
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        TestPC mate = w.AddPlayer(spy: true, default);
        var mateBody = (SBasePawn)mate.Pawn!;
        mateBody.Health = 30;
        mateBody.bPoisonned = true;
        var heal = (SHealMateInteraction)pc.IManager!.Interactions[2]!;
        heal.ReplicateMate(mateBody);

        InteractionMenuRules.InteractionInit(pc, heal, 0);

        Assert.Equal(new SPawnAttaque().Health, mateBody.Health);
        Assert.False(mateBody.bPoisonned);
    }

    // ---- rules 10 to 18: opening, release and the server calls --------------------------------------------------

    [Fact]
    public void DoInteraction_opens_the_menu_only_when_slow_and_Use_is_held()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.IManager!.AddInteractionObj(entry);

        pc.bUsing = 1;
        pc.aForward = 4000f;
        pc.aStrafe = 1f;
        Assert.False(InteractionMenuRules.DoInteraction(pc, canInteract: true));

        pc.aStrafe = 0f;
        Assert.False(InteractionMenuRules.DoInteraction(pc, canInteract: false));
        Assert.True(InteractionMenuRules.DoInteraction(pc, canInteract: true));
        Assert.True(pc.IsInState(K.s_MenuInteraction));
        Assert.Equal(K.s_PlayerWalkingSt0H, pc.PreviousState);
        Assert.Same(entry, pc.IManager.GetCurrentInteraction());
    }

    [Fact]
    public void DoInteraction_rearms_the_Use_button_once_it_is_released()
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.bCanCheckUsingButton = false;
        pc.bUsing = 0;

        Assert.False(InteractionMenuRules.DoInteraction(pc, canInteract: true));
        Assert.True(pc.bCanCheckUsingButton);
    }

    [Fact]
    public void Releasing_on_Exit_returns_to_the_previous_state_without_saving_the_menu()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.IManager!.AddInteractionObj(entry);

        EMenuRelease result = ObjWorld.UseEntry(pc, pc.IManager.Interactions[4]!);

        Assert.Equal(EMenuRelease.Leave, result);
        Assert.True(pc.IsInState(K.s_PlayerWalkingSt0H));
        Assert.Equal(K.s_PlayerWalkingSt0H, pc.PreviousState);
    }

    [Fact]
    public void Releasing_on_a_plain_switch_asks_for_the_press_button_state()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.IManager!.AddInteractionObj(entry);

        Assert.Equal(EMenuRelease.PressButton, ObjWorld.UseEntry(pc, entry));
        Assert.False(pc.bCanCheckUsingButton);
        Assert.Equal(EMenuRelease.None, InteractionMenuRules.MenuRelease(pc));
    }

    [Fact]
    public void Releasing_on_another_entry_sends_InteractionInit_and_the_default_returns_the_player()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<SInteractObject>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.IManager!.AddInteractionObj(entry);
        w.Run(3.5f);

        Assert.Equal(EMenuRelease.InteractionInitSent, ObjWorld.UseEntry(pc, entry));

        Assert.Equal(3, entry.iLastUsedTime);
        Assert.True(pc.IsInState(K.s_PlayerWalkingSt0H));
        Assert.Contains(pc.Mirrors, m => m.State == K.s_PlayerWalkingSt0H);
    }

    [Fact]
    public void Default_InitInteract_leaves_a_dead_player_alone()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<SInteractObject>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.PreviousState = K.s_PlayerWalkingCr0H;
        pc.Pawn!.Health = 0;

        InteractionMenuRules.InteractionInit(pc, entry, 0);

        Assert.Empty(pc.Mirrors);
    }

    [Fact]
    public void Press_button_end_sends_InteractionInit_for_the_selection()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        owner.Event = "Lights";
        var entry = SpawnEntry<STriggerInteraction>(w, owner);
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.IManager!.AddInteractionObj(entry);
        pc.IManager.SelectedInteractions = pc.IManager.IndexOf(entry);
        var light = w.Level.Spawn<EventCounter>(tag: "Lights")!;

        InteractionMenuRules.PressButtonAnimEnd(pc);

        Assert.Equal(1, light.Triggers);
    }

    [Fact]
    public void ChangePresenceDetected_sets_the_flag()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<SInteractObject>(w, owner);

        entry.ChangePresenceDetected(true);

        Assert.True(entry.bPresenceDetected);
    }

    [Fact]
    public void TriggerEventServer_fires_the_event_and_SetPriority_reorders()
    {
        (ObjWorld w, SBasicGamePlayObject owner) = WithOwner();
        var entry = SpawnEntry<SInteractObject>(w, owner);
        var target = w.Level.Spawn<EventCounter>(tag: "Door")!;

        entry.TriggerEventServer("Door", entry, null);
        entry.UnTriggerEventServer("Door", entry, null);
        entry.SetPriority(42);

        Assert.Equal((1, 1), (target.Triggers, target.UnTriggers));
        Assert.Equal(42, entry.iPriority);
    }
}

/// <summary>Counts the Trigger and UnTrigger events it receives.</summary>
internal sealed class EventCounter : Actor
{
    public int Triggers;
    public int UnTriggers;
    public Pawn? LastInstigator;

    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        Triggers++;
        LastInstigator = EventInstigator;
    }

    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => UnTriggers++;
}
