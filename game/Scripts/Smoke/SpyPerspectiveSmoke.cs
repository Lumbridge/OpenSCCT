using System;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Render;
using OpenScctGame.Render.Characters;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Spy perspective through real input, a replicated pawn, the rendered camera and body visibility.</summary>
public sealed class SpyPerspectiveSmoke : SmokeTest
{
    public const string TestName = "spy-perspective";
    private const string PerspectiveKey = ControlActions.VirtualPrefix + "RB:perspective";
    private const string ReadyKey = ControlActions.VirtualPrefix + "Y:ready";
    private const string FireKey = ControlActions.VirtualPrefix + "RT:fire";
    private const string UseKey = ControlActions.VirtualPrefix + "use";
    private const string BackKey = ControlActions.VirtualPrefix + "back";
    private const string BinocularKey = ControlActions.VirtualPrefix + "scope";
    private static readonly Name LaserSocket = new("Laser");
    private const ETraceFlags Geometry = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_LevelGeometry;
    private PlayRoot? _root;
    private string? _oldBehavior;
    private string? _oldScopeBehavior;
    private int _phase;
    private long _at;
    private int _firstFrames, _thirdFrames, _crouchedFrames, _standingFrames, _armedFrames;
    private bool _placed;
    private bool _standQueued;
    private bool _placedForShot;
    private bool _expectRig;
    private bool _missingPistol;
    private Transform3D _wallPose;
    private FVector? _thirdPersonLaserEnd;
    private FVector? _thirdPersonLaserDot;
    private int _shotBefore;
    private SkeletalModel? _preloadedArms;
    private SkeletalModel? _preloadedWeapon;
    private SmokeResult _result;
    private int _teardown = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_teardown >= 0) return ++_teardown < 6 ? SmokeResult.Running : _result;
        if (_root is null)
        {
            _oldBehavior = SettingsStore.Current.SpyKbm.PerspectiveBehavior;
            _oldScopeBehavior = SettingsStore.Current.SpyKbm.ScopeBehavior;
            SettingsStore.Current.SpyKbm.PerspectiveBehavior = "Toggle";
            SettingsStore.Current.SpyKbm.ScopeBehavior = "Toggle";
            _root = TestRange.Start(context, PlaySide.Spy, 0);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (_root.Session is not { } session || session.LocalPawn is not SPawnAttaque pawn ||
            session.LocalController is not { } pc || _root.Controls is not { } input ||
            _root.Pawns?.ViewOf(pawn) is not { } body || _root.Camera is not { } camera ||
            _root.SpyFirstPerson is not { } rig)
            return SmokeResult.Running;
        if (session.ServerFailure is { } failure) return Finish(SmokeResult.Fail(failure.Message));
        if (!_placed)
        {
            if (!TestRange.Walking(pc)) return SmokeResult.Running;
            input.Enabled = true;
            input.RequireCapture = false;
            input.CapturePointer = false;
            string? shared = _root.Map?.SharedRoot;
            _expectRig = SkeletalModel.FindGlb("SPerso.ATT_01_fp", shared) is not null &&
                SkeletalModel.FindGlb("SWeaponFirst.p190_fp", shared) is not null;
            if (rig.Available != _expectRig)
                return Finish(SmokeResult.Fail($"the first-person rig availability was {rig.Available}, expected {_expectRig}"));
            _preloadedArms = rig.ArmsModel;
            _preloadedWeapon = rig.WeaponModel;
            // The west wall of the generated hall, facing into it. A quick first-person turn must stay by the pawn.
            TestRange.Place(session, _ => session.ServerController, new FVector(pawn.CollisionRadius + 3f, 3072f, 0f), 32768);
            _placed = true;
            _at = session.TicksRun;
            return SmokeResult.Running;
        }
        long elapsed = session.TicksRun - _at;
        if (elapsed > 240)
            return Finish(SmokeResult.Fail($"phase {_phase} timed out in {pc.GetStateName()}, " +
                $"loaded={pawn.WeaponLoaded}, weapon={pawn.MyWeapon?.GetType().Name}/{pawn.MyWeapon?.bDeleteMe}, " +
                $"dummy={pawn.WeaponDummy?.GetType().Name}/{pawn.WeaponDummy?.bDeleteMe}"));

        // Replicated world attachments may load during the test. Track the actual preloaded eye-view models.
        if (!ReferenceEquals(rig.ArmsModel, _preloadedArms) || !ReferenceEquals(rig.WeaponModel, _preloadedWeapon))
            return Finish(SmokeResult.Fail($"phase {_phase}: the Spy view replaced a preloaded first-person model"));

        bool expectedFirst = _phase is 1 or 2 or 4 or 6 or 7 or 11 or >= 13 and <= 20 or 22;
        if (_phase > 0 && elapsed > (_phase is 21 or 22 ? 60 : 6))
        {
            bool expectedShadow = expectedFirst || _phase == 21 && !pc.bBehindView;
            if (pc.SpyFirstPersonActive != expectedFirst || body.ShadowOnly != expectedShadow)
                return Finish(SmokeResult.Fail($"phase {_phase}: eye={pc.SpyFirstPersonActive}/{expectedFirst}, own body hidden={body.ShadowOnly}/{expectedShadow}"));
            bool expectedRig = _expectRig && expectedFirst && _phase is 11 or 13 or 14;
            bool armsDrawn = rig.ArmsModel?.Root.IsVisibleInTree() == true;
            bool weaponDrawn = rig.WeaponModel?.Root.IsVisibleInTree() == true;
            if ((_phase != 15 || pawn.WeaponLoaded == 0) &&
                (rig.Shown != expectedRig || armsDrawn != expectedRig || weaponDrawn != expectedRig))
                return Finish(SmokeResult.Fail($"phase {_phase}: rig={rig.Shown}, arms={armsDrawn}, P190={weaponDrawn}, expected {expectedRig}"));
            if (expectedFirst)
            {
                if (camera.Occluded) return Finish(SmokeResult.Fail($"phase {_phase}: clear first-person view was blacked out"));
                _firstFrames++;
                if (pawn.bIsCrouched) _crouchedFrames++;
                else _standingFrames++;
                if (pawn.WeaponLoaded == 2) _armedFrames++;
                FVector eye = camera.Current.Location;
                FVector relative = eye - pawn.Location;
                if (relative.Z < 15f)
                    return Finish(SmokeResult.Fail($"phase {_phase}: eye dropped below the stance's head ({relative}; eye height {pawn.EyeHeight}; base eye height {pawn.BaseEyeHeight})"));
                // This also detects a stale third-person look-ahead orbit on the first-person view.
                if (relative.X * relative.X + relative.Y * relative.Y > pawn.CollisionRadius * pawn.CollisionRadius ||
                    Math.Abs(relative.Z) > pawn.CollisionHeight)
                    return Finish(SmokeResult.Fail($"phase {_phase}: drawn eye escaped the pawn cylinder ({relative}; height {pawn.CollisionHeight})"));
                if (session.Client.Map?.Physics?.Collision is { } collision &&
                    (collision.PointCheck(out _, pawn, eye, new FVector(2f, 2f, 2f), Geometry) ||
                     collision.LineCheck(out _, pawn, eye, pawn.Location, default, Geometry)))
                    return Finish(SmokeResult.Fail($"phase {_phase}: drawn eye intersects geometry or sees through the wall at {eye}"));
                if (_phase <= 7)
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
                input.Key(ReadyKey, true);
                Next(session);
                break;
            case 9 when elapsed >= 6:
                input.Key(ReadyKey, false);
                Next(session);
                break;
            case 10 when elapsed >= 60:
                if (pawn.WeaponLoaded != 2) return Finish(SmokeResult.Fail("the spy did not draw the weapon before changing perspective"));
                if (pawn.MyWeapon is not SStickyShocker || pawn.SelectedGadget is null ||
                    body.Attachments?.TryGetWeapon(out _, out _) != true)
                {
                    string server = session.ServerSnapshot(_ => session.ServerController?.SBPawn is { } p
                        ? $"weapon={p.MyWeapon?.GetType().Name}, gadget={p.SelectedGadget?.GetType().Name}, grant={p.GearGrantWeapon}"
                        : "no pawn");
                    return Finish(SmokeResult.Fail($"armed spy has no full kit or drawn model: client weapon={pawn.MyWeapon?.GetType().Name}, gadget={pawn.SelectedGadget?.GetType().Name}; server {server}"));
                }
                TestRange.Face(pc, 32768);
                input.Key(PerspectiveKey, true);
                Next(session);
                break;
            case 11 when elapsed >= 30:
                if (_expectRig)
                {
                    if (!rig.Shown || rig.WeaponModel is not { } wallPistol)
                        return Finish(SmokeResult.Fail("the armed eye view lost the P190 at the wall"));
                    FVector eye = camera.Current.Location;
                    FVector ahead = eye + camera.Current.Rotation.Vector() * 96f;
                    if (session.Client.Map?.Physics?.Collision is not { } collision || !collision.LineCheck(out _, pawn, ahead, eye, default, Geometry))
                        return Finish(SmokeResult.Fail("the armed eye view is no longer facing the nearby wall"));
                    _wallPose = CameraRelative(wallPistol, eye, camera.Current.Rotation);
                }
                input.Key(PerspectiveKey, false);
                Next(session);
                break;
            case 12 when elapsed >= 20:
                if (!_expectRig) return Pass();
                if (!_placedForShot)
                {
                    TestRange.Place(session, _ => session.ServerController, new FVector(1024f, 3072f, 0f), 0);
                    TestRange.Face(pc, 0);
                    _placedForShot = true;
                    _at = session.TicksRun;
                    break;
                }
                if (_root.SpyLasers is { BeamsDrawn: > 0, DotsDrawn: > 0 } thirdPersonLaser &&
                    ReferenceEquals(thirdPersonLaser.BeamPawn, pawn))
                {
                    _thirdPersonLaserEnd = thirdPersonLaser.BeamEnd;
                    _thirdPersonLaserDot = thirdPersonLaser.DotAt;
                }
                else if (elapsed < 120) break;
                TestRange.Shot(_root, TestName, session.TicksRun / 60.0, "third person");
                input.Key(PerspectiveKey, true);
                Next(session);
                break;
            case 13 when elapsed >= 35:
                if (!rig.Shown || rig.WeaponModel is not { Meshes.Count: > 0 } weapon || rig.ArmsModel is not { Meshes.Count: > 0 } arms)
                    return Finish(SmokeResult.Fail("the drawn view is missing the P190 or arms"));
                if (!InForeground(arms) || !InForeground(weapon))
                    return Finish(SmokeResult.Fail("the first-person P190 or arms escaped the foreground layer"));
                if (rig.SupportContactError is < 0f or > 0.008f)
                    return Finish(SmokeResult.Fail($"the support hand lost the fore-end grip ({rig.SupportContactError:F3} m)"));
                if (!SamePose(_wallPose, CameraRelative(weapon, camera.Current.Location, camera.Current.Rotation)))
                    return Finish(SmokeResult.Fail("the P190 pose changed between the nearby wall and the open hall"));
                if (pawn.MyWeapon is not SWeaponAttaque { bDeleteMe: false } pistol)
                {
                    // A link can deliver the ready state before its owner-only actor reference.
                    // The P190 still draws while fire is unavailable.
                    if (elapsed < 120) break;
                    _missingPistol = true;
                    input.Key(ReadyKey, true);
                    _phase = 15;
                    _at = session.TicksRun;
                    break;
                }
                if (!rig.TryGetSocket(pistol, LaserSocket, out Transform3D emitter) ||
                    _root.SpyLasers is not { BeamsDrawn: > 0 } lasers ||
                    !ReferenceEquals(lasers.BeamPawn, pawn) ||
                    (UeSpace.ToUe(emitter.Origin) - lasers.BeamStart).Size() > 0.25f)
                    return Finish(SmokeResult.Fail("the local P190 laser did not start at its first-person socket"));
                if (_thirdPersonLaserEnd is not { } end || _thirdPersonLaserDot is not { } dot ||
                    lasers.DotsDrawn == 0 || (lasers.BeamEnd - end).Size() > 1f || (lasers.DotAt - dot).Size() > 1f)
                    return Finish(SmokeResult.Fail($"the P190 laser target changed with perspective: " +
                        $"beam={_thirdPersonLaserEnd}/{lasers.BeamEnd}, dot={_thirdPersonLaserDot}/{lasers.DotAt}"));
                TestRange.Shot(_root, TestName, session.TicksRun / 60.0, "P190 aim");
                _shotBefore = pistol.ShotCounter;
                input.Key(FireKey, true);
                Next(session);
                break;
            case 14:
                if (rig.WeaponModel is not { } firedWeapon ||
                    !SamePose(_wallPose, CameraRelative(firedWeapon, camera.Current.Location, camera.Current.Rotation)))
                    return Finish(SmokeResult.Fail("firing moved the first-person P190"));
                if (rig.SupportContactError is < 0f or > 0.008f)
                    return Finish(SmokeResult.Fail($"firing moved the support hand off the fore-end ({rig.SupportContactError:F3} m)"));
                if (elapsed >= 4) input.Key(FireKey, false);
                if (elapsed < 40) break;
                if (pawn.MyWeapon is not SWeaponAttaque shot || shot.ShotCounter != _shotBefore + 1)
                    return Finish(SmokeResult.Fail($"expected one shot, counter={(pawn.MyWeapon as SWeaponAttaque)?.ShotCounter}, before={_shotBefore}"));
                input.Key(ReadyKey, true);
                Next(session);
                break;
            case 15 when elapsed >= 5:
                input.Key(ReadyKey, false);
                if (elapsed < 40 || pawn.WeaponLoaded != 0 || !TestRange.Walking(pc)) break;
                TestRange.Shot(_root, TestName, session.TicksRun / 60.0, "holstered");
                TestRange.Station terminal = TestRange.Stations()["Terminal"];
                TestRange.Place(session, _ => session.ServerController, terminal.Spot, terminal.Yaw);
                TestRange.Face(pc, terminal.Yaw);
                Next(session);
                break;
            case 16 when elapsed >= 35:
                if (pc.IManager?.HasUsableInteractions() != true) break;
                input.Key(UseKey, true);
                Next(session);
                break;
            case 17 when elapsed >= 12:
                input.Key(UseKey, false);
                Next(session);
                break;
            case 18 when elapsed >= 35:
                if (!pc.IsInState("s_Computer_Use")) break;
                TestRange.Shot(_root, TestName, session.TicksRun / 60.0, "terminal work");
                input.Key(BackKey, true);
                Next(session);
                break;
            case 19 when elapsed >= 12:
                input.Key(BackKey, false);
                if (!TestRange.Walking(pc)) break;
                Next(session);
                break;
            case 20 when elapsed >= 30:
                input.Key(BinocularKey, true);
                input.Key(BinocularKey, false);
                Next(session);
                break;
            case 21 when elapsed >= 90:
                if (!pawn.bBinocularsOn || rig.Shown)
                    return Finish(SmokeResult.Fail("the Spy rig did not yield to binoculars"));
                input.Key(BinocularKey, true);
                input.Key(BinocularKey, false);
                Next(session);
                break;
            case 22 when elapsed >= 70:
                if (pawn.bBinocularsOn || !pc.SpyFirstPersonActive)
                    return Finish(SmokeResult.Fail("the Spy eye view did not resume after binoculars"));
                input.Key(PerspectiveKey, false);
                Next(session);
                break;
            case 23 when elapsed >= 20:
                return Pass();
        }
        return SmokeResult.Running;
    }

    private SmokeResult Pass()
    {
        if (_standingFrames <= 10 || _thirdFrames <= 10 || _crouchedFrames <= 10 || _armedFrames <= 10)
            return Finish(SmokeResult.Fail("not enough rendered perspective samples"));
        string details = !_expectRig ? "old-pack fallback" : _missingPistol
            ? "steady arms and P190 while the weapon actor is missing, terminal cancellation, binocular handoff"
            : "steady P190 and grip at the wall and through firing, matching laser target, terminal cancellation, binocular handoff";
        return Finish(SmokeResult.Pass($"toggle and hold restore the right camera and body; {_firstFrames} safe eye frames; {details}"));
    }

    private static Transform3D CameraRelative(SkeletalModel model, FVector eye, FRotator rotation) =>
        UeSpace.TransformToGodot(eye, rotation).AffineInverse() * model.Root.GlobalTransform;

    private static bool SamePose(Transform3D a, Transform3D b) =>
        a.Origin.DistanceSquaredTo(b.Origin) < 0.00000001f && a.Basis.IsEqualApprox(b.Basis);

    private static bool InForeground(SkeletalModel model)
    {
        foreach (MeshInstance3D mesh in model.Meshes)
            if (mesh.Layers != RenderLayers.FirstPerson || mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off)
                return false;
        return true;
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
        if (_oldScopeBehavior is not null) SettingsStore.Current.SpyKbm.ScopeBehavior = _oldScopeBehavior;
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
