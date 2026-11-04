using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Smoke;

/// <summary>Measures the rendered support hand relative to the rifle through real idle and moving jumps.</summary>
public sealed class MercJumpPoseSmoke : SmokeTest
{
    public const string TestName = "merc-jump-pose";
    private readonly List<string> _trace = ["case,time,physics,sequence,clipFrame,leftX,leftY,leftZ,rightX,rightY,rightZ,sourceError,frameStep"];
    private readonly List<string> _results = [];
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private int _case;
    private int _phase;
    private double _phaseAt;
    private double _landedAt = -1;
    private double _recoveryAt = -1;
    private bool _airborne;
    private bool _sawLanding;
    private bool _recoveryShot;
    private bool _lateShot;
    private int _samples;
    private int _recoverySamples;
    private int _recoveryContactSamples;
    private float _maxContactError;
    private float _maxStep;
    private float _maxRecoveryStep;
    private float _maxRecoveryContactError;
    private string _maxContactAt = "";
    private Vector3? _lastLeft;
    private Name _previousSequence;
    private SmokeResult _final;
    private int _teardown = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown >= 0) return ++_teardown < 6 ? SmokeResult.Running : _final;
        if (_root is null)
        {
            _root = TestRange.Start(context, PlaySide.Merc, bots: 0);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (!_root.IsLoaded || _root.Session is not { } session ||
            (_keys is null && !TestRange.Walking(session.LocalController)))
            return context.ElapsedSeconds > 90 ? Finish(SmokeResult.Fail("Merc did not spawn")) : SmokeResult.Running;
        if (session.ServerFailure is { } failure) return Finish(SmokeResult.Fail(failure.Message));
        _keys ??= KeyPilot.Attach(_root);
        _keys.Update();
        if (!_keys.Ready || session.LocalController is not { } pc || session.LocalPawn is not SPAWNDEFENSE pawn ||
            _root.FirstPerson is not { Shown: true, Model: { } model } fp || pawn.MyWeapon is not SWeaponDefense weapon)
            return SmokeResult.Running;

        double elapsed = context.ElapsedSeconds - _phaseAt;
        switch (_phase)
        {
            case 0:
                TestRange.Station lane = TestRange.Stations()["Lane"];
                TestRange.Place(session, _ => session.ServerController, new FVector(1600f, lane.Spot.Y, 0f), 0);
                _keys.ReleaseAll();
                Next(context, 1);
                break;
            case 1:
                if (elapsed < 1) break;
                TestRange.Face(pc, 0);
                if (_case == 1) _keys.Press("BT_Up");
                Next(context, 2);
                break;
            case 2:
                if (elapsed < 0.4) break;
                _keys.Tap("Jump");
                _airborne = _sawLanding = _recoveryShot = _lateShot = false;
                _landedAt = _recoveryAt = -1;
                _samples = _recoverySamples = _recoveryContactSamples = 0;
                _maxContactError = _maxStep = _maxRecoveryStep = _maxRecoveryContactError = 0;
                _lastLeft = null;
                _previousSequence = default;
                Next(context, 3);
                break;
            case 3:
                Sample(context, pawn, weapon, model, fp.ChannelFrame(0));
                if (pawn.Physics == EPhysics.PHYS_Falling) _airborne = true;
                else if (_airborne && _landedAt < 0)
                {
                    _landedAt = context.ElapsedSeconds;
                    TestRange.Shot(_root, TestName, context.ElapsedSeconds, $"{_case}-touchdown");
                }
                if (_landedAt < 0)
                {
                    if (elapsed > 5) return Finish(SmokeResult.Fail("Jump did not leave the ground and land"));
                    break;
                }
                if (context.ElapsedSeconds - _landedAt < 2) break;
                _keys.ReleaseAll();
                string result = string.Create(CultureInfo.InvariantCulture,
                    $"{(_case == 0 ? "idle" : "moving")}: {_samples} frames, {_recoverySamples} recovery frames, {_recoveryContactSamples} contact frames, " +
                    $"max contact error {_maxContactError:F3}uu ({_maxContactAt}), frame step {_maxStep:F3}uu, " +
                    $"recovery step {_maxRecoveryStep:F3}uu, recovery contact error {_maxRecoveryContactError:F3}uu");
                _results.Add(result);
                if (!_sawLanding || _recoverySamples < 10 || _recoveryContactSamples < 3)
                    return Finish(SmokeResult.Fail("Landing and recovery were not both rendered: " + result));
                // The authored idle/walk and landing endpoints keep the support hand against the rifle.
                // A joint-space crossfade can bend the arm through it even though those endpoints agree.
                if (_maxRecoveryContactError > 0.1f)
                    return Finish(SmokeResult.Fail("Support hand left its authored rifle contact during recovery: " + result));
                if (++_case < 2) Next(context, 0);
                else return Finish(SmokeResult.Pass(string.Join("; ", _results)));
                break;
        }
        return SmokeResult.Running;
    }

    private void Sample(SmokeContext context, SPAWNDEFENSE pawn, SWeaponDefense weapon, SkeletalModel model, float frame)
    {
        int left = model.Skeleton.FindBone("Bip01 L Hand");
        int right = model.Skeleton.FindBone("Bip01 R Hand");
        int body = model.Skeleton.FindBone("corps");
        if (left < 0 || right < 0 || body < 0) throw new InvalidOperationException("Rifle contact bones are missing");
        Transform3D root = model.Skeleton.GlobalTransform;
        Transform3D rifle = root * model.CurrentBonePose(body);
        Vector3 leftAt = Relative(root * model.CurrentBonePose(left), rifle);
        Vector3 rightAt = Relative(root * model.CurrentBonePose(right), rifle);
        Name sequence = weapon.ViewAnim.Sequence(0);
        bool landing = sequence == ViewModelClips.FirstLandStFd2 || sequence == ViewModelClips.FirstLandStHi2;
        float error = float.NaN;
        if (model.Sampler.Find(sequence.ToString()) is { Length: > 0 } clip)
        {
            // The runtime frame spans N keys, while SampleBone's phase spans the N-1 key intervals.
            int keys = Math.Max(1, (int)MathF.Round(clip.Length * Views.ViewModelPose.DefaultFps) + 1);
            float phase = frame * keys / (Views.ViewModelPose.DefaultFps * clip.Length);
            bool loop = weapon.ViewAnim.IsAnimating(0) && weapon.ViewAnim.Remaining(0) <= 0f;
            // The loop's final-to-first interval blends local joints. Leave its reference error unreported;
            // the contact assertion covers the first 0.2 s of recovery, before either loop reaches that seam.
            if (!loop || phase <= 1f)
            {
                Vector3 authored = Relative(root * model.Sampler.SampleBone(clip, left, phase),
                    root * model.Sampler.SampleBone(clip, body, phase));
                error = leftAt.DistanceTo(authored);
            }
        }
        float step = _lastLeft is { } prior ? leftAt.DistanceTo(prior) : 0;
        _maxStep = MathF.Max(_maxStep, step);
        if (landing) _sawLanding = true;
        if (_sawLanding)
        {
            if (error > _maxContactError)
            {
                _maxContactError = error;
                _maxContactAt = $"{sequence} at {frame:F3}";
            }
            if (!landing)
            {
                _recoverySamples++;
                _maxRecoveryStep = MathF.Max(_maxRecoveryStep, step);
                if (_recoveryAt < 0)
                {
                    _recoveryAt = context.ElapsedSeconds;
                    TestRange.Shot(_root, TestName, context.ElapsedSeconds, $"{_case}-recovery-entry");
                }
                double recovery = context.ElapsedSeconds - _recoveryAt;
                if (recovery <= 0.2 && float.IsFinite(error))
                {
                    _recoveryContactSamples++;
                    _maxRecoveryContactError = MathF.Max(_maxRecoveryContactError, error);
                }
                if (!_recoveryShot && recovery >= 0.05)
                {
                    _recoveryShot = true;
                    TestRange.Shot(_root, TestName, context.ElapsedSeconds, $"{_case}-recovery-50ms");
                }
                if (!_lateShot && recovery >= 0.15)
                {
                    _lateShot = true;
                    TestRange.Shot(_root, TestName, context.ElapsedSeconds, $"{_case}-recovery-150ms");
                }
            }
        }
        _trace.Add(string.Create(CultureInfo.InvariantCulture,
            $"{_case},{context.ElapsedSeconds:F6},{pawn.Physics},{sequence},{frame:F6}," +
            $"{leftAt.X:F6},{leftAt.Y:F6},{leftAt.Z:F6},{rightAt.X:F6},{rightAt.Y:F6},{rightAt.Z:F6},{error:F6},{step:F6}"));
        if (sequence != _previousSequence)
            GD.Print($"SVM_JUMP {_case} {sequence} frame={frame:F3} left={leftAt} right={rightAt} sourceError={error:F3}uu");
        _previousSequence = sequence;
        _lastLeft = leftAt;
        _samples++;
    }

    private static Vector3 Relative(Transform3D hand, Transform3D rifle) =>
        rifle.Basis.Orthonormalized().Inverse() * (hand.Origin - rifle.Origin) * 100f;

    private void Next(SmokeContext context, int phase) { _phase = phase; _phaseAt = context.ElapsedSeconds; }

    private SmokeResult Finish(SmokeResult result)
    {
        string folder = Path.Combine(SmokePaths.RepoRoot ?? ProjectSettings.GlobalizePath("res://"), "local", "test-results");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "merc-jump-pose.csv");
        File.WriteAllLines(path, _trace);
        _keys?.ReleaseAll();
        _root?.QueueFree();
        _final = result with { Detail = result.Detail + "; trace " + path };
        _teardown = 0;
        return SmokeResult.Running;
    }
}
