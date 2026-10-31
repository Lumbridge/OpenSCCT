using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Bots;

/// <summary>
/// A spy's route script: the terminals of the map ranked by walking distance from the spy's spawn, and which of them
/// this spy takes (every <see cref="Stride"/>-th from <see cref="Offset"/>), so two spies split the map. With
/// <see cref="TakeLeftovers"/> a spy that finished its share hacks any terminal still standing. <see cref="Idle"/>
/// spies stay where they spawn.
/// </summary>
public sealed record SpyScript
{
    public int Offset { get; init; }

    public int Stride { get; init; } = 2;

    public bool TakeLeftovers { get; init; } = true;

    public bool Idle { get; init; }

    /// <summary>Walks from terminal to terminal without using them (a spy that only crosses the map).</summary>
    public bool Roam { get; init; }

    public static SpyScript HackShare(int offset, int stride = 2) => new() { Offset = offset, Stride = stride };

    public static SpyScript RoamShare(int offset, int stride = 2) => new() { Offset = offset, Stride = stride, Roam = true };

    public static SpyScript StayHome => new() { Idle = true };
}

/// <summary>
/// A spy bot: walks to a terminal's use spot, faces it, stands still, holds Use until the Use menu opens and releases
/// it (the menu sends <c>InteractionInit</c> for the terminal's entry), then leaves the input alone while the
/// terminal states walk it to the spot and hack, until the terminal is taken. A hack that does not start within
/// <see cref="StartTimeout"/> seconds is asked again, and a terminal that cannot be reached or started is left for
/// later.
/// </summary>
public sealed class SpyBrain : BotBrain
{
    /// <summary>Seconds the spy stands still at the spot before asking.</summary>
    public const float SettleSeconds = 0.4f;

    public const float StartTimeout = 3f;

    /// <summary>Seconds between two crouch presses while the spy is standing.</summary>
    public const float CrouchRetrySeconds = 0.5f;

    /// <summary>Seconds a spy that gave up every terminal waits before trying them all again.</summary>
    public const float RetryAllSeconds = 5f;

    /// <summary>Seconds a terminal that could not be reached or started is left alone before it is tried again.</summary>
    public const float GiveUpSeconds = 20f;

    /// <summary>Longest the spy holds Use waiting for the menu.</summary>
    public const float UseHoldSeconds = 1f;

    private readonly SpyScript _script;
    private readonly List<FVector> _scratch = new();
    private readonly Dictionary<SComputerObjectiveTrigger, float> _given = new();
    private BotNavigator? _nav;
    private NavGrid? _grid;
    private List<SComputerObjectiveTrigger>? _share;
    private SComputerObjectiveTrigger? _target;
    private EPhase _phase;
    private float _phaseTime;
    private int _yaw;
    private int _asks;
    private SBasePawn? _pawn;
    private float _crouchPressAt = -10f;
    private float _gaveUpAllAt = -1f;
    private SComputerObjectiveTrigger? _lastVisited;

