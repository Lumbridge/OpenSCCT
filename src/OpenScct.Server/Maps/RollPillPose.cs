using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScct.Physics.Movement;

namespace OpenScct.Server;

/// <summary>
/// Collision pills follow installed spy and merc animation channels on drawing and dedicated machines alike.
/// The historical type name is retained for callers. Missing clips use the reference pose; sampled channels,
/// transitions, mirrors and root locks use the same content and pose rules as the visible characters.
/// </summary>
public sealed class RollPillPose : IPillPose
{
    private readonly RollPoseContent? _spyContent;
    private readonly RollPoseContent? _mercContent;
    private readonly ConditionalWeakTable<SBasePawn, PawnPoseHistory> _histories = new();

    private sealed class PawnPoseHistory
    {
        public RollPoseContent.PoseHistory Pose = new();
        public readonly RollPoseContent.PoseHistory RootPose = new();
        public readonly Dictionary<string, Matrix4x4> Bones = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Matrix4x4> FirstBones = new(StringComparer.Ordinal);
        public readonly RollPoseContent.Layer[] Layers = new RollPoseContent.Layer[PawnAnimation.ChannelCount];
        public readonly RollPoseContent.Layer[] RootLayer = new RollPoseContent.Layer[1];
        public PawnPoseHistory? Query;
        public readonly List<PillShape> Scratch = new(15);
        public float Time = -1f;
        public uint ResetId;
        public bool RootLocked;
        public Matrix4x4 LockedRoot;

        public PawnPoseHistory QueryCopy()
        {
            // Trace evaluation owns separate reusable buffers; neither samples nor root corrections may
            // overwrite the simulation's tween history. Nothing mutable is shared between pawn histories.
            PawnPoseHistory copy = Query ??= new();
            Pose.CopyTo(copy.Pose);
            copy.Time = Time; copy.ResetId = ResetId;
            copy.RootLocked = RootLocked; copy.LockedRoot = LockedRoot;
            return copy;
        }
    }

    public RollPillPose(RollPoseContent? content, RollPoseContent? mercContent = null)
    {
        if (content is null && mercContent is null) throw new ArgumentNullException(nameof(content));
        _spyContent = content;
        _mercContent = mercContent;
    }

    public static IPillPose FromContent(string? characters)
    {
        if (characters is null) return EstimatedPillPose.Instance;
        // The two skeletons are separate files read into separate objects: the spy's (the larger) is read on a worker
        // meanwhile. A failure of the spy's file is still the one reported when both fail.
        Task<RollPoseContent?> spyRead = Task.Run(() => RollPoseContent.LoadShared(Path.Combine(characters, "SPerso", "ATT_01.glb"), allClips: true));
        RollPoseContent? merc;
        try
        {
            merc = RollPoseContent.LoadShared(Path.Combine(characters, "SPerso", "DEF_01.glb"), allClips: true);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _ = spyRead.GetAwaiter().GetResult();
            throw;
        }

        RollPoseContent? spy = spyRead.GetAwaiter().GetResult();
        return spy is null && merc is null ? EstimatedPillPose.Instance : new RollPillPose(spy, merc);
    }

    public void GetPills(SBasePawn Pawn, List<PillShape> Shapes)
        => GetPills(Pawn, Shapes, _histories.GetOrCreateValue(Pawn).QueryCopy());

    public static IPillPose FromContentWithAnimations(OpenScct.Animation.AnimationProject project, IPillPose? fallback)
    {
        if (fallback is not RollPillPose source) return fallback ?? EstimatedPillPose.Instance;
        RollPoseContent? spy = source._spyContent?.WithAnimations(project, "SPerso.ATT_01");
        RollPoseContent? merc = source._mercContent?.WithAnimations(project, "SPerso.DEF_01");
        return spy is null && merc is null ? fallback : new RollPillPose(spy, merc);
    }

    public void UpdatePose(SBasePawn Pawn)
    {
        if (Pawn.XLevel.World.IsReplaying) return; // Prediction replays must not replace the last presented history.
        PawnPoseHistory history = _histories.GetOrCreateValue(Pawn);
        history.Scratch.Clear();
        GetPills(Pawn, history.Scratch, history);
    }

