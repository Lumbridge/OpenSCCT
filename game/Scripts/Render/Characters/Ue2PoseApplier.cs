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
    public static void Apply(AnimSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        Skeleton3D skeleton = sampler.Skeleton;
        if (sampler.Current is not { } clip)
        {
            skeleton.ResetBonePoses();
            return;
        }

        double t = sampler.Time;
        Animation animation = clip.Animation;
        foreach ((int bone, int track, Animation.TrackType type) in clip.Tracks)
        {
            switch (type)
            {
                case Animation.TrackType.Position3D:
                    skeleton.SetBonePosePosition(bone, animation.PositionTrackInterpolate(track, t));
                    break;
                case Animation.TrackType.Rotation3D:
                    skeleton.SetBonePoseRotation(bone, animation.RotationTrackInterpolate(track, t));
                    break;
                case Animation.TrackType.Scale3D:
                    skeleton.SetBonePoseScale(bone, animation.ScaleTrackInterpolate(track, t));
                    break;
                default:
                    break;
            }
        }
    }
}
