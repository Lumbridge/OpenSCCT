using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Spy perspective through real input, a replicated pawn, the rendered camera and body visibility.</summary>
public sealed class SpyPerspectiveSmoke : SmokeTest
{
    public const string TestName = "spy-perspective";
    private const string PerspectiveKey = ControlActions.VirtualPrefix + "RB:perspective";
    private const ETraceFlags Geometry = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_LevelGeometry;
    private PlayRoot? _root;
    private string? _oldBehavior;
    private int _phase;
    private long _at;
    private int _firstFrames, _thirdFrames, _crouchedFrames, _standingFrames;
    private bool _placed;
    private bool _standQueued;
    private SmokeResult _result;
    private int _teardown = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown >= 0) return ++_teardown < 6 ? SmokeResult.Running : _result;
        if (_root is null)
        {
            _oldBehavior = SettingsStore.Current.SpyKbm.PerspectiveBehavior;
            SettingsStore.Current.SpyKbm.PerspectiveBehavior = "Toggle";
            _root = TestRange.Start(context, PlaySide.Spy, 0);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (_root.Session is not { } session || session.LocalPawn is not SPawnAttaque pawn ||
            session.LocalController is not { } pc || _root.Controls is not { } input ||
            _root.Pawns?.ViewOf(pawn) is not { } body || _root.Camera is not { } camera)
            return SmokeResult.Running;
        if (session.ServerFailure is { } failure) return Finish(SmokeResult.Fail(failure.Message));
        if (!_placed)
        {
            if (!TestRange.Walking(pc)) return SmokeResult.Running;
            input.Enabled = true;
            input.RequireCapture = false;
            input.CapturePointer = false;
            // The west wall of the generated hall, facing into it. A quick first-person turn must stay by the pawn.
            TestRange.Place(session, _ => session.ServerController, new FVector(pawn.CollisionRadius + 3f, 3072f, 0f), 32768);
            _placed = true;
            _at = session.TicksRun;
            return SmokeResult.Running;
        }
        long elapsed = session.TicksRun - _at;
        if (elapsed > 240) return Finish(SmokeResult.Fail($"phase {_phase} timed out in {pc.GetStateName()}"));

        bool expectedFirst = _phase is 1 or 2 or 4 or 6 or 7;
        if (_phase > 0 && elapsed > 6)
        {
            if (pc.SpyFirstPersonActive != expectedFirst || body.ShadowOnly != expectedFirst)
                return Finish(SmokeResult.Fail($"phase {_phase}: active={pc.SpyFirstPersonActive}, own body hidden={body.ShadowOnly}, expected {expectedFirst}"));
            if (expectedFirst)
            {
                if (camera.Occluded) return Finish(SmokeResult.Fail($"phase {_phase}: clear first-person view was blacked out"));
                _firstFrames++;
                if (pawn.bIsCrouched) _crouchedFrames++;
                else _standingFrames++;
                FVector eye = camera.Current.Location;
                FVector relative = eye - pawn.Location;
                // This also detects a stale third-person look-ahead orbit on the first-person view.
                if (relative.X * relative.X + relative.Y * relative.Y > pawn.CollisionRadius * pawn.CollisionRadius ||
                    Math.Abs(relative.Z) > pawn.CollisionHeight)
                    return Finish(SmokeResult.Fail($"phase {_phase}: drawn eye escaped the pawn cylinder ({relative}; height {pawn.CollisionHeight})"));
                if (session.Client.Map?.Physics?.Collision is { } collision &&
                    (collision.PointCheck(out _, pawn, eye, new FVector(2f, 2f, 2f), Geometry) ||
                     collision.LineCheck(out _, pawn, eye, pawn.Location, default, Geometry)))
                    return Finish(SmokeResult.Fail($"phase {_phase}: drawn eye intersects geometry or sees through the wall at {eye}"));
                input._UnhandledInput(new InputEventMouseMotion { Relative = new Vector2(13f, 4f) });
            }
            else _thirdFrames++;
        }

        switch (_phase)
        {
            case 0 when elapsed >= 60:
                if (pawn.bIsCrouched)
                {
                    if (!_standQueued)
                    {
                        input.Key(ControlActions.VirtualPrefix + "B:crouch", true);
                        input.Key(ControlActions.VirtualPrefix + "B:crouch", false);
                        _standQueued = true;
                    }
                    if (elapsed >= 180) return Finish(SmokeResult.Fail("the spy did not stand before the perspective check"));
                    break;
                }
                if (pawn.Location.X > pawn.CollisionRadius + 8f)
                {
                    if (elapsed < 180) break;
                    FVector server = session.ServerSnapshot(_ => session.ServerController?.Pawn?.Location ?? default);
                    return Finish(SmokeResult.Fail($"the test pawn did not arrive beside the west wall: client {pawn.Location}, server {server}, radius {pawn.CollisionRadius}"));
                }
                if (session.Client.Map?.Physics?.Collision is not { } wall ||
                    !wall.LineCheck(out _, pawn, pawn.Location - new FVector(128f, 0f, 0f), pawn.Location, default, Geometry))
                    return Finish(SmokeResult.Fail("the wall fixture is missing"));
                input.Key(PerspectiveKey, true);
                Next(session);
                break;
            case 1 when elapsed >= 20:
                input.Key(PerspectiveKey, false);
                Next(session);
                break;
            case 2 when elapsed >= 20:
                input.Key(PerspectiveKey, true);
                input.Key(PerspectiveKey, false);
                Next(session);
                break;
            case 3 when elapsed >= 20:
                SettingsStore.Current.SpyKbm.PerspectiveBehavior = "Hold";
                input.Key(PerspectiveKey, true);
                Next(session);
                break;
            case 4 when elapsed >= 20:
                input.Key(PerspectiveKey, false);
                Next(session);
                break;
            case 5 when elapsed >= 20:
                input.Key(PerspectiveKey, true);
                if (!pawn.bIsCrouched)
                {
                    input.Key(ControlActions.VirtualPrefix + "B:crouch", true);
                    input.Key(ControlActions.VirtualPrefix + "B:crouch", false);
                }
                Next(session);
                break;
            case 6 when elapsed >= 60:
                if (!pawn.bIsCrouched) return Finish(SmokeResult.Fail("the spy did not crouch"));
                Next(session);
                break;
            case 7 when elapsed >= 20:
                input.ReleasePointer();
                Next(session);
                break;
            case 8 when elapsed >= 20:
                return Finish(_standingFrames > 10 && _thirdFrames > 10 && _crouchedFrames > 10
                    ? SmokeResult.Pass($"toggle retains first person on release; hold returns on release/focus loss; {_firstFrames} safe rendered first-person frames ({_standingFrames} standing, {_crouchedFrames} crouched) beside a wall; own body restored across {_thirdFrames} third-person frames")
                    : SmokeResult.Fail("not enough rendered perspective samples"));
        }
        return SmokeResult.Running;
    }

    private void Next(PlaySession session)
    {
        _phase++;
        _at = session.TicksRun;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _result = result;
        _teardown = 0;
        if (_oldBehavior is not null) SettingsStore.Current.SpyKbm.PerspectiveBehavior = _oldBehavior;
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
