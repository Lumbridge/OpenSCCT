using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>The authored five-second Polar Base spawn-door hack, through solo play, input, replication and HUD.</summary>
public sealed class PlayDoorTimerSmoke(bool delayed = false, bool retry = false) : PlaySmoke
{
    public const string TestName = "play-door-timer";
    public const string DelayedName = "play-door-timer-delayed";
    public const string RetryName = "play-door-timer-retry";
    public override string Name => retry ? RetryName : delayed ? DelayedName : TestName;
    protected override string Map => "PolaE";
    protected override int MaxTicks => 6000;
    private readonly HashSet<int> _seen = new();
    private float _started = -1f;
    private float _lastReport = -1f;
    private bool _hooked;
    private bool _cancelled;
    private bool _restarted;
    private ScriptedPilot? _retryPilot;
    private float _lastDeadline;
    private float _maxDisplayError;
    private int _cancelledTicks;

    protected override (FVector Spot, FVector Face)? Target(SPlayerController pc) => UseSpot(pc, "porteSpawn");

    protected override SmokeResult? Check(PlayRoot root, ScriptedPilot pilot)
    {
        PlaySession session = root.Session!;
        if (!_hooked)
        {
            _hooked = true;
            var beforeTick = session.BeforeClientTick;
            session.BeforeClientTick = (s, tick) =>
            {
                if (delayed && s.Client.World.Level.Info.TimeSeconds < 35.25f)
                    return;
                beforeTick?.Invoke(s, tick);
                if (retry && !_cancelled && _started > 0f &&
                    s.Client.World.Level.Info.TimeSeconds - _started > 1.3f && s.LocalController?.IsInState("s_Hack_Use") == true)
                {
                    _cancelled = true;
                    s.Input.Set("BT_Up", true);
                }
                if (_cancelled)
                    _cancelledTicks++;
                if (_cancelledTicks > 2 && !_restarted && s.LocalController is { } owner &&
                    (owner.IsInState("s_PlayerWalkingSt0H") || owner.IsInState("s_PlayerWalkingCr0H")))
                {
                    _restarted = true;
                    _retryPilot = new ScriptedPilot(Target);
                }
                _retryPilot?.Tick(s);
            };
        }
        var authority = session.ServerSnapshot(host =>
        {
            foreach (SInterrupteur panel in host.World.Level.AllActors<SInterrupteur>())
            {
                if (panel.Event != new Name("porteSpawn") || panel.TriggerInteraction is not { } entry)
                    continue;
                var gri = panel.Level.GRI;
                return (Time: panel.Level.TimeSeconds, Deadline: entry.MinTriggerTime,
                    Elapsed: gri?.ElapsedTime ?? -1, Precise: gri?.PreciseElapsedTime() ?? -1f,
                    Finished: !entry.bUsable, State: entry.GetStateName().ToString());
            }
            return (Time: 0f, Deadline: 0f, Elapsed: -1, Precise: -1f, Finished: false, State: "missing");
        });
        if (session.LocalController is not { } pc)
            return null;
        var snapshot = new HudSnapshot();
        HudSimReader.Read(pc, snapshot);
        if (authority.Deadline > 0f)
        {
            if (authority.Deadline != _lastDeadline)
            {
                _lastDeadline = authority.Deadline;
                _started = -1f;
                _seen.Clear();
            }
            if (_started < 0f)
                _started = authority.Time;
            float remaining = authority.Deadline - authority.Precise;
            int shown = snapshot.HackCountdown?.Seconds ?? -1;
            _maxDisplayError = Math.Max(_maxDisplayError, shown < 0 ? 0f : shown - remaining);
            if (authority.Time - _lastReport >= .45f)
            {
                _lastReport = authority.Time;
                GD.Print($"SVM_DOOR_TIMER server={authority.Time:F3} deadline={authority.Deadline:F3} elapsed={authority.Elapsed} precise={authority.Precise:F3} remaining={remaining:F3} shown={shown} clientElapsed={pc.GameReplicationInfo?.ElapsedTime} clientPrecise={pc.GameReplicationInfo?.PreciseElapsedTime():F3}");
            }
            if (authority.Time - _started > .3f && remaining > .2f)
            {
                if (shown < 1 || Math.Abs(shown - MathF.Ceiling(remaining)) > 0f)
                {
                    SaveShot("-bad-countdown");
                    return SmokeResult.Fail($"Polar door has {remaining:F3}s left but HUD shows {shown}; server clock {authority.Precise:F3}, client clock {pc.GameReplicationInfo?.PreciseElapsedTime():F3}");
                }
                if (_seen.Add(shown))
                    SaveShot($"-remaining-{shown}");
            }
        }
        if (!authority.Finished)
            return null;
        if (_started < 0f || _seen.Count < 3)
            return SmokeResult.Fail($"door completed without three distinct positive countdown values ({string.Join(',', _seen)})");
        if (snapshot.HackCountdown is not null)
            return null;
        if (retry && !_restarted)
            return SmokeResult.Fail("retry scenario never interrupted and restarted the hack");
        return SmokeResult.Pass($"Polar door completed after {authority.Time - _started:F3}s; positive HUD countdown values {string.Join(',', _seen)}; max rounding error {_maxDisplayError:F3}s; countdown removed on completion; retry={_restarted}");
    }
}
