using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The use entry of a switch or hackable panel, written from spec/objectives/switch-hack.md.
public partial class STriggerInteraction
{
    public static new readonly StateTable ClassStates = new(SInteractObject.ClassStates,
        WaitingBeforeActivation.Instance, WaitingBeforeDesactivation.Instance, s_Maintain_Use.Instance, s_Hacking.Instance);

    public override StateTable GetStateTable() => ClassStates;

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        InstigatorTempo = Live(InstigatorTempo);
        OwnerInterrupteur = Live(OwnerInterrupteur);
    }

    [Uc("SBase.STriggerInteraction.PostBeginPlay", Sha1 = "03bc7f1750adc5a3871222cca50f625394f0b9cd")]
    [PortOf("uc:SBase.STriggerInteraction:PostBeginPlay", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void PostBeginPlay()
    {
        if (Owner is SInterrupteur owner)
        {
            bHackable = owner.iHackingTime != 0;
            bMaintain = owner.bMaintainsSwitch;
        }

        base.PostBeginPlay();
    }

    /// <summary>Server side of a switch use (rule 7): delay, hack, hold or fire, then the use sound.</summary>
    [Uc("SBase.STriggerInteraction.InitInteract", Sha1 = "a57cd4ab47bdfb74a30f36c0c1dc3f59dadfbcb8")]
    [PortOf("uc:SBase.STriggerInteraction:InitInteract", Spec = "spec/objectives/switch-hack.md#rules")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (Owner is SBasicGamePlayObject gpo)
        {
            InstigatorTempo = Instigator;
            var sw = Owner as SInterrupteur;
            if (gpo.iAttenteAvantActivation != 0)
            {
                ObjectiveNatives.MirrorState(this, K.WaitingBeforeActivation);
            }
            else if (sw is { iHackingTime: not 0 } && ObjectiveNatives.IsAlive(Instigator))
            {
                ObjectiveNatives.MirrorState(Instigator, K.s_Hack_GoTo);
                return;
            }
            else if (sw is { bMaintainsSwitch: true } && ObjectiveNatives.IsAlive(Instigator))
            {
                ObjectiveNatives.MirrorState(Instigator, K.s_Maintain_GoTo);
                return;
            }
            else
            {
                HandleEventInfo();
            }
        }
        else if (Owner is not null)
        {
            TriggerEvent(Owner.Event, Owner, Instigator.Pawn);
        }

        if (Owner is SInterrupteur { InteractionSound.IsNone: false } s)
        {
            s.PlaySound(s.InteractionSound, ESoundSlot.SLOT_Misc, s.SoundVolume / 255f, false, s.SoundRadius, s.SoundPitch / 64f);
        }
    }

    /// <summary>Server side of the hack or hold start (rule 11), sent by the enter states with sub-index 2.</summary>
    [Uc("SBase.STriggerInteraction.Interact", Sha1 = "5c4d36467880bd9c0d3f6eae46d8302c1bc12eaa")]
    [PortOf("uc:SBase.STriggerInteraction:Interact", Spec = "spec/objectives/switch-hack.md#rules")]
    protected internal override void Interact(SPlayerController Instigator, int SubInteraction)
    {
        var sw = Owner as SInterrupteur;
        if (sw is { InteractionSound.IsNone: false, iHackingTime: 0 })
        {
            sw.PlaySound(sw.InteractionSound, ESoundSlot.SLOT_Misc, sw.SoundVolume / 255f, false, sw.SoundRadius, sw.SoundPitch / 64f);
        }

        if (sw is { iHackingTime: not 0 } && SubInteraction == K.SubStart)
        {
            if (sw.sWarnHackingText.Length != 0)
            {
                ObjectiveMessages.BroadcastTeam(this, K.TeamMercs, K.MsgWarnHacking, sw);
            }

            ObjectiveMessages.Broadcast(this, K.MsgHackingInProgress);
            AddRemoveActorOnClientHud(Instigator, true);
            ObjectiveNatives.MirrorState(this, K.s_Hacking);
        }
        else if (sw is { bMaintainsSwitch: true } && SubInteraction == K.SubStart)
        {
            ObjectiveNatives.MirrorState(this, K.s_Maintain_Use);
        }
    }

    /// <summary>
    /// Multicast (flag 0x01000000): on every machine, the hacker's own HUD adds this entry to its draw list (only his
    /// machine has his HUD); a removal takes it off the local player's HUD.
    /// </summary>
    [Uc("SBase.STriggerInteraction.AddRemoveActorOnClientHud", Sha1 = "c0b3569ac1ffc7afc81d561199778ca5da5b9272")]
    [PortOf("uc:SBase.STriggerInteraction:AddRemoveActorOnClientHud", Spec = "spec/objectives/switch-hack.md#rules")]
    [PortOf("rep:SBase.STriggerInteraction.AddRemoveActorOnClientHud", Spec = "spec/objectives/messages.md#network")]
    public void AddRemoveActorOnClientHud(SPlayerController? Instigator, bool Add)
    {
        if (Add)
        {
            if (Instigator?.myHUD is SHud hud)
            {
                ObjectiveHud.AddRemoveActorOnHud(hud, this, true);
            }
        }
        else if (Level.LocalPlayerController?.myHUD is SHud local)
        {
            ObjectiveHud.AddRemoveActorOnHud(local, this, false);
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            ObjectiveNatives.Net(this).AddRemoveActorOnClientHud(this, Instigator, Add);
        }
    }

    /// <summary>
    /// Fires the switch's Event with the last user as instigator, by the switch's TriggerMethode, and applies
    /// bTriggerOnceOnly (expected contract, rule 21).
    /// </summary>
    [UcNative("ASTriggerInteraction::execHandleEventInfo", Exe = 0x10AF6650, Editor = 0x111BDBE0)]
    [PortOf("nat:SBase.STriggerInteraction.HandleEventInfo", Spec = "spec/objectives/switch-hack.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected from RE+ measurements: TriggerToggle triggers every use, TriggerControl alternates (bIsActive), once-only clears bUsable")]
    public void HandleEventInfo()
    {
        if (Owner is not SBasicGamePlayObject gpo)
        {
            return;
        }

        Pawn? instigator = InstigatorTempo?.Pawn;
        switch (gpo.TriggerMethode)
        {
            case TriggerType.TriggerControl:
                if (bIsActive)
                {
                    gpo.UntriggerEvent(gpo.Event, gpo, instigator);
                }
                else
                {
                    gpo.TriggerEvent(gpo.Event, gpo, instigator);
                }

                bIsActive = !bIsActive;
                break;
            case TriggerType.UnTriggerOnly:
                gpo.UntriggerEvent(gpo.Event, gpo, instigator);
                break;
            default:
                gpo.TriggerEvent(gpo.Event, gpo, instigator);
                break;
        }

        if (gpo.bTriggerOnceOnly)
        {
            bUsable = false;
        }
    }

    /// <summary>The base conditions plus: nobody is hacking or holding it.</summary>
    [UcNative("ASTriggerInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111BD780)]
    [PortOf("nat:SBase.STriggerInteraction.CanUse", Spec = "spec/objectives/switch-hack.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected: base conditions and not bInUse (spec open question 2)")]
    public override bool CanUse(SPlayerController? TheController) => !bInUse && base.CanUse(TheController);
}
