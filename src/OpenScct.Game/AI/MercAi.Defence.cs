using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

// Defending as a team of players does: the objectives that are left are shared out (MercDefence), each merc mines
// the ones of its sector first, then patrols between them, stopping at a post beside each and watching a long sight
// line through the rifle's scope now and then. The scope is also raised on a spy seen far off.
public sealed partial class MercAi
{
    private static readonly Name NameScope = new("s_Binoculars_Def");

    private readonly List<SObjectiveTrigger> _sector = new();
    private readonly List<SObjectiveTrigger> _candidates = new();
    private readonly Dictionary<SObjectiveTrigger, float> _visited = new(ReferenceEqualityComparer.Instance);
    private float _sectorAt = -100f;
    private SObjectiveTrigger? _lastPost;
    private bool _postReached;
    private int _watchYaw = -1;
    private float _scopeFrom;
    private float _scopeTo;
    private float _scopeWantAt = -1f;
    private float _scopeShotAt = -10f;

    /// <summary>Shots fired through the scope.</summary>
    public int ScopedShots { get; private set; }

    /// <summary>Guard posts reached (a stop of the patrol).</summary>
    public int PostsReached { get; private set; }

    /// <summary>The objectives this merc defends (diagnostics).</summary>
    public IReadOnlyList<SObjectiveTrigger> Sector => _sector;

    /// <summary>True when the brain asked for the scope at <paramref name="now"/> (this tick's plan); the controller presses the key.</summary>
    public bool WantsScope(float now) => _scopeWantAt == now;

    /// <summary>The scope is up and zoomed (the raise clip has ended).</summary>
    private bool Scoped => Pc.bBinocularVision && Pc.GetStateName() == NameScope && Pc.FovAngle <= Equipment.K.ScopeFov1 + 1f;

    private bool CanSnipe => Difficulty.GadgetUse >= 0.5f && Pawn.MyWeapon is SWeaponDefense { bCanSnipe: true };

    /// <summary>The range a spy is shot at: with a scope to raise, as far as it reaches.</summary>
    private float EngageRange => CanSnipe ? AiK.MercScopeRange : AiK.MercEngageRange;

    private void RefreshSector(float now)
    {
        if (now - _sectorAt < 1f && now >= _sectorAt)
        {
            return;
        }

        _sectorAt = now;
        _sector.Clear();
        _sector.AddRange(World.Defence.Sector(Pawn, now));
    }

    private float _setupAt = -100f;
    private bool _setup;

    /// <summary>
    /// The merc still owes its sector mines: it carries one it may place and an objective of the sector lacks its
    /// traps (one it stopped at within the last 45 s without managing counts as tried). While this holds the merc
    /// walks straight from objective to objective, places without the usual pauses and is not drawn off by noises
    /// or callouts, so the traps are down before the spies arrive.
    /// </summary>
    private bool SettingUp(float now)
    {
        if (now - _setupAt < 1f && now >= _setupAt)
        {
            return _setup;
        }

        _setupAt = now;
        _setup = false;
        if (!CanTrap())
        {
            return false;
        }

        RefreshSector(now);
        foreach (SObjectiveTrigger t in _sector)
        {
            if (Defensible(t) && NeedsTrap(t) &&
                (ReferenceEquals(t, _post) || !_visited.TryGetValue(t, out float at) || now - at > 45f))
            {
                _setup = true;
                break;
            }
        }

        return _setup;
    }

