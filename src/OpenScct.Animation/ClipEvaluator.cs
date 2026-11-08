using System.Numerics;

namespace OpenScct.Animation;

/// <summary>Shared authoring/runtime evaluator. Evaluating or seeking never dispatches events.</summary>
public static class ClipEvaluator
{
    public static Vector4 Sample(IReadOnlyList<AnimationKey> keys, float time, bool rotation = false)
    {
        if (keys.Count == 0) return rotation ? new Vector4(0, 0, 0, 1) : Vector4.Zero;
        if (time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;
        int lo = 0, hi = keys.Count - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (keys[mid].Time <= time) lo = mid; else hi = mid; }
        AnimationKey a = keys[lo], b = keys[hi];
        float span = b.Time - a.Time, t = (time - a.Time) / span;
        if (a.Interpolation == KeyInterpolation.Step) return a.Value;
        if (a.Interpolation == KeyInterpolation.Linear)
        {
            if (!rotation) return Vector4.Lerp(a.Value, b.Value, t);
            return V(Quaternion.Normalize(Quaternion.Slerp(Q(a.Value), Q(b.Value), t)));
        }
        float t2 = t * t, t3 = t2 * t;
        Vector4 value = (2 * t3 - 3 * t2 + 1) * a.Value + (t3 - 2 * t2 + t) * span * a.OutTangent
            + (-2 * t3 + 3 * t2) * b.Value + (t3 - t2) * span * b.InTangent;
        return rotation ? V(SafeRotation(value)) : value;
    }

    public static Quaternion SafeRotation(Vector4 value) => value.LengthSquared() > 1e-12f ? Quaternion.Normalize(Q(value)) : Quaternion.Identity;
    public static Quaternion Q(Vector4 value) => new(value.X, value.Y, value.Z, value.W);
    public static Vector4 V(Quaternion value) => new(value.X, value.Y, value.Z, value.W);
    public static Vector4 V(Vector3 value) => new(value, 0);
    public static Vector3 Xyz(Vector4 value) => new(value.X, value.Y, value.Z);
    public static float ClipTime(AnimationClip clip, float time) => clip.Loop && clip.Duration > 0
        ? ((time % clip.Duration) + clip.Duration) % clip.Duration : Math.Clamp(time, 0, clip.Duration);

    public static BoneTransform[] Evaluate(SkeletonDefinition skeleton, AnimationClip? clip, float time, bool constraints = true)
    {
        BoneTransform[] pose = skeleton.Bones.Select(b => b.Rest).ToArray();
        if (clip is null) return pose;
        float t = ClipTime(clip, time);
        ApplyTracks(skeleton, pose, clip.Tracks, t);
        foreach (AnimationLayer layer in clip.Layers)
        {
            if (!layer.Enabled) continue;
            float weight = Math.Clamp(layer.Weight * (layer.WeightKeys.Count > 0 ? Sample(layer.WeightKeys, t).X : 1), 0, 1);
            BoneTransform[] next = layer.Additive ? skeleton.Bones.Select(b => b.Rest).ToArray() : (BoneTransform[])pose.Clone();
            ApplyTracks(skeleton, next, layer.Tracks, t);
            for (int i = 0; i < pose.Length; i++)
            {
                if (layer.Mask.Count > 0 && !layer.Mask.Contains(skeleton.Bones[i].Name, StringComparer.Ordinal)) continue;
                if (!layer.Tracks.Any(track => track.Bone == skeleton.Bones[i].Name)) continue;
                pose[i] = layer.Additive ? Additive(pose[i], next[i], skeleton.Bones[i].Rest, weight) : BoneTransform.Blend(pose[i], next[i], weight);
            }
        }
        if (constraints) RigSolver.Apply(skeleton, pose, clip.Constraints, t);
        return pose;
    }

    public static BoneTransform Additive(BoneTransform value, BoneTransform delta, BoneTransform reference, float weight)
    {
        Quaternion difference = Quaternion.Normalize(Quaternion.Inverse(reference.Rotation) * delta.Rotation);
        Vector3 scale = new(reference.Scale.X == 0 ? 1 : delta.Scale.X / reference.Scale.X,
            reference.Scale.Y == 0 ? 1 : delta.Scale.Y / reference.Scale.Y, reference.Scale.Z == 0 ? 1 : delta.Scale.Z / reference.Scale.Z);
        return new(value.Position + (delta.Position - reference.Position) * weight,
            Quaternion.Normalize(value.Rotation * Quaternion.Slerp(Quaternion.Identity, difference, weight)), value.Scale * Vector3.Lerp(Vector3.One, scale, weight));
    }
    public static BoneTransform RootMotion(AnimationClip clip, float time)
    {
        var definition = new SkeletonDefinition { Bones = [new() { Name = "RootMotion" }] };
        var pose = new[] { BoneTransform.Identity }; ApplyTracks(definition, pose, clip.RootTracks, time); return pose[0];
    }

    public static void ApplyTracks(SkeletonDefinition skeleton, BoneTransform[] pose, IEnumerable<BoneTrack> tracks, float time)
    {
        foreach (BoneTrack track in tracks)
        {
            int bone = skeleton.Bones.FindIndex(b => b.Name == track.Bone);
            if (bone < 0 || track.Keys.Count == 0) continue;
            BoneTransform p = pose[bone];
            Vector4 value = Sample(track.Keys, time, track.Channel == TrackChannel.Rotation);
            pose[bone] = track.Channel switch
            {
                TrackChannel.Translation => p with { Position = Xyz(value) },
                TrackChannel.Rotation => p with { Rotation = SafeRotation(value) },
                _ => p with { Scale = Xyz(value) },
            };
        }
    }

    public static Matrix4x4[] World(SkeletonDefinition skeleton, IReadOnlyList<BoneTransform> pose)
    {
        var world = new Matrix4x4[pose.Count];
        for (int i = 0; i < pose.Count; i++) world[i] = skeleton.Bones[i].Parent < 0 ? pose[i].Matrix : pose[i].Matrix * world[skeleton.Bones[i].Parent];
        return world;
    }

    /// <summary>Forward playback edges use (from,to]; start events are requested explicitly by the player.</summary>
    public static IEnumerable<AnimationEvent> Events(AnimationClip clip, float from, float to, bool includeStart = false)
        => EventEdges(clip, from, to, includeStart).Select(edge => edge.Event);

    public static IEnumerable<(float Time, AnimationEvent Event)> EventEdges(AnimationClip clip, float from, float to, bool includeStart = false)
    {
        if (to < from || clip.Duration <= 0) yield break;
        int first = clip.Loop ? (int)MathF.Floor(from / clip.Duration) : 0;
        int last = clip.Loop ? (int)MathF.Floor(to / clip.Duration) : 0;
        for (int loop = first; loop <= last; loop++)
            foreach (AnimationEvent e in clip.Events.OrderBy(e => e.Time).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                float at = loop * clip.Duration + e.Time;
                if ((at > from || (includeStart && at == from)) && at <= to) yield return (at, e);
            }
    }
}
