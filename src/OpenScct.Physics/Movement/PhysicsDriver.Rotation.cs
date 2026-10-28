using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    /// <summary>
    /// The rotation step of AActor::performPhysics for a non-pawn actor: unless it is interpolating, an actor with a
    /// RotationRate (any axis, low 16 bits) turns by <see cref="physicsRotation"/>. This is what spins a PHYS_Rotating
    /// actor (a map mover with bFixedRotationDir, such as a carousel), turns a lid with bRotateToDesired, and spins
    /// falling debris.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10a2b130: physicsRotation when bInterpolating (0x2f0 bit 0x20000000) is clear and RotationRate.Pitch, Yaw or Roll & 0xffff is not 0")]
    [Provenance(ProvenanceKind.Estimated, "The original skips a PHYS_Rotating actor not rendered in the last second (Level.TimeSeconds - LastRenderTime > 1); the sim has no render time, so it always turns (a dedicated server's copy then turns too)")]
    private void ActorRotation(Actor actor, float DeltaSeconds)
    {
        if (actor.bDeleteMe || actor.bInterpolating)
        {
            return;
        }

        FRotator rate = actor.RotationRate;
        if ((rate.Pitch & 0xffff) != 0 || (rate.Yaw & 0xffff) != 0 || (rate.Roll & 0xffff) != 0)
        {
            physicsRotation(actor, DeltaSeconds);
        }
    }

    /// <summary>
    /// AActor::physicsRotation: with bRotateToDesired or bFixedRotationDir (and with bRotateToDesired only while
    /// Rotation is not DesiredRotation), each axis turns by the truncated RotationRate x dt through
    /// <see cref="fixedTurn"/> (yaw, then pitch, then roll); a changed rotation is applied by a zero-delta MoveActor,
    /// which an encroached pose can refuse.
    /// </summary>
    [UcNative("AActor::physicsRotation", Exe = 0x10a2bb20, Editor = 0x1117c140)]
    [PortOf("eng:AActor::physicsRotation", Spec = "spec/native/natives2-movement.md#2-aactorphysicsrotationdeltatime")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a2bb20: the rate per axis truncated (_ftol 0x10b83aa0); an axis already at DesiredRotation is skipped under bRotateToDesired; MoveActor(zero, NewRotation)")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a2bb20 calls EndedRotation when bRotateToDesired reaches DesiredRotation; no class of this build implements it, so it is not sent")]
    public void physicsRotation(Actor actor, float deltaTime)
    {
        System.ArgumentNullException.ThrowIfNull(actor);
        bool toDesired = actor.bRotateToDesired;
        if (!toDesired && !actor.bFixedRotationDir)
        {
            return;
        }

        FRotator rotation = actor.Rotation;
        FRotator desired = actor.DesiredRotation;
        if (toDesired && rotation == desired)
        {
            return;
        }

        int pitchStep = (int)(actor.RotationRate.Pitch * deltaTime);
        int yawStep = (int)(actor.RotationRate.Yaw * deltaTime);
        int rollStep = (int)(actor.RotationRate.Roll * deltaTime);
        FRotator turned = rotation;
        if (yawStep != 0 && (!toDesired || desired.Yaw != turned.Yaw))
        {
            turned.Yaw = fixedTurn(actor, turned.Yaw, desired.Yaw, yawStep);
        }

        if (pitchStep != 0 && (!toDesired || desired.Pitch != turned.Pitch))
        {
            turned.Pitch = fixedTurn(actor, turned.Pitch, desired.Pitch, pitchStep);
        }

        if (rollStep != 0 && (!toDesired || desired.Roll != turned.Roll))
        {
            turned.Roll = fixedTurn(actor, turned.Roll, desired.Roll, rollStep);
        }

        if (turned != rotation)
        {
            MoveActor(actor, FVector.Zero, turned, out _);
        }
    }

    /// <summary>
    /// AActor::fixedTurn: one axis (0..65535) turned by <paramref name="rate"/>. bFixedRotationDir alone spins by the
    /// rate; with bRotateToDesired too it turns the rate's way, stopping at <paramref name="desired"/>; bRotateToDesired
    /// alone turns the short way by at most |rate|; otherwise the axis stays.
    /// </summary>
    [UcNative("AActor::fixedTurn", Exe = 0x10a2b210)]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a2b210 (current, desired and rate in registers, the actor's 0x2f0 bits 0x08000000 bFixedRotationDir and 0x10000000 bRotateToDesired)")]
    internal static int fixedTurn(Actor actor, int current, int desired, int rate)
    {
        int c = current & 0xffff;
        if (rate == 0)
        {
            return c;
        }

        int d = desired & 0xffff;
        if (actor.bFixedRotationDir)
        {
            if (!actor.bRotateToDesired)
            {
                return (c + rate) & 0xffff;
            }

            int step;
            if (rate > 0)
            {
                int ahead = (d < c ? d + 0x10000 : d) - c;
                step = rate <= ahead ? rate : ahead;
            }
            else
            {
                int behind = d - (c < d ? c + 0x10000 : c);
                step = behind <= rate ? rate : behind;
            }

            return (c + step) & 0xffff;
        }

        if (!actor.bRotateToDesired)
        {
            return c;
        }

        int magnitude = rate < 0 ? -rate : rate;
        if (d < c)
        {
            int back = c - d;
            if (back > 0x7fff)
            {
                int forward = d - c + 0x10000;
                return (c + (magnitude < forward ? magnitude : forward)) & 0xffff;
            }

            return (c - (back <= magnitude ? back : magnitude)) & 0xffff;
        }

        int up = d - c;
        if (up < 0x8000)
        {
            return (c + (up <= magnitude ? up : magnitude)) & 0xffff;
        }

        int down = c - d + 0x10000;
        return (c - (down <= magnitude ? down : magnitude)) & 0xffff;
    }
}
