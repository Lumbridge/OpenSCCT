using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class SpySecurityTests
{
    [Fact]
    public void BeamCoverageIncludesBodyButNotSafeSpaceAboveItAndStopsWhenDisabled()
    {
        var rig = Rig(Floor(), null, V(0, -200, 0));
        var laser = rig.Level.SpawnActor(new SLaserTrigger { bStatic = false, bNoDelete = false, SpawnPerCent = 100 }, V(-200, 0, 40), default)!;
        laser.GotoState(new Name("s_Active"));
        laser.LaserEndLocation = V(200, 0, 40);
        Assert.True(SpySecurity.Covers(laser, V(0, 20, 80), 35, 85));
        Assert.False(SpySecurity.Covers(laser, V(0, 20, 200), 35, 85));
        Assert.False(SpySecurity.Covers(laser, V(0, 100, 80), 35, 85));
        laser.GotoState(new Name("s_LockedByChaff"));
        Assert.False(SpySecurity.Covers(laser, V(0, 20, 80), 35, 85));
    }

    [Fact]
    public void VisibleLaserStopsApproachAndRequestsNormalWeaponDraw()
    {
        var rig = Rig(Floor(), null, V(0, -160, 0));
        var world = World(rig, V(-320, -320, -100), V(320, 320, 300));
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal) with { GadgetUse = 0 }, 0);
        bot.Tick();
        var laser = rig.Level.SpawnActor(new SLaserTrigger { bStatic = false, bNoDelete = false, SpawnPerCent = 100 }, V(-100, 0, 40), default)!;
        laser.GotoState(new Name("s_Active"));
        laser.LaserEndLocation = V(200, 0, 40);
        var security = new SpySecurity();
        security.Observe(bot, world.Now);
        var plan = new AiPlan { Yaw = 16384, Keys = new AiInput { BT_Up = 1 } };
        Assert.True(security.Steer(bot, world.Now, ref plan));
        Assert.False(plan.Keys.Moving);
        Assert.Equal(1, plan.Keys.bFireMode);
        Assert.True(security.Penalty(bot, V(0, 0, 0)) > 0);
        // A failed shot sequence must give navigation another turn instead of freezing forever.
        plan = new AiPlan { Yaw = 16384, Keys = new AiInput { BT_Up = 1 } };
        Assert.True(security.Steer(bot, world.Now + 7f, ref plan));
        Assert.False(security.Steer(bot, world.Now + 7.1f, ref plan));
        Assert.True(float.IsPositiveInfinity(security.Penalty(bot, V(0, 0, 0))));
        laser.GotoState(new Name("s_Inactive"));
        Assert.False(security.Steer(bot, world.Now, ref plan));
        Assert.Equal(0, security.Penalty(bot, V(0, 0, 0)));
    }

    [Fact]
    public void CameraConeAndDisabledStateAreRespected()
    {
        var rig = Rig(Floor(), null, V(0, -160, 0));
        var camera = rig.Level.SpawnActor(new SCamera { bStatic = false, bNoDelete = false, bAutoWallSupport = false }, V(0, 0, 200), default)!;
        camera.GotoState(new Name("s_Patrol"));
        camera.Rotation = default;
        camera.VisibilityConeAngle = 60;
        camera.VisibilityMaxDistance = 800;
        Assert.True(SpySecurity.Covers(camera, V(300, 0, 120), 35, 85));
        Assert.False(SpySecurity.Covers(camera, V(-300, 0, 120), 35, 85));
        camera.GotoState(new Name("s_LockedByChaff"));
        Assert.False(SpySecurity.Covers(camera, V(300, 0, 120), 35, 85));
    }
}
