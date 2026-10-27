using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Team;

/// <summary>
/// A bot helps its team mate through the keys (TeamAi): it heals him, takes his spy mark off and revives him through
/// the Use key and menu, climbs on his boost with the co-op key and offers him a boost. The player is the rig's own
/// pawn; the bot drives the mate's controller with its own input each tick.
/// </summary>
public sealed class TeamAiTests
{
    private readonly ITestOutputHelper _output;

    public TeamAiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The bot drives <paramref name="mate"/>: its input goes on the controller and is held for the rig's step.</summary>
    private sealed class BotMate
    {
        public BotMate(PawnRig rig, RigMate mate, AiWorld world, AiSkill skill = AiSkill.Normal)
        {
            Rig = rig;
            Mate = mate;
            Bot = new AiController(world, mate.Controller, AiDifficulty.For(skill), 0);
        }

        public PawnRig Rig { get; }

        public RigMate Mate { get; }

        public AiController Bot { get; }

        public List<string> Trace { get; } = new();

        /// <summary>Steps until <paramref name="done"/> (true) or <paramref name="seconds"/> run out (false).</summary>
        public bool Run(float seconds, System.Func<bool> done, PlayerInputFrame player = default)
        {
            string last = string.Empty;
            for (int i = 0; i < (int)(seconds * Sim.Hz); i++)
            {
                Bot.Tick();
                AiInput a = Bot.LastInput;
                Mate.Input = new PlayerInputFrame
                {
                    BT_Up = a.BT_Up,
                    BT_Down = a.BT_Down,
                    BT_Left = a.BT_Left,
                    BT_Right = a.BT_Right,
                    bCrouch = a.bCrouch,
                    bRoll = a.bRoll,
                    bUsing = a.bUsing,
                    bFireMode = a.bFireMode,
                    bUseBackToWall = a.bUseBackToWall,
                    View = a.View,
                    FastSpeed = a.FastSpeed,
                };
                Rig.Step(player);
                string s = $"{Bot.TeamPlay.Status} / {Mate.Controller.GetStateName()} at {Mate.Pawn.Location}";
                if (s != last)
                {
                    Trace.Add($"{Rig.Level.TimeSeconds:F2} {s}");
                    last = s;
                }

                if (done())
                {
                    return true;
                }
            }

            return false;
        }
    }

    private BotMate Setup(PawnRig rig, FVector mateFloor, int mateYaw, CollisionDataBuilder? level = null, TraversalDoc? ges = null)
    {
        OpenScct.Game.Equipment.CombatEquipment.Install(rig.Level);
        RigMate mate = rig.AddMate(rig.Team, mateFloor, mateYaw);
        InteractionMenuRules.EnsureManager(rig.Controller);
        InteractionMenuRules.EnsureManager(mate.Controller);
        AiWorld world = OpenScct.Game.Tests.AI.AiTestKit.World(rig, V(-1500f, -1500f, -100f), V(1500f, 1500f, 800f), ges);
        rig.Hold(NoInput, 10);
        return new BotMate(rig, mate, world);
    }

    private void Dump(BotMate b)
    {
        foreach (string line in b.Trace)
        {
            _output.WriteLine(line);
        }
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void TheBotWalksUpAndHealsAHurtPlayerThroughTheUseMenu(PawnTeam team)
    {
        PawnRig rig = Rig(team, yaw: 0);
        BotMate b = Setup(rig, V(-500f, 400f, 0f), 0);
        int full = SHealMateInteraction.DefaultHealth(rig.Pawn);
        rig.Pawn.Health = 40;
        bool healed = b.Run(20f, () => rig.Pawn.Health == full);
        Dump(b);
        Assert.True(healed, b.Bot.Status);
        Assert.Equal(1, b.Bot.TeamPlay.Done[(int)TeamGoal.Heal] + (b.Bot.TeamPlay.Goal == TeamGoal.Heal ? 1 : 0));
    }

    [Fact]
    public void TheBotTakesTheSpyMarkOffThePlayer()
    {
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 16384);
        BotMate b = Setup(rig, V(400f, -300f, 0f), 0);
        rig.Pawn.HitBySpyBullet(1);
        bool off = b.Run(20f, () => !rig.Pawn.bSpyBulletOn);
        Dump(b);
        Assert.True(off, b.Bot.Status);
    }

