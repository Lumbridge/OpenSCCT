using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using CombatK = OpenScct.Game.Combat.K;

namespace OpenScct.Game.AI;

/// <summary>What a bot does for a team mate (docs/AI.md, "Helping team mates").</summary>
public enum TeamGoal : byte
{
    None,

    /// <summary>A mate lies knocked out: walk up to him, face him, press Use (FindPawnToReanimate).</summary>
    Revive,

    /// <summary>A mate is hurt: stand in front of him, facing him, and pick Heal Mate in the Use menu.</summary>
    Heal,

    /// <summary>A mate carries a spy mark: the same, with Unspy Mate.</summary>
    Unspy,

    /// <summary>A mate crouches ready to boost: walk to him and press the co-op key to be boosted.</summary>
    ClimbBoost,

    /// <summary>A mate hangs ready to hoist: stand under him and press the co-op key.</summary>
    ClimbHoist,

    /// <summary>A human mate waits at the foot of a high ledge: take the boost spot and press the co-op key.</summary>
    OfferBoost,
}

/// <summary>Tuning of the bots' team play (choices of the bots, not game values; docs/AI.md).</summary>
public static class TeamAiK
{
    /// <summary>A mate under this share of his class health is worth healing.</summary>
    public const float HealBelow = 0.8f;

    /// <summary>How far the bot goes to help (path start distance, uu).</summary>
    public const float HelpRange = 2500f;

    /// <summary>How far the bot goes to climb on a ready mate.</summary>
    public const float ClimbRange = 1500f;

    /// <summary>Where the bot stands to use the Use menu on a mate: this far in front of the mate's facing.</summary>
    public const float MenuStandOff = 110f;

    /// <summary>The point the bot walks to before stepping up to a mate along the line he faces.</summary>
    public const float MenuStaging = 190f;

    /// <summary>The staging distances tried, farthest first.</summary>
    public static readonly float[] MenuStagingSteps = [MenuStaging, 165f, 145f];

    /// <summary>How far off that line the bot may be and still walk straight up.</summary>
    public const float MenuLaneWidth = 45f;

    /// <summary>How far behind the bot a back-off looks for floor before it steps (test value: a body and a step).</summary>
    public const float BackOffProbe = 60f;

    /// <summary>The bot stops walking at the mate this close (closer, a walk into him starts the spy's vault).</summary>
    public const float MenuClosest = 125f;

    /// <summary>A Use press for the menu is held this long, then let go for <see cref="UseReleaseSeconds"/> and made again.</summary>
    public const float UsePressSeconds = 0.4f;

    public const float UseReleaseSeconds = 0.15f;

    /// <summary>A bot too close with its body turned away backs off this far before walking up again.</summary>
    public const float MenuBackOff = 175f;

    /// <summary>The body counts as facing the mate within this yaw (65536 a turn, about 16 degrees).</summary>
    public const int BodyFacingTolerance = 3000;

    /// <summary>Where the bot stops before a downed mate (the revive search reaches 250).</summary>
    public const float ReviveStandOff = 110f;

    /// <summary>A human mate who stays this long within reach of a boost spot, looking up, gets a boost offered.</summary>
    public const float OfferWaitSeconds = 1.0f;

    /// <summary>The human mate must be this close to the boost spot (horizontal, uu).</summary>
    public const float OfferRadius = 260f;

    /// <summary>How far along the wall from the mate's own spot the bot takes its boost spot.</summary>
    public const float OfferBeside = 90f;

    /// <summary>A side spot counts as on the floor with a graph node this near it (horizontal, uu) ...</summary>
    public const float SideFloorReach = 48f;

    /// <summary>... and this near its height (uu).</summary>
    public const float SideFloorStep = 24f;

    /// <summary>Looking up: a view pitch above this (65536 a turn, about 11 degrees).</summary>
    public const int OfferLookUpPitch = 2000;

    /// <summary>A helper bot waiting this long with nobody climbing stands down.</summary>
    public const float OfferGiveUpSeconds = 20f;

    /// <summary>After a goal, the same goal on the same mate waits this long.</summary>
    public const float GoalCooldown = 3f;

    /// <summary>An enemy seen this close keeps the bot from healing, unspying or climbing (reviving goes on).</summary>
    public const float DangerRange = 1500f;

    /// <summary>A goal with no route and no open line to its spot for this long is given up for a while.</summary>
    public const float NoRouteSeconds = 3f;

    /// <summary>A goal that has not finished after this long is given up for a while.</summary>
    public const float GoalTimeout = 25f;

    /// <summary>After a mate loses health, this long before the bot goes to heal or unspy him (he is in a fight).</summary>
    public const float MateUnderFireSeconds = 6f;

    /// <summary>
    /// The bot presses Use only this far inside the game's own reach (revive 250, menu 200): the press acts a tick
    /// later, after both pawns moved, and a merc's Use that finds nothing is the berserk spin or the elbow.
    /// </summary>
    public const float ReachMargin = 25f;

