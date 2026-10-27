using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// Switches and hackable panels, written from spec/objectives/switch-hack.md.
public partial class SInterrupteur
{
    /// <summary>The switch's use entry, once spawned.</summary>
    public STriggerInteraction? TriggerInteraction => InteractionObj as STriggerInteraction;

    [Uc("SBase.SInterrupteur.ForceTriggerableEvenIfMoving", Sha1 = "fae745e128f7cfd7b363ed8fcdbb2127cb85320c")]
    [PortOf("uc:SBase.SInterrupteur:ForceTriggerableEvenIfMoving", Spec = "spec/objectives/switch-hack.md#data")]
    public override bool ForceTriggerableEvenIfMoving() => bMaintainsSwitch;

    /// <summary>Sticky gadgets do not attach to switches.</summary>
    [Uc("SBase.SInterrupteur.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SInterrupteur:AcceptStickableGPO", Spec = "spec/objectives/switch-hack.md#rules")]
    public override bool AcceptStickableGPO() => false;

    [Uc("SBase.SInterrupteur.PreBeginPlay", Sha1 = "e2ec1fd17e52a406fadf192ace44a71434bf5c6f")]
    [PortOf("uc:SBase.SInterrupteur:PreBeginPlay", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void PreBeginPlay()
    {
        base.PreBeginPlay();
        bInitialSecondaryObjective = bSecondaryObjective;
        bSecondaryObjective = false;
    }

    /// <summary>The parent spawns the STriggerInteraction; the switch then sets its on/off state.</summary>
    [Uc("SBase.SInterrupteur.PostBeginPlay", Sha1 = "95831edd31254127fa0ced4e1a4c3c80877a5441")]
    [PortOf("uc:SBase.SInterrupteur:PostBeginPlay", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (TriggerInteraction is { } entry)
        {
            entry.bIsActive = bInitialyActive;
        }
    }

    [Uc("SBase.SInterrupteur.PostNetBeginPlay", Sha1 = "6bad22395317b55297ba2a1b6b74629a2b0e693e")]
    [PortOf("uc:SBase.SInterrupteur:PostNetBeginPlay", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        if (InteractionObj is null)
        {
            return;
        }

        if (bInitialSecondaryObjective)
        {
            bSecondaryObjective = InteractionObj.bUsable;
        }

        if (TriggerInteraction is { } entry)
        {
            entry.OwnerInterrupteur = this;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            InteractionObj.iReusableTime = iReusableTime;
        }
    }

    /// <summary>An event toggles whether the switch can be used.</summary>
    [Uc("SBase.SInterrupteur.Trigger", Sha1 = "a768534700e8919e20ec7b8af78e6f31da50bd68")]
    [PortOf("uc:SBase.SInterrupteur:Trigger", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (InteractionObj is null)
        {
            return;
        }

        InteractionObj.bUsable = !InteractionObj.bUsable;
        if (bInitialSecondaryObjective)
        {
            bSecondaryObjective = InteractionObj.bUsable;
        }
    }

    [Uc("SBase.SInterrupteur.UnTrigger", Sha1 = "87762287040ca330b1ee32918ac709f65bd87fb4")]
    [PortOf("uc:SBase.SInterrupteur:UnTrigger", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        if (InteractionObj is null)
        {
            return;
        }

        InteractionObj.bUsable = false;
        if (bInitialSecondaryObjective)
        {
            bSecondaryObjective = false;
        }
    }

    /// <summary>
    /// The hack or hold spot in front of the switch (s_Hack_GoTo rule 9): <c>Location + X * UseRelativeLocation.X +
    /// Y * UseRelativeLocation.Y</c> in the switch's axes, at the pawn's height (27.5 higher when crouched), facing
    /// the switch's yaw plus <c>UseRelativeRotation.Yaw</c>.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_Hack_GoTo.BeginState / s_Maintain_GoTo.BeginState use spot")]
    public void GetUseSpot(Pawn pawn, out FVector Spot, out FVector Facing)
    {
        Rotation.GetAxes(out FVector x, out FVector y, out _);
        Spot = Location + x * UseRelativeLocation.X + y * UseRelativeLocation.Y;
        Spot.Z = pawn.Location.Z + (pawn.bIsCrouched ? K.CrouchedUseSpotLift : 0f);
        Facing = (Rotation + new FRotator(0, UseRelativeRotation.Yaw, 0)).Vector();
    }
}
