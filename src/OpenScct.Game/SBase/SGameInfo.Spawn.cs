using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SGameInfo: the match start, spawn point choice and spawning a player's pawn. Written from
// spec/game/match-lifecycle.md, spec/game/spawn-selection.md and spec/game/respawn-and-spawning.md.
public partial class SGameInfo
{
    // ---- match start ---------------------------------------------------------------------------------------

    /// <summary>
    /// Starts the match: Merc Deathmatch moves everyone to the mercs, the Engine part spawns the players, every
    /// mission is deactivated and the current mode's missions reset, each player gets the lives of its side, then
    /// the clock restarts and the team lives are counted.
    /// </summary>
    [Uc("SBase.SGameInfo.StartMatch", Sha1 = "9591cd4c170a3d875634ca3fbd3bd2988990dfc0")]
    [PortOf("uc:SBase.SGameInfo:StartMatch", Spec = "spec/game/match-lifecycle.md#rules")]
    public override void StartMatch()
    {
        EGameMode mode = MatchInfo?.GameMode ?? EGameMode.GM_Undefined;
        if (mode == EGameMode.GM_MercDeathMatch)
        {
            MoveEveryoneToMercs(false);
        }

        base.StartMatch();
        foreach (SMission m in XLevel.AllActors<SMission>())
        {
            m.Desactivate();
        }

        GameReplicationInfo? gri = GameReplicationInfo;
        if (gri is not null)
        {
            gri.NumObjectives = 0;
        }

        foreach (SMission m in XLevel.AllActors<SMission>())
        {
            if (mode == EGameMode.GM_MercDeathMatch)
            {
                continue;
            }

            if (m.GameMode == mode)
            {
                if (m.GameMode == EGameMode.GM_DiskHunt)
                {
                    m.MinimumObjectives = PersistentData.NumDisks;
                }

                m.Reset();
            }
        }

        for (Controller? p = Level.ControllerList; p is not null; p = p.nextController)
        {
            if (p is not PlayerController pc)
            {
                continue;
            }

            SetPause(false, pc);
            PlayerReplicationInfo? pri = p.PlayerReplicationInfo;
            if (pri is not null)
            {
                if (p.Pawn is SPawnAttaque)
                {
                    if (pri.Team is not null)
                    {
                        pri.Team.TeamIndex = 0;
                    }

                    pri.NumLives = MatchInfo?.NumLivesATT ?? 0;
                }
                else if (p.Pawn is SPAWNDEFENSE)
                {
                    if (pri.Team is not null)
                    {
                        pri.Team.TeamIndex = 1;
                    }

                    pri.NumLives = MatchInfo?.NumLivesDEF ?? 0;
                }
                else if (mode == EGameMode.GM_MercDeathMatch)
                {
                    pri.NumLives = pri.Team is { TeamIndex: 0 } ? MatchInfo?.NumLivesATT ?? 0 : MatchInfo?.NumLivesDEF ?? 0;
                }
            }

            if (gri is not null)
            {
                gri.bMatchLaunching = true;
                gri.TimesRespawn = MatchInfo?.TimesRespawn ?? 0;
            }

            Host.ClientCall(pc, new GameCall("resetConnectionPing"));
        }

        if (gri is not null && Host.IsGUIInGame(Level.ControllerList as PlayerController))
        {
            gri.bMatchHasBegun = true;
        }

        if (Level.NetMode == ENetMode.NM_Standalone && Level.ControllerList is SPlayerController head)
        {
            Ports.SpawnMagmaHud(head);
        }

        gri?.Reset();
        UpdateTeamNumLives();
        bWelcomePending = true;
    }

    // ---- spawn point choice --------------------------------------------------------------------------------

