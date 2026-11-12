using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

public sealed partial class MercAi
{
    /// <summary>Heights above the floor at the wall a mine is tried at (uu): shin to above the head.</summary>
    private static readonly float[] TrapHeights = [54f, 66f, 88f, 104f, 130f, 152f];

    /// <summary>The bot stands this far from the wall (uu): just clear of its 35 uu cylinder.</summary>
    private const float TrapStandOff = 38f;

    /// <summary>The placement is planned within this reach of the eye (uu), inside the launcher's 55.</summary>
    private const float TrapPlanReach = 51.5f;

    /// <summary>At the wall the bot places within this reach (uu), and steps in when the eye is farther.</summary>
    private const float TrapUseReach = 53.5f;

    /// <summary>An objective counts a trap within this distance (uu): its own walls and the lanes into it.</summary>
    private const float TrapCoverRange = 600f;

    private float _trapNext;
    private float _trapAtWallSince = -1f;
    private FVector _trapBadWall;
    private float _trapBadUntil = -100f;

    /// <summary>Mines and trackers this bot started to place (the AltFire accepted at a wall).</summary>
    public int TrapsPlaced { get; private set; }

    private float _trapUntil;
    private bool _trapApproach;
    private bool _trapCrouched;
    private FVector _trapWall;
    private FVector _trapFloor;
    private SGadMultiMinesLauncher? _trapLauncher;
    private int _trapSlot;
    private float _trapVerifyUntil;
    private int _trapBeforeCharge;
    private readonly record struct TrapSite(FVector Floor, float Value);
    private readonly record struct TrapCandidate(FVector Wall, FVector Normal, float Score);

    /// <summary>A wall spot ranked for a mine: where to stand, the stance, and its score with the graph cost of the way there.</summary>
    private readonly record struct TrapOption(FVector Wall, FVector Floor, bool Crouched, float Score);

    /// <summary>Where a search for a mine wall stands; each stage takes a tick or more (<see cref="DefendWithTrap"/>).</summary>
    private enum TrapStage
    {
        Idle,
        Probing,
        Ranking,
        Checking,
    }

    private TrapStage _trapStage;

    /// <summary>What the search probes round, nearest first (objectives, then heal stations); <see cref="_trapSource"/> is the next.</summary>
    private readonly List<Actor> _trapSources = new();
    private int _trapSource;
    private float _trapWanted;
    private readonly List<TrapCandidate> _trapCandidates = new();

    /// <summary>The ranked spots of the search under way, best first; <see cref="_trapOption"/> is the next to check.</summary>
    private readonly List<TrapOption> _trapOptions = new();
    private int _trapOption;
    private float _trapSearchUntil;
    private NavFollower? _trapRoute;
    private NavGraph.PathSearch? _trapSearch;
    private bool _trapChecking;
    private readonly List<(FVector At, float RangeSquared)> _trapKeepOuts = new();

    /// <summary>Routes checked for one search at most, one a tick: the best spots by the sweep.</summary>
    private const int TrapRouteChecks = 8;

    /// <summary>A search is dropped after this long (s): the merc was called away, or the budget kept it waiting.</summary>
    private const float TrapSearchSeconds = 1.5f;

    // Guarding mercs inspect nearby walls, approach a clear placement, then use the same
    // selected gadget and animation-driven placement as a human. Combat always interrupts.
    private bool DefendWithTrap(float now, ref AiPlan plan)
    {
        if (Pawn.bSleeping || Pawn.bLyingOnGround || Pawn.bStickyShocking || Pawn.bGasMask ||
            Pawn.Physics != EPhysics.PHYS_Walking ||
            !Pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal)) return false;
        if (_trapApproach && (now > _trapUntil || _trapLauncher is null || _trapLauncher.bDeleteMe || _trapLauncher.Charge <= 0))
        {
            // Out of time at this wall: another one next time.
            if (now > _trapUntil) GiveUpWall(now);
            _trapApproach = false;
        }

        if (_trapVerifyUntil > 0f)
        {
            bool placed = _trapLauncher is { } launcher && launcher.Charge < _trapBeforeCharge;
            if (!placed && now < _trapVerifyUntil) return false;
            _trapVerifyUntil = 0f;
            _trapNext = now + (placed ? (SettingUp(now) ? 1.5f : 7f) : 2f) * Bot.Personality.Tempo;
        }

