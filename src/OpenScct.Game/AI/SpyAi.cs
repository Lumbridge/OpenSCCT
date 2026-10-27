using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>
/// A spy bot. Goals, scored every decision period:
/// <list type="bullet">
/// <item><b>Hack</b>: the terminal with the cheapest route (light and known mercs make a route dearer, by the
/// difficulty's stealth), not claimed by a teammate; it sneaks there crouched (silent: crouched steps play no sound)
/// or runs when no merc is about, uses the terminal (stand still, hold Use until the menu opens, release) and waits
/// while the terminal states hack.</item>
/// <item><b>Evade</b>: when a merc it sees faces it and it is lit, or it was hurt: run to the darkest nearby spot out
/// of the merc's line of sight, with a quick-launched smoke grenade at the feet when the merc is close.</item>
/// <item><b>Hide</b>: a merc in sight that does not face it while it stands in the dark: crouch, freeze and watch.</item>
/// <item><b>Shock</b>: a merc close by and turned away (or in its face): draw the shocker, aim, fire, holster.</item>
/// </list>
/// Night vision goes on in the dark and off in the light, thermal when smoke is near.
/// </summary>
public sealed class SpyAi : AiBrain
{
    public const int GIdle = 0;
    public const int GHack = 1;
    public const int GEvade = 2;
    public const int GHide = 3;
    public const int GShock = 4;

    private static readonly Name NAME_s_MenuInteraction = new("s_MenuInteraction");

    private enum HackPhase : byte
    {
        Walk,
        Settle,
        PressUse,
        Asked,
        Hacking,
    }

    private SComputerObjectiveTrigger? _target;
    private HackPhase _hack;
    private float _hackAt;
    private int _asks;
    private int _useYaw;
    private readonly Dictionary<SComputerObjectiveTrigger, float> _givenUp = new(ReferenceEqualityComparer.Instance);
    private FVector _hideSpot;
    private float _hideSpotAt = -10f;
    private Pawn? _threat;
    private float _smokeAt = -100f;
    private float _visionAt = -100f;
    private int _shockPhase;
    private float _shockAt;
    private float _shockCooldownUntil;
    private float _hideCooldownUntil;
    private int _failures;
    private float _lastFailAt = -10f;
    private FVector _wander;
    private float _wanderAt = -100f;

    public SpyAi(AiController bot)
        : base(bot)
    {
    }

    protected override bool Spy => true;

    /// <summary>Terminals this spy saw taken while it hacked them.</summary>
    public int Hacked { get; private set; }

    /// <summary>Use requests sent (Use held until the menu opened).</summary>
    public int Requests { get; private set; }

    public int SmokesThrown { get; private set; }

