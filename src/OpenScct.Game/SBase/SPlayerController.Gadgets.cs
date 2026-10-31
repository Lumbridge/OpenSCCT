using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController calls of the default gadgets that are not view states: the chaff's HUD perturbation and
// goggle reset, the mine refill, the backpack share, and the gadget part of PlayerTick. Written from
// spec/equipment/chaff-grenade.md rules 8 and 9, mine-launcher.md rule 25, gadget-natives.md rule 7 and
// sticky-cam.md rule 31.
public partial class SPlayerController
{
    private static readonly Name CNAME_SetHudPerturbation = new("SetHudPerturbation");
    private static readonly Name CNAME_ClientChaffed = new("ClientChaffed");
    private static readonly Name CNAME_ReloadMine = new("ReloadMine");
    private static readonly Name CNAME_TellClientToAddGadgetCharge = new("TellClientToAddGadgetCharge");
    private static readonly Name CNAME_AskServerGiveObjectToPawn = new("AskServerGiveObjectToPawn");
    private static readonly ClassRef CREF_SMineLaser = new("SBase.SMineLaser");
    private static readonly ClassRef CREF_SMineProx = new("SBase.SMineProx");
    private static readonly ClassRef CREF_SMinePoison = new("SBase.SMinePoison");
    private static readonly ClassRef CREF_SGadMultiMinesLauncher = new("SBase.SGadMultiMinesLauncher");
    private static readonly ClassRef CREF_SGadMultiPresDetectorLauncher = new("SBase.SGadMultiPresDetectorLauncher");

    /// <summary>A state's version of ClientChaffed (the camera network view leaves).</summary>
    public interface IClientChaffed
    {
        void ClientChaffed(SPlayerController self);
    }

