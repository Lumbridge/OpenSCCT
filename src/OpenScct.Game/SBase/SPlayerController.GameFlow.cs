using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.GameFlow;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController game flow: readiness and the match start, possession and the start state, respawn,
// resign, spectator targets, the end menus and the Merc Deathmatch respawn weapon. Written from spec/game/*.md.
//
// Calls from the server to the owning client run here for a player on this machine and go to the game host for a
// remote player (IsRemotePlayer), the way UnrealScript replicated functions behave.
public partial class SPlayerController
{
    private static readonly Name NAME_s_Dead = new("s_Dead");
    private static readonly Name NAME_s_WaitRespawn = new("s_WaitRespawn");
    private static readonly Name NAME_s_MultiSpectating_WhenKilled = new("s_MultiSpectating_WhenKilled");
    private static readonly Name NAME_s_Spectating_Visitor = new("s_Spectating_Visitor");
    private static readonly Name NAME_s_BlastFloor = new("s_BlastFloor");
    private static readonly Name NAME_GameEnded = new("GameEnded");
    private static readonly Name NAME_WaitingForPawn = new("WaitingForPawn");
    private static readonly Name NAME_s_PlayerWalkingCr0H = new("s_PlayerWalkingCr0H");
    private static readonly Name NAME_s_PlayerWalkingSt2H = new("s_PlayerWalkingSt2H");

    /// <summary>A state version of AntiBugTellServerIAmDead (the spectating states and s_WaitRespawn ignore it).</summary>
    public interface IAntiBugTellServerIAmDead
    {
        void AntiBugTellServerIAmDead(SPlayerController self);
    }

    /// <summary>The level's hooks into other areas.</summary>
    protected IGameFlowPorts Ports => GameFlowServices.Ports(this);

    private SGameInfo? SGame => Level.Game as SGameInfo;

    /// <summary>
    /// The controller's start values, on the server and on the owning client alike: the spy starts at running speed,
    /// and no recent hit is on record. The controller outlives its pawns, so a respawn keeps whatever speed the player
    /// chose since.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:PostBeginPlay", Spec = "spec/movement/input-and-rotation.md#speed-mode-getspeedmode-changespeed")]
    [Provenance(ProvenanceKind.Decoded, "PostBeginPlay: fTimeWhenHit -10, bFastSpeed true, bOldHoldBreath 0; the goggles spawn in EnsureGoggle and the RJ45 checker, camera and weird effect are not here")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        fTimeWhenHit = MovementK.AdrenalineReset;
        bFastSpeed = true;
        bOldHoldBreath = 0;
    }

    // ---- readiness and the match start ---------------------------------------------------------------------

    /// <summary>Client to server: the player finished loading; after the start, a waiting Merc Deathmatch player joins.</summary>
    [Uc("SBase.SPlayerController.PlayerWaitingToPlayToServer", Sha1 = "486cdf37d50764eade4f417225894a8c6c425e77")]
    [PortOf("uc:SBase.SPlayerController:PlayerWaitingToPlayToServer", Spec = "spec/game/login-and-teams.md#rules")]
    public void PlayerWaitingToPlayToServer(bool _bWaiting)
    {
        if (PlayerReplicationInfo is { } pri)
        {
            pri.bWaitingPlayer = _bWaiting;
        }

        if (_bWaiting)
        {
            SGame?.StartLateJoiner(this);
        }
    }

    /// <summary>Client to server: the player only watches (the team size is not changed).</summary>
    [Uc("SBase.SPlayerController.PlayerOnlySpectatorToServer", Sha1 = "1ea55cceeb22a878321be4c24055b2f60ef67aa4")]
    [PortOf("uc:SBase.SPlayerController:PlayerOnlySpectatorToServer", Spec = "spec/game/login-and-teams.md#rules")]
    public void PlayerOnlySpectatorToServer(bool _bOnlySpectator)
    {
        if (PlayerReplicationInfo is { } pri)
        {
            pri.bOnlySpectator = _bOnlySpectator;
        }

        if (_bOnlySpectator && Level.Game is { } game)
        {
            game.NumPlayers--;
            game.NumSpectators++;
        }
    }