    [Fact]
    public void TheBotRevivesTheKnockedOutPlayer()
    {
        Assert.SkipWhen(ContentOptions() is null, "the converted characters (clip lengths) are not in .content");
        PawnRig rig = Rig(PawnTeam.Spy, yaw: 16384, options: ContentOptions());
        BotMate b = Setup(rig, V(600f, 300f, 0f), 0);
        rig.Controller.Handle_DropAttackedDmg(b.Mate.Pawn);
        bool lying = b.Run(10f, () => rig.Controller.IsInState(new Name("s_LyingOnGround")));
        Assert.True(lying, rig.Controller.GetStateName().ToString());
        bool revived = b.Run(20f, () => rig.Controller.IsInState(new Name("s_Reanimating")));
        Dump(b);
        Assert.True(revived, b.Bot.Status + " / player " + rig.Controller.GetStateName());
        Assert.True(b.Run(10f, () => rig.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", System.StringComparison.Ordinal)));
    }

    /// <summary>A wall from y = 100 whose top is at <paramref name="top"/>, with an LG along its front edge.</summary>
    private static (CollisionDataBuilder Level, TraversalDoc Ges) Wall(float top)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-500f, 100f, 0f), V(500f, 700f, top));
        return (b, Doc(Ge(0, GEKind.LG, V(-500f, 100f, top), V(500f, 100f, top))));
    }

    [Fact]
    public void TheBotClimbsOnThePlayersBoost()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 55f, 0f), yaw: -16384);
        BotMate b = Setup(rig, V(-400f, -500f, 0f), 16384, level, ges);
        // The player offers the boost (T at the foot of the high ledge) and waits ready.
        rig.Step(new PlayerInputFrame { CoopMove = true });
        bool ready = b.Run(5f, () => rig.Pawn.bReadyForShortLadder);
        Assert.True(ready, rig.Controller.GetStateName().ToString());
        bool boosted = b.Run(25f, () => b.Mate.Controller.IsInState(MovementStateNames.s_ShortLadder_S_Jump));
        Dump(b);
        Assert.True(boosted, b.Bot.Status);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_ShortLadder_P_WaitForShortLadder), rig.Controller.GetStateName().ToString());
    }

    [Fact]
    public void TheBotClimbsOnThePlayersHoist()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(503f);
        // The player drops beside the wall, catches the ledge and hangs (as CoopTests' hoist rig).
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 100f - 39f, 310f), yaw: 16384);
        for (int i = 0; i < 300 && !rig.Controller.IsInState(MovementStateNames.OnLedge); i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge), rig.Controller.GetStateName().ToString());
        BotMate b = Setup(rig, V(-400f, -500f, 0f), 16384, level, ges);
        rig.Step(new PlayerInputFrame { CoopMove = true });
        Assert.True(b.Run(3f, () => rig.Pawn.bReadyForHoistClimb), rig.Controller.GetStateName().ToString());
        bool hoisted = b.Run(25f, () => b.Mate.Controller.IsInState(MovementStateNames.s_Hoist_S_Jump));
        Dump(b);
        Assert.True(hoisted, b.Bot.Status);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_Hoist_P_WaitForHoist), rig.Controller.GetStateName().ToString());
        // The pull and the finish clips play (the rig's built-in clips carry no root motion, so the bot stays on the
        // floor, see CoopTests), the bot ends crouched and the player hangs again.
        Assert.True(b.Run(15f, () => b.Mate.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H) &&
            rig.Controller.IsInState(MovementStateNames.OnLedge)), b.Mate.Controller.GetStateName() + " / player " + rig.Controller.GetStateName());
    }

    /// <summary>
    /// Playtest (MounE): the player hung hurt from a ledge, the bot took a heal goal on him, walked off an edge, caught a
    /// ledge itself and hung there until the goal timed out (25 s). The menu's mate search wants him on his feet, so a
    /// hanging mate is not a heal goal; he is once he walks again.
    /// </summary>
    [Fact]
    public void TheBotWaitsForAHurtPlayerHangingFromALedgeToStandBeforeHealingHim()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(503f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 100f - 39f, 310f), yaw: 16384);
        for (int i = 0; i < 300 && !rig.Controller.IsInState(MovementStateNames.OnLedge); i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge), rig.Controller.GetStateName().ToString());
        BotMate b = Setup(rig, V(-400f, -500f, 0f), 16384, level, ges);
        rig.Pawn.Health = 40;
        // Past the 6 s a mate who just lost health is left alone (he is in a fight).
        bool tookHeal = b.Run(9f, () => b.Bot.TeamPlay.Goal == TeamGoal.Heal);
        Dump(b);
        Assert.False(tookHeal, b.Bot.Status);
        Assert.Equal(0, b.Bot.TeamPlay.Done[(int)TeamGoal.Heal]);
        Assert.Equal(EPhysics.PHYS_Ledge, rig.Pawn.Physics);
    }

    /// <summary>
    /// Smoke (play-team on SteeE, about 1 spy leg in 5): the bot sent to heal the player came up out of the pit under
    /// pipe GE218 by the pipe, and held it without climbing for the rest of the goal: the menu goal gave no keys while
    /// the bot was not walking. A pipe of the route is climbed, then the bot walks up and heals him.
    /// </summary>
    [Fact]
    public void TheBotClimbsAPipeOfItsRouteToHealThePlayerAbove()
    {
        // A floor, a platform 600 high from y = 230, and a pipe up its front (as the traversal tests' pipe rig).
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-300f, 230f, 0f), V(300f, 900f, 600f));
        TraversalDoc ges = Doc(Ge(0, GEKind.PIPE, V(0f, 200f, 0f), V(0f, 200f, 600f), V(0f, 190f, 0f)));
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 600f, 600f), yaw: 16384);
        BotMate b = Setup(rig, V(0f, -300f, 0f), 16384, level, ges);
        int full = SHealMateInteraction.DefaultHealth(rig.Pawn);
        rig.Pawn.Health = 40;
        bool climbed = false;
        bool healed = b.Run(40f, () =>
        {
            climbed |= b.Mate.Pawn.Physics == EPhysics.PHYS_OnPipe;
            return rig.Pawn.Health == full;
        });
        Dump(b);
        Assert.True(climbed, "the bot never took the pipe: " + b.Bot.Status);
        Assert.True(healed, b.Bot.Status + " / " + b.Mate.Controller.GetStateName() + " at " + b.Mate.Pawn.Location);
    }

    /// <summary>
    /// The play-team smoke on SteeE, once the player's own boost reached its ledge: the bot, a floor below, took a
    /// ledge of its route to the hurt player, and each time it hung there the menu goal steered it at the player
    /// himself. The new route was planned from the hang, its first point on the floor below, so the bot let go,
    /// jumped, caught the ledge and let go again until the goal timed out. Hanging on the route, the route's own
    /// goal goes on and the bot pulls up.
    /// </summary>
    [Fact]
    public void TheBotPullsUpALedgeOfItsRouteToHealThePlayerAbove()
    {
        // A floor and a block 250 high from y = 100 with an LG on its front lip; the player on top, facing away.
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-400f, 100f, 0f), V(400f, 900f, 250f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-400f, 100f, 250f), V(400f, 100f, 250f)));
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 500f, 250f), yaw: 16384);
        BotMate b = Setup(rig, V(0f, -300f, 0f), 16384, level, ges);
        int full = SHealMateInteraction.DefaultHealth(rig.Pawn);
        rig.Pawn.Health = 40;
        int hangs = 0;
        bool hanging = false;
        bool healed = b.Run(40f, () =>
        {
            bool now = b.Mate.Pawn.Physics == EPhysics.PHYS_Ledge;
            if (now && !hanging)
            {
                hangs++;
            }

            hanging = now;
            return rig.Pawn.Health == full;
        });
        Dump(b);
        Assert.True(hangs >= 1, "the bot never took the ledge: " + b.Bot.Status);
        Assert.True(healed, $"{b.Bot.Status} / {b.Mate.Controller.GetStateName()} at {b.Mate.Pawn.Location}, ledge catches {hangs}");
        Assert.Equal(1, hangs);
    }

    [Fact]
    public void TheBotOffersABoostToAPlayerWaitingUnderAHighLedge()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        // The player stands at the foot of the wall looking up at the ledge.
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(120f, -20f, 0f), yaw: 16384);
        rig.SetView(6000, 16384);
        BotMate b = Setup(rig, V(-500f, -500f, 0f), 16384, level, ges);
        PlayerInputFrame lookUp = new() { View = new FRotator(6000, 16384, 0) };
        rig.Step(lookUp);
        Assert.True(rig.Driver.CheckIfCanStartSHL(rig.Pawn, true, out FVector spotLoc, out _, out _, out _), "no boost spot by the player");
        _output.WriteLine($"spot {spotLoc} player {rig.Pawn.Location} pitch {rig.Controller.Rotation.Pitch} viewport {rig.Controller.Player?.GetType().Name} vel {rig.Pawn.Velocity}");
        bool helper = b.Run(25f, () => b.Mate.Pawn.bReadyForShortLadder, lookUp);
        Dump(b);
        Assert.True(helper, b.Bot.Status);
        Assert.True(b.Mate.Controller.IsInState(MovementStateNames.s_ShortLadder_P_Waiting), b.Mate.Controller.GetStateName().ToString());
        // The player steps in front of him (the climber's side) and climbs with the co-op key.
        FVector front = b.Mate.Pawn.Location + (b.Mate.Pawn.Rotation.Vector() * 160f);
        rig.Teleport(new FVector(front.X, front.Y, 0f), (b.Mate.Pawn.Rotation.Yaw + 32768) & 0xFFFF);
        rig.Step(NoInput);
        Assert.Equal(SPlayerController.CA_ShortLadder, rig.Controller.CheckIfCanInteractWithMate(out _, true));
        rig.Step(new PlayerInputFrame { CoopMove = true });
        Assert.True(b.Run(10f, () => rig.Controller.IsInState(MovementStateNames.s_ShortLadder_S_Jump)), rig.Controller.GetStateName().ToString());
    }
}