    /// <summary>The HUD perturbation for <paramref name="Lenght"/> seconds (server to owner, unreliable).</summary>
    [Uc("SBase.SPlayerController.SetHudPerturbation", Sha1 = "559f7bed5ed1fa1d7caea2542ddb2906b727d2ce")]
    [PortOf("uc:SBase.SPlayerController:SetHudPerturbation", Spec = "spec/equipment/chaff-grenade.md#rules")]
    public void SetHudPerturbation(float Lenght)
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_SetHudPerturbation) { Float1 = Lenght }))
        {
            return;
        }

        SetHudPerturbationLength(Lenght);
    }

    /// <summary>ClientChaffed through the current state, or the class version; sent to a remote owner.</summary>
    public void eventClientChaffed()
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_ClientChaffed)))
        {
            return;
        }

        if (StateNode is IClientChaffed s)
        {
            s.ClientChaffed(this);
        }
        else if (StateRunsGlobal("ClientChaffed", out _))
        {
            ClientChaffed();
        }
    }

    /// <summary>The chaff field resets the goggles to the normal view at once (server to owner, reliable).</summary>
    [Uc("SBase.SPlayerController.ClientChaffed", Sha1 = "3bb80f52c7738fa6bac3f4776869a6e259ea9755")]
    [PortOf("uc:SBase.SPlayerController:ClientChaffed", Spec = "spec/equipment/chaff-grenade.md#rules")]
    public void ClientChaffed() => Goggle?.ResetVisionMode(true);

    /// <summary>
    /// A mine picked up (server to owner, reliable): for a proximity, laser or poison mine, the first plain mine
    /// launcher of the first 4 selectable gadgets missing a charge gets one back and the "mine off" message.
    /// </summary>
    [Uc("SBase.SPlayerController.ReloadMine", Sha1 = "6d3f403ff8c036712fa790b6f2b0c12ffebf3f9f")]
    [PortOf("uc:SBase.SPlayerController:ReloadMine", Spec = "spec/equipment/mine-launcher.md#removing-and-refilling")]
    [Provenance(ProvenanceKind.Estimated, "The spy trap and presence detector branch (not default gadgets) is not ported")]
    public void ReloadMine(Actor? Owner)
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_ReloadMine) { Actor1 = Owner }))
        {
            return;
        }

        if (Owner is null || SBPawn is not { } pawn ||
            !(PawnInventory.IsA(Owner, CREF_SMineLaser) || PawnInventory.IsA(Owner, CREF_SMineProx) || PawnInventory.IsA(Owner, CREF_SMinePoison)))
        {
            return;
        }

        SGadget? g = pawn.MyGadget?.SelectNext();
        for (int i = 0; i < K.GadgetSlots && g is not null; i++)
        {
            if (PawnInventory.IsA(g, CREF_SGadMultiMinesLauncher) && !PawnInventory.IsA(g, CREF_SGadMultiPresDetectorLauncher) &&
                g.Charge < g.ClassDefaults.Charge)
            {
                GadgetHud.AddMessageToAuxiliaryStack(this, MineOff, K.MineOffMessageTime);
                g.AddCharge();
                return;
            }

            g = g.MyGadget?.SelectNext();
        }
    }

    /// <summary>
    /// The Give entry of the Use menu on release (interaction-system.md rule 14a, input machine): a "gave" message,
    /// one charge off the selected gadget and the server call that gives it to the mate. The message reads its word
    /// from menu slot 0 as the original does, which is the backpack entry, so the word is empty.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.PlayerMove", Spec = "spec/objectives/interaction-system.md#rules")]
    public void GiveSelectedGadget(SShareObjectInteraction Share)
    {
        if (Share.aActorToInteractWith is not { } mate || SBPawn?.SelectedGadget is not { } gadget)
        {
            return;
        }

        string gave = (IManager?.Interactions[0] as SShareObjectInteraction)?.tGave ?? "";
        GadgetHud.AddMessageToStack(this, $"1 {gadget.ItemName} {gave}", K.ShareMessageTime);
        gadget.DecCharge();
        AskServerGiveObjectToPawn(gadget, mate, 1);
    }

    /// <summary>Owner to server: the mate's own machine adds the charges (TellClientToAddGadgetCharge).</summary>
    [Uc("SBase.SPlayerController.AskServerGiveObjectToPawn", Sha1 = "1587156c5f2999fe0ba10f8948ba803c25ee2877")]
    [PortOf("uc:SBase.SPlayerController:AskServerGiveObjectToPawn", Spec = "spec/objectives/interaction-system.md#rules")]
    [PortOf("rep:SBase.SPlayerController.AskServerGiveObjectToPawn", Spec = "spec/objectives/interaction-system.md#network")]
    public void AskServerGiveObjectToPawn(SGadget? gad, SBasePawn? pw, int NB)
    {
        if (gad is OpenScct.Game.Equipment.SidearmCarry) return;
        if (GadgetNet.ToServer(this, this, new CombatCall(CNAME_AskServerGiveObjectToPawn) { Target = gad, Actor1 = pw, Int1 = NB }))
        {
            return;
        }

        if (gad is not null && pw?.Controller is SPlayerController mate)
        {
            mate.TellClientToAddGadgetCharge(SGadget.GetUniqueIDByClass(new ClassRef(gad.GetUcClass().Path)), NB);
        }
    }

    /// <summary>
    /// A charge received from a team mate's backpack (server to owner): the first gadget of that id gets one charge
    /// and a "received" message.
    /// </summary>
    [Uc("SBase.SPlayerController.TellClientToAddGadgetCharge", Sha1 = "b28b9ecc9a1cc9c25e51e9224887a108bcad6e4a")]
    [PortOf("uc:SBase.SPlayerController:TellClientToAddGadgetCharge", Spec = "spec/equipment/gadget-natives.md#rules")]
    public void TellClientToAddGadgetCharge(int IDGadget, int NB)
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_TellClientToAddGadgetCharge) { Int1 = IDGadget, Int2 = NB }))
        {
            return;
        }

        for (SGadget? g = SBPawn?.MyGadget; g is not null; g = g.MyGadget)
        {
            if (SGadget.GetUniqueIDByClass(new ClassRef(g.GetUcClass().Path)) == IDGadget)
            {
                string received = (IManager?.Interactions[0] as SShareObjectInteraction)?.tReceived ?? "";
                GadgetHud.AddMessageToAuxiliaryStack(this, $"{NB} {g.ItemName} {received}", K.StickyMessageTime);
                g.AddCharge();
                return;
            }
        }
    }

    /// <summary>The gadget part of the local player's PlayerTick: the predicted sticky cam (sticky-cam.md rule 31).</summary>
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/sticky-cam.md#prediction-reloaded-40-script")]
    [Provenance(ProvenanceKind.Estimated, "Runs with the equipment part of PlayerTick (after the movement tick), not before the moves")]
    public void GadgetPlayerTick(float DeltaTime)
    {
        if (PredictedStickyCam is not null)
        {
            ReconcilePredictedStickyCam();
        }
    }
}
