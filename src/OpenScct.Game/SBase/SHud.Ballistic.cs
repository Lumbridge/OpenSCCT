using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SHud: the throw charge gauge and its tick (spec/equipment/grenade-throw.md).</summary>
public partial class SHud
{
    [Provenance(ProvenanceKind.Decoded, "SHud.SetBallisticValue: Sound'Armes_impacts.Gadgets.Hud_viseProgress'")]
    private static readonly AssetRef BallisticTick = new("Armes_impacts.Gadgets.Hud_viseProgress");

    /// <summary>The gauge ticks once per sixth of the charge.</summary>
    [Provenance(ProvenanceKind.Decoded, "SHud.SetBallisticValue: int(Value * 6.0) > int(BallisticValue * 6.0)")]
    public const float BallisticTickSteps = 6f;

    /// <summary>
    /// The throw charge shown by the gauge. The tick sound plays to the player when a charge starts from 0, and each
    /// time the charge passes another sixth.
    /// </summary>
    [Uc("SBase.SHud.SetBallisticValue", Sha1 = "04635fd36f4d777d1a5dbb2cbeaee8524e977c7f")]
    [PortOf("uc:SBase.SHud:SetBallisticValue", Spec = "spec/equipment/grenade-throw.md#rules")]
    public void SetBallisticValue(float Value)
    {
        if ((BallisticValue == 0f && Value > 0f) ||
            SvmMath.Trunc(Value * BallisticTickSteps) > SvmMath.Trunc(BallisticValue * BallisticTickSteps))
        {
            (Owner as PlayerController)?.ClientPlaySound(BallisticTick);
        }

        BallisticValue = Value;
    }
}

public partial class SPlayerController
{
    /// <summary>
    /// <c>SHud(myHUD).SetBallisticValue(v)</c>: the HUD's gauge (only where this player has a HUD) and the
    /// notification the client HUD reads.
    /// </summary>
    internal void SetBallisticValue(float Value)
    {
        CombatFx.Notify(this, CombatFx.SetBallisticValue, Value);
        (myHUD as SHud)?.SetBallisticValue(Value);
    }
}
