using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The "Use" entry of a terminal, written from spec/objectives/terminal-hack.md.
public partial class SComputerInteraction
{
    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        ObjectiveTrigger = Live(ObjectiveTrigger);
    }

    [Uc("SBase.SComputerInteraction.PostBeginPlay", Sha1 = "22f3b4553588099a87c5ad9c8c2b1de7c1b9b4b0")]
    [PortOf("uc:SBase.SComputerInteraction:PostBeginPlay", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        // Localized inherited defaults can differ in the installed content even when code generation saw them equal.
        Description = UcText.Get("SBase.SComputerInteraction", nameof(Description), Description);
        ObjectiveTrigger = Owner as SObjectiveTrigger;
    }

    /// <summary>
    /// Server side of "Use" (rule 7): an active, unlocked terminal records the taker and starts the hack flow;
    /// otherwise a player still in the menu is sent back.
    /// </summary>
    [Uc("SBase.SComputerInteraction.InitInteract", Sha1 = "333db4603217257c6e085379e337c192251f8e2f")]
    [PortOf("uc:SBase.SComputerInteraction:InitInteract", Spec = "spec/objectives/terminal-hack.md#rules")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        SObjectiveTrigger? trigger = ObjectiveTrigger;
        if (trigger is { bActive: true, bLocked: false } && !trigger.IsInState(K.s_RestoreAfterAlarm))
        {
            trigger.ControllerWhoTakesIt = Instigator;
            if (trigger.CaptureTime == 0)
            {
                trigger.CaptureTime = 1;
            }

            TerminalHackRules.UseComputer(Instigator);
        }
        else if (Instigator.IsInState(K.s_MenuInteraction) && ObjectiveNatives.IsAlive(Instigator))
        {
            ObjectiveNatives.MirrorState(Instigator, Instigator.PreviousState, Name.None, true);
        }
    }

    /// <summary>The base conditions plus an active, unlocked terminal.</summary>
    [UcNative("ASComputerInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111BE210)]
    [PortOf("nat:SBase.SComputerInteraction.CanUse", Spec = "spec/objectives/terminal-hack.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected: base conditions, terminal bActive and not bLocked (spec open question 2)")]
    public override bool CanUse(SPlayerController? TheController) =>
        ObjectiveTrigger is { bActive: true, bLocked: false } && base.CanUse(TheController);
}
