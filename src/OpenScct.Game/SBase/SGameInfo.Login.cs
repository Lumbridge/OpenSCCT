using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SGameInfo: login, team choice, late joiners and logout. Written from spec/game/login-and-teams.md and
// spec/game/match-lifecycle.md (PostLogin).
public partial class SGameInfo
{
    private static readonly Name NAME_s_Spectating_Visitor = new("s_Spectating_Visitor");
    private static readonly Name NAME_s_PlayerWaiting = new("s_PlayerWaiting");

    private GameMessage? _gameMessageDefaults;

    /// <summary>The class default object of GameMessageClass (login failure texts).</summary>
    private GameMessage GameMessageDefaults =>
        _gameMessageDefaults ??= GetClassIndex().Find(GameMessageClass)?.Create() as GameMessage ?? new GameMessage();

    /// <summary>
    /// Turns a connection into a player controller. Capacity first, then the requested team (swapped in the second
    /// game of a revenge pair, 255 for a non-playing host), a controller at a spy start (or far away for the host),
    /// spectators and late joiners as visitors, the team, side, FOV, mesh and pawn class, the editor's direct start,
    /// and the reconnect take-over of a disconnected pawn. Returns null with <paramref name="Error"/> on failure.
    /// </summary>
    [Uc("SBase.SGameInfo.Login", Sha1 = "c0bf98c189db5accb64bc8b5a44dfd40e8995360")]
    [PortOf("uc:SBase.SGameInfo:Login", Spec = "spec/game/login-and-teams.md#rules")]
    public override PlayerController? Login(string Portal, string Options, ref string Error)
    {
        bool bSpectator = UcStrings.EqualsIgnoreCase(ParseOption(Options, "SpectatorOnly"), "true");
        bool bNewProfile = UcStrings.EqualsIgnoreCase(ParseOption(Options, "NewProfile"), "true");
        bool bServerOnly = UcStrings.EqualsIgnoreCase(ParseOption(Options, "ServerOnly"), "true");
        if (AtCapacity(bSpectator))
        {
            Error = GameMessageDefaults.MaxedOutMessage;
            return null;
        }

        string inName = UcStrings.Left(ParseOption(Options, "Name"), K.PlayerNameLength);
        byte inTeam = (byte)GetIntOption(Options, "Team", 0);
        InLAN = ParseOption(Options, "LAN");
        int pbOption = GetIntOption(Options, "PB", 0);

        if (MatchInfo is { Revenge: true } && GetIntOption(Options, "GamesInARow", K.RevengeGamesInARowDefault) < 2)
        {
            inTeam = inTeam switch
            {
                0 => 1,
                1 => 0,
                _ => inTeam,
            };
        }

        if (bServerOnly)
        {
            inTeam = K.SpectatorTeam;
        }

        if (PlayerControllerClass.IsNone)
        {
            PlayerControllerClass = new ClassRef("SBase.SPlayerController");
        }

        PlayerStart? startSpot = null;
        PlayerController? newPlayer;
        if (bServerOnly)
        {
            newPlayer = UcSpawn.Spawn(this, PlayerControllerClass, null, K.ServerOnlyLocation, default(FRotator)) as PlayerController;
            if (newPlayer is SPlayerController host)
            {
                host.MyDesiredRotation = default;
            }
        }
        else
        {
            // The controller argument is None here, so FindPlayerStart reads side 0: the login start is a spy
            // start whatever team was asked for. A missing start reads as the origin.
            startSpot = FindPlayerStart(null, inTeam, Portal);
            FRotator initial = SpyCameraRotation(startSpot);
            newPlayer = UcSpawn.Spawn(this, PlayerControllerClass, null, startSpot?.Location ?? default, initial) as PlayerController;
            if (newPlayer is SPlayerController spc)
            {
                spc.MyDesiredRotation = startSpot?.Rotation ?? default;
            }
        }

        if (newPlayer is null)
        {
            Error = GameMessageDefaults.FailedSpawnMessage;
            return null;
        }

        newPlayer.iPBEnabled = newPlayer.Player is UViewport ? (Host.IsPBClientEnabled() ? 1 : 0) : pbOption;
        if (GameReplicationInfo is not null)
        {
            GameReplicationInfo.m_ActualMapName = LevelSettings?.MapName ?? string.Empty;
        }

        newPlayer.StartSpot = startSpot;
        if (newPlayer is SPlayerController withProfile)
        {
            withProfile.bNewProfile = bNewProfile;
        }

        newPlayer.GameReplicationInfo = GameReplicationInfo;
        newPlayer.PlayerSecurity = UcSpawn.Spawn(this, SecurityClass, this) as Security;
        ChangeName(newPlayer, inName, false);

        PlayerReplicationInfo? pri = newPlayer.PlayerReplicationInfo;
        bool matchHasBegun = GameReplicationInfo?.bMatchHasBegun ?? false;
        // OpenSCCT's own: a player the online server lets back into the round he dropped out of (?SvmRejoin=1) is
        // not made a visitor; he waits on his side like a Merc Deathmatch late joiner and the server spawns him.
        bool bRejoin = ParseOption(Options, "SvmRejoin") == "1";
        if (inTeam == K.SpectatorTeam ||
            (matchHasBegun && !bRejoin && (MatchInfo?.GameMode != EGameMode.GM_MercDeathMatch || bGameEnded)))
        {
            bWelcomePending = true;
            if (pri is not null)
            {
                pri.bOnlySpectator = true;
                pri.bIsSpectator = true;
            }

            NumSpectators++;
            if (newPlayer is SPlayerController)
            {
                newPlayer.GotoState(NAME_s_Spectating_Visitor);
            }

            return newPlayer;
        }

        inTeam = PickTeam(inTeam, newPlayer);
        if (!eventChangeTeam(newPlayer, inTeam, false))
        {
            Error = GameMessageDefaults.FailedTeamMessage;
            return null;
        }

        if (newPlayer is SPlayerController side)
        {
            if (TeamIndexOf(newPlayer) == 0)
            {
                side.SPawnType = PawnType.SPAWNOFFENSE;
                side.DefaultFOV = side.SpyFOV;
                side.DesiredFOV = side.SpyFOV;
            }
            else
            {
                side.SPawnType = PawnType.SPAWNDEFENSE;
                side.DefaultFOV = side.DefenderFOV;
                side.DesiredFOV = side.DefenderFOV;
            }
        }

        if (startSpot is null)
        {
            Error = GameMessageDefaults.FailedPlaceMessage;
            return null;
        }

        byte inMesh = (byte)GetIntOption(Options, "Mesh", 0);
        if (pri is not null)
        {
            pri.PlayerID = CurrentID++;
            pri.MyMeshNum = inMesh;
            // OpenSCCT's own: the cosmetic look the online server checked against the player's unlocks.
            if (long.TryParse(ParseOption(Options, "SvmLook"), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out long look))
            {
                pri.SvmLookLow = (int)(look & 0xFFFFFFFFL);
                pri.SvmLookHigh = (int)(look >> 32);
            }

            // OpenSCCT's own: the level, prestige and title the online server checked against the player's ticket.
            pri.SvmRank = GetIntOption(Options, "SvmRank", 0);
        }

        Name pawnClassName = pri?.Team is STeamInfo team && inMesh < team.PawnList.Count ? team.PawnList[inMesh] : Name.None;
        newPlayer.SetPawnClass(pawnClassName);
        NumPlayers++;

        if (UcStrings.EqualsIgnoreCase(ParseOption(Options, "Editeur"), "true"))
        {
            bDelayedStart = false;
            bWaitingToStartMatch = true;
            Host.OnScriptEvent(newPlayer, 3);
        }

        if (bDelayedStart || matchHasBegun)
        {
            newPlayer.GotoState(NAME_s_PlayerWaiting);
            return newPlayer;
        }

        // A disconnected player's living pawn with the same owner name is taken over by the new connection.
        foreach (Pawn testPawn in XLevel.DynamicActors<Pawn>())
        {
            if (testPawn.Controller is PlayerController old && old.Player is null && testPawn.Health > 0 &&
                UcStrings.EqualsIgnoreCase(testPawn.OwnerName, inName))
            {
                newPlayer.Destroy();
                testPawn.Rotation = old.Rotation;
                testPawn.bInitializeAnimation = false;
                Ports.PlayWaiting(testPawn);
                return old;
            }
        }

        return newPlayer;
    }

