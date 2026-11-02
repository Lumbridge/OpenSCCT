using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

/// <summary>A scored option of a utility choice.</summary>
public readonly record struct AiOption(int Id, float Score);

/// <summary>
/// The base of the side brains: a utility choice among a few goals every decision period (with the difficulty's
/// noise and a bonus for the current goal, so the bot does not flicker), and the shared skills: walking a route,
/// looking at a point, using a terminal, picking gadgets from the pawn's list.
/// </summary>
public abstract class AiBrain
{
    private float _nextDecision;
    private float _failedAt = -10f;
    private readonly List<int> _scratchNodes = new();

    protected AiBrain(AiController bot)
    {
        Bot = bot ?? throw new ArgumentNullException(nameof(bot));
        Follower = new NavFollower(bot.World, Spy);
        Doors = new DoorAi(bot, Follower, Spy);
    }

    public AiController Bot { get; }

    protected AiWorld World => Bot.World;

    protected AiDifficulty Difficulty => Bot.Difficulty;

    protected SPlayerController Pc => Bot.Controller;

    protected SBasePawn Pawn => Bot.Pawn!;

    protected abstract bool Spy { get; }

    public NavFollower Follower { get; }

    /// <summary>Closed doors on the route: opened through their switch, waited for, or planned round.</summary>
    public DoorAi Doors { get; }

    /// <summary>The goal chosen last (a brain's own id).</summary>
    public int Goal { get; private set; } = -1;

    public float GoalSince { get; private set; }

    /// <summary>Times each goal was chosen (diagnostics), by id.</summary>
    public int[] GoalCounts { get; } = new int[16];

    public abstract string Status { get; }

    /// <summary>One tick: decide when due, then act on the goal.</summary>
    public void Think(float now, ref AiPlan plan)
    {
        // Finish a traversal before a tactical decision can clear its path halfway up a spawn fence.
        if (Follower.ActiveLink >= 0 || NavFollower.OnFence(Pawn))
        {
            NavCommand command = Follower.Steer(Pawn, now, Pc.Rotation.Yaw & 0xFFFF, sneak: false);
            plan.Keys = command.Keys;
            plan.Yaw = command.Yaw;
            plan.Tolerance = command.Tolerance;
            plan.Crouched = command.Crouched ?? plan.Crouched;
            return;
        }

        if (now >= _nextDecision)
        {
            _nextDecision = now + Difficulty.DecisionPeriod;
            int choice = Choose(now);
            if (choice != Goal)
            {
                LeaveGoal(Goal, choice, now);
                Goal = choice;
                GoalSince = now;
                if (choice >= 0 && choice < GoalCounts.Length)
                {
                    GoalCounts[choice]++;
                }
            }
        }

        Act(now, ref plan);
    }

    /// <summary>Scores the options and picks one, with the difficulty's noise and a bonus for staying.</summary>
    protected int Pick(ReadOnlySpan<AiOption> options)
    {
        int best = -1;
        float bestScore = float.MinValue;
        foreach (AiOption o in options)
        {
            if (o.Score <= 0f)
            {
                continue;
            }

            float score = o.Score + (o.Id == Goal ? 0.12f : 0f) + (Bot.Random.FRand() - 0.5f) * 2f * Difficulty.DecisionNoise;
            if (score > bestScore)
            {
                bestScore = score;
                best = o.Id;
            }
        }

        return best;
    }

    protected abstract int Choose(float now);

    protected abstract void Act(float now, ref AiPlan plan);

    /// <summary>The goal changes from <paramref name="from"/> to <paramref name="to"/>.</summary>
    protected virtual void LeaveGoal(int from, int to, float now)
    {
    }

    public virtual void Reset()
    {
        Follower.Clear();
        Doors.Reset();
        Goal = -1;
    }

    // ---- skills ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Walks toward <paramref name="floor"/>: plans when the goal moved more than 48 uu or the last plan is done, then
    /// steers. False when the goal cannot be reached.
    /// </summary>
    protected bool MoveTo(FVector floor, float now, ref AiPlan plan, bool sneak, float stealth = 0f, Func<int, float>? penalty = null)
    {
        bool moved = !Follower.HasGoal || NavFollower.Horizontal(Follower.Goal - floor) > 48f || MathF.Abs(Follower.Goal.Z - floor.Z) > 60f;
        if (moved || Follower.Failed)
        {
            // A goal that just failed is not planned again every tick.
            if (!moved && now - _failedAt < 1.5f)
            {
                return false;
            }

            if (this is SpyAi spy)
            {
                Func<int, float>? other = penalty;
                penalty = n => (other?.Invoke(n) ?? 0f) + spy.Security.Penalty(Bot, Follower.Graph.Node(n).Floor);
            }

            if (!Follower.SetGoal(Pawn, floor, stealth, penalty))
            {
                _failedAt = now;
                return false;
            }
        }

        // A closed door on the route takes the keys while it is opened or waited for.
        if (Doors.Steer(now, ref plan))
        {
            return true;
        }

        NavCommand c = Follower.Steer(Pawn, now, Pc.Rotation.Yaw & 0xFFFF, sneak);
        plan.Keys = c.Keys;
        plan.Yaw = c.Yaw ?? plan.Yaw;
        plan.Tolerance = c.Tolerance;
        plan.Crouched = c.Crouched ?? plan.Crouched;
        return !Follower.Failed;
    }

