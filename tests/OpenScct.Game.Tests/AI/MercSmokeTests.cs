using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class MercSmokeTests
{
    [Fact]
    public void UnseenSilentCameraDoesNotTriggerMaskButAnAudibleCameraDoes()
    {
        var rig = Rig(Floor(), null, V(0, 0, 0), PawnTeam.Merc);
        var world = World(rig, V(-320, -320, -100), V(320, 640, 400));
        var mask = rig.Level.SpawnActor(new SGadGasMask(), rig.Pawn.Location, default)!;
        mask.GiveTo(rig.Pawn);
        rig.Pawn.SelectedGadget = mask;
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Expert), 0);
        bot.Tick();
        var spy = rig.Level.SpawnActor(new SPawnAttaque(), V(500, 0, 90), default)!;
        rig.Level.SpawnActor(new SPlayerController(), spy.Location, default)!.Possess(spy);
        var cam = rig.Level.SpawnActor(new SStickyCam(), V(0, -200, 100), default)!;
        // Behind the merc's view; do not reveal silent hidden cameras to the bot.
        for (int i = 0; i < Sim.Hz; i++)
        {
            world.BeginTick();
            var plan = new AiPlan();
            bot.GasProtection.Apply(bot, world.Now, ref plan);
            plan.Keys.ApplyTo(rig.Controller);
            rig.World.Step();
        }
        Assert.False(rig.Pawn.bGasMask);
        world.Noise.CheckNoiseHearing(cam, spy, 1f);
        for (int i = 0; i < Sim.Hz; i++)
        {
            world.BeginTick();
            var plan = new AiPlan();
            bot.GasProtection.Apply(bot, world.Now, ref plan);
            plan.Keys.ApplyTo(rig.Controller);
            rig.World.Step();
        }
        Assert.True(rig.Pawn.bGasMask);
    }

    [Fact]
    public void SmokeBlocksSightAndBlindFireUsesOnlyARecentVisualSnapshot()
    {
        var rig = Rig(Floor(), null, V(0, 0, 0), PawnTeam.Merc);
        var world = World(rig, V(-640, -640, -100), V(640, 960, 400));
        var spy = rig.Level.SpawnActor(new SPawnAttaque(), V(0, 500, 90), default)!;
        spy.bSpyBulletOn = true; // An electronic mark must not turn smoke into a direct body sighting.
        var memory = new AiMemory();
        var difficulty = AiDifficulty.For(AiSkill.Normal);
        var contact = memory.Observe(spy, new AiSighting(true, 1, 500, 1, true), world.Now, 2, difficulty);
        FVector last = spy.Location;
        rig.Level.SpawnActor(new SSmokeEmitter { RadiusEffect = 200 }, last, default);
        world.BeginTick();
        foreach (AiVisionMode mode in new[] { AiVisionMode.Normal, AiVisionMode.Night, AiVisionMode.Motion, AiVisionMode.Emf })
            Assert.False(AiSight.Look(world, AiSight.Eye(rig.Pawn), rig.Controller.Rotation, 90, mode, spy, difficulty).Visible);
        spy.Location = V(400, 800, 90);
        memory.Observe(spy, default, world.Now + .1f, .1f, difficulty);
        Assert.True(MercSmokeTactics.LastSeenShot(world, AiSight.Eye(rig.Pawn), contact, world.Now + 1, out FVector aim));
        Assert.Equal(last, aim);
        Assert.False(MercSmokeTactics.LastSeenShot(world, AiSight.Eye(rig.Pawn), contact, world.Now + 3, out _));
        var heardOnly = new AiContact(spy) { Position = last, Confidence = 1, Source = AiContactSource.Heard };
        Assert.False(MercSmokeTactics.LastSeenShot(world, AiSight.Eye(rig.Pawn), heardOnly, world.Now, out _));
    }

    [Theory]
    [InlineData(AiSkill.Easy)]
    [InlineData(AiSkill.Expert)]
    public void MercEquipsMaskAfterDifficultyReactionAndRemovesItWhenSafe(AiSkill skill)
    {
        var rig = Rig(Floor(), null, V(0, 0, 0), PawnTeam.Merc);
        var world = World(rig, V(-320, -320, -100), V(320, 640, 400));
        var mask = rig.Level.SpawnActor(new SGadGasMask(), rig.Pawn.Location, default)!;
        mask.GiveTo(rig.Pawn);
        var frag = rig.Level.SpawnActor(new SGadFragGrenade(), rig.Pawn.Location, default)!;
        frag.GiveTo(rig.Pawn);
        rig.Pawn.SelectedGadget = frag;
        var difficulty = AiDifficulty.For(skill);
        var bot = new AiController(world, rig.Controller, difficulty, 0);
        bot.Tick();
        var smoke = rig.Level.SpawnActor(new SSmokeEmitter { RadiusEffect = 300 }, rig.Pawn.Location, default)!;
        float start = world.Now;
        for (int i = 0; i < 2 * Sim.Hz; i++)
        {
            world.BeginTick();
            var plan = new AiPlan();
            bot.GasProtection.Apply(bot, world.Now, ref plan);
            plan.Keys.ApplyTo(rig.Controller);
            if (world.Now - start < difficulty.ReactionTime * 1.5f) Assert.False(rig.Pawn.bGasMask);
            rig.World.Step();
        }
        Assert.True(rig.Pawn.bGasMask);
        Assert.True(mask.MaskDuration < 30);
        smoke.Destroy();
        for (int i = 0; i < 3 * Sim.Hz; i++)
        {
            world.BeginTick();
            var plan = new AiPlan();
            bot.GasProtection.Apply(bot, world.Now, ref plan);
            plan.Keys.ApplyTo(rig.Controller);
            rig.World.Step();
        }
        Assert.False(rig.Pawn.bGasMask);
        Assert.False(rig.Pawn.bSleeping);
    }
}
