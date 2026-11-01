using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Dead: the body stays, blinks, then the player spectates or respawns. Written from
// spec/game/death-lives-respawn.md (rules 6 to 10).
public partial class SPlayerController
{
    /// <summary>The class default of fTempsAvantClignote (seconds before a dead body starts blinking).</summary>
    private static readonly float DefaultTempsAvantClignote = new SPlayerController().fTempsAvantClignote;

    [Uc("SBase.SPlayerController.s_Dead")]
    public class s_Dead : UcActorState, IServerReStartPlayer, IPlayerTick
    {
        public static readonly s_Dead Instance = new(UcMeta.s_Dead, null);

        protected s_Dead(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// The death entry: the parts other areas own go through the game-flow ports (sounds, screen effect, radar
        /// marks, gadgets, sticky shock, voice, HUD alarms, camera); here the Disk Hunt disks, the used object, the
        /// damage history, the death time, the loading tip, the hand item and the Story disk, the behind view, the
        /// inventory, the vision reset and the blink timer (0.2 s on the server and offline, 1 s on a client).
        /// </summary>
        [Uc("SBase.SPlayerController.s_Dead.BeginState", Sha1 = "65baa40b262a968fac8a16cfcbea600dc5178154")]
        [PortOf("uc:SBase.SPlayerController:s_Dead.BeginState", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            IGameFlowPorts ports = pc.Ports;
            ports.BeginDeath(pc);
            if (pc.GameReplicationInfo is { GameMode: EGameMode.GM_DiskHunt })
            {
                pc.DropDisks();
            }

            // The spy mark comes off the dead pawn (spy-bullet.md rule 12).
            if (pc.Role == ENetRole.ROLE_Authority && pc.SBPawn is { } marked)
            {
                marked.DetectedByMarqueurSpy(pc.SPawnType == PawnType.SPAWNDEFENSE ? 0 : 1, false);
            }

            if (pc.Role == ENetRole.ROLE_Authority && pc.SBPawn is { UsedObject: { } used } pawn)
            {
                used.Destroy();
                pawn.UsedObject = null;
            }

            for (int i = 0; i < pc.LastDamages.Length; i++)
            {
                pc.LastDamages[i].DamageType = SDamageType.NoDamageType;
            }

            if (pc.Role == ENetRole.ROLE_Authority && pc.SBPawn is { } lying)
            {
                lying.bLyingOnGround = false;
            }

            pc.ReleaseChatPartners();
            pc.bCrouchWithRoll = false;
            pc.EndCoughNow();
            // A lethal landing can enter here while replaying an older remote input command.
            // The death/blink timer runs on the world clock, so its anchor must use that clock too.
            pc.fRespawnDate = pc.XLevel.WorldTimeSeconds;
            if (pc.PlayerReplicationInfo is { } pri)
            {
                if (pc.bNewProfile)
                {
                    pri.iTipNumber = 0;
                    pc.bNewProfile = false;
                }
                else
                {
                    int tips = (pri.Team as STeamInfo)?.aTipStrings.Count ?? 0;
                    pri.iTipNumber = pc.Rand(tips);
                    if (pri.iTipNumber >= tips)
                    {
                        pri.iTipNumber = 0;
                    }
                }
            }

            // The hand item and the Story disk fall where the player died (disk-flag.md rule 16). A client's copy would
            // only send the same server functions that the server's copy runs here as it enters the state.
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                pc.DiscardPickup();
                pc.DiscardFlag();
            }

            (pc.myHUD as SHud)?.EmptyAlarmsList();

            pc.bBehindView = true;
            pc.CombatHeadCamera(false, default);
            pc.MovementCamera(ECamMode.ECM_Walking);
            if (pc.SBPawn is { } body)
            {
                ports.DeleteAllInventory(body);
            }

            pc.RespawnTimeCount = 0;
            if (pc.Goggle is not null)
            {
                ports.ResetVisionMode(pc);
            }

            pc.SetTimer(pc.Level.NetMode != ENetMode.NM_Client ? K.DeadTimerServer : K.DeadTimerClient, true);
            pc.BeginRespawnWeaponPick();
        }

        [Uc("SBase.SPlayerController.s_Dead.EndState", Sha1 = "65cba9d25687145e8f6beffc4183431e3654316a")]
        [PortOf("uc:SBase.SPlayerController:s_Dead.EndState", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.Ports.EndDeath(pc);
            }

            pc.EndCoughNow();
            pc.bFireMode = 0;
            pc.fTempsAvantClignote = DefaultTempsAvantClignote;
            pc.bInstantRespawn = false;
        }

        /// <summary>
        /// Every tick of the timer: a client reminds the server it is dead; after fTempsAvantClignote the body blinks
        /// for 2.5 s, then it is hidden and loses its collision, and the player spectates (a respawn delay is set, or
        /// no life is left) or respawns at once.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Dead.Timer", Sha1 = "7d2972961306bcfa6d709a8cc41d722d4dce7378")]
        [PortOf("uc:SBase.SPlayerController:s_Dead.Timer", Spec = "spec/game/death-lives-respawn.md#rules")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.Level.NetMode == ENetMode.NM_Client)
            {
                pc.GameHost.ServerCall(pc, new GameCall("AntiBugTellServerIAmDead"));
            }

            float t = pc.XLevel.WorldTimeSeconds - pc.fRespawnDate;
            if (!(t > pc.fTempsAvantClignote) && !pc.bInstantRespawn)
            {
                return;
            }

            if (t < pc.fTempsAvantClignote + K.BlinkDuration && !pc.bInstantRespawn)
            {
                if (pc.SBPawn is { } blinking)
                {
                    blinking.bHidden = !blinking.bHidden;
                }

                return;
            }

            if (pc.SBPawn is { } hidden)
            {
                hidden.bHidden = true;
            }

            pc.Pawn?.SetCollision(false, false, false);
            float respawnTime = pc.GameReplicationInfo?.TimesRespawn ?? 0;
            if (respawnTime != 0f || (pc.PlayerReplicationInfo?.NumLives ?? 0) == 0)
            {
                if (pc.PlayerReplicationInfo is { } pri)
                {
                    pri.bIsSpectator = true;
                }

                pc.eventMirrorState(NAME_s_MultiSpectating_WhenKilled);
            }
            else
            {
                pc.ServerRespawn();
            }
        }

        [Uc("SBase.SPlayerController.s_Dead.ServerReStartPlayer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_Dead.ServerReStartPlayer", Spec = "spec/game/death-lives-respawn.md#rules")]
        public void ServerReStartPlayer(Controller self)
        {
        }

        /// <summary>
        /// Input, then the respawn weapon key. Not ported here: the camera rotation and the zero-acceleration move
        /// (camera and movement specs) and the client position update (net).
        /// </summary>
        [Uc("SBase.SPlayerController.s_Dead.PlayerTick", Sha1 = "9d9acda5896122cdfdf1124ff8b7a2be015e8eda")]
        [PortOf("uc:SBase.SPlayerController:s_Dead.PlayerTick", Spec = "spec/game/death-lives-respawn.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.Ports.NativePlayerInput(pc, DeltaTime);
            pc.CheckRespawnWeaponKey();
        }
    }
}
