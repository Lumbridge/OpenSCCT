using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController waiting states: s_PlayerWaiting (before the match), WaitingForPawn (the pawn has not
// replicated yet), and the Engine-style BaseSpectating and PlayerWaiting kept by the class. Written from
// spec/game/login-and-teams.md and spec/game/respawn-and-spawning.md.
public partial class SPlayerController
{
    /// <summary>
    /// Restart from a waiting state: not before WaitDelay, never on a client; marks the player ready while the match
    /// waits to start, otherwise restarts it (shared by s_PlayerWaiting and PlayerWaiting).
    /// </summary>
    private static void WaitingServerReStartPlayer(Controller self)
    {
        var pc = (SPlayerController)self;
        if (pc.Level.TimeSeconds < pc.WaitDelay || pc.Level.NetMode == ENetMode.NM_Client)
        {
            return;
        }

        if (pc.Level.Game is { bWaitingToStartMatch: true })
        {
            if (pc.PlayerReplicationInfo is { } pri)
            {
                pri.bReadyToPlay = true;
            }
        }
        else
        {
            pc.Level.Game?.eventRestartPlayer(pc);
        }
    }

    /// <remarks>
    /// Ignores input, movement, damage and PlayerTick. Not ported here: the other ignored functions without state
    /// dispatch (sight, hearing, bumps, jump, rotation, moves).
    /// </remarks>
    [Uc("SBase.SPlayerController.s_PlayerWaiting")]
    public class s_PlayerWaiting : UcActorState, IServerReStartPlayer, IPlayerTick, ITakeDamage
    {
        public static readonly s_PlayerWaiting Instance = new(UcMeta.s_PlayerWaiting, null);

        protected s_PlayerWaiting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWaiting.BeginState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWaiting.BeginState", Spec = "spec/game/login-and-teams.md#rules")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWaiting.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWaiting.EndState", Spec = "spec/game/login-and-teams.md#rules")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWaiting.ServerReStartPlayer", Sha1 = "a431ca45015f18865c75451ac5682d9c322759c7")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWaiting.ServerReStartPlayer", Spec = "spec/game/login-and-teams.md#rules")]
        public void ServerReStartPlayer(Controller self) => WaitingServerReStartPlayer(self);

        /// <summary>Ignored.</summary>
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
        }

        /// <summary>Ignored.</summary>
        public void TakeDamage(Actor self, int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag)
        {
        }

        /// <summary>
        /// The state code only waits for the front end: online setup on a game-service client and, outside the entry
        /// and lobby maps, a 1 s sleep loop until the GUI exists. The simulation has no GUI to wait for, so it stops.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWaiting@code", Sha1 = "ea0a9f5fc4c21bae922d528289d599a5b710ebfe")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWaiting@code", Spec = "spec/game/login-and-teams.md#rules")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            frame.Pc = LatentFrame.NoCode;
        }
    }

    /// <summary>The Engine-style base of PlayerWaiting and WaitingForPawn. Its free-fly move needs MoveSmooth (not ported).</summary>
    [Uc("SBase.SPlayerController.BaseSpectating")]
    public class BaseSpectating : UcActorState
    {
        public static readonly BaseSpectating Instance = new(UcMeta.BaseSpectating, null);

        protected BaseSpectating(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }

    /// <summary>Entered only through PlayerController.Reset, which this game never calls; kept for completeness.</summary>
    [Uc("SBase.SPlayerController.PlayerWaiting")]
    public class PlayerWaiting : BaseSpectating, IServerReStartPlayer, ITakeDamage
    {
        public static new readonly PlayerWaiting Instance = new(UcMeta.PlayerWaiting, null);

        protected PlayerWaiting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.PlayerWaiting.BeginState", Sha1 = "908589f4de6d513bceef8e81e5bd981fb0487f44")]
        [PortOf("uc:SBase.SPlayerController:PlayerWaiting.BeginState", Spec = "spec/game/login-and-teams.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.PlayerReplicationInfo?.SetWaitingPlayer(true);
            pc.bCollideWorld = true;
        }

        /// <summary>Leaves waiting. The pawn mesh refresh (SetMesh, view) is not ported.</summary>
        [Uc("SBase.SPlayerController.PlayerWaiting.EndState", Sha1 = "f94da1a032109cfc2b468ccee9846767aa362d0a")]
        [PortOf("uc:SBase.SPlayerController:PlayerWaiting.EndState", Spec = "spec/game/login-and-teams.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.PlayerReplicationInfo?.SetWaitingPlayer(false);
            pc.bCollideWorld = false;
        }

        [Uc("SBase.SPlayerController.PlayerWaiting.ServerReStartPlayer", Sha1 = "a431ca45015f18865c75451ac5682d9c322759c7")]
        [PortOf("uc:SBase.SPlayerController:PlayerWaiting.ServerReStartPlayer", Spec = "spec/game/login-and-teams.md#rules")]
        public void ServerReStartPlayer(Controller self) => WaitingServerReStartPlayer(self);

        /// <summary>The team change goes to the game rules (as a new team choice).</summary>
        [Uc("SBase.SPlayerController.PlayerWaiting.ChangeTeam", Sha1 = "c83992e99b127d113ea18c52c0d2b0f6b4b031e3")]
        [PortOf("uc:SBase.SPlayerController:PlayerWaiting.ChangeTeam", Spec = "spec/game/login-and-teams.md#rules")]
        public virtual void ChangeTeam(SPlayerController self, int N) => self.Level.Game?.eventChangeTeam(self, N, true);

        /// <summary>Fire restarts the player (LoadPlayers, a view helper, is not ported).</summary>
        [Uc("SBase.SPlayerController.PlayerWaiting.Fire", Sha1 = "761949310a87e9d2924d0fb7abdd901b46f61507")]
        [PortOf("uc:SBase.SPlayerController:PlayerWaiting.Fire", Spec = "spec/game/login-and-teams.md#rules")]
        public virtual void Fire(SPlayerController self, float F) => self.eventServerReStartPlayer();

        /// <summary>Ignored.</summary>
        public void TakeDamage(Actor self, int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag)
        {
        }
    }

    [Uc("SBase.SPlayerController.WaitingForPawn")]
    public class WaitingForPawn : BaseSpectating, IPlayerTick
    {
        public static new readonly WaitingForPawn Instance = new(UcMeta.WaitingForPawn, null);

        protected WaitingForPawn(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Asks the server for the pawn now and every 0.2 s.</summary>
        [Uc("SBase.SPlayerController.WaitingForPawn.BeginState", Sha1 = "5222c3a1c8ca724aa8ae51f58a5eda3ec2abb1a3")]
        [PortOf("uc:SBase.SPlayerController:WaitingForPawn.BeginState", Spec = "spec/game/respawn-and-spawning.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.SetTimer(K.WaitingForPawnRetry, true);
            pc.AskForPawn();
        }

        [Uc("SBase.SPlayerController.WaitingForPawn.EndState", Sha1 = "dc54efb6275d87867556581613359f0545e7fd8a")]
        [PortOf("uc:SBase.SPlayerController:WaitingForPawn.EndState", Spec = "spec/game/respawn-and-spawning.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bBehindView = false;
            pc.SetTimer(0f, false);
        }

        [Uc("SBase.SPlayerController.WaitingForPawn.Timer", Sha1 = "7cec48a1c0ca0553c970689af6a13fa54a29220a")]
        [PortOf("uc:SBase.SPlayerController:WaitingForPawn.Timer", Spec = "spec/game/respawn-and-spawning.md#rules")]
        public override void Timer(Actor self) => ((SPlayerController)self).AskForPawn();

        /// <summary>Once the pawn has arrived it is taken (ClientRestart); otherwise the retry timer is kept running.</summary>
        [Uc("SBase.SPlayerController.WaitingForPawn.PlayerTick", Sha1 = "ff0271bbfb2e8c85ebd377371a43739a96f824f7")]
        [PortOf("uc:SBase.SPlayerController:WaitingForPawn.PlayerTick", Spec = "spec/game/respawn-and-spawning.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.PlayerTick(DeltaTime);
            if (pc.Pawn is { } pawn)
            {
                pawn.Controller = pc;
                pawn.bUpdateEyeheight = true;
                pc.ClientRestart();
            }
            else if (pc.TimerRate <= 0f || pc.TimerRate > K.WaitingForPawnMaxRate)
            {
                pc.SetTimer(K.WaitingForPawnRetry, true);
                pc.AskForPawn();
            }
        }

        [Uc("SBase.SPlayerController.WaitingForPawn.Fire", Sha1 = "7cec48a1c0ca0553c970689af6a13fa54a29220a")]
        [PortOf("uc:SBase.SPlayerController:WaitingForPawn.Fire", Spec = "spec/game/respawn-and-spawning.md#rules")]
        public virtual void Fire(SPlayerController self, float F) => self.AskForPawn();
    }
}
