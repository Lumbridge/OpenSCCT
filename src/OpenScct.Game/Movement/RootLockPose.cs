using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The drawn root bone during a root-motion lock (spec/native/anim.md rule 3.4, spec/movement/coop.md rule 24): the
/// root bone is moved by the root motion measured since the lock, not pinned. Drawn root = root bone(t) + R(lock) -
/// R(t), with R the root sample physics uses (the keyed root track when the clip has one, else the root bone). For a
/// clip without a track this is the root bone at the lock (pinned); for a clip with a track the body shows the bone's
/// own movement around the moving actor. With lock bit 2 the caller also turns the root (position and rotation) back
/// by the actor's turn since the lock.
/// </summary>
public static class RootLockPose
{
    /// <summary>
    /// The offset to add to the sampled root bone (pawn space: X forward, Y right, Z up, uu): R(lock) - R(t), with the
    /// sideways part reversed for a mirrored clip. False when the clip has no root data (the caller pins the root).
    /// </summary>
    public static bool Offset(PawnClip? clip, float clipFrame, bool mirror, FVector lockedRoot, out FVector offset) =>
        Offset(clip, clipFrame, mirror, lockedRoot, 1, false, false, out offset);

    /// <summary>
    /// The offset to add to the sampled root bone for lock <paramref name="lockType"/> (anim.md rules 3.4 and 5.6).
    /// Forward play: R(lock) - R(t). Backward play: 2 R(lock) - R(0) - R(t), where R(0) is the root at the start of
    /// the backward play (the clip's end key); with lock bit 2 the backward offset is only -R(t) and bit 4 puts the
    /// vertical part back to 2 R(lock).z - R(0).z - R(t).z. The caller turns bone + offset by the yaw lock; that
    /// turn is about the vertical, so bit 4 changes nothing in a forward play.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "pose build game 0x109ba4f0 (bone 0 with lock 0x184 > 0): forward bone + field 0x1f0 - field 0x250; backward without bit 2 bone + 2 x 0x1f0 - 0x2b0 - 0x250; backward with bit 2 bone - 0x250 turned, bit 4 Z = bone.z + 2 x 0x1f0.z - 0x2b0.z - 0x250.z; root update game 0x109b9c40 writes 0x250 (current frame) and 0x2b0 (frame 0), LockRootMotion game 0x109b82b0 copies 0x250 to 0x1f0")]
    public static bool Offset(PawnClip? clip, float clipFrame, bool mirror, FVector lockedRoot, int lockType, bool backward,
        bool loop, out FVector offset)
    {
        offset = default;
        if (clip is null || (clip.RootTrack is null && clip.Root is not { Length: > 0 }))
        {
            return false;
        }

        FVector now = clip.RootAt(clipFrame);
        if (!backward)
        {
            offset = lockedRoot - now;
        }
        else
        {
            FVector start = clip.RootAt(loop ? 1f : 1f - (1f / clip.Frames));
            FVector full = (lockedRoot * 2f) - start - now;
            if ((lockType & 2) == 0)
            {
                offset = full;
            }
            else
            {
                offset = new FVector(-now.X, -now.Y, (lockType & 4) != 0 ? full.Z : -now.Z);
            }
        }

        if (mirror)
        {
            offset.Y = -offset.Y;
        }

        return true;
    }
}
