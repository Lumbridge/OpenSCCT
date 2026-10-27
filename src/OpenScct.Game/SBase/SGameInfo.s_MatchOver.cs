using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SGameInfo state s_MatchOver: the round is over. Written from spec/game/end-of-round.md.
public partial class SGameInfo
{
    [Uc("SBase.SGameInfo.s_MatchOver")]
    public class s_MatchOver : UcActorState, IRestartPlayer, IChangeTeam, IScoreKill
    {
        public static readonly s_MatchOver Instance = new(UcMeta.s_MatchOver, null);

        protected s_MatchOver(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Every player's own machine and its server copy enter the end state; then a 1 s timer replaces the rules
        /// timer (poison, welcome messages, the time limit and the timed event stop).
        /// </summary>
        [Uc("SBase.SGameInfo.s_MatchOver.BeginState", Sha1 = "65ea1587dabf69d1703f0332e5a36f1f4a3b1c70")]
        [PortOf("uc:SBase.SGameInfo:s_MatchOver.BeginState", Spec = "spec/game/end-of-round.md#rules")]
        public override void BeginState(Actor self)
        {
            var game = (SGameInfo)self;
            for (Controller? p = game.Level.ControllerList; p is not null; p = p.nextController)
            {
                if (p is not PlayerController pc)
                {
                    continue;
                }

                if (pc.IsRemotePlayer)
                {
                    game.Host.ClientCall(pc, new GameCall("ClientGameEnded"));
                }
                else
                {
                    pc.ClientGameEnded();
                }

                pc.GameHasEnded();
            }

            game.SetTimer(K.MatchOverTimer, true);
        }

        /// <summary>Travels again when every controller is an SBase player that asked to play again (legacy menu).</summary>
        [Uc("SBase.SGameInfo.s_MatchOver.Timer", Sha1 = "760b218c00793602685434e486d6ab7c64624130")]
        [PortOf("uc:SBase.SGameInfo:s_MatchOver.Timer", Spec = "spec/game/end-of-round.md#rules")]
        public override void Timer(Actor self)
        {
            var game = (SGameInfo)self;
            bool bRestart = true;
            for (Controller? p = game.Level.ControllerList; p is not null; p = p.nextController)
            {
                if (p is not SPlayerController { PlayerReplicationInfo.bReadyToPlayAgain: true })
                {
                    bRestart = false;
                }
            }

            if (bRestart)
            {
                game.RestartGame();
            }
        }

        [Uc("SBase.SGameInfo.s_MatchOver.RestartPlayer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SGameInfo:s_MatchOver.RestartPlayer", Spec = "spec/game/respawn-and-spawning.md#rules")]
        public void RestartPlayer(GameInfo self, Controller aPlayer)
        {
        }

        [Uc("SBase.SGameInfo.s_MatchOver.ScoreKill", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SGameInfo:s_MatchOver.ScoreKill", Spec = "spec/game/end-of-round.md#rules")]
        public void ScoreKill(GameInfo self, Controller? Killer, Controller? Other)
        {
        }

        [Uc("SBase.SGameInfo.s_MatchOver.ChangeTeam", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SGameInfo:s_MatchOver.ChangeTeam", Spec = "spec/game/end-of-round.md#rules")]
        public bool ChangeTeam(GameInfo self, Controller Other, int num, bool bNewTeam) => false;
    }
}
