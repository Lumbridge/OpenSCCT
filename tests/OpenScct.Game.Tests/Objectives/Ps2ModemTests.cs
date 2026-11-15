using System.Collections.Generic;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class Ps2ModemTests
{
    private static readonly AssetRef Mesh = new("PS2_Objectives.OriginalModem");
    private static readonly AssetRef Loop = new("PS2_Objectives.ModemLoop");

    private sealed record Scene(ObjWorld World, SBombTargetObjectiveTrigger Target, SObjective Objective, TestPC Spy, SGadBomb Gadget);

    private static Scene Make(bool wall = true, int capture = 10)
    {
        var world = new ObjWorld();
        SBombTargetObjectiveTrigger? target = null;
        SObjective? objective = null;
        world.Load(w =>
        {
            var console = w.Place(new SComputerObjectiveTrigger { bPS2Objective = true, StaticMesh = AssetRef.None }, new FVector(40f, 60f, 0f));
            target = w.Place(new SBombTargetObjectiveTrigger
            {
                bPS2Objective = true, Ps2Computer = console, CaptureTime = capture,
                CollisionRadius = 200f, CollisionHeight = 200f,
                Ps2ModemMesh = Mesh, Ps2ModemSound = Loop,
            }, new FVector(70f, 0f, 0f));
            objective = new SObjective();
            objective.Triggers.Add(target);
            w.Place(objective, default);
        });
        objective!.Reset();
        var builder = new CollisionDataBuilder();
        if (wall) builder.AddBox(new FVector(70f, -500f, -500f), new FVector(90f, 500f, 500f));
        world.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        TestPC spy = world.AddPlayer(true, default);
        spy.SBPawn = (SBasePawn)spy.Pawn!;
        spy.Pawn!.Physics = EPhysics.PHYS_Walking;
        var gadget = world.Level.SpawnActor(new SGadBomb(), default, default)!;
        gadget.GiveTo(spy.Pawn);
        world.Run(4.1f);
        return new Scene(world, target!, objective, spy, gadget);
    }

    private static Ps2Modem Plant(Scene s)
    {
        Assert.True(s.Gadget.CanUseGadget(), $"Placement refused: {s.Gadget.WhyCantUse}");
        s.Gadget.Use(1f);
        return Assert.Single(Modems(s));
    }

    private static List<Ps2Modem> Modems(Scene s)
    {
        var result = new List<Ps2Modem>();
        foreach (Ps2Modem modem in s.World.Level.AllActors<Ps2Modem>()) if (!modem.bDeleteMe) result.Add(modem);
        return result;
    }

    [Fact]
    public void Gadget_fire_places_source_modem_on_world_surface_and_capture_completes_on_its_timer()
    {
        Scene s = Make();
        Assert.Equal(1, s.Gadget.Charge);
        Assert.True(PawnInventory.IsSelectable(s.Gadget));
        Assert.Null(s.Target.InteractionObj);
        Assert.True(s.Target.bHidden);
        Ps2Modem modem = Plant(s);
        Assert.Equal(0, s.Gadget.Charge);
        Assert.Equal(Mesh, modem.StaticMesh);
        Assert.Equal(Loop, modem.AmbientSound);
        Assert.InRange(modem.Location.X, 69.4f, 70f); // The collision reader keeps its half-unit trace skin.
        Assert.Equal(new FRotator(0, 32768, 0), modem.Rotation);
        Assert.True(s.Target.bTriggering);
        Assert.Same(s.Spy, s.Target.ControllerWhoTakesIt);
        Assert.IsType<SBombInteraction>(modem.InteractionObj);
        s.World.Run(9f);
        Assert.False(s.Objective.ObjectiveTaken);
        s.World.Run(1.1f);
        Assert.True(s.Objective.ObjectiveTaken);
        Assert.True(modem.bDeleteMe);
    }

    [Fact]
    public void Missing_surface_locked_target_and_duplicate_modem_refuse_without_spending_charge()
    {
        Scene noSurface = Make(false);
        noSurface.Gadget.Use(1f);
        Assert.Equal(1, noSurface.Gadget.WhyCantUse);
        Assert.Equal(1, noSurface.Gadget.Charge);
        Assert.Empty(Modems(noSurface));
        Scene s = Make();
        s.Target.eventLock();
        s.Gadget.Use(1f);
        Assert.Equal(4, s.Gadget.WhyCantUse);
        Assert.Equal(1, s.Gadget.Charge);
        s.Target.eventUnLock();
        Plant(s);
        s.World.Run(4.1f);
        s.Gadget.FullReload();
        s.Gadget.Use(1f);
        Assert.Equal(4, s.Gadget.WhyCantUse);
        Assert.Equal(1, s.Gadget.Charge);
        Assert.Single(Modems(s));
    }

    [Fact]
    public void Shooting_modem_keeps_whole_seconds_progress_and_replacement_finishes_remaining_time()
    {
        Scene s = Make();
        Ps2Modem modem = Plant(s);
        s.World.Run(3.25f);
        modem.eventTakeDamage(1, s.Spy.Pawn, modem.Location, FVector.Zero, SDamageType.SDTBase);
        Assert.True(modem.bDeleteMe);
        Assert.False(s.Target.bTriggering);
        Assert.Equal(7, s.Target.CaptureTime);
        Assert.False(s.Objective.ObjectiveTaken);
        s.World.Run(1f);
        s.Gadget.FullReload();
        Ps2Modem replacement = Plant(s);
        s.World.Run(6f);
        Assert.False(s.Objective.ObjectiveTaken);
        s.World.Run(1.1f);
        Assert.True(s.Objective.ObjectiveTaken);
        Assert.True(replacement.bDeleteMe);
    }

    [Fact]
    public void Merc_remove_entry_destroys_modem_and_pauses_capture()
    {
        Scene s = Make();
        Ps2Modem modem = Plant(s);
        TestPC merc = s.World.AddPlayer(false, modem.Location);
        s.World.Run(2.2f);
        modem.InteractionObj!.eventInitInteract(merc, 0);
        Assert.True(modem.bDeleteMe);
        Assert.True(modem.InteractionObj.bDeleteMe);
        Assert.False(s.Target.bTriggering);
        Assert.Equal(8, s.Target.CaptureTime);
    }

    [Fact]
    public void Cycling_to_modem_then_actual_alternate_fire_input_places_it_and_HUD_fields_are_source_values()
    {
        Scene s = Make();
        SBasePawn pawn = s.Spy.SBPawn!;
        var smoke = s.World.Level.SpawnActor(new SGadSmokeGrenade(), pawn.Location, pawn.Rotation)!;
        smoke.GiveTo(pawn);
        pawn.SelectedGadget = smoke;
        PawnInventory.NextGadget(pawn);
        Assert.Same(s.Gadget, pawn.SelectedGadget);
        Assert.Equal("Modem", pawn.SelectedGadget.ItemName);
        Assert.Equal((128, 64), (pawn.SelectedGadget.IconU, pawn.SelectedGadget.IconV));
        s.Spy.eventAltFire(0f);
        Assert.Single(Modems(s));
        Assert.Equal(0, pawn.SelectedGadget.Charge);
        Assert.True(s.Target.bTriggering);
    }
}
