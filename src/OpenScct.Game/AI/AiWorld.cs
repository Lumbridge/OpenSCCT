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
    private long _gatheredTick = -1;

    public AiWorld(Level level, ICollisionWorld collision, NavGraph spyNav, NavGraph mercNav, AiLightField? light = null)
    {
        Level = level ?? throw new ArgumentNullException(nameof(level));
        Collision = collision ?? throw new ArgumentNullException(nameof(collision));
        SpyNav = spyNav ?? throw new ArgumentNullException(nameof(spyNav));
        MercNav = mercNav ?? throw new ArgumentNullException(nameof(mercNav));
        Light = light ?? AiLightField.Unknown;
        Teams = [new AiTeamBoard(0), new AiTeamBoard(1)];
    }

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
        _torchPoses.Clear();
        foreach (SPAWNDEFENSE m in _torches)
        {
            if (!ReferenceEquals(m, target))
            {
                FVector torch = AiSight.Eye(m);
                _torchPoses.Add((torch, m.Controller?.Rotation ?? m.Rotation, LineOpen(torch, target.Location)));
            }
        }

        return Light.LevelAt(target.Location, target.CollisionRadius + target.CollisionHeight * 0.5f, eye, _torchPoses);
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
        HashSet<int> set = _badLinkSets[side];
        if (set.Count > 0)
        {
            _expired.Clear();
            foreach (int link in set)
            {
                if (_badLinks[side].TryGetValue(link, out float until) && until < Now)
                {
                    _expired.Add(link);
                }
            }

            foreach (int link in _expired)
            {
                set.Remove(link);
                _badLinks[side].Remove(link);
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

    public AiTeamBoard(int team)
    {
        Team = team;
    }

    public int Team { get; }

    /// <summary>An enemy position a teammate saw or heard.</summary>
    public readonly record struct Callout(Pawn Enemy, FVector Position, float Time, float Confidence, object From);

    public IReadOnlyList<Callout> Callouts => _callouts;

    public void Call(Pawn enemy, FVector position, float time, float confidence, object from)
    {
        for (int i = 0; i < _callouts.Count; i++)
        {
            if (ReferenceEquals(_callouts[i].Enemy, enemy))
            {
                _callouts[i] = new Callout(enemy, position, time, confidence, from);
                return;
            }
        }

        _callouts.Add(new Callout(enemy, position, time, confidence, from));
    }

    public void Expire(float now)
    {
        for (int i = _callouts.Count - 1; i >= 0; i--)
        {
            Callout c = _callouts[i];
            if (now - c.Time > 10f || c.Enemy.bDeleteMe || c.Enemy.Health <= 0)
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

    /// <summary>Releases every claim of <paramref name="owner"/>.</summary>
    public void Release(object owner)
    {
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
