using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render.Characters;

/// <summary>One sequence's bone tracks, bound to a skeleton.</summary>
public sealed class AnimClip
{
    internal AnimClip(string name, Animation animation, List<(int Bone, int Track, Animation.TrackType Type)> tracks)
    {
        Name = name;
        Animation = animation;
        Tracks = tracks;
    }

    public string Name { get; }

    public Animation Animation { get; }

    /// <summary>Seconds of the sequence at rate 1 (its last key).</summary>
    public float Length => (float)Animation.Length;

    internal List<(int Bone, int Track, Animation.TrackType Type)> Tracks { get; }
}

/// <summary>
/// A simple stand-in for the core animation channels: plays one sequence by name at a rate, looping or once, and
/// keeps the UE2 normalized frame (0 to 1). It only samples; <see cref="Ue2PoseApplier"/> writes the pose. Godot's
/// AnimationPlayer and AnimationTree are never used to drive the skeleton.
/// </summary>
public sealed class AnimSampler
{
    private readonly Dictionary<string, AnimClip> _clips;

    public AnimSampler(Skeleton3D skeleton, IEnumerable<(string Name, Animation Animation)> animations)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(animations);
        Skeleton = skeleton;
        _clips = new Dictionary<string, AnimClip>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, Animation animation) in animations)
        {
            var tracks = new List<(int, int, Animation.TrackType)>();
            for (int t = 0; t < animation.GetTrackCount(); t++)
            {
                Animation.TrackType type = animation.TrackGetType(t);
                if (type is not (Animation.TrackType.Position3D or Animation.TrackType.Rotation3D or Animation.TrackType.Scale3D))
                {
                    continue;
                }

                NodePath path = animation.TrackGetPath(t);
                string bone = path.GetConcatenatedSubNames();
                int index = bone.Length > 0 ? skeleton.FindBone(bone) : -1;
                if (index >= 0)
                {
                    tracks.Add((index, t, type));
                }
            }

            _clips[name] = new AnimClip(name, animation, tracks);
        }
    }

    private AnimSampler(Skeleton3D skeleton, Dictionary<string, AnimClip> clips)
    {
        Skeleton = skeleton;
        _clips = clips;
    }

    /// <summary>
    /// A sampler of the same clips for <paramref name="skeleton"/>, a copy of this sampler's skeleton (same bones in
    /// the same order): the clips' bone tracks are shared, only the playing state is new.
    /// </summary>
    public AnimSampler For(Skeleton3D skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        return new AnimSampler(skeleton, _clips);
    }

    public Skeleton3D Skeleton { get; }
    internal Ue2PoseApplier.PoseState? LastPose { get; set; }

    public IEnumerable<string> ClipNames => _clips.Keys;

    public AnimClip? Current { get; private set; }

    /// <summary>Play rate: 1 plays the sequence at its own frame rate.</summary>
    public float Rate { get; set; } = 1f;

    public bool Loop { get; set; } = true;

    /// <summary>Seconds into the current sequence at rate 1.</summary>
    public float Time { get; private set; }

    /// <summary>The UE2 channel frame: position in the sequence from 0 to 1.</summary>
    public float Frame => Current is { Length: > 0f } c ? Time / c.Length : 0f;

    public bool Has(string name) => _clips.ContainsKey(name);

    /// <summary>The clip of a sequence by name, or null (for appliers that blend several channels).</summary>
    public AnimClip? Find(string name) => _clips.TryGetValue(name, out AnimClip? clip) ? clip : null;

    /// <summary>Starts <paramref name="name"/> at <paramref name="frame"/> (0 to 1). False when the mesh has no such sequence.</summary>
    public bool Play(string name, float rate = 1f, bool loop = true, float frame = 0f)
    {
        if (!_clips.TryGetValue(name, out AnimClip? clip))
        {
            return false;
        }

        Current = clip;
        Rate = rate;
        Loop = loop;
        Time = Math.Clamp(frame, 0f, 1f) * clip.Length;
        return true;
    }

    /// <summary>Sample one bone in skeleton space without changing the displayed pose.</summary>
    public Transform3D SampleBone(AnimClip clip, int bone, float frame)
    {
        Transform3D result = Transform3D.Identity;
        double time = Math.Clamp(frame, 0f, 1f) * clip.Length;
        for (int b = bone; b >= 0; b = Skeleton.GetBoneParent(b))
        {
            Transform3D rest = Skeleton.GetBoneRest(b);
            Vector3 position = rest.Origin, scale = rest.Basis.Scale;
            Quaternion rotation = rest.Basis.GetRotationQuaternion();
            foreach (var (index, track, type) in clip.Tracks)
            {
                if (index != b) continue;
                switch (type)
                {
                    case Animation.TrackType.Position3D: position = clip.Animation.PositionTrackInterpolate(track, time); break;
                    case Animation.TrackType.Rotation3D: rotation = clip.Animation.RotationTrackInterpolate(track, time).Normalized(); break;
                    case Animation.TrackType.Scale3D: scale = clip.Animation.ScaleTrackInterpolate(track, time); break;
                }
            }
            result = new Transform3D(new Basis(rotation).ScaledLocal(scale), position) * result;
        }
        return result;
    }

    public void Advance(double delta)
    {
        if (Current is not { } clip || clip.Length <= 0f)
        {
            return;
        }

        float t = Time + ((float)delta * Rate);
        Time = Loop ? ((t % clip.Length) + clip.Length) % clip.Length : Math.Clamp(t, 0f, clip.Length);
    }
}
