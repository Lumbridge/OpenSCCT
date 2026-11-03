using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Game.Combat;

namespace OpenScctGame.Hud;

/// <summary>Licensed Game-icons.net SVG silhouettes embedded with the game, decoded once and shared by all HUDs.</summary>
public static class HudActivityIcons
{
    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.Ordinal);
    public static string NameFor(HudActivityEntry entry)
    {
        if (!entry.IsCombat) return Outcome(entry.Kind);
        string name = entry.Cause != HudActivityCause.None ? entry.Cause switch
        {
            HudActivityCause.Firearm => "firearm", HudActivityCause.Headshot => "headshot",
            HudActivityCause.Explosion => "explosion", HudActivityCause.StickyBlast => "electrified-contact",
            HudActivityCause.Charge => "charge", HudActivityCause.DropAttack => "drop-attack", HudActivityCause.Fall => "fall",
            HudActivityCause.Elbow => "melee", HudActivityCause.Grab => "grab", HudActivityCause.Choke => "choke",
            HudActivityCause.NeckBreak => "neck-break", HudActivityCause.Crush => "crush",
            HudActivityCause.Poison => "poison", HudActivityCause.StickyShock => "shock",
            HudActivityCause.Taser => "taser", HudActivityCause.ShockCollision => "shock-collision",
            HudActivityCause.Smoke => "smoke", HudActivityCause.StickyCamera => "zzz",
            HudActivityCause.ProximityMine => "proximity-mine", HudActivityCause.LaserMine => "laser-mine",
            HudActivityCause.PoisonMine => "poison-mine", HudActivityCause.FragGrenade => "grenade",
            HudActivityCause.BarrelExplosion => "barrel", HudActivityCause.ExplosiveProp => "explosive-prop",
            HudActivityCause.MolotovExplosion => "molotov", HudActivityCause.MolotovFire => "molotov-fire",
            HudActivityCause.Fire => "fire", HudActivityCause.Hazard => "hazard", HudActivityCause.LedgePull => "ledge-pull",
            HudActivityCause.Rifle => "rifle", HudActivityCause.Shotgun => "shotgun", HudActivityCause.Smg => "smg",
            HudActivityCause.Sniper => "sniper", HudActivityCause.Pistol => "pistol", HudActivityCause.Revolver => "revolver",
            HudActivityCause.GasExplosion => "gas-explosion", HudActivityCause.ExplosiveExtinguisher => "extinguisher",
            HudActivityCause.SodaExplosion => "soda",
            _ => Outcome(entry.Kind),
        } : Outcome(entry.Kind);
        if (entry.Headshot && entry.Cause is HudActivityCause.Rifle or HudActivityCause.Shotgun or HudActivityCause.Smg or
            HudActivityCause.Sniper or HudActivityCause.Pistol or HudActivityCause.Revolver) return name + "-headshot";
        if (entry.Trigger == CombatDeathTrigger.Shot && entry.Cause is HudActivityCause.ProximityMine or HudActivityCause.LaserMine or
            HudActivityCause.PoisonMine or HudActivityCause.BarrelExplosion or HudActivityCause.MolotovExplosion or
            HudActivityCause.ExplosiveProp or HudActivityCause.GasExplosion or HudActivityCause.ExplosiveExtinguisher or HudActivityCause.SodaExplosion)
            return "shot-" + name;
        return name;
    }

    private static string Outcome(HudActivityKind kind) => kind switch
    {
        HudActivityKind.Kill or HudActivityKind.Death or HudActivityKind.Suicide => "death",
        HudActivityKind.Sleep => "sleep", HudActivityKind.Knockout => "knockout",
        HudActivityKind.Join => "join", HudActivityKind.Leave or HudActivityKind.Resign => "leave",
        HudActivityKind.LastLife => "last-life", _ => "match",
    };

    public static Texture2D? For(HudActivityEntry entry) => Load(NameFor(entry));
    internal static Texture2D? Load(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        using Stream? stream = typeof(HudActivityIcons).Assembly.GetManifestResourceStream("EventFeed/" + name + ".svg");
        if (stream is null) { GD.PushWarning("Event feed icon missing: " + name); Cache[name] = null; return null; }
        using var reader = new StreamReader(stream);
        using var image = new Image();
        Error error = image.LoadSvgFromString(reader.ReadToEnd(), .5f); // 256px: enough for 4K + editor enlargement.
        if (error != Error.Ok) { GD.PushWarning("Event feed SVG failed: " + name + ": " + error); Cache[name] = null; return null; }
        var texture = ImageTexture.CreateFromImage(image);
        Cache[name] = texture;
        return texture;
    }
}