    /// <summary>The revive press wants the body facing the mate at least this much (the game takes over -0.5).</summary>
    public const float ReviveFacingDot = -0.2f;

    /// <summary>The menu press wants both facings this much (the game takes over 0.75).</summary>
    public const float MenuFacingDot = 0.8f;

    /// <summary>The menu press waits until the bot and the mate are this slow (uu/s).</summary>
    public const float MenuStillSpeed = 40f;
}

/// <summary>
/// The team play of one bot, consulted before its side's brain every tick: revive a knocked-out mate, heal a hurt
/// one, take a spy mark off one, climb on a mate's boost or hoist, and offer a boost to a human mate waiting at a
/// high ledge. Everything goes through the player's keys and the game's own rules: Use opens the menu (whose mate
/// entries find the mate themselves: 200 uu, in front, facing), S moves the selection, releasing Use acts; Use at a
/// downed mate revives him; the co-op key (T) starts the co-op moves. Godot-free and deterministic.
/// </summary>
public sealed class TeamAi
{
    private static readonly Name NAME_s_MenuInteraction = new("s_MenuInteraction");
    private static readonly Name NAME_OnLedge = new("OnLedge");

    private readonly AiController _bot;
    private NavFollower? _follower;
    private SBasePawn? _target;
    private float _since;
    private float _nextDecision;
    private float _failedPlanAt = -10f;
    private float _lastUse = -10f;
    private float _offerWatch = -1f;
    private SBasePawn? _offerFor;
    private bool _menuDownHeld;
    private bool _backingOff;
    private float _stuckSince = -1f;
    private float _readySince = -1f;
    private float _pressSince = -1f;
    private bool _pressedCoop;
    private float _doneAt = -10f;
    private TeamGoal _doneGoal;
    private SBasePawn? _doneTarget;
    private float _gaveUpAt = -100f;
    private readonly Dictionary<SBasePawn, (int Health, float HurtAt)> _mateHealth = new();

    public TeamAi(AiController bot)
    {
        _bot = bot ?? throw new ArgumentNullException(nameof(bot));
    }

    public TeamGoal Goal { get; private set; }

    public SBasePawn? Target => _target;

    /// <summary>Goals finished (diagnostics), by goal.</summary>
    public int[] Done { get; } = new int[8];

    public string Status => Goal == TeamGoal.None
        ? "team none"
        : string.Create(CultureInfo.InvariantCulture, $"team {Goal} {_target?.PlayerReplicationInfo?.PlayerName ?? _target?.ToString()} {_bot.World.Now - _since:F1}s route {_follower?.NextIndex}/{_follower?.Path.Count}{(_follower?.Failed == true ? " failed" : "")}");

    private SPlayerController Pc => _bot.Controller;

    /// <summary>
    /// One tick. True when a team goal holds the bot this tick (its plan is in <paramref name="plan"/>); false leaves
    /// the tick to the side's brain.
    /// </summary>
    public bool Think(float now, ref AiPlan plan)
    {
        if (_bot.Pawn is not { } pawn || _bot.Difficulty.Teamwork <= 0f)
        {
            Goal = TeamGoal.None;
            return false;
        }

        _follower ??= new NavFollower(_bot.World, pawn is SPawnAttaque);
        if (now >= _nextDecision && !Committed(pawn))
        {
            _nextDecision = now + _bot.Difficulty.DecisionPeriod;
            Choose(pawn, now);
        }

        if (Goal == TeamGoal.None)
        {
            return false;
        }

        // No way to the mate (no route and no open line) for a while, or too long on it: given up for a while.
        bool stuck = _stuckSince >= 0f && now - _stuckSince > TeamAiK.NoRouteSeconds;
        if ((stuck || now - _since > TeamAiK.GoalTimeout) && !Committed(pawn))
        {
            _stuckSince = -1f;
            Finish(now, gaveUp: true);
            return false;
        }

        bool keep = Goal switch
        {
            TeamGoal.Revive => Revive(pawn, now, ref plan),
            TeamGoal.Heal => Menu(pawn, now, ref plan, slot: 2),
            TeamGoal.Unspy => Menu(pawn, now, ref plan, slot: 1),
            TeamGoal.ClimbBoost => Climb(pawn, now, ref plan, hoist: false),
            TeamGoal.ClimbHoist => Climb(pawn, now, ref plan, hoist: true),
            TeamGoal.OfferBoost => Offer(pawn, now, ref plan),
            _ => false,
        };

        if (!keep)
        {
            Finish(now, gaveUp: false);
        }

        return keep;
    }

