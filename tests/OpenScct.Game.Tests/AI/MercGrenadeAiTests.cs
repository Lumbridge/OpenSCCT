using System.Text;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class MercGrenadeAiTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false, 0)]
    [InlineData(true, false, false, true, 4, true)]
    public void MercLaunchesAnActualFragForASafeKnownTarget(bool visible, bool teammateAtTarget, bool blockedArc, bool expected,
        int charges = 4, bool teammateAfterLaunch = false)
    {
        var geometry = Floor();
        if (blockedArc) geometry.AddBox(V(-300f, 400f, 0f), V(300f, 440f, 400f));
        PawnRig rig = Rig(geometry, null, V(0f, 0f, 0f), PawnTeam.Merc);
        CombatEquipment.Install(rig.Level);
        AiWorld world = World(rig, V(-256f, -320f, -100f), V(256f, 1200f, 500f));
        var rifle = rig.Level.SpawnActor(new SWeaponDefense(), rig.Pawn.Location, default)!;
        rifle.SetOwner(rig.Pawn);
        rifle.GiveTo(rig.Pawn);
        rig.Pawn.WeaponLoaded = 2;
        var grenade = rig.Level.SpawnActor(new SGadFragGrenade(), rig.Pawn.Location, default)!;
        grenade.GiveTo(rig.Pawn);
        grenade.Charge = charges;
        var mines = rig.Level.SpawnActor(new SGadMultiMinesLauncher(), rig.Pawn.Location, default)!;
        mines.GiveTo(rig.Pawn);
        rig.Pawn.SelectedGadget = mines;
        // The rifle is ready and the initial gadget refire interval has elapsed, as in a running match.
        for (int frame = 0; frame < 3 * Sim.Hz; frame++) rig.World.Step();
        AiDifficulty difficulty = AiDifficulty.For(AiSkill.Expert) with { GadgetUse = 1f, DecisionNoise = 0f, Teamwork = 0f };
        var bot = new AiController(world, rig.Controller, difficulty, 0);
        bot.Tick();
        FVector target = V(0f, 1000f, 90f);
        var observed = new SPawnAttaque { Location = target };
        if (teammateAtTarget)
        {
            rig.Level.SpawnActor(new SPAWNDEFENSE(), target + V(100f, 0f, 0f), default);
        }

        bool spawned = false;
        var trace = new StringBuilder();
        for (int frame = 0; frame < 5 * Sim.Hz; frame++)
        {
            if (visible)
                bot.Memory.Observe(observed, new AiSighting(true, 1f, 1000f, 1f, true), world.Now, 1f, difficulty);
            else
                bot.Memory.Report(observed, target, world.Now, 1f, AiContactSource.Told);
            bot.Tick();
            rig.World.Step();
            if (teammateAfterLaunch && grenade.Charge < charges)
            {
                // The throw is committed; late movement cannot cancel counting that accepted use/cooldown.
                rig.Level.SpawnActor(new SPAWNDEFENSE(), target + V(100f, 0f, 0f), default);
                teammateAfterLaunch = false;
            }
            foreach (SProjFrag projectile in rig.Level.DynamicActors<SProjFrag>())
                spawned |= !projectile.bDeleteMe;
            if (frame % Sim.Hz == 0)
                trace.AppendLine($"{world.Now:F2} {bot.Status}, controller={rig.Controller.GetStateName()}, weapon={rifle.GetStateName()}, gadget={rig.Pawn.SelectedGadget?.GetUcClass().Path}, ammo={grenade.Charge}");
        }

        Assert.True(spawned == expected, trace.ToString());
        Assert.Equal(expected ? charges - 1 : charges, grenade.Charge);
        Assert.Equal(expected ? 1 : 0, Assert.IsType<MercAi>(bot.Brain).FragsThrown);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void MercUsesTheWindowToReachAnElevatedHackAlarm(bool window, bool teammateInside, bool expected)
    {
        var geometry = Floor();
        geometry.AddBox(V(-400f, 780f, 0f), V(400f, 1600f, 320f));
        geometry.AddBox(V(-400f, 450f, 0f), V(400f, 480f, window ? 188f : 760f));
        if (window)
        {
            geometry.AddBox(V(-400f, 450f, 410f), V(400f, 480f, 760f));
            geometry.AddBox(V(-400f, 450f, 188f), V(-90f, 480f, 410f));
            geometry.AddBox(V(90f, 450f, 188f), V(400f, 480f, 410f));
        }
        geometry.AddBox(V(-400f, 1350f, 320f), V(400f, 1380f, 760f));
        PawnRig rig = Rig(geometry, null, default, PawnTeam.Merc);
        CombatEquipment.Install(rig.Level);
        AiWorld world = World(rig, V(-350f, -320f, -100f), V(350f, 1500f, 820f));
        var rifle = rig.Level.SpawnActor(new SWeaponDefense(), rig.Pawn.Location, default)!;
        rifle.GiveTo(rig.Pawn);
        rig.Pawn.WeaponLoaded = 2;
        var frag = rig.Level.SpawnActor(new SGadFragGrenade(), rig.Pawn.Location, default)!;
        frag.GiveTo(rig.Pawn);
        var terminal = rig.Level.SpawnActor(new SComputerObjectiveTrigger
        {
            bStatic = false, bNoDelete = false, bCollideActors = false, bBlockPlayers = false,
            UseRelativeLocation = default,
        }, V(0f, 1050f, 400f), default)!;
        if (teammateInside)
            rig.Level.SpawnActor(new SPAWNDEFENSE(), V(60f, 1110f, 410f), default);

        for (int i = 0; i < 3 * Sim.Hz; i++) rig.World.Step();
        var difficulty = AiDifficulty.For(AiSkill.Hard) with { SightRange = 1f, HearingScale = 0f };
        var bot = new AiController(world, rig.Controller, difficulty, 0);
        int charges = frag.Charge;
        bool inside = false;
        for (int i = 0; i < 5 * Sim.Hz; i++)
        {
            terminal.bTriggering = true;
            bot.Tick();
            rig.World.Step();
            foreach (SProjFrag launched in rig.Level.DynamicActors<SProjFrag>())
                inside |= launched.Location.Y > 780f && launched.Location.Z > 320f;
        }

        Assert.Equal(expected ? charges - 1 : charges, frag.Charge);
        Assert.Equal(expected, inside);
        Assert.Equal(expected ? 1 : 0, Assert.IsType<MercAi>(bot.Brain).FragsThrown);
    }

}
