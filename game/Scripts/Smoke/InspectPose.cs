using System;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Render.Characters;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// The animation and feet checks of one drawn pawn (<see cref="SimPawnView"/>): over windows of
/// <see cref="PlayInspector.InspectWindowSeconds"/>, whether the drawn bones move relative to the skeleton while the
/// pawn moves, whether channel 0 is stuck, which channel sequences have no clip, and where the lowest drawn bone is
/// against the floor under the pawn.
/// </summary>
public sealed class InspectPose
{
    private Vector3[] _start = [];
    private Vector3[] _now = [];
    private FVector _startLocation;
    private Name _startSeq0;
    private float _startFrame0;
    private double _windowTime;
    private bool _windowOpen;
    private float _windowBoneMotion;
    private bool _windowChannelAdvanced;

    public InspectPose(SimPawnView view, string label)
    {
        View = view ?? throw new ArgumentNullException(nameof(view));
        Label = label;
    }

    public SimPawnView View { get; }

    /// <summary>A name for reports: the player name and the class.</summary>
    public string Label { get; }

    /// <summary>Windows in which the pawn moved.</summary>
    public int MovingWindows { get; private set; }

    /// <summary>Windows in which the pawn moved and its drawn bones moved too.</summary>
    public int AnimatedWindows { get; private set; }

    /// <summary>The largest bone movement relative to the skeleton seen in a window (uu).</summary>
    public float MaxBoneMotion { get; private set; }

    /// <summary>Lowest and highest foot offset seen (lowest drawn bone minus the floor, uu), or NaN.</summary>
    public float MinFoot { get; private set; } = float.NaN;

    public float MaxFoot { get; private set; } = float.NaN;
    public object? MinimumFootPose { get; private set; }
    public object? MaximumFootPose { get; private set; }
    public int LandingTransitionSamples { get; private set; }
    public float MaxLandingTransitionFoot { get; private set; } = float.NaN;
    public object? MaximumLandingTransitionPose { get; private set; }
    public int FootSamples { get; private set; }
    public int MissingFloorSamples { get; private set; }
    public int CylinderSupportSamples { get; private set; }
    public int UnsupportedSamples { get; private set; }
    public int NonWalkingSamples { get; private set; }
    public int MissingFootSamples { get; private set; }
    private double _unsupportedSeconds;

    /// <summary>One frame of checks; reports issues through <paramref name="inspector"/>.</summary>
    public void Frame(PlayInspector inspector, double dt, CollisionWorld? collision, InspectChecks checks)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        if (View.Pawn is not { } pawn || View.Model is not { } model || !View.Visible || pawn.bDeleteMe)
        {
            _windowOpen = false;
            return;
        }

        Skeleton3D skeleton = model.Skeleton;
        int count = skeleton.GetBoneCount();
        if (_now.Length != count)
        {
            _now = new Vector3[count];
            _start = new Vector3[count];
            _windowOpen = false;
        }

        Transform3D skeletonWorld = skeleton.GlobalTransform;
        float lowest = float.MaxValue;
        int lowestBone = -1;
        for (int b = 0; b < count; b++)
        {
            Vector3 local = skeleton.GetBoneGlobalPose(b).Origin;

            // Relative to the skeleton, in uu (Godot units are uu / 100): the skeleton's rotation and scale apply,
            // its placement does not.
            _now[b] = skeletonWorld.Basis * local * 100f;
            float z = (skeletonWorld * local).Y * 100f;
            if (z < lowest && IsFootBone(skeleton, b))
            {
                lowest = z;
                lowestBone = b;
            }
        }

        if ((checks & InspectChecks.Animation) != 0)
        {
            MissingClips(inspector, pawn, model);
            Window(inspector, pawn, dt);
        }

