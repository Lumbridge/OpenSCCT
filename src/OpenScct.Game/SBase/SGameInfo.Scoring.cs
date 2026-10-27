using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// SBase.SGameInfo: kill bookkeeping, lives, points, statistics and the messages that announce them. Written from
// spec/game/death-lives-respawn.md and spec/game/scoring-and-stats.md.
public partial class SGameInfo
{
    private static readonly ClassRef SGameMessagesClass = new("SBase.SGameMessages");

    /// <summary>
    /// A player died: a death and one life less for the victim, the kill (or team kill, or suicide) for the killer,
    /// environmental kill and flag-carrier credit, the death message, the last-life team message, then the end
    /// check. Points are awarded by the caller afterwards (AddPoint).
    /// </summary>
    [Uc("SBase.SGameInfo.Killed", Sha1 = "b10d5a56b22bfc16a296888b65cc6a3a9af2f33f")]
    [PortOf("uc:SBase.SGameInfo:Killed", Spec = "spec/game/death-lives-respawn.md#rules")]
    public override void Killed(Controller? Killer, Controller? Killed, Pawn? KilledPawn, SDamageType DamageType)
    {
        bool mercDeathMatch = MatchInfo is { GameMode: EGameMode.GM_MercDeathMatch };
        if (Killed is not null)
        {
            PlayerReplicationInfo? victim = Killed.PlayerReplicationInfo;
            if (victim is not null)
            {
                victim.Deaths += 1;
                victim.NumLives--;
            }

            if (Killer is not null)
            {
                if (!ReferenceEquals(Killer, Killed))
                {
                    if (TeamIndexOf(Killer) != TeamIndexOf(Killed) || mercDeathMatch)
                    {
                        if (Killer.PlayerReplicationInfo is { } killerPri)
                        {
                            killerPri.Kills++;
                        }

                        CountKillFeats(Killer, Killed, KilledPawn, DamageType);

                        SDamageVolume? volume = FindDamageVolume(Killed.Pawn, DamageType);
                        if (DamageType == SDamageType.Crushed ||
                            (GPODamageInstigator is not null && ReferenceEquals(Killer, GPODamageInstigator.Controller)) ||
                            (volume is not null && ReferenceEquals(Killer, volume.TriggerController)))
                        {
                            CountEnvironmentalKill(Killer, Killed);
                        }

                        if (Killed is SPlayerController { SBPawn.CarriedFlag: { } flag })
                        {
                            flag.CarrierKiller = Killer;
                        }
                    }
                    else
                    {
                        if (Killer.PlayerReplicationInfo?.Stats is { } killerStats)
                        {
                            killerStats.TeamKills++;
                        }

                        if (victim?.Stats is { } victimStats)
                        {
                            victimStats.DeathFromTeammate++;
                        }
                    }
                }
                else
                {
                    if (victim?.Stats is { } suicideStats)
                    {
                        suicideStats.Suicides++;
                    }

                    if (mercDeathMatch && victim is not null)
                    {
                        victim.Kills--;
                    }
                }
            }

            BroadcastDeathMessage(Killer, Killed, DamageType);
            if ((victim?.NumLives ?? 0) < 1)
            {
                BroadcastTeamLocalizedMessage((byte)TeamIndexOf(Killed), SGameMessagesClass, K.MessageLastLife, victim, null, this);
            }
        }

        BroadcastScoreTime = Level.TimeSeconds + 2f;
        if (sCheckEndGame())
        {
            sEndGame();
        }
    }

    /// <summary>Sums the lives of each side's playing controllers into GRI.TotalTeamLives (no team reads side 0).</summary>
    [Uc("SBase.SGameInfo.UpdateTeamNumLives", Sha1 = "58e0ce862751adf5cdb1d2a3d9dec47bef7c7cbd")]
    [PortOf("uc:SBase.SGameInfo:UpdateTeamNumLives", Spec = "spec/game/death-lives-respawn.md#rules")]
    public void UpdateTeamNumLives()
    {
        if (GameReplicationInfo is not { } gri)
        {
            return;
        }

        gri.TotalTeamLives[0] = 0;
        gri.TotalTeamLives[1] = 0;
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is not PlayerController p)
            {
                continue;
            }

