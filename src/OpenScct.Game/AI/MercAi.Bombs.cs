using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

public sealed partial class MercAi
{
    private SBombTargetObjectiveTrigger? _bomb;
    private bool _bombMenuDown;
    private float _bombPressAt;
    private FVector _bombFloor;

    private SBombTargetObjectiveTrigger? ArmedBomb()
    {
        SBombTargetObjectiveTrigger? best = null;
        float distance = float.MaxValue;
        NavGraph graph = Follower.Graph;
        int start = graph.NearestNode(Pawn.Location, Pawn.CollisionHeight);
        float[]? costs = null;
        // Planted bombs announce their location to defenders, just as the objective HUD does.
        foreach (SBombTargetObjectiveTrigger bomb in World.Level.AllActors<SBombTargetObjectiveTrigger>())
        {
            if (bomb.bDeleteMe || !bomb.bActive || !bomb.bTriggering || bomb.InteractionObj is not { } entry) continue;
            costs ??= graph.CostsFrom(start, badLinks: World.BadLinks(false), badEdges: World.BadEdges(false));
            var nodes = new List<int>();
            graph.NodesNear(entry.Location, Math.Max(140f, entry.CollisionRadius + Pawn.CollisionRadius), nodes);
            foreach (int node in nodes)
            {
                if (!float.IsFinite(costs[node]) || costs[node] >= distance) continue;
                FVector floor = graph.Node(node).Floor;
                FVector center = floor + new FVector(0, 0, Pawn.CollisionHeight);
                if (NavFollower.Horizontal(center - entry.Location) > entry.CollisionRadius + Pawn.CollisionRadius - 8f ||
                    MathF.Abs(center.Z - entry.Location.Z) > entry.CollisionHeight + Pawn.CollisionHeight - 4f ||
                    !World.LineOpen(center, entry.Location)) continue;
                best = bomb;
                distance = costs[node];
                _bombFloor = floor;
            }
        }
        return best;
    }

    private bool ImmediateBombThreat()
    {
        foreach (AiContact contact in Bot.Memory.Contacts)
            if (contact.Visible && contact.Confirmed && contact.Pose is not { Downed: true } &&
                (contact.Position - Pawn.Location).SizeSquared() < 250f * 250f) return true;
        return false;
    }

    private void Defuse(float now, ref AiPlan plan)
    {
        if (_bomb is not { bActive: true, bTriggering: true, bDeleteMe: false } bomb ||
            bomb.InteractionObj is not { } entry) return;
        if (Pc.IsInState(new Name("s_MenuInteraction")))
        {
            if (ReferenceEquals(Pc.IManager?.GetCurrentInteraction(), entry))
            {
                // Release Use on Remove bomb; the normal interaction validates and defuses it.
                plan.Keys.bUsing = 0;
                _bombMenuDown = false;
            }
            else
            {
                plan.Keys.bUsing = 1;
                _bombMenuDown = !_bombMenuDown;
                plan.Keys.BT_Down = (byte)(_bombMenuDown ? 1 : 0);
            }
            return;
        }
        if ((Feet - _bombFloor).Size() > 35f)
        {
            MoveTo(_bombFloor, now, ref plan, sneak: false);
            return;
        }
        LookAt(bomb.Location, ref plan);
        bool listed = false;
        if (Pc.IManager is { } manager)
            foreach (SInteractObject? item in manager.Interactions)
                if (ReferenceEquals(item, entry)) { listed = true; break; }
        if (!listed || !entry.CanUse(Pc))
        {
            // Close the remaining gap at walking speed without abandoning the urgent objective.
            if (AimErrorTo(bomb.Location) < 15f) plan.Keys.BT_Up = 1;
            return;
        }
        if (AimErrorTo(bomb.Location) < 15f && now - _bombPressAt > .3f)
        {
            plan.Keys.bUsing = 1;
            _bombPressAt = now;
        }
    }
}
