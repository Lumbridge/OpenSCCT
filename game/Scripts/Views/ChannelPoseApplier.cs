using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.Views;

/// <summary>One animation channel as the pose build reads it (spec/native/anim.md, channel record).</summary>
public struct ChannelPose
{
    /// <summary>The clip, or null for an empty channel.</summary>
    public AnimClip? Clip;

    /// <summary>The sequence name (for the tween cache), or null.</summary>
    public string? Sequence;

    /// <summary>The channel frame: 0 to AnimLast for a one-shot, wrapping at 1 for a loop; negative while tweening in.</summary>
    public float Frame;

    /// <summary>The position the pose samples, normalised (a backward play mirrored in time; ANSWERS-anim.md A5).</summary>
    public float ClipFrame;

    /// <summary>Frame count N of the sequence.</summary>
    public int Frames;

    /// <summary>Frames per second of the sequence at rate 1.</summary>
    public float Fps;

    /// <summary>BlendAlpha (channel 0 is drawn at 1 whatever it holds).</summary>
    public float Alpha;

    /// <summary>The bone the channel starts at (0: the whole skeleton).</summary>
    public int StartBone;

    public bool Loop;

    public bool Mirror;
}

/// <summary>How the root bone is held during a root-motion move (iLockRootMotionType bits, anim.md rule 5.6).</summary>
[Flags]
public enum RootLock
{
    None = 0,

    /// <summary>Bit 1: the root's translation stays where the move started (the actor carries the motion).</summary>
    Translation = 1,

    /// <summary>Bit 2: the root's turn since the start is taken out too (the actor's yaw carries it).</summary>
    Yaw = 2,
}

/// <summary>
/// Builds a skeleton pose from the UE2 animation channels (anim.md rule 3, ANSWERS-anim.md A4 and A5): channel 0 at
/// weight 1, then every other channel with a clip and an alpha above 0 over the bones from its start bone down, in
/// channel order, each bone's sampled rotation slerped and translation lerped over the pose so far by the channel's
/// alpha. A channel tweening in blends from the last drawn pose by the tween step (1 - frame / last frame, so the
/// tween is linear in the frame). A mirrored channel swaps left and right bones; a locked root moves the root bone
/// against the root motion the actor carries since the move started. The only writer of a pawn skeleton's pose.
/// </summary>
public sealed class ChannelPoseApplier
{
    private readonly Skeleton3D _skeleton;
    private readonly int _count;
    private readonly int[] _parent;
    private readonly int[] _order;
    private readonly int[] _mirror;
    private readonly bool[] _included;
    private readonly Vector3[] _restPos;
    private readonly Quaternion[] _restRot;
    private readonly Vector3[] _restScale;
    private readonly Vector3[] _pos;
    private readonly Quaternion[] _rot;
    private readonly Vector3[] _scale;
    private readonly Vector3[] _fromPos;
    private readonly Quaternion[] _fromRot;
    private readonly Vector3[] _fromScale;
    private readonly Dictionary<string, int> _bones = new(StringComparer.Ordinal);
    private readonly float[] _cacheFrame = new float[64];
    private readonly string?[] _cacheSequence = new string?[64];
    private bool _hasFrom;

    public ChannelPoseApplier(Skeleton3D skeleton)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        _count = skeleton.GetBoneCount();
        _parent = new int[_count];
        _mirror = new int[_count];
        _included = new bool[_count];
        _restPos = new Vector3[_count];
        _restRot = new Quaternion[_count];
        _restScale = new Vector3[_count];
        _pos = new Vector3[_count];
        _rot = new Quaternion[_count];
        _scale = new Vector3[_count];
        _fromPos = new Vector3[_count];
        _fromRot = new Quaternion[_count];
        _fromScale = new Vector3[_count];
        for (int b = 0; b < _count; b++)
        {
            _parent[b] = skeleton.GetBoneParent(b);
            Transform3D rest = skeleton.GetBoneRest(b);
            _restPos[b] = rest.Origin;
            _restRot[b] = rest.Basis.GetRotationQuaternion();
            _restScale[b] = rest.Basis.Scale;
            _bones[skeleton.GetBoneName(b)] = b;
        }