    /// <summary>The bot is inside a move a goal started (the menu, a revive, a co-op move): no new choice now.</summary>
    private bool Committed(SBasePawn pawn)
    {
        string state = Pc.GetStateName().ToString();
        return state == "s_MenuInteraction" || state.StartsWith("s_ReanimateFriend", StringComparison.Ordinal) ||
            state.StartsWith("s_ShortLadder", StringComparison.Ordinal) || state.StartsWith("s_Hoist", StringComparison.Ordinal) ||
            state.StartsWith("s_Coop", StringComparison.Ordinal) || state.StartsWith("s_ItTarget", StringComparison.Ordinal) ||
            (Goal is TeamGoal.ClimbBoost or TeamGoal.ClimbHoist && _pressedCoop && pawn.Physics != EPhysics.PHYS_Walking);
    }

    // ---- choosing ---------------------------------------------------------------------------------------------

    private void Choose(SBasePawn pawn, float now)
    {
        if (now - _gaveUpAt < TeamAiK.GoalCooldown * 3f)
        {
            return;
        }

        bool danger = EnemyClose(pawn);
        bool spy = pawn is SPawnAttaque;

        // An enemy in sight near the bot calls off a help on the way (only a revive is worth the risk); a move the bot
        // is already inside (the menu, a co-op move) is finished first (the decision is not made while committed).
        if (danger && Goal is not (TeamGoal.None or TeamGoal.Revive))
        {
            Goal = TeamGoal.None;
            _target = null;
            _follower?.Clear();
        }

        TeamGoal best = TeamGoal.None;
        SBasePawn? bestTarget = null;
        float bestScore = 0f;
        foreach (SBasePawn mate in _bot.World.Level.DynamicActors<SBasePawn>())
        {
            if (mate.bDeleteMe || ReferenceEquals(mate, pawn) || mate.SPawnType != pawn.SPawnType || mate.Health <= 0)
            {
                continue;
            }

            float d = (mate.Location - pawn.Location).Size();
            Consider(TeamGoal.Revive, mate, mate.bLyingOnGround && mate.bSleeping && d < TeamAiK.HelpRange, 1.2f, d);
            if (danger)
            {
                continue;
            }

            // A mate who lost health just now is in a fight: the bot does not walk into it to heal him.
            if (UnderFire(mate, now))
            {
                continue;
            }

            // The menu's mate entries want him on his feet in front of the bot: a mate hanging from a ledge, climbing
            // or falling is left until he walks again (a bot sent after a hanging mate hung beside him).
            int full = SHealMateInteraction.DefaultHealth(mate);
            bool onFeet = mate.Physics == EPhysics.PHYS_Walking && !mate.bLyingOnGround;
            Consider(TeamGoal.Unspy, mate, mate.bSpyBulletOn && onFeet && d < TeamAiK.HelpRange, 0.75f, d);
            Consider(TeamGoal.Heal, mate, mate.Health < full * TeamAiK.HealBelow && onFeet && d < TeamAiK.HelpRange,
                0.6f + (0.4f * (1f - (mate.Health / (float)Math.Max(1, full)))), d);
            if (spy && pawn.Physics == EPhysics.PHYS_Walking)
            {
                Consider(TeamGoal.ClimbBoost, mate, mate.bReadyForShortLadder && d < TeamAiK.ClimbRange, 0.7f, d);
                Consider(TeamGoal.ClimbHoist, mate, mate.bReadyForHoistClimb && d < TeamAiK.ClimbRange, 0.7f, d);
                Consider(TeamGoal.OfferBoost, mate, WantsBoost(pawn, mate, now), 0.55f, d);
            }
        }

        if (best == Goal && ReferenceEquals(bestTarget, _target))
        {
            return;
        }

        if (best == TeamGoal.None && Goal != TeamGoal.None)
        {
            // Keep a goal whose own end test runs in its act (the target may still need the last step).
            return;
        }

        Goal = best;
        _target = bestTarget;
        _since = now;
        _pressedCoop = false;
        _menuDownHeld = false;
        _follower?.Clear();

        void Consider(TeamGoal goal, SBasePawn mate, bool wanted, float score, float d)
        {
            if (!wanted || (goal == _doneGoal && ReferenceEquals(mate, _doneTarget) && now - _doneAt < TeamAiK.GoalCooldown))
            {
                return;
            }

            // Teamwork weighs the will to help; nearer mates first.
            float s = score * (0.5f + 0.5f * _bot.Difficulty.Teamwork) * (1f - (0.25f * SvmMath.Min(1f, d / TeamAiK.HelpRange)));
            if (s > bestScore)
            {
                best = goal;
                bestTarget = mate;
                bestScore = s;
            }
        }
    }

    /// <summary>
    /// True while the mate's health dropped within the last <see cref="TeamAiK.MateUnderFireSeconds"/> (the team's
    /// health as the HUD shows it, watched every decision).
    /// </summary>
    private bool UnderFire(SBasePawn mate, float now)
    {
        if (!_mateHealth.TryGetValue(mate, out (int Health, float HurtAt) seen))
        {
            _mateHealth[mate] = (mate.Health, -100f);
            return false;
        }

        if (mate.Health < seen.Health)
        {
            seen.HurtAt = now;
        }

        seen.Health = mate.Health;
        _mateHealth[mate] = seen;
        return now - seen.HurtAt < TeamAiK.MateUnderFireSeconds;
    }

