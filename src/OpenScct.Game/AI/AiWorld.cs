using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

/// <summary>What the bots of one level share: the map's navigation, light and noise, the objectives and the teams' boards.</summary>
public sealed class AiWorld
{
    private const ETraceFlags SightFlags = ETraceFlags.TRACE_World | ETraceFlags.TRACE_StopAtFirstHit;

    private readonly List<SSmokeEmitter> _smokes = new();
    private readonly List<SPAWNDEFENSE> _torches = new();
    private readonly List<SComputerObjectiveTrigger> _terminals = new();
    private readonly Dictionary<int, float>[] _badLinks = [new(), new()];
    private readonly HashSet<int>[] _badLinkSets = [new(), new()];
    private readonly Dictionary<int, float>[] _badEdges = [new(), new()];
    private readonly HashSet<int>[] _badEdgeSets = [new(), new()];
    private long _gatheredTick = -1;
    private readonly List<AiController> _bots = new();
    private readonly Dictionary<SComputerObjectiveTrigger, MercResponse> _responses = new();
    private readonly Dictionary<(Pawn Enemy, (int X, int Y, int Z) Cell), float> _patrolSeen = new();

    internal AiPatrolHistory Patrol { get; }
    private sealed record MercResponse(AiController Primary, AiController? Cover, FVector Approach, float ReassignAt);

    internal (bool Assigned, bool Cover, FVector Approach) RespondTo(AiController bot, SComputerObjectiveTrigger terminal, float now)
    {
        if (bot.Difficulty.Teamwork <= .3f) return (true, false, default);
        // Simultaneous alerts split the team by objective. One alarm gets a responder and an exit guard.
        int alarms = 0;
        foreach (SComputerObjectiveTrigger t in _terminals)
            if (t.bTriggering && !IsTaken(t) && ++alarms > 1) return (true, false, default);

        if (!_responses.TryGetValue(terminal, out MercResponse? response) ||
            now >= response.ReassignAt || !AvailableForAlarm(response.Primary))
        {
            AiController? primary = null;
            AiController? cover = null;
            float first = float.MaxValue;
            float second = float.MaxValue;
            foreach (AiController mate in _bots)
            {
                if (!AvailableForAlarm(mate) || mate.Difficulty.Teamwork <= .3f) continue;
                FVector delta = mate.Pawn!.Location - terminal.Location;
                float score = (NavFollower.Horizontal(delta) + 2f * MathF.Abs(delta.Z)) / mate.Personality.Aggression;
                if (score < first)
                {
                    cover = primary;
                    second = first;
                    primary = mate;
                    first = score;
                }
                else if (score < second)
                {
                    cover = mate;
                    second = score;
                }
            }

            if (primary is null) return (true, false, default);
            response = new(primary, cover, primary.Pawn!.Location, now + 7f);
            _responses[terminal] = response;
        }

        bool covering = ReferenceEquals(response.Cover, bot);
        return (covering || ReferenceEquals(response.Primary, bot), covering, response.Approach);
    }

    private static bool AvailableForAlarm(AiController bot) =>
        bot.Team == 1 && bot.Route is null && !bot.Held && !bot.Stopped &&
        bot.TeamPlay.Goal == TeamGoal.None && bot.Pawn is SPAWNDEFENSE { Health: > 0, bDeleteMe: false, bLyingOnGround: false, bSleeping: false } pawn &&
        ReferenceEquals(pawn, bot.Controller.Pawn);

    internal void ObserveApproach(Pawn enemy, FVector point, float now)
    {
        var key = (enemy, AiPatrolHistory.Cell(point));
        if (_patrolSeen.TryGetValue(key, out float lastSeen) && now - lastSeen < 14f)
        {
            _patrolSeen[key] = now;
            return;
        }

        _patrolSeen[key] = now;
        Patrol.Record(point);
    }

    internal IReadOnlyList<AiController> Bots => _bots;
    internal void RegisterBot(AiController bot) => _bots.Add(bot);

