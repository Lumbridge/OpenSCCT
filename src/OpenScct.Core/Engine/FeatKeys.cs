namespace OpenScct.Core.Engine;

/// <summary>
/// Names of the per-round feats a player's stats count for achievements (OpenSCCT's own, not retail). They go to the
/// master in the round's results and are stored there by name, so a key is never renamed.
/// </summary>
public static class FeatKeys
{
    public const string SpyGrabKills = "spy.grab_kills";
    public const string SpyNeckBreaks = "spy.neck_breaks";
    public const string SpyChokes = "spy.chokes";
    public const string SpyDropKills = "spy.drop_kills";
    public const string SpyStickyKos = "spy.sticky_kos";
    public const string SpyObjectives = "spy.objectives";

    public const string MercSniperKills = "merc.sniper_kills";
    public const string MercHeadshots = "merc.headshots";
    public const string MercTerminateKills = "merc.terminate_kills";
    public const string MercGrenadeKills = "merc.grenade_kills";
    public const string MercMineKills = "merc.mine_kills";
    public const string MercHipfireKills = "merc.hipfire_kills";
    public const string MercMarks = "merc.marks";
    public const string MercChargeHits = "merc.charge_hits";

    /// <summary>The merc gun classes the camo ladders follow.</summary>
    public static readonly string[] Guns = ["ar", "smg", "shotgun"];

    /// <summary>Kills with one merc gun class ("ar", "smg" or "shotgun").</summary>
    public static string GunKills(string gun) => "merc." + gun + ".kills";

    /// <summary>Headshot kills with one merc gun class.</summary>
    public static string GunHeadshots(string gun) => "merc." + gun + ".headshots";

    /// <summary>Every key a server may send; the master ignores others.</summary>
    public static readonly string[] All =
    [
        SpyGrabKills, SpyNeckBreaks, SpyChokes, SpyDropKills, SpyStickyKos, SpyObjectives,
        MercSniperKills, MercHeadshots, MercTerminateKills, MercGrenadeKills, MercMineKills, MercHipfireKills, MercMarks,
        MercChargeHits,
        GunKills("ar"), GunKills("smg"), GunKills("shotgun"),
        GunHeadshots("ar"), GunHeadshots("smg"), GunHeadshots("shotgun"),
    ];
}