    private bool EnemyClose(SBasePawn pawn)
    {
        foreach (AiContact c in _bot.Memory.Contacts)
        {
            if (c.Visible && c.Confirmed && (c.Position - pawn.Location).Size() < TeamAiK.DangerRange)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A human mate (not a bot) who stands still within reach of a boost spot looking up, for a moment: the bot
    /// offers the boost. A bot mate climbs by its own goals and never asks.
    /// </summary>
    private bool WantsBoost(SBasePawn pawn, SBasePawn mate, float now)
    {
        bool candidate = mate.Controller is SPlayerController { Player: not AiViewport } human && mate.Physics == EPhysics.PHYS_Walking &&
            mate.Velocity.SizeSquared() < 25f * 25f && Pitch(human.Rotation) > TeamAiK.OfferLookUpPitch && BoostSpot(mate, out _, out _);
        if (!candidate)
        {
            if (ReferenceEquals(_offerFor, mate))
            {
                _offerWatch = -1f;
                _offerFor = null;
            }

            return false;
        }

        if (!ReferenceEquals(_offerFor, mate) || _offerWatch < 0f)
        {
            _offerFor = mate;
            _offerWatch = now;
        }

        return now - _offerWatch >= TeamAiK.OfferWaitSeconds && Goal != TeamGoal.ClimbBoost;
    }

    private static int Pitch(FRotator r) => ((r.Pitch + 32768) & 0xFFFF) - 32768;

    /// <summary>The boost spot a helper would take near <paramref name="mate"/> (CheckIfCanStartSHL from the mate's place).</summary>
    private static bool BoostSpot(SBasePawn mate, out FVector loc, out FVector dir)
    {
        loc = default;
        dir = default;
        return MovementNatives.Driver(mate) is { } driver && driver.CheckIfCanStartSHL(mate, true, out loc, out dir, out _, out _) &&
            NavFollower.Horizontal(loc - mate.Location) < TeamAiK.OfferRadius;
    }

    private void Finish(float now, bool gaveUp)
    {
        if (Goal != TeamGoal.None && !gaveUp)
        {
            Done[(int)Goal]++;
        }

        if (gaveUp)
        {
            _gaveUpAt = now;
        }

        _stuckSince = -1f;
        _readySince = -1f;
        _doneGoal = Goal;
        _doneTarget = _target;
        _doneAt = now;
        Goal = TeamGoal.None;
        _target = null;
        _pressedCoop = false;
        _follower?.Clear();
    }

    // ---- acting -------------------------------------------------------------------------------------------------

    private bool Revive(SBasePawn pawn, float now, ref AiPlan plan)
    {
        if (_target is not { } mate || mate.bDeleteMe || mate.Health <= 0)
        {
            return false;
        }

        string state = Pc.GetStateName().ToString();
        if (state.StartsWith("s_ReanimateFriend", StringComparison.Ordinal))
        {
            // The revive clip plays: stand still and watch him.
            LookAt(pawn, mate.Location, ref plan);
            return true;
        }

        // Lying but no longer asleep: he is getting up (revived by this bot or another). A press now finds nobody to
        // revive, and a merc's Use that finds nothing is the berserk spin or the elbow, into the mate: done.
        if (!mate.bLyingOnGround || !mate.bSleeping)
        {
            return false;
        }

        FVector to = mate.Location - pawn.Location;
        float d = NavFollower.Horizontal(to);
        if (d > TeamAiK.ReviveStandOff + 60f)
        {
            FVector stop = mate.Location - (new FVector(to.X, to.Y, 0f).SafeNormal() * TeamAiK.ReviveStandOff);
            MoveTo(pawn, Floor(stop, mate), now, ref plan);
            return true;
        }

        LookAt(pawn, mate.Location, ref plan);
        if (to.Size() >= CombatK.ReviveRange - TeamAiK.ReachMargin && d > 40f)
        {
            // Close on the floor but out of the search's 3D reach (he lies lower or higher): a step closer.
            if (WalkStraight(pawn, Floor(mate.Location, pawn), ref plan))
            {
                plan.Yaw = NavFollower.YawTo(pawn.Location, mate.Location);
            }

            return true;
        }

        if (now - _lastUse > 0.4f && AimError(pawn, mate.Location) < 30f && Walking() && ReviveReach(pawn, mate))
        {
            // Use is a press: the walking states look for a mate to revive on its rising edge. No stick on that tick
            // (a merc's Use with forward held is the charge).
            _lastUse = now;
            plan.Keys = default;
            plan.Keys.bUsing = 1;
        }

        return true;
    }

    /// <summary>
    /// The revive search's own test for this mate (FindPawnToReanimate, knockout-and-terminate.md rule 33: same side,
    /// alive, asleep, within 250 uu, in front of the body), inside <see cref="TeamAiK.ReachMargin"/> and facing him a
    /// little more than the game asks.
    /// </summary>
    public static bool ReviveReach(SBasePawn me, SBasePawn mate)
    {
        ArgumentNullException.ThrowIfNull(me);
        ArgumentNullException.ThrowIfNull(mate);
        return !ReferenceEquals(me, mate) && mate.SPawnType == me.SPawnType && mate.Health > 0 && mate.bSleeping &&
            FVector.Dist(mate.Location, me.Location) < CombatK.ReviveRange - TeamAiK.ReachMargin &&
            ((mate.Location - me.Location).SafeNormal() | me.Rotation.Vector().SafeNormal()) > TeamAiK.ReviveFacingDot;
    }

    /// <summary>
    /// The mate menu entries' reach (<see cref="SMateInteraction.MateInReach"/>: 200 uu, in front, facing each other at
    /// 0.75) with a margin, both pawns nearly still: the menu then opens on the press, never the spin.
    /// </summary>
    private static bool MenuReach(SBasePawn me, SBasePawn mate)
    {
        FVector to = mate.Location - me.Location;
        if (to.Size() >= SMateInteraction.MateRange - TeamAiK.ReachMargin ||
            me.Velocity.SizeSquared() > TeamAiK.MenuStillSpeed * TeamAiK.MenuStillSpeed ||
            mate.Velocity.SizeSquared() > TeamAiK.MenuStillSpeed * TeamAiK.MenuStillSpeed)
        {
            return false;
        }

        FVector facing = me.Rotation.Vector();
        return (to.SafeNormal() | facing) > TeamAiK.MenuFacingDot && -(mate.Rotation.Vector().SafeNormal() | facing) > TeamAiK.MenuFacingDot;
    }

    /// <summary>The controller is in a walking state (the states that act on the Use press).</summary>
    private bool Walking() => Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal);

    /// <summary>
    /// Heal Mate or Unspy Mate: stand in front of the mate, facing him (the mate search wants both), hold Use until
    /// the menu opens, move the selection to the entry with S, release Use.
    /// </summary>
    private bool Menu(SBasePawn pawn, float now, ref AiPlan plan, int slot)
    {
        if (_target is not { } mate || mate.bDeleteMe || mate.Health <= 0 || mate.bLyingOnGround ||
            (mate.Physics != EPhysics.PHYS_Walking && !Pc.IsInState(NAME_s_MenuInteraction)))
        {
            return false;
        }

        bool wanted = slot == 2 ? mate.Health != SHealMateInteraction.DefaultHealth(mate) : mate.bSpyBulletOn;
        // The built-in entries move down as touched objects are inserted ahead of them: find the entry by class.
        SInteractObject? entry = Pc.IManager is { } manager
            ? Array.Find(manager.Interactions, e => slot == 2 ? e is SHealMateInteraction : e is SRemoveSpyBulletInteraction)
            : null;
        if (Pc.IsInState(NAME_s_MenuInteraction))
        {
            _pressSince = -1f;
            plan.Keys.bUsing = 1;
            LookAt(pawn, mate.Location, ref plan);
            if (!wanted || entry is null)
            {
                // Nothing left to do here: release on Exit or whatever is selected leaves the menu.
                plan.Keys.bUsing = 0;
                return wanted;
            }

            SInteractObject? selected = Pc.IManager?.GetCurrentInteraction();
            if (ReferenceEquals(selected, entry))
            {
                plan.Keys.bUsing = 0;
                return true;
            }

            // S moves the selection on its rising edge: press, release, press.
            _menuDownHeld = !_menuDownHeld;
            plan.Keys.BT_Down = _menuDownHeld ? (byte)1 : (byte)0;
            return true;
        }

        if (!wanted)
        {
            return false;
        }

        if (pawn.Physics == EPhysics.PHYS_Ledge)
        {
            // Caught by the arms on the way (a walk off an edge, or a ledge of the route): the route's own goal goes on,
            // so a ledge of the route is climbed; with no route the walk climbs up or lets go toward the mate. Left
            // alone here, the bot hung until the goal timed out; steered at the mate himself, it planned anew from the
            // hang, whose first point was the floor below, and let go of a ledge of its route each time it caught it.
            MoveTo(pawn, _follower is { HasGoal: true, Failed: false } onRoute ? onRoute.Goal : Floor(mate.Location, mate), now, ref plan);
            return true;
        }

        if (pawn.Physics == EPhysics.PHYS_OnPipe && _follower is { HasGoal: true } route)
        {
            // On a pipe of the route (up out of a pit below the mate, say): the route's keys climb it. With none the bot
            // held the pipe for the rest of the goal (the play-team smoke on SteeE, pipe GE218).
            MoveTo(pawn, route.Goal, now, ref plan);
            return true;
        }

        if (!Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal))
        {
            return true;
        }

        if (SMateInteraction.MateInReach(pawn, mate) && (_pressSince >= 0f || MenuReach(pawn, mate)))
        {
            // In front of him, facing him, both ways, well inside the reach and both still: press Use. The walking
            // states act on the press only (its rising edge), so a press that opened nothing is let go and made again.
            // A press at the edge of the reach could open nothing a tick later, and a merc's Use that opens nothing is
            // the berserk spin or the elbow, into the mate. No stick while pressing (Use with forward is the charge).
            LookAt(pawn, mate.Location, ref plan);
            plan.Keys = default;
            if (_pressSince >= 0f || entry?.CanUse(Pc) == true)
            {
                if (_pressSince < 0f)
                {
                    _pressSince = now;
                }

                float held = now - _pressSince;
                plan.Keys.bUsing = held < TeamAiK.UsePressSeconds ? (byte)1 : (byte)0;
                if (held > TeamAiK.UsePressSeconds + TeamAiK.UseReleaseSeconds)
                {
                    _pressSince = -1f;
                }
            }

            return true;
        }

        _pressSince = -1f;

        // The mate search wants the bot in front of the mate and the bot's pawn facing him. A standing spy does not
        // turn his body with the view, so the bot walks up to him along the line he faces: from a staging point
        // further out, straight at him.
        FVector facing = new FRotator(0, mate.Rotation.Yaw, 0).Vector();
        FVector staging = Staging(mate, facing);
        FVector fromMate = pawn.Location - mate.Location;
        float along = fromMate | facing;
        float across = NavFollower.Horizontal(fromMate - (facing * along));
        float d = NavFollower.Horizontal(fromMate);
        if (along < TeamAiK.MenuStandOff * 0.5f || across > TeamAiK.MenuLaneWidth)
        {
            // No route to the staging point (the graph can miss a narrow way a body still fits through): straight at
            // the mate while the line is open, which brings the staging point into an open line too.
            if (!MoveTo(pawn, Floor(staging, mate), now, ref plan) && WalkStraight(pawn, Floor(mate.Location, mate), ref plan))
            {
                _stuckSince = -1f;
                if (d < TeamAiK.MenuClosest)
                {
                    plan.Keys.BT_Up = 0;
                }
            }

            return true;
        }

        int yaw = NavFollower.YawTo(pawn.Location, mate.Location);
        plan.Yaw = yaw;
        plan.Pitch = 0;
        plan.Tolerance = 2500;
        plan.Crouched = false;
        // Too close with the body turned away: back off along the lane (the view stays on him), then walk at him
        // again so the body turns with the walk; stop short of bumping into him.
        bool bodyAway = NavFollower.YawError(pawn.Rotation.Yaw & 0xFFFF, yaw) > TeamAiK.BodyFacingTolerance;
        if (d < TeamAiK.MenuClosest + 10f && bodyAway)
        {
            _backingOff = true;
        }
        else if (d > TeamAiK.MenuBackOff)
        {
            _backingOff = false;
        }

        if (_backingOff && !GroundBehind(pawn, mate))
        {
            // No floor behind (the bot stood between the mate and the edge of SteeE's pit and backed off into it, fell
            // 640 uu and could not come back): out to the staging point on the graph instead, which keeps to floor,
            // and straight at him again from there.
            _backingOff = false;
            if (MoveTo(pawn, Floor(staging, mate), now, ref plan))
            {
                return true;
            }
        }

        if (_backingOff)
        {
            plan.Tolerance = 65536;
            plan.Keys.BT_Down = 1;
            return true;
        }

        plan.Keys.BT_Up = d > TeamAiK.MenuClosest ? (byte)1 : (byte)0;
        return true;
    }

