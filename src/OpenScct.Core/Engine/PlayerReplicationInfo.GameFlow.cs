using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.PlayerReplicationInfo: per-player statistics, the location and ping timer, and the GRI player list.
// Written from spec/game/replication-info.md.
public partial class PlayerReplicationInfo
{
    /// <summary>On the server: creates the statistics object, records the join time and starts the location timer.</summary>
    [Uc("Engine.PlayerReplicationInfo.PostBeginPlay", Sha1 = "cdcf48183c77e93f57ef178b9208986b2313d89f")]
    [PortOf("uc:Engine.PlayerReplicationInfo:PostBeginPlay", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void PostBeginPlay()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            return;
        }

        Stats = new USPlayerStats();
        StartTime = Level.Game?.GameReplicationInfo?.ElapsedTime ?? 0;
        Timer();
        SetTimer(1.5f + FRand(), true);
    }

    /// <summary>
    /// Updates the location, re-arms itself for 1.5 + FRand() seconds and, with probability 0.35, refreshes the ping
    /// unless the client reported it.
    /// </summary>
    [Uc("Engine.PlayerReplicationInfo.Timer", Sha1 = "cf102750fd10e74cdae0c30aad2020bc23b55a7e")]
    [PortOf("uc:Engine.PlayerReplicationInfo:Timer", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void Timer()
    {
        if (Role <= ENetRole.ROLE_SimulatedProxy)
        {
            return;
        }

        UpdatePlayerLocation();
        SetTimer(1.5f + FRand(), true);
        if (FRand() < 0.65f)
        {
            return;
        }

        if (!bReceivedPing)
        {
            Ping = Owner is PlayerController pc ? (XLevel.GetService<IGameHost>() ?? NullGameHost.Instance).GetPing(pc) : 0;
        }
    }

    /// <summary>
    /// The pawn's zone, and the highest-priority named volume it touches. Volume.Encompasses (native) is not ported:
    /// a touching volume counts as encompassing.
    /// </summary>
    [Uc("Engine.PlayerReplicationInfo.UpdatePlayerLocation", Sha1 = "df37d4bb903ea46adb0c858a911b55bce74f7f95")]
    [PortOf("uc:Engine.PlayerReplicationInfo:UpdatePlayerLocation", Spec = "spec/game/replication-info.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Volume.Encompasses is native and not ported; touching stands in for it")]
    public void UpdatePlayerLocation()
    {
        Pawn? p = (Owner as Controller)?.Pawn;
        if (p is null)
        {
            PlayerVolume = null;
            PlayerZone = null;
            return;
        }

        PlayerZone = p.Region.Zone;
        Volume? best = null;
        foreach (Actor a in p.Touching)
        {
            if (a is not Volume v || string.IsNullOrEmpty(v.LocationName))
            {
                continue;
            }

            if (best is not null && v.LocationPriority <= best.LocationPriority)
            {
                continue;
            }

            best = v;
        }

        PlayerVolume = best;
    }

    /// <summary>Joins the player list of the first game replication info found.</summary>
    [Uc("Engine.PlayerReplicationInfo.PostNetBeginPlay", Sha1 = "c35f95e908f9a8793ac778d80e53e26980624b6c")]
    [PortOf("uc:Engine.PlayerReplicationInfo:PostNetBeginPlay", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void PostNetBeginPlay()
    {
        foreach (GameReplicationInfo gri in XLevel.DynamicActors<GameReplicationInfo>())
        {
            gri.AddPRI(this);
            break;
        }
    }

    /// <summary>
    /// Leaves every game replication info's player list. Not ported here: the notice to the local player controller
    /// off LAN (HUD bookkeeping).
    /// </summary>
    [Uc("Engine.PlayerReplicationInfo.Destroyed", Sha1 = "c456cb943475281ce6053e2194a58fecef07d0db")]
    [PortOf("uc:Engine.PlayerReplicationInfo:Destroyed", Spec = "spec/game/replication-info.md#rules")]
    protected internal override void Destroyed()
    {
        foreach (GameReplicationInfo gri in XLevel.DynamicActors<GameReplicationInfo>())
        {
            gri.RemovePRI(this);
        }

        base.Destroyed();
    }

    /// <summary>Clears score, deaths, flag, readiness and lives (nothing in this game calls it).</summary>
    [Uc("Engine.PlayerReplicationInfo.Reset", Sha1 = "37ec31693a90af859bed485d25bae79b76d6d554")]
    [PortOf("uc:Engine.PlayerReplicationInfo:Reset", Spec = "spec/game/replication-info.md#rules")]
    public override void Reset()
    {
        Score = 0f;
        Deaths = 0;
        SetFlag(null);
        bReadyToPlay = false;
        NumLives = 0;
        bOutOfLives = false;
    }

    [Uc("Engine.PlayerReplicationInfo.SetFlag", Sha1 = "c281c8b96c3cc4f2d57205f831d79dbbc9dc1fb3")]
    [PortOf("uc:Engine.PlayerReplicationInfo:SetFlag", Spec = "spec/game/replication-info.md#rules")]
    public void SetFlag(CarriedObject? NewFlag)
    {
        HasFlag = NewFlag;
        bHasFlag = NewFlag is not null;
    }

    [Uc("Engine.PlayerReplicationInfo.SetPlayerName", Sha1 = "f8db823eda7f5074715ac2b62fc29b49dc47ed0f")]
    [PortOf("uc:Engine.PlayerReplicationInfo:SetPlayerName", Spec = "spec/game/replication-info.md#rules")]
    public void SetPlayerName(string S)
    {
        OldName = PlayerName;
        PlayerName = S ?? string.Empty;
    }

    [Uc("Engine.PlayerReplicationInfo.SetWaitingPlayer", Sha1 = "3fee6984b8aa79dd7b6d194b5c791fcbaf1aabb0")]
    [PortOf("uc:Engine.PlayerReplicationInfo:SetWaitingPlayer", Spec = "spec/game/replication-info.md#rules")]
    public void SetWaitingPlayer(bool B)
    {
        bIsSpectator = B;
        bWaitingPlayer = B;
    }

    /// <summary>Client to server: the player left the match.</summary>
    [Uc("Engine.PlayerReplicationInfo.playerQuitMatch", Sha1 = "0afbf3fa2ec6a9bcbcc790d00dcff8d9b1330968")]
    [PortOf("uc:Engine.PlayerReplicationInfo:playerQuitMatch", Spec = "spec/game/replication-info.md#rules")]
    public void playerQuitMatch()
    {
        bQuittedMatch = true;
    }

    protected internal override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Team = Live(Team);
        PlayerZone = Live(PlayerZone);
        PlayerVolume = Live(PlayerVolume);
    }
}
