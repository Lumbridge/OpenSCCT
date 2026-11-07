using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class SquadTacticsTests
{
    private static readonly AiDifficulty Quiet = AiDifficulty.For(AiSkill.Hard) with
    {
        SightRange = 1f, HearingScale = 0f, DecisionNoise = 0f,
    };

    [Fact]
    public void MercsTakeDifferentSidesOfASingleHackAlarm()
    {
        PawnRig rig = Rig(Floor(), null, V(0, -510, 0), PawnTeam.Merc);
        RigMate mate = rig.AddMate(PawnTeam.Merc, V(0, 600, 0), -16384);
        AiWorld world = World(rig, V(-800, -900, -100), V(800, 950, 400));
        SComputerObjectiveTrigger terminal = Terminal(rig, V(0, 0, 80));
        var first = new AiController(world, rig.Controller, Quiet, 0);
        var second = new AiController(world, mate.Controller, Quiet, 1);
        first.Tick();
        second.Tick();
        for (int i = 0; i < Sim.Hz; i++)
        {
            terminal.bTriggering = true;
            first.Tick();
            second.Tick();
            rig.World.Step();
        }

        var a = world.RespondTo(first, terminal, world.Now);
        var b = world.RespondTo(second, terminal, world.Now);
        Assert.True(a.Assigned && b.Assigned);
        Assert.NotEqual(a.Cover, b.Cover);
        var firstBrain = Assert.IsType<MercAi>(first.Brain);
        var secondBrain = Assert.IsType<MercAi>(second.Brain);
        Assert.Equal(MercAi.GRespond, firstBrain.Goal);
        Assert.Equal(MercAi.GRespond, secondBrain.Goal);
        Assert.True((firstBrain.Follower.Goal | secondBrain.Follower.Goal) < 0,
            $"{firstBrain.Follower.Goal} vs {secondBrain.Follower.Goal}");
    }

    [Fact]
    public void SpyStartsSmokeToCoverAnotherBotsHack()
    {
        PawnRig rig = Rig(Floor(), null, V(0, 0, 0));
        RigMate mate = rig.AddMate(PawnTeam.Spy, V(0, 565, 0), 16384);
        CombatEquipment.Install(rig.Level);
        AiWorld world = World(rig, V(-650, -400, -100), V(650, 1500, 400));
        SComputerObjectiveTrigger terminal = Terminal(rig, V(0, 800, 80));
        var rifle = rig.Level.SpawnActor(new SWeaponAttaque(), rig.Pawn.Location, default)!;
        rifle.GiveTo(rig.Pawn);
        var smoke = rig.Level.SpawnActor(new SGadSmokeGrenade(), rig.Pawn.Location, default)!;
        smoke.GiveTo(rig.Pawn);
        int charges = smoke.Charge;
        for (int i = 0; i < 3 * Sim.Hz; i++) rig.World.Step();

        var lead = new AiController(world, mate.Controller, Quiet, 0);
        var helper = new AiController(world, rig.Controller, Quiet, 1);
        var guard = new SPAWNDEFENSE { Location = V(0, 1220, 87) };
        bool reserved = false;
        for (int i = 0; i < 4 * Sim.Hz; i++)
        {
            lead.Tick();
            helper.Memory.Report(guard, guard.Location, world.Now, .8f, AiContactSource.Told);
            helper.Tick();
            reserved |= world.Teams[0].AwaitSmoke(terminal, lead, world.Now);
            rig.World.Step();
            if (smoke.Charge < charges) break;
        }

        Assert.True(reserved, $"{lead.Status} | {helper.Status}");
        Assert.Equal(charges - 1, smoke.Charge);
    }

    [Fact]
    public void HeartbeatTimingAndLikelihoodVaryBySpy()
    {
        PawnRig rig = Rig(Floor(), null, V(-350, 0, 0));
        RigMate mate = rig.AddMate(PawnTeam.Spy, V(350, 0, 0), 16384);
        CombatEquipment.Install(rig.Level);
        AiWorld world = World(rig, V(-900, -900, -100), V(900, 900, 400));
        foreach (SBasePawn pawn in new[] { rig.Pawn, mate.Pawn })
        {
            rig.Level.SpawnActor(new SWeaponAttaque(), pawn.Location, default)!.GiveTo(pawn);
            rig.Level.SpawnActor(new SGadHeartBeatSensor(), pawn.Location, default)!.GiveTo(pawn);
        }

        var first = new AiController(world, rig.Controller, Quiet, 0);
        var second = new AiController(world, mate.Controller, Quiet, 1);
        Assert.NotEqual(first.Personality.Style, second.Personality.Style);
        Assert.NotEqual(first.ReactionTime, second.ReactionTime);
        Assert.NotEqual(first.Personality.HeartbeatLikelihood, second.Personality.HeartbeatLikelihood);
        int firstScan = -1;
        int secondScan = -1;
        for (int i = 0; i < 35 * Sim.Hz; i++)
        {
            first.Tick();
            second.Tick();
            if (firstScan < 0 && first.Gadgets.Active == SpyGadgetAct.Heartbeat) firstScan = i;
            if (secondScan < 0 && second.Gadgets.Active == SpyGadgetAct.Heartbeat) secondScan = i;
            rig.World.Step();
            if (first.Gadgets.Scans > 0 && second.Gadgets.Scans > 0) break;
        }

        Assert.True(first.Gadgets.Scans > 0 && second.Gadgets.Scans > 0);
        Assert.NotEqual(firstScan, secondScan);
    }

    [Fact]
    public void RepeatedApproachesCarryIntoTheNextRoundAndFade()
    {
        var patrol = new AiPatrolHistory();
        FVector observed = V(96, 150, 80);
        patrol.Record(observed);
        patrol.StartRound();
        Assert.Equal(0f, patrol.Interest(observed));
        patrol.Record(observed);
        patrol.Record(observed);
        patrol.StartRound();
        Assert.True(patrol.Interest(observed) > 0f);
        Assert.Equal(0f, patrol.Interest(observed + V(0, 0, 300)));
        for (int i = 0; i < 5; i++) patrol.StartRound();
        Assert.Equal(0f, patrol.Interest(observed));
    }

    private static SComputerObjectiveTrigger Terminal(PawnRig rig, FVector point) =>
        rig.Level.SpawnActor(new SComputerObjectiveTrigger
        {
            bStatic = false, bNoDelete = false, bCollideActors = false, bBlockPlayers = false,
            UseRelativeLocation = default, bActive = true,
        }, point, default)!;
}
