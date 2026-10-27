using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScctGame.Play.Session;

/// <summary>What the scripted pilot is doing.</summary>
public enum PilotPhase
{
    WaitForPawn,
    StandUp,
    Plan,
    Walk,
    Face,
    PressUse,
    ReleaseUse,
    Done,
    Failed,
}

/// <summary>
/// A scripted local player for smoke runs: it presses the keys a player would (stand up, fast speed, forward, Use)
/// while turning the view toward the next waypoint of a <see cref="RoutePlanner"/> route to a use spot, then faces
/// the object and presses and releases Use. Turning writes the view rotation directly, as the scenario format's
/// view step does; everything else goes through the per-tick input. A test driver, not part of the game.
/// </summary>
public sealed class ScriptedPilot
{
    private const float ArriveDistance = 18f;
    private const int StuckTicks = 120;
    private const float MaxLandingError = 40f;
    private const int MaxReplans = 6;

    private readonly Func<SPlayerController, (FVector Spot, FVector Face)?> _target;
    private List<FVector>? _route;
    private int _waypoint;
    private int _phaseTicks;
    private FVector _lastLocation;
    private int _stillTicks;
    private bool _wasAirborne;
    private int _replans;

    /// <param name="target">The use spot and the point to face, found on the client's world when the pawn exists.</param>
    public ScriptedPilot(Func<SPlayerController, (FVector Spot, FVector Face)?> target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public PilotPhase Phase { get; private set; } = PilotPhase.WaitForPawn;

    public string? Failure { get; private set; }

    /// <summary>The planned route (pawn locations), once planned.</summary>
    public IReadOnlyList<FVector>? Route => _route;

    /// <summary>Ticks the Use key is held before it is released.</summary>
    public int UseHoldTicks { get; init; } = 20;

    /// <summary>The drop the route may take (below the spy's fall damage).</summary>
    public float MaxDrop { get; init; } = 590f;

    /// <summary>The next waypoint index.</summary>
    public int Waypoint => _waypoint;

    /// <summary>Runs one tick of the pilot; call it from <see cref="PlaySession.BeforeClientTick"/>.</summary>
    public void Tick(PlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        LocalInput input = session.Input;
        input.Set("BT_Up", false);
        input.Set("bCrouch", false);
        SPlayerController? pc = session.LocalController;
        SBasePawn? pawn = pc?.Pawn as SBasePawn;
        _phaseTicks++;
        switch (Phase)
        {
            case PilotPhase.WaitForPawn:
                if (pc is not null && pawn is not null && (pc.IsInState("s_PlayerWalkingCr0H") || pc.IsInState("s_PlayerWalkingSt0H") ||
                    pc.IsInState("s_PlayerWalkingSt2H")))
                {
                    Next(PilotPhase.StandUp);
                }

                break;
            case PilotPhase.StandUp:
                // The spy starts crouched and slow; the route is planned for the stance the pawn has, and fast speed
                // keeps a slow spy's back-ledge grab from catching the edges it walks off.
                if (_phaseTicks == 4 && pawn is SPawnAttaque && !pc!.bFastSpeed)
                {
                    input.Commands.Add(new InputCommand("ChangeSpeed"));
                }

                if (_phaseTicks > 30)
                {
                    Next(PilotPhase.Plan);
                }

                break;
            case PilotPhase.Plan:
            {
                if (_target(pc!) is not { } t || session.Client.Map?.Physics?.Collision is not { } collision)
                {
                    Fail("no target or no collision on the client");
                    break;
                }

                var planner = new RoutePlanner(collision, pawn!, pawn!.CollisionRadius, pawn.CollisionHeight) { MaxDrop = MaxDrop };
                _route = planner.Find(pawn.Location, t.Spot, 30f);
                if (_route is null)
                {
                    Fail($"no route from {pawn.Location} to {t.Spot} ({planner.Explored} spots explored)");
                    break;
                }

                _waypoint = 1;
                _lastLocation = pawn.Location;
                Next(PilotPhase.Walk);
                break;
            }

            case PilotPhase.Walk:
            {
                if (_route is null || pawn is null || pc is null)
                {
                    Fail("lost the pawn");
                    break;
                }

                if (!IsWalkingState(pc))
                {
                    // Landing, falling or a traversal state: keep pushing forward toward the waypoint.
                }

                FVector at = pawn.Location;
                bool airborne = pawn.Physics == EPhysics.PHYS_Falling;
                if (_wasAirborne && !airborne && _waypoint < _route.Count && MathF.Abs(_route[_waypoint].Z - at.Z) > MaxLandingError &&
                    _replans++ < MaxReplans)
                {
                    // A fall carried the pawn somewhere else than planned: plan again from where it landed.
                    _wasAirborne = false;
                    Next(PilotPhase.Plan);
                    break;
                }

                _wasAirborne = airborne;
                if (pc.IsInState("OnLedge"))
                {
                    // Caught a ledge on the way down: let go (crouch) and plan again from where it lands.
                    input.Set("bCrouch", _phaseTicks % 20 < 2);
                    _wasAirborne = true;
                    _lastLocation = at;
                    break;
                }

                if (!IsWalkingState(pc) && !airborne)
                {
                    // Landing or a transition: wait for walking before pushing on.
                    _lastLocation = at;
                    break;
                }

                while (_waypoint < _route.Count && Horizontal(_route[_waypoint] - at) < ArriveDistance)
                {
                    _waypoint++;
                }

                if (_waypoint >= _route.Count)
                {
                    Next(PilotPhase.Face);
                    break;
                }

                FVector to = _route[_waypoint] - at;
                pc.SetViewRotation(new FRotator(0, new FVector(to.X, to.Y, 0f).Rotation().Yaw, 0));
                input.Set("BT_Up", true);
                _stillTicks = (at - _lastLocation).SizeSquared() < 0.25f ? _stillTicks + 1 : 0;
                _lastLocation = at;
                if (_stillTicks > StuckTicks)
                {
                    if (_replans++ < MaxReplans)
                    {
                        _stillTicks = 0;
                        Next(PilotPhase.Plan);
                        break;
                    }

                    Fail($"stuck at {at} on the way to waypoint {_waypoint} {_route[_waypoint]} in state {pc.GetStateName()}");
                }

                break;
            }

            case PilotPhase.Face:
                if (_target(pc!) is { } face && pawn is not null)
                {
                    FVector d = face.Face - pawn.Location;
                    pc!.SetViewRotation(new FRotator(0, new FVector(d.X, d.Y, 0f).Rotation().Yaw, 0));
                }

                if (_phaseTicks > 30)
                {
                    Next(PilotPhase.PressUse);
                }

                break;
            case PilotPhase.PressUse:
                input.Set("bUsing", true);
                if (_phaseTicks >= UseHoldTicks)
                {
                    Next(PilotPhase.ReleaseUse);
                }

                break;
            case PilotPhase.ReleaseUse:
                input.Set("bUsing", false);
                if (_phaseTicks > 2)
                {
                    Next(PilotPhase.Done);
                }

                break;
        }
    }

    private static bool IsWalkingState(SPlayerController pc) => pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal);

    private static float Horizontal(FVector v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    private void Next(PilotPhase phase)
    {
        Phase = phase;
        _phaseTicks = 0;
    }

    private void Fail(string reason)
    {
        Failure = reason;
        Phase = PilotPhase.Failed;
    }
}
