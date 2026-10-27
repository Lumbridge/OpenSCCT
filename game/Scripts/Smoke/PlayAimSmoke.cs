using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Animation;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-aim</c>: the upper body follows the aim (spec/native/aim-ik.md). SteeE as the spy with three
/// bots, as the launchers play it. The spy at the keys draws the Shocker (E), looks up, holds, looks down, holds.
/// The drawn aim is the elevation of the right hand seen from B Spine2 (the bone the aim turns); the view's pitch is
/// the controller's. Passes when the drawn elevation looking up is above the one looking down by at least half of the
/// share of the pitch change the aim takes (0.85), so the arms and pistol follow the view. The merc bots' drawn
/// elevation against the pitch they aim at (their iDesiredYawPitch) is reported.
/// </summary>
public sealed class PlayAimSmoke : PlayPoseSmoke
{
    public const string TestName = "play-aim";

    private const string UpStep = "hold up";
    private const string DownStep = "hold down";

    private readonly List<(float Pitch, float Elevation)> _up = [];
    private readonly List<(float Pitch, float Elevation)> _down = [];
    private readonly Dictionary<string, (int Samples, double PitchSum, double ElevSum, double ErrorSum, double AbsPitchSum)> _bots = new(StringComparer.Ordinal);

    public override string Name => TestName;

    protected override int Bots => 3;

    protected override List<KeyStep> Timeline(SBasePawn local) =>
    [
        new KeyStep("settle", 1.0),
        new KeyStep("draw", 2.0) { Tap = ["IK_E"] },
        new KeyStep("look up", 1.0) { LookPerSecond = new Vector2(0f, -260f) },
        new KeyStep(UpStep, 2.0),
        new KeyStep("look down", 2.0) { LookPerSecond = new Vector2(0f, 260f) },
        new KeyStep(DownStep, 2.0),
        new KeyStep("level", 1.0) { LookPerSecond = new Vector2(0f, -260f) },
        new KeyStep("watch the bots", 10.0),
    ];

    protected override void OnFrame(PlaySession session, string step)
    {
        if (Root?.Pawns is not { } pawns)
        {
            return;
        }

        SBasePawn? local = session.LocalPawn;
        foreach (SimPawnView view in pawns.Views)
        {
            if (view.Pawn is not { } pawn || pawn.bDeleteMe || pawn.Health <= 0 || !Elevation(view, out float elevation))
            {
                continue;
            }

            if (ReferenceEquals(pawn, local))
            {
                if (step is UpStep or DownStep && session.LocalController is { } pc && pawn.WeaponLoaded != 0)
                {
                    float pitch = Degrees(pc.Rotation.Pitch);
                    (step == UpStep ? _up : _down).Add((pitch, elevation));
                }

                continue;
            }

            // Yaw: where the hand points sideways against where the aim points, both from the pawn's facing.
            MeshAimIK aim = pawn.PawnAnim.Aim;
            if (pawn.WeaponLoaded != 0 && MathF.Abs(aim.FullYaw) > 0.15f && HandYaw(view, pawn, out float handYaw))
            {
                // FullYaw is negative for an aim to the right (UE yaw +); the drawn hand turns the same way.
                bool agree = MathF.Sign(handYaw - _restHandYaw.GetValueOrDefault(pawn.ActorId)) == MathF.Sign(-aim.FullYaw);
                _yawSamples++;
                _yawAgree += agree ? 1 : 0;
            }
            else if (pawn.WeaponLoaded != 0 && MathF.Abs(aim.FullYaw) < 0.02f && HandYaw(view, pawn, out float rest))
            {
                _restHandYaw[pawn.ActorId] = rest;
            }

            if (pawn is SPAWNDEFENSE && pawn.WeaponLoaded != 0)
            {
                float aimPitch = Degrees(pawn.iDesiredYawPitch >> 16);
                string key = $"{pawn.PlayerReplicationInfo?.PlayerName ?? "merc"}#{pawn.ActorId}";
                _bots.TryGetValue(key, out var t);
                _bots[key] = (t.Samples + 1, t.PitchSum + aimPitch, t.ElevSum + elevation, t.ErrorSum + Math.Abs(elevation - aimPitch), t.AbsPitchSum + Math.Abs(aimPitch));
            }
        }
    }

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        foreach ((string bot, var t) in _bots)
        {
            GD.Print(string.Create(CultureInfo.InvariantCulture,
                $"SVM_AIM bot {bot}: samples {t.Samples}, mean aim pitch {t.PitchSum / Math.Max(1, t.Samples):F1}, mean drawn elevation {t.ElevSum / Math.Max(1, t.Samples):F1}, mean |elevation - pitch| {t.ErrorSum / Math.Max(1, t.Samples):F1} (mean |pitch| {t.AbsPitchSum / Math.Max(1, t.Samples):F1})"));
        }

        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_AIM yaw: the drawn hand turned the way of the aim in {_yawAgree} of {_yawSamples} frames with a sideways aim over 8.6 degrees"));
        if (_yawSamples >= 30 && _yawAgree < _yawSamples * 7 / 10)
        {
            return SmokeResult.Fail($"the drawn hand turned against the sideways aim in {_yawSamples - _yawAgree} of {_yawSamples} frames");
        }

