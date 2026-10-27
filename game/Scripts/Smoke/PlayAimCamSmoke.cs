using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-aimcam</c>: the spy's aiming camera (SecondPerson, spec/native/camera.md mode 4) stays out of
/// the spy's head. SteeE as the spy with three bots. The spy at the keys looks through the binoculars and puts them away
/// (B; the view goes from the eyes to the following camera in one frame), selects the sticky cam launcher (3), draws it
/// (E), holds, looks round, strafes left and right while turning and looks up and down. Every frame while armed, the
/// drawn camera (what is on the screen) is measured against the drawn head bone (B Head) and the drawn goggle glow of
/// the own spy. Passes when the camera never comes nearer the head than <see cref="NearestHead"/> and the own glow is
/// never in front of the camera nearer than <see cref="NearestGlow"/>: the original's over-the-shoulder view keeps the
/// head 30 uu or more away and shows no glow from behind (local/goggle-glow/game/spy-back-5.png).
/// </summary>
public sealed class PlayAimCamSmoke : PlayPoseSmoke
{
    public const string TestName = "play-aimcam";

    /// <summary>The nearest the drawn camera may come to the drawn head bone while aiming (uu).</summary>
    public const float NearestHead = 20f;

    /// <summary>The nearest the own goggle glow may be drawn in front of the camera (uu).</summary>
    public const float NearestGlow = 20f;

    private int _armedFrames;
    private float _minHead = float.MaxValue;
    private string _minHeadAt = "";
    private float _minGlow = float.MaxValue;
    private string _minGlowAt = "";
    private int _glowNearFrames;
    private float _maxYawGap;

    public override string Name => TestName;

    protected override int Bots => 3;

    protected override List<KeyStep> Timeline(SBasePawn local)
    {
        var steps = new List<KeyStep>();
        if (local.bIsCrouched)
        {
            steps.Add(new KeyStep("stand up", 1.2) { Tap = ["bCrouch"] });
        }

        steps.AddRange(
        [
            new KeyStep("binoc in", 1.5) { Tap = ["IK_B"] },
            new KeyStep("binoc look", 1.0) { LookPerSecond = new Vector2(250f, 0f) },
            new KeyStep("binoc out", 1.5) { Tap = ["IK_B"] },
            new KeyStep("select sticky", 0.6) { Tap = ["IK_3"] },
            new KeyStep("draw", 1.5) { Tap = ["IK_E"] },
            new KeyStep("hold", 1.0),
            new KeyStep("look right", 1.5) { LookPerSecond = new Vector2(300f, 0f) },
            new KeyStep("look left", 1.5) { LookPerSecond = new Vector2(-300f, 0f) },
            new KeyStep("strafe look", 2.0) { Hold = ["BT_Left"], LookPerSecond = new Vector2(200f, 0f) },
            new KeyStep("strafe right", 2.0) { Hold = ["BT_Right"], LookPerSecond = new Vector2(-200f, 0f) },
            new KeyStep("walk", 1.5) { Hold = ["BT_Up"] },
            new KeyStep("look up", 1.0) { LookPerSecond = new Vector2(0f, -200f) },
            new KeyStep("look down", 1.5) { LookPerSecond = new Vector2(0f, 200f) },
            new KeyStep("level", 1.0) { LookPerSecond = new Vector2(0f, -100f) },
            new KeyStep("crouch", 1.5) { Tap = ["bCrouch"] },
            new KeyStep("crouch look", 1.5) { LookPerSecond = new Vector2(300f, 0f) },
        ]);
        return steps;
    }