    /// <summary>
    /// Refreshes the replicated game info from the match and host settings, flags the LAN host's PRI, then runs the
    /// Engine PostLogin with the start forced to wait once the match has begun.
    /// </summary>
    [Uc("SBase.SGameInfo.PostLogin", Sha1 = "3fc446f7d35b9fc5672b955a0047a572134dc156")]
    [PortOf("uc:SBase.SGameInfo:PostLogin", Spec = "spec/game/match-lifecycle.md#rules")]
    public override void PostLogin(PlayerController NewPlayer)
    {
        if (GameReplicationInfo is not null)
        {
            GameReplicationInfo.ShortName = Level.GetLocalPlayerController()?.PlayerReplicationInfo?.PlayerName ?? string.Empty;
        }

        USPersistentData pd = PersistentData;
        if (NewPlayer.GameReplicationInfo is { } g)
        {
            USMatchInfo? m = MatchInfo;
            g.ServerName = pd.LevelName;
            g.TimeLimit = m?.TimeLimit ?? 0;
            g.GamesInARow = m?.GamesInARow ?? 0;
            g.TeamLivesATT = m?.NumLivesATT ?? 0;
            g.TeamLivesDEF = m?.NumLivesDEF ?? 0;
            g.TimesRespawn = m?.TimesRespawn ?? 0;
            g.GameMode = m?.GameMode ?? EGameMode.GM_Undefined;
            g.ServerOnly = pd.bDedicatedServer;
            g.Revenge = m?.Revenge ?? false;
            g.bRandomMap = pd.bRandomMap;
            g.GameWithRanking = m?.GameWithRanking ?? false;
            g.bAvlb_SPY_ThermalVision = pd.bAvlb_SPY_ThermalVision;
            g.bAvlb_SPY_NightVision = pd.bAvlb_SPY_NightVision;
            g.bAvlb_SPY_Grab = pd.bAvlb_SPY_Grab;
            g.bAvlb_SPY_Elbow = pd.bAvlb_SPY_Elbow;
            g.bAvlb_SPY_LeathalDropTech = pd.bAvlb_SPY_LeathalDropTech;
            g.bAvlb_MERC_Elbow = pd.bAvlb_MERC_Elbow;
            g.bAvlb_MERC_LeathalDropTech = pd.bAvlb_MERC_LeathalDropTech;
            g.bAvlb_MERC_EMFVision = pd.bAvlb_MERC_EMFVision;
            g.bAvlb_MERC_MotiontrackingVision = pd.bAvlb_MERC_MotiontrackingVision;
            g.bAvlb_MERC_Torche = pd.bAvlb_MERC_Torche;
            g.bAvlb_MERC_Laser = pd.bAvlb_MERC_Laser;
            g.bAvlb_MERC_Berserk = pd.bAvlb_MERC_Berserk;
            g.bAvlb_MERC_Charge = pd.bAvlb_MERC_Charge;
            g.bAvlb_CinematicCamera = pd.bAvlb_CinematicCamera;
            if (g.Teams[0] is STeamInfo spies)
            {
                spies.bDisallowedInv = pd.b_SPY_GadgetRestriction;
                spies.iNBGadgets = pd.b_SPY_NBGadget;
            }

            if (g.Teams[1] is STeamInfo mercs)
            {
                mercs.bDisallowedInv = pd.b_MERC_GadgetRestriction;
                mercs.iNBGadgets = pd.b_MERC_NBGadget;
            }

            g.VoiceChatMode = pd.VoiceChatMode switch
            {
                1 => EVoiceChatMode.VCM_Classic,
                2 => EVoiceChatMode.VCM_Team,
                3 => EVoiceChatMode.VCM_Open,
                _ => EVoiceChatMode.VCM_Modern,
            };
        }

        Host.ClientCall(NewPlayer, new GameCall("ClientSetCinematicCamera") { Bool = pd.bAvlb_CinematicCamera });
        if (NewPlayer is SPlayerController voicePlayer)
        {
            // The voice mode is never replicated with the GRI: the player's machine gets it here (ServerGetVoiceChatters).
            voicePlayer.ServerGetVoiceChatters(voicePlayer);
        }

        if (UcStrings.EqualsIgnoreCase(InLAN, "Server") && NewPlayer.PlayerReplicationInfo is { } pri)
        {
            pri.bIAmServer = true;
        }

        bool bKeptDelayedStart = bDelayedStart;
        if (GameReplicationInfo?.bMatchHasBegun ?? false)
        {
            bDelayedStart = true;
        }

        base.PostLogin(NewPlayer);
        bDelayedStart = bKeptDelayedStart;

        // On LAN the host with no pending client spawns the LAN beacon link; the LAN service is not ported.
    }

