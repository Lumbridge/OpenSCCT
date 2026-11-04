using System;
using System.Collections.Generic;

namespace OpenScct.Core.Engine;

/// <summary>One authoritative round's counters, shared by scoreboards, history and career reporting.</summary>
public static class RoundStatCounters
{
    public static Dictionary<string, int> Read(PlayerReplicationInfo pri)
    {
        var feats = new Dictionary<string, int>(StringComparer.Ordinal);
        if (pri.Stats is { } s)
        {
            foreach (var entry in s.Feats) if (entry.Value > 0) feats[entry.Key] = entry.Value;
            foreach (var counter in CareerStatCounters.All) Set(counter.Key, counter.Read(s));
            Set(FeatKeys.SpyStickyKos, s.Spy_StickyCamKOs);
            Set(FeatKeys.MercChargeHits, s.Merc_ChargeHits);
            Set(FeatKeys.SpySmokeKos, s.Spy_SmokeKOs);
            Set(FeatKeys.SpyStickyCamUses, s.Spy_StickyCamUsed);
            Set(FeatKeys.SpyShockerHeadshots, s.Spy_ShockerGunHeadshots);
            Set(FeatKeys.SpyLedgeGrabs, s.Spy_LedgeGrabs);
            Set(FeatKeys.MercTaserHits, s.Merc_TaserHits);
            Set(FeatKeys.MercMinesPlaced, s.Merc_MinesPlaced);
            Set(FeatKeys.MercBerserkHits, s.Merc_BerserkHits);
        }
        Set(FeatKeys.SpyObjectives, pri.iNbObjectivesTaken);
        if (pri.Team?.TeamIndex == 0)
            Set(FeatKeys.SpyNeutralisations, Value(FeatKeys.EnemyKills) + Value(FeatKeys.SpyStickyKos) +
                Value(FeatKeys.SpySmokeKos) + Value(FeatKeys.SpyGrabKos) + Value(FeatKeys.SpyDropKos));
        return feats;

        int Value(string key) => feats.GetValueOrDefault(key);
        void Set(string key, int value) { if (value > 0) feats[key] = value; }
    }
}
