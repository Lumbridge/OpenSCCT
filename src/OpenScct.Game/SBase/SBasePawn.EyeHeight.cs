using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The eye height of a player's pawn (spec/movement/pawn.md "Eye position"): Engine.Pawn.UpdateEyeHeight, which the
// pawn's post-tick runs while bUpdateEyeheight (set when a player possesses the pawn, on the server and the owning
// client), and the controllers' AdjustView it ends with. The eye is where the merc's camera and his rifle's trace
// start, so crouching lowers both.
public partial class SBasePawn
{
    /// <summary>The eye may rise this far above the collision top when nothing is over the head.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.Pawn.UpdateEyeHeight: CollisionHeight + 34.0")]
    public const float EyeHeadroom = 34f;

    /// <summary>The ceiling check reaches this much further, and the eye keeps this far under a hit.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.Pawn.UpdateEyeHeight: trace to CollisionHeight + 34 + 14, MaxEyeHeight = HitLocation.Z - Location.Z - 14")]
    public const float EyeCeilingMargin = 14f;

    /// <summary>The smoothing rate toward BaseEyeHeight, per second.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.Pawn.UpdateEyeHeight: smooth = FMin(1, 10 DeltaTime / TimeDilation)")]
    public const float EyeSmoothRate = 10f;

    /// <summary>
    /// UpdateEyeHeight: no controller, no eye; a torn-off pawn stops updating. The eye may reach CollisionHeight + 34,
    /// or 14 under whatever a trace above the head hits. Walking (not just landed) the eye keeps its world height
    /// through this tick's climb or drop (EyeHeight - Location.Z + OldZ) and eases toward BaseEyeHeight by
    /// min(1, 10 dt / TimeDilation), clamped to -CollisionHeight / 2 .. the maximum; otherwise it eases without the
    /// climb and bJustLanded clears. Then the controller's AdjustView.
    /// </summary>
    [Uc("Engine.Pawn.UpdateEyeHeight", Sha1 = "16117045d487b723062806eb239df191aa246ba3")]
    [PortOf("uc:Engine.Pawn:UpdateEyeHeight", Spec = "spec/movement/pawn.md#eye-position")]
    public virtual void UpdateEyeHeight(float DeltaTime)
    {
        if (Controller is not { } c)
        {
            EyeHeight = 0f;
            return;
        }

        if (bTearOff)
        {
            EyeHeight = 0f;
            bUpdateEyeheight = false;
            return;
        }

        FVector top = Location + new FVector(0f, 0f, CollisionHeight + EyeHeadroom + EyeCeilingMargin);
        FVector head = Location + new FVector(0f, 0f, CollisionHeight);
        Actor? hit = MovementNatives.Trace(this, out FVector hitLocation, out _, top, head, true);
        float maxEyeHeight = hit is null ? CollisionHeight + EyeHeadroom : hitLocation.Z - Location.Z - EyeCeilingMargin;
        float smooth = SvmMath.Min(1f, EyeSmoothRate * DeltaTime / Level.TimeDilation);
        if (WantsSmoothedView(c))
        {
            float eye = ((EyeHeight - Location.Z + OldZ) * (1f - smooth)) + (BaseEyeHeight * smooth);
            EyeHeight = SvmMath.Clamp(eye, -0.5f * CollisionHeight, maxEyeHeight);
        }
        else
        {
            bJustLanded = false;
            EyeHeight = SvmMath.Min((EyeHeight * (1f - smooth)) + (BaseEyeHeight * smooth), maxEyeHeight);
        }

        AdjustView(c, DeltaTime);
    }

    /// <summary>Controller.WantsSmoothedView: the pawn walks and has not just landed.</summary>
    [Uc("Engine.Controller.WantsSmoothedView", Sha1 = "ceee9a39f3432a39e3ab3d0b697fbd4f3352b40b")]
    [PortOf("uc:Engine.Controller:WantsSmoothedView", Spec = "spec/movement/pawn.md#eye-position")]
    public static bool WantsSmoothedView(Controller C) =>
        C.Pawn is { } p && p.Physics == EPhysics.PHYS_Walking && !p.bJustLanded;

    /// <summary>
    /// AdjustView. A player's controller (PlayerController.AdjustView) eases FovAngle toward DesiredFOV: by at least 7
    /// degrees, or 0.9 dt of the gap, snapping once within 10. Any other controller (Controller.AdjustView) stops the
    /// pawn's eye updates and sets the eye to its base unless a player views the pawn.
    /// </summary>
    [Uc("Engine.PlayerController.AdjustView", Sha1 = "86d9f911cc7b2081fba9514d9ad27d35b0399920")]
    [PortOf("uc:Engine.PlayerController:AdjustView", Spec = "spec/movement/pawn.md#eye-position")]
    [Uc("Engine.Controller.AdjustView", Sha1 = "b2aa99293e0d5bb6aa634f9ef5d9dd3073cf3575")]
    [PortOf("uc:Engine.Controller:AdjustView", Spec = "spec/movement/pawn.md#eye-position")]
    public static void AdjustView(Controller C, float DeltaTime)
    {
        if (C is PlayerController pc)
        {
            if (pc.FovAngle == pc.DesiredFOV)
            {
                return;
            }

            float gap = pc.FovAngle - pc.DesiredFOV;
            pc.FovAngle = pc.FovAngle > pc.DesiredFOV
                ? pc.FovAngle - SvmMath.Max(EyeFovStep, EyeFovShare * DeltaTime * gap)
                : pc.FovAngle - SvmMath.Min(-EyeFovStep, EyeFovShare * DeltaTime * gap);
            if (SvmMath.Abs(pc.FovAngle - pc.DesiredFOV) <= EyeFovSnap)
            {
                pc.FovAngle = pc.DesiredFOV;
            }

            return;
        }

        if (C.Pawn is not { } pawn)
        {
            return;
        }

        foreach (PlayerController viewer in C.XLevel.AllActors<PlayerController>())
        {
            if (!viewer.bDeleteMe && ReferenceEquals(viewer.ViewTarget, pawn))
            {
                return;
            }
        }

        pawn.bUpdateEyeheight = false;
        pawn.EyeHeight = pawn.BaseEyeHeight;
    }

    /// <summary>The smallest FOV step of PlayerController.AdjustView, in degrees.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.PlayerController.AdjustView: FMax(7.0, ...) / FMin(-7.0, ...)")]
    public const float EyeFovStep = 7f;

    /// <summary>The share of the FOV gap closed per second by PlayerController.AdjustView.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.PlayerController.AdjustView: 0.9 * DeltaTime * (FovAngle - DesiredFOV)")]
    public const float EyeFovShare = 0.9f;

    /// <summary>Within this many degrees PlayerController.AdjustView snaps to DesiredFOV.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.PlayerController.AdjustView: Abs(FovAngle - DesiredFOV) <= 10")]
    public const float EyeFovSnap = 10f;
}
