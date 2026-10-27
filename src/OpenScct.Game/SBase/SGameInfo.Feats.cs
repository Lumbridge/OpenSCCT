using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

// OpenSCCT's own kill bookkeeping for achievements (not retail): sorts an enemy kill by its cause into the retail
// statistics retail declares but never fills (Merc_*Kills, Spy_DeathFrom*) and into the round's feats (FeatKeys).
public partial class SGameInfo
{
    /// <summary>The merc gun class of a weapon for the camo ladders ("ar", "smg", "shotgun"), or null.</summary>
    public static string? GunClassOf(SWeapon? weapon) => weapon switch
    {
        SWeaponDefense_Uzi => "smg",
        SWeaponDefense_Shotgun => "shotgun",
        SWeaponDefense => "ar",
        _ => null,
    };

    /// <summary>Counts one enemy kill's cause for the killer and the victim.</summary>
    private static void CountKillFeats(Controller killer, Controller killed, Pawn? killedPawn, SDamageType type)
    {
        USPlayerStats? ks = killer.PlayerReplicationInfo?.Stats;
        USPlayerStats? vs = killed.PlayerReplicationInfo?.Stats;
        if (ks is null)
        {
            return;
        }

        switch (type)
        {
            case SDamageType.DamTypeGrabBreak:
                ks.AddFeat(FeatKeys.SpyGrabKills);
                ks.AddFeat(FeatKeys.SpyNeckBreaks);
                return;
            case SDamageType.DamTypeGrabChoke:
                ks.AddFeat(FeatKeys.SpyGrabKills);
                ks.AddFeat(FeatKeys.SpyChokes);
                return;
            case SDamageType.SDamage_DropAttacked:
                ks.AddFeat(FeatKeys.SpyDropKills);
                return;
        }

        if (killer.Pawn is not SPAWNDEFENSE merc)
        {
            return;
        }

        if (GadgetBlast.CurrentIsMine is { } mine)
        {
            if (mine)
            {
                ks.Merc_MineKills++;
                ks.AddFeat(FeatKeys.MercMineKills);
                if (vs is not null)
                {
                    vs.Spy_DeathFromMine++;
                }
            }
            else
            {
                ks.Merc_GrenadeKills++;
                ks.AddFeat(FeatKeys.MercGrenadeKills);
                if (vs is not null)
                {
                    vs.Spy_DeathFromGrenade++;
                }
            }

            return;
        }

        if (killedPawn is not null && ReferenceEquals(merc.m_PawnToTerminate, killedPawn))
        {
            ks.Merc_TerminateKills++;
            ks.AddFeat(FeatKeys.MercTerminateKills);
            if (vs is not null)
            {
                vs.Spy_DeathFromTerminate++;
            }

            return;
        }

        if (killed is SPlayerController { PoisonInstigator: not null } || type is not (SDamageType.SDTBase or SDamageType.SDTSnipeShot))
        {
            return;
        }

        if (GunClassOf(merc.MyWeapon) is not { } gun)
        {
            return;
        }

        bool headshot = killed is SPlayerController vpc && vpc.LastDamages[0].PillTag == K.HeadPill;
        ks.AddFeat(FeatKeys.GunKills(gun));
        if (headshot)
        {
            ks.AddFeat(FeatKeys.MercHeadshots);
            ks.AddFeat(FeatKeys.GunHeadshots(gun));
        }

        if (type == SDamageType.SDTSnipeShot)
        {
            ks.Merc_WeaponSniperKills++;
            ks.AddFeat(FeatKeys.MercSniperKills);
            if (vs is not null)
            {
                vs.Spy_DeathFromWeaponSniper++;
            }
        }
        else
        {
            ks.Merc_WeaponHipfireKills++;
            ks.AddFeat(FeatKeys.MercHipfireKills);
            if (vs is not null)
            {
                vs.Spy_DeathFromWeaponHipfire++;
            }
        }
    }
}
