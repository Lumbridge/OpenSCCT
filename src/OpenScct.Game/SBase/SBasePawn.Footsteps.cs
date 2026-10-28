using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn footsteps: the walk-cycle notifies PlayFootStepLeft and PlayFootStepRight and the footstep sounds
// they play. Written from spec/native/audio.md rules 27 and 28. The sounds are SimEvents (PlaySound); the surface the
// switch sounds pick comes from FootPrint's SetSwitch (IMovementHooks.FootPrint, which runs FootPrint below), else
// from the sound system's own trace below the sound (rule 13).
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

    /// <summary>How far below the foot bone FootPrint looks for the floor.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.FootPrint: Trace to footcoord.Origin + (0, 0, -100)")]
    public const float FootPrintTraceDepth = 100f;

    /// <summary>Seconds a footprint stays drawn.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.FootPrint: AbandonProjector(20.0)")]
    public const float FootPrintLifetime = 20f;

    /// <summary>Straight down: the direction the footprint's projection is cast.</summary>
    [Provenance(ProvenanceKind.Estimated, "GetBoneRotation of the foot bone replaced by a downward rotation under the pawn's yaw")]
    private const int FootPrintPitchDown = -16384;

    /// <summary>
    /// The footprint of one step: a line from the foot bone 100 uu down; the floor's surface type goes to the sound
    /// system (SetSwitch, the next footstep's switch sound), sand, water, snow and fake ceilings make their effects,
    /// and dirty feet leave a print (an abandoned SFootPrint projection) that fades out over the next steps: the sand,
    /// water (water and snow) or phosphor (no surface effect) texture, in StepColor with an alpha of
    /// 1 + StepColor.A x FootDirtyness, the right foot's texture upside down.
    /// </summary>
    [Uc("SBase.SBasePawn.FootPrint")]
    [PortOf("uc:SBase.SBasePawn:FootPrint", Spec = "spec/native/audio.md#footsteps-sbasepawn")]
    public virtual void FootPrint(bool bTrueIfLeftFoot)
    {
        if (XLevel is null || MovementNatives.Driver(this) is not { } driver)
        {
            return;
        }

        Name bone = bTrueIfLeftFoot ? LeftFootBone : RightFootBone;
        FVector foot = CombatServices.Ports(this).BoneCoords(this, bone).Origin;
        var flags = (ETraceFlags)MovementK.TraceScriptWorld;
        if (!driver.Collision.LineCheck(out FCheckResult hit, this, foot + new FVector(0f, 0f, -FootPrintTraceDepth), foot,
                default, flags) || hit.Material < 0)
        {
            return;
        }

        var surface = (ESurfaceType)hit.SurfaceType;
        int printType = 0;
        FVector at = hit.Location + hit.Normal;
        SetSwitch(hit.SurfaceType);
        switch (surface)
        {
            case ESurfaceType.SURFACE_Sand:
                SetFootDirt(1f, 1f, 140, 140, 140, 80);
                printType = 1;
                XLevel.SpawnActor(new SFootPrintEffect(), at, Rotation, this);
                break;
            case ESurfaceType.SURFACE_MediumWater:
                SetFootDirt(1f, 0.05f, 0, 0, 0, 170);
                printType = 2;
                XLevel.SpawnActor(new SWaterFootStepEffect(), at, Rotation, this);
                break;
            case ESurfaceType.SURFACE_Snow:
                SetFootDirt(1f, 1f, 20, 25, 40, 80);
                printType = 2;
                break;
            case ESurfaceType.SURFACE_DeepWater:
                SetFootDirt(1f, 0.02f, 0, 0, 0, 170);
                printType = 2;
                if (driver.Collision.LineCheck(out FCheckResult water, this, Location + new FVector(0f, 0f, -100f),
                        Location + new FVector(0f, 0f, 50f), default, flags))
                {
                    XLevel.SpawnActor(new SSparkWater(), water.Location + water.Normal, Rotation, this);
                }

                break;
            case ESurfaceType.SURFACE_FakeCeiling:
                if (Velocity.Size() > CeilingDustMinSpeed)
                {
                    XLevel.SpawnActor(new SDustEmitter(), at, Rotation, this);
                }

                break;
        }

        if (FootDirtyness > 0f && surface != ESurfaceType.SURFACE_MediumWater && surface != ESurfaceType.SURFACE_DeepWater)
        {
            SFootPrint? print = XLevel.SpawnActor(new SFootPrint(), hit.Location + (hit.Normal * 10f),
                new FRotator(FootPrintPitchDown, Rotation.Yaw, 0), this);
            if (print is not null)
            {
                if (!bTrueIfLeftFoot)
                {
                    print.UseRightFoot();
                }

                print.SetStepTexture(printType switch
                {
                    1 => print.FootStepSand,
                    2 => print.FootStepWater,
                    _ => print.FootStepPhospho,
                });
                FColor tint = StepColor;
                tint.A = (byte)(1f + (StepColor.A * FootDirtyness));
                print.StepTint = tint;
                print.AttachProjector();
                print.AbandonProjector(FootPrintLifetime);
                print.Destroy();
            }
        }

        FootDirtyness -= DecreaseFootDirtyness;
    }

    private void SetFootDirt(float dirt, float decrease, byte r, byte g, byte b, byte a)
    {
        FootDirtyness = dirt;
        DecreaseFootDirtyness = decrease;
        StepColor.R = r;
        StepColor.G = g;
        StepColor.B = b;
        StepColor.A = a;
    }
}