    private void GetPills(SBasePawn Pawn, List<PillShape> Shapes, PawnPoseHistory history)
    {
        RollPoseContent? content = Pawn is SPawnAttaque ? _spyContent : Pawn is SPAWNDEFENSE ? _mercContent : null;
        if (content is null)
        {
            EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
            return;
        }

        Dictionary<string, Matrix4x4> bones = history.Bones;
        bool rootLocked = Pawn.Physics == EPhysics.PHYS_RootMotion && Pawn.iLockRootMotionType != 0;
        if (Pawn.Level.TimeSeconds < history.Time || Pawn.NetworkResetId != history.ResetId)
        {
            history.Pose.Reset();
            history.RootLocked = false;
        }
        if (history.RootLocked && !rootLocked) content.CorrectHistoryRoot(history.Pose, history.LockedRoot);
        history.Time = Pawn.Level.TimeSeconds;
        history.ResetId = Pawn.NetworkResetId;
        history.RootLocked = rootLocked;
        if (!TrySample(Pawn, content, bones, history))
        {
            history.Pose.Reset();
            history.RootLocked = false;
            EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
            return;
        }

        // Keep the entire result on the fallback if a partial/corrupt skeleton lacks a collision bone.
        foreach (CollisionPoint point in Points)
        {
            if (!bones.ContainsKey(point.Bone))
            {
                EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
                return;
            }
        }

        content.RotOrigin.GetAxes(out FVector mx, out FVector my, out FVector mz);
        new FRotator(0, Pawn.Rotation.Yaw, 0).GetAxes(out FVector px, out FVector py, out FVector pz);
        FVector MeshPoint(Vector3 local)
        {
            FVector mesh = mx * (local.X * 100f) + my * (local.Z * 100f) + mz * (local.Y * 100f);
            return new FVector(mesh.X * content.MeshScale.X, mesh.Y * content.MeshScale.Y, mesh.Z * content.MeshScale.Z);
        }

        FVector rootOffset = default;
        int rootTurn = 0;
        PawnAnimChannel baseChannel = Pawn.PawnAnim[0];
        if (rootLocked)
        {
            PawnRootMotion? mesh = ((ISBasePawnPhysics)Pawn).RootMotionMesh as PawnRootMotion;
            int lockType = mesh is { RootLockType: not 0 } ? mesh.RootLockType : Pawn.iLockRootMotionType;
            float firstFrame = baseChannel.bBackward ? baseChannel.AnimLast : 0f;
            FVector locked = mesh is { RootLockType: not 0 } ? mesh.LockedRoot : baseChannel.Clip?.RootAt(firstFrame) ?? default;
            if (!RootLockPose.Offset(baseChannel.Clip, baseChannel.ClipFrame, baseChannel.bMirror, locked, lockType,
                baseChannel.bBackward, baseChannel.bLoop, out rootOffset))
            {
                // Content-only fixtures or clips without a root track pin the visible skeleton root.
                Dictionary<string, Matrix4x4> first = history.FirstBones;
                if (baseChannel.Clip is not { Fps: > 0 } clip)
                {
                    EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
                    return;
                }
                history.RootLayer[0] = new RollPoseContent.Layer(baseChannel.Sequence.ToString(), firstFrame * baseChannel.Frames / clip.Fps, 1f, Mirror: baseChannel.bMirror);
                if (!content.TrySampleLayers(history.RootLayer, first, history.RootPose) || !first.TryGetValue("B", out Matrix4x4 atLock) || !bones.TryGetValue("B", out Matrix4x4 now))
                {
                    EstimatedPillPose.Instance.GetPills(Pawn, Shapes);
                    return;
                }
                rootOffset = MeshPoint(atLock.Translation - now.Translation);
            }
            if ((lockType & 2) != 0 && mesh is not null) rootTurn = -mesh.CarriedRootTurn(baseChannel, baseChannel.ClipFrame);
        }
        new FRotator(0, rootTurn, 0).GetAxes(out FVector rx, out FVector ry, out FVector rz);
        Matrix4x4 toPawn = Axes(MeshPoint(Vector3.UnitX), MeshPoint(Vector3.UnitY), MeshPoint(Vector3.UnitZ));
        Matrix4x4 rootCorrection = Matrix4x4.CreateTranslation(ToVector(rootOffset)) * Axes(rx, ry, rz);
        if (rootLocked && bones.TryGetValue("B", out Matrix4x4 root) && Matrix4x4.Invert(toPawn, out Matrix4x4 fromPawn))
            history.LockedRoot = root * toPawn * rootCorrection * fromPawn;
        Matrix4x4 toWorld = toPawn * rootCorrection * Axes(px, py, pz) * Matrix4x4.CreateTranslation(ToVector(Pawn.Location + Pawn.PrePivot));
        foreach (string bone in content.NamedBones) bones[bone] *= toWorld;
        MeshAimIK aim = Pawn.PawnAnim.Aim;
        if (aim.FullYaw != 0f || aim.FullPitch != 0f)
        {
            FVector Rotate(FVector value) => Turn(pz, aim.FullYaw, Turn(py, aim.FullPitch, value));
            RotateSubtree(content, bones, MeshAimIK.FullAimBoneName,
                Axes(Rotate(new FVector(1f, 0f, 0f)), Rotate(new FVector(0f, 1f, 0f)), Rotate(new FVector(0f, 0f, 1f))));
        }
        // Bone controls are absolute world rotations, processed parent first like the character renderer.
        Matrix4x4 boneCoordinates = Axes(new FVector(1f, 0f, 0f), new FVector(0f, 0f, 1f), new FVector(0f, 1f, 0f));
        IReadOnlyList<BoneDirection> directions = Pawn.PawnAnim.Directions.All;
        foreach (string bone in content.NamedBones)
        {
            for (int d = 0; d < directions.Count; d++)
            {
                BoneDirection direction = directions[d];
                if (direction.Bone.ToString() != bone || !bones.TryGetValue(bone, out Matrix4x4 current) ||
                    !Matrix4x4.Decompose(boneCoordinates * current, out _, out Quaternion rotation, out _)) continue;
                direction.Rotation.GetAxes(out FVector dx, out FVector dy, out FVector dz);
                Quaternion desired = Quaternion.CreateFromRotationMatrix(Axes(dx, dy, dz));
                Quaternion blended = Quaternion.Normalize(Quaternion.Slerp(rotation, desired, direction.Alpha));
                Matrix4x4 correction = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(rotation)) * Matrix4x4.CreateFromQuaternion(blended);
                RotateSubtree(content, bones, bone, correction);
            }
        }
        Span<FVector> points = stackalloc FVector[Points.Length];
        for (int i = 0; i < Points.Length; i++)
        {
            CollisionPoint point = Points[i];
            FVector offset = point.Offset;
            Vector3 local = Vector3.Transform(new Vector3(offset.X, offset.Z, offset.Y) / 100f, bones[point.Bone]);
            points[i] = new FVector(local.X, local.Y, local.Z);
        }

