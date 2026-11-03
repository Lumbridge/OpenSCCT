using System;

namespace OpenScct.Core.Engine;

/// <summary>Additional round counters exported as career totals. Keys are permanent; durations are whole seconds.</summary>
public static class CareerStatCounters
{
    public sealed record Counter(string Key, string Category, string Label, Func<USPlayerStats, int> Read, int RoundLimit);

    public static readonly Counter[] All =
    [
        new("career.shotsfired", "Weapons", "Shots fired", s => s.ShotsFired, 1000000),
        new("career.shotshit", "Weapons", "Shots hit", s => s.ShotsHit, 1000000),
        new("career.damagegiven", "Overview", "Damage dealt", s => s.DamageGiven, 10000000),
        new("career.damagetaken", "Overview", "Damage taken", s => s.DamageTaken, 10000000),
        new("career.environmentalkills", "Overview", "Environmental kills", s => s.EnvironmentalKills, 10000),
        new("career.suicides", "Overview", "Suicides", s => s.Suicides, 10000),
        new("career.teamkills", "Overview", "Team kills", s => s.TeamKills, 10000),
        new("career.elbowstrikes", "Overview", "Elbow strikes dealt", s => s.ElbowStrikes, 10000),
        new("career.strikeattacks", "Overview", "Melee strikes dealt", s => s.StrikeAttacks, 10000),
        new("career.dropattacks", "Spy", "Drop attacks", s => s.DropAttacks, 10000),
        new("career.elbowstrikestaken", "Overview", "Elbow strikes received", s => s.ElbowStrikesTaken, 10000),
        new("career.strikeattackstaken", "Overview", "Melee strikes received", s => s.StrikeAttacksTaken, 10000),
        new("career.dropattackstaken", "Mercenary", "Drop attacks received", s => s.DropAttacksTaken, 10000),
        new("career.deathfromdropattack", "Mercenary", "Deaths from drop attacks", s => s.DeathFromDropAttack, 10000),
        new("career.deathfromteammate", "Overview", "Deaths from teammates", s => s.DeathFromTeammate, 10000),
        new("career.deathfromenvironmental", "Overview", "Environmental deaths", s => s.DeathFromEnvironmental, 10000),
        new("career.medkitsused", "Gadgets", "Medkits used", s => s.MedkitsUsed, 10000),
        new("career.spy_grabs", "Spy", "Grabs", s => s.Spy_Grabs, 10000),
        new("career.spy_deathfromweaponhipfire", "Spy", "Deaths from hip-fire", s => s.Spy_DeathFromWeaponHipfire, 10000),
        new("career.spy_deathfromweaponsniper", "Spy", "Deaths from scoped shots", s => s.Spy_DeathFromWeaponSniper, 10000),
        new("career.spy_deathfromterminate", "Spy", "Deaths from merc choke kills", s => s.Spy_DeathFromTerminate, 10000),
        new("career.spy_deathfromgrenade", "Spy", "Deaths from grenades", s => s.Spy_DeathFromGrenade, 10000),
        new("career.spy_deathfrommine", "Spy", "Deaths from mines", s => s.Spy_DeathFromMine, 10000),
        new("career.spy_deathfromjumpingtasedmerc", "Spy", "Deaths from shocked mercenary collisions", s => s.Spy_DeathFromJumpingTasedMerc, 10000),
        new("career.spy_spybullettargets", "Gadgets", "Spy · Spy-bullet targets tagged", s => s.Spy_SpyBulletTargets, 10000),
        new("career.spy_objectivetimecaptured", "Spy", "Objective capture time (seconds)", s => s.Spy_ObjectiveTimeCaptured, 86400),
        new("career.spy_alarmstriggered", "Spy", "Alarms triggered", s => s.Spy_AlarmsTriggered, 10000),
        new("career.merc_knockedfromstickycam", "Mercenary", "Sticky camera knockouts received", s => s.Merc_KnockedFromStickyCam, 10000),
        new("career.merc_knockedfromsmoke", "Mercenary", "Smoke knockouts received", s => s.Merc_KnockedFromSmoke, 10000),
        new("career.merc_deathfromgrab", "Mercenary", "Deaths from grabs", s => s.Merc_DeathFromGrab, 10000),
        new("career.merc_deathfromledgegrab", "Mercenary", "Deaths from ledge grabs", s => s.Merc_DeathFromLedgeGrab, 10000),
        new("career.merc_spytrackersplaced", "Gadgets", "Mercenary · Spy trackers placed", s => s.Merc_SpyTrackersPlaced, 10000),
        new("career.merc_spytraptargets", "Gadgets", "Mercenary · Spy-trap targets caught", s => s.Merc_SpyTrapTargets, 10000),
        new("career.merc_securedbombs", "Mercenary", "Bombs secured", s => s.Merc_SecuredBombs, 10000),
        new("career.merc_securedisks", "Mercenary", "Disks returned", s => s.Merc_SecureDisks, 10000),
        new("career.merc_gasmasktimeused", "Gadgets", "Mercenary · Gas-mask time (seconds)", s => (int)Math.Clamp(float.IsFinite(s.Merc_GasMaskTimeUsed) ? s.Merc_GasMaskTimeUsed : 0f, 0f, 86400f), 86400),
        new("career.merc_backpacksgiven", "Gadgets", "Mercenary · Backpacks given", s => s.Merc_BackpacksGiven, 10000),
        new("career.merc_backpackstaken", "Gadgets", "Mercenary · Backpacks taken", s => s.Merc_BackpacksTaken, 10000),
        new("career.merc_ammostocksused", "Gadgets", "Mercenary · Ammo stocks used", s => s.Merc_AmmoStocksUsed, 10000),
    ];

    public const string ShotsFired = "career.shotsfired";
    public const string ShotsHit = "career.shotshit";

    public static int RoundLimit(string key, int fallback) => Array.Find(All, c => c.Key == key)?.RoundLimit ?? fallback;
}
