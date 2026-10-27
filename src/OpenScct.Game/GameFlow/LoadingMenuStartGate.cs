using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.GameFlow;

/// <summary>
/// The match start gate of the host's loading page (GUI.Loading_Menu), which is what starts a match from the menus:
/// a listen host waits until no client is pending (or the LAN service reports every client in the map), then checks
/// every second that every player reported waiting, and one second later starts the match with StartMatchOnServer;
/// an offline match starts after 2 s. Spawn one on the host when the map has loaded; it destroys itself once the
/// match has started (the page closes). Only the gate is ported: labels, lists and the LAN beacon are UI.
/// </summary>
public sealed class LoadingMenuStartGate : Info
{
    private static readonly Name NAME_s_Init = new("s_Init");
    private static readonly Name NAME_s_Server = new("s_Server");
    private static readonly Name NAME_s_StandAlone = new("s_StandAlone");

    public static new readonly StateTable ClassStates =
        new(Actor.ClassStates, s_Init.Instance, s_Server.Instance, s_StandAlone.Instance);

    public LoadingMenuStartGate()
    {
        InitialState = NAME_s_Init;
    }

    /// <summary>Set by the LAN service event EVTLANINFO_CONNEXION_ALL_CLIENTS_IN_MAP, or when no client is pending.</summary>
    public bool bAllPlayersInMap;

    /// <summary>When the page opened (offline delay).</summary>
    public float WaitingTime;

    /// <summary>Number of player checks made.</summary>
    public int TimeOut;

    /// <summary>The first PRI index that must be waiting (1 when the host does not play).</summary>
    public int NumFirstPlayer;

    /// <summary>True once the gate has started the match.</summary>
    public bool bStarted;

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>The LAN service reports every client in the map.</summary>
    [PortOf("uc:GUI.Loading_Menu:InternalOnInfoEvent", Spec = "spec/game/match-lifecycle.md#rules")]
    public void NotifyAllClientsInMap() => bAllPlayersInMap = true;

    private SPlayerController? Host => Level.GetLocalPlayerController() as SPlayerController;

    private void Start(bool network)
    {
        if (Level.Game is { } game)
        {
            game.bDelayedStart = false;
            if (network)
            {
                Host?.StartMatchOnServer();
            }
            else
            {
                game.StartMatch();
            }
        }

        bStarted = true;
        Destroy();
    }

    // The page's first state: every 0.5 s, a listen host moves on to the server gate; offline the match starts
    // once more than 2 s have passed.
    private void InitCode(ref LatentFrame f)
    {
        if (f.Pc == 1)
        {
            WaitingTime = Level.TimeSeconds;
            f.Pc = 10;
        }

        switch (Level.NetMode)
        {
            case ENetMode.NM_ListenServer:
                TimeOut = 0;
                GotoState(NAME_s_Server);
                return;
            case ENetMode.NM_Standalone when Level.TimeSeconds - WaitingTime > K.StartGateStandaloneDelay:
                GotoState(NAME_s_StandAlone);
                return;
        }

        f.Pc = 10;
        Sleep(K.StartGateInitPoll);
    }

    // The server gate: the host reports waiting; with clients pending it waits (1 s steps) for the all-in event,
    // then every second checks that every PRI from NumFirstPlayer waits, and starts one second after it does.
    private void ServerCode(ref LatentFrame f)
    {
        switch (f.Pc)
        {
            case 1:
                Host?.PlayerWaitingToPlayToServer(true);
                if (GameFlowServices.Host(this).NbPendingClientOnServer() == 0)
                {
                    bAllPlayersInMap = true;
                }

                goto case 2;
            case 2:
                if (!bAllPlayersInMap)
                {
                    f.Pc = 2;
                    Sleep(K.StartGateServerPoll);
                    return;
                }

                goto case 10;
            case 10:
                GameReplicationInfo? gri = Level.Game?.GameReplicationInfo;
                if (gri is { ServerOnly: true })
                {
                    NumFirstPlayer = 1;
                }

                TimeOut++;
                bool bStart = gri is not null;
                for (int i = NumFirstPlayer; gri is not null && i < gri.PRIArray.Count; i++)
                {
                    if (!gri.PRIArray[i].bWaitingPlayer)
                    {
                        bStart = false;
                    }
                }

                f.Pc = bStart ? 11 : 10;
                Sleep(K.StartGateServerPoll);
                return;
            case 11:
                f.Pc = LatentFrame.NoCode;
                Start(network: true);
                return;
            default:
                f.Pc = LatentFrame.NoCode;
                return;
        }
    }

    [PortOf("uc:GUI.Loading_Menu:s_Init@code", Spec = "spec/game/match-lifecycle.md#rules")]
    public sealed class s_Init : ActorState
    {
        public static readonly s_Init Instance = new();

        private s_Init()
            : base(nameof(LoadingMenuStartGate), nameof(s_Init), null, false, ("Begin", 1), ("Loop", 10))
        {
        }

        public override void Code(Actor self, ref LatentFrame frame) => ((LoadingMenuStartGate)self).InitCode(ref frame);
    }

    [PortOf("uc:GUI.Loading_Menu:s_Server@code", Spec = "spec/game/match-lifecycle.md#rules")]
    public sealed class s_Server : ActorState
    {
        public static readonly s_Server Instance = new();

        private s_Server()
            : base(nameof(LoadingMenuStartGate), nameof(s_Server), null, false, ("Begin", 1), ("CheckPlayer", 10))
        {
        }

        public override void Code(Actor self, ref LatentFrame frame) => ((LoadingMenuStartGate)self).ServerCode(ref frame);
    }

    [PortOf("uc:GUI.Loading_Menu:s_StandAlone@code", Spec = "spec/game/match-lifecycle.md#rules")]
    public sealed class s_StandAlone : ActorState
    {
        public static readonly s_StandAlone Instance = new();

        private s_StandAlone()
            : base(nameof(LoadingMenuStartGate), nameof(s_StandAlone), null, false, ("Begin", 1))
        {
        }

        public override void Code(Actor self, ref LatentFrame frame)
        {
            frame.Pc = LatentFrame.NoCode;
            ((LoadingMenuStartGate)self).Start(network: false);
        }
    }
}
