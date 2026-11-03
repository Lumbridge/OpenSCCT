using System;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Combat;

/// <summary>Presentation-only source detail. Damage rules and kill credit continue to use SDamageType.</summary>
public enum CombatDeathCause : byte
{
    None, ProximityMine, LaserMine, PoisonMine, FragGrenade, BarrelExplosion, ExplosiveProp,
    MolotovFire, Fire, Hazard, MolotovExplosion, LedgePull, Rifle, Shotgun, Smg, Sniper,
    Pistol, Revolver, Poison, GasExplosion, ExplosiveExtinguisher, SodaExplosion, Crush,
    Taser, ShockCollision,
}

public enum CombatDeathTrigger : byte { None, Shot, ChainReaction }

/// <summary>The existing localized-message integer carries optional source detail, retaining its original low byte.</summary>
public static class CombatActivityDeath
{
    private const int Marker = 1 << 24;
    public static int Encode(int legacySwitch, CombatDeathCause cause, CombatDeathTrigger trigger = CombatDeathTrigger.None) =>
        cause == CombatDeathCause.None ? legacySwitch : Marker | (legacySwitch & 255) | ((int)cause << 8) | ((int)trigger << 16);
    public static int LegacySwitch(int message) => (message & Marker) != 0 ? message & 255 : message;
    public static CombatDeathCause Cause(int message)
    {
        var cause = (CombatDeathCause)((message >> 8) & 255);
        return (message & Marker) != 0 && Enum.IsDefined(cause) ? cause : CombatDeathCause.None;
    }
    public static CombatDeathTrigger Trigger(int message)
    {
        var trigger = (CombatDeathTrigger)((message >> 16) & 255);
        return Cause(message) != CombatDeathCause.None && Enum.IsDefined(trigger) ? trigger : CombatDeathTrigger.None;
    }
}

/// <summary>Stable identity and source data, retained separately from destructible mines, props and poison emitters.</summary>
public readonly record struct CombatDeathSource(CombatDeathCause Cause,
    CombatDeathTrigger Trigger = CombatDeathTrigger.None, PlayerReplicationInfo? Detonator = null,
    PlayerReplicationInfo? Instigator = null);

/// <summary>Nested, synchronous damage provenance. Delayed effects explicitly save and restore their own snapshot.</summary>
public static class CombatDeathContext
{
    [ThreadStatic] private static CombatDeathSource _current;
    public static CombatDeathSource Current => _current;
    public static Scope Push(CombatDeathSource source) => new(source);

    public readonly struct Scope : IDisposable
    {
        private readonly CombatDeathSource _previous;
        internal Scope(CombatDeathSource source) { _previous = _current; _current = source; }
        public void Dispose() => _current = _previous;
    }

    public static CombatDeathSource Triggered(CombatDeathCause cause, Pawn? instigator, SDamageType damage)
    {
        bool weapon = _current.Cause is CombatDeathCause.Rifle or CombatDeathCause.Shotgun or CombatDeathCause.Smg or
            CombatDeathCause.Sniper or CombatDeathCause.Pistol or CombatDeathCause.Revolver;
        bool chain = _current.Cause != CombatDeathCause.None && !weapon ||
            damage is SDamageType.SDamage_Explosion or SDamageType.SDTStickyBlast;
        bool shot = weapon || damage is SDamageType.SDTBase or SDamageType.SDTSnipeShot or SDamageType.SDTStickyShocker;
        PlayerReplicationInfo? detonator = chain ? _current.Detonator ?? instigator?.PlayerReplicationInfo : instigator?.PlayerReplicationInfo;
        return new(cause, chain ? CombatDeathTrigger.ChainReaction : shot ? CombatDeathTrigger.Shot : CombatDeathTrigger.None,
            detonator, instigator?.PlayerReplicationInfo ?? _current.Instigator);
    }

    public static CombatDeathCause MineCause(SMines mine) => mine switch
    {
        SMinePoison => CombatDeathCause.PoisonMine,
        SMineLaser => CombatDeathCause.LaserMine,
        SMineProx => CombatDeathCause.ProximityMine,
        _ => CombatDeathCause.None,
    };

    public static CombatDeathCause PropCause(SGamePlayObject prop)
    {
        // These are authored asset identities, never the old damage enum (extinguishers use melee damage).
        string identity = prop.InitialStaticMesh.Path + " " + prop.StaticMesh.Path + " " + prop.DestroySound.Path;
        if (Has(identity, "molotov")) return CombatDeathCause.MolotovExplosion;
        if (Has(identity, "bombgaz") || Has(identity, "explo_gaz") || Has(identity, "bonbonne")) return CombatDeathCause.GasExplosion;
        if (Has(identity, "barrel") || Has(identity, "fuel_drum") || Has(identity, "baril") || Has(identity, "barril"))
            return CombatDeathCause.BarrelExplosion;
        if (Has(identity, "obj_extinct")) return CombatDeathCause.ExplosiveExtinguisher;
        if (Has(identity, "canette") || Has(identity, "explo_coke")) return CombatDeathCause.SodaExplosion;
        return CombatDeathCause.ExplosiveProp;
    }

    public static CombatDeathCause VolumeCause(SDamageVolume volume, CombatDeathSource source)
    {
        if (source.Cause is CombatDeathCause.MolotovExplosion or CombatDeathCause.MolotovFire || Has(volume.Tag.ToString(), "moloaie"))
            return CombatDeathCause.MolotovFire;
        if (Has(volume.Tag.ToString(), "fire") || Has(volume.Tag.ToString(), "flame")) return CombatDeathCause.Fire;
        // An authored fall/crush/poison/explosion volume already carries meaningful legacy damage semantics.
        return volume.DamageType is SDamageType.SDTBase or SDamageType.SDTBase_NoAnim or SDamageType.NoDamageType
            ? CombatDeathCause.Hazard : CombatDeathCause.None;
    }

    private static bool Has(string value, string text) => value.Contains(text, StringComparison.OrdinalIgnoreCase);
}