    /// <summary>
    /// Picks a start for the controller's side (its SPawnType; side 0 without an SPlayerController). Standalone may
    /// reuse the last start while waiting; before bMatchHasBegun the enabled start of the side with the highest
    /// Priority above 0 wins; otherwise the start whose nearest pawn is farthest away; Merc Deathmatch falls back to
    /// any enabled start.
    /// </summary>
    [Uc("SBase.SGameInfo.FindPlayerStart", Sha1 = "cf475c55cddbb551fd50dac0930da91c4ec2216d")]
    [PortOf("uc:SBase.SGameInfo:FindPlayerStart", Spec = "spec/game/spawn-selection.md#rules")]
    public override PlayerStart? FindPlayerStart(Controller? Player, byte InTeam = 0, string incomingName = "")
    {
        InTeam = (byte)((Player as SPlayerController)?.SPawnType ?? PawnType.SPAWNOFFENSE);
        bool isMercDeathMatch = MatchInfo is { GameMode: EGameMode.GM_MercDeathMatch };
        bool bMercDMStarts = false;
        if (isMercDeathMatch)
        {
            foreach (PlayerStart n in XLevel.AllActors<PlayerStart>())
            {
                if (n.bMercDeathMatchStart)
                {
                    bMercDMStarts = true;
                    break;
                }
            }
        }

        if (Player?.StartSpot is { } reuse && Level.NetMode == ENetMode.NM_Standalone &&
            (bWaitingToStartMatch || (Player.PlayerReplicationInfo?.bWaitingPlayer ?? false)))
        {
            return reuse;
        }

        PlayerStart? bestStart = null;
        if (!(GameReplicationInfo?.bMatchHasBegun ?? false))
        {
            byte bestPriority = 0;
            foreach (PlayerStart n in XLevel.AllActors<PlayerStart>())
            {
                if (!n.bEnabled || n.TeamNumber != InTeam || n.bMercDeathMatchStart != bMercDMStarts)
                {
                    continue;
                }

                if (n.Priority > bestPriority)
                {
                    bestPriority = n.Priority;
                    bestStart = n;
                }
            }

            if (bestStart is not null)
            {
                return bestStart;
            }
        }

        float bestRating = K.InitialBestRating;
        foreach (PlayerStart n in XLevel.AllActors<PlayerStart>())
        {
            if (n.bMercDeathMatchStart != bMercDMStarts)
            {
                continue;
            }

            float rating = RatePlayerStart(n, InTeam, Player);
            if (rating > bestRating)
            {
                bestRating = rating;
                bestStart = n;
            }
        }

        if (bestStart is null && isMercDeathMatch)
        {
            foreach (PlayerStart n in XLevel.AllActors<PlayerStart>())
            {
                if (!n.bEnabled)
                {
                    continue;
                }

                float rating = RateStartByDistance(n);
                if (rating > bestRating)
                {
                    bestRating = rating;
                    bestStart = n;
                }
            }
        }

        return bestStart;
    }

    /// <summary>0 for a disabled start or one of the other side, else its distance rating.</summary>
    [Uc("SBase.SGameInfo.RatePlayerStart", Sha1 = "df1f2e68f77226ca838c7663bc8d1ee7bec32503")]
    [PortOf("uc:SBase.SGameInfo:RatePlayerStart", Spec = "spec/game/spawn-selection.md#rules")]
    public override float RatePlayerStart(PlayerStart N, byte Team, Controller? Player)
    {
        if (N is null || !N.bEnabled || N.TeamNumber != Team)
        {
            return 0f;
        }

        return RateStartByDistance(N);
    }

    /// <summary>The distance from the start to the nearest SBasePawn in the level, at most 1,000,000.</summary>
    [Uc("SBase.SGameInfo.RateStartByDistance", Sha1 = "e06d6d618e0bd10776f3997ec12e71f754beb880")]
    [PortOf("uc:SBase.SGameInfo:RateStartByDistance", Spec = "spec/game/spawn-selection.md#rules")]
    public float RateStartByDistance(PlayerStart N)
    {
        float rating = K.InitialDistanceRating;
        foreach (SBasePawn pawn in XLevel.AllActors<SBasePawn>())
        {
            float dist = (pawn.Location - N.Location).Size();
            if (dist < rating)
            {
                rating = dist;
            }
        }

        return rating;
    }

    // ---- spawning a pawn -----------------------------------------------------------------------------------

