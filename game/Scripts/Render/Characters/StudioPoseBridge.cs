using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Animation;

namespace OpenScctGame.Render.Characters;

/// <summary>The same native animation pose upload in the studio and game.</summary>
public static class StudioPoseBridge
{
    public static Transform3D Transform(BoneTransform value) => new(new Basis(new Quaternion(value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W)).Scaled(new(value.Scale.X, value.Scale.Y, value.Scale.Z)), new(value.Position.X, value.Position.Y, value.Position.Z));
    public static void Apply(Skeleton3D skeleton, SkeletonDefinition definition, IReadOnlyList<BoneTransform> pose)
    {
        if (pose.Count != definition.Bones.Count) throw new ArgumentException("Pose does not match its skeleton.");
        for (int i = 0; i < definition.Bones.Count; i++)
        {
            int bone = skeleton.FindBone(definition.Bones[i].Name);
            if (bone < 0) continue;
            BoneTransform transform = pose[i];
            skeleton.SetBonePosePosition(bone, new(transform.Position.X, transform.Position.Y, transform.Position.Z));
            skeleton.SetBonePoseRotation(bone, new(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W));
            skeleton.SetBonePoseScale(bone, new(transform.Scale.X, transform.Scale.Y, transform.Scale.Z));
        }
    }
}