    public bool HasOrderRecipients(int team) => _bots.Exists(bot => bot.Team == team && bot.Route is null && !bot.Controller.bDeleteMe);

    public AiController? IssueOrder(SPlayerController player, int team, AiOrder order, FVector location)
    {
        if (player.Pawn is not SBasePawn requester || team is < 0 or > 1) return null;
        var candidates = new List<(AiController Bot, float Cost)>();
        foreach (AiController bot in _bots)
        {
            if (bot.Team != team) continue;
            if (!bot.TeamPlay.CanTakeOrder(requester))
            {
                if (ReferenceEquals(bot.TeamPlay.OrderRequester, requester)) return null;
                continue;
            }

            float cost = (bot.Pawn!.Location - location).Size() / bot.Personality.Teamwork;
            if (ReferenceEquals(bot.TeamPlay.OrderRequester, requester)) cost *= .5f;
            if (cost < 5500f) candidates.Add((bot, cost));
        }

        candidates.Sort((a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : a.Bot.Index.CompareTo(b.Bot.Index));
        foreach (var candidate in candidates)
        {
            if (!candidate.Bot.TeamPlay.TryOrder(requester, order, location, Now)) continue;
            foreach (var other in candidates)
                if (other.Bot != candidate.Bot && ReferenceEquals(other.Bot.TeamPlay.OrderRequester, requester))
                    other.Bot.TeamPlay.CancelOrder(Now);
            return candidate.Bot;
        }

        return null;
    }

    public AiWorld(Level level, ICollisionWorld collision, NavGraph spyNav, NavGraph mercNav, AiLightField? light = null)
    {
        Level = level ?? throw new ArgumentNullException(nameof(level));
        Collision = collision ?? throw new ArgumentNullException(nameof(collision));
        SpyNav = spyNav ?? throw new ArgumentNullException(nameof(spyNav));
        MercNav = mercNav ?? throw new ArgumentNullException(nameof(mercNav));
        Light = light ?? AiLightField.Unknown;
        Patrol = level.GetService<AiPatrolHistory>() ?? new AiPatrolHistory();
        Teams = [new AiTeamBoard(0), new AiTeamBoard(1)];
        Defence = new MercDefence(this);
    }

    /// <summary>How the merc bots share out the objectives that are left (MercDefence.cs).</summary>
    public MercDefence Defence { get; }

    public Level Level { get; }

    public ICollisionWorld Collision { get; }

    public NavGraph SpyNav { get; }

    public NavGraph MercNav { get; }

    /// <summary>The map's light as the bots read it (a test may replace it, as with a dark field).</summary>
    public AiLightField Light { get; set; }

    public AiNoiseBoard Noise { get; } = new();

    /// <summary>The spies' (0) and mercs' (1) boards.</summary>
    public AiTeamBoard[] Teams { get; }

    /// <summary>HUD messages delivered to bot controllers this tick and before (the bots read and clear their own).</summary>
    public AiMessages Messages { get; } = new();

    public float Now => Level.TimeSeconds;

    /// <summary>The terminals of the level, in actor order.</summary>
    public IReadOnlyList<SComputerObjectiveTrigger> Terminals => _terminals;

    /// <summary>Smoke clouds alive this tick.</summary>
    public IReadOnlyList<SSmokeEmitter> Smokes => _smokes;

    /// <summary>
    /// Installs <paramref name="world"/> on its level: the MakeNoise listener and the HUD message tap over the level's
    /// objective net calls.
    /// </summary>
    public static void Install(AiWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Level.SetService(world);
        world.Level.SetService<INoiseHearing>(world.Noise);
        IObjectiveNet inner = world.Level.GetService<IObjectiveNet>() ?? ObjectiveNetLocal.Instance;
        if (inner is not AiObjectiveTap)
        {
            world.Level.SetService<IObjectiveNet>(new AiObjectiveTap(inner, world.Messages));
        }
    }

    public static AiWorld? Of(Level level) => level?.GetService<AiWorld>();

    /// <summary>Refreshes the per-tick lists (smoke, torches, terminals, noises) once a tick.</summary>
    public void BeginTick()
    {
        if (_gatheredTick == Level.TickCount)
        {
            return;
        }

        _gatheredTick = Level.TickCount;
        _bots.RemoveAll(bot => bot.Controller.bDeleteMe);
        _smokes.Clear();
        foreach (SSmokeEmitter s in Level.DynamicActors<SSmokeEmitter>())
        {
            if (!s.bDeleteMe)
            {
                _smokes.Add(s);
            }
        }

        Light.Time = Level.TimeSeconds;
        Light.FollowSwitches();
        _torches.Clear();
        foreach (SPAWNDEFENSE m in Level.DynamicActors<SPAWNDEFENSE>())
        {
            if (!m.bDeleteMe && m.Health > 0 && m.Torche is { bDeleteMe: false } t && (t.bInUse || t.LightType != ELightType.LT_None))
            {
                _torches.Add(m);
            }
        }

        if (_terminals.Count == 0)
        {
            foreach (SComputerObjectiveTrigger t in Level.AllActors<SComputerObjectiveTrigger>())
            {
                _terminals.Add(t);
            }
        }

        foreach (SComputerObjectiveTrigger terminal in _terminals)
            if (!terminal.bTriggering) _responses.Remove(terminal);
        Noise.Gather(Level);
        foreach (AiTeamBoard team in Teams)
        {
            team.Expire(Now);
        }
    }

    public NavGraph NavFor(bool spy) => spy ? SpyNav : MercNav;

    // ---- lines ------------------------------------------------------------------------------------------------

    /// <summary>True when the line from <paramref name="a"/> to <paramref name="b"/> hits no level geometry or mover.</summary>
    public bool LineOpen(FVector a, FVector b) => !Collision.LineCheck(out _, null, b, a, default, SightFlags);

    /// <summary>True when the segment passes through a smoke cloud (a sphere of RadiusEffect round the emitter).</summary>
    public bool SmokeBetween(FVector a, FVector b)
    {
        if (OpenScct.Game.Equipment.ResponsiveSmokeWorld.IsEnabled(Level))
            return OpenScct.Game.Equipment.ResponsiveSmokeWorld.For(Level).SmokeBetween(a, b);
        foreach (SSmokeEmitter s in _smokes)
        {
            float r = s.RadiusEffect > 0f ? s.RadiusEffect : AiK.SmokeRadius;
            if (SegmentSphere(a, b, s.Location, r))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="p"/> is inside a smoke cloud.</summary>
    public bool InSmoke(FVector p, float extra = 0f)
    {
        if (OpenScct.Game.Equipment.ResponsiveSmokeWorld.IsEnabled(Level))
            return OpenScct.Game.Equipment.ResponsiveSmokeWorld.For(Level).InSmoke(p, extra);
        foreach (SSmokeEmitter s in _smokes)
        {
            float r = (s.RadiusEffect > 0f ? s.RadiusEffect : AiK.SmokeRadius) + extra;
            if (FVector.DistSquared(p, s.Location) <= r * r)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentSphere(FVector a, FVector b, FVector c, float r)
    {
        FVector d = b - a;
        float len2 = d.SizeSquared();
        float t = len2 > 0f ? SvmMath.Clamp(((c - a) | d) / len2, 0f, 1f) : 0f;
        return FVector.DistSquared(a + d * t, c) <= r * r;
    }

    // ---- light ------------------------------------------------------------------------------------------------

    private readonly List<(FVector Location, FRotator Aim, bool Clear)> _torchPoses = [];

    /// <summary>
    /// The light level on <paramref name="target"/> as seen from <paramref name="eye"/>, as the screen lights it: the
    /// map's lights and spot shadow lights by the actor lighting rules (<see cref="AiLightField"/>), plus every other
    /// merc torch that is on (a spot shadow light, its line open), all under the same saturation.
    /// </summary>
    public float LightOn(Pawn target, FVector eye)
    {
        ArgumentNullException.ThrowIfNull(target);
        return LightAt(target.Location, target.CollisionRadius + target.CollisionHeight * 0.5f, eye, target);
    }

    /// <summary>The same light rule at a hypothetical body position, for inspecting a search location.</summary>
    public float LightAt(FVector point, float radius, FVector eye, Pawn? self = null)
    {
        _torchPoses.Clear();
        foreach (SPAWNDEFENSE m in _torches)
        {
            if (!ReferenceEquals(m, self))
            {
                FVector torch = AiSight.Eye(m);
                _torchPoses.Add((torch, m.Controller?.Rotation ?? m.Rotation, LineOpen(torch, point)));
            }
        }

        return Light.LevelAt(point, radius, eye, _torchPoses);
    }

    /// <summary>The light around a floor point (for hiding spots); the unknown level without light data.</summary>
    public float LightAround(FVector floor) => Light.LevelAround(floor + new FVector(0f, 0f, AiK.SpyCrouchHeight), AiK.PawnRadius);

    // ---- objectives -------------------------------------------------------------------------------------------

    /// <summary>True when the terminal is taken (hacked screen, or its objective taken).</summary>
    public bool IsTaken(SComputerObjectiveTrigger terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (terminal.bDeleteMe || terminal.SkinState == SComputerObjectiveTrigger.SKIN_Hacked)
        {
            return true;
        }

        foreach (SObjective o in Level.AllActors<SObjective>())
        {
            if (o.ObjectiveTaken && o.Triggers.Contains(terminal))
            {
                return true;
            }
        }

        return false;
    }

    // ---- links that failed ------------------------------------------------------------------------------------

    /// <summary>Leaves link <paramref name="link"/> of the side's graph out of plans for <see cref="AiK.BadLinkSeconds"/>.</summary>
    public void MarkBadLink(bool spy, int link)
    {
        if (link < 0)
        {
            return;
        }

        int side = spy ? 0 : 1;
        _badLinks[side][link] = Now + AiK.BadLinkSeconds;
        _badLinkSets[side].Add(link);
    }

    /// <summary>The links of the side's graph that are left out now.</summary>
    public IReadOnlySet<int> BadLinks(bool spy)
    {
        int side = spy ? 0 : 1;
        return Expire(_badLinkSets[side], _badLinks[side]);
    }

    // ---- edges that are shut (closed doors, movers, places a walker stuck at twice) -----------------------------

    /// <summary>Leaves edges <paramref name="edges"/> of the side's graph out of plans for <see cref="AiK.BlockedEdgeSeconds"/>.</summary>
    public void MarkBadEdges(bool spy, IReadOnlyList<int> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        int side = spy ? 0 : 1;
        foreach (int e in edges)
        {
            _badEdges[side][e] = Now + AiK.BlockedEdgeSeconds;
            _badEdgeSets[side].Add(e);
        }
    }

    /// <summary>The edges of the side's graph that are left out now.</summary>
    public IReadOnlySet<int> BadEdges(bool spy)
    {
        int side = spy ? 0 : 1;
        return Expire(_badEdgeSets[side], _badEdges[side]);
    }

    private HashSet<int> Expire(HashSet<int> set, Dictionary<int, float> until)
    {
        if (set.Count > 0)
        {
            _expired.Clear();
            foreach (int k in set)
            {
                if (until.TryGetValue(k, out float t) && t < Now)
                {
                    _expired.Add(k);
                }
            }

            foreach (int k in _expired)
            {
                set.Remove(k);
                until.Remove(k);
            }
        }

        return set;
    }

    private readonly List<int> _expired = new();
}

/// <summary>A team's shared knowledge: callouts of enemy positions, and claims on objectives and posts.</summary>
public sealed class AiTeamBoard
{
    private readonly List<Callout> _callouts = new();
    private readonly Dictionary<Actor, object> _claims = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (FVector At, float Until)> _searchClaims = new(ReferenceEqualityComparer.Instance);

    public AiTeamBoard(int team)
    {
        Team = team;
    }

    public int Team { get; }
    private readonly Dictionary<SComputerObjectiveTrigger, (AiController Lead, AiController Helper, FVector Screen, float HoldUntil, float Expires)> _smokePlays = new();

    internal bool ReserveSmoke(SComputerObjectiveTrigger target, AiController lead, AiController helper, FVector screen, float now)
    {
        if (_smokePlays.TryGetValue(target, out var play) && now < play.Expires) return false;
        _smokePlays[target] = (lead, helper, screen, now + 4.5f, now + 12f);
        return true;
    }

    internal bool AwaitSmoke(SComputerObjectiveTrigger target, AiController lead, float now)
    {
        return _smokePlays.TryGetValue(target, out var play) && ReferenceEquals(lead, play.Lead) &&
            now < play.HoldUntil && !play.Helper.World.InSmoke(play.Screen, 30f) &&
            play.Helper.Pawn is { Health: > 0, bLyingOnGround: false } && !play.Helper.Stopped && !play.Helper.Held;
    }

    internal void CancelSmoke(SComputerObjectiveTrigger target, AiController helper)
    {
        if (_smokePlays.TryGetValue(target, out var play) && ReferenceEquals(helper, play.Helper))
            _smokePlays.Remove(target);
    }
    private readonly Dictionary<SComputerObjectiveTrigger, (AiController Owner, float Until)> _objectiveFrags = new();

    internal bool ObjectiveFragReserved(SComputerObjectiveTrigger alarm, AiController bot, float now) =>
        _objectiveFrags.TryGetValue(alarm, out var frag) && !ReferenceEquals(bot, frag.Owner) && now < frag.Until;

    internal bool ReserveObjectiveFrag(SComputerObjectiveTrigger alarm, AiController bot, float now)
    {
        if (ObjectiveFragReserved(alarm, bot, now)) return false;
        _objectiveFrags[alarm] = (bot, now + 4.5f);
        return true;
    }

    /// <summary>An enemy position a teammate saw or heard.</summary>
    public readonly record struct Callout(Pawn Enemy, FVector Position, float Time, float Confidence, object From);

    public IReadOnlyList<Callout> Callouts => _callouts;

    /// <summary>A called position: where a player would say he is, to the nearest <see cref="AiK.CalloutGrid"/> uu.</summary>
    private static FVector Rough(FVector p) => new(MathF.Round(p.X / AiK.CalloutGrid) * AiK.CalloutGrid,
        MathF.Round(p.Y / AiK.CalloutGrid) * AiK.CalloutGrid, p.Z);

    public void Call(Pawn enemy, FVector position, float time, float confidence, object from)
    {
        for (int i = 0; i < _callouts.Count; i++)
        {
            if (ReferenceEquals(_callouts[i].Enemy, enemy))
            {
                // A callout is a few words, not a live feed: one per enemy every 1.5 s.
                if (time - _callouts[i].Time >= AiK.CalloutSeconds || time < _callouts[i].Time)
                {
                    _callouts[i] = new Callout(enemy, Rough(position), time, confidence, from);
                }

                return;
            }
        }

        _callouts.Add(new Callout(enemy, Rough(position), time, confidence, from));
    }

    public void Expire(float now)
    {
        for (int i = _callouts.Count - 1; i >= 0; i--)
        {
            Callout c = _callouts[i];
            if (now - c.Time > 10f)
            {
                _callouts.RemoveAt(i);
            }
        }
    }

    /// <summary>Claims <paramref name="target"/> (an objective or post) for <paramref name="owner"/>; false when another holds it.</summary>
    public bool Claim(Actor target, object owner)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_claims.TryGetValue(target, out object? holder) && !ReferenceEquals(holder, owner))
        {
            return false;
        }

        _claims[target] = owner;
        return true;
    }

    public bool IsClaimedByOther(Actor target, object owner) =>
        _claims.TryGetValue(target, out object? holder) && !ReferenceEquals(holder, owner);

    public void ClaimSearch(FVector at, object owner, float now) => _searchClaims[owner] = (at, now + 2f);

    public bool SearchClaimedByOther(FVector at, object owner, float now)
    {
        foreach (KeyValuePair<object, (FVector At, float Until)> claim in _searchClaims)
        {
            if (!ReferenceEquals(claim.Key, owner) && claim.Value.Until > now &&
                (claim.Value.At - at).SizeSquared() < 240f * 240f)
            {
                return true;
            }
        }

        return false;
    }

    public void ReleaseSearch(object owner) => _searchClaims.Remove(owner);

    /// <summary>Releases every claim of <paramref name="owner"/>.</summary>
    public void Release(object owner)
    {
        ReleaseSearch(owner);
        _released.Clear();
        foreach (KeyValuePair<Actor, object> kv in _claims)
        {
            if (ReferenceEquals(kv.Value, owner))
            {
                _released.Add(kv.Key);
            }
        }

        foreach (Actor a in _released)
        {
            _claims.Remove(a);
        }
    }

    private readonly List<Actor> _released = new();
}

/// <summary>A HUD message one bot controller received.</summary>
public readonly record struct AiMessage(PlayerController Receiver, int Switch, Actor? OptionalObject, float Time);

/// <summary>The SGameMessages switches the bots' controllers received, as their HUD would show them.</summary>
public sealed class AiMessages
{
    private readonly List<AiMessage> _messages = new();

    public void Add(AiMessage message)
    {
        _messages.Add(message);
        if (_messages.Count > 256)
        {
            _messages.RemoveRange(0, 128);
        }
    }

    /// <summary>Moves the messages of <paramref name="receiver"/> into <paramref name="into"/>.</summary>
    public void Take(PlayerController receiver, List<AiMessage> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (int i = 0; i < _messages.Count; i++)
        {
            if (ReferenceEquals(_messages[i].Receiver, receiver))
            {
                into.Add(_messages[i]);
                _messages.RemoveAt(i);
                i--;
            }
        }
    }
}

/// <summary>
/// The level's objective net calls with the localized messages to bot controllers noted for them (the HUD a bot would
/// read). Everything goes on to the real implementation unchanged.
/// </summary>
public sealed class AiObjectiveTap : IObjectiveNet
{
    private readonly IObjectiveNet _inner;
    private readonly AiMessages _messages;

    public AiObjectiveTap(IObjectiveNet inner, AiMessages messages)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
    }

    public void AddRemoveObjectiveOnClientHud(SObjective Objective, bool Add) => _inner.AddRemoveObjectiveOnClientHud(Objective, Add);

    public void AddRemoveObjectiveForRadar(SObjectiveTrigger Trigger, bool bAdd) => _inner.AddRemoveObjectiveForRadar(Trigger, bAdd);

    public void AddRemoveActorOnClientHud(STriggerInteraction Interaction, SPlayerController? Instigator, bool Add) =>
        _inner.AddRemoveActorOnClientHud(Interaction, Instigator, Add);

    public void SetFlagCarrier(SFlag Flag, bool taken) => _inner.SetFlagCarrier(Flag, taken);

    public void AddObjetOnClient(SInteractionManager Manager, SInteractObject? Obj, int i) => _inner.AddObjetOnClient(Manager, Obj, i);

    public void RemoveInteractionObjOnClient(SInteractionManager Manager, SInteractObject? Object) =>
        _inner.RemoveInteractionObjOnClient(Manager, Object);

    public void ReceiveLocalizedMessage(PlayerController Receiver, int Switch, Actor? OptionalObject)
    {
        if (Receiver is { Player: AiViewport })
        {
            _messages.Add(new AiMessage(Receiver, Switch, OptionalObject, Receiver.Level?.TimeSeconds ?? 0f));
        }

        _inner.ReceiveLocalizedMessage(Receiver, Switch, OptionalObject);
    }
}
