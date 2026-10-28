using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// The bone direction controllers a skeletal mesh instance keeps (Actor.SetBoneDirection, spec/native/aim-ik.md
/// rule 5): per bone, a world rotation the pose build gives the bone in place of its animated one, by an alpha. A
/// controller stays until the script sets it again or releases it (alpha 0). The simulation sets them (TurnHead's arm
/// cases, <see cref="ArmAim"/>); the view applies them when it poses the mesh.
/// </summary>
public sealed class MeshBoneDirections
{
    private readonly List<BoneDirection> _controls = new(4);

    /// <summary>The bones under control now.</summary>
    public IReadOnlyList<BoneDirection> All => _controls;

    /// <summary>
    /// Actor.SetBoneDirection in world space (space 1): <paramref name="bone"/> is drawn with the world rotation
    /// <paramref name="rotation"/> at <paramref name="alpha"/>; alpha 0 releases the bone to its animation.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "SetBoneDirection space 1 read as the bone's world frame (its X axis down the bone, as the SPerso skeletons have it) set to the rotator's axes; the native (1109) is not decoded")]
    public void SetBoneDirection(Name bone, FRotator rotation, float alpha)
    {
        int i = IndexOf(bone);
        if (!(alpha > 0f))
        {
            if (i >= 0)
            {
                _controls.RemoveAt(i);
            }

            return;
        }

        var control = new BoneDirection(bone, rotation, alpha > 1f ? 1f : alpha);
        if (i >= 0)
        {
            _controls[i] = control;
        }
        else
        {
            _controls.Add(control);
        }
    }

    /// <summary>The world rotation <paramref name="bone"/> is held at, when a full controller holds it.</summary>
    public bool TryGetRotation(Name bone, out FRotator rotation)
    {
        int i = IndexOf(bone);
        if (i >= 0 && _controls[i].Alpha >= 1f)
        {
            rotation = _controls[i].Rotation;
            return true;
        }

        rotation = default;
        return false;
    }

    /// <summary>Releases every bone.</summary>
    public void Clear() => _controls.Clear();

    private int IndexOf(Name bone)
    {
        for (int i = 0; i < _controls.Count; i++)
        {
            if (_controls[i].Bone == bone)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>One bone direction controller: the bone, its world rotation and the alpha it is laid over the pose by.</summary>
public readonly record struct BoneDirection(Name Bone, FRotator Rotation, float Alpha);
