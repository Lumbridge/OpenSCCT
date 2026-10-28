using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The HUD's distance unit (the profile's bInFeet: metres or feet in the range finder and motion tracking read-outs).
public partial class SHud
{
    [Provenance(ProvenanceKind.Decoded, "SHud.HudUnit: fDistanceUnit 0.03281 and \"ft\" with bFeet")]
    public const float FeetPerUnit = 0.03281f;

    [Provenance(ProvenanceKind.Decoded, "SHud.HudUnit: fDistanceUnit 0.01 and \"m\" without bFeet")]
    public const float MetresPerUnit = 0.01f;

    /// <summary>Feet ("ft", 0.03281 a unit) or metres ("m", 0.01 a unit) for the HUD's distances.</summary>
    [Uc("SBase.SHud.HudUnit", Sha1 = "76f5dfcd8fea6f48c121b9e20b3158b70fd629d4")]
    [PortOf("uc:SBase.SHud:HudUnit", Spec = "spec/hud/counters-and-prompts.md#rules")]
    public void HudUnit(bool bFeet)
    {
        if (bFeet)
        {
            StrDistanceUnit = "ft";
            fDistanceUnit = FeetPerUnit;
        }
        else
        {
            StrDistanceUnit = "m";
            fDistanceUnit = MetresPerUnit;
        }
    }
}
