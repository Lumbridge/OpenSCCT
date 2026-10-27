using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

// AActor::physFollow (PHYS_FollowParent, mode 12): the held merc of a spy grab (spec/combat/spy-grab.md).
public sealed partial class PhysicsDriver
{
    /// <summary>
    /// The held pawn follows its grabber (m_AttackedBy): the grabber's pitch and roll are zeroed, the pawn goes to
    /// the grabber's location plus the pawn's own vGrabOffset turned by the grabber's rotation, takes the grabber's
    /// rotation and base, and its velocity is the move over the step (zero below 2 uu/s). Called at most once per
    /// level time (the fLastRootMotionTimeCalled guard); a pawn with no grabber does not move.
    /// </summary>
    [UcNative("AActor::physFollow", Exe = 0x10ad2750, Editor = 0x111fcd90)]
    [PortOf("eng:AActor::physFollow", Spec = "spec/combat/spy-grab.md#holding-spy-s_grabbingplayer-merc-s_grabbedbyplayer")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111fcd90: base = this+0x630 (m_AttackedBy), offset = this+0x4b0 (vGrabOffset) through the base's coords, MoveActor with the base rotation, SetBase(base.Base), velocity from the move, guard at this+0xa50")]
    [Provenance(ProvenanceKind.Estimated, "The authority's copy of the grabber rotation into ReceivedRotation (this+0x60c) is left out: the field is not on the physics interface")]
    public void physFollow(Pawn Pawn, float DeltaTime)
    {
        NotNull(Pawn);
        if (Pawn is not ISBasePawnPhysics sb)
        {
            return;
        }

        float now = Pawn.Level.TimeSeconds;
        if (now < sb.fLastRootMotionTimeCalled + K.RootMotion.TimeGuard)
        {
            return;
        }

        if (sb.m_AttackedBy is not { } parent)
        {
            return;
        }

        FVector old = Pawn.Location;
        FRotator r = parent.Rotation;
        r.Pitch = 0;
        r.Roll = 0;
        parent.Rotation = r;
        r.GetAxes(out FVector x, out FVector y, out FVector z);
        FVector off = sb.vGrabOffset;
        FVector goal = parent.Location + x * off.X + y * off.Y + z * off.Z;
        Pawn.SetBase(parent.Base);
        MoveActor(Pawn, goal - Pawn.Location, r, out _);
        if (DeltaTime > 0f)
        {
            Pawn.Velocity = (Pawn.Location - old) * (1f / DeltaTime);
            if (Pawn.Velocity.Size() < 2f)
            {
                Pawn.Velocity = default;
            }
        }

        sb.fLastRootMotionTimeCalled = now;
    }
}
