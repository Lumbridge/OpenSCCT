using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net.Moves;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=range-roll</c>: the spy's roll and his run under fire at the keys, on TestRange (spy, no bots, host
/// thread, wall clock, HUD). <see cref="KeyPilot"/> runs him at full speed facing +X, taps the roll key, then holds
/// the right key and turns the mouse while he rolls: the roll keeps the direction it began with and the view does not
/// turn (s_PlayerRolling.PlayerMove has no UpdateRotation, spec/movement/roll.md). Then the server puts him back at
/// the spawn and he runs while the server hits him with rifle rounds every third of a second: the hits reach his
/// machine (PlayAnimDmg, adrenaline 1.2 for 5 s) and the run must not be pulled back (no applied correction above
/// 16 uu; spec/native/net-moves.md Edge cases). With a window and <c>SVM_PLAY_SHOTS</c> it saves frames at the roll
/// and the run.
/// </summary>
public sealed class RangeRollSmoke : SmokeTest
{
    public const string TestName = "range-roll";

    private const int TeardownFrames = 6;

    /// <summary>The largest pull back a running spy may get (a frame of running plus margin, as PlayStepCorrectionTests).</summary>
    private const float SmallCorrection = 16f;

    /// <summary>The roll's velocity heading may wander this much (degrees): the pawn turns toward the start direction.</summary>
    private const float HeadingSlack = 4f;

    private readonly List<string> _log = [];
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SmokeResult _final;
    private int _teardown = -1;
    private string _step = "";
    private FVector _spawnFeet;
    private int _pendingHits;
    private bool _hooked;
    private double _stepAt;
    private double _nextHit;
    private double _nextShot;

    // The roll.
    private bool _rolled;
    private int _rollYaw = int.MinValue;
    private int _rollYawDrift;
    private float _rollHeading = float.NaN;
    private float _rollHeadingDrift;
    private int _rollFrames;

    // The run under fire.
    private int _appliedAt = -1;
    private int _applied;
    private float _largest;
    private float _peakSpeed;
    private float _hitAt = float.NaN;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _root = TestRange.Start(context, PlaySide.Spy, bots: 0);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        SPlayerController? pc = session.LocalController;
        if (_keys is null)
        {
            if (TestRange.Walking(pc) && session.LocalPawn is { } spawned && _root.Pawns?.ViewOf(spawned) is not null)
            {
                _spawnFeet = spawned.Location - new FVector(0f, 0f, spawned.CollisionHeight);
                TestRange.Face(pc!, 0);
                _keys = KeyPilot.Attach(_root);
                _keys.Play(Timeline(spawned.bIsCrouched));
                GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_ROLL spy at {spawned.Location}, crouched {spawned.bIsCrouched}"));
            }
            else if (context.ElapsedSeconds > PlayInspectSmoke.SpawnTimeoutSeconds)
            {
                return Finish(SmokeResult.Fail($"the spy was not walking after {PlayInspectSmoke.SpawnTimeoutSeconds:F0} s"));
            }

            return SmokeResult.Running;
        }

        if (!_hooked)
        {
            _hooked = true;
            session.Host.BeforeWorldTick += _ => HitOnServer(session);
        }

        _keys.Update();
        double now = context.ElapsedSeconds;
        if (_keys.Step != _step)
        {
            _step = _keys.Step;
            _stepAt = now;
            OnStepEntered(session, pc!, now);
        }

        if (pc is not null && session.LocalPawn is { } pawn)
        {
            Watch(session, pc, pawn, now);
        }

        if (!_keys.Done)
        {
            return now > 300 ? Finish(SmokeResult.Fail("the timeline did not finish in 300 s")) : SmokeResult.Running;
        }

        if (_keys.Missing.Count > 0)
        {
            return Finish(SmokeResult.Fail("no key bound to " + string.Join(", ", _keys.Missing)));
        }

