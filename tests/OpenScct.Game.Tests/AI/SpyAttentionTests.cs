using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class SpyAttentionTests
{
    [Fact]
    public void SpyEvadesTheMercFacingItWhenANearerMercIsLookingAway()
    {
        var rig = Rig(Floor(), null, default, yaw: 0);
        rig.Pawn.MyWeapon = null;
        FVector near = V(260f, -100f, 89f);
        FVector farther = V(500f, 100f, 89f);
        var light = new AiLightField([
            new(near + V(-40f, 0f, 0f), default, 255f, 0, 255, 5f, 128, 0, -1, false),
            new(farther + V(-40f, 0f, 0f), default, 255f, 0, 255, 5f, 128, 0, -1, false),
        ]);
        var world = World(rig, V(-500f, -500f, -100f), V(600f, 500f, 300f), light: light);
        var lookingAway = rig.Level.SpawnActor(new SPAWNDEFENSE { Health = 100 }, near, default)!;
        var facingSpy = rig.Level.SpawnActor(new SPAWNDEFENSE { Health = 100 }, farther, new FRotator(0, 32768, 0))!;
        var bot = new AiController(world, rig.Controller,
            AiDifficulty.For(AiSkill.Normal) with { DecisionNoise = 0f, GadgetUse = 0f, MeleeUse = 0f, SpotSeconds = 0.05f }, 0);

        bot.Tick();

        Assert.True(bot.Memory.Find(lookingAway)?.Confirmed);
        Assert.True(bot.Memory.Find(facingSpy)?.Confirmed);
        Assert.Equal(SpyAi.GEvade, Assert.IsType<SpyAi>(bot.Brain).Goal);
    }

    [Theory]
    [InlineData(360f, false, true)]
    [InlineData(180f, false, false)]
    [InlineData(180f, true, false)]
    public void SpyTurnsTowardAudibleMercFootstepsWithoutSeeingOrAttackingTheMerc(float speed, bool crouched, bool audible)
    {
        var geometry = Floor();
        geometry.AddBox(V(-100f, 210f, 0f), V(100f, 240f, 300f));
        var rig = Rig(geometry, null, default, yaw: 0);
        var world = World(rig, V(-500f, -100f, -100f), V(500f, 500f, 300f));
        var merc = rig.Level.SpawnActor(new SPAWNDEFENSE { Health = 100 }, V(0f, 450f, 89f), default)!;
        merc.Physics = EPhysics.PHYS_Walking;
        merc.bIsCrouched = crouched;
        merc.Velocity = V(speed, 0f, 0f);
        Assert.False(world.LineOpen(AiSight.Eye(rig.Pawn), AiSight.Eye(merc)));

        var bot = new AiController(world, rig.Controller,
            AiDifficulty.For(AiSkill.Normal) with { DecisionNoise = 0f, GadgetUse = 0f, MeleeUse = 0f }, 0);
        merc.PlayFootStepSound(true);
        float strongestLook = 0f;
        for (int tick = 0; tick < 24; tick++)
        {
            bot.Tick();
            Assert.Equal(0, bot.LastInput.bFire);
            Assert.NotEqual(AiCommand.Fire, bot.LastInput.Command1);
            Assert.NotEqual(AiCommand.Fire, bot.LastInput.Command2);
            strongestLook = SvmMath.Max(strongestLook, bot.LastInput.View?.Vector().Y ?? 0f);
            rig.World.Step();
        }

        if (!audible)
        {
            Assert.Null(bot.Memory.Find(merc));
            return;
        }

        AiContact heard = Assert.IsType<AiContact>(bot.Memory.Find(merc));
        Assert.Equal(AiContactSource.Heard, heard.Source);
        Assert.False(heard.Confirmed);
        Assert.Null(heard.Pose);
        Assert.True(strongestLook > 0.85f, $"Strongest turn toward the heard Merc: {strongestLook:F2}");
    }
}