    /// <summary>
    /// A spy this merc does not see, away from the objectives it defends, with another merc nearer: that one goes.
    /// Without this one callout or noise drew the whole team to the same spot and left the other objectives open.
    /// </summary>
    private bool LeftToMate(AiContact contact, float now)
    {
        if (contact.Visible || Difficulty.Teamwork <= .3f || now - Bot.HurtAt < 3f)
        {
            return false;
        }

        float mine = (contact.Position - Pawn.Location).Size();
        if (mine < AiK.MercOwnGround)
        {
            return false;
        }

        RefreshSector(now);
        foreach (SObjectiveTrigger t in _sector)
        {
            if ((t.Location - contact.Position).Size() < AiK.MercSectorReach)
            {
                return false;
            }
        }

        foreach (SPAWNDEFENSE mate in World.Level.DynamicActors<SPAWNDEFENSE>())
        {
            if (!ReferenceEquals(mate, Pawn) && mate is { bDeleteMe: false, Health: > 0, bSleeping: false, bLyingOnGround: false } &&
                (contact.Position - mate.Location).Size() < mine)
            {
                return true;
            }
        }

        return false;
    }

    private void Guard(float now, ref AiPlan plan)
    {
        RefreshSector(now);
        if (DefendWithTrap(now, ref plan)) return;
        // This objective has its traps and another still lacks them: on to that one now.
        if (_post is not null && _postReached && SettingUp(now) && !NeedsTrap(_post))
        {
            _postUntil = MathF.Min(_postUntil, now);
        }

        if ((_post is null && now >= _postUntil) ||
            (_post is not null && (!Defensible(_post) || now > _postUntil || (_sector.Count > 0 && !_sector.Contains(_post)))))
        {
            PickPost(now);
        }

        if (_post is null)
        {
            LookRound(now, ref plan, Pawn.Location + Pawn.Rotation.Vector() * 500f);
            return;
        }

        if ((Feet - _postFloor).Size() > 60f)
        {
            if (!MoveTo(_postFloor, now, ref plan, sneak: false))
            {
                // Another post, but not a full pick every tick while the plan keeps failing.
                _postUntil = MathF.Min(_postUntil, now + 1f);
            }

            return;
        }

        if (!_postReached)
        {
            ArrivePost(now);
        }

        if (_watchYaw >= 0 && now >= _scopeFrom && now < _scopeTo && now - Bot.HurtAt > 3f)
        {
            // Overwatch: the scope along the longest open line from the post, swept a few degrees each way.
            _scopeWantAt = now;
            plan.Keys = default;
            plan.MoveYaw = null;
            plan.Yaw = (_watchYaw + (int)(SvmMath.Sin((now - _scopeFrom) * 0.9f) * 1100f)) & 0xFFFF;
            plan.Pitch = 0;
            plan.Tolerance = 65536;
            return;
        }

        WatchApproaches(now, ref plan);
    }

    private readonly List<int> _watchYaws = new();
    private int _watchIndex;

    /// <summary>
    /// The ways in to watch from the post: the three longest open lines round it at eye height, at least 45 degrees
    /// apart (a corridor, a doorway, the open side of a hall), found once on arrival.
    /// </summary>
    private void FindWatchLines()
    {
        _watchYaws.Clear();
        _watchIndex = 0;
        FVector eye = AiSight.Eye(Pawn);
        Span<float> open = stackalloc float[16];
        for (int i = 0; i < 16; i++)
        {
            FVector direction = new FRotator(0, i * 4096, 0).Vector();
            open[i] = World.LineOpen(eye, eye + direction * 2400f) ? 2400f :
                World.LineOpen(eye, eye + direction * 1400f) ? 1400f :
                World.LineOpen(eye, eye + direction * AiK.WatchLineMin) ? AiK.WatchLineMin : 0f;
        }

        for (int pick = 0; pick < 3; pick++)
        {
            int best = -1;
            for (int i = 0; i < 16; i++)
            {
                if (open[i] < AiK.WatchLineMin || (best >= 0 && open[i] <= open[best])) continue;
                bool apart = true;
                foreach (int yaw in _watchYaws)
                {
                    if (NavFollower.YawError(yaw, i * 4096) < 8192) apart = false;
                }

                if (apart) best = i;
            }

            if (best < 0) break;
            _watchYaws.Add(best * 4096);
            open[best] = 0f;
        }
    }

