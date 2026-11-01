using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class DoorAiTests
{
    [Theory]
    [InlineData(PawnTeam.Merc, "SBase.SPAWNDEFENSE", true, false, true)]
    [InlineData(PawnTeam.Merc, "SBase.SBasePawn", true, false, true)]
    [InlineData(PawnTeam.Spy, "SBase.SPAWNDEFENSE", true, false, false)]
    [InlineData(PawnTeam.Merc, "SBase.SPAWNDEFENSE", false, false, false)]
    [InlineData(PawnTeam.Merc, "SBase.SPAWNDEFENSE", true, true, false)]
    [InlineData(PawnTeam.Merc, "None", true, false, false)]
    public void PresenceSensorDoorIsOnlyKeptOnRoutesForPawnsThatCanOpenIt(
        PawnTeam team, string detectedClass, bool enabled, bool untrigger, bool permitted)
    {
        var level = Floor();
        level.AddBoxHullCollider(ColliderKind.Mover, "Door", CollisionDataBuilder.WorldFlags,
            V(-300f, -8f, 0f), V(300f, 8f, 256f));
        PawnRig rig = Rig(level, null, V(0f, -160f, 0f), team);
        Mover door = rig.Level.SpawnActor(new Mover
        {
            bStatic = false,
            bNoDelete = false,
            InitialState = new Name("TriggerOpenTimed"),
            Tag = new Name("SpawnDoor"),
        }, default, default)!;
        Assert.NotNull(door);
        SPresenceTrigger sensor = rig.Level.SpawnActor(new SPresenceTrigger
        {
            bStatic = false,
            bNoDelete = false,
            Event = door.Tag,
            DetectedClass = new ClassRef(detectedClass),
            bCollideActors = enabled,
            bUntrigger = untrigger,
            CollisionRadius = 40f,
            CollisionHeight = 90f,
        }, V(0f, -40f, 90f), default)!;
        Assert.NotNull(sensor);
        rig.Collision!.BindLevel(rig.Level, id => id == "Door" ? door : null);
        AiWorld world = World(rig, V(-256f, -320f, -100f), V(256f, 320f, 400f));
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        bot.Tick();
        AiBrain brain = bot.Brain!;
        Assert.True(brain.Follower.SetGoal(rig.Pawn, V(0f, 160f, 0f)));
        var plan = new AiPlan();
        new DoorAi(bot, brain.Follower, team == PawnTeam.Spy).Steer(world.Now, ref plan);

        // The bot sees the closed mover before entering the sensor. Allowed pawns must keep approaching it;
        // others must mark the crossing blocked instead of assuming a sensor of the wrong class will open it.
        if (permitted)
        {
            Assert.Empty(world.BadEdges(team == PawnTeam.Spy));
            Assert.False(brain.Follower.Failed);
        }
        else
        {
            Assert.NotEmpty(world.BadEdges(team == PawnTeam.Spy));
        }
    }
}
