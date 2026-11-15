using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Svm;

/// <summary>A Spy gadget whose effect and charge are committed together by the server.</summary>
public abstract class SpyDeployableGadget : SGadget
{
    protected override void SetDefaults()
    {
        base.SetDefaults();
        bActivatable = true;
    }

    public override void FullReload() { }

    protected bool HasCharge => Charge > 0 || PracticeRules.ResourcesInfinite(this);

    protected bool CanDeploy(SPlayerController pc) =>
        Role == ENetRole.ROLE_Authority && !bDeleteMe &&
        OwnerPawn is SPawnAttaque { Health: > 0, bDeleteMe: false, bStickyShocking: false, bSleeping: false, bLyingOnGround: false, m_AttackedBy: null } spy &&
        ReferenceEquals(pc.SBPawn, spy) && ReferenceEquals(spy.Controller, pc) &&
        ReferenceEquals(spy.SelectedGadget, this) && !pc.bIsInInventoryMode && pc.iDontUpdatePadInput == 0;

    protected void ShowMessage(SPlayerController pc, string message)
    {
        if (!GadgetNet.ToOwner(pc, new CombatCall(new Name("SpyGadgetMessage")) { Name1 = new Name(message) }))
            GadgetHud.AddMessageToAuxiliaryStack(pc, message, 2f);
    }

    protected static UcClassInfo GadgetClass<T>(string path) where T : SpyDeployableGadget, new() =>
        new(path, typeof(T), SGadget.UcMeta.Class, (UcClassFlags)0x36u, null, 0,
            static () => new T(), [], [], [], null);
}
