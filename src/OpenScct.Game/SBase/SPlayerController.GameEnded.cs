using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state GameEnded: the round is over for this player. Written from
// spec/game/end-of-round.md (rules 16 to 18).
public partial class SPlayerController
{
    /// <remarks>
    /// The state ignores GotoState, MirrorState and ChangeState, so once entered the controller stays here until the
    /// map changes. Not ported here: the ignored sight and hearing functions and the tutorial branch of the end
    /// menu (profile functions). Damage is ignored by the combat
    /// dispatch.
    /// </remarks>
    [Uc("SBase.SPlayerController.GameEnded")]
    public class GameEnded : UcActorState, IPlayerTick, IServerReStartPlayer, IGotoState, IMirrorState, IChangeState,
        IPlayerMove, IFire, IAltFire, ISwitchROF, ISwitchHeadset, IGadTorcheSwitch, IGadLaserSwitch
    {
        public static readonly GameEnded Instance = new(UcMeta.GameEnded, null);

        protected GameEnded(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// The end screen on this player's machine (menus, Magma end page, rumble, HUD perturbation), then the end
        /// flags, the ready flag cleared on the server, the flash, the end menu clock and the vision reset.
        /// </summary>
        [Uc("SBase.SPlayerController.GameEnded.BeginState", Sha1 = "e522e22ec10ae79ef66374444936202e00f3c0a4")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.BeginState", Spec = "spec/game/end-of-round.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            bool local = !pc.IsRemotePlayer;
            IGameHost host = pc.GameHost;
            if (local)
            {
                host.ClientCall(pc, new GameCall("SetWeirdEffect") { Float1 = 0f });
                host.ClientCall(pc, new GameCall("CloseMagmaMenu"));
                host.ClientCall(pc, new GameCall("SpawnMagmaEndGame"));
                host.ClientCall(pc, new GameCall("NativeRumbleEnable") { Bool = false });
            }

            pc.bGameEnded = true;
            // The result state sends zero acceleration on the owning machine. Modern network
            // movement stops accepting commands at this boundary, so the server copy must also
            // release the last held input here. Keep velocity/physics for normal braking or falling.
            if (pc.Pawn is { } pawn) pawn.Acceleration = FVector.Zero;
            pc.EndCoughNow();
            pc.ReadyToPlayAgain(false);
            pc.FlashColor = pc.FlashColor with { A = 0 };
            pc.fGrabDate = pc.Level.TimeSeconds;
            pc.bEndOfFade = false;
            pc.bLineEffect = false;
            if (pc.Goggle is not null)
            {
                pc.Ports.ResetVisionMode(pc);
            }

            if (local)
            {
                host.ClientCall(pc, new GameCall("StopHudPerturbation"));
            }
        }

        [Uc("SBase.SPlayerController.GameEnded.EndState", Sha1 = "a6a1fe6c0bf050417137e030ba6527e1fe6080aa")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.EndState", Spec = "spec/game/end-of-round.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bLineEffect = false;
            pc.bGameEnded = false;
            pc.EndCoughNow();
        }

        /// <summary>
        /// On the player's machine: the victory or defeat music once the winner is known, and 10 s later the end menu
        /// (Ending_Menu for LAN and online games, the ranking page for ranked Live games, back to the entry map
        /// offline); then the class-level PlayerTick with Fire released.
        /// </summary>
        [Uc("SBase.SPlayerController.GameEnded.PlayerTick", Sha1 = "fed749ef47dc1c3e4bc120cbbf6d588315fe47c0")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.PlayerTick", Spec = "spec/game/end-of-round.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            IGameHost host = pc.GameHost;
            GameReplicationInfo? gri = pc.GameReplicationInfo;
            int winningTeam = gri?.WinningTeam ?? 0;
            if (winningTeam == K.NoResult)
            {
                pc.fGrabDate = pc.Level.TimeSeconds;
            }
            else if (!pc.bLineEffect)
            {
                pc.bLineEffect = true;
                AssetRef music = winningTeam == (int)pc.SPawnType ? pc.VictoryMusic : pc.LooseMusic;
                host.ClientCall(pc, new GameCall("ClientSetMusic") { Asset = music, Int = (int)EMusicTransition.MTRAN_Fade });
            }

            if (!pc.bEndOfFade && pc.Level.TimeSeconds - pc.fGrabDate > K.EndMenuDelay)
            {
                pc.bEndOfFade = true;
                NetworkType net = host.NetType();
                if (net == NetworkType.NET_XBOXLIVE && (gri?.GameWithRanking ?? false))
                {
                    host.ClientCall(pc, new GameCall("ClientOpenMenu") { Name1 = new Name("UpdateRanking") });
                }
                else if (net is NetworkType.NET_LAN or NetworkType.NET_XBOXLIVE or NetworkType.NET_GAMESERVICE)
                {
                    host.ClientCall(pc, new GameCall("ClientCloseMenu") { Bool = true });
                    host.ClientCall(pc, new GameCall("ClientOpenMenu") { Name1 = new Name("Ending_Menu") });
                }
                else
                {
                    host.ClientCall(pc, new GameCall("ClientOpenMenu") { Name1 = new Name("loading"), Text = "VisitMapReturn" });
                }
            }

            pc.bFire = 0;
            pc.PlayerTick(DeltaTime);
        }

        /// <summary>The Engine GameEnded version: no restart.</summary>
        [Uc("Engine.PlayerController.GameEnded.ServerReStartPlayer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:Engine.PlayerController:GameEnded.ServerReStartPlayer", Spec = "spec/game/end-of-round.md#rules")]
        public void ServerReStartPlayer(Controller self)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.Timer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.Timer", Spec = "spec/game/end-of-round.md#rules")]
        public override void Timer(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.AnimEnd", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.AnimEnd", Spec = "spec/game/end-of-round.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
        }

        /// <summary>Ignored.</summary>
        public override void Bump(Actor self, Actor Other)
        {
        }

        /// <summary>Ignored: the controller cannot leave the end state.</summary>
        public EGotoState GotoState(SPlayerController self, Name NewState, Name Label, bool bDontSaveCurrentState) =>
            EGotoState.GOTOSTATE_Success;

        /// <summary>Ignored.</summary>
        public void MirrorState(SPlayerController self, Name nomdustate, Name nomduLabel, bool bDontSaveCurrentState, Actor? AddActor)
        {
        }

        /// <summary>Ignored.</summary>
        public void ChangeState(SPlayerController self, Name nomdustate, Name nomduLabel, bool bDontSaveCurrentState)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.UpdateRotation", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.UpdateRotation", Spec = "spec/game/end-of-round.md#rules")]
        public virtual void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch)
        {
        }

        /// <summary>The pawn gets no acceleration: zero is sent (client) or processed (server).</summary>
        [Uc("SBase.SPlayerController.GameEnded.PlayerMove", Sha1 = "a78fb98db48690c50da32a57168cd320c63472d1")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.PlayerMove", Spec = "spec/game/end-of-round.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.SendMove(DeltaTime, FVector.Zero);

        [Uc("SBase.SPlayerController.GameEnded.Fire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.Fire", Spec = "spec/game/end-of-round.md#rules")]
        public void Fire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.AltFire", Spec = "spec/game/end-of-round.md#rules")]
        public void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.SwitchROF", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.SwitchROF", Spec = "spec/game/end-of-round.md#rules")]
        public void SwitchROF(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.SwitchHeadset", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.SwitchHeadset", Spec = "spec/game/end-of-round.md#rules")]
        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.GadTorcheSwitch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.GadTorcheSwitch", Spec = "spec/game/end-of-round.md#rules")]
        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.GameEnded.GadLaserSwitch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:GameEnded.GadLaserSwitch", Spec = "spec/game/end-of-round.md#rules")]
        public void GadLaserSwitch(SPlayerController self)
        {
        }
    }
}