        for (int b = 0; b < _count; b++)
        {
            _mirror[b] = _bones.TryGetValue(MirrorName(skeleton.GetBoneName(b)), out int m) ? m : b;
        }

        // Parent-first order, so "included when the parent is" can be decided in one pass.
        var order = new List<int>(_count);
        var placed = new bool[_count];
        while (order.Count < _count)
        {
            int before = order.Count;
            for (int b = 0; b < _count; b++)
            {
                if (!placed[b] && (_parent[b] < 0 || placed[_parent[b]]))
                {
                    placed[b] = true;
                    order.Add(b);
                }
            }

            if (order.Count == before)
            {
                break;
            }
        }

        _order = order.ToArray();
        RootBone = _order.Length > 0 ? _order[0] : -1;
    }

    /// <summary>The first root bone of the skeleton (the one root motion moves).</summary>
    public int RootBone { get; }

    /// <summary>The bone the aim turns this pose (-1: none), set before <see cref="Apply"/>.</summary>
    public int AimBone { get; set; } = -1;

    /// <summary>The aim's turn of <see cref="AimBone"/>, in skeleton space about the bone's origin.</summary>
    public Quaternion AimRotation { get; set; } = Quaternion.Identity;

    /// <summary>
    /// The pose build's IK turn of one bone (BuildPose's per-bone call, editor 0x111022f0): the bone's space base is
    /// turned after its pose is blended and before its children are built, so the whole subtree follows. Here the
    /// bone's local rotation is changed so that its skeleton-space rotation becomes AimRotation x the blended one.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "BuildPose 0x11109a50 calls 0x111022f0 for each bone after its space base; 0x11102160 rotates the axes and keeps the origin")]
    private void TurnAimBone()
    {
        int b = AimBone;
        if (b < 0 || b >= _count || AimRotation.IsEqualApprox(Quaternion.Identity))
        {
            return;
        }

        Quaternion parent = Quaternion.Identity;
        for (int p = _parent[b]; p >= 0; p = _parent[p])
        {
            parent = _rot[p].Normalized() * parent;
        }

        // A product of unit rotations drifts off unit length (Inverse refuses one past its tolerance): normalized again.
        parent = parent.Normalized();
        _rot[b] = (parent.Inverse() * AimRotation.Normalized() * parent * _rot[b].Normalized()).Normalized();
    }

    /// <summary>The index of a bone by name, or -1.</summary>
    public int FindBone(string name) => _bones.TryGetValue(name, out int b) ? b : -1;

    /// <summary>
    /// Poses the skeleton from <paramref name="channels"/> (index = channel). With a root lock the root bone is moved
    /// by <paramref name="rootOffset"/> (skeleton space: R(lock) - R(t), the root motion the actor carries since the
    /// lock, coop.md rule 24), or without one keeps its translation at <paramref name="lockClipFrame"/> of channel 0;
    /// with <see cref="RootLock.Yaw"/> the root (position and rotation) is turned by <paramref name="rootTurn"/> (the
    /// actor's turn since the lock, taken back out), or without one its turn since that frame is taken out.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "anim.md rule 3: channel order, alpha slerp and lerp, start bone subtree; ANSWERS-anim.md A4 tween from the last drawn pose, A5 sampled key")]
    [Provenance(ProvenanceKind.Estimated, "Mirroring swaps the L and R bones and reflects their local transforms across the mesh's X plane (anim.md open question 1)")]
    public void Apply(ReadOnlySpan<ChannelPose> channels, RootLock rootLock = RootLock.None, float lockClipFrame = 0f,
        Vector3? rootOffset = null, Quaternion? rootTurn = null)
    {
        Array.Copy(_restPos, _pos, _count);
        Array.Copy(_restRot, _rot, _count);
        Array.Copy(_restScale, _scale, _count);
        for (int c = 0; c < channels.Length; c++)
        {
            ChannelPose ch = channels[c];
            if (ch.Clip is null || ch.Frames < 1 || !(ch.Fps > 0f))
            {
                continue;
            }

            float alpha = c == 0 ? 1f : Math.Clamp(ch.Alpha, 0f, 1f);
            if (alpha <= 0f)
            {
                continue;
            }

            Mark(c == 0 ? -1 : ch.StartBone);
            float step = TweenStep(c, ch);
            ApplyClip(ch, alpha, step);
        }

        if (rootLock != RootLock.None && RootBone >= 0 && channels.Length > 0 && channels[0].Clip is { } clip0)
        {
            LockRoot(channels[0], clip0, rootLock, lockClipFrame, rootOffset, rootTurn);
        }

        TurnAimBone();
        for (int b = 0; b < _count; b++)
        {
            _skeleton.SetBonePosePosition(b, _pos[b]);
            _skeleton.SetBonePoseRotation(b, _rot[b].Normalized());
            _skeleton.SetBonePoseScale(b, _scale[b]);
        }

        Array.Copy(_pos, _fromPos, _count);
        Array.Copy(_rot, _fromRot, _count);
        Array.Copy(_scale, _fromScale, _count);
        _hasFrom = true;
    }

    /// <summary>
    /// The tween step of a channel this draw (A4): 1 when not tweening; when the frame is below 0, 1 - frame / the
    /// frame of the last draw (0 when a new tween starts, from -1/N after it), so each draw moves the pose by the
    /// frame's share of the rest.
    /// </summary>
    private float TweenStep(int c, ChannelPose ch)
    {
        if (ch.Frame >= 0f || !_hasFrom || c >= _cacheFrame.Length)
        {
            return 1f;
        }

        float t = 1f - (ch.Frame / _cacheFrame[c]);
        if (!string.Equals(_cacheSequence[c], ch.Sequence, StringComparison.Ordinal) || !(t >= 0f && t <= 1f))
        {
            // A new tween: this draw still shows the last pose; the next one moves from the clip's first tween frame.
            _cacheFrame[c] = -1f / ch.Frames;
            _cacheSequence[c] = ch.Sequence;
            return 0f;
        }

        _cacheFrame[c] = ch.Frame;
        return t;
    }

    /// <summary>Marks the bones a channel affects: its start bone and every bone below it (-1 or 0: all).</summary>
    private void Mark(int start)
    {
        if (start <= 0 || start >= _count)
        {
            Array.Fill(_included, true);
            return;
        }

        Array.Clear(_included);
        foreach (int b in _order)
        {
            _included[b] = b == start || (_parent[b] >= 0 && _included[_parent[b]]);
        }
    }

    private void ApplyClip(ChannelPose ch, float alpha, float tween)
    {
        AnimClip clip = ch.Clip!;
        Animation animation = clip.Animation;
        float t = TimeOf(ch, ch.ClipFrame);
        float length = clip.Length;
        bool wrap = ch.Loop && t > length && length > 0f;
        float wrapAlpha = wrap ? Math.Clamp((t - length) * ch.Fps, 0f, 1f) : 0f;
        foreach ((int bone, int track, Animation.TrackType type) in clip.Tracks)
        {
            int target = ch.Mirror ? _mirror[bone] : bone;
            if (!_included[target])
            {
                continue;
            }

            switch (type)
            {
                case Animation.TrackType.Position3D:
                {
                    Vector3 p = (Vector3)animation.PositionTrackInterpolate(track, wrap ? length : t);
                    if (wrap)
                    {
                        p = p.Lerp((Vector3)animation.PositionTrackInterpolate(track, 0.0), wrapAlpha);
                    }

                    if (ch.Mirror)
                    {
                        p = new Vector3(-p.X, p.Y, p.Z);
                    }

                    if (tween < 1f)
                    {
                        p = _fromPos[target].Lerp(p, tween);
                    }

                    _pos[target] = alpha >= 1f ? p : _pos[target].Lerp(p, alpha);
                    break;
                }

                case Animation.TrackType.Rotation3D:
                {
                    // Sampled keys can be a little off unit length; Godot's slerp needs unit quaternions.
                    Quaternion q = animation.RotationTrackInterpolate(track, wrap ? length : t).Normalized();
                    if (wrap)
                    {
                        q = q.Slerp(animation.RotationTrackInterpolate(track, 0.0).Normalized(), wrapAlpha).Normalized();
                    }

                    if (ch.Mirror)
                    {
                        q = new Quaternion(q.X, -q.Y, -q.Z, q.W);
                    }

                    if (tween < 1f)
                    {
                        q = _fromRot[target].Normalized().Slerp(q, tween).Normalized();
                    }

                    _rot[target] = alpha >= 1f ? q : _rot[target].Normalized().Slerp(q, alpha).Normalized();
                    break;
                }

                case Animation.TrackType.Scale3D:
                {
                    Vector3 s = (Vector3)animation.ScaleTrackInterpolate(track, wrap ? length : t);
                    if (tween < 1f)
                    {
                        s = _fromScale[target].Lerp(s, tween);
                    }

                    _scale[target] = alpha >= 1f ? s : _scale[target].Lerp(s, alpha);
                    break;
                }

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// The root during a locked move (anim.md rules 3.4, 5.6; coop.md rule 24): the sampled root bone moved by the root
    /// motion since the lock (bone(t) + R(lock) - R(t), which for a clip without a root track is the bone at the lock)
    /// and, with the yaw lock, turned back about the mesh's vertical axis by the actor's turn since the lock (position
    /// and rotation together, so the body stays put while the actor's yaw carries the turn). Without an offset (no root
    /// data) the translation is pinned at the lock frame; without a turn the bone's own turn since the lock frame is
    /// taken out of its rotation.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "pose build game 0x109ba4f0: bone 0 with a lock is root + field 0x1f0 - field 0x250 (coop.md rule 24); with lock bit 2 the result is turned back by the root yaw since the lock, position and rotation")]
    private void LockRoot(ChannelPose ch0, AnimClip clip0, RootLock rootLock, float lockClipFrame, Vector3? rootOffset,
        Quaternion? rootTurn)
    {
        int root = RootBone;
        if (rootOffset is { } offset && (rootLock & RootLock.Translation) != 0)
        {
            _pos[root] += offset;
        }

        if (rootTurn is { } turnBack && (rootLock & RootLock.Yaw) != 0)
        {
            Quaternion q = turnBack.Normalized();
            _pos[root] = q * _pos[root];
            _rot[root] = (q * _rot[root]).Normalized();
            rootLock &= ~RootLock.Yaw;
        }

        float lockTime = TimeOf(ch0, lockClipFrame);
        foreach ((int bone, int track, Animation.TrackType type) in clip0.Tracks)
        {
            if (bone != root)
            {
                continue;
            }

            if (type == Animation.TrackType.Position3D && (rootLock & RootLock.Translation) != 0 && rootOffset is null)
            {
                _pos[root] = (Vector3)clip0.Animation.PositionTrackInterpolate(track, lockTime);
            }
            else if (type == Animation.TrackType.Rotation3D && (rootLock & RootLock.Yaw) != 0)
            {
                Quaternion atLock = clip0.Animation.RotationTrackInterpolate(track, lockTime).Normalized();
                float turn = YawOf(_rot[root]) - YawOf(atLock);
                _rot[root] = (new Quaternion(Vector3.Up, -turn) * _rot[root]).Normalized();
            }
        }
    }

    /// <summary>The turn of a rotation's X axis about the vertical (Godot Y) axis, in radians.</summary>
    private static float YawOf(Quaternion q)
    {
        Vector3 x = q * Vector3.Right;
        return MathF.Atan2(-x.Z, x.X);
    }

    /// <summary>Seconds into the clip of a normalised position: the position times N keys at the clip's rate.</summary>
    private static float TimeOf(ChannelPose ch, float clipFrame)
    {
        float t = clipFrame * ch.Frames / ch.Fps;
        return ch.Loop ? t : Math.Clamp(t, 0f, ch.Clip?.Length ?? 0f);
    }

    /// <summary>The mirror bone's name: " L " and " R " (and trailing L or R parts) swapped.</summary>
    private static string MirrorName(string name)
    {
        if (name.Contains(" L ", StringComparison.Ordinal))
        {
            return name.Replace(" L ", " R ", StringComparison.Ordinal);
        }

        if (name.Contains(" R ", StringComparison.Ordinal))
        {
            return name.Replace(" R ", " L ", StringComparison.Ordinal);
        }

        if (name.EndsWith(" L", StringComparison.Ordinal))
        {
            return name[..^1] + "R";
        }

        if (name.EndsWith(" R", StringComparison.Ordinal))
        {
            return name[..^1] + "L";
        }

        return name;
    }
}
