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
    public const string SpySmokeKos = "spy.smoke_kos";
    public const string SpyStickyCamUses = "spy.sticky_cam_uses";
    public const string SpyShockerHeadshots = "spy.shocker_headshots";
    public const string SpyLedgeGrabs = "spy.ledge_grabs";

    /// <summary>Mercs a spy's chaff jammed (each merc once per grenade).</summary>
    public const string SpyChaffJams = "spy.chaff_jams";

    /// <summary>Mercs killed by a merc mine the spy set off.</summary>
    public const string SpyMineKills = "spy.mine_kills";

    /// <summary>Mercs a spy's bullet hit directly (not the wall marker's zone).</summary>
    public const string SpyBulletHits = "spy.spy_bullet_hits";

    /// <summary>Mercs a spy's flash-bangs flashed (every merc each flash catches counts).</summary>
    public const string SpyFlashes = "spy.flashes";

    public const string MercSniperKills = "merc.sniper_kills";
    public const string MercHeadshots = "merc.headshots";
    public const string MercTerminateKills = "merc.terminate_kills";
    public const string MercGrenadeKills = "merc.grenade_kills";
    public const string MercMineKills = "merc.mine_kills";
    public const string MercHipfireKills = "merc.hipfire_kills";
    public const string MercMarks = "merc.marks";
    public const string MercChargeHits = "merc.charge_hits";
    public const string MercTaserHits = "merc.taser_hits";
    public const string MercMinesPlaced = "merc.mines_placed";
    public const string MercBerserkHits = "merc.berserk_hits";

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
        SpySmokeKos, SpyStickyCamUses, SpyShockerHeadshots, SpyLedgeGrabs, SpyChaffJams, SpyMineKills, SpyBulletHits, SpyFlashes,
        MercSniperKills, MercHeadshots, MercTerminateKills, MercGrenadeKills, MercMineKills, MercHipfireKills, MercMarks,
        MercChargeHits, MercTaserHits, MercMinesPlaced, MercBerserkHits,
        GunKills("ar"), GunKills("smg"), GunKills("shotgun"),
        GunKills("suppressed"), GunKills("automatic"), GunKills("magnum44"), GunKills("desert_eagle"),
        GunHeadshots("suppressed"), GunHeadshots("automatic"), GunHeadshots("magnum44"), GunHeadshots("desert_eagle"),
        GunHeadshots("ar"), GunHeadshots("smg"), GunHeadshots("shotgun"),
    ];
}