    /// <summary>
    /// At the post the merc looks down the ways in, one after another, with a glance at the objective now and then.
    /// It used to look at the objective or directly away from it at random, which left the side approaches unwatched.
    /// </summary>
    private void WatchApproaches(float now, ref AiPlan plan)
    {
        if (_watchYaws.Count == 0)
        {
            LookRound(now, ref plan, _post!.Location);
            return;
        }

        if (now >= _lookAt)
        {
            _lookAt = now + (2f + Bot.Random.FRand() * 2f) * Bot.Personality.Tempo;
            if (Bot.Random.FRand() < 0.2f)
            {
                _lookYaw = NavFollower.YawTo(Pawn.Location, _post!.Location) & 0xFFFF;
            }
            else
            {
                _watchIndex = (_watchIndex + 1) % _watchYaws.Count;
                _lookYaw = (_watchYaws[_watchIndex] + (int)(Bot.Random.RandRange(-1f, 1f) * 1500f)) & 0xFFFF;
            }
        }

        plan.Yaw = _lookYaw;
        plan.Pitch = 0;
        plan.Tolerance = 65536;
    }

    /// <summary>
    /// At the post: the stop of the patrol starts here (shorter with other objectives to walk on to), an objective
    /// without its traps is mined at once, and with a long open line the scope is planned for a few seconds of the stop.
    /// </summary>
    private void ArrivePost(float now)
    {
        _postReached = true;
        PostsReached++;
        _visited[_post!] = now;
        _lastPost = _post;
        float stay = (_candidates.Count > 1 ? 7f + Bot.Random.FRand() * 8f : 12f + Bot.Random.FRand() * 18f) / Bot.Personality.Aggression;
        if (CanTrap() && NeedsTrap(_post!))
        {
            _trapNext = MathF.Min(_trapNext, now);
        }

        _watchYaw = -1;
        FindWatchLines();
        // No overwatch with a spy believed close: the scoped view is narrow and a scoped merc can be grabbed.
        bool spyClose = false;
        foreach (AiContact known in Bot.Memory.Contacts)
        {
            if (known.Confidence > 0.3f && (known.Position - Pawn.Location).Size() < AiK.ScopeSafeRange) spyClose = true;
        }

        if (CanSnipe && !spyClose && Bot.Random.FRand() < Bot.Utility)
        {
            FVector eye = AiSight.Eye(Pawn);
            float best = 0f;
            int first = Bot.Random.Rand(16);
            for (int k = 0; k < 16; k++)
            {
                int i = (first + k) & 15;
                FVector direction = new FRotator(0, i * 4096, 0).Vector();
                float open = World.LineOpen(eye, eye + direction * 3400f) ? 3400f :
                    World.LineOpen(eye, eye + direction * 2600f) ? 2600f :
                    World.LineOpen(eye, eye + direction * AiK.OverwatchRange) ? AiK.OverwatchRange : 0f;
                if (open > best)
                {
                    best = open;
                    _watchYaw = i * 4096;
                }
            }

            if (_watchYaw >= 0)
            {
                _scopeFrom = now + 1.5f + Bot.Random.FRand() * 3f;
                _scopeTo = _scopeFrom + 4f + Bot.Random.FRand() * 3f;
                stay = MathF.Max(stay, _scopeTo - now + 1f);
            }
        }

        _postUntil = now + stay;
        if (SettingUp(now))
        {
            // No overwatch and no long stop while mines are owed: a few seconds to find a wall here, then on.
            _watchYaw = -1;
            _postUntil = now + 6f;
        }
    }