    protected override void OnFrame(PlaySession session, string step)
    {
        CheckAxis(session, step);
        if (Root?.Camera is not { } camera || Root.Pawns is not { } pawns || session.LocalPawn is not { } pawn ||
            session.LocalController is not { } pc || pawn.WeaponLoaded != 2 || !pc.bBehindView || step.Length == 0 ||
            step is "select sticky" or "draw" or "stand up")
        {
            return;
        }

        if (pawns.ViewOf(pawn) is not SimPawnView view || !view.TryGetBone(new Name("B Head"), out Transform3D head))
        {
            return;
        }

        _armedFrames++;
        (FVector eye, FRotator rotation, float _) = camera.Current;
        FVector headUe = UeSpace.ToUe(head.Origin);
        float toHead = (headUe - eye).Size();
        int yawGap = Math.Abs(FRotator.NormalizeAxis(pawn.Rotation.Yaw - pc.Rotation.Yaw));
        _maxYawGap = Math.Max(_maxYawGap, yawGap * 360f / 65536f);
        string where = string.Create(CultureInfo.InvariantCulture,
            $"step '{step}', {pc.GetStateName()}, camera mode {pc.m_camera?.m_camMode}, distance {pc.m_camera?.m_prevDistance:F0}, pawn yaw - view yaw {FRotator.NormalizeAxis(pawn.Rotation.Yaw - pc.Rotation.Yaw)}, camera {eye}, head {headUe}");
        if (toHead < _minHead)
        {
            _minHead = toHead;
            _minHeadAt = where;
        }

        if (view.GoggleGlow is { Shown: true, Model: { } glow })
        {
            FVector glowUe = UeSpace.ToUe(glow.Root.GlobalPosition);
            float ahead = (glowUe - eye) | rotation.Vector();
            float dist = (glowUe - eye).Size();
            if (ahead > 0f && dist < _minGlow)
            {
                _minGlow = dist;
                _minGlowAt = where;
            }

            if (ahead > 0f && dist < NearestGlow)
            {
                _glowNearFrames++;
            }
        }
    }

    /// <summary>
    /// Every frame of the behind view (the binoculars put away included): the drawn camera's distance from the pawn's
    /// axis, as the play inspector measures it; nearer than <see cref="PlayInspector.InspectCameraNearest"/> at head
    /// height is a frame drawn from inside the spy.
    /// </summary>
    private void CheckAxis(PlaySession session, string step)
    {
        if (Root?.Camera is not { } camera || session.LocalPawn is not { } pawn || session.LocalController is not { bBehindView: true } pc ||
            pc.bFirstPersonWithPawn || step.Length == 0 || !pc.IsInState("s_PlayerWalkingSt0H") && !pc.IsInState("s_PlayerWalkingSt2H"))
        {
            return;
        }

        FVector rel = camera.Current.Location - pawn.Location;
        float horizontal = MathF.Sqrt((rel.X * rel.X) + (rel.Y * rel.Y));
        if (horizontal < PlayInspector.InspectCameraNearest && Math.Abs(rel.Z) < pawn.CollisionHeight)
        {
            _insideFrames++;
            _insideAt = string.Create(CultureInfo.InvariantCulture, $"{horizontal:F0} uu from the axis in step '{step}'");
        }
    }

    private int _insideFrames;
    private string _insideAt = "";

    protected override SmokeResult Verdict(DrawnPoseTrack? local, IReadOnlyList<DrawnPoseTrack> others)
    {
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"{_armedFrames} armed frames; nearest camera to the drawn head {_minHead:F1} uu ({_minHeadAt}); nearest own glow ahead {_minGlow:F1} uu ({_minGlowAt}); glow nearer than {NearestGlow:F0} uu in {_glowNearFrames} frames; behind view inside the spy in {_insideFrames} frames {_insideAt}; largest pawn to view yaw gap {_maxYawGap:F0} deg");
        GD.Print("SVM_AIMCAM " + detail);
        if (_armedFrames < 60)
        {
            return SmokeResult.Fail("the spy was not drawn aiming: " + detail);
        }

        if (_insideFrames > 0)
        {
            return SmokeResult.Fail("the behind view was drawn from inside the spy: " + detail);
        }

        if (_minHead < NearestHead)
        {
            return SmokeResult.Fail("the aiming camera went into the spy's head: " + detail);
        }

        return _glowNearFrames > 0
            ? SmokeResult.Fail("the spy's own goggle glow was drawn in front of the camera: " + detail)
            : SmokeResult.Pass(detail);
    }
}