    public int ShocksFired { get; private set; }

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"spy {GoalName(Goal)} {_hack} target {_target?.TriggerName ?? "-"} path {Follower.NextIndex}/{Follower.Path.Count} links {Follower.LinksDone}/{Follower.LinksFailed} hacked {Hacked}");

    public static string GoalName(int goal) => goal switch
    {
        GHack => "hack",
        GEvade => "evade",
        GHide => "hide",
        GShock => "shock",
        _ => "idle",
    };

    public override void Reset()
    {
        base.Reset();
        _target = null;
        _hack = HackPhase.Walk;
        _shockPhase = 0;
    }

    // ---- choice ----------------------------------------------------------------------------------------------

    protected override int Choose(float now)
    {
        // A hack under way is finished unless the spy is being shot.
        bool hurt = now - Bot.HurtAt < 1.5f;
        if (Goal == GHack && _hack == HackPhase.Hacking && !hurt)
        {
            return GHack;
        }

        if (Goal == GShock && _shockPhase != 0 && now - _shockAt < 3f)
        {
            return GShock;
        }

        float exposure = Exposure(now, out Pawn? threat, out float threatDistance, out bool threatFacing);
        _threat = threat;
        float myLight = World.LightOn(Pawn, threat is null ? AiSight.Eye(Pawn) + Pawn.Rotation.Vector() * 300f : AiSight.Eye(threat));
        Span<AiOption> options = stackalloc AiOption[5];
        int n = 0;
        options[n++] = new AiOption(GIdle, 0.05f);
        if (NextTerminal(now) is not null)
        {
            // Right after a shock the guard is stunned: the moment to hack.
            options[n++] = new AiOption(GHack, 0.6f + (now > _shockCooldownUntil - 8f && now < _shockCooldownUntil - 3f ? 0.6f : 0f));
        }

        if (exposure > 0.25f || hurt)
        {
            options[n++] = new AiOption(GEvade, 0.4f + exposure + (hurt ? 0.8f : 0f));
        }

        // Freeze in the dark while a merc goes by, a few seconds at a time (then the plan goes on round it).
        if (threat is not null && !threatFacing && threatDistance < 1100f && myLight < 0.25f &&
            (Goal == GHide ? now - GoalSince < 5f : now > _hideCooldownUntil))
        {
            options[n++] = new AiOption(GHide, 0.75f);
        }

        // The shocker on a merc in the way: close, or guarding the terminal the spy wants (it stuns, it does not kill).
        bool inTheWay = threat is not null && _target is { } t && (threat.Location - t.Location).Size() < 900f;
        if (threat is not null && threatDistance < 650f && Pawn.MyWeapon is SStickyShocker && now > _shockCooldownUntil &&
            (threatDistance < 350f || (inTheWay && !threatFacing)) && Bot.Random.FRand() < Difficulty.GadgetUse + 0.2f)
        {
            options[n++] = new AiOption(GShock, 0.5f + Difficulty.GadgetUse * 0.6f);
        }

        return Pick(options[..n]);
    }

    /// <summary>
    /// How exposed the spy is: a merc it sees (confirmed) that faces it, by distance, with the light on the spy as
    /// that merc sees it. Every input is what the spy's own screen shows (the merc's body and torch).
    /// </summary>
    private float Exposure(float now, out Pawn? threat, out float distance, out bool facing)
    {
        threat = null;
        distance = float.MaxValue;
        facing = false;
        float best = 0f;
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (!c.Visible || !c.Confirmed)
            {
                continue;
            }

            FVector to = Pawn.Location - c.Pawn.Location;
            float d = to.Size();
            float cos = d > 1f ? (to | c.Pawn.Rotation.Vector()) / d : 1f;
            bool faces = cos > 0.75f;
            float light = World.LightOn(Pawn, AiSight.Eye(c.Pawn));
            float e = (faces ? 1f : 0.25f) * (light > 0.15f ? 1f : 0.35f) * SvmMath.Max(0f, 1f - d / 2500f);
            if (e > best || (threat is null))
            {
                if (e >= best)
                {
                    best = e;
                }

                if (d < distance)
                {
                    threat = c.Pawn;
                    distance = d;
                    facing = faces;
                }
            }
        }

        return best;
    }

    protected override void LeaveGoal(int from, int to, float now)
    {
        if (from == GHack && _hack is not HackPhase.Hacking)
        {
            _hack = HackPhase.Walk;
        }

        if (from == GShock)
        {
            _shockPhase = 0;
        }

        if (from == GHide)
        {
            _hideCooldownUntil = now + 8f;
        }

        if (to != GHack)
        {
            World.Teams[0].Release(Bot);
        }

        Follower.Clear();
    }

    // ---- acting ----------------------------------------------------------------------------------------------

    protected override void Act(float now, ref AiPlan plan)
    {
        Vision(now, ref plan);
        switch (Goal)
        {
            case GHack:
                Hack(now, ref plan);
                break;
            case GEvade:
                Evade(now, ref plan);
                break;
            case GHide:
                Hide(ref plan);
                break;
            case GShock:
                Shock(now, ref plan);
                break;
            default:
                Wander(now, ref plan);
                break;
        }
    }

    /// <summary>Nothing to do (every terminal given up for now): walk to a nearby spot, so a stuck spy moves on.</summary>
    private void Wander(float now, ref AiPlan plan)
    {
        plan.Crouched = true;
        if (now - _wanderAt > 6f || (Feet - _wander).Size() < 40f)
        {
            _wanderAt = now;
            List<int> nodes = NodesAround(Pawn.Location, 700f);
            if (nodes.Count == 0)
            {
                Follower.StartWiggle(now);
                return;
            }

            _wander = Follower.Graph.Node(nodes[Bot.Random.Rand(nodes.Count)]).Floor;
            Follower.Clear();
        }

        if (!MoveTo(_wander, now, ref plan, sneak: true) && now - _lastFailAt > 1.2f)
        {
            _lastFailAt = now;
            Follower.StartWiggle(now);
        }
    }

    private bool MercsAbout(float now)
    {
        foreach (AiContact c in Bot.Memory.Contacts)
        {
            if (c.Confidence > 0.25f && (c.Position - Pawn.Location).Size() < 2200f)
            {
                return true;
            }
        }

        return false;
    }

    private SComputerObjectiveTrigger? NextTerminal(float now)
    {
        if (_target is { } current && !World.IsTaken(current) && !_givenUp.ContainsKey(current))
        {
            return current;
        }

        SComputerObjectiveTrigger? best = null;
        float bestScore = float.MaxValue;
        foreach (SComputerObjectiveTrigger t in World.Terminals)
        {
            if (World.IsTaken(t) || t.bTriggering && !ReferenceEquals(t, _target) || (_givenUp.TryGetValue(t, out float at) && now - at < 25f))
            {
                continue;
            }

            // Straight-line distance plus the known danger round it; teammates' claims push it back.
            float score = (t.Location - Pawn.Location).Size();
            foreach (AiContact c in Bot.Memory.Contacts)
            {
                if (c.Confidence > 0.2f)
                {
                    float d = (c.Position - t.Location).Size();
                    if (d < 1000f)
                    {
                        score += (1000f - d) * c.Confidence * Difficulty.Stealth * 2f;
                    }
                }
            }

            if (World.Teams[0].IsClaimedByOther(t, Bot))
            {
                score += 4000f * (0.3f + Difficulty.Teamwork);
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return best;
    }

    private void Hack(float now, ref AiPlan plan)
    {
        switch (_hack)
        {
            case HackPhase.Walk:
            {
                SComputerObjectiveTrigger? t = NextTerminal(now);
                if (t is null)
                {
                    plan.Crouched = true;
                    return;
                }

                if (!ReferenceEquals(t, _target))
                {
                    _target = t;
                    _asks = 0;
                    Follower.Clear();
                    World.Teams[0].Claim(t, Bot);
                }

                if (!UseSpot(t, out FVector floor, out _useYaw))
                {
                    GiveUp(now);
                    return;
                }

                bool sneak = Difficulty.Stealth >= 0.5f || MercsAbout(now);
                if (!MoveTo(floor, now, ref plan, sneak, Difficulty.Stealth, DangerPenalty(Difficulty.Stealth)))
                {
                    // No way from here: wiggle free a couple of times before the terminal is given up.
                    if (Follower.Failed && now - _lastFailAt > 1.2f)
                    {
                        _lastFailAt = now;
                        if (++_failures >= 3)
                        {
                            _failures = 0;
                            GiveUp(now);
                        }
                        else
                        {
                            Follower.StartWiggle(now);
                        }
                    }

                    return;
                }

                _failures = 0;

                if (Follower.Arrived)
                {
                    EnterHack(HackPhase.Settle, now);
                    plan.Yaw = _useYaw;
                }

                return;
            }

            case HackPhase.Settle:
                plan.Yaw = _useYaw;
                plan.Crouched = null;
                if (now - _hackAt < 0.4f || !Pawn.Velocity.IsZero() || NavFollower.YawError(Pc.Rotation.Yaw & 0xFFFF, _useYaw) > 600)
                {
                    if (now - _hackAt > 3f)
                    {
                        EnterHack(HackPhase.Walk, now);
                        Follower.Clear();
                    }

                    return;
                }

                _asks++;
                Requests++;
                EnterHack(HackPhase.PressUse, now);
                plan.Keys.bUsing = 1;
                return;

            case HackPhase.PressUse:
                plan.Yaw = _useYaw;
                if (Pc.IsInState(NAME_s_MenuInteraction) || now - _hackAt > 1f)
                {
                    EnterHack(HackPhase.Asked, now);
                    return;
                }

                plan.Keys.bUsing = 1;
                return;

            case HackPhase.Asked:
                plan.Yaw = _useYaw;
                if (_target is null || World.IsTaken(_target))
                {
                    EnterHack(HackPhase.Walk, now);
                    return;
                }

                if (_target.bTriggering || InTerminalStates())
                {
                    EnterHack(HackPhase.Hacking, now);
                }
                else if (now - _hackAt > 3f)
                {
                    if (_asks >= 3)
                    {
                        GiveUp(now);
                    }
                    else
                    {
                        // Back to the spot (the menu or a bump may have moved the spy) and ask again.
                        EnterHack(HackPhase.Walk, now);
                        Follower.Clear();
                    }
                }

                return;

            case HackPhase.Hacking:
                // The terminal states own the pawn while they walk it to the spot and hack: no input.
                plan.Yaw = null;
                plan.Crouched = null;
                if (_target is null)
                {
                    EnterHack(HackPhase.Walk, now);
                    return;
                }

                if (World.IsTaken(_target))
                {
                    Hacked++;
                    World.Teams[0].Release(Bot);
                    _target = null;
                    EnterHack(HackPhase.Walk, now);
                    return;
                }

                if (!_target.bTriggering && !InTerminalStates() && now - _hackAt > 1f)
                {
                    EnterHack(HackPhase.Walk, now);
                    Follower.Clear();
                }

                // The walk to the use spot that never arrives (a terminal collider that keeps the pawn short of the
                // spot, docs/AI.md): the terminal is left for later.
                if (Pc.IsInState(NAME_s_Computer_GoTo) && now - _hackAt > 8f)
                {
                    GiveUp(now);
                }

                return;
        }
    }

    private static readonly Name NAME_s_Computer_GoTo = new("s_Computer_GoTo");

    private void EnterHack(HackPhase phase, float now)
    {
        _hack = phase;
        _hackAt = now;
    }

    private void GiveUp(float now)
    {
        if (_target is not null)
        {
            _givenUp[_target] = now;
        }

        World.Teams[0].Release(Bot);
        _target = null;
        Follower.Clear();
        EnterHack(HackPhase.Walk, now);
    }

    private bool InTerminalStates() => Pc.GetStateName().ToString().StartsWith("s_Computer", StringComparison.Ordinal);

    private void Evade(float now, ref AiPlan plan)
    {
        Pawn? threat = _threat;
        FVector threatEye = threat is not null ? AiSight.Eye(threat) : (Bot.Memory.Best()?.Position ?? Pawn.Location);

        // A smoke grenade at the feet when a merc is close and facing (quick launch, the left key while holstered).
        if (threat is not null && (threat.Location - Pawn.Location).Size() < 750f && now - _smokeAt > 8f &&
            Pawn.WeaponLoaded == 0 && Bot.Random.FRand() < Difficulty.GadgetUse * 0.1f)
        {
            int slot = GadgetSlot<SGadSmokeGrenade>(out SGadSmokeGrenade? smoke);
            if (slot >= 0 && smoke is { Charge: > 0 })
            {
                plan.Keys.Press(AiCommand.GadgetQuickChoice, slot);
                _smokeAt = now;
                SmokesThrown++;
            }
        }

        if (now - _hideSpotAt > 1.5f || (Pawn.Location - _hideSpot).Size() < 40f)
        {
            _hideSpot = FindHideSpot(threatEye);
            _hideSpotAt = now;
        }

        if (!MoveTo(_hideSpot, now, ref plan, sneak: false, stealth: Difficulty.Stealth * 0.5f, DangerPenalty(1f)))
        {
            // Nowhere to go: at least get low.
            plan.Crouched = true;
        }
        else
        {
            plan.Crouched = false;
        }
    }

    /// <summary>The best spot within 900 uu: out of the threat's line of sight, dark, and away from it.</summary>
    private FVector FindHideSpot(FVector threatEye)
    {
        List<int> nodes = NodesAround(Pawn.Location, 900f);
        NavGraph g = Follower.Graph;
        FVector best = Feet;
        float bestScore = float.MaxValue;
        int stride = Math.Max(1, nodes.Count / 48);
        for (int i = 0; i < nodes.Count; i += stride)
        {
            FVector floor = g.Node(nodes[i]).Floor;
            float light = g.HasLight ? SvmMath.Max(0f, g.LightOf(nodes[i])) : 0.3f;
            bool seen = World.LineOpen(threatEye, floor + new FVector(0f, 0f, 60f));
            float away = (floor - threatEye).Size();
            float score = (seen ? 3f : 0f) + light * 2f - away / 1500f + (floor - Pawn.Location).Size() / 1200f;
            if (score < bestScore)
            {
                bestScore = score;
                best = floor;
            }
        }

        return best;
    }

    private void Hide(ref AiPlan plan)
    {
        plan.Crouched = true;
        if (_threat is not null)
        {
            LookAt(_threat.Location, ref plan);
            plan.Tolerance = 65536;
        }
    }

    private void Shock(float now, ref AiPlan plan)
    {
        Pawn? target = _threat;
        if (target is null || target.Health <= 0)
        {
            _shockPhase = 0;
            return;
        }

        plan.Crouched = null;
        LookAt(target.Location, ref plan);
        plan.Tolerance = 65536;
        switch (_shockPhase)
        {
            case 0:
                // Draw: a short tap of the fire-mode key draws the shocker (with its laser).
                _shockPhase = 1;
                _shockAt = now;
                plan.Keys.bFireMode = 1;
                return;
            case 1:
                if (now - _shockAt < 0.08f)
                {
                    plan.Keys.bFireMode = 1;
                    return;
                }

                if (Pawn.WeaponLoaded != 0)
                {
                    _shockPhase = 2;
                    _shockAt = now;
                }
                else if (now - _shockAt > 1.5f)
                {
                    _shockPhase = 0;
                }

                return;
            case 2:
                if (AimErrorTo(target.Location) < 2f + Difficulty.AimSettledDegrees || now - _shockAt > 1.2f)
                {
                    plan.Keys.Press(AiCommand.Fire);
                    plan.Keys.bFire = 1;
                    ShocksFired++;
                    _shockPhase = 3;
                    _shockAt = now;
                }

                return;
            default:
                if (now - _shockAt > 0.6f)
                {
                    // Holster with another tap and go back to the plan while the merc is stunned (the sticky shock
                    // freezes it for seconds; it does not kill).
                    plan.Keys.bFireMode = 1;
                    _shockPhase = 0;
                    _shockCooldownUntil = now + 8f;
                }

                return;
        }
    }

    private void Vision(float now, ref AiPlan plan)
    {
        if (Difficulty.GadgetUse < 0.15f || now - _visionAt < 3f || Goal == GHack && _hack is HackPhase.PressUse or HackPhase.Asked or HackPhase.Hacking)
        {
            return;
        }

        int mode = AiSight.RendMapOf(Pc);
        bool smoke = World.InSmoke(Pawn.Location, 400f);
        float light = World.LightAround(Feet);
        int want = smoke ? SGoggle.REN_ThermalVision : light < 0.12f ? SGoggle.REN_NightVision : SGoggle.REN_DynLight;
        if (want == mode)
        {
            return;
        }

        _visionAt = now;
        if (want == SGoggle.REN_DynLight)
        {
            // The active mode's key turns it off.
            plan.Keys.Press(AiCommand.SwitchHeadset, mode == SGoggle.REN_ThermalVision ? 2 : 1);
        }
        else
        {
            plan.Keys.Press(AiCommand.SwitchHeadset, want == SGoggle.REN_ThermalVision ? 2 : 1);
        }
    }
}
