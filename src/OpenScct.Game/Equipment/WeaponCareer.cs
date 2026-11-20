using System;
using System.Threading;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Equipment;

/// <summary>Weapon progress owned by the authority for one player, retained across rounds.</summary>
public sealed class WeaponCareer
{
    private readonly int[] _kills = new int[WeaponCareerCounters.Count];
    public event Action<int>? KillRecorded;
    public event Action<int, bool>? KillConfirmed;

    public WeaponCareer(int[]? saved = null)
    {
        if (saved is null) return;
        for (int i = 0; i < Math.Min(saved.Length, _kills.Length); i++)
            _kills[i] = Math.Max(0, saved[i]);
    }

    public int Kills(string weapon)
    {
        int index = WeaponCareerCounters.IndexOf(weapon);
        return index >= 0 ? Volatile.Read(ref _kills[index]) : 0;
    }
    public int[] Snapshot() => (int[])_kills.Clone();

    public void SetMinimum(int index, int total)
    {
        int current = Volatile.Read(ref _kills[index]);
        while (current < total)
        {
            int observed = Interlocked.CompareExchange(ref _kills[index], total, current);
            if (observed == current) break;
            current = observed;
        }
    }

    public static int GetKills(Controller? owner, string weapon) =>
        (owner as SPlayerController)?.WeaponProgress.Kills(weapon) ?? 0;

    public static void RecordKill(Controller killer, string weapon, bool headshot = false)
    {
        int index = WeaponCareerCounters.IndexOf(weapon);
        if (index < 0 || killer.Role != ENetRole.ROLE_Authority) return;
        string key = WeaponCareerCounters.Key(index);
        if (Array.IndexOf(WeaponCareerCounters.NewRifleKeys, key) >= 0)
            killer.PlayerReplicationInfo?.Stats?.AddFeat(key);
        if (headshot && MercPrimaryMastery.Find(weapon) is { } primary)
            killer.PlayerReplicationInfo?.Stats?.AddFeat(FeatKeys.GunHeadshots(primary.Id));
        if (killer is SPlayerController pc)
        {
            var career = pc.WeaponProgress;
            int current;
            do { current = Volatile.Read(ref career._kills[index]); }
            while (current < int.MaxValue && Interlocked.CompareExchange(ref career._kills[index], current + 1, current) != current);
            career.KillRecorded?.Invoke(index);
            career.KillConfirmed?.Invoke(index, headshot);
        }
    }
}
