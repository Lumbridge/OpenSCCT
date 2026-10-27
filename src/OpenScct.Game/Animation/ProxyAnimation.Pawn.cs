using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn's start-up as another player's pawn on a client (spec/net/proxy-pawns.md rule 6): the walk set of
// its stance before the first relayed action arrives (ProxyAnimation.InitializeBasicActions).
public partial class SBasePawn
{
    /// <summary>A pawn without a controller (another player's, on a client) starts with the walk set of its stance.</summary>
    [Uc("SBase.SBasePawn.PostNetBeginPlay", Sha1 = "ef52ab3a7b6325d73d59405a0cd691bd2516647b")]
    [PortOf("uc:SBase.SBasePawn:PostNetBeginPlay", Spec = "spec/net/proxy-pawns.md#rules")]
    protected override void PostNetBeginPlay()
    {
        if (Controller is null && MovementService.Of(this) is not null)
        {
            ProxyAnimation.InitializeBasicActions(this);
        }

        base.PostNetBeginPlay();
    }
}
