using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The fixed "Refill" entry: a merc refills his gadgets from the backpack a team mate carries for him. Written from
// spec/objectives/interaction-system.md and spec/objectives/team-interactions.md.
public partial class SBackPackInteraction
{
    /// <summary>The radius of the use sound played at the mate.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBackPackInteraction.InitInteract: Mate.PlaySound(sUseSound, , 1.0, , 100.0)")]
    public const float UseSoundRadius = 100f;

    /// <summary>
    /// Offered when one of the player's first four gadgets (torches and laser torches left out) is below its
    /// class's charges and the mate search finds a team mate whose backpack slot for this player (A or B, the
    /// player as its owner) holds a charge for one of those gadgets.
    /// </summary>
    [UcNative("ASBackPackInteraction::CanUse", Exe = 0x10ABC920, Editor = 0x111CA960)]
    [PortOf("nat:SBase.SMateInteraction.CanUse", Spec = "spec/objectives/team-interactions.md#refill")]
    [Provenance(ProvenanceKind.Decoded, "Game CanUse virtual 0x10abc920: gadget chain MyGadget +0xa88/+0x338, Charge +0x330 against the class default, IsA SGadTorche/SGadTorcheLaser skipped, aNbChargesGad_Mate_A0..3 with aBackPackOwner_A, B0..3 with aBackPackOwner_B")]
    public override bool CanUse(SPlayerController? TheController)
    {
        if (TheController?.Pawn is not SBasePawn me)
        {
            return false;
        }

        // Bit i of full: the i-th refillable gadget already holds its class's charges.
        int full = 0;
        int i = 0;
        bool any = false;
        for (SGadget? g = me.MyGadget; g is not null; g = g.MyGadget)
        {
            if (!Refillable(g))
            {
                continue;
            }

            if (i < 4 && DefaultCharge(g) <= g.Charge)
            {
                full |= 1 << i;
            }

            i++;
            any = true;
        }

        if (any && full == 0b1111)
        {
            return false;
        }

        // The search's test reads the searcher and the full mask from fields, so a call makes no closure (the HUD and
        // the bots ask every frame and tick).
        _searcher = me;
        _full = full;
        return FindMate(TheController, _wantsMate ??= WantsMate);
    }

    private SBasePawn? _searcher;
    private int _full;
    private System.Func<SPlayerController, SBasePawn, bool>? _wantsMate;

    private bool WantsMate(SPlayerController controller, SBasePawn mate) =>
        (ReferenceEquals(mate.aBackPackOwner_A, _searcher) && Holds(_full, mate.aNbChargesGad_Mate_A0, mate.aNbChargesGad_Mate_A1,
            mate.aNbChargesGad_Mate_A2, mate.aNbChargesGad_Mate_A3)) ||
        (ReferenceEquals(mate.aBackPackOwner_B, _searcher) && Holds(_full, mate.aNbChargesGad_Mate_B0, mate.aNbChargesGad_Mate_B1,
            mate.aNbChargesGad_Mate_B2, mate.aNbChargesGad_Mate_B3));

    private static bool Holds(int full, int c0, int c1, int c2, int c3) =>
        (c0 > 0 && (full & 1) == 0) || (c1 > 0 && (full & 2) == 0) || (c2 > 0 && (full & 4) == 0) || (c3 > 0 && (full & 8) == 0);

    /// <summary>Every gadget but the torches (SGadTorche, SGadTorcheLaser and their subclasses).</summary>
    public static bool Refillable(SGadget g) => !Equipment.PawnInventory.IsA(g, TorchClass) && !Equipment.PawnInventory.IsA(g, LaserTorchClass);

    private static readonly ClassRef TorchClass = new("SBase.SGadTorche");

    private static readonly ClassRef LaserTorchClass = new("SBase.SGadTorcheLaser");

    /// <summary>The charges of the gadget's class defaults (gad.default.Charge), from the gadget's cached default object.</summary>
    public static int DefaultCharge(SGadget g) => g.ClassDefaults.Charge;

    /// <summary>
    /// A mate is there: the player's gadgets take what the mate's backpack holds for them, the use sound plays at the
    /// mate (radius 100), the stats count a backpack taken and given; then the default sends the player back.
    /// </summary>
    [Uc("SBase.SBackPackInteraction.InitInteract", Sha1 = "26cec4317abf5509d215cdbb1f9aa194170213bd")]
    [PortOf("uc:SBase.SBackPackInteraction:InitInteract", Spec = "spec/objectives/team-interactions.md#refill")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (Mate is { } mate)
        {
            Instigator.UpdateMyInventoryChargeBaseOnMateBackpack(mate);
            mate.PlaySound(sUseSound, Volume: 1f, Radius: UseSoundRadius);
            if (Instigator.PlayerReplicationInfo?.Stats is { } taken)
            {
                taken.Merc_BackpacksTaken++;
            }

            if ((mate.Controller as SPlayerController)?.PlayerReplicationInfo?.Stats is { } given)
            {
                given.Merc_BackpacksGiven++;
            }
        }

        base.InitInteract(Instigator, SubInteraction);
    }
}
