using System;
using Godot;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// Poses a <see cref="Skeleton3D"/> from an <see cref="AnimSampler"/>: each animated bone gets the sampled local
/// position, rotation and scale; bones the sequence leaves out keep their rest pose. This is the only writer of the
/// skeleton's pose (no AnimationPlayer, no AnimationTree).
/// </summary>
public static class Ue2PoseApplier
{
    internal sealed class PoseState(int count)
    {
        public readonly Vector3[] Positions = new Vector3[count];
        public readonly Quaternion[] Rotations = new Quaternion[count];
        public readonly Vector3[] Scales = new Vector3[count];
        public readonly byte[] Known = new byte[count];
    }

    public static void Apply(AnimSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        Skeleton3D skeleton = sampler.Skeleton;
        if (sampler.Current is not { } clip)
        {
            skeleton.ResetBonePoses();
            if (sampler.LastPose is { } reset) Array.Clear(reset.Known);
            return;
        }

        double t = sampler.Time;
        PoseState state = sampler.LastPose ??= new PoseState(skeleton.GetBoneCount());
        foreach ((int bone, int track, Animation.TrackType type) in clip.Tracks)
        {
            switch (type)
            {
                case Animation.TrackType.Position3D:
                    Vector3 position = clip.Animation.PositionTrackInterpolate(track, t);
                    if ((state.Known[bone] & 1) == 0 || state.Positions[bone] != position)
                    {
                        state.Positions[bone] = position;
                        state.Known[bone] |= 1;
                        skeleton.SetBonePosePosition(bone, position);
                    }
                    break;
                case Animation.TrackType.Rotation3D:
                    Quaternion rotation = clip.Animation.RotationTrackInterpolate(track, t);
                    if ((state.Known[bone] & 2) == 0 || state.Rotations[bone] != rotation)
                    {
                        state.Rotations[bone] = rotation;
                        state.Known[bone] |= 2;
                        skeleton.SetBonePoseRotation(bone, rotation);
                    }
                    break;
                case Animation.TrackType.Scale3D:
                    Vector3 scale = clip.Animation.ScaleTrackInterpolate(track, t);
                    if ((state.Known[bone] & 4) == 0 || state.Scales[bone] != scale)
                    {
                        state.Scales[bone] = scale;
                        state.Known[bone] |= 4;
                        skeleton.SetBonePoseScale(bone, scale);
                    }
                    break;
                default:
                    break;
            }
        }
    }
}