    /// <summary>
    /// Turns the view to <paramref name="point"/> from the eye. A move already planned this tick goes on in its own
    /// direction (camera-relative keys).
    /// </summary>
    protected void LookAt(FVector point, ref AiPlan plan)
    {
        FRotator r = (point - AiSight.Eye(Pawn)).Rotation();
        if (plan.Keys.BT_Up != 0 && plan.Yaw is { } moving && plan.MoveYaw is null)
        {
            plan.MoveYaw = moving;
        }

        plan.Yaw = r.Yaw & 0xFFFF;
        plan.Pitch = r.Pitch;
        plan.Tolerance = 65536;
    }

    /// <summary>The yaw error (degrees) between the view and the direction to <paramref name="point"/>.</summary>
    protected float AimErrorTo(FVector point)
    {
        FVector to = point - AiSight.Eye(Pawn);
        float len = to.Size();
        if (len < 1f)
        {
            return 0f;
        }

        float cos = (to | Pc.Rotation.Vector()) / len;
        return SvmMath.Acos(SvmMath.Clamp(cos, -1f, 1f)) * (180f / MathF.PI);
    }

    /// <summary>The floor point and yaw of a terminal's use spot on the side's graph.</summary>
    protected bool UseSpot(SComputerObjectiveTrigger terminal, out FVector floor, out int yaw)
    {
        terminal.GetUseSpot(Pawn, out FVector spot, out FVector facing);
        yaw = facing.Rotation().Yaw & 0xFFFF;
        // A nearby graph sample may lie on the terminal or container roof. Probe the actual
        // interaction point so the final walk targets its supporting floor, not that surface.
        FVector top = new(spot.X, spot.Y, terminal.Location.Z + Follower.Graph.Profile.StepUp + 2f);
        FVector bottom = new(spot.X, spot.Y, terminal.Location.Z - 200f);
        const ETraceFlags flags = ETraceFlags.TRACE_Blocking | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
            ETraceFlags.TRACE_Volumes | ETraceFlags.TRACE_Others;
        if (!World.Collision.LineCheck(out FCheckResult hit, NavBuilder.CollisionProbe, bottom, top, new FVector(4f, 4f, 2f), flags) ||
            hit.bStartPenetrating || hit.Normal.Z < Follower.Graph.Profile.MinFloorNormalZ)
        {
            floor = default;
            return false;
        }

        floor = new FVector(spot.X, spot.Y, hit.Location.Z - 2f);
        return true;
    }

    /// <summary>The slot (0 to 3) of the first gadget of type <typeparamref name="T"/> among the selectable ones, or -1.</summary>
    protected int GadgetSlot<T>(out T? gadget) where T : SGadget
    {
        int slot = 0;
        for (SGadget? g = Pawn.MyGadget; g is not null && slot < 8; g = g.MyGadget)
        {
            if (!PawnInventory.IsSelectable(g))
            {
                continue;
            }

            if (g is T t && !g.bDeleteMe)
            {
                gadget = t;
                return slot;
            }

            slot++;
        }

        gadget = null;
        return -1;
    }

    /// <summary>A danger cost per node from the contacts of <paramref name="memory"/> (the nodes near believed enemies).</summary>
    protected Func<int, float>? DangerPenalty(float scale)
    {
        if (scale <= 0f || Bot.Memory.Contacts.Count == 0)
        {
            return null;
        }

        NavGraph g = Follower.Graph;
        _danger.Clear();
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (c.Confidence > 0.2f)
            {
                _danger.Add((c.Position, c.Confidence));
            }
        }

        if (_danger.Count == 0)
        {
            return null;
        }

        var danger = _danger.ToArray();
        return n =>
        {
            FVector p = g.Node(n).Floor;
            float cost = 0f;
            foreach ((FVector at, float conf) in danger)
            {
                float d2 = FVector.DistSquared(p, at);
                if (d2 < 900f * 900f)
                {
                    cost += scale * conf * (1f - SvmMath.Sqrt(d2) / 900f) * 60f;
                }
            }

            return cost;
        };
    }

    private readonly List<(FVector, float)> _danger = new();

    /// <summary>Nodes of the side's graph within <paramref name="radius"/> of the pawn (sorted by index).</summary>
    protected List<int> NodesAround(FVector center, float radius)
    {
        Follower.Graph.NodesNear(center, radius, _scratchNodes);
        return _scratchNodes;
    }

    /// <summary>Feet of the pawn.</summary>
    protected FVector Feet => NavFollower.Feet(Pawn);
}
