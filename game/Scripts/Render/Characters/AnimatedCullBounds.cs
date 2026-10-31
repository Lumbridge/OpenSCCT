using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render.Characters;

/// <summary>Conservative bounds for every linear-keyed pose, independent of the bone rotations.</summary>
internal sealed class AnimatedCullBounds
{
    private readonly float[] _distance;
    private readonly float[] _scale;
    private readonly Skeleton3D _skeleton;
    private bool _valid = true;
    private readonly Dictionary<(Rid Mesh, ulong Skin, Transform3D Relative), Aabb> _cache = [];

    public AnimatedCullBounds(AnimSampler sampler)
    {
        _skeleton = sampler.Skeleton;
        int count = _skeleton.GetBoneCount();
        _distance = new float[count];
        _scale = new float[count];
        for (int bone = 0; bone < count; bone++)
        {
            Transform3D rest = _skeleton.GetBoneRest(bone);
            _distance[bone] = rest.Origin.Length();
            _scale[bone] = MaxScale(rest.Basis.Scale);
        }

        foreach (string name in sampler.ClipNames)
        {
            AnimClip clip = sampler.Find(name)!;
            foreach ((int bone, int track, Animation.TrackType type) in clip.Tracks)
            {
                if (type == Animation.TrackType.Rotation3D) continue;
                Animation animation = clip.Animation;
                if (animation.TrackGetInterpolationType(track) is Animation.InterpolationType.Cubic or Animation.InterpolationType.CubicAngle)
                {
                    // Cubic tangents may overshoot the keys; preserve the previous generous margin for these assets.
                    _valid = false;
                    return;
                }

                for (int key = 0; key < animation.TrackGetKeyCount(track); key++)
                {
                    using Variant value = animation.TrackGetKeyValue(track, key);
                    Vector3 v = value.AsVector3();
                    if (type == Animation.TrackType.Position3D) _distance[bone] = Math.Max(_distance[bone], v.Length());
                    else _scale[bone] = Math.Max(_scale[bone], MaxScale(v));
                }
            }
        }

        // A child origin cannot be farther away than the sum of the local offsets along its ancestry, scaled by
        // each parent. Rotation and convex channel blends cannot increase these bounds.
        var visited = new bool[count];
        for (int bone = 0; bone < count; bone++) Accumulate(bone, visited);
    }

    private void Accumulate(int bone, bool[] visited)
    {
        if (visited[bone]) return;
        int parent = _skeleton.GetBoneParent(bone);
        if (parent >= 0)
        {
            Accumulate(parent, visited);
            _distance[bone] = _distance[parent] + _distance[bone] * _scale[parent];
            _scale[bone] *= _scale[parent];
        }

        visited[bone] = true;
    }

    public bool TryGet(MeshInstance3D node, Skeleton3D skeleton, out Aabb bounds)
    {
        bounds = default;
        if (!_valid || node.Mesh is not ArrayMesh mesh || node.Skin is not { } skin || mesh.GetBlendShapeCount() != 0) return false;
        Transform3D relative = ToRoot(node).AffineInverse() * ToRoot(skeleton);
        var key = (mesh.GetRid(), skin.GetInstanceId(), relative);
        if (_cache.TryGetValue(key, out bounds)) return true;
        float radius = 0;
        Aabb restBox = mesh.GetAabb();
        for (int bind = 0; bind < skin.GetBindCount(); bind++)
        {
            int bone = skin.GetBindBone(bind);
            if (bone < 0) bone = skeleton.FindBone(skin.GetBindName(bind));
            if (bone < 0 || bone >= _distance.Length) return false;
            Transform3D inverseBind = skin.GetBindPose(bind);
            float influence = 0;
            for (int corner = 0; corner < 8; corner++)
                influence = Math.Max(influence, (inverseBind * restBox.GetEndpoint(corner)).Length());
            radius = Math.Max(radius, _distance[bone] + _scale[bone] * influence);
        }

        if (!(radius > 0) || !float.IsFinite(radius)) return false;
        Vector3 extent = Vector3.One * (radius + 0.1f);
        bounds = relative * new Aabb(-extent, extent * 2f);
        _cache[key] = bounds;
        return true;
    }

    private static float MaxScale(Vector3 scale)
    {
        Vector3 v = scale.Abs();
        return Math.Max(v.X, Math.Max(v.Y, v.Z));
    }

    private static Transform3D ToRoot(Node3D node)
    {
        Transform3D transform = node.Transform;
        for (Node? parent = node.GetParent(); parent is Node3D spatial; parent = parent.GetParent())
            transform = spatial.Transform * transform;
        return transform;
    }
}