    /// <summary>
    /// The next stop: among the objectives of the sector (all that are left when the bot has none), first the nearest
    /// one still missing its traps while the bot carries one, then the one left alone the longest, not the one just
    /// left. A post a team mate holds is avoided, with teamwork.
    /// </summary>
    private void PickPost(float now)
    {
        // The sweep is under way: the pick is made when it ends, and nothing changes until then.
        if (WholeGraphCosts() is not { } costs)
        {
            return;
        }

        World.Teams[1].Release(Bot);
        _post = null;
        _postReached = false;
        _watchYaw = -1;
        SObjectiveTrigger? best = null;
        FVector bestPost = default;
        float bestScore = float.MaxValue;
        _candidates.Clear();
        foreach (SObjectiveTrigger t in _sector)
        {
            if (Defensible(t)) _candidates.Add(t);
        }

        if (_candidates.Count == 0)
        {
            foreach (SObjectiveTrigger t in World.Level.AllActors<SObjectiveTrigger>())
            {
                if (Defensible(t)) _candidates.Add(t);
            }
        }

        bool canTrap = CanTrap();
        foreach (SObjectiveTrigger t in _candidates)
        {
            if (!GuardSpot(t, out FVector spot) || !GuardPost(spot, costs, out FVector post))
            {
                continue;
            }

            float distance = (t.Location - Pawn.Location).Size();
            float since = _visited.TryGetValue(t, out float at) ? now - at : 600f;
            // A stop there within the last 45 s already tried its walls: the patrol goes on rather than waiting at an
            // objective no trap can be put by.
            float score = canTrap && since > 45f && NeedsTrap(t)
                ? distance * 0.3f - 20000f
                : distance * 0.25f - MathF.Min(since, 120f) * 40f + Bot.Random.FRand() * 400f -
                    World.Patrol.Interest(post) * 180f * Bot.Personality.Curiosity;
            if (_candidates.Count > 1 && ReferenceEquals(t, _lastPost))
            {
                score += 6000f;
            }

            if (World.Teams[1].IsClaimedByOther(t, Bot))
            {
                score += 5000f * Difficulty.Teamwork;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = t;
                bestPost = post;
            }
        }

        if (best is null)
        {
            Follower.Clear();
            _postUntil = now + 5f;
            return;
        }

        _post = best;
        _postFloor = bestPost;
        // The walk there; the stop is timed from the arrival (ArrivePost).
        _postUntil = now + 30f + (bestPost - Pawn.Location).Size() / 80f;
        _guardTorch = Bot.Random.FRand() < Difficulty.GadgetUse;
        _guardMotion = !_guardTorch && Bot.Random.FRand() < Difficulty.GadgetUse;
        World.Teams[1].Claim(best, Bot);
    }

    private bool GuardSpot(SObjectiveTrigger objective, out FVector spot)
    {
        if (objective is SComputerObjectiveTrigger terminal) return UseSpot(terminal, out spot, out _);
        FVector location = objective is SDisk disk && disk.PickupInteraction is { } pickup ? pickup.Location :
            objective.InteractionObj?.Location ?? objective.Location;
        int node = Follower.Graph.NearestNode(location, 0f, 150f);
        if (node < 0) { spot = default; return false; }
        spot = new FVector(location.X, location.Y, Follower.Graph.Node(node).Floor.Z);
        return true;
    }

    private bool GuardPost(FVector spot, float[] costs, out FVector post)
    {
        // A terminal's nearest node can be on a desk. Guard it from a reachable floor nearby instead of
        // repeatedly walking to the frontier of an unreachable furniture island (Warehouse's Meeting Room).
        post = default;
        float postScore = float.MaxValue;
        var eyeLift = new FVector(0f, 0f, Follower.Graph.Profile.StandingHeight + AiK.EyeHeightStanding);
        foreach (int n in NodesAround(spot, 300f))
        {
            FVector f = Follower.Graph.Node(n).Floor;
            float d = NavFollower.Horizontal(f - spot);
            if (!float.IsFinite(costs[n]) || !Follower.Graph.Node(n).Standing ||
                !World.LineOpen(f + eyeLift, spot + eyeLift))
            {
                continue;
            }

            // Prefer 150 to 300 uu beside the use spot, with a closer reachable post only in cramped rooms.
            float s = MathF.Abs(d - 235f / Bot.Personality.Aggression) + (d < 150f ? 300f : 0f) +
                Bot.Random.FRand() * 60f - World.Patrol.Interest(f) * 65f * Bot.Personality.Curiosity;
            if (s < postScore)
            {
                postScore = s;
                post = f;
            }
        }

        return postScore < float.MaxValue;
    }

