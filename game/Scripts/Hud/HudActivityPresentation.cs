using System;
using OpenScct.Game.Combat;
using OpenScct.Core.Engine;

namespace OpenScctGame.Hud;

/// <summary>Readable event-feed copy, independent of message transport, legacy labels and rendering.</summary>
public static class HudActivityPresentation
{
    public static string Action(HudActivityKind kind) => kind switch
    {
        HudActivityKind.Kill => "Eliminated",
        HudActivityKind.Death => "Died",
        HudActivityKind.Suicide => "Self-eliminated",
        HudActivityKind.Resign => "Resigned",
        HudActivityKind.Sleep => "Put to sleep",
        HudActivityKind.Knockout => "Knocked out",
        HudActivityKind.Join => "Joined",
        HudActivityKind.Leave => "Left",
        HudActivityKind.Match => "Match update",
        HudActivityKind.LastLife => "Last life used",
        _ => "Event",
    };

    public static string Cause(HudActivityCause cause) => cause switch
    {
        HudActivityCause.None => "",
        HudActivityCause.Firearm => "Firearm",
        HudActivityCause.Headshot => "Headshot",
        HudActivityCause.Explosion => "Explosion",
        HudActivityCause.Charge => "Charge",
        HudActivityCause.DropAttack => "Drop attack",
        HudActivityCause.Elbow => "Elbow strike",
        HudActivityCause.Grab => "Grab",
        HudActivityCause.NeckBreak => "Neck break",
        HudActivityCause.Choke => "Choke",
        HudActivityCause.Fall => "Fall",
        HudActivityCause.Crush => "Crush",
        HudActivityCause.Poison => "Poison",
        HudActivityCause.StickyShock => "Electrical damage",
        HudActivityCause.StickyBlast => "Fatal electrical contact",
        HudActivityCause.Taser => "Taser",
        HudActivityCause.ShockCollision => "Fatal charge collision",
        HudActivityCause.Smoke => "Smoke",
        HudActivityCause.StickyCamera => "Camera gas",
        HudActivityCause.ProximityMine => "Proximity mine",
        HudActivityCause.LaserMine => "Laser mine",
        HudActivityCause.PoisonMine => "Poison mine",
        HudActivityCause.FragGrenade => "Frag grenade",
        HudActivityCause.BarrelExplosion => "Explosive barrel",
        HudActivityCause.ExplosiveProp => "Prop damage",
        HudActivityCause.MolotovExplosion => "Molotov blast",
        HudActivityCause.MolotovFire => "Molotov fire",
        HudActivityCause.Fire => "Fire",
        HudActivityCause.Hazard => "Environmental hazard",
        HudActivityCause.LedgePull => "Ledge pull",
        HudActivityCause.Rifle => "Rifle",
        HudActivityCause.Shotgun => "Shotgun",
        HudActivityCause.Smg => "SMG",
        HudActivityCause.Sniper => "Sniper",
        HudActivityCause.Pistol => "Pistol",
        HudActivityCause.Revolver => "Revolver",
        HudActivityCause.GasExplosion => "Gas explosion",
        HudActivityCause.ExplosiveExtinguisher => "Explosive canister",
        HudActivityCause.SodaExplosion => "Soda can blast",
        _ => "Other",
    };

    public static string Cause(HudActivityEntry entry) => Cause(entry.Cause) +
        (entry.Headshot && entry.Cause != HudActivityCause.Headshot ? " headshot" : "");

    public static bool UsesVictimFirstMessage(HudActivityEntry entry) =>
        entry.Kind is HudActivityKind.Kill or HudActivityKind.Death or HudActivityKind.Suicide &&
        entry.Cause is HudActivityCause.ShockCollision or HudActivityCause.StickyBlast;

    public static string ContactLead(HudActivityEntry entry) =>
        (entry.Cause == HudActivityCause.ShockCollision ? "Charged into" : "Contact with") +
        (entry.Actor is null ? "" : " electrified");

    public static string ContactTarget(HudActivityEntry entry) =>
        entry.Actor is null ? "an electrified player" : ParticipantName(entry.Actor);

    public static bool HasTriggerDetail(HudActivityEntry entry) => entry.IsCombat &&
        (entry.Trigger != CombatDeathTrigger.None || entry.TriggeredBy is not null);

    public static string TriggerDetail(HudActivityEntry entry)
    {
        if (!HasTriggerDetail(entry)) return "";
        string? name = entry.TriggeredBy is null ? null : ParticipantName(entry.TriggeredBy);
        return entry.Trigger switch
        {
            CombatDeathTrigger.ChainReaction => name is null ? "Chain reaction" : "Chain reaction · " + name,
            CombatDeathTrigger.Shot when entry.Cause is HudActivityCause.MolotovFire or HudActivityCause.Fire =>
                name is null ? "Ignited by a shot" : "Ignited by " + name,
            CombatDeathTrigger.Shot => name is null ? "Detonated by a shot" : "Shot by " + name,
            _ => name is null ? "Triggered" : "Triggered by " + name,
        };
    }

    /// <summary>Participant names retain their case and Unicode; missing names never invent an identity.</summary>
    public static string ParticipantName(HudActivityParticipant? participant)
    {
        string name = Clean(participant?.Name);
        return name.Length > 0 ? name : "Unknown player";
    }

    /// <summary>The first line of a notice. Participant and action are separated to avoid repeated names.</summary>
    public static string NoticeTitle(HudActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind switch
        {
            HudActivityKind.Join or HudActivityKind.Leave or HudActivityKind.LastLife => ParticipantName(entry.Actor),
            HudActivityKind.Match => "Match update",
            _ => ParticipantName(entry.Victim ?? entry.Actor),
        };
    }

    public static string NoticeDetail(HudActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind switch
        {
            HudActivityKind.Join => "Joined the match",
            HudActivityKind.Leave => "Left the match",
            HudActivityKind.LastLife => "Used " + PlayerPronouns.For(entry.Actor?.Pronouns).Possessive + " last life",
            HudActivityKind.Match => Clean(entry.Text) is { Length: > 0 } text ? text : "Match status updated",
            _ => Action(entry.Kind),
        };
    }

    private static string Clean(string? text) => string.IsNullOrWhiteSpace(text) ? "" :
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