    /// <summary>
    /// The team a player gets: always mercs in Merc Deathmatch; otherwise the asked team unless it is full, or the
    /// smaller team once the match is launching, or with auto-balance (Lobby map only).
    /// </summary>
    [Uc("SBase.SGameInfo.PickTeam", Sha1 = "5027c533ceb88f354db197076075d82381096116")]
    [PortOf("uc:SBase.SGameInfo:PickTeam", Spec = "spec/game/login-and-teams.md#rules")]
    public override byte PickTeam(byte num, Controller C)
    {
        if (MatchInfo is { GameMode: EGameMode.GM_MercDeathMatch })
        {
            return 1;
        }

        if (UcStrings.EqualsIgnoreCase(Host.GetURLMap(), "Lobby"))
        {
            bPlayersBalanceTeams = PersistentData.bAutoBalance;
        }
        else
        {
            PersistentData.bAutoBalance = false;
            bPlayersBalanceTeams = false;
        }

        TeamInfo? smallTeam = GameReplicationInfo?.Teams[0];
        TeamInfo? bigTeam = GameReplicationInfo?.Teams[1];
        if (smallTeam is null || bigTeam is null)
        {
            return num;
        }

        if (smallTeam.Size > bigTeam.Size)
        {
            (smallTeam, bigTeam) = (bigTeam, smallTeam);
        }

        TeamInfo? newTeam = num < 2 ? GameReplicationInfo!.Teams[num] : null;
        if (smallTeam.Size < bigTeam.Size && GameReplicationInfo!.bMatchLaunching)
        {
            newTeam = smallTeam;
        }

        if (bPlayersBalanceTeams && smallTeam.Size < bigTeam.Size &&
            (Level.NetMode != ENetMode.NM_Standalone || C is not PlayerController))
        {
            newTeam = smallTeam;
        }

        if (newTeam is null || newTeam.Size >= MaxTeamSize)
        {
            newTeam = smallTeam;
        }

        return (byte)newTeam.TeamIndex;
    }