    // ---- cover ------------------------------------------------------------------------------------------------

    private FVector _clearSpot;
    private bool _clearSpotFound;
    private float _clearSpotAt = -10f;

    /// <summary>Ticks an engaging merc held its fire because the bullet's line was blocked (diagnostics).</summary>
    public int HeldShots { get; private set; }

    /// <summary>
    /// A standing spot within <see cref="AiK.ClearShotRadius"/> of <paramref name="target"/> from which a bullet reaches
    /// it: reachable on the graph, the nearest to the merc among up to 64 samples.
    /// </summary>
    private bool ClearShotSpot(FVector target, out FVector spot, out bool ready)
    {
        spot = default;
        float best = float.MaxValue;
        NavGraph graph = Follower.Graph;
        float[]? costs = WholeGraphCosts();
        ready = costs is not null;
        if (costs is null)
        {
            return false;
        }

        var eyeLift = new FVector(0f, 0f, graph.Profile.StandingHeight + Pawn.DefaultObject.BaseEyeHeight);
        List<int> nodes = NodesAround(target, AiK.ClearShotRadius);
        int stride = Math.Max(1, nodes.Count / 64);
        for (int i = 0; i < nodes.Count; i += stride)
        {
            int n = nodes[i];
            if (!float.IsFinite(costs[n]) || !graph.Node(n).Standing || costs[n] >= best)
            {
                continue;
            }

            FVector floor = graph.Node(n).Floor;
            if (ShotBlockedFrom(floor + eyeLift, target))
            {
                continue;
            }

            best = costs[n];
            spot = floor;
        }

        return best < float.MaxValue;
    }

    // ---- the scope -------------------------------------------------------------------------------------------

    /// <summary>
    /// The scope for a spy in sight: far enough (nearer once it is up), the merc not hurt in the last 2 s and no other
    /// spy believed close, since the scoped view is narrow and a scoped merc can be grabbed.
    /// </summary>
    private bool ScopeWanted(AiContact target, float now)
    {
        if (!CanSnipe || now - Bot.HurtAt < 2f || Pawn.Physics != EPhysics.PHYS_Walking)
        {
            return false;
        }

        float distance = (target.Position - Pawn.Location).Size();
        if (distance < (Pc.bBinocularVision ? AiK.ScopeLeaveRange : AiK.ScopeEnterRange))
        {
            return false;
        }

        foreach (AiContact other in Bot.Memory.Contacts)
        {
            if (!ReferenceEquals(other, target) && other.Confidence > 0.3f &&
                (other.Position - Pawn.Location).Size() < AiK.ScopeSafeRange)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// One shot through the scope when the view is on the target, not through a team mate: the scope fires single
    /// shots without spread, so the bot's own aim error decides the hit. An empty clip is reloaded inside the scope.
    /// </summary>
    private void ScopedShot(float now, FVector target, float spread, ref AiPlan plan)
    {
        if (Pawn.MyWeapon is not SWeaponDefense rifle)
        {
            return;
        }

        if (rifle.ClipAmmo <= 0)
        {
            plan.Keys.bReload = 1;
            return;
        }

        if (now - _scopeShotAt < AiK.ScopeShotSeconds || AimErrorTo(target) > spread + AiK.ScopeAimSlack || TeammateInTheWay(target) ||
            ShotBlocked(target))
        {
            return;
        }

        _scopeShotAt = now;
        ScopedShots++;
        plan.Keys.Press(AiCommand.Fire);
        plan.Keys.bFire = 1;
    }
}
