using System.Numerics;

namespace OpenScct.Animation;

/// <summary>Portable animation source. Bone transforms use glTF metres, Y up and parent-local TRS.</summary>
public sealed class AnimationProject
{
    public string Format { get; set; } = "osanim/1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled animation";
    public float FramesPerSecond { get; set; } = 30;
    public List<SkeletonDefinition> Skeletons { get; set; } = [];
    public List<AnimationClip> Clips { get; set; } = [];
    public List<PosePreset> Poses { get; set; } = [];
    public List<ControlRig> ControlRigs { get; set; } = [];
    public List<RetargetProfile> RetargetProfiles { get; set; } = [];
    public List<RigInstance> Scene { get; set; } = [];
    public List<AnimationGraph> Graphs { get; set; } = [];
    public List<AnimationSequence> Sequences { get; set; } = [];
    public List<AnimationBinding> Bindings { get; set; } = [];
    public List<FirstPersonProfile> FirstPersonProfiles { get; set; } = [];
}

public enum FirstPersonRole { Spy, Merc }
public sealed class FirstPersonProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public FirstPersonRole Role { get; set; }
    public string Weapon { get; set; } = "";
    public string MainRig { get; set; } = "";
    public string? ArmsRig { get; set; }
    public string MainAsset { get; set; } = "";
    public string? ArmsAsset { get; set; }
    public float FieldOfView { get; set; } = 70;
    public bool Enabled { get; set; }
    public BoneTransform WeaponMount { get; set; } = BoneTransform.Identity;
    public List<FirstPersonAction> Actions { get; set; } = [];
}
public sealed class FirstPersonAction
{
    public string Name { get; set; } = "Idle";
    public string Slot { get; set; } = "";
    public string? Clip { get; set; }
    public string? ArmsClip { get; set; }
}

public sealed class SkeletonDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Rig";
    public string? Model { get; set; }
    public BoneTransform DisplayTransform { get; set; } = BoneTransform.Identity;
    public List<BoneDefinition> Bones { get; set; } = [];
}

public sealed class BoneDefinition
{
    public string Name { get; set; } = "Root";
    public int Parent { get; set; } = -1;
    public BoneTransform Rest { get; set; } = BoneTransform.Identity;
}

public readonly record struct BoneTransform(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static BoneTransform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    public Matrix4x4 Matrix => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
    public static BoneTransform Blend(BoneTransform a, BoneTransform b, float weight) => new(
        Vector3.Lerp(a.Position, b.Position, weight), Quaternion.Normalize(Quaternion.Slerp(a.Rotation, b.Rotation, weight)), Vector3.Lerp(a.Scale, b.Scale, weight));
    public static BoneTransform FromMatrix(Matrix4x4 value)
    {
        if (!Matrix4x4.Decompose(value, out Vector3 scale, out Quaternion rotation, out Vector3 position))
            throw new InvalidDataException("Animation transform cannot be decomposed into TRS.");
        return new(position, Quaternion.Normalize(rotation), scale);
    }
}

public enum TrackChannel { Translation, Rotation, Scale }
public enum KeyInterpolation { Step, Linear, Cubic }
public sealed class AnimationKey
{
    public float Time { get; set; }
    public Vector4 Value { get; set; }
    public Vector4 InTangent { get; set; }
    public Vector4 OutTangent { get; set; }
    public KeyInterpolation Interpolation { get; set; } = KeyInterpolation.Linear;
}

public sealed class BoneTrack
{
    public string Bone { get; set; } = "Root";
    public TrackChannel Channel { get; set; }
    public List<AnimationKey> Keys { get; set; } = [];
}

public sealed class AnimationClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New clip";
    public string Skeleton { get; set; } = "";
    public float Duration { get; set; } = 1;
    public float FramesPerSecond { get; set; } = 30;
    public bool Loop { get; set; }
    public string? Source { get; set; }
    public string? SourceClip { get; set; }
    public string? RootMotionBone { get; set; }
    public List<BoneTrack> RootTracks { get; set; } = [];
    public List<BoneTrack> Tracks { get; set; } = [];
    public List<AnimationLayer> Layers { get; set; } = [];
    public List<AnimationEvent> Events { get; set; } = [];
    public List<RigConstraint> Constraints { get; set; } = [];
}

public sealed class AnimationLayer
{
    public string Name { get; set; } = "Layer";
    public bool Enabled { get; set; } = true;
    public bool Additive { get; set; }
    public float Weight { get; set; } = 1;
    public List<AnimationKey> WeightKeys { get; set; } = [];
    public List<string> Mask { get; set; } = [];
    public List<BoneTrack> Tracks { get; set; } = [];
}

public sealed class PosePreset
{
    public string Name { get; set; } = "Pose";
    public string Skeleton { get; set; } = "";
    public List<BoneTransform> Bones { get; set; } = [];
}

public enum ConstraintKind { TwoBoneIk, ChainIk, LookAt, Parent, Pin }
public sealed class RigConstraint
{
    public string Name { get; set; } = "Constraint";
    public ConstraintKind Kind { get; set; }
    public string Bone { get; set; } = "";
    public string Root { get; set; } = "";
    public string? TargetRig { get; set; }
    public string? TargetBone { get; set; }
    public Vector3 Target { get; set; }
    public Vector3 Pole { get; set; } = Vector3.UnitZ;
    public float Weight { get; set; } = 1;
    public List<AnimationKey> WeightKeys { get; set; } = [];
    public BoneTransform Offset { get; set; } = BoneTransform.Identity;
}

public sealed class ControlRig
{
    public string Name { get; set; } = "Controls";
    public string Skeleton { get; set; } = "";
    public List<RigConstraint> Controls { get; set; } = [];
    public Dictionary<string, string> MirrorBones { get; set; } = new(StringComparer.Ordinal);
}

public sealed class RetargetProfile
{
    public string Name { get; set; } = "Retarget";
    public string SourceSkeleton { get; set; } = "";
    public string TargetSkeleton { get; set; } = "";
    public float RootScale { get; set; } = 1;
    public Dictionary<string, string> Bones { get; set; } = new(StringComparer.Ordinal);
}

public sealed class RigInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Character";
    public string Skeleton { get; set; } = "";
    public string? Clip { get; set; }
    public BoneTransform Transform { get; set; } = BoneTransform.Identity;
    public float Start { get; set; }
}

public enum AnimationEventKind { Marker, Sound, StopSound, Trigger, Untrigger, ScriptNotify, Attach, Detach }
public sealed class AnimationEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public float Time { get; set; }
    public AnimationEventKind Kind { get; set; }
    public float? SourceNormalizedTime { get; set; }
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
}

public enum ReplacementMode { Visual, Gameplay }
public sealed class AnimationBinding
{
    public string Target { get; set; } = "";
    public string Slot { get; set; } = "";
    public string? Clip { get; set; }
    public string? Graph { get; set; }
    public string? Sequence { get; set; }
    public ReplacementMode Mode { get; set; }
}