        if (!_trapApproach)
        {
            // The search is spread over ticks, where it ran whole in one (up to 70 ms on a map with many walls):
            // a tick probes the walls round one objective or heal station, a tick ranks the spots found, then
            // each tick checks the route to one of them, best first, and the first that holds is taken. Every
            // step waits while the tick's budget is used up.
            if (_trapStage == TrapStage.Idle)
            {
                if (now < _trapNext || Difficulty.GadgetUse <= 0f || World.BudgetSpent) return false;
                _trapNext = now + (SettingUp(now) ? 2f : 4f) * Bot.Personality.Tempo;
                if (ChooseTrap()) BeginTrapSearch(now);
                return false;
            }

            if (now > _trapSearchUntil || _trapLauncher is not { bDeleteMe: false, Charge: > 0 })
            {
                _trapStage = TrapStage.Idle;
                return false;
            }

            if (World.BudgetSpent) return false;
            if (_trapStage == TrapStage.Probing)
            {
                ProbeTrapSource(now);
                if (_trapSource >= _trapSources.Count) _trapStage = TrapStage.Ranking;
                return false;
            }

            if (_trapStage == TrapStage.Ranking)
            {
                // Ranked when the sweep it reads has ended.
                if (RankTrapWalls())
                {
                    _trapStage = _trapOptions.Count > 0 ? TrapStage.Checking : TrapStage.Idle;
                }

                return false;
            }

            // The route to the next option, searched over as many ticks as the budget makes it take.
            TrapOption option = _trapOptions[_trapOption];
            _trapRoute ??= new NavFollower(World, spy: false);
            if (!_trapChecking)
            {
                _trapSearch ??= new NavGraph.PathSearch();
                _trapChecking = _trapRoute.BeginGoal(Pawn, option.Floor, _trapSearch);
            }

            bool searched = !_trapChecking;
            while (!searched && !World.BudgetSpent)
            {
                searched = _trapRoute.ContinuePlan(Pawn, AiK.PlanChunkNodes);
            }

            if (!searched) return false;
            _trapChecking = false;
            if (++_trapOption >= _trapOptions.Count) _trapStage = TrapStage.Idle;
            if (!TrapRoute(option.Floor, out float routeLength) || routeLength > TrapRouteLength || OccupiedWall(option.Wall))
                return false;
            _trapStage = TrapStage.Idle;
            _trapWall = option.Wall;
            _trapFloor = option.Floor;
            _trapCrouched = option.Crouched;
            _trapApproach = true;
            _trapAtWallSince = -1f;
            _trapUntil = now + MathF.Max(8f, routeLength / 150f + 6f);
            Follower.Clear();
        }
        if (_trapAtWallSince < 0f && (NavFollower.Horizontal(Feet - _trapFloor) > 8f || MathF.Abs(Feet.Z - _trapFloor.Z) > 30f))
        {
            if (!MoveTo(_trapFloor, now, ref plan, sneak: false))
            {
                _trapApproach = false;
                return false;
            }

            if (Doors.Busy)
            {
                _trapUntil = now + 6f;
                return true;
            }

            // The navigation arrival radius is wider than the remaining few steps to the mine's 55 uu reach.
            if (!Follower.Arrived) return true;
            plan.MoveYaw = (_trapFloor - Feet).Rotation().Yaw;
            plan.Keys.BT_Up = 1;
            return true;
        }

        if (OccupiedWall(_trapWall))
        {
            _trapApproach = false;
            return false;
        }

        // At the wall: from here the bot only turns, changes stance and steps in, as a player lines a mine up.
        if (_trapAtWallSince < 0f) _trapAtWallSince = now;
        FVector reach = _trapWall - TrapEye;
        FRotator aim = reach.Rotation();
        plan.Yaw = aim.Yaw & 0xFFFF;
        plan.Pitch = aim.Pitch;
        plan.Tolerance = 65536;
        plan.Crouched = _trapCrouched;

        // The stance clip runs in its own walking state, and the eye eases to the new height over several frames.
        // Reach and pitch are judged only once both have settled, so a low mine is not lined up from the standing eye.
        if (Pawn.bIsCrouched != _trapCrouched || MathF.Abs(Pawn.EyeHeight - Pawn.BaseEyeHeight) > 2f) return true;

        if (reach.SafeNormal().Z > Equipment.K.MineMaxViewUp)
        {
            // Too steep from here (a step, a slope): stepping in would only steepen it.
            GiveUpWall(now);
            return true;
        }

