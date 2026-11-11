using System.Runtime.CompilerServices;
using Godot;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// A skeleton's bone poses for the frame being drawn, read from the engine once a frame whoever asks: the own frame's
/// object pass and the shadow pass both pose the same characters, and a character's parts share one skeleton.
/// </summary>
internal static class RdSkeletonPoses
{
    private sealed class Entry
    {
        public ulong Frame = ulong.MaxValue;
        public Transform3D[] Poses = [];
    }

    private static readonly ConditionalWeakTable<Skeleton3D, Entry> s_entries = new();

    /// <summary>Every bone's global pose (the skeleton's own space), by bone index. Valid until the next frame; main thread.</summary>
    public static Transform3D[] Of(Skeleton3D skeleton)
    {
        Entry entry = s_entries.GetValue(skeleton, static _ => new Entry());
        ulong frame = Engine.GetProcessFrames();
        if (entry.Frame == frame) return entry.Poses;
        int count = skeleton.GetBoneCount();
        if (entry.Poses.Length != count) entry.Poses = new Transform3D[count];
        for (int i = 0; i < count; i++) entry.Poses[i] = skeleton.GetBoneGlobalPose(i);
        entry.Frame = frame;
        return entry.Poses;
    }
}
