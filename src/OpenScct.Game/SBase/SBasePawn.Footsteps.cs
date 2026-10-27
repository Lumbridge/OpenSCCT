using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn footsteps: the walk-cycle notifies PlayFootStepLeft and PlayFootStepRight and the footstep sounds
// they play. Written from spec/native/audio.md rules 27 and 28. The sounds are SimEvents (PlaySound); the surface the
// switch sounds pick comes from FootPrint's SetSwitch (IMovementHooks.FootPrint), else from the sound system's own
// trace below the sound (rule 13).
public partial class SBasePawn
{
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.PlayFootStepSound: the footstep and gear sounds")]
    private static readonly AssetRef FootCrouchSound = new("Sound_Persos.FootSteps.SW_FootCrouch");

    private static readonly AssetRef FootWalkSound = new("Sound_Persos.FootSteps.SW_FootWalk");
    private static readonly AssetRef FootRunSound = new("Sound_Persos.FootSteps.SW_FootRun");
    private static readonly AssetRef SpyGearRunSound = new("Sound_Persos.vetements_materiel.RA_matos_Run");
    private static readonly AssetRef SpyBreathSound = new("Sound_Persos.onomatopees.RA_breath");
    private static readonly AssetRef MercGearRunSound = new("Sound_Persos.vetements_materiel.RA_matos_Run_Def");
    private static readonly AssetRef MercBreathSound = new("Sound_Persos.onomatopees.RA_breath_Def");
    private static readonly Name LeftFootBone = new("B L Foot");
    private static readonly Name RightFootBone = new("B R Foot");
    private static readonly Name GearBone = new("B Spine2");

    /// <summary>Speed (uu/s) below which a standing step is a walk.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.PlayFootStepSound: speed < 250 plays SW_FootWalk")]
    public const int FootWalkSpeedLimit = 250;

    /// <summary>Radius of the running gear sound.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.PlayFootStepSound: RA_matos_Run radius 250")]
    public const float FootGearRadius = 250f;

    /// <summary>Radius of the breath after a run.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.PlayFootStepSound: RA_breath radius 10")]
    public const float FootBreathRadius = 10f;

    /// <summary>Whole seconds of walking or standing before a run step breathes again.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.PlayFootStepSound: iLastWalkTime + 4 <= TimeSeconds")]
    public const int FootBreathDelay = 4;

    /// <summary>The right-foot notify of the walk cycles: footprint (and surface switch), then the step sound.</summary>
    [Uc("SBase.SBasePawn.PlayFootStepRight", Sha1 = "de4c95e318b721f11ef51435fbc4fd78b5ea5a9f")]
    [PortOf("uc:SBase.SBasePawn:PlayFootStepRight", Spec = "spec/native/audio.md#footsteps-sbasepawn")]
    public virtual void PlayFootStepRight()
    {
        MovementService.HooksOf(this).FootPrint(this, false);
        PlayFootStepSound(false);
    }

    /// <summary>The left-foot notify of the walk cycles.</summary>
    [Uc("SBase.SBasePawn.PlayFootStepLeft", Sha1 = "516be1a5b07f990a9d0f5ad26efe6a0281516ad8")]
    [PortOf("uc:SBase.SBasePawn:PlayFootStepLeft", Spec = "spec/native/audio.md#footsteps-sbasepawn")]
    public virtual void PlayFootStepLeft()
    {
        MovementService.HooksOf(this).FootPrint(this, true);
        PlayFootStepSound(true);
    }

    /// <summary>
    /// The step sound for the pawn's speed and stance (crouch, walk or run), plus the run gear and breath sounds.
    /// The spy's noise notification to the local controller (ClientNotifyNoise, the HUD noise meter) is not ported.
    /// </summary>
    [Uc("SBase.SBasePawn.PlayFootStepSound", Sha1 = "fc62d4b6777b1bccb47434a83d610af51e4402cd")]
    [PortOf("uc:SBase.SBasePawn:PlayFootStepSound", Spec = "spec/native/audio.md#footsteps-sbasepawn")]
    public virtual void PlayFootStepSound(bool bTrueIfLeftFoot)
    {
        if (Physics == EPhysics.PHYS_Falling || XLevel is null)
        {
            return;
        }

        Name bone = bTrueIfLeftFoot ? LeftFootBone : RightFootBone;
        FVector horizontal = new(Velocity.X, Velocity.Y, 0f);
        int speed = (int)horizontal.Size();
        if (ReferenceEquals(Level.GetLocalPlayerController()?.Pawn, this))
        {
            FootStepsVolume = LocalFootStepsVolume;
        }

        float stepRadius = speed / (RunningSpeed / 200f);
        int now = (int)Level.TimeSeconds;
        if (bIsCrouched)
        {
            if (speed > RunningSpeed / 4f)
            {
                PlaySound(FootCrouchSound, ESoundSlot.SLOT_Interact, FootStepsVolume * speed / (RunningSpeed / 2f), false,
                    stepRadius, null, true, bone);
            }

            iLastWalkTime = now;
        }
        else if (speed < FootWalkSpeedLimit)
        {
            PlaySound(FootWalkSound, ESoundSlot.SLOT_Interact, FootStepsVolume * speed / RunningSpeed, false, stepRadius,
                null, true, bone);
            iLastWalkTime = now;
        }
        else
        {
            PlaySound(FootRunSound, ESoundSlot.SLOT_Interact, FootStepsVolume * speed / RunningSpeed, false, stepRadius,
                null, true, bone);
            bool spy = this is SPawnAttaque;
            PlaySound(spy ? SpyGearRunSound : MercGearRunSound, ESoundSlot.SLOT_Misc, FootStepsVolume * 1f, false,
                FootGearRadius, null, true, GearBone);
            if (iLastWalkTime + FootBreathDelay <= now)
            {
                PlaySound(spy ? SpyBreathSound : MercBreathSound, ESoundSlot.SLOT_Misc, FootStepsVolume * 1f, false,
                    FootBreathRadius, null, true, HEADBONE);
            }
        }
    }
}