        if (reach.Size() > TrapUseReach)
        {
            // A little short of the wall: step in until the eye is within reach (the cylinder stops at the wall).
            plan.MoveYaw = new FVector(reach.X, reach.Y, 0f).Rotation().Yaw & 0xFFFF;
            plan.Keys.BT_Up = 1;
            if (now - _trapAtWallSince > 2.5f) GiveUpWall(now);
            return true;
        }

        if (NavFollower.YawError(Pc.Rotation.Yaw & 0xffff, aim.Yaw & 0xffff) > 200 ||
            NavFollower.YawError(Pc.Rotation.Pitch & 0xffff, aim.Pitch & 0xffff) > 200 ||
            NavFollower.Horizontal(Pawn.Velocity) > 5f) return true;
        if (!ReferenceEquals(Pawn.SelectedGadget, _trapLauncher))
        {
            plan.Keys.Press(AiCommand.SelectGadget, _trapSlot);
            return true;
        }
        _trapLauncher!.UseMine();
        // A tracker beam needs an opposite wall; the presence detector does not.
        if (_trapLauncher.CanUse[_trapLauncher.iCurrentMineType] == 0)
            for (int i = 0; i < _trapLauncher.NbIT - 2; i++)
                if (_trapLauncher.CanUse[i] != 0) { _trapLauncher.iCurrentMineType = i; break; }
        if (_trapLauncher.HitActor is not null && (_trapLauncher.HitLocation - _trapWall).SizeSquared() < 8f * 8f &&
            _trapLauncher.CanUse[_trapLauncher.iCurrentMineType] != 0)
        {
            plan.Keys.Press(AiCommand.AltFire);
            TrapsPlaced++;
            _trapBeforeCharge = _trapLauncher.Charge;
            _trapVerifyUntil = now + 4f;
            _trapApproach = false;
        }
        else
        {
            // The launcher refuses this spot (a mine or a terminal too near, no stickable surface).
            GiveUpWall(now);
        }