        if ((checks & InspectChecks.Feet) != 0 && collision is not null && lowestBone >= 0)
        {
            Feet(inspector, pawn, collision, lowest, skeleton.GetBoneName(lowestBone), dt);
        }
        else if ((checks & InspectChecks.Feet) != 0) MissingFootSamples++;
    }

    private void MissingClips(PlayInspector inspector, SBasePawn pawn, SkeletalModel model)
    {
        PawnAnimation anim = pawn.PawnAnim;
        for (int c = 0; c < PawnAnimation.ChannelCount; c++)
        {
            PawnAnimChannel ch = anim[c];
            if (ch.Sequence.IsNone || (c > 0 && !(ch.BlendAlpha > 0f)))
            {
                continue;
            }

            string seq = ch.Sequence.ToString();
            if (ch.Clip is null)
            {
                inspector.Note(InspectChecks.Animation, "no sim clip " + seq,
                    $"channel {c} plays {seq} but the simulation has no clip for it (nothing is drawn for that channel) on {Label}");
            }
            else if (model.Sampler.Find(seq) is null)
            {
                inspector.Note(InspectChecks.Animation, "no mesh clip " + seq,
                    $"channel {c} plays {seq} but the drawn mesh has no animation of that name (nothing is drawn for that channel) on {Label}");
            }
        }
    }

    private void Window(PlayInspector inspector, SBasePawn pawn, double dt)
    {
        if (!_windowOpen)
        {
            Array.Copy(_now, _start, _now.Length);
            _startLocation = pawn.Location;
            _startSeq0 = pawn.PawnAnim[0].Sequence;
            _startFrame0 = pawn.PawnAnim[0].Frame;
            _windowTime = 0.0;
            _windowBoneMotion = 0f;
            _windowChannelAdvanced = false;
            _windowOpen = true;
            return;
        }

        _windowTime += dt;
        // A looping walk can return to exactly its first pose at the window boundary. Measure every
        // drawn sample, not just the endpoints, or a complete gait cycle looks like frozen animation.
        for (int b = 0; b < _now.Length; b++)
            _windowBoneMotion = Math.Max(_windowBoneMotion, (_now[b] - _start[b]).Length());
        PawnAnimChannel c0 = pawn.PawnAnim[0];
        _windowChannelAdvanced |= c0.Sequence != _startSeq0 || c0.Frame != _startFrame0;
        if (_windowTime < PlayInspector.InspectWindowSeconds)
        {
            return;
        }

        _windowOpen = false;
        FVector moved = pawn.Location - _startLocation;
        float horizontal = MathF.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y));
        float speed = horizontal / (float)_windowTime;
        float bones = _windowBoneMotion;

        MaxBoneMotion = Math.Max(MaxBoneMotion, bones);
        if (speed < PlayInspector.InspectMovingSpeed)
        {
            return;
        }

        MovingWindows++;
        string stance = Stance(pawn);
        if (bones >= PlayInspector.InspectBoneMotion)
        {
            AnimatedWindows++;
        }
        else
        {
            inspector.Note(InspectChecks.Animation, "frozen " + Label,
                string.Create(CultureInfo.InvariantCulture,
                    $"{Label} moves at {speed:F0} uu/s ({stance}) but no drawn bone moved more than {bones:F2} uu in {_windowTime:F1} s (channel 0 {Seq(c0.Sequence)} frame {c0.Frame:F2})"),
                bones);
        }

        if (c0.Sequence.IsNone)
        {
            inspector.Note(InspectChecks.Animation, "no channel 0 " + Label,
                string.Create(CultureInfo.InvariantCulture, $"{Label} moves at {speed:F0} uu/s ({stance}) with nothing on channel 0"));
        }
        else if (!_windowChannelAdvanced && c0.Frames > 1)
        {
            inspector.Note(InspectChecks.Animation, "stuck channel 0 " + Label,
                string.Create(CultureInfo.InvariantCulture,
                    $"{Label} moves at {speed:F0} uu/s ({stance}) while channel 0 stays on {c0.Sequence} frame {c0.Frame:F3} (rate {c0.Rate:F3})"));
        }
    }

    private void Feet(PlayInspector inspector, SBasePawn pawn, CollisionWorld collision, float lowest, string bone, double dt)
    {
        if (pawn.Physics != EPhysics.PHYS_Walking)
        {
            NonWalkingSamples++;
            _unsupportedSeconds = 0;
            return;
        }

        FVector start = pawn.Location;
        FVector end = start - new FVector(0f, 0f, pawn.CollisionHeight + PlayInspector.InspectFloorSearch);
        if (!collision.LineCheck(out FCheckResult hit, pawn, end, start, default, PlayInspector.WorldTrace))
        {
            // A cylinder can stand with its centre over an edge. Use the same short cylinder
            // support sweep as movement before calling it unsupported; a centre ray alone misses it.
            FVector extent = new(pawn.CollisionRadius, pawn.CollisionRadius, pawn.CollisionHeight);
            if (collision.LineCheck(out FCheckResult support, pawn, start - new FVector(0, 0, 8f), start, extent, PlayInspector.WorldTrace)
                && support.Normal.Z >= 0.7f)
            {
                CylinderSupportSamples++;
                _unsupportedSeconds = 0;
                return; // Support is proven, but there is no surface directly under the measured feet.
            }
            MissingFloorSamples++;
            _unsupportedSeconds += dt;
            if (_unsupportedSeconds > 0.25)
                inspector.Note(InspectChecks.Feet, $"no support {Label}", $"Walking pawn {Label} has no floor below its collision shape.");
            return;
        }

        float floor = hit.Location.Z;

        // Only a floor the pawn stands on: its collision bottom is at the floor.
        if (Math.Abs(start.Z - pawn.CollisionHeight - floor) > PlayInspector.InspectFloorSearch * 0.5f)
        {
            UnsupportedSamples++;
            _unsupportedSeconds += dt;
            if (_unsupportedSeconds > 0.25)
                inspector.Note(InspectChecks.Feet, $"collision off floor {Label}",
                    $"Walking pawn {Label} collision bottom differs from floor by {start.Z - pawn.CollisionHeight - floor:F1} uu.",
                    Math.Abs(start.Z - pawn.CollisionHeight - floor));
            return;
        }

        _unsupportedSeconds = 0;
        float offset = lowest - floor;
        if (View.InterpolatingFromAirborne)
        {
            // Current physics has landed, but this draw still interpolates the previous airborne endpoint.
            // Retain that evidence separately; it is not a grounded foot/floor sample.
            LandingTransitionSamples++;
            if (float.IsNaN(MaxLandingTransitionFoot) || offset > MaxLandingTransitionFoot)
            {
                MaxLandingTransitionFoot = offset;
                MaximumLandingTransitionPose = FootPose(pawn, floor, offset, bone);
            }
            return;
        }
        FootSamples++;
        DumpBones(pawn, floor);
        // Preserve the exact animation sample behind an extreme, rather than just a cumulative number.
        // This runs only in the inspector and allocates only when a new extreme is observed.
        if (float.IsNaN(MinFoot) || offset < MinFoot) MinimumFootPose = FootPose(pawn, floor, offset, bone);
        if (float.IsNaN(MaxFoot) || offset > MaxFoot) MaximumFootPose = FootPose(pawn, floor, offset, bone);
        MinFoot = float.IsNaN(MinFoot) ? offset : Math.Min(MinFoot, offset);
        MaxFoot = float.IsNaN(MaxFoot) ? offset : Math.Max(MaxFoot, offset);
        string stance = Stance(pawn);
        if (offset < -PlayInspector.InspectFeetBelow)
        {
            inspector.Note(InspectChecks.Feet, $"under floor {Label} {stance}",
                string.Create(CultureInfo.InvariantCulture,
                    $"{Label} ({stance}, channel 0 {Seq(pawn.PawnAnim[0].Sequence)}): the lowest drawn foot bone ({bone}) is {-offset:F1} uu under the floor (pawn at {pawn.Location.Z:F1}, collision height {pawn.CollisionHeight:F1}, floor {floor:F1})"),
                -offset);
        }
        else if (offset > PlayInspector.InspectFeetAbove)
        {
            inspector.Note(InspectChecks.Feet, $"over floor {Label} {stance}",
                string.Create(CultureInfo.InvariantCulture,
                    $"{Label} ({stance}, channel 0 {Seq(pawn.PawnAnim[0].Sequence)}): the lowest drawn foot bone ({bone}) floats {offset:F1} uu over the floor (pawn at {pawn.Location.Z:F1}, collision height {pawn.CollisionHeight:F1}, floor {floor:F1})"),
                offset);
        }
    }

    private object FootPose(SBasePawn pawn, float floor, float offset, string bone)
    {
        var channels = new object[PawnAnimation.ChannelCount];
        for (int i = 0; i < channels.Length; i++)
        {
            PawnAnimChannel channel = pawn.PawnAnim[i];
            channels[i] = new { sequence = channel.Sequence.ToString(), channel.Frame, channel.Rate,
                alpha = channel.BlendAlpha, channel.TweenRate, channel.TweenWeight };
        }
        return new { tick = pawn.XLevel.TickCount, role = pawn.Role.ToString(),
            position = new[] { pawn.Location.X, pawn.Location.Y, pawn.Location.Z },
            pivot = new[] { pawn.PrePivot.X, pawn.PrePivot.Y, pawn.PrePivot.Z },
            pawn.CollisionHeight, drawnOrigin = View.Model!.Skeleton.GlobalTransform.Origin.Y * 100f,
            floor, offset, bone, pawn.bIsCrouched, View.InterpolatingFromAirborne, View.DrawAlpha, channels };
    }

    private bool[]? _feet;

    /// <summary>
    /// The bones the feet check measures: the biped foot and toe bones (UE2 "B L Foot", "B R Toe0"...). Attachment
    /// and helper bones that the animations do not pose (ATT_02_HEAD, HOLSTER) would read as under the floor.
    /// </summary>
    private bool IsFootBone(Skeleton3D skeleton, int bone)
    {
        if (_feet is null || _feet.Length != skeleton.GetBoneCount())
        {
            _feet = new bool[skeleton.GetBoneCount()];
            for (int b = 0; b < _feet.Length; b++)
            {
                string name = skeleton.GetBoneName(b);
                _feet[b] = name.Contains("Foot", StringComparison.OrdinalIgnoreCase) || name.Contains("Toe", StringComparison.OrdinalIgnoreCase);
            }
        }

        return _feet[bone];
    }

    private readonly System.Collections.Generic.HashSet<string> _dumped = [];

    /// <summary>With SVM_INSPECT_BONES=1, prints every drawn bone's height over the floor once per stance and channel 0 sequence.</summary>
    private void DumpBones(SBasePawn pawn, float floor)
    {
        if (System.Environment.GetEnvironmentVariable("SVM_INSPECT_BONES") != "1" || View.Model is not { } model)
        {
            return;
        }

        string key = Stance(pawn) + " " + Seq(pawn.PawnAnim[0].Sequence);
        if (!_dumped.Add(key))
        {
            return;
        }

        Skeleton3D skeleton = model.Skeleton;
        Transform3D world = skeleton.GlobalTransform;
        var sb = new System.Text.StringBuilder();
        for (int b = 0; b < skeleton.GetBoneCount(); b++)
        {
            Vector3 p = world * skeleton.GetBoneGlobalPose(b).Origin;
            sb.Append(string.Create(CultureInfo.InvariantCulture, $" {skeleton.GetBoneName(b)}={(p.Y * 100f) - floor:F0}"));
        }

        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"SVM_INSPECT bones {Label} {key} pawnZ-floor={pawn.Location.Z - floor:F1} skeletonY-floor={(world.Origin.Y * 100f) - floor:F1}:{sb}"));
    }

    private static string Seq(Name n) => n.IsNone ? "None" : n.ToString();

    /// <summary>Crouched or standing, and still or moving.</summary>
    public static string Stance(SBasePawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        FVector v = pawn.Velocity;
        bool moving = (v.X * v.X) + (v.Y * v.Y) > PlayInspector.InspectMovingSpeed * PlayInspector.InspectMovingSpeed;
        return (pawn.bIsCrouched ? "crouched" : "standing") + (moving ? " moving" : " still");
    }
}
