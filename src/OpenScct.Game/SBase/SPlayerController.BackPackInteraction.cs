using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController's part of the "Refill" entry (spec/objectives/team-interactions.md, refill).
public partial class SPlayerController
{
    /// <summary>
    /// Each refillable gadget (torches left out) below its class's charges takes what the mate's backpack slot for
    /// this player holds for it (slot A when the mate's aBackPackOwner_A is this pawn, else slot B), up to the
    /// missing charges; the slot gives them up. The gadgets are counted in their list order, torches not counted.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdateMyInventoryChargeBaseOnMateBackpack", Sha1 = "e704e4f190fcd5b328403b622142c6f4a7714971")]
    [PortOf("uc:SBase.SPlayerController:UpdateMyInventoryChargeBaseOnMateBackpack", Spec = "spec/objectives/team-interactions.md#refill")]
    public void UpdateMyInventoryChargeBaseOnMateBackpack(SBasePawn Mate)
    {
        if (SBPawn is null || Mate is null)
        {
            return;
        }

        int i = 0;
        for (SGadget? gad = SBPawn.MyGadget; gad is not null; gad = gad.MyGadget)
        {
            if (!SBackPackInteraction.Refillable(gad))
            {
                continue;
            }

            int full = SBackPackInteraction.DefaultCharge(gad);
            if (gad.Charge < full)
            {
                bool a = ReferenceEquals(Mate.aBackPackOwner_A, Pawn);
                int held = (a, i) switch
                {
                    (true, 0) => Mate.aNbChargesGad_Mate_A0,
                    (true, 1) => Mate.aNbChargesGad_Mate_A1,
                    (true, 2) => Mate.aNbChargesGad_Mate_A2,
                    (true, 3) => Mate.aNbChargesGad_Mate_A3,
                    (false, 0) => Mate.aNbChargesGad_Mate_B0,
                    (false, 1) => Mate.aNbChargesGad_Mate_B1,
                    (false, 2) => Mate.aNbChargesGad_Mate_B2,
                    (false, 3) => Mate.aNbChargesGad_Mate_B3,
                    _ => 0,
                };
                if (held > 0)
                {
                    int add = (byte)System.Math.Clamp(full - gad.Charge, 0, held);
                    gad.AddChargeOnClientFromServer(add);
                    switch ((a, i))
                    {
                        case (true, 0): Mate.aNbChargesGad_Mate_A0 -= add; break;
                        case (true, 1): Mate.aNbChargesGad_Mate_A1 -= add; break;
                        case (true, 2): Mate.aNbChargesGad_Mate_A2 -= add; break;
                        case (true, 3): Mate.aNbChargesGad_Mate_A3 -= add; break;
                        case (false, 0): Mate.aNbChargesGad_Mate_B0 -= add; break;
                        case (false, 1): Mate.aNbChargesGad_Mate_B1 -= add; break;
                        case (false, 2): Mate.aNbChargesGad_Mate_B2 -= add; break;
                        case (false, 3): Mate.aNbChargesGad_Mate_B3 -= add; break;
                    }
                }
            }

            i++;
        }
    }
}
