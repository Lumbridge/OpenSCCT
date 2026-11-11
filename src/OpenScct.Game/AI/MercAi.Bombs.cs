using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
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
            costs ??= ReachabilityCosts(start);
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

    /// <summary>
    /// A spy on his feet in sight who must be dealt with before the charge: within 250 uu whatever the line, or
    /// within 800 uu with a clear shot (he is guarding his bomb; walking past him to it gets the merc grabbed or shocked).
    /// </summary>
    private bool ImmediateBombThreat()
    {
        foreach (AiContact contact in Bot.Memory.Contacts)
        {
            if (!contact.Visible || !contact.Confirmed || contact.Pose is { Downed: true }) continue;
            float d2 = (contact.Position - Pawn.Location).SizeSquared();
            if (d2 < 250f * 250f || d2 < AiK.BombThreatRange * AiK.BombThreatRange && !ShotBlocked(contact.Position)) return true;
        }

        return false;
    }

    /// <summary>
    /// The charge is this merc's to deal with: it has a bullet line to it from where it stands, or no other merc
    /// bot on its feet is clearly nearer. The others keep to what they were doing, so one bomb does not empty
    /// every other objective.
    /// </summary>
    private bool BombIsMine(SBombTargetObjectiveTrigger bomb)
    {
        if (Difficulty.Teamwork <= .3f || CanShootBomb(bomb)) return true;
        float mine = (bomb.Location - Pawn.Location).Size();
        foreach (AiController mate in World.Bots)
        {
            if (ReferenceEquals(mate, Bot) || mate.Team != 1 ||
                mate.Pawn is not SPAWNDEFENSE { Health: > 0, bDeleteMe: false, bSleeping: false, bLyingOnGround: false } other) continue;
            if ((bomb.Location - other.Location).Size() + AiK.BombNearerMargin < mine) return false;
        }

        return true;
    }

    private SBombTargetObjectiveTrigger? _bombShotAt;
    private int _bombShots;
    private float _bombShotTime = -10f;
    private float _bombAimSince = -1f;

    /// <summary>A bullet from the rifle reaches the charge from here (any damage defuses it), within the rifle's range.</summary>
    private bool CanShootBomb(SBombTargetObjectiveTrigger bomb)
    {
        if (Pawn.MyWeapon is not SWeaponDefense || Pawn.Physics != EPhysics.PHYS_Walking) return false;
        if (ReferenceEquals(bomb, _bombShotAt) && _bombShots >= AiK.BombShotTries) return false;
        float d = (bomb.Location - AiSight.Eye(Pawn)).Size();
        return d > 120f && d < EngageRange && !ShotBlocked(bomb.Location);
    }

    /// <summary>
    /// Shooting the charge: stand, aim at it, through the scope when it is far and the rifle has one, and fire single
    /// shots once the aim has been on it for the bot's reaction time. After a few shots that did not defuse it the
    /// merc walks to it instead.
    /// </summary>
    private void ShootBomb(SBombTargetObjectiveTrigger bomb, float now, ref AiPlan plan)
    {
        if (!ReferenceEquals(bomb, _bombShotAt))
        {
            _bombShotAt = bomb;
            _bombShots = 0;
            _bombAimSince = -1f;
        }

        Follower.Clear();
        plan.Keys = default;
        plan.MoveYaw = null;
        LookAt(bomb.Location, ref plan);
        if (Pawn.MyWeapon is not SWeaponDefense rifle) return;
        if (rifle.ClipAmmo <= 0)
        {
            plan.Keys.bReload = 1;
            return;
        }

        float d = (bomb.Location - AiSight.Eye(Pawn)).Size();
        bool scope = CanSnipe && d > AiK.ScopeEnterRange && now - Bot.HurtAt > 2f;
        if (scope) _scopeWantAt = now;
        if (scope && !Scoped) return;

        // The charge is a hand's width: the aim must be within it at this distance.
        float within = MathF.Max(0.35f, SvmMath.Atan2(AiK.BombHalfSize, d) * (180f / MathF.PI));
        if (AimErrorTo(bomb.Location) > within)
        {
            _bombAimSince = -1f;
            return;
        }

        if (_bombAimSince < 0f) _bombAimSince = now;
        if (now - _bombAimSince < Bot.ReactionTime || now - _bombShotTime < AiK.ScopeShotSeconds || TeammateInTheWay(bomb.Location)) return;
        _bombShotTime = now;
        _bombShots++;
        if (scope) ScopedShots++;
        plan.Keys.Press(AiCommand.Fire);
        plan.Keys.bFire = 1;
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
        if ((Feet - _bombFloor).Size() > 120f && CanShootBomb(bomb))
        {
            ShootBomb(bomb, now, ref plan);
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
