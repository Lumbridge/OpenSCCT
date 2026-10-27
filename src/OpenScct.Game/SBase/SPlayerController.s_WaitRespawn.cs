using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_WaitRespawn: the server restarts the player. Written from
// spec/game/death-lives-respawn.md (rule 14).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_WaitRespawn")]
    public class s_WaitRespawn : UcActorState, IServerReStartPlayer, IPlayerTick, IAntiBugTellServerIAmDead
    {
        public static readonly s_WaitRespawn Instance = new(UcMeta.s_WaitRespawn, null);

        protected s_WaitRespawn(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_WaitRespawn.BeginState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_WaitRespawn.BeginState", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_WaitRespawn.EndState", Sha1 = "623eeff6742359269faf4f06d6d4ab3ccf3c9118")]
        [PortOf("uc:SBase.SPlayerController:s_WaitRespawn.EndState", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bFireMode = 0;
            pc.LocalMyDesiredRotation = pc.Rotation;
        }

        /// <summary>On the server, the game rules spawn the player again.</summary>
        [Uc("SBase.SPlayerController.s_WaitRespawn.ServerReStartPlayer", Sha1 = "3da5cf99d0c5fa0102316f5a650dd79671df3251")]
        [PortOf("uc:SBase.SPlayerController:s_WaitRespawn.ServerReStartPlayer", Spec = "spec/game/death-lives-respawn.md#rules")]
        public void ServerReStartPlayer(Controller self)
        {
            if (self.Level.NetMode == ENetMode.NM_Client)
            {
                return;
            }

            self.Level.Game?.eventRestartPlayer(self);
        }

        /// <summary>Only the zero-acceleration move (movement spec, not ported here).</summary>
        [Uc("SBase.SPlayerController.s_WaitRespawn.PlayerTick", Sha1 = "3f33a512fb63e77e97bfb7281a891e820074b854")]
        [PortOf("uc:SBase.SPlayerController:s_WaitRespawn.PlayerTick", Spec = "spec/game/death-lives-respawn.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
        }

        /// <summary>Ignored here.</summary>
        public void AntiBugTellServerIAmDead(SPlayerController self)
        {
        }

        /// <summary>Begin: off a client, restart the player; then stop.</summary>
        [Uc("SBase.SPlayerController.s_WaitRespawn@code", Sha1 = "58e4bc485ac560e2cfbda23bef5cd07ff467b2dc")]
        [PortOf("uc:SBase.SPlayerController:s_WaitRespawn@code", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var pc = (SPlayerController)self;
            frame.Pc = LatentFrame.NoCode;
            if (pc.Level.NetMode != ENetMode.NM_Client)
            {
                pc.eventServerReStartPlayer();
            }
        }
    }
}