        return Finish(Verdict(pc));
    }

    private static List<KeyStep> Timeline(bool crouched)
    {
        var steps = new List<KeyStep>();
        if (crouched)
        {
            steps.Add(new KeyStep("stand", 1.2) { Tap = ["Crouch"] });
        }

        steps.Add(new KeyStep("fast", 0.4) { Tap = ["ChangeSpeed"] });
        steps.Add(new KeyStep("run", 1.5) { Hold = ["BT_Up"] });
        steps.Add(new KeyStep("roll", 0.15) { Hold = ["BT_Up"], Tap = ["Roll"] });
        steps.Add(new KeyStep("roll steer", 0.9) { Hold = ["BT_Up", "BT_Right"], LookPerSecond = new Vector2(900f, 0f) });
        steps.Add(new KeyStep("after roll", 1.0));
        steps.Add(new KeyStep("back to spawn", 1.0));
        steps.Add(new KeyStep("run under fire", 4.0) { Hold = ["BT_Up"] });
        steps.Add(new KeyStep("end", 0.5));
        return steps;
    }

    private void OnStepEntered(PlaySession session, SPlayerController pc, double now)
    {
        if (_step == "back to spawn")
        {
            TestRange.Place(session, _ => session.ServerController, _spawnFeet, 0);
        }
        else if (_step == "run under fire")
        {
            TestRange.Face(pc, 0);
            _nextHit = now + 0.5;
            if (MovesOf(session, pc) is { } moves)
            {
                _appliedAt = moves.CorrectionsApplied;
            }
        }

        Shot(now, _step + "-start");
    }

    private void Watch(PlaySession session, SPlayerController pc, SBasePawn pawn, double now)
    {
        bool rolling = pc.IsInState(MovementStateNames.s_PlayerRolling) || pc.IsInState(MovementStateNames.s_PlayerRollingUp);
        if (rolling)
        {
            _rolled = true;
            _rollFrames++;
            if (_rollYaw == int.MinValue)
            {
                _rollYaw = pc.Rotation.Yaw;
                Shot(now, "roll-begin");
            }

            _rollYawDrift = Math.Max(_rollYawDrift, Math.Abs((short)(pc.Rotation.Yaw - _rollYaw)));
            if (pawn.Velocity.Size2D() > 100f)
            {
                float heading = MathF.Atan2(pawn.Velocity.Y, pawn.Velocity.X) * 180f / MathF.PI;
                if (float.IsNaN(_rollHeading))
                {
                    _rollHeading = heading;
                }

                float d = MathF.Abs(((heading - _rollHeading + 540f) % 360f) - 180f);
                _rollHeadingDrift = MathF.Max(_rollHeadingDrift, d);
            }

            if (_rollFrames == 30)
            {
                Shot(now, "roll-mid");
            }
        }

        if (_step != "run under fire")
        {
            return;
        }

        if (now >= _nextHit)
        {
            _nextHit = now + 1.0 / 3.0;
            Interlocked.Increment(ref _pendingHits);
        }

        if (pc.fTimeWhenHit > 0f && float.IsNaN(_hitAt))
        {
            _hitAt = (float)(now - _stepAt);
        }

        _peakSpeed = MathF.Max(_peakSpeed, pawn.Velocity.Size2D());
        if (MovesOf(session, pc) is { } moves && moves.CorrectionsApplied != _applied + _appliedAt && _appliedAt >= 0)
        {
            int count = moves.CorrectionsApplied - _appliedAt;
            if (moves.UpdatingSMove is { } matched)
            {
                float size = (moves.vUpdateLocation - matched.SavedLocation).Size();
                if (now - _stepAt > 0.6)
                {
                    _largest = MathF.Max(_largest, size);
                }

                _log.Add(string.Create(CultureInfo.InvariantCulture, $"{now - _stepAt:F2} s {size:F1} uu at {pawn.Velocity.Size2D():F0} uu/s"));
            }

            _applied = count;
        }

        if (now >= _nextShot)
        {
            _nextShot = now + 1.0;
            Shot(now, "run-under-fire");
        }
    }

    /// <summary>On the server thread: the asked hits, a rifle round in the back of the spy (pill 2, as merc fire).</summary>
    private void HitOnServer(PlaySession session)
    {
        if (Interlocked.Exchange(ref _pendingHits, 0) == 0 || session.ServerController is not { Pawn: { } spy } server)
        {
            return;
        }

        spy.Health = 100;
        server.PawnTakeDamage(5, null, spy.Location, spy.Rotation.Vector() * 100f, SDamageType.SDTBase, 2, 70);
    }

    private SmokeResult Verdict(SPlayerController? pc)
    {
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"roll: {_rollFrames} frames, view yaw drift {_rollYawDrift}, heading drift {_rollHeadingDrift:F1} deg; run under fire: first hit at {_hitAt:F2} s, peak {_peakSpeed:F0} uu/s, {_applied} corrections, largest {_largest:F1} uu [{string.Join("; ", _log)}]");
        GD.Print("SVM_ROLL " + detail);
        var problems = new List<string>();
        if (!_rolled)
        {
            problems.Add("the spy never rolled");
        }
        else
        {
            if (_rollYawDrift != 0)
            {
                problems.Add($"the view turned {_rollYawDrift} during the roll");
            }

            if (_rollHeadingDrift > HeadingSlack)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"the roll changed direction by {_rollHeadingDrift:F1} degrees"));
            }
        }

        if (float.IsNaN(_hitAt) || pc is null)
        {
            problems.Add("the hits never reached the spy's machine");
        }

        if (_peakSpeed < 500f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the hit spy never ran with adrenaline (peak {_peakSpeed:F0} uu/s)"));
        }

        if (_largest > SmallCorrection)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the running spy was pulled back {_largest:F1} uu"));
        }

        return problems.Count == 0 ? SmokeResult.Pass(detail) : SmokeResult.Fail(string.Join("; ", problems) + " (" + detail + ")");
    }

    private static ClientMoves? MovesOf(PlaySession session, SPlayerController pc) =>
        session.Client.World.Level.GetService<IMovementNet>() is NetMovementBridge bridge ? bridge.EndpointOf(pc)?.Client : null;

    private void Shot(double seconds, string label) => TestRange.Shot(_root, TestName, seconds, label);

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _keys?.ReleaseAll();
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