    /// <summary>Client to server: ready flag of the end-of-match menus.</summary>
    [Uc("SBase.SPlayerController.PlayerReadyToPlayToServer", Sha1 = "d36bb24c31f3318d30c95924f3e4527ddd0f9a15")]
    [PortOf("uc:SBase.SPlayerController:PlayerReadyToPlayToServer", Spec = "spec/game/login-and-teams.md#rules")]
    public void PlayerReadyToPlayToServer(bool _bReady)
    {
        if (PlayerReplicationInfo is { } pri)
        {
            pri.bReadyToPlay = _bReady;
        }
    }

    /// <summary>The listen host starts the match: bMatchHasBegun first, so every start spawn sees it set.</summary>
    [Uc("SBase.SPlayerController.StartMatchOnServer", Sha1 = "63cb6be293045a03d99316e42cde7468b1dd15a9")]
    [PortOf("uc:SBase.SPlayerController:StartMatchOnServer", Spec = "spec/game/match-lifecycle.md#rules")]
    public void StartMatchOnServer()
    {
        if (GameReplicationInfo is { } gri)
        {
            gri.bMatchHasBegun = true;
        }

        Level.Game?.StartMatch();
    }

    /// <summary>Server to client: the lobby countdown flags of this machine's copy of the game info.</summary>
    [Uc("SBase.SPlayerController.launchMatchNow", Sha1 = "47f6e8efb693f54eef41380fcbcdecb7c1f30797")]
    [PortOf("uc:SBase.SPlayerController:launchMatchNow", Spec = "spec/game/match-lifecycle.md#rules")]
    public void launchMatchNow()
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("launchMatchNow"));
            return;
        }

        if (GameReplicationInfo is { } gri)
        {
            gri.bMatchLaunchingNow = true;
            gri.bMatchLaunching = true;
        }
    }

    /// <summary>Server to client: the host's cinematic camera setting.</summary>
    [Uc("SBase.SPlayerController.ClientSetCinematicCamera", Sha1 = "80f11bcbabc82c8ba525341a66e0df20130b1817")]
    [PortOf("uc:SBase.SPlayerController:ClientSetCinematicCamera", Spec = "spec/game/match-lifecycle.md#rules")]
    public void ClientSetCinematicCamera(bool bAvailable)
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("ClientSetCinematicCamera") { Bool = bAvailable });
            return;
        }

        bAvlb_CinematicCamera = bAvailable;
        if (GameReplicationInfo is { } gri)
        {
            gri.bAvlb_CinematicCamera = bAvailable;
        }
    }

    // ---- possession and the start state --------------------------------------------------------------------

    /// <summary>
    /// The server gives this controller its new pawn: the interaction manager once, then (not for an only-spectator)
    /// location, the spy camera rotation from the start, possession and Restart. Not ported here: the saved move
    /// reset (net) and ServerSetHandedness (view).
    /// </summary>
    [Uc("SBase.SPlayerController.Possess", Sha1 = "07fe15922022468e574d1abbcf0fa0ec906b9f7d")]
    [PortOf("uc:SBase.SPlayerController:Possess", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void Possess(Pawn aPawn)
    {
        if (Role == ENetRole.ROLE_Authority && IManager is null)
        {
            IManager = Spawn<SInteractionManager>(this);
        }

        if (PlayerReplicationInfo?.bOnlySpectator ?? false)
        {
            return;
        }

        Location = aPawn.Location;
        if (StartSpot is { } start)
        {
            Rotation = new FRotator(start.Rotation.Pitch + start.iSpyCam_InitAngle_Ver,
                start.Rotation.Yaw + start.iSpyCam_InitAngle_Hor, start.Rotation.Roll);
        }
        else
        {
            Rotation = aPawn.Rotation;
        }

        aPawn.PossessedBy(this);
        Pawn = aPawn;
        SBPawn = aPawn as SBasePawn;
        aPawn.bStasis = false;
        Restart();
    }

    /// <summary>
    /// The server takes the pawn away and destroys it (the dead body goes with it). Not ported here: the saved move
    /// reset (net).
    /// </summary>
    [Uc("SBase.SPlayerController.UnPossess", Sha1 = "1eaf9ce248c6d24cea7300b03f46f0933d2d1768")]
    [PortOf("uc:SBase.SPlayerController:UnPossess", Spec = "spec/game/death-lives-respawn.md#rules")]
    public override void UnPossess()
    {
        if (Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        if (Pawn is { } pawn)
        {
            Location = pawn.Location;
            pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
            pawn.UnPossessed();
            if (ReferenceEquals(ViewTarget, pawn))
            {
                SetViewTarget(this);
            }

            pawn.Destroy();
        }

        Pawn = null;
        SBPawn = null;
    }

    /// <summary>
    /// After a spawn, on the server and the owning machine: 3 s of explosion protection for spies, the zone tips
    /// around the start, the side's FOV and start state (spies crouched without a weapon, mercs standing with the
    /// rifle), then the state on this machine and, from the server, on the owning client.
    /// </summary>
    [Uc("SBase.SPlayerController.EnterStartState", Sha1 = "57d15df2c32b9f7eee4244e0fcfb8dd4bb544e57")]
    [PortOf("uc:SBase.SPlayerController:EnterStartState", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void EnterStartState()
    {
        fSpawnProtectionEnd = Level.TimeSeconds + K.SpawnProtection;
        LocalMyDesiredRotation = Rotation;
        MyDesiredRotation = Rotation;
        if (!ReferenceEquals(SBPawn, Pawn))
        {
            SBPawn = Pawn as SBasePawn;
        }

        if (SBPawn is { } pawn)
        {
            pawn.bActionsSendByServerMove = false;
            pawn.bActionsHasBeenSentByServerMove = false;
        }

        bTazered = false;
        FVector startLocation = StartSpot?.Location ?? default;
        foreach (SZoneTips tip in XLevel.AllActors<SZoneTips>())
        {
            if (!tip.bUseCylinderCollision || Pawn is null)
            {
                continue;
            }

            FVector d = startLocation - tip.Location;
            if (d.X * d.X + d.Y * d.Y <= tip.CollisionRadius * tip.CollisionRadius &&
                d.Z * d.Z <= tip.CollisionHeight * tip.CollisionHeight)
            {
                Ports.ZoneTipTouch(tip, Pawn);
            }
        }

        // A missing pawn reads SPawnType 0, the spy side.
        bool bSpy = (SBPawn?.SPawnType ?? PawnType.SPAWNOFFENSE) == PawnType.SPAWNOFFENSE;
        Name newState;
        if (bSpy)
        {
            DefaultFOV = SpyFOV;
            bCrouchWithRoll = true;
            newState = NAME_s_PlayerWalkingCr0H;
        }
        else
        {
            DefaultFOV = DefenderFOV;
            newState = NAME_s_PlayerWalkingSt2H;
            if (!bLaserEtaitAllume)
            {
                bNoReticule = false;
            }
        }

        if (SBPawn is { } startPawn)
        {
            Ports.EnterStartState(this, startPawn, bSpy);
        }

        DesiredFOV = DefaultFOV;
        FovAngle = DefaultFOV;
        if (SBPawn is not null)
        {
            SBPawn.LastZipP = K.FarZipPoint;
        }

        if (IAmOnClient())
        {
            if (IsInState(newState))
            {
                eventBeginState();
            }
            else
            {
                GotoState(newState);
            }
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            eventMirrorState(newState);
        }
    }

    /// <summary>
    /// Server to owner: take the new pawn, or wait for it to arrive (WaitingForPawn). Not ported here: the blob
    /// shadow (view) and the saved move reset (net).
    /// </summary>
    [Uc("SBase.SPlayerController.ClientRestart", Sha1 = "b9ecf3c85e81e3c501fb84288902082dd2c14c0c")]
    [PortOf("uc:SBase.SPlayerController:ClientRestart", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void ClientRestart()
    {
        if (Pawn is { bTearOff: true } torn)
        {
            torn.Controller = null;
            Pawn = null;
        }

        if (Pawn is null)
        {
            GotoState(NAME_WaitingForPawn);
            return;
        }

        bFireMode = 0;
        LocalMyDesiredRotation = Rotation;
        Pawn.ClientRestart();
        SetViewTarget(Pawn);
        bBehindView = Pawn.PointOfView();

        // Port safety: a new pawn never starts with the camera on a head bone (a round can end mid-grab).
        CombatHeadCamera(false, default);
        EnterStartState();
    }

    /// <summary>
    /// Server to owner: hands the server's pawn to the owning machine. As in the original, SBPawn first takes the
    /// previous pawn and is refreshed by the start state.
    /// </summary>
    [Uc("SBase.SPlayerController.GivePawn", Sha1 = "c428f2fcae80fcdb4af06ad0c689d3262c0b2a4d")]
    [PortOf("uc:SBase.SPlayerController:GivePawn", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void GivePawn(Pawn? NewPawn)
    {
        if (NewPawn is null)
        {
            return;
        }

        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("GivePawn") { Actor = NewPawn });
            return;
        }

        SBPawn = Pawn as SBasePawn;
        Pawn = NewPawn;
        NewPawn.Controller = this;
        ClientRestart();
    }

    // ---- death, respawn and resign -------------------------------------------------------------------------

    /// <summary>
    /// The server removes the dead body and restarts the player through s_WaitRespawn. Releases the voice chat
    /// partners; TalkToMate (voice spec) is not ported.
    /// </summary>
    [Uc("SBase.SPlayerController.ServerRespawn", Sha1 = "b6afa6e0f0151bc1a946fbee537a457df67c6c1b")]
    [PortOf("uc:SBase.SPlayerController:ServerRespawn", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void ServerRespawn()
    {
        if (Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        ReleaseChatPartners();
        UnPossess();
        GotoState(NAME_s_WaitRespawn);
    }

    [Uc("SBase.SPlayerController.Respawn", Sha1 = "40f74bf0570202a2c07c45d2e4b6a920b312e21c")]
    [PortOf("uc:SBase.SPlayerController:Respawn", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void Respawn() => ServerRespawn();

    /// <summary>
    /// The player gives up: refused before the start, for spectators, without lives, at the end or while blasted to
    /// the floor. A living pawn dies by its own hand with its last life; otherwise the lives drop to 0 with the death
    /// and last-life messages and the end check.
    /// </summary>
    [Uc("SBase.SPlayerController.ServerResign", Sha1 = "210eaff07067cda2f11fd69656adb0f8eff9468a")]
    [PortOf("uc:SBase.SPlayerController:ServerResign", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void ServerResign()
    {
        PlayerReplicationInfo? pri = PlayerReplicationInfo;
        if (!(GameReplicationInfo?.bMatchHasBegun ?? false) || (pri?.bOnlySpectator ?? false) || (pri?.NumLives ?? 0) < 1 ||
            IsInState(NAME_GameEnded) || IsInState(NAME_s_BlastFloor) || pri is null)
        {
            return;
        }

        pri.bResigned = true;
        if (Pawn is { Health: > 0 } pawn)
        {
            pri.NumLives = 1;
            PawnTakeDamage(pawn.Health + K.ResignExtraDamage, pawn, pawn.Location, default, SDamageType.Suicided);
            return;
        }

        pri.NumLives = 0;
        if (SGame is { } game)
        {
            game.BroadcastDeathMessage(this, this, SDamageType.Suicided);
            game.BroadcastTeamLocalizedMessage((byte)SGameInfo.TeamIndexOf(this), new ClassRef("SBase.SGameMessages"),
                K.MessageLastLife, pri, null, game);
            if (game.sCheckEndGame())
            {
                game.sEndGame();
            }
        }
    }

    [Uc("SBase.SPlayerController.Resign", Sha1 = "59f64cee992cf6bb6ea83a8081e8772ecaea0b7b")]
    [PortOf("uc:SBase.SPlayerController:Resign", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void Resign() => ServerResign();

    /// <summary>AntiBugTellServerIAmDead through the current state.</summary>
    public void eventAntiBugTellServerIAmDead()
    {
        if (StateNode is IAntiBugTellServerIAmDead s)
        {
            s.AntiBugTellServerIAmDead(this);
        }
        else
        {
            AntiBugTellServerIAmDead();
        }
    }

    /// <summary>Client to server, every second while dead: the server copy enters s_Dead if it is not there.</summary>
    [Uc("SBase.SPlayerController.AntiBugTellServerIAmDead", Sha1 = "fa429da9775d3b189036d179fa4f5281f69d5a1c")]
    [PortOf("uc:SBase.SPlayerController:AntiBugTellServerIAmDead", Spec = "spec/game/death-lives-respawn.md#rules")]
    protected virtual void AntiBugTellServerIAmDead()
    {
        if (!IsInState(NAME_s_Dead))
        {
            GotoState(NAME_s_Dead);
        }
    }

    // ---- spectator targets ---------------------------------------------------------------------------------

    /// <summary>
    /// A dead player watches the next living teammate (same side, with a pawn) after the current one; with
    /// <paramref name="bCheckOnly"/> the current target is kept while it is still valid. Only in
    /// s_MultiSpectating_WhenKilled. Not ported here: the camera mode change (camera spec).
    /// </summary>
    [Uc("SBase.SPlayerController.Server_FindNextFriend", Sha1 = "bae1ff480e9e8278c288e3a5e843f5d696b2df81")]
    [PortOf("uc:SBase.SPlayerController:Server_FindNextFriend", Spec = "spec/game/spectating.md#rules")]
    public void Server_FindNextFriend(bool bCheckOnly)
    {
        if (Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        if (bCheckOnly && SpectateTargetStillValid())
        {
            return;
        }

        if (!IsInState(NAME_s_MultiSpectating_WhenKilled))
        {
            return;
        }

        AttachToNextPlayer(sameSideOnly: true, muteForOthers: false);
    }

    /// <summary>
    /// A visitor (only-spectator) watches the next player of either side with a pawn. The state is set to
    /// s_Spectating_Visitor first, on any machine; every player stops hearing the visitor.
    /// </summary>
    [Uc("SBase.SPlayerController.Server_FindNextVisitor", Sha1 = "f302d8f3510500cb00ad4723d9a6a02504c5f283")]
    [PortOf("uc:SBase.SPlayerController:Server_FindNextVisitor", Spec = "spec/game/spectating.md#rules")]
    public void Server_FindNextVisitor(bool bCheckOnly)
    {
        GotoState(NAME_s_Spectating_Visitor);
        if (Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        if (bCheckOnly && SpectateTargetStillValid())
        {
            return;
        }

        AttachToNextPlayer(sameSideOnly: false, muteForOthers: true);
    }

    /// <summary>The watched controller still has the watched pawn and is not itself dead and spectating.</summary>
    private bool SpectateTargetStillValid() =>
        PC_spectated is SPlayerController watched && SpecTarget is not null && watched.SBPawn is not null &&
        ReferenceEquals(watched.SBPawn, SpecTarget) && !watched.IsInState(NAME_s_MultiSpectating_WhenKilled);

    private void AttachToNextPlayer(bool sameSideOnly, bool muteForOthers)
    {
        // The original keeps at most 12 candidates in a fixed array; further controllers are dropped.
        var found = new PlayerController?[K.SpectateCandidateSlots];
        int nbPc = -1;
        int myPc = -1;
        foreach (PlayerController pc in XLevel.AllActors<PlayerController>())
        {
            if (muteForOthers && SGame is { } game)
            {
                Ports.HearVoiceChatter(game, pc, this, true);
            }

            nbPc++;
            if (nbPc < found.Length)
            {
                found[nbPc] = pc;
            }

            if (ReferenceEquals(PC_spectated, pc))
            {
                myPc = nbPc;
            }
        }

        bool bFound = false;
        for (int loop = 0; loop <= nbPc; loop++)
        {
            myPc++;
            if (myPc > nbPc)
            {
                myPc = 0;
            }

            if (myPc < found.Length && found[myPc] is SPlayerController candidate && candidate.SBPawn is not null &&
                (!sameSideOnly || candidate.SPawnType == SPawnType) && !ReferenceEquals(candidate, this))
            {
                PC_spectated = candidate;
                SpecTarget = candidate.SBPawn;
                bForceReplicationForSpecTarget = true;
                bFound = true;
                break;
            }
        }

        if (bFound)
        {
            UnPossess();
            SetViewTarget(SpecTarget);
            ClientSetViewTarget(SpecTarget);
        }
    }

    private static readonly Name GNAME_s_Spectating_Visitor = new("s_Spectating_Visitor");

    /// <summary>
    /// Server to owner: the Engine rotation set, and the desired rotations follow it so the aim does not swing back
    /// (respawn-and-spawning.md rule 7).
    /// </summary>
    [Uc("SBase.SPlayerController.ClientSetRotation", Sha1 = "8cc2abd82be36ddcd9eb6dbf69c3e54d741a9fd0")]
    [PortOf("uc:SBase.SPlayerController:ClientSetRotation", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void ClientSetRotation(FRotator NewRotation)
    {
        base.ClientSetRotation(NewRotation);
        MyDesiredRotation = Rotation;
        LocalMyDesiredRotation = Rotation;
    }

    /// <summary>
    /// The camera follows <paramref name="A"/> (spectating.md rule 5a). A visitor whose new view pawn is of the other side
    /// drops a goggle mode that side has no use for: EMF or motion tracking on a spy, night or thermal on a merc, to
    /// the plain view.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientSetViewTarget", Sha1 = "4449b1177fc44ed4a299f4f5459b14605da1b073")]
    [PortOf("uc:SBase.SPlayerController:ClientSetViewTarget", Spec = "spec/game/spectating.md#rules")]
    public override void ClientSetViewTarget(Actor? A)
    {
        base.ClientSetViewTarget(A);
        if (IsRemotePlayer || !IsInState(GNAME_s_Spectating_Visitor) || A is not SBasePawn watched || Goggle is not { } goggle)
        {
            return;
        }

        bool drop = goggle.CurrentMode switch
        {
            SGoggle.REN_EMFVision or SGoggle.REN_MotionTrackingVision => watched.SPawnType == PawnType.SPAWNOFFENSE,
            SGoggle.REN_NightVision or SGoggle.REN_ThermalVision => watched.SPawnType == PawnType.SPAWNDEFENSE,
            _ => false,
        };
        if (drop)
        {
            goggle.CurrentMode = SGoggle.REN_DynLight;
            goggle.Activate();
        }
    }

    // ---- end menus and travel ------------------------------------------------------------------------------

    [Uc("SBase.SPlayerController.ReadyToPlayAgain", Sha1 = "dca85e298e88b4bb488eacf64004f0546fd84062")]
    [PortOf("uc:SBase.SPlayerController:ReadyToPlayAgain", Spec = "spec/game/round-travel.md#rules")]
    public void ReadyToPlayAgain(bool B) => ServerReadyToPlayAgain(B);

    [Uc("SBase.SPlayerController.ServerReadyToPlayAgain", Sha1 = "2a8144128493baaf90c03bafceecbbb520e1f809")]
    [PortOf("uc:SBase.SPlayerController:ServerReadyToPlayAgain", Spec = "spec/game/round-travel.md#rules")]
    public void ServerReadyToPlayAgain(bool B)
    {
        if (PlayerReplicationInfo is { } pri)
        {
            pri.bReadyToPlayAgain = B;
        }
    }

    /// <summary>Server to client: back to the lobby page (the launch flag of this machine's game info is cleared).</summary>
    [Uc("SBase.SPlayerController.CLientForceMenuLobby", Sha1 = "5db5f05b2e0bcbc4ca7aa6899ae380aa52fae740")]
    [PortOf("uc:SBase.SPlayerController:CLientForceMenuLobby", Spec = "spec/game/round-travel.md#rules")]
    public void CLientForceMenuLobby()
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("CLientForceMenuLobby"));
            return;
        }

        if (GameReplicationInfo is { } gri)
        {
            gri.bMatchLaunching = false;
        }

        GameHost.ClientCall(this, new GameCall("ClientCloseMenu") { Bool = true });
        GameHost.ClientCall(this, new GameCall("ClientOpenMenu") { Name1 = new Name("BackToLobby") });
    }

    /// <summary>Server to client: close the menus and leave for the entry page ("the host has ended the session").</summary>
    [Uc("SBase.SPlayerController.ClientForceMenuEntry", Sha1 = "31d7d2366d48d6c363df887f414db640c407608b")]
    [PortOf("uc:SBase.SPlayerController:ClientForceMenuEntry", Spec = "spec/game/round-travel.md#rules")]
    public void ClientForceMenuEntry()
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("ClientForceMenuEntry"));
            return;
        }

        GameHost.ClientCall(this, new GameCall("ClientCloseMenu") { Bool = true });
        GameHost.ClientCall(this, new GameCall("ClientOpenMenu") { Name1 = new Name("QuitMatch_Page"), Text = ClientQuitByServer });
    }

    /// <summary>Server to client: the client's menu clears its ready mark (script event 4).</summary>
    [Uc("SBase.SPlayerController.ForceUnreadyOnClient", Sha1 = "ceeb7a040128520f6d8000a8d20fdca6e580b012")]
    [PortOf("uc:SBase.SPlayerController:ForceUnreadyOnClient", Spec = "spec/game/login-and-teams.md#network")]
    public void ForceUnreadyOnClient()
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("ForceUnreadyOnClient"));
            return;
        }

        GameHost.OnScriptEvent(this, 4);
    }

    // ---- Merc Deathmatch respawn weapon --------------------------------------------------------------------

    [Uc("SBase.SPlayerController.CanPickRespawnWeapon", Sha1 = "fdaa5d6563f032153bf663fd4e4ece3a619998f6")]
    [PortOf("uc:SBase.SPlayerController:CanPickRespawnWeapon", Spec = "spec/game/death-lives-respawn.md#rules")]
    public bool CanPickRespawnWeapon() =>
        GameReplicationInfo is { GameMode: EGameMode.GM_MercDeathMatch } && (PlayerReplicationInfo?.NumLives ?? 0) > 0 &&
        IsInState(NAME_s_Dead);

    /// <summary>
    /// The owner starts the weapon pick: remembers whether Use is held. The Use key name for the prompt comes from
    /// the key bindings (input layer, not ported); "Use" stands in.
    /// </summary>
    [Uc("SBase.SPlayerController.BeginRespawnWeaponPick", Sha1 = "7d7dcfcce8241ad101450f6161ebe36b6ef76e16")]
    [PortOf("uc:SBase.SPlayerController:BeginRespawnWeaponPick", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void BeginRespawnWeaponPick()
    {
        if (!IAmOnClient() || !CanPickRespawnWeapon())
        {
            return;
        }

        bRespawnWeaponKeyHeld = bUsing > 0;
        RespawnWeaponKey = "Use";
    }

    /// <summary>Each dead tick: a Use press (edge) cycles the respawn weapon.</summary>
    [Uc("SBase.SPlayerController.CheckRespawnWeaponKey", Sha1 = "b3770cb5a50151e4a9fc4c2c1a0da32ee82b703f")]
    [PortOf("uc:SBase.SPlayerController:CheckRespawnWeaponKey", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void CheckRespawnWeaponKey()
    {
        if (bUsing > 0 && !bRespawnWeaponKeyHeld && CanPickRespawnWeapon())
        {
            ServerCycleRespawnWeapon();
        }

        bRespawnWeaponKeyHeld = bUsing > 0;
    }

    /// <summary>Client to server: the next weapon of the team list after the current one (wrapping).</summary>
    [Uc("SBase.SPlayerController.ServerCycleRespawnWeapon", Sha1 = "3cb2bacb0b5e226bd8f1ff489cc1b26c6d10bead")]
    [PortOf("uc:SBase.SPlayerController:ServerCycleRespawnWeapon", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void ServerCycleRespawnWeapon()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            GameHost.ServerCall(this, new GameCall("ServerCycleRespawnWeapon"));
            return;
        }

        if (!CanPickRespawnWeapon() || PlayerReplicationInfo?.Team is not STeamInfo team || team.ClassWeapon.Count == 0)
        {
            return;
        }

        int i = MercWeaponIndex(RespawnWeapon) + 1;
        if (i >= team.ClassWeapon.Count)
        {
            i = 0;
        }

        RespawnWeapon = team.ClassWeapon[i];
        ClientSetRespawnWeapon(RespawnWeapon);
    }

    /// <summary>The weapon a respawn gets: the request outside Merc Deathmatch, else the chosen weapon.</summary>
    [Uc("SBase.SPlayerController.SpawnWeaponFor", Sha1 = "7d822ab59ca57b1b2e91d3c7e46f5b3502cdceef")]
    [PortOf("uc:SBase.SPlayerController:SpawnWeaponFor", Spec = "spec/game/death-lives-respawn.md#rules")]
    public ClassRef SpawnWeaponFor(ClassRef Requested)
    {
        if (GameReplicationInfo?.GameMode != EGameMode.GM_MercDeathMatch)
        {
            return Requested;
        }

        if (RespawnWeapon.IsNone && PlayerReplicationInfo?.Team is STeamInfo team && team.ClassWeapon.Count > 0)
        {
            RespawnWeapon = team.ClassWeapon[SvmMath.Max(MercWeaponIndex(Requested), 0)];
            ClientSetRespawnWeapon(RespawnWeapon);
        }

        return RespawnWeapon;
    }

    /// <summary>The index of a weapon in the team's weapon list, or -1.</summary>
    [Uc("SBase.SPlayerController.MercWeaponIndex", Sha1 = "aa311762dcbb9a1fc5de913f67bec3b59baf66c0")]
    [PortOf("uc:SBase.SPlayerController:MercWeaponIndex", Spec = "spec/game/death-lives-respawn.md#rules")]
    public int MercWeaponIndex(ClassRef Weapon)
    {
        if (PlayerReplicationInfo?.Team is not STeamInfo team)
        {
            return -1;
        }

        for (int i = 0; i < team.ClassWeapon.Count; i++)
        {
            if (team.ClassWeapon[i] == Weapon)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Server to client: the chosen respawn weapon.</summary>
    [Uc("SBase.SPlayerController.ClientSetRespawnWeapon", Sha1 = "499fed8afc52f81012316bfebee4027f5fc324c7")]
    [PortOf("uc:SBase.SPlayerController:ClientSetRespawnWeapon", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void ClientSetRespawnWeapon(ClassRef Weapon)
    {
        if (IsRemotePlayer)
        {
            GameHost.ClientCall(this, new GameCall("ClientSetRespawnWeapon") { Class = Weapon });
            return;
        }

        RespawnWeapon = Weapon;
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    /// <summary>Releases the voice chat partners: each one is heard again by its side, as the original does on death and respawn.</summary>
    private void ReleaseChatPartners()
    {
        if (SGame is not { } game)
        {
            return;
        }

        for (int i = 0; i < PCChattingWith.Length; i++)
        {
            if (PCChattingWith[i] is not { } partner)
            {
                continue;
            }

            Ports.HearVoiceChatter(game, this, partner, true);
            foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
            {
                if (pc.SPawnType == partner.SPawnType && !ReferenceEquals(pc, partner))
                {
                    Ports.HearVoiceChatter(game, pc, partner, false);
                }
            }

            PCChattingWith[i] = null;
        }
    }
}