    /// <summary>
    /// True when the ground goes on behind the bot, away from <paramref name="mate"/> (the way a back-off walks): a line
    /// down from the bot's feet level finds floor within a step at half and all of <see cref="TeamAiK.BackOffProbe"/>.
    /// A wall there is fine (the back-off stops against it).
    /// </summary>
    private bool GroundBehind(SBasePawn pawn, SBasePawn mate)
    {
        FVector away = new FVector(pawn.Location.X - mate.Location.X, pawn.Location.Y - mate.Location.Y, 0f).SafeNormal();
        FVector feet = NavFollower.Feet(pawn);
        foreach (float d in (ReadOnlySpan<float>)[TeamAiK.BackOffProbe * 0.5f, TeamAiK.BackOffProbe])
        {
            FVector p = feet + (away * d);
            if (_bot.World.LineOpen(p + new FVector(0f, 0f, 8f), p - new FVector(0f, 0f, OpenScct.Physics.K.MAXSTEPHEIGHT + 8f)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The staging point on the mate's facing line: the farthest of a few distances (190 down to 140) with an open
    /// line from the mate, so a mate facing a wall or a doorway still gets one.
    /// </summary>
    private FVector Staging(SBasePawn mate, FVector facing)
    {
        foreach (float d in TeamAiK.MenuStagingSteps)
        {
            FVector p = mate.Location + (facing * d);
            if (_bot.World.LineOpen(mate.Location, p))
            {
                return p;
            }
        }

        return mate.Location + (facing * TeamAiK.MenuStagingSteps[^1]);
    }

    /// <summary>Climb on a ready mate: walk to the climber's side, press the co-op key, then let the move run.</summary>
    private bool Climb(SBasePawn pawn, float now, ref AiPlan plan, bool hoist)
    {
        if (_target is not { } mate || mate.bDeleteMe || mate.Health <= 0)
        {
            return false;
        }

        string state = Pc.GetStateName().ToString();
        if (_pressedCoop)
        {
            if (state.StartsWith("s_ShortLadder", StringComparison.Ordinal) || state.StartsWith("s_Hoist", StringComparison.Ordinal) ||
                state.StartsWith("s_Coop", StringComparison.Ordinal) || state.StartsWith("s_ItTarget", StringComparison.Ordinal))
            {
                return true;
            }

            if (Pc.IsInState(NAME_OnLedge) || pawn.Physics == EPhysics.PHYS_Ledge)
            {
                // Boosted to the ledge: pull up.
                plan.Keys.BT_Up = 1;
                plan.Yaw = pawn.Rotation.Yaw & 0xFFFF;
                plan.Tolerance = 65536;
                return true;
            }

            // Walking again (on top, or back down when the move failed): done.
            return pawn.Physics != EPhysics.PHYS_Walking && now - _since < TeamAiK.GoalTimeout;
        }

        if (hoist ? !mate.bReadyForHoistClimb : !mate.bReadyForShortLadder)
        {
            return false;
        }

        if (Pc.CheckIfCanInteractWithMate(out Actor? found, true) == (hoist ? SPlayerController.CA_HoistClimb : SPlayerController.CA_ShortLadder) &&
            ReferenceEquals(found, mate))
        {
            plan.Keys.Press(AiCommand.PerformCoopMvt);
            _pressedCoop = true;
            return true;
        }

        mate.Rotation.GetAxes(out FVector x, out _, out FVector z);
        FVector spot;
        int faceYaw;
        if (hoist)
        {
            // Under the hanging mate, facing the wall as he does.
            spot = mate.Location - (x * 130.7f) - (z * 296.7f);
            faceYaw = mate.Rotation.Yaw & 0xFFFF;
        }
        else
        {
            // In front of the helper (who faces away from the wall), facing him.
            spot = mate.Location + (new FVector(x.X, x.Y, 0f).SafeNormal() * 150f);
            faceYaw = NavFollower.YawTo(spot, mate.Location);
        }

        if (NavFollower.Horizontal(spot - pawn.Location) > 40f)
        {
            MoveTo(pawn, Floor(spot, pawn), now, ref plan);
            return true;
        }

        plan.Yaw = faceYaw;
        plan.Pitch = 0;
        plan.Tolerance = 1200;
        if (NavFollower.YawError(pawn.Rotation.Yaw & 0xFFFF, faceYaw) > 1500)
        {
            // A step in the facing direction turns the pawn.
            plan.Keys.BT_Up = 1;
        }

        return true;
    }

    /// <summary>Offer a boost: take the spot under the high ledge, press the co-op key, wait for the climb.</summary>
    private bool Offer(SBasePawn pawn, float now, ref AiPlan plan)
    {
        if (_target is not { } mate || mate.bDeleteMe || mate.Health <= 0)
        {
            return false;
        }

        string state = Pc.GetStateName().ToString();
        if (state.StartsWith("s_ShortLadder_P", StringComparison.Ordinal))
        {
            // Ready (or walking to the spot): hold still; stand down when nobody comes.
            if (state == "s_ShortLadder_P_Waiting" && pawn.bReadyForShortLadder)
            {
                if (_readySince < 0f)
                {
                    _readySince = now;
                }
                else if (now - _readySince > TeamAiK.OfferGiveUpSeconds)
                {
                    plan.Keys.Press(AiCommand.PerformCoopMvt);
                }
            }

            _pressedCoop = true;
            return true;
        }

        if (_pressedCoop)
        {
            // The boost is over (or the offer was taken back).
            return false;
        }

        if (!BoostSpot(mate, out FVector loc, out FVector dir))
        {
            return false;
        }

        // A spot of the same ledge near the mate (the spot is the ledge point nearest the helper, so any place along
        // the wall beside the mate's own spot gives one): press the co-op key, the helper's walk settles on it.
        if (pawn.Physics == EPhysics.PHYS_Walking && pawn.CheckIfCanStartSHL(out FVector mine, out _) &&
            NavFollower.Horizontal(mine - mate.Location) < TeamAiK.OfferRadius && NavFollower.Horizontal(mine - pawn.Location) < 60f)
        {
            plan.Keys.Press(AiCommand.PerformCoopMvt);
            _pressedCoop = true;
            return true;
        }

        // Beside the mate's spot along the wall (not between him and the wall, where there is no room to pass him),
        // on the bot's side.
        FVector along = new FVector(-dir.Y, dir.X, 0f).SafeNormal();
        FVector a = loc + (along * TeamAiK.OfferBeside);
        FVector b = loc - (along * TeamAiK.OfferBeside);
        // Only a side with walkable floor (a node of the spy graph on the mate's floor, in the open from him): beside a
        // mate near the end of a ledge the far side was past the floor's edge and the bot walked off it.
        bool okA = SideFloor(mate, a);
        bool okB = SideFloor(mate, b);
        FVector side = okA != okB ? (okA ? a : b)
            : NavFollower.Horizontal(a - pawn.Location) < NavFollower.Horizontal(b - pawn.Location) ? a : b;
        MoveTo(pawn, Floor(side, mate), now, ref plan);
        return true;
    }

    /// <summary>True when <paramref name="spot"/> has floor at the mate's height (a spy graph node within reach) and an open line from him.</summary>
    private bool SideFloor(SBasePawn mate, FVector spot)
    {
        FVector floor = Floor(spot, mate);
        NavGraph g = _bot.World.NavFor(spy: true);
        int node = g.NearestNode(floor + new FVector(0f, 0f, 1f));
        return node >= 0 && NavFollower.Horizontal(g.Node(node).Floor - floor) < TeamAiK.SideFloorReach &&
            MathF.Abs(g.Node(node).Floor.Z - floor.Z) < TeamAiK.SideFloorStep &&
            _bot.World.LineOpen(mate.Location, new FVector(spot.X, spot.Y, mate.Location.Z));
    }

    // ---- skills -------------------------------------------------------------------------------------------------

    private bool MoveTo(SBasePawn pawn, FVector floor, float now, ref AiPlan plan)
    {
        bool going = Route(pawn, floor, now, ref plan);
        if (going)
        {
            _stuckSince = -1f;
        }
        else if (_stuckSince < 0f)
        {
            _stuckSince = now;
        }

        return going;
    }

    private bool Route(SBasePawn pawn, FVector floor, float now, ref AiPlan plan)
    {
        NavFollower f = _follower!;
        bool moved = !f.HasGoal || NavFollower.Horizontal(f.Goal - floor) > 48f || MathF.Abs(f.Goal.Z - floor.Z) > 60f;
        if (moved || f.Failed)
        {
            if (!moved && now - _failedPlanAt < 1.5f)
            {
                return WalkStraight(pawn, floor, ref plan);
            }

            if (!f.SetGoal(pawn, floor))
            {
                _failedPlanAt = now;
                return WalkStraight(pawn, floor, ref plan);
            }
        }

        NavCommand c = f.Steer(pawn, now, Pc.Rotation.Yaw & 0xFFFF, sneak: false);
        plan.Keys = c.Keys;
        plan.Yaw = c.Yaw ?? plan.Yaw;
        plan.Tolerance = c.Tolerance;
        plan.Crouched = c.Crouched ?? false;
        return !f.Failed;
    }

    /// <summary>
    /// No route on the graph (a goal next to a mate off the graph's nodes): walk straight at it while the line is
    /// open. False when it is not.
    /// </summary>
    private bool WalkStraight(SBasePawn pawn, FVector floor, ref AiPlan plan)
    {
        FVector at = new(floor.X, floor.Y, floor.Z + pawn.CollisionHeight);
        if (!_bot.World.LineOpen(pawn.Location, at))
        {
            return false;
        }

        plan.Yaw = NavFollower.YawTo(pawn.Location, at);
        plan.Tolerance = 3000;
        plan.Crouched = false;
        plan.Keys.BT_Up = NavFollower.Horizontal(at - pawn.Location) > 20f ? (byte)1 : (byte)0;
        return true;
    }

    private static void LookAt(SBasePawn pawn, FVector point, ref AiPlan plan)
    {
        FRotator r = (point - AiSight.Eye(pawn)).Rotation();
        plan.Yaw = r.Yaw & 0xFFFF;
        plan.Pitch = r.Pitch;
        plan.Tolerance = 65536;
        plan.Crouched = false;
    }

    private float AimError(SBasePawn pawn, FVector point)
    {
        FVector to = point - AiSight.Eye(pawn);
        float len = to.Size();
        if (len < 1f)
        {
            return 0f;
        }

        float cos = (to | Pc.Rotation.Vector()) / len;
        return SvmMath.Acos(SvmMath.Clamp(cos, -1f, 1f)) * (180f / MathF.PI);
    }

    /// <summary>The floor under <paramref name="point"/> at the height of <paramref name="standing"/>'s feet.</summary>
    private static FVector Floor(FVector point, Pawn standing) =>
        new(point.X, point.Y, standing.Location.Z - standing.CollisionHeight);
}
