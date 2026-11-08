using System.Numerics;

namespace OpenScct.Animation;

/// <summary>Authoring constraints are evaluated in rig space and baked on export.</summary>
public static class RigSolver
{
    public static void Apply(SkeletonDefinition skeleton, BoneTransform[] pose, IEnumerable<RigConstraint> constraints, float time,
        Func<string, string?, Matrix4x4?>? externalTarget = null)
    {
        foreach (RigConstraint constraint in constraints)
        {
            float weight = Math.Clamp(constraint.Weight * (constraint.WeightKeys.Count > 0 ? ClipEvaluator.Sample(constraint.WeightKeys, time).X : 1), 0, 1);
            if (weight == 0) continue;
            int end = skeleton.Bones.FindIndex(b => b.Name == constraint.Bone);
            if (end < 0) continue;
            Vector3 target = constraint.Target;
            Matrix4x4 targetFrame = Matrix4x4.Identity;
            if (constraint.TargetRig is { Length: > 0 } rig)
            {
                if (externalTarget?.Invoke(rig, constraint.TargetBone) is not { } resolved) continue;
                targetFrame = resolved; target += resolved.Translation;
            }
            else if (constraint.TargetBone is { Length: > 0 } targetBone)
            {
                int index = skeleton.Bones.FindIndex(b => b.Name == targetBone);
                if (index >= 0) { targetFrame = ClipEvaluator.World(skeleton, pose)[index]; target += targetFrame.Translation; }
            }
            BoneTransform[] before = (BoneTransform[])pose.Clone();
            switch (constraint.Kind)
            {
                case ConstraintKind.Pin:
                case ConstraintKind.Parent:
                {
                    Matrix4x4 parent = skeleton.Bones[end].Parent >= 0 ? ClipEvaluator.World(skeleton, pose)[skeleton.Bones[end].Parent] : Matrix4x4.Identity;
                    if (!Matrix4x4.Invert(parent, out Matrix4x4 inverse)) break;
                    Matrix4x4 desired = constraint.Kind == ConstraintKind.Parent
                        ? constraint.Offset.Matrix * Matrix4x4.CreateTranslation(constraint.Target) * targetFrame
                        : constraint.Offset.Matrix * Matrix4x4.CreateTranslation(target);
                    BoneTransform local = BoneTransform.FromMatrix(desired * inverse);
                    pose[end] = constraint.Kind == ConstraintKind.Pin ? pose[end] with { Position = local.Position } : local;
                    break;
                }
                case ConstraintKind.LookAt:
                {
                    Matrix4x4[] world = ClipEvaluator.World(skeleton, pose);
                    Vector3 direction = target - world[end].Translation;
                    Vector3 forward = Vector3.TransformNormal(Vector3.UnitZ, world[end]);
                    RotateWorld(skeleton, pose, end, FromTo(forward, direction));
                    break;
                }
                case ConstraintKind.TwoBoneIk: TwoBone(skeleton, pose, end, target, constraint.Pole); break;
                case ConstraintKind.ChainIk: Chain(skeleton, pose, end, constraint.Root, target); break;
            }
            for (int i = 0; i < pose.Length; i++) pose[i] = BoneTransform.Blend(before[i], pose[i], weight);
        }
    }