        if (_up.Count < 10 || _down.Count < 10)
        {
            return SmokeResult.Fail($"the spy was not drawn aiming in both holds (up {_up.Count}, down {_down.Count} frames)");
        }

        (float upPitch, float upElev) = Mean(_up);
        (float downPitch, float downElev) = Mean(_down);
        float pitchChange = upPitch - downPitch;
        float elevChange = upElev - downElev;
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"view pitch up {upPitch:F1} down {downPitch:F1} deg; drawn hand elevation up {upElev:F1} down {downElev:F1} deg (change {elevChange:F1} for a view change of {pitchChange:F1})");
        GD.Print("SVM_AIM spy " + detail);
        if (pitchChange < 20f)
        {
            return SmokeResult.Fail("the view did not pitch enough to judge: " + detail);
        }

        return elevChange >= 0.5f * MeshAimIK.FullAimShare * pitchChange
            ? SmokeResult.Pass(detail)
            : SmokeResult.Fail("the drawn arms do not follow the aim: " + detail);
    }

    private readonly Dictionary<int, float> _restHandYaw = new();
    private int _yawSamples;
    private int _yawAgree;

    /// <summary>The right hand's direction from B Spine2 in the horizontal plane against the pawn's facing, degrees (right positive).</summary>
    private static bool HandYaw(SimPawnView view, SBasePawn pawn, out float degrees)
    {
        degrees = 0f;
        if (!view.TryGetBone(new Name(MeshAimIK.FullAimBoneName), out Transform3D spine) || !view.TryGetBone(new Name("B R Hand"), out Transform3D hand))
        {
            return false;
        }

        FVector d = OpenScctGame.UeSpace.DirectionToUe(hand.Origin - spine.Origin);
        float yaw = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
        float facing = pawn.Rotation.Yaw * 360f / 65536f;
        degrees = (((yaw - facing) % 360f) + 540f) % 360f - 180f;
        return true;
    }

    /// <summary>The elevation in degrees of the right hand seen from B Spine2, as drawn.</summary>
    private static bool Elevation(SimPawnView view, out float degrees)
    {
        degrees = 0f;
        if (!view.TryGetBone(new Name(MeshAimIK.FullAimBoneName), out Transform3D spine) || !view.TryGetBone(new Name("B R Hand"), out Transform3D hand))
        {
            return false;
        }

        Vector3 d = hand.Origin - spine.Origin;
        float horizontal = new Vector2(d.X, d.Z).Length();
        if (horizontal < 1e-4f)
        {
            return false;
        }

        degrees = Mathf.RadToDeg(MathF.Atan2(d.Y, horizontal));
        return true;
    }

    private static float Degrees(int ueAngle)
    {
        int a = ueAngle & 0xffff;
        if (a >= 32768)
        {
            a -= 65536;
        }

        return a * 360f / 65536f;
    }

    private static (float, float) Mean(List<(float A, float B)> list)
    {
        double a = 0, b = 0;
        foreach ((float x, float y) in list)
        {
            a += x;
            b += y;
        }

        return ((float)(a / list.Count), (float)(b / list.Count));
    }
}