    /// <summary>
    /// Moves a controller to team <paramref name="num"/> (always mercs in Merc Deathmatch). Refused when the team is
    /// full off Standalone, or when the controller is already on it. Clears the controller's StartSpot.
    /// </summary>
    [Uc("SBase.SGameInfo.ChangeTeam", Sha1 = "14e2f4f35b9eebe0488a87b60446d36b1aa71def")]
    [PortOf("uc:SBase.SGameInfo:ChangeTeam", Spec = "spec/game/login-and-teams.md#rules")]
    protected override bool ChangeTeam(Controller Other, int num, bool bNewTeam)
    {
        if (MatchInfo is { GameMode: EGameMode.GM_MercDeathMatch } && num != 1)
        {
            num = 1;
        }

        TeamInfo? newTeam = GameReplicationInfo is { } gri && num >= 0 && num < gri.Teams.Length ? gri.Teams[num] : null;
        if (newTeam is null)
        {
            return false;
        }

        if (newTeam.Size >= MaxTeamSize && Level.NetMode != ENetMode.NM_Standalone)
        {
            return false;
        }

        if (ReferenceEquals(Other.PlayerReplicationInfo?.Team, newTeam))
        {
            return false;
        }

        Other.StartSpot = null;
        Other.PlayerReplicationInfo?.Team?.RemoveFromTeam(Other);
        newTeam.AddToTeam(Other);
        Ports.UpdateVoiceChatMuting(this, true);
        return true;
    }

    /// <summary>Merc Deathmatch: every playing controller not yet on the mercs moves there, past the team cap.</summary>
    [Uc("SBase.SGameInfo.MoveEveryoneToMercs", Sha1 = "67cbe6837b8997557b099a845934e57e668917b6")]
    [PortOf("uc:SBase.SGameInfo:MoveEveryoneToMercs", Spec = "spec/game/login-and-teams.md#rules")]
    public void MoveEveryoneToMercs(bool bUnready)
    {
        int keptTeamSize = MaxTeamSize;
        MaxTeamSize = K.MoveEveryoneToMercsTeamCap;
        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            if (p is PlayerController && p.PlayerReplicationInfo is { bOnlySpectator: false } pri &&
                (pri.Team is null || pri.Team.TeamIndex != 1))
            {
                eventChangeTeam(p, 1, false);
                if (bUnready && p is SPlayerController spc)
                {
                    spc.ForceUnreadyOnClient();
                }
            }
        }