    public static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        if (from.LengthSquared() < 1e-12f || to.LengthSquared() < 1e-12f) return Quaternion.Identity;
        from = Vector3.Normalize(from); to = Vector3.Normalize(to);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        if (dot > .999999f) return Quaternion.Identity;
        if (dot < -.999999f)
        {
            Vector3 axis = Vector3.Cross(from, MathF.Abs(from.X) < .8f ? Vector3.UnitX : Vector3.UnitY);
            return new Quaternion(Vector3.Normalize(axis), 0);
        }
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
    }

    private static void RotateWorld(SkeletonDefinition skeleton, BoneTransform[] pose, int bone, Quaternion delta)
    {
        Matrix4x4[] world = ClipEvaluator.World(skeleton, pose);
        Quaternion parent = skeleton.Bones[bone].Parent < 0 ? Quaternion.Identity : BoneTransform.FromMatrix(world[skeleton.Bones[bone].Parent]).Rotation;
        Quaternion rotation = Quaternion.Normalize(delta * BoneTransform.FromMatrix(world[bone]).Rotation);
        pose[bone] = pose[bone] with { Rotation = Quaternion.Normalize(Quaternion.Inverse(parent) * rotation) };
    }

    private static void TwoBone(SkeletonDefinition skeleton, BoneTransform[] pose, int end, Vector3 target, Vector3 pole)
    {
        int mid = skeleton.Bones[end].Parent;
        int root = mid >= 0 ? skeleton.Bones[mid].Parent : -1;
        if (root < 0) return;
        Matrix4x4[] world = ClipEvaluator.World(skeleton, pose);
        Vector3 a = world[root].Translation, b = world[mid].Translation, c = world[end].Translation;
        float upper = Vector3.Distance(a, b), lower = Vector3.Distance(b, c);
        if (upper < 1e-6f || lower < 1e-6f || Vector3.DistanceSquared(target, a) < 1e-12f) return;
        Vector3 axis = Vector3.Normalize(target - a);
        float length = Math.Clamp(Vector3.Distance(a, target), MathF.Abs(upper - lower) + 1e-6f, upper + lower - 1e-6f);
        float along = (upper * upper - lower * lower + length * length) / (2 * length);
        float height = MathF.Sqrt(MathF.Max(0, upper * upper - along * along));
        Vector3 side = pole - a - axis * Vector3.Dot(pole - a, axis);
        if (side.LengthSquared() < 1e-10f) side = Vector3.Cross(axis, MathF.Abs(axis.Y) < .9f ? Vector3.UnitY : Vector3.UnitZ);
        Vector3 elbow = a + axis * along + Vector3.Normalize(side) * height;
        RotateWorld(skeleton, pose, root, FromTo(b - a, elbow - a));
        world = ClipEvaluator.World(skeleton, pose);
        RotateWorld(skeleton, pose, mid, FromTo(world[end].Translation - world[mid].Translation, target - world[mid].Translation));
    }

    private static void Chain(SkeletonDefinition skeleton, BoneTransform[] pose, int end, string rootName, Vector3 target)
    {
        int root = skeleton.Bones.FindIndex(b => b.Name == rootName);
        if (root < 0) return;
        var chain = new List<int>();
        for (int i = skeleton.Bones[end].Parent; i >= 0; i = skeleton.Bones[i].Parent) { chain.Add(i); if (i == root) break; }
        if (chain.Count == 0 || chain[^1] != root) return;
        for (int pass = 0; pass < 16; pass++)
        {
            foreach (int joint in chain)
            {
                Matrix4x4[] world = ClipEvaluator.World(skeleton, pose);
                if (Vector3.DistanceSquared(world[end].Translation, target) < 1e-8f) return;
                RotateWorld(skeleton, pose, joint, FromTo(world[end].Translation - world[joint].Translation, target - world[joint].Translation));
            }
        }
    }

    public static Dictionary<string, BoneTransform[]> Scene(AnimationProject project, float time, IReadOnlyDictionary<string, string>? clipOverrides = null)
    {
        AnimationClip? Clip(RigInstance instance) => project.Clips.FirstOrDefault(c => c.Id == (clipOverrides?.GetValueOrDefault(instance.Id) ?? instance.Clip));
        var poses = new Dictionary<string, BoneTransform[]>(StringComparer.Ordinal);
        foreach (RigInstance instance in project.Scene)
        {
            SkeletonDefinition skeleton = project.Skeletons.Single(s => s.Id == instance.Skeleton);
            AnimationClip? clip = Clip(instance);
            poses.Add(instance.Id, ClipEvaluator.Evaluate(skeleton, clip, MathF.Max(0, time - instance.Start), constraints: false));
        }
        var solved = new HashSet<string>(StringComparer.Ordinal); var solving = new HashSet<string>(StringComparer.Ordinal);
        void Solve(RigInstance instance)
        {
            if (solved.Contains(instance.Id)) return;
            if (!solving.Add(instance.Id)) throw new InvalidDataException("Cross-rig constraints contain a dependency cycle. Use an independent world-space contact target.");
            SkeletonDefinition skeleton = project.Skeletons.Single(s => s.Id == instance.Skeleton);
            AnimationClip? clip = Clip(instance);
            if (clip is null) { solving.Remove(instance.Id); solved.Add(instance.Id); return; }
            foreach (string dependency in clip.Constraints.Select(c => c.TargetRig).OfType<string>().Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                Solve(project.Scene.FirstOrDefault(i => i.Id == dependency) ?? throw new InvalidDataException("Unknown target rig: " + dependency));
            Matrix4x4.Invert(skeleton.DisplayTransform.Matrix * instance.Transform.Matrix, out Matrix4x4 inverse);
            Apply(skeleton, poses[instance.Id], clip.Constraints, ClipEvaluator.ClipTime(clip, MathF.Max(0, time - instance.Start)), (id, bone) =>
            {
                RigInstance? other = project.Scene.FirstOrDefault(i => i.Id == id);
                if (other is null || !poses.TryGetValue(id, out BoneTransform[]? pose)) return null;
                SkeletonDefinition otherSkeleton = project.Skeletons.Single(s => s.Id == other.Skeleton);
                int index = otherSkeleton.Bones.FindIndex(b => b.Name == bone);
                Matrix4x4 frame = index < 0 ? Matrix4x4.Identity : ClipEvaluator.World(otherSkeleton, pose)[index];
                return frame * otherSkeleton.DisplayTransform.Matrix * other.Transform.Matrix * inverse;
            });
            solving.Remove(instance.Id); solved.Add(instance.Id);
        }
        foreach (RigInstance instance in project.Scene) Solve(instance);
        return poses;
    }
}
