using OpenScct.Game.Animation;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The server pawn's side of a forced reinit (spec/net/proxy-pawns.md rule 7).
public partial class SBasePawn
{
    /// <summary>
    /// A forced reinit (type 0, from ReinitPawnBecauseOfDivergences): the action batch time goes back to -1, so the
    /// other players' copies start a new action epoch with the next batch.
    /// </summary>
    [Uc("SBase.SBasePawn.DistantClientReinit", Sha1 = "ab555afc3a159aa28867b4618082d34ae1436f63")]
    [PortOf("uc:SBase.SBasePawn:DistantClientReinit", Spec = "spec/net/proxy-pawns.md#rules")]
    public void DistantClientReinit(byte RT)
    {
        if (RT == 0)
        {
            fRepTimeOfLastActionChange = ProxyAnimation.ReinitBatchTime;
        }
    }
}
