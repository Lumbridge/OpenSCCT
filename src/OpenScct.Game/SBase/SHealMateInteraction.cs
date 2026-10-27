using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The fixed "Heal Mate" entry, written from spec/objectives/interaction-system.md and
// spec/objectives/team-interactions.md.
public partial class SHealMateInteraction
{
    /// <summary>
    /// Offered while the player has heal shots left (iHealShot, 5, never spent in this build) and the mate search
    /// finds a team mate whose health differs from his class's default.
    /// </summary>
    [UcNative("ASHealMateInteraction::CanUse", Exe = 0x10AB97F0, Editor = 0x111CA6E0)]
    [PortOf("nat:SBase.SMateInteraction.CanUse", Spec = "spec/objectives/team-interactions.md#heal-mate")]
    [Provenance(ProvenanceKind.Decoded, "Game CanUse virtual 0x10ab97f0: PC.iHealShot > 0, mate.Health != mate.Class.Default.Health, then the mate search")]
    public override bool CanUse(SPlayerController? TheController) =>
        TheController is { iHealShot: > 0 } && FindMate(TheController, static (_, mate) => mate.Health != DefaultHealth(mate));

    /// <summary>The health of the mate's class defaults (Mate.default.Health), from the pawn's cached default object.</summary>
    public static int DefaultHealth(Pawn mate) => mate is SBasePawn pawn
        ? pawn.DefaultObject.Health
        : (mate.GetUcClass().Create() as Pawn)?.Health ?? mate.Health;

    /// <summary>
    /// A living mate is healed to his class's full health and cured of poison (his poison drain is counted first);
    /// then the default sends the healer back to his previous state.
    /// </summary>
    [Uc("SBase.SHealMateInteraction.InitInteract", Sha1 = "a3c8b74fac6b1640fb673516a99c10abae7a1e3b")]
    [PortOf("uc:SBase.SHealMateInteraction:InitInteract", Spec = "spec/objectives/interaction-system.md#data")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (Mate is { Health: > 0 } mate)
        {
            if (mate.Controller is SPlayerController pc)
            {
                GameFlowServices.Ports(this).CountPoisonDrain(pc);
            }

            mate.Health = DefaultHealth(mate);
            mate.bPoisonned = false;
        }

        base.InitInteract(Instigator, SubInteraction);
    }
}
