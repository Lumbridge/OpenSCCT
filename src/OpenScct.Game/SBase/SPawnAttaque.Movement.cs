using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPawnAttaque (the spy) movement: the third-person view. Written from spec/movement/pawn.md.
public partial class SPawnAttaque
{
    /// <summary>The spy is seen from behind (third person).</summary>
    [Uc("SBase.SPawnAttaque.PointOfView", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
    [PortOf("uc:SBase.SPawnAttaque:PointOfView", Spec = "spec/movement/pawn.md#eye-position")]
    public override bool PointOfView() => true;
}