        return true;
    }

    /// <summary>The wall the bot could not use is left alone for a minute, so the next look picks another.</summary>
    private void GiveUpWall(float now)
    {
        _trapBadWall = _trapWall;
        _trapBadUntil = now + 60f;
        _trapApproach = false;
        _trapNext = now + 3f * Bot.Personality.Tempo;
    }

    /// <summary>A wall point within the planned reach of an eye <paramref name="eyeHeight"/> above <paramref name="floor"/>, not pitched up past the launcher's limit.</summary>
    private static bool TrapInReach(FVector wall, FVector floor, float eyeHeight)
    {
        FVector reach = wall - (floor + new FVector(0f, 0f, eyeHeight));
        return reach.Size() <= TrapPlanReach && reach.SafeNormal().Z <= Equipment.K.MineMaxViewUp - 0.03f;
    }

    /// <summary>Mines and trackers within <see cref="TrapCoverRange"/> of <paramref name="at"/>, as a merc sees them on the map.</summary>
    private int TrapsNear(FVector at)
    {
        int count = 0;
        foreach (SMines mine in World.Level.AllActors<SMines>())
            if (!mine.bDeleteMe && (mine.Location - at).SizeSquared() < TrapCoverRange * TrapCoverRange) count++;
        return count;
    }

    /// <summary>
    /// Fewer traps near the objective than wanted: two each while the bot's sector is one or two objectives, one each
    /// over a wider sector, so the charges go round.
    /// </summary>
    private bool NeedsTrap(SObjectiveTrigger objective) => TrapsNear(objective.Location) < (_sector.Count <= 2 ? 2 : 1);

    /// <summary>A launcher with a charge and room under its owner's limit, without choosing it.</summary>
    private bool CanTrap()
    {
        if (Difficulty.GadgetUse <= 0f) return false;
        for (SGadget? gadget = Pawn.MyGadget; gadget is not null; gadget = gadget.MyGadget)
        {
            if (PawnInventory.IsSelectable(gadget) && gadget is SGadMultiMinesLauncher { Charge: > 0, bDeleteMe: false } launcher &&
                (launcher is SGadMultiPresDetectorLauncher ? Pc.NbTrapMineInMapIOwn : Pc.NbBasicMineInMapIOwn) < launcher.iNbMineMax)
                return true;
        }

        return false;
    }

    private FVector TrapEye => Pawn.Location + Pawn.WalkBob + new FVector(0, 0, Pawn.EyeHeight);

    private IEnumerable<TrapSite> TrapLanes(FVector objective)
    {
        // Find narrow carriageways entering the objective area. Require floor, a sightline to the
        // area being defended and a wall within mine reach of the lane, then favor separate approaches.
        var sites = new List<TrapSite>();
        NavGraph graph = Follower.Graph;
        List<int> nodes = NodesAround(objective, 490f);
        var height = new FVector(0, 0, graph.Profile.StandingHeight);
        int stride = Math.Max(1, nodes.Count / 64);
        World.ChargeWork(Math.Min(nodes.Count, 64) * 3 * AiK.TraceWork);
        for (int i = 0; i < nodes.Count; i += stride)
        {
            FVector floor = graph.Node(nodes[i]).Floor;
            FVector delta = floor - objective;
            if (MathF.Abs(delta.Z) > 80f || delta.SizeSquared() < 190f * 190f ||
                !World.LineOpen(floor + height, objective + height)) continue;
            FVector side = new FVector(-delta.Y, delta.X, 0).SafeNormal();
            FVector eye = floor + height;
            if (!World.Collision.LineCheck(out var left, Pawn, eye + side * 185f, eye, default, ETraceFlags.TRACE_World) ||
                !World.Collision.LineCheck(out var right, Pawn, eye - side * 185f, eye, default, ETraceFlags.TRACE_World)) continue;
            if (left.bStartPenetrating || right.bStartPenetrating || left.Actor is Mover || right.Actor is Mover) continue;
            float width = (left.Location - right.Location).Size();
            if (width < Follower.Graph.Profile.Radius * 2f) continue;
            sites.Add(new(floor, 360f - width * .45f));
        }

        TrapSite? first = null;
        foreach (TrapSite site in sites.OrderByDescending(x => x.Value))
        {
            if (first is { } best && (site.Floor - best.Floor).SizeSquared() < 240f * 240f) continue;
            yield return site;
            if (first is not null) yield break;
            first = site;
        }
    }

    /// <summary>
    /// The graph cost a mine spot's approach may have at most to be ranked. A spot is taken only within
    /// <see cref="TrapRouteLength"/> of route, and a route costs at least its length; the margin covers crouched
    /// stretches and links, which cost more than their length.
    /// </summary>
    private const float TrapSweepCost = 4000f;

    /// <summary>The longest route to a mine spot.</summary>
    private const float TrapRouteLength = 1450f;

    /// <summary>
    /// Starts a search for a mine wall: lists what to probe round (the three nearest objectives that want a trap,
    /// then the heal stations near), nearest first. Nothing is traced here.
    /// </summary>
    private void BeginTrapSearch(float now)
    {
        _trapSources.Clear();
        _trapCandidates.Clear();
        _trapOptions.Clear();
        _trapSource = 0;
        _trapOption = 0;
        _trapChecking = false;
        var objectives = new List<SObjectiveTrigger>();
        foreach (SObjectiveTrigger objective in World.Level.AllActors<SObjectiveTrigger>())
            if (Defensible(objective) && (objective.Location - Pawn.Location).SizeSquared() < 1100f * 1100f &&
                NeedsTrap(objective))
                objectives.Add(objective);
        foreach (SObjectiveTrigger objective in objectives.OrderBy(o => (o.Location - Pawn.Location).SizeSquared()).Take(3))
            _trapSources.Add(objective);
        foreach (SInfirmary station in World.Level.AllActors<SInfirmary>())
        {
            if (station.bDeleteMe || station.bHidden || (station.Location - Pawn.Location).SizeSquared() >= 700f * 700f) continue;
            _trapSources.Add(station);
        }

        if (_trapSources.Count == 0) return;
        // A height band is preferred per placement: low most often (where players hide them), chest or head
        // height otherwise; the nearest reachable height is taken where the band cannot be had.
        float roll = Bot.Random.FRand();
        _trapWanted = roll < 0.5f ? 56f : roll < 0.75f ? 95f : 150f;
        GatherKeepOuts();
        _trapSearchUntil = now + TrapSearchSeconds;
        _trapStage = TrapStage.Probing;
    }

    /// <summary>Probes the walls round the next source of the search: an objective's use spot and the lanes into it, or a heal station.</summary>
    private void ProbeTrapSource(float now)
    {
        Actor source = _trapSources[_trapSource++];
        if (source.bDeleteMe) return;
        if (source is SObjectiveTrigger objective)
        {
            if (!GuardSpot(objective, out FVector spot)) return;
            ProbeTrapSite(new(spot, 210f), now);
            foreach (TrapSite lane in TrapLanes(spot)) ProbeTrapSite(lane, now);
        }
        else if (source is SInfirmary station)
        {
            FVector location = station.InteractionObj?.Location ?? station.Location;
            int node = Follower.Graph.NearestNode(location, 0f, 200f);
            if (node >= 0) ProbeTrapSite(new(Follower.Graph.Node(node).Floor, 30f), now);
        }
    }

    /// <summary>Wall probes round one site: 16 directions at six heights, each wall hit a candidate with its score.</summary>
    private void ProbeTrapSite(TrapSite site, float now)
    {
        World.ChargeWork(TrapHeights.Length * 16 * AiK.TraceWork);
        foreach (float height in TrapHeights)
        for (int i = 0; i < 16; i++)
        {
            FVector from = site.Floor + new FVector(0, 0, height);
            FVector direction = new FRotator(0, i * 4096, 0).Vector();
            if (!World.Collision.LineCheck(out var hit, Pawn, from + direction * 400f, from, default,
                SGadMultiMinesLauncher.WallTraceFlags) || hit.bStartPenetrating || MathF.Abs(hit.Normal.Z) > .2f ||
                hit.Actor is Mover || KeptOut(hit.Location) ||
                (now < _trapBadUntil && (hit.Location - _trapBadWall).SizeSquared() < 120f * 120f)) continue;

            float distance = (hit.Location - from).Size();
            if (distance < 70f) continue;
            float score = (hit.Location - Feet).Size() * .35f + MathF.Abs(distance - 120f) * 1.1f -
                site.Value + MathF.Abs(height - _trapWanted) * 2f;
            _trapCandidates.Add(new(hit.Location, hit.Normal, score));
        }
    }

    /// <summary>
    /// Fills <see cref="_trapOptions"/> with the best of the wall spots probed, best first: the sweep from the merc
    /// gives every spot's distance by the graph. No path is searched here; <see cref="TrapRoute"/> checks the route
    /// to one option a tick. False while the sweep is under way.
    /// </summary>
    private bool RankTrapWalls()
    {
        if (_trapCandidates.Count == 0) return true;
        if (WholeGraphCosts() is not { } costs) return false;
        float crouchEye = Pawn.CrouchHeight + ((SPAWNDEFENSE)Pawn).BaseCrouchEyeHeight;
        float standEye = Follower.Graph.Profile.StandingHeight + Pawn.DefaultObject.BaseEyeHeight;

        // The sweep from the merc gives the graph cost to every node: a spot's route is judged from it, where a
        // path search for each spot crossed the graph up to 36 times in one tick.
        var approaches = new Dictionary<(int, int), (bool Ok, FVector Floor, float Cost)>();
        foreach (TrapCandidate candidate in _trapCandidates.OrderBy(c => c.Score)
            .DistinctBy(c => ((int)(c.Wall.X / 16f), (int)(c.Wall.Y / 16f), (int)(c.Wall.Z / 16f))).Take(72))
        {
            // The heights of one wall spot share its approach.
            var spot = ((int)(candidate.Wall.X / 16f), (int)(candidate.Wall.Y / 16f));
            if (!approaches.TryGetValue(spot, out var approach))
            {
                if (approaches.Count >= 36) continue;
                bool ok = TrapStand(candidate.Wall, candidate.Normal, costs, out FVector approachFloor, out float cost);
                approach = (ok, approachFloor, cost);
                approaches[spot] = approach;
            }

            if (!approach.Ok) continue;
            // Slopes and raised platforms change the height at the actual placement floor: the stance is the
            // one whose eye reaches the spot from there, crouched first.
            bool crouched;
            if (TrapInReach(candidate.Wall, approach.Floor, crouchEye)) crouched = true;
            else if (TrapInReach(candidate.Wall, approach.Floor, standEye)) crouched = false;
            else continue;
            _trapOptions.Add(new(candidate.Wall, approach.Floor, crouched, candidate.Score + approach.Cost * .75f));
        }

        // OrderBy is stable: equal scores keep the order the probes found them in.
        var ranked = _trapOptions.OrderBy(o => o.Score).Take(TrapRouteChecks).ToList();
        _trapOptions.Clear();
        _trapOptions.AddRange(ranked);
        return true;
    }

    /// <summary>
    /// Where the bot stands to reach <paramref name="wall"/>, and the graph cost from the merc to the nearest node
    /// the route's goal is chosen from (<paramref name="costs"/>: the sweep from the merc). False without floor
    /// there or with no such node within <see cref="TrapSweepCost"/>.
    /// </summary>
    private bool TrapStand(FVector wall, FVector normal, float[] costs, out FVector floor, out float cost)
    {
        floor = default;
        cost = float.PositiveInfinity;
        // Leave room inside the 55 uu reach for a pitched view and the final walking tolerance.
        FVector approach = wall + new FVector(normal.X, normal.Y, 0f).SafeNormal() * TrapStandOff;
        FVector top = approach + new FVector(0f, 0f, 40f);
        FVector bottom = approach - new FVector(0f, 0f, 240f);
        if (!World.Collision.LineCheck(out FCheckResult ground, Pawn, bottom, top, new FVector(4f, 4f, 2f),
            SGadMultiMinesLauncher.WallTraceFlags) || ground.bStartPenetrating || ground.Normal.Z < .7f)
            return false;

        floor = ground.Location - new FVector(0f, 0f, 2f);
        // The same nodes the route's goal is chosen from (NavFollower.GoalNode).
        foreach (int node in NodesAround(floor, Follower.Graph.Profile.Spacing * 5f))
        {
            if (costs[node] < cost) cost = costs[node];
        }

        return cost <= TrapSweepCost;
    }

    /// <summary>The route just searched from the merc to <paramref name="floor"/> ends there and passes no closed door; its length.</summary>
    private bool TrapRoute(FVector floor, out float length)
    {
        length = 0f;
        if (_trapRoute is null || _trapRoute.Failed || _trapRoute.Planning || _trapRoute.Path.Count == 0 ||
            (_trapRoute.Path[^1].Point - floor).SizeSquared() > 4f)
            return false;

        FVector from = Pawn.Location;
        foreach (NavStep step in _trapRoute.Path)
        {
            FVector to = step.Point + new FVector(0f, 0f, Pawn.CollisionHeight);
            // The static graph assumes doors are open. Do not choose a placement beyond a closed door.
            if (World.Collision.LineCheck(out _, Pawn, to, from, default, ETraceFlags.TRACE_Movers)) return false;
            from = to;
        }

        length = _trapRoute.Remaining(Pawn.Location);
        return true;
    }

    private bool ChooseTrap()
    {
        _trapLauncher = null;
        int slot = 0;
        for (SGadget? gadget = Pawn.MyGadget; gadget is not null && slot < 8; gadget = gadget.MyGadget)
        {
            if (!PawnInventory.IsSelectable(gadget)) continue;
            if (gadget is SGadMultiMinesLauncher { Charge: > 0, bDeleteMe: false } launcher)
            {
                bool tracker = launcher is SGadMultiPresDetectorLauncher;
                int placed = tracker ? Pc.NbTrapMineInMapIOwn : Pc.NbBasicMineInMapIOwn;
                if (placed < launcher.iNbMineMax && (_trapLauncher is null || !tracker && _trapLauncher is SGadMultiPresDetectorLauncher))
                {
                    _trapLauncher = launcher;
                    _trapSlot = slot;
                }
            }
            slot++;
        }
        return _trapLauncher is not null;
    }

    private bool Defensible(SObjectiveTrigger objective) => MercDefence.Defensible(World, objective);

    private bool OccupiedWall(FVector wall)
    {
        GatherKeepOuts();
        return KeptOut(wall);
    }

    /// <summary>
    /// The places a new trap keeps clear of, read from the level once for a whole round of wall probes: 220 uu
    /// round each mine and tracker, 80 uu round each objective and heal station.
    /// </summary>
    private void GatherKeepOuts()
    {
        _trapKeepOuts.Clear();
        foreach (SMines mine in World.Level.AllActors<SMines>())
            if (!mine.bDeleteMe) _trapKeepOuts.Add((mine.Location, 220f * 220f));
        foreach (SObjectiveTrigger terminal in World.Level.AllActors<SObjectiveTrigger>())
            _trapKeepOuts.Add((terminal.Location, 80f * 80f));
        foreach (SInfirmary station in World.Level.AllActors<SInfirmary>())
            _trapKeepOuts.Add((station.Location, 80f * 80f));
    }

    private bool KeptOut(FVector wall)
    {
        foreach ((FVector at, float rangeSquared) in _trapKeepOuts)
            if ((at - wall).SizeSquared() < rangeSquared) return true;
        return false;
    }
}
