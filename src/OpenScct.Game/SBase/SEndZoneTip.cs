using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SEndZoneTip: a zone tip that ends the game when a player reaches it (spec/devices/zone-tips.md).</summary>
public partial class SEndZoneTip
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_DelayBeforeEnd.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>The hint, then for a player pawn: remember its team and start the end delay.</summary>
    [Uc("SBase.SEndZoneTip.Touch", Sha1 = "7cb21ae45f31cb1d3ad8bf74d67b3f40f540635f")]
    [PortOf("uc:SBase.SEndZoneTip:Touch", Spec = "spec/devices/zone-tips.md#rules")]
    protected override void Touch(Actor Other)
    {
        base.Touch(Other);
        if (Other is SBasePawn { Controller: SPlayerController } pawn)
        {
            TouchedByTeam = (byte)pawn.SPawnType;
            GotoState(nameof(s_DelayBeforeEnd));
        }
    }

    /// <summary>The touching team wins (END_TEAMDEATH) and the game ends.</summary>
    private void EndGame()
    {
        if (Level.Game is SGameInfo game)
        {
            game.ReplicateWinningReason(TouchedByTeam, EEndReason.END_TEAMDEATH);
            game.sEndGame();
        }
    }

    /// <summary>Counting down EndGameDelay seconds (or ending at once without a delay).</summary>
    [Uc("SBase.SEndZoneTip.s_DelayBeforeEnd")]
    public class s_DelayBeforeEnd : UcActorState
    {
        public static readonly s_DelayBeforeEnd Instance = new(UcMeta.s_DelayBeforeEnd, null);

        protected s_DelayBeforeEnd(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SEndZoneTip.s_DelayBeforeEnd.BeginState", Sha1 = "aa399c89f34aae2156663f5a21ff33b111e843ad")]
        [PortOf("uc:SBase.SEndZoneTip:s_DelayBeforeEnd.BeginState", Spec = "spec/devices/zone-tips.md#rules")]
        public override void BeginState(Actor self)
        {
            var tip = (SEndZoneTip)self;
            if (tip.EndGameDelay > 0)
            {
                tip.SetTimer(tip.EndGameDelay, false);
            }
            else
            {
                tip.EndGame();
            }
        }

        [Uc("SBase.SEndZoneTip.s_DelayBeforeEnd.Timer", Sha1 = "09ae49e040aae026138df5c05c6f8ecd96649781")]
        [PortOf("uc:SBase.SEndZoneTip:s_DelayBeforeEnd.Timer", Spec = "spec/devices/zone-tips.md#rules")]
        public override void Timer(Actor self) => ((SEndZoneTip)self).EndGame();
    }
}