    /// <summary>
    /// Spawns and possesses a pawn for a player: a start for its side, the pawn placed 79 uu above the floor found
    /// by a 200 uu box probe (or at the start), the camera rotation, the default inventory, the start's event and
    /// sound, and the sticky shock of enemies standing too close. A failed spawn turns the player into a visitor.
    /// </summary>
    [Uc("SBase.SGameInfo.RestartPlayer", Sha1 = "eae9c00cb697e9a54bdd859d7c786552a54bcb22")]
    [PortOf("uc:SBase.SGameInfo:RestartPlayer", Spec = "spec/game/respawn-and-spawning.md#rules")]
    protected override void RestartPlayer(Controller aPlayer)
    {
        PlayerReplicationInfo? pri = aPlayer.PlayerReplicationInfo;
        if (pri is { bOnlySpectator: true })
        {
            return;
        }

        if (bRestartLevel && Level.NetMode != ENetMode.NM_DedicatedServer && Level.NetMode != ENetMode.NM_ListenServer)
        {
            return;
        }

        int teamNum = pri?.Team is { } team ? team.TeamIndex : K.SpectatorTeam;
        PlayerStart? startSpot = FindPlayerStart(aPlayer, (byte)teamNum);
        if (startSpot is null)
        {
            return;
        }

        aPlayer.StartSpot = startSpot;
        if (!aPlayer.PawnClass.IsNone)
        {
            FVector spawnAt = startSpot.Location;
            FVector end = spawnAt - new FVector(0f, 0f, K.FloorProbeLength);
            if (TraceFloor(spawnAt, end, out FVector hitLocation))
            {
                spawnAt.Z = hitLocation.Z + K.SpawnHeightAboveHit;
            }

            aPlayer.Pawn = UcSpawn.Spawn(this, aPlayer.PawnClass, null, spawnAt, startSpot.Rotation) as Pawn;
        }

        Pawn? pawn = aPlayer.Pawn;
        if (pawn is null)
        {
            if (pri is not null)
            {
                pri.bOnlySpectator = true;
                pri.bIsSpectator = true;
            }

            NumSpectators++;
            aPlayer.GotoState(NAME_s_Spectating_Visitor);
            return;
        }

        pawn.Anchor = startSpot;
        pawn.LastStartSpot = startSpot;
        pawn.LastStartTime = Level.TimeSeconds;
        ClassRef pawnClass = ClassOf(pawn, aPlayer.PawnClass);
        aPlayer.PreviousPawnClass = pawnClass;
        aPlayer.Possess(pawn);
        aPlayer.PawnClass = pawnClass;
        Ports.PlayTeleportEffect(pawn, true, true);

        FRotator clientRotation = teamNum == 0 ? SpyCameraRotation(startSpot) : pawn.Rotation;
        SendClientSetRotation(aPlayer, clientRotation);

        AddDefaultInventory(pawn);
        TriggerEvent(startSpot.Event, startSpot, pawn);
        if (aPlayer is SPlayerController spc)
        {
            spc.MyDesiredRotation = aPlayer.Rotation;
            Host.ClientCall(spc, new GameCall("ClientPlaySound") { Asset = PlayerStartSound });
        }

        for (Controller? other = Level.ControllerList; other is not null; other = other.nextController)
        {
            if (ReferenceEquals(other, aPlayer) || other.Pawn is not { } otherPawn ||
                other.PlayerReplicationInfo?.Team is not { } otherTeam || otherTeam.TeamIndex == teamNum)
            {
                continue;
            }

            FVector d = otherPawn.Location - pawn.Location;
            bool shocked = d.Z > K.SpawnShockLevelZ
                ? d.Size() < K.SpawnShockRadiusAbove
                : SvmMath.Abs(d.Z) <= K.SpawnShockLevelZ && d.Size() < K.SpawnShockRadiusLevel;
            if (shocked)
            {
                other.PawnTakeDamage(0, pawn, otherPawn.Location, d, SDamageType.SDTStickyShocker, K.SpawnShockPillTag);
            }
        }
    }

    /// <summary>The pawn's inventory and default gadgets for the current mode (inventory spec).</summary>
    [Uc("SBase.SGameInfo.AddDefaultInventory", Sha1 = "06ba094941fd0229a7486bfdb62a27afab8811ab")]
    [PortOf("uc:SBase.SGameInfo:AddDefaultInventory", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void AddDefaultInventory(Pawn PlayerPawn)
    {
        if (PlayerPawn is SBasePawn pawn)
        {
            Ports.AddDefaultInventory(pawn, MatchInfo?.GameMode ?? EGameMode.GM_Undefined);
        }
    }

    /// <summary>Resets the goggles of the pawn's controller (no script caller).</summary>
    [Uc("SBase.SGameInfo.ResetVisionMode", Sha1 = "d81e9d663fe3a1a4b11ef7867ecbe88d8c35c137")]
    [PortOf("uc:SBase.SGameInfo:ResetVisionMode", Spec = "spec/game/respawn-and-spawning.md#rules")]
    public override void ResetVisionMode(Pawn PlayerPawn)
    {
        if (PlayerPawn.Controller is SPlayerController pc)
        {
            Ports.ResetVisionMode(pc);
        }
    }

    /// <summary>
    /// Actor.Trace with an extent, colliding with actors: true when the probe hits something, with the hit location
    /// (the box centre at contact). Without a collision world nothing is hit.
    /// </summary>
    [UcNative("AActor::execTrace")]
    [Provenance(ProvenanceKind.Estimated, "UE2 Trace with bTraceActors uses the projectile-target flags; hit location is the box centre at contact")]
    private bool TraceFloor(FVector start, FVector end, out FVector hitLocation)
    {
        hitLocation = end;
        if (Collision is not { } world)
        {
            return false;
        }

        if (world.LineCheck(out FCheckResult hit, this, end, start, K.FloorProbeExtent, ETraceFlags.TRACE_ProjTargets))
        {
            hitLocation = hit.Location;
            return true;
        }

        return false;
    }

    /// <summary>
    /// <c>Actor.Class</c> as a class reference: <paramref name="expected"/> when it names the actor's own type,
    /// otherwise the actor's nearest generated class.
    /// </summary>
    private ClassRef ClassOf(Actor actor, ClassRef expected)
    {
        if (GetClassIndex().Find(expected)?.Type == actor.GetType())
        {
            return expected;
        }

        return GetClassIndex().FindNearest(actor.GetType()) is { } info ? new ClassRef(info.Path) : expected;
    }

    /// <summary>ClientSetRotation, a server-to-owner call: runs here for a local player, goes to the host otherwise.</summary>
    private void SendClientSetRotation(Controller c, FRotator rotation)
    {
        if (c is PlayerController { IsRemotePlayer: true } remote)
        {
            Host.ClientCall(remote, new GameCall("ClientSetRotation") { Rotation = rotation });
        }
        else
        {
            c.ClientSetRotation(rotation);
        }
    }
}