        for (int i = 0; i < Endpoints.Length; i++)
        {
            (int a, int b) = Endpoints[i];
            PillTable.Row row = PillTable.Rows[i];
            Shapes.Add(new PillShape(row.Pill, points[a], points[b], row.Radius, PillTable.Priority, row.IsSphere));
        }
    }

    private static Vector3 ToVector(FVector value) => new(value.X, value.Y, value.Z);

    private static Matrix4x4 Axes(FVector x, FVector y, FVector z) => new(
        x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);

    private static void RotateSubtree(RollPoseContent content, Dictionary<string, Matrix4x4> bones, string root, Matrix4x4 rotation)
    {
        if (!bones.TryGetValue(root, out Matrix4x4 transform)) return;
        Matrix4x4 correction = Matrix4x4.CreateTranslation(-transform.Translation) * rotation * Matrix4x4.CreateTranslation(transform.Translation);
        foreach (string bone in content.NamedBones)
            if (content.IsWithin(bone, root)) bones[bone] *= correction;
    }

    private static FVector Turn(FVector axis, float angle, FVector value)
    {
        float c = SvmMath.Cos(angle), s = SvmMath.Sin(angle), t = 1f - c;
        // The native aim's coords use the negative right-hand turn (same convention as the visible aim).
        return value * c + axis * ((axis | value) * t) - (axis ^ value) * s;
    }

    private static bool TrySample(SBasePawn pawn, RollPoseContent content, Dictionary<string, Matrix4x4> bones, PawnPoseHistory history)
    {
        RollPoseContent.Layer[] layers = history.Layers;
        int count = 0;
        int first = 0;
        // A full-body overlay replaces all lower channels, including unsupported clips no longer contributing.
        for (int i = 1; i < PawnAnimation.ChannelCount; i++)
            if (pawn.PawnAnim[i] is { Clip: not null, BlendAlpha: >= 1f, Frame: >= 0f, StartBone.IsNone: true }) first = i;
        for (int i = first; i < PawnAnimation.ChannelCount; i++)
        {
            PawnAnimChannel channel = pawn.PawnAnim[i];
            float alpha = i == 0 ? 1f : channel.BlendAlpha;
            if (alpha <= 0f || channel.Sequence.IsNone || channel.Clip is not { Fps: > 0 } clip) continue;
            layers[count++] = new RollPoseContent.Layer(channel.Sequence.ToString(), channel.ClipFrame * channel.Frames / clip.Fps,
                alpha, i == 0 || channel.StartBone.IsNone ? null : channel.StartBone.ToString(), channel.bLoop, clip.Fps, channel.bMirror,
                i, channel.Frame, channel.Frames, channel.bGlobalPose, pawn.BoneMirrors);
        }
        return content.TrySampleLayers(layers.AsSpan(0, count), bones, history.Pose);
    }

    private readonly record struct CollisionPoint(string Bone, FVector Offset);

    // Bone-space collision offsets, independent of animation tracks. spec/native/hit-zones.md; decoded by
    // re/ghidra/scripts/actions/pills.py (local/re-out/c/actions/pills-SPerso.json, ATT_01).
    [Provenance(ProvenanceKind.Decoded, "ATT_01's 26 collision points; spec/native/hit-zones.md")]
    private static readonly CollisionPoint[] Points =
    [
        new("B Head", new(4.172f, -2.376f, 0f)), new("B Head", new(11.519f, -0.428f, 0f)),
        new("B Spine2", new(15.82f, -2.171f, 6.21f)), new("B Pelvis", new(2.591f, -2.42f, 4.759f)),
        new("B L UpperArm", new(0.503f, 1.035f, 0.881f)), new("B L ForeArm", new(0.345f, 1.431f, -1.673f)),
        new("B L ForeArm", new(17.537f, 1.051f, -1.409f)), new("B L Hand", new(4.393f, -0.614f, -1.436f)),
        new("B L Thigh", new(3.65f, -1.73f, 0.08f)), new("B L Calf", new(-0.205f, -2.376f, -1.444f)),
        new("B L Calf", new(5.693f, -0.653f, -1.457f)), new("B L Foot", new(-4.25f, -1.127f, -0.066f)),
        new("B L Foot", new(5.318f, 0.159f, -0.153f)), new("B L Toe0", new(5.208f, -3.884f, -0.137f)),
        new("B Spine2", new(15.82f, -2.171f, -6.21f)), new("B Pelvis", new(2.591f, -2.42f, -4.759f)),
        new("B R UpperArm", new(0.503f, 1.035f, -0.881f)), new("B R ForeArm", new(0.345f, 1.431f, 1.673f)),
        new("B R ForeArm", new(17.537f, 1.051f, 1.409f)), new("B R Hand", new(4.393f, -0.614f, 1.436f)),
        new("B R Thigh", new(3.65f, -1.73f, -0.08f)), new("B R Calf", new(-0.205f, -2.376f, 1.444f)),
        new("B R Calf", new(5.693f, -0.653f, 1.457f)), new("B R Foot", new(-4.25f, -1.127f, 0.066f)),
        new("B R Foot", new(5.318f, 0.159f, 0.153f)), new("B R Toe0", new(5.208f, -3.884f, 0.137f)),
    ];

    private static readonly (int A, int B)[] Endpoints =
        [(0, 1), (2, 3), (4, 5), (5, 6), (7, 7), (8, 9), (10, 11), (12, 13),
         (14, 15), (16, 17), (17, 18), (19, 19), (20, 21), (22, 23), (24, 25)];
}
