using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>What a brain sees each frame: its client, controller, pawn and the shared walk graphs.</summary>
public sealed class BotContext
{
    internal BotContext(ClientSession session, NavCache nav)
    {
        Session = session;
        Nav = nav;
    }

    public ClientSession Session { get; }

    public NavCache Nav { get; }

    public World World => Session.World;

    public float Now => Session.World.TimeSeconds;

    public SPlayerController Controller { get; internal set; } = null!;

    public SBasePawn Pawn { get; internal set; } = null!;

    /// <summary>The client's map (loaded once Welcome arrived).</summary>
    public MapInfo Map => Session.Map!;

    /// <summary>The replicated game state, once its channel opened.</summary>
    public GameReplicationInfo? Gri
    {
        get
        {
            foreach (GameReplicationInfo gri in World.Level.AllActors<GameReplicationInfo>())
            {
                return gri;
            }

            return null;
        }
    }

    /// <summary>Line of sight between two points on the client's map (BSP and static geometry).</summary>
    public bool CanSee(FVector from, FVector to) => Map.Physics?.Collision.FastTrace(to, from) ?? true;
}

/// <summary>A bot's decisions: one <see cref="BotInput"/> per frame while it has a pawn.</summary>
public abstract class BotBrain
{
    /// <summary>The input for this frame.</summary>
    public abstract BotInput Think(BotContext context);

    /// <summary>A one-line description of what the brain is doing (diagnostics).</summary>
    public virtual string Status => GetType().Name;

    /// <summary>The local player has no pawn (waiting, dead, spectating): forget the current plan.</summary>
    public virtual void Reset()
    {
    }

    /// <summary>Terminals of the client's map, in the level's actor order.</summary>
    protected static List<SComputerObjectiveTrigger> Terminals(BotContext context)
    {
        var list = new List<SComputerObjectiveTrigger>();
        foreach (SComputerObjectiveTrigger t in context.World.Level.AllActors<SComputerObjectiveTrigger>())
        {
            list.Add(t);
        }

        return list;
    }

    /// <summary>True when the terminal's objective is taken (its screen shows hacked, or its objective says so).</summary>
    public static bool IsTaken(BotContext context, SComputerObjectiveTrigger terminal)
    {
        if (terminal.SkinState == SComputerObjectiveTrigger.SKIN_Hacked)
        {
            return true;
        }

        foreach (SObjective o in context.World.Level.AllActors<SObjective>())
        {
            if (o.ObjectiveTaken && o.Triggers.Contains(terminal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The floor point a walker stands on to use <paramref name="terminal"/> (its use spot).</summary>
    protected static bool UseSpotFloor(BotContext context, NavGrid grid, SComputerObjectiveTrigger terminal, out FVector floor, out int yaw)
    {
        terminal.GetUseSpot(context.Pawn, out FVector spot, out FVector facing);
        yaw = facing.Rotation().Yaw;
        int node = grid.NearestNode(new FVector(spot.X, spot.Y, terminal.Location.Z), 0f);
        if (node < 0)
        {
            floor = default;
            return false;
        }

        floor = new FVector(spot.X, spot.Y, grid.Nodes[node].Z);
        return true;
    }

    /// <summary>Path length (uu) from the pawn to <paramref name="floor"/>, or infinity when unreachable.</summary>
    protected static float PathLength(NavGrid grid, Pawn pawn, FVector floor, List<FVector> scratch)
    {
        int from = grid.NearestNode(pawn.Location, pawn.CollisionHeight);
        int to = grid.NearestNode(floor + new FVector(0f, 0f, 1f), 0f);
        if (!grid.FindPath(from, to, scratch))
        {
            return float.PositiveInfinity;
        }

        float length = 0f;
        FVector at = pawn.Location;
        foreach (FVector p in scratch)
        {
            length += BotNavigator.Horizontal(p - at) + MathF.Abs(p.Z - at.Z);
            at = p;
        }

        return length;
    }
}
