using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Traversal;
using PhysicsK = OpenScct.Physics.K;

namespace OpenScct.Game.AI;

public enum AiOrder : byte
{
    Watch,
    Check,
    Boost,
}

public sealed partial class TeamAi
{
    private DoorAi? _orderDoors;
    private FVector? _boostLedge;
    private FVector _orderFloor;
    private FVector _orderPoint;
    private float _orderArrived;
    internal SBasePawn? OrderRequester { get; private set; }

    internal bool CanTakeOrder(SBasePawn requester) =>
        !_bot.Held && !_bot.Stopped && _bot.Route is null && _bot.Pawn is { Health: > 0, bDeleteMe: false } pawn &&
        ReferenceEquals(_bot.Controller.Pawn, pawn) && pawn.SPawnType == requester.SPawnType &&
        pawn.Physics == EPhysics.PHYS_Walking && !pawn.bSleeping && !pawn.bLyingOnGround && !pawn.bStickyShocking &&
        !Committed(pawn) && Goal != TeamGoal.Revive && !EnemyClose(pawn) &&
        _bot.Gadgets.Active == SpyGadgetAct.None && _bot.Brain?.Follower.ActiveLink < 0 &&
        _bot.Brain is not (MercAi { Goal: MercAi.GFrag or MercAi.GDefuse } or SpyAi { AtTerminal: true } or SpyAi { InMelee: true }) &&
        (OrderRequester is null || ReferenceEquals(OrderRequester, requester));

    internal bool TryOrder(SBasePawn requester, AiOrder order, FVector point, float now)
    {
        SBasePawn pawn = _bot.Pawn!;
        bool spy = pawn is SPawnAttaque;
        var route = new NavFollower(_bot.World, spy);
        FVector floor;
        FVector? boostLedge = null;
        if (order == AiOrder.Boost)
        {
            if (!spy) return false;
            if (BoostSpot(requester, out FVector ledge, out FVector direction) &&
                (ledge - point).SizeSquared() <= 360f * 360f)
            {
                FVector along = new FVector(-direction.Y, direction.X, 0f).SafeNormal() * TeamAiK.OfferBeside;
                FVector a = ledge + along;
                FVector b = ledge - along;
                bool okA = SideFloor(requester, a);
                bool okB = SideFloor(requester, b);
                if (!okA && !okB) return false;
                FVector side = okA && (!okB || (a - pawn.Location).SizeSquared() < (b - pawn.Location).SizeSquared()) ? a : b;
                floor = Floor(side, requester);
                if (!ExactRoute(route, pawn, floor)) return false;
            }
            else
            {
                if (!RemoteBoostPost(route, pawn, point, out floor, out FVector remoteLedge)) return false;
                boostLedge = remoteLedge;
            }
        }
        else if (!PingPost(route, pawn, point, order == AiOrder.Watch, out floor))
        {
            return false;
        }

        Goal = order switch
        {
            AiOrder.Watch => TeamGoal.Watch,
            AiOrder.Check => TeamGoal.Check,
            _ => TeamGoal.OfferBoost,
        };
        OrderRequester = requester;
        _target = requester;
        _since = now;
        _orderFloor = floor;
        _boostLedge = boostLedge;
        _orderPoint = point;
        _orderArrived = -1f;
        _follower = route;
        _orderDoors = new DoorAi(_bot, route, spy);
        _stuckSince = _readySince = -1f;
        _pressedCoop = _menuDownHeld = _backingOff = false;
        return true;
    }

    internal void CancelOrder(float now)
    {
        if (OrderRequester is not null) Finish(now, gaveUp: true);
    }

    private bool OrderStillValid(SBasePawn pawn, float now) =>
        OrderRequester is { Health: > 0, bDeleteMe: false } mate && mate.SPawnType == pawn.SPawnType &&
        now - _since < 22f && !EnemyClose(pawn) && now - _bot.HurtAt > 1f &&
        (Goal != TeamGoal.OfferBoost || _boostLedge is not null || (mate.Location - _orderPoint).SizeSquared() < 650f * 650f);