            int team = TeamIndexOf(p);
            if (team >= 0 && team < 2 && !(p.PlayerReplicationInfo?.bOnlySpectator ?? false))
            {
                gri.TotalTeamLives[team] += p.PlayerReplicationInfo?.NumLives ?? 0;
            }
        }
    }

    /// <summary>
    /// Adds the points of action <paramref name="IndexScore"/> to the attack counter (team 0) or the defence counter,
    /// and to the player's score, then runs the end check. The caller chooses the team.
    /// </summary>
    [Uc("SBase.SGameInfo.AddPoint", Sha1 = "e277c89ebd4dae301da231c5c7fc619e922a54a0")]
    [PortOf("uc:SBase.SGameInfo:AddPoint", Spec = "spec/game/scoring-and-stats.md#rules")]
    public void AddPoint(int Team, PlayerController? P, int IndexScore)
    {
        int points = Level.GetActionPoints(IndexScore);
        if (Team == 0)
        {
            Level.NbPointsAttack += points;
        }
        else
        {
            Level.NbPointsDefense += points;
        }

        if (P?.PlayerReplicationInfo is { } pri)
        {
            pri.Score += points;
        }

        if (sCheckEndGame())
        {
            sEndGame();
        }
    }

    /// <summary>Sets both team point counters (no caller in this game).</summary>
    [Uc("SBase.SGameInfo.InitScores", Sha1 = "08aaff412dbf29af3dfbe3bc1b7e7d9ced9b41f0")]
    [PortOf("uc:SBase.SGameInfo:InitScores", Spec = "spec/game/scoring-and-stats.md#rules")]
    public void InitScores(int AP, int DP)
    {
        Level.NbPointsAttack = AP;
        Level.NbPointsDefense = DP;
    }

    /// <summary>
    /// Announces a death to everyone: the damage type is the message switch, 23 for a sniper head shot (counted in
    /// the killer's statistics), 24 for a resigned player.
    /// </summary>
    [Uc("SBase.SGameInfo.BroadcastDeathMessage", Sha1 = "5c4b1759f3617aaeb1a512c49fb5b333ac90dd33")]
    [PortOf("uc:SBase.SGameInfo:BroadcastDeathMessage", Spec = "spec/game/scoring-and-stats.md#rules")]
    public override void BroadcastDeathMessage(Controller? Killer, Controller? Other, SDamageType DamageType)
    {
        int msgSwitch = (int)DamageType;
        if (DamageType == SDamageType.SDTSnipeShot && Other is SPlayerController victim &&
            victim.LastDamages[0].PillTag == K.HeadPill)
        {
            msgSwitch = K.DeathMessageHeadshot;
            if (Killer is SPlayerController { PlayerReplicationInfo.Stats: { } stats })
            {
                stats.Merc_SniperHeadshots++;
            }
        }
        else if (Other?.PlayerReplicationInfo is { bResigned: true })
        {
            msgSwitch = K.DeathMessageResigned;
        }

        BroadcastLocalized(this, DeathMessageClass, msgSwitch, Killer?.PlayerReplicationInfo, Other?.PlayerReplicationInfo);
    }

    /// <summary>
    /// The first active damage volume of the same damage type, set off by a controller, that the victim's pawn
    /// touches.
    /// </summary>
    [Uc("SBase.SGameInfo.FindDamageVolume", Sha1 = "1488dcce31b33e7cac11e2375fa453f9b9694088")]
    [PortOf("uc:SBase.SGameInfo:FindDamageVolume", Spec = "spec/game/scoring-and-stats.md#rules")]
    public SDamageVolume? FindDamageVolume(Pawn? Victim, SDamageType DamageType)
    {
        if (Victim is null)
        {
            return null;
        }

        foreach (Actor a in Victim.Touching)
        {
            if (a is SDamageVolume v && v.bActive && v.DamageType == DamageType && v.TriggerController is not null)
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>Environmental kill statistics for an enemy kill (any kill in Merc Deathmatch).</summary>
    [Uc("SBase.SGameInfo.CountEnvironmentalKill", Sha1 = "9c45b15d8951502f656987aa969e958e07f2dc43")]
    [PortOf("uc:SBase.SGameInfo:CountEnvironmentalKill", Spec = "spec/game/scoring-and-stats.md#rules")]
    public void CountEnvironmentalKill(Controller? Killer, Controller? Killed)
    {
        if (Killer is null || Killed is null || ReferenceEquals(Killer, Killed))
        {
            return;
        }

        if (TeamIndexOf(Killer) == TeamIndexOf(Killed) && MatchInfo?.GameMode != EGameMode.GM_MercDeathMatch)
        {
            return;
        }

        if (Killer.PlayerReplicationInfo?.Stats is { } killerStats)
        {
            killerStats.EnvironmentalKills++;
        }

        if (Killed.PlayerReplicationInfo?.Stats is { } killedStats)
        {
            killedStats.DeathFromEnvironmental++;
        }
    }

    /// <summary>A localized message for every player of one side (a team-less player reads side 0).</summary>
    [Uc("SBase.SGameInfo.BroadcastTeamLocalizedMessage", Sha1 = "bee326dd1a9d3da1091945fede6e50fedb83de68")]
    [PortOf("uc:SBase.SGameInfo:BroadcastTeamLocalizedMessage", Spec = "spec/game/scoring-and-stats.md#rules")]
    public void BroadcastTeamLocalizedMessage(byte Team, ClassRef MessageClass, int Switch = 0,
        PlayerReplicationInfo? RelatedPRI_1 = null, PlayerReplicationInfo? RelatedPRI_2 = null, UObject? OptionalObject = null)
    {
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is PlayerController p && TeamIndexOf(p) == Team)
            {
                Host.ClientCall(p, new GameCall("ReceiveLocalizedMessage")
                {
                    Class = MessageClass,
                    Int = Switch,
                    Pri1 = RelatedPRI_1,
                    Pri2 = RelatedPRI_2,
                    Object = OptionalObject,
                });
            }
        }
    }

    /// <summary>Plays a music track for every SBase player of one side.</summary>
    [Uc("SBase.SGameInfo.BroadcastMusic", Sha1 = "421d83263170e1d2f76fb6df9a28c3ebfc812433")]
    [PortOf("uc:SBase.SGameInfo:BroadcastMusic", Spec = "spec/game/end-of-round.md#rules")]
    public void BroadcastMusic(AssetRef MusicRef, EMusicTransition Transition, byte teamidx)
    {
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is SPlayerController p && TeamIndexOf(p) == teamidx)
            {
                Host.ClientCall(p, new GameCall("ClientSetMusic") { Asset = MusicRef, Int = (int)Transition });
            }
        }
    }

    /// <summary>An objective was taken: the jingle for both sides, then the end check.</summary>
    [Uc("SBase.SGameInfo.NotifyObjectiveTaken", Sha1 = "1f64cb8c19f715456c3ac067a51a24b74d9a4a08")]
    [PortOf("uc:SBase.SGameInfo:NotifyObjectiveTaken", Spec = "spec/game/end-of-round.md#rules")]
    public void NotifyObjectiveTaken(SObjective? Obj)
    {
        var jingle = new AssetRef(K.ObjectiveTakenJingle);
        BroadcastMusic(jingle, EMusicTransition.MTRAN_None, 0);
        BroadcastMusic(jingle, EMusicTransition.MTRAN_None, 1);
        BroadcastScoreTime = Level.TimeSeconds + 2f;
        if (sCheckEndGame())
        {
            sEndGame();
        }
    }

    /// <summary>A Deathmatch dog tag was taken: count it, then the end check.</summary>
    [Uc("SBase.SGameInfo.NotifyDogTag", Sha1 = "ca15710daa78356792ecbb2b5f73251b7f5996d6")]
    [PortOf("uc:SBase.SGameInfo:NotifyDogTag", Spec = "spec/game/end-of-round.md#rules")]
    public void NotifyDogTag()
    {
        if (GameReplicationInfo is not null)
        {
            GameReplicationInfo.NumDogTagsTaken++;
        }

        if (sCheckEndGame())
        {
            sEndGame();
        }
    }
}
