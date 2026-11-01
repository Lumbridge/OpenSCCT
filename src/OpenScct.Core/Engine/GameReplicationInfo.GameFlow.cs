using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.GameReplicationInfo: the match clock and the player list. Written from spec/game/replication-info.md.
public partial class GameReplicationInfo
{
    /// <summary>Starts the 1 s clock timer. A client blanks the server identity strings until they replicate.</summary>
    [Uc("Engine.GameReplicationInfo.PostBeginPlay", Sha1 = "0d4201fd6a33a942f5f8478617390981478b8ea8")]
    [PortOf("uc:Engine.GameReplicationInfo:PostBeginPlay", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void PostBeginPlay()
    {
        if (Level.NetMode == ENetMode.NM_Client)
        {
            ServerName = string.Empty;
            AdminName = string.Empty;
            AdminEmail = string.Empty;
        }

        SecondCount = XLevel.WorldTimeSeconds;
        SetTimer(1f, true);
    }

    /// <summary>Adds every replication info that already exists; a client then checks the team symbols.</summary>
    [Uc("Engine.GameReplicationInfo.PostNetBeginPlay", Sha1 = "4a8a1da5a86fb02de8c61c93f8e7ed46feb7906a")]
    [PortOf("uc:Engine.GameReplicationInfo:PostNetBeginPlay", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void PostNetBeginPlay()
    {
        foreach (PlayerReplicationInfo pri in XLevel.DynamicActors<PlayerReplicationInfo>())
        {
            AddPRI(pri);
        }

        if (Level.NetMode == ENetMode.NM_Client)
        {
            TeamSymbolNotify();
        }
    }

    /// <summary>
    /// Once both teams and their icons exist, marks the symbols as updated. Not ported here: the SetGRI call on every
    /// actor (HUD and minimap code).
    /// </summary>
    [Uc("Engine.GameReplicationInfo.TeamSymbolNotify", Sha1 = "96e83b74810d50663236c0967b30dad9a66120c6")]
    [PortOf("uc:Engine.GameReplicationInfo:TeamSymbolNotify", Spec = "spec/game/replication-info.md#rules")]
    public void TeamSymbolNotify()
    {
        if (Teams[0] is null || Teams[1] is null || Teams[0]!.TeamIcon.IsNone || Teams[1]!.TeamIcon.IsNone)
        {
            return;
        }

        bTeamSymbolsUpdated = true;
    }

    /// <summary>Restarts the match clock (StartMatch): elapsed 0, the limit seeded, the countdown running.</summary>
    [Uc("Engine.GameReplicationInfo.Reset", Sha1 = "f18892169fd109025d960d986e2bde6d761d61ef")]
    [PortOf("uc:Engine.GameReplicationInfo:Reset", Spec = "spec/game/replication-info.md#rules")]
    public override void Reset()
    {
        SecondCount = XLevel.WorldTimeSeconds;
        ElapsedTime = 0;
        RemainingMinute = TimeLimit * 60;
        bStopCountDown = false;
        SetTimer(1f, true);
    }

    /// <summary>
    /// Counts at most one second per call: when a game second has passed since the last count, ElapsedTime goes up
    /// and, unless stopped, RemainingTime goes down (seeded once from RemainingMinute).
    /// </summary>
    [Uc("Engine.GameReplicationInfo.Timer", Sha1 = "9cac26ab3858ad5a34a7a2b9846ecbde9a2961c7")]
    [PortOf("uc:Engine.GameReplicationInfo:Timer", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void Timer()
    {
        // Not simulated: a client's copy (a simulated proxy) never runs it.
        if (Role <= ENetRole.ROLE_SimulatedProxy)
        {
            return;
        }

        if (XLevel.WorldTimeSeconds - SecondCount >= Level.TimeDilation)
        {
            ElapsedTime++;
            if (RemainingMinute != 0)
            {
                RemainingTime = RemainingMinute;
                RemainingMinute = 0;
            }

            if (!bStopCountDown)
            {
                RemainingTime--;
            }

            SecondCount += Level.TimeDilation;
        }
    }

    [Uc("Engine.GameReplicationInfo.PreciseElapsedTime", Sha1 = "bba6b6494e01acfc78ada8f655c88198c51f2371")]
    [PortOf("uc:Engine.GameReplicationInfo:PreciseElapsedTime", Spec = "spec/game/replication-info.md#rules")]
    // Match deadlines are advanced by world ticks. A movement command can temporarily replace TimeSeconds
    // with the client's older input time; mixing it with SecondCount makes valid future deadlines negative.
    public float PreciseElapsedTime() => ElapsedTime + (XLevel.WorldTimeSeconds - SecondCount) / Level.TimeDilation;

    [Uc("Engine.GameReplicationInfo.AddPRI", Sha1 = "0ca37458a568c5b7be211a0cac07ab67ccc31916")]
    [PortOf("uc:Engine.GameReplicationInfo:AddPRI", Spec = "spec/game/replication-info.md#rules")]
    public void AddPRI(PlayerReplicationInfo PRI) => PRIArray.Add(PRI);

    /// <summary>Removes the first entry equal to <paramref name="PRI"/>; nothing (the original logs an error) when absent.</summary>
    [Uc("Engine.GameReplicationInfo.RemovePRI", Sha1 = "8858d19af5712de9c8e962396cc77c9c21909461")]
    [PortOf("uc:Engine.GameReplicationInfo:RemovePRI", Spec = "spec/game/replication-info.md#rules")]
    public void RemovePRI(PlayerReplicationInfo PRI)
    {
        int i = PRIArray.IndexOf(PRI);
        if (i >= 0)
        {
            PRIArray.RemoveAt(i);
        }
    }

    /// <summary>Copies the entries that are not None.</summary>
    [Uc("Engine.GameReplicationInfo.GetPRIArray", Sha1 = "bf3405ba49ac989342e794861b75eb50ff58fe75")]
    [PortOf("uc:Engine.GameReplicationInfo:GetPRIArray", Spec = "spec/game/replication-info.md#rules")]
    public void GetPRIArray(List<PlayerReplicationInfo> pris)
    {
        pris.Clear();
        foreach (PlayerReplicationInfo pri in PRIArray)
        {
            if (pri is { bDeleteMe: false })
            {
                pris.Add(pri);
            }
        }
    }

    /// <summary>Toggles ability flag <paramref name="Id"/> (0 spy thermal ... 12 merc charge).</summary>
    [Uc("Engine.GameReplicationInfo.SetBridage", Sha1 = "2e483c3a57ef0a5cb15c567c67b1d8acb987d65b")]
    [PortOf("uc:Engine.GameReplicationInfo:SetBridage", Spec = "spec/game/replication-info.md#rules")]
    public void SetBridage(int Id)
    {
        switch (Id)
        {
            case 0: bAvlb_SPY_ThermalVision = !bAvlb_SPY_ThermalVision; break;
            case 1: bAvlb_SPY_NightVision = !bAvlb_SPY_NightVision; break;
            case 2: bAvlb_SPY_Grab = !bAvlb_SPY_Grab; break;
            case 3: bAvlb_SPY_Elbow = !bAvlb_SPY_Elbow; break;
            case 4: bAvlb_SPY_LeathalDropTech = !bAvlb_SPY_LeathalDropTech; break;
            case 5: bAvlb_MERC_Elbow = !bAvlb_MERC_Elbow; break;
            case 6: bAvlb_MERC_LeathalDropTech = !bAvlb_MERC_LeathalDropTech; break;
            case 7: bAvlb_MERC_EMFVision = !bAvlb_MERC_EMFVision; break;
            case 8: bAvlb_MERC_MotiontrackingVision = !bAvlb_MERC_MotiontrackingVision; break;
            case 9: bAvlb_MERC_Torche = !bAvlb_MERC_Torche; break;
            case 10: bAvlb_MERC_Laser = !bAvlb_MERC_Laser; break;
            case 11: bAvlb_MERC_Berserk = !bAvlb_MERC_Berserk; break;
            case 12: bAvlb_MERC_Charge = !bAvlb_MERC_Charge; break;
        }
    }

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        for (int i = 0; i < Teams.Length; i++)
        {
            Teams[i] = Live(Teams[i]);
        }
    }
}