    private bool RemoteBoostPost(NavFollower route, SBasePawn pawn, FVector point, out FVector floor, out FVector ledge)
    {
        const float boostHeight = PhysicsK.GE.ShlSpotDown;
        floor = ledge = default;
        if (MovementNatives.Driver(pawn) is not { } driver) return false;
        var hits = new List<GEHit>();
        byte mask = _bot.World.Level.Info.GEActivationMask;
        if (driver.GEs.QuerySphere(point, 180f, (int)GEKind.LG, mask, hits) == 0)
            driver.GEs.QuerySphere(point + new FVector(0f, 0f, boostHeight), 180f, (int)GEKind.LG, mask, hits);
        hits.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index));
        for (int i = 0; i < Math.Min(4, hits.Count); i++)
        {
            GEHit hit = hits[i];
            FVector axis = hit.GE.P1 - hit.GE.P0;
            if (MathF.Abs(axis.Z) >= 15f) continue;
            FVector normal = new FVector(-axis.Y, axis.X, 0f).SafeNormal();
            FVector under = hit.ClosestPoint - new FVector(0f, 0f, boostHeight);
            for (int side = -1; side <= 1; side += 2)
            {
                int node = route.Graph.NearestNode(under + normal * (side * 76f) + new FVector(0f, 0f, 10f), below: 24f);
                if (node < 0 || !route.Graph.Node(node).Standing) continue;
                floor = route.Graph.Node(node).Floor;
                if (MathF.Abs(under.Z - floor.Z) > 8f || NavFollower.Horizontal(floor - under) > 130f ||
                    !ExactRoute(route, pawn, floor) || route.Remaining(pawn.Location) > 4500f) continue;
                ledge = hit.ClosestPoint;
                return true;
            }
        }
        return false;
    }

    private bool StageRemoteBoost(SBasePawn pawn, FVector ledge, float now, ref AiPlan plan)
    {
        if (pawn.Physics == EPhysics.PHYS_Walking && pawn.CheckIfCanStartSHL(out FVector spot, out _) &&
            NavFollower.Horizontal(spot - pawn.Location) < 65f &&
            (spot + new FVector(0f, 0f, PhysicsK.GE.ShlSpotDown - pawn.CollisionHeight) - ledge).SizeSquared() < 170f * 170f)
        {
            plan.Keys.Press(AiCommand.PerformCoopMvt);
            _pressedCoop = true;
            return true;
        }

        if (!MoveTo(pawn, _orderFloor, now, ref plan) || _follower is not { Arrived: true }) return true;
        if (_orderArrived < 0f) _orderArrived = now;
        return now - _orderArrived < 1f;
    }

    private bool PingPost(NavFollower route, SBasePawn pawn, FVector point, bool watch, out FVector floor)
    {
        floor = default;
        var graph = route.Graph;
        float radius = watch ? 480f : 220f;
        float standingEye = graph.Profile.StandingHeight + pawn.DefaultObject.BaseEyeHeight;
        var nodes = new List<int>();
        var lower = new List<int>();
        // A ping is a point in the world, often at eye height on a wall. The navigation query only
        // returns floors within 120 uu of its centre, so also sample the floor below eye-level pings.
        graph.NodesNear(point, radius, nodes);
        graph.NodesNear(point - new FVector(0, 0, standingEye), radius, lower);
        var found = new HashSet<int>(nodes);
        foreach (int node in lower)
            if (found.Add(node)) nodes.Add(node);

        var posts = new List<(int Node, float Cost)>();
        bool spy = pawn is SPawnAttaque;
        int from = graph.NearestNode(pawn.Location, pawn.CollisionHeight);
        if (from < 0) return false;
        float[] costs = graph.CostsFrom(from, maxCost: 4500f, _bot.World.BadLinks(spy), _bot.World.BadEdges(spy));
        foreach (int node in nodes)
        {
            var candidate = graph.Node(node);
            if (!float.IsFinite(costs[node]) || !candidate.Standing && !spy) continue;
            float height = candidate.Standing ? graph.Profile.StandingHeight : graph.Profile.CrouchHeight;
            FVector eye = candidate.Floor + new FVector(0, 0, height + pawn.DefaultObject.BaseEyeHeight);
            float distance = NavFollower.Horizontal(point - eye);
            if (watch && distance < 90f) continue;
            FVector nearFace = point + (eye - point).SafeNormal() * 12f;
            if (!_bot.World.LineOpen(eye, nearFace)) continue;
            float cost = (eye - point).Size() + costs[node] * .2f;
            if (watch) cost += MathF.Abs(distance - 250f);
            posts.Add((node, cost));
        }

        posts.Sort((a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : a.Node.CompareTo(b.Node));
        for (int i = 0; i < Math.Min(12, posts.Count); i++)
        {
            floor = graph.Node(posts[i].Node).Floor;
            if (ExactRoute(route, pawn, floor) && route.Remaining(pawn.Location) < 4500f) return true;
        }
        return false;
    }

    private static bool ExactRoute(NavFollower route, SBasePawn pawn, FVector floor) =>
        route.SetGoal(pawn, floor) && route.Path.Count > 0 && (route.Path[^1].Point - floor).SizeSquared() < 72f * 72f;

    private bool ActOnPing(SBasePawn pawn, float now, ref AiPlan plan)
    {
        if (!MoveTo(pawn, _orderFloor, now, ref plan) || _follower is not { Arrived: true }) return true;
        if (_orderArrived < 0f) _orderArrived = now;
        bool watch = Goal == TeamGoal.Watch;
        if (now - _orderArrived > (watch ? 12f : 3f)) return false;

        FRotator aim = (_orderPoint - AiSight.Eye(pawn)).Rotation();
        plan.Yaw = aim.Yaw + (watch ? 0 : (int)(4200f * SvmMath.Sin((now - _orderArrived) * 3f)));
        plan.Pitch = aim.Pitch;
        plan.Tolerance = 65536;
        plan.Crouched = pawn is SPawnAttaque;
        return true;
    }
}
