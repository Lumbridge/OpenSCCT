using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The case a carried disk is delivered to, written from spec/objectives/disk-flag.md.
public partial class SFlagDropZone
{
    [Provenance(ProvenanceKind.Decoded, "disk-flag.md rule 4: the lid mesh")]
    public static readonly AssetRef LidMesh = new("SGameplayObjects.Extraction_SM.Malette_haut");

    public static new readonly StateTable ClassStates = new(SObjectiveTrigger.ClassStates, s_Initial.Instance, s_Inactive.Instance);

    public override StateTable GetStateTable() => ClassStates;

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Cover = Live(Cover);
    }

    /// <summary>The parent spawns the "Drop" entry; then the lid is spawned and the case blocks actors only.</summary>
    [Uc("SBase.SFlagDropZone.PostBeginPlay", Sha1 = "7e44dfb6c25b62669e04db3fde45041336b9d8f8")]
    [PortOf("uc:SBase.SFlagDropZone:PostBeginPlay", Spec = "spec/objectives/disk-flag.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        Rotation.GetAxes(out FVector x, out _, out _);
        Cover = ObjectiveNatives.Spawn<SBasicGamePlayObject>(this, this,
            Location + new FVector(0f, 0f, K.LidOffsetUp) - x * K.LidOffsetBack, Rotation);
        if (Cover is not null)
        {
            Cover.SetStaticMesh(LidMesh);
            Cover.SetDrawType(EDrawType.DT_StaticMesh);
            Cover.bHidden = false;
            Cover.SetCollision(true, true, true);
            Cover.SetPhysics(EPhysics.PHYS_Rotating);
            Cover.bMovable = true;
            Cover.bReplicateMovement = true;
            Cover.bRotateToDesired = true;
            Cover.DesiredRotation = Rotation;
            Cover.RotationRate = new FRotator(K.LidRotationRate, 0, 0);
        }

        SetCollision(true, true, false);
    }

    /// <summary>Opens the lid (pitch +5000, a quarter second at 20000 per second).</summary>
    [Uc("SBase.SFlagDropZone.Open", Sha1 = "07effffaffd310d86101b24ada8e75e3d2c11fb8")]
    [PortOf("uc:SBase.SFlagDropZone:Open", Spec = "spec/objectives/disk-flag.md#rules")]
    public void Open()
    {
        if (Cover is not null)
        {
            Cover.DesiredRotation = Rotation + new FRotator(K.LidOpenPitch, 0, 0);
        }
    }

    /// <summary>Closes the lid 1.5 s later.</summary>
    [Uc("SBase.SFlagDropZone.Close", Sha1 = "fc78ee034f4e424b14ce2fcca2527eb7390fe588")]
    [PortOf("uc:SBase.SFlagDropZone:Close", Spec = "spec/objectives/disk-flag.md#rules")]
    public void Close() => SetTimer(K.LidCloseDelay, false);

    [Uc("SBase.SFlagDropZone.Timer", Sha1 = "57b6220f155c17f9ea8d6fdbef311a848b0b8550")]
    [PortOf("uc:SBase.SFlagDropZone:Timer", Spec = "spec/objectives/disk-flag.md#rules")]
    protected override void Timer()
    {
        if (Cover is not null)
        {
            Cover.DesiredRotation = Rotation;
        }
    }

    /// <summary>True while a carried disk lists this zone.</summary>
    [Uc("SBase.SFlagDropZone.IsWaitingAFlag", Sha1 = "9a31b0945438e88a0435662a40896f59fe8f4ecd")]
    [PortOf("uc:SBase.SFlagDropZone:IsWaitingAFlag", Spec = "spec/objectives/disk-flag.md#rules")]
    public bool IsWaitingAFlag()
    {
        foreach (SFlag flag in XLevel.AllActors<SFlag>())
        {
            if (flag.FlagCarrier is null)
            {
                continue;
            }

            foreach (Actor? zone in flag.DropZone)
            {
                if (ReferenceEquals(zone, this))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