    public SpyBrain(SpyScript script)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
    }

    private static readonly Name NAME_s_MenuInteraction = new("s_MenuInteraction");

    private enum EPhase : byte
    {
        Choose,
        Walk,
        Settle,
        PressUse,
        Asked,
        Hacking,
    }

    /// <summary>The walker (diagnostics).</summary>
    public BotNavigator? Navigator => _nav;

    /// <summary>InteractionInit calls sent.</summary>
    public int Requests { get; private set; }

    /// <summary>Terminals this spy saw taken while it hacked them.</summary>
    public int Hacked { get; private set; }

    /// <summary>Terminals given up (unreachable or never started).</summary>
    public int GivenUp => _given.Count;

    public override string Status => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"spy {_phase} target {_target?.TriggerName ?? "-"} waypoints {_nav?.Path.Count ?? 0} plans {_nav?.Plans ?? 0} gave way {_nav?.GiveWays ?? 0} asks {Requests} hacked {Hacked} given up {GivenUp}");

    public override void Reset()
    {
        _phase = EPhase.Choose;
        _target = null;
        _nav?.Clear();
    }

    public override BotInput Think(BotContext context)
    {
        if (_script.Idle)
        {
            return BotInput.Idle;
        }

        _grid ??= context.Nav.Get(context.Map, spy: true);
        // A spy gives way at the second stuck spell blamed on a pawn (a merc gives way at the first).
        _nav ??= new BotNavigator(_grid, BotNavigator.Seed(context.Session.Settings.PlayerName)) { GiveWayAfter = 2 };
        if (!ReferenceEquals(_pawn, context.Pawn))
        {
            _pawn = context.Pawn;
            Reset();
        }

        _share ??= Rank(context);
        float now = context.Now;
        BotInput input = Decide(context, now);

        // The spy walks crouched (its start stance, and what the walk graph is sized for): a landing that stood it
        // up is followed by a crouch press.
        if (_phase is EPhase.Choose or EPhase.Walk && !context.Pawn.bIsCrouched && context.Pawn.Physics == EPhysics.PHYS_Walking &&
            now - _crouchPressAt > CrouchRetrySeconds)
        {
            _crouchPressAt = now;
            input.bCrouch = 1;
        }

        return input;
    }

    private BotInput Decide(BotContext context, float now)
    {
        switch (_phase)
        {
            case EPhase.Choose:
                _target = Next(context);
                ForgetOldGiveUps(now);
                if (_target is null && _given.Count > 0)
                {
                    // Everything left was given up: wait a little, then try again from here.
                    _gaveUpAllAt = _gaveUpAllAt < 0f ? now : _gaveUpAllAt;
                    if (now - _gaveUpAllAt > RetryAllSeconds)
                    {
                        _given.Clear();
                        _gaveUpAllAt = -1f;
                    }
                }

                if (_target is null || !UseSpotFloor(context, _grid!, _target, out FVector floor, out _yaw))
                {
                    return BotInput.Idle;
                }

                _nav!.SetGoal(floor);
                _asks = 0;
                Enter(EPhase.Walk, now);
                goto case EPhase.Walk;

            case EPhase.Walk:
                if (_target is null || IsTaken(context, _target))
                {
                    Enter(EPhase.Choose, now);
                    return BotInput.Idle;
                }

                BotInput walk = _nav!.Steer(context.Pawn, now);
                if (_nav.Failed)
                {
                    _given[_target] = now;
                    Enter(EPhase.Choose, now);
                    return BotInput.Idle;
                }

                if (_nav.Arrived)
                {
                    if (_script.Roam)
                    {
                        // A roaming spy only passes by: on to the next terminal (this one goes to the back).
                        _lastVisited = _target;
                        _share!.Remove(_target);
                        _share.Add(_target);
                        Enter(EPhase.Choose, now);
                        return BotInput.Idle;
                    }

                    Enter(EPhase.Settle, now);
                    return Face(context);
                }

                return walk;

            case EPhase.Settle:
                if (now - _phaseTime < SettleSeconds || !context.Pawn.Velocity.IsZero() || !FacingUseSpot(context))
                {
                    return Face(context);
                }

                _asks++;
                Requests++;
                Enter(EPhase.PressUse, now);
                return Face(context, use: true);

            case EPhase.PressUse:
                // Hold Use until the menu opens, then release it: the menu sends InteractionInit for its entry.
                if (InState(context, NAME_s_MenuInteraction) || now - _phaseTime > UseHoldSeconds)
                {
                    Enter(EPhase.Asked, now);
                    return Face(context);
                }

                return Face(context, use: true);

            case EPhase.Asked:
                if (_target is null || IsTaken(context, _target))
                {
                    Enter(EPhase.Choose, now);
                    return BotInput.Idle;
                }

                if (_target.bTriggering || InTerminalStates(context))
                {
                    Enter(EPhase.Hacking, now);
                }
                else if (now - _phaseTime > StartTimeout)
                {
                    if (_asks >= 3)
                    {
                        _given[_target] = now;
                        Enter(EPhase.Choose, now);
                    }
                    else
                    {
                        // Walk back to the spot (the menu or a bump may have moved the spy) and ask again.
                        _nav!.SetGoal(_nav.Goal);
                        Enter(EPhase.Walk, now);
                    }
                }

                return Face(context);

            case EPhase.Hacking:
                if (_target is null)
                {
                    Enter(EPhase.Choose, now);
                    return BotInput.Idle;
                }

                if (IsTaken(context, _target))
                {
                    Hacked++;
                    Enter(EPhase.Choose, now);
                    return BotInput.Idle;
                }

                if (!_target.bTriggering && !InTerminalStates(context) && now - _phaseTime > 1f)
                {
                    // The hack stopped without the terminal being taken: back to the spot and ask again.
                    Enter(EPhase.Choose, now);
                }

                return Idle();
        }

        return BotInput.Idle;
    }

    private void ForgetOldGiveUps(float now)
    {
        if (_given.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<SComputerObjectiveTrigger, float> kv in _given)
        {
            if (now - kv.Value > GiveUpSeconds)
            {
                _forget.Add(kv.Key);
            }
        }

        foreach (SComputerObjectiveTrigger t in _forget)
        {
            _given.Remove(t);
        }

        _forget.Clear();
    }

    private readonly List<SComputerObjectiveTrigger> _forget = new();

    private bool FacingUseSpot(BotContext context) => Math.Abs((int)(short)(context.Pawn.Rotation.Yaw - _yaw)) <= 1024;

    private BotInput Face(BotContext context, bool use = false) => new()
    {
        View = new FRotator(0, _yaw, 0), bUsing = (byte)(use ? 1 : 0),
        // A holstered spy turns its body with movement, independently of its camera. Settle the body as a player
        // would with a brief forward press; changing only the view can leave every subsequent Use facing away.
        BT_Up = (byte)(!InTerminalStates(context) && !FacingUseSpot(context) ? 1 : 0),
    };

    // While the terminal states run, the spy leaves the view alone (the walk to the spot turns it).
    private static BotInput Idle() => BotInput.Idle;

    private static bool InState(BotContext context, Name state) => context.Controller.IsInState(state);

    private static bool InTerminalStates(BotContext context)
    {
        string state = context.Controller.GetStateName().ToString();
        return state.StartsWith("s_Computer", StringComparison.Ordinal);
    }

    private void Enter(EPhase phase, float now)
    {
        _phase = phase;
        _phaseTime = now;
    }

    // This spy's terminals: all of them ranked by walking distance from the first spawn, every Stride-th from Offset.
    private List<SComputerObjectiveTrigger> Rank(BotContext context)
    {
        var ranked = new List<(float Length, int Order, SComputerObjectiveTrigger Terminal)>();
        List<SComputerObjectiveTrigger> all = Terminals(context);
        for (int i = 0; i < all.Count; i++)
        {
            float length = UseSpotFloor(context, _grid!, all[i], out FVector floor, out _)
                ? PathLength(_grid!, context.Pawn, floor, _scratch)
                : float.PositiveInfinity;
            ranked.Add((length, i, all[i]));
        }

        ranked.Sort((a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.Order.CompareTo(b.Order));
        var share = new List<SComputerObjectiveTrigger>();
        for (int i = _script.Offset; i < ranked.Count; i += Math.Max(1, _script.Stride))
        {
            share.Add(ranked[i].Terminal);
        }

        foreach ((float _, int _, SComputerObjectiveTrigger t) in ranked)
        {
            if (!share.Contains(t))
            {
                share.Add(t);
            }
        }

        _shareCount = Math.Min(share.Count, (ranked.Count - _script.Offset + Math.Max(1, _script.Stride) - 1) / Math.Max(1, _script.Stride));
        return share;
    }

    private int _shareCount;

    private SComputerObjectiveTrigger? Next(BotContext context)
    {
        int limit = _script.TakeLeftovers ? _share!.Count : _shareCount;
        for (int i = 0; i < limit; i++)
        {
            SComputerObjectiveTrigger t = _share![i];
            if (!t.bDeleteMe && !_given.ContainsKey(t) && !IsTaken(context, t) && !t.bTriggering &&
                !(_script.Roam && ReferenceEquals(t, _lastVisited) && limit > 1))
            {
                return t;
            }
        }

        return null;
    }
}
