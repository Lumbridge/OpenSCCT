using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The "Drop" entry of a drop zone, written from spec/objectives/disk-flag.md.
public partial class SFlagDropZoneInteraction
{
    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        MyOwner = Live(MyOwner);
    }

    /// <summary>The generic sizing, then a 100 x 100 collision cylinder (ItRadius and ItHeight keep the generic values).</summary>
    [Uc("SBase.SFlagDropZoneInteraction.PostBeginPlay", Sha1 = "cb172a24398de643f4850ed8289b002621d348ac")]
    [PortOf("uc:SBase.SFlagDropZoneInteraction:PostBeginPlay", Spec = "spec/objectives/disk-flag.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        ObjectiveNatives.SetCollisionSize(this, K.DropZoneInteractionSize, K.DropZoneInteractionSize);
    }

    /// <summary>The parent re-applies ItRadius x ItHeight (the zone's instance size on the server); MyOwner replicates the owner.</summary>
    [Uc("SBase.SFlagDropZoneInteraction.PostNetBeginPlay", Sha1 = "938c2563023472653122561a7e7f10296183d8bb")]
    [PortOf("uc:SBase.SFlagDropZoneInteraction:PostNetBeginPlay", Spec = "spec/objectives/disk-flag.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        MyOwner = Owner;
    }

    /// <summary>
    /// Server side of "Drop" (rule 25): a living carrier whose disk this zone takes walks to the zone, and the lid
    /// closes 1.5 s later; anyone else gets the generic refusal.
    /// </summary>
    [Uc("SBase.SFlagDropZoneInteraction.InitInteract", Sha1 = "2b705d9c17a53f48fdff786fc842cb7427f08a3b")]
    [PortOf("uc:SBase.SFlagDropZoneInteraction:InitInteract", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (ObjectiveNatives.IsAlive(Instigator) && Instigator.Pawn is SBasePawn { CarriedFlag: { } flag } &&
            flag.CheckDropZone(Owner))
        {
            ObjectiveNatives.MirrorState(Instigator, K.s_FlagAndDropZone_GoTo);
            (Owner as SFlagDropZone)?.Close();
            return;
        }

        base.InitInteract(Instigator, SubInteraction);
    }

    /// <summary>The base conditions plus: the player carries a disk that lists this zone.</summary>
    [UcNative("ASFlagDropZoneInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111BCFB0)]
    [PortOf("nat:SBase.SFlagDropZoneInteraction.CanUse", Spec = "spec/objectives/disk-flag.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected: bUsable, team, and carrying a disk for this zone (spec open question 5)")]
    public override bool CanUse(SPlayerController? TheController)
    {
        if (!base.CanUse(TheController) || TheController!.Pawn is not SBasePawn { CarriedFlag: { } flag })
        {
            return false;
        }

        foreach (Actor? zone in flag.DropZone)
        {
            if (ReferenceEquals(zone, Owner))
            {
                return true;
            }
        }

        return false;
    }
}