        MaxTeamSize = keptTeamSize;
    }

    /// <summary>
    /// A player who reported ready after the start while still waiting (only Merc Deathmatch lets one wait) gets the
    /// merc lives and spawns.
    /// </summary>
    [Uc("SBase.SGameInfo.StartLateJoiner", Sha1 = "cdabc338f157c2f75b880307a15ee3faec43b0ba")]
    [PortOf("uc:SBase.SGameInfo:StartLateJoiner", Spec = "spec/game/login-and-teams.md#rules")]
    public void StartLateJoiner(PlayerController P)
    {
        if (!(GameReplicationInfo?.bMatchHasBegun ?? false) || !P.IsInState(NAME_s_PlayerWaiting))
        {
            return;
        }

        if (P.PlayerReplicationInfo is { } pri)
        {
            pri.NumLives = MatchInfo?.NumLivesDEF ?? 0;
        }

        eventRestartPlayer(P);
        Host.ClientCall(P, new GameCall("resetConnectionPing"));
        UpdateTeamNumLives();
    }

    /// <summary>
    /// A player leaves: marked as quit, voice muted both ways, and in a network game (not the Lobby) the end check
    /// runs for a playing leaver, otherwise the leave message goes out; then the Engine Logout. The team keeps
    /// counting the leaver.
    /// </summary>
    [Uc("SBase.SGameInfo.Logout", Sha1 = "555d2f15665ba0c3ad3e42f1ff847d2d034a3181")]
    [PortOf("uc:SBase.SGameInfo:Logout", Spec = "spec/game/login-and-teams.md#rules")]
    public override void Logout(Controller Exiting)
    {
        if (Exiting.PlayerReplicationInfo is { } leaving)
        {
            leaving.bQuittedMatch = true;
        }

        if (Exiting is PlayerController exitingPc)
        {
            for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
            {
                if (p is PlayerController other)
                {
                    Ports.HearVoiceChatter(this, exitingPc, other, true);
                    Ports.HearVoiceChatter(this, other, exitingPc, true);
                }
            }
        }

        if (!UcStrings.EqualsIgnoreCase(Host.GetURLMap(), "Lobby") &&
            (Level.NetMode == ENetMode.NM_DedicatedServer || Level.NetMode == ENetMode.NM_ListenServer))
        {
            bool bEndGame = !(Exiting.PlayerReplicationInfo?.bOnlySpectator ?? false) && sCheckEndGame();
            if (bEndGame)
            {
                sEndGame();
            }
            else
            {
                BroadcastLocalizedMessage(GameMessageClass, K.MessageLeftGame, Exiting.PlayerReplicationInfo);
            }
        }

        base.Logout(Exiting);
    }

    /// <summary>Lobby helper: every player except this machine's own reports ready (or not).</summary>
    [Uc("SBase.SGameInfo.AllPlayerReadyToPlayToServer", Sha1 = "460acb2489c423713ca66465eef2924100e4aba7")]
    [PortOf("uc:SBase.SGameInfo:AllPlayerReadyToPlayToServer", Spec = "spec/game/netmode-branches.md#rules")]
    public void AllPlayerReadyToPlayToServer(bool _bReady)
    {
        PlayerController? local = Level.GetLocalPlayerController();
        foreach (SPlayerController p in XLevel.DynamicActors<SPlayerController>())
        {
            if (!ReferenceEquals(p, local))
            {
                p.PlayerReadyToPlayToServer(_bReady);
            }
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>PRI.Team.TeamIndex</c> as script reads it: a missing PRI or team reads 0.
    /// </summary>
    public static int TeamIndexOf(Controller? c) => c?.PlayerReplicationInfo?.Team?.TeamIndex ?? 0;

    /// <summary>
    /// A start's rotation plus its spy camera offsets, each axis wrapped once into -32768..32767 (a missing start
    /// reads as zero).
    /// </summary>
    public static FRotator SpyCameraRotation(PlayerStart? start)
    {
        FRotator r = start?.Rotation ?? default;
        r.Yaw += start?.iSpyCam_InitAngle_Hor ?? 0;
        r.Pitch += start?.iSpyCam_InitAngle_Ver ?? 0;
        r.Yaw = WrapOnce(r.Yaw);
        r.Pitch = WrapOnce(r.Pitch);
        return r;
    }

    private static int WrapOnce(int axis)
    {
        if (axis > 32767)
        {
            axis -= 65536;
        }

        if (axis < -32768)
        {
            axis += 65536;
        }

        return axis;
    }
}
