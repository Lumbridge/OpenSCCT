using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Modes;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Movement;

namespace OpenScct.Server;

/// <summary>Live bot controls, owned by the simulation thread.</summary>
public interface IPracticeBotControl
{
    IReadOnlyList<PlayerController> Controllers { get; }
    bool Stopped { get; set; }
    PlayerController? Add(byte team);
    void Remove(PlayerController controller);
    bool ChangeTeam(PlayerController controller, byte team);
}

/// <summary>Shared by online practice and offline Visit Map. Called on the host thread.</summary>
public static class PracticeBotCommands
{
    public const string WeaponHelp = "botweapon <rifle|uzi|shotgun> (all living merc bots)";

    private static readonly (string Verb, string Usage, string Description)[] HelpEntries =
    [
        ("addbot", "addbot <team> [number]", "Spawn bots on that team; number defaults to 1."),
        ("kickbot", "kickbot <team> [number]", "Remove the newest bots on that team; number defaults to 1."),
        ("kickbot", "kickbot all", "Remove every bot."),
        ("placebot", "placebot <team>", "Spawn one bot in front of you, facing you; requires clear floor."),
        ("botstop", "botstop", "Stop all bots, including future spawns and respawns."),
        ("botresume", "botresume", "Resume all stopped bots."),
        ("bothealth", "bothealth <team|all> <1-10000>", "Set living bots' health; resets on respawn."),
        ("botweapon", "botweapon <rifle|uzi|shotgun>", "Equip all living merc bots; resets on respawn."),
        ("botteam", "botteam <from> <to> [number]", "Change team and respawn bots; number defaults to 1."),
    ];

    /// <summary>Read-only help shared by the console and online/offline chat; null for a different topic.</summary>
    public static IReadOnlyList<string>? GetHelp(string topic, bool chat = false)
    {
        topic = topic.Trim().ToLowerInvariant();
        if (topic != "bots" && !IsCommand(topic)) return null;
        string prefix = chat ? "!" : "";
        var lines = new List<string> { "Bot commands: practice / Visit Map only; session-control permission required online." };
        foreach (var entry in HelpEntries)
            if (topic == "bots" || topic == entry.Verb)
                lines.Add(prefix + entry.Usage + " - " + entry.Description);
        if (topic is "bots" or "addbot" or "kickbot" or "placebot" or "bothealth" or "botteam")
            lines.Add("Teams: spy / merc (also spies / mercs or 0 / 1). Counts: 1-24, subject to session capacity.");
        if (topic == "botweapon") lines.Add($"Example: {prefix}botweapon shotgun. Use {prefix}botweapon rifle to restore the normal gun.");
        if (topic == "bots") lines.Add($"Use {prefix}help <command> for details, e.g. {prefix}help botweapon.");
        return lines;
    }

    public static bool IsCommand(string verb) => verb is "addbot" or "kickbot" or "placebot" or "botstop" or "botresume" or "bothealth" or "botweapon" or "botteam";

    public static string Run(ServerHost host, IPracticeBotControl bots, PlayerController? player, int capacity, string verb, string arg)
    {
        if (host.World.Level.Info.Game is not SGameInfo game || !PracticeRules.IsOn(game))
            return "Bot commands work in practice / Visit Map games only.";
        if (game.bGameEnded || game.GameReplicationInfo?.bMatchHasBegun != true || player is null)
            return "Bot commands work during a round.";

        string[] args = arg.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (verb == "kickbot" && args.Length == 1 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var all = bots.Controllers.ToArray();
            foreach (var pc in all) bots.Remove(pc);
            return $"Removed {all.Length} bot(s).";
        }
        if (verb == "botteam")
        {
            int number = 1;
            if (args.Length is < 2 or > 3 || !TryTeam(args[0], out byte from) || !TryTeam(args[1], out byte to) ||
                (args.Length == 3 && (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out number) || number is < 1 or > 24)))
                return "usage: botteam <spy|merc> <spy|merc> [number: 1-24, default 1]";
            if (from == to) return "Choose different source and destination teams.";
            if (to == 0 && game.MatchInfo?.GameMode == EGameMode.GM_MercDeathMatch) return "Merc Deathmatch only supports merc bots.";
            var targets = bots.Controllers.Where(pc => SGameInfo.TeamIndexOf(pc) == from).Reverse().Take(number).ToArray();
            int moved = targets.Count(pc => bots.ChangeTeam(pc, to));
            return $"Moved {moved} bot(s) to {(to == 0 ? "spies" : "mercs")} (respawned).";
        }
        if (verb == "bothealth") return SetHealth(bots, args);
        if (verb == "botweapon") return SetWeapon(bots, args);
        if (verb is "botstop" or "botresume")
        {
            if (args.Length != 0) return "usage: " + verb;
            bots.Stopped = verb == "botstop";
            return bots.Stopped ? "Bots stopped (including future spawns)." : "Bots resumed.";
        }

        int count = 1;
        if (args.Length < 1 || args.Length > (verb == "placebot" ? 1 : 2) || !TryTeam(args[0], out byte team) ||
            (args.Length == 2 && (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1 || count > 24)))
            return $"usage: {verb} <spy|merc>" + (verb == "placebot" ? "" : " [number: 1-24, default 1]");
        string side = team == 0 ? "spy" : "merc";
        if (verb == "kickbot")
        {
            var remove = bots.Controllers.Where(pc => SGameInfo.TeamIndexOf(pc) == team).Reverse().Take(count).ToArray();
            foreach (var pc in remove) bots.Remove(pc);
            return $"Removed {remove.Length} {side} bot(s).";
        }
        if (team == 0 && game.MatchInfo?.GameMode == EGameMode.GM_MercDeathMatch)
            return "Merc Deathmatch only supports merc bots.";
        if (verb == "placebot" && player.Pawn is not { Health: > 0, bDeleteMe: false })
            return "You must be alive to place a bot.";

        int added = 0;
        for (; added < count; added++)
        {
            int players = 0;
            foreach (var pc in host.World.Level.AllActors<PlayerController>())
                if (!pc.bDeleteMe && pc.PlayerReplicationInfo is { bOnlySpectator: false }) players++;
            if (players >= capacity || bots.Add(team) is not { } bot) break;
            if (verb == "placebot" && !Place(host, bot, player))
            {
                bots.Remove(bot);
                return "No safe space in front of you. Bot not placed.";
            }
        }
        return added == 0 ? "Could not add a bot: the session is full or no spawn is available." :
            $"{(verb == "placebot" ? "Placed" : "Added")} {added} {side} bot(s)." +
            (added < count ? " No room for the remaining bots." : "");
    }

    private static string SetHealth(IPracticeBotControl bots, string[] args)
    {
        byte team = 255;
        bool all = args.Length > 0 && args[0].Equals("all", StringComparison.OrdinalIgnoreCase);
        if (args.Length != 2 || (!all && !TryTeam(args[0], out team)))
            return "usage: bothealth <spy|merc|all> <1-10000>";
        var targets = bots.Controllers.Where(pc => (all || SGameInfo.TeamIndexOf(pc) == team) && pc.Pawn is SBasePawn { Health: > 0, bDeleteMe: false }).ToArray();
        if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int health) || health is < 1 or > 10000)
            return "usage: bothealth <spy|merc|all> <1-10000>";
        foreach (var pc in targets) pc.Pawn!.Health = health;
        return $"Set {targets.Length} bot(s) to {health} health.";
    }

    private static string SetWeapon(IPracticeBotControl bots, string[] args)
    {
        if (args.Length != 1) return "usage: " + WeaponHelp;
        string weapon = args[0].ToLowerInvariant();
        if (weapon is not ("rifle" or "uzi" or "shotgun")) return "usage: " + WeaponHelp;
        var targets = bots.Controllers.Where(pc => pc.Pawn is SPAWNDEFENSE { Health: > 0, bDeleteMe: false }).ToArray();
        if (targets.Length == 0) return "No living merc bots to equip.";
        int changed = 0;
        foreach (var pc in targets)
        {
            var pawn = (SBasePawn)pc.Pawn!;
            ClassRef cls = weapon switch
            {
                "uzi" => new("SBase.SWeaponDefense_Uzi"), "shotgun" => new("SBase.SWeaponDefense_Shotgun"),
                _ => Loadouts.MercWeapon,
            };
            if (DeviceClasses.Spawn(pawn, cls, pawn, default, pawn.Location, pawn.Rotation) is not SWeapon next) continue;
            var old = pawn.PrimaryWeapon ?? pawn.MyWeapon;
            (old as SWeaponDefense)?.Dummy?.Destroy();
            old?.Destroy();
            pawn.MyWeapon = pawn.PrimaryWeapon = null;
            pawn.ActiveWeaponSlot = 0;
            pawn.WeaponSwitching = false;
            pawn.WeaponSwitchUntil = 0;
            if (pawn is SPAWNDEFENSE merc) merc.WeaponDummy = null;
            next.GiveTo(pawn);
            if (pawn is SPAWNDEFENSE equipped) equipped.WeaponDummy = (next as SWeaponDefense)?.Dummy;
            changed++;
        }
        return $"Equipped {changed} merc bot(s) with {weapon}.";
    }

    private static bool TryTeam(string value, out byte team)
    {
        team = value.ToLowerInvariant() switch { "spy" or "spies" or "0" => 0, "merc" or "mercs" or "mercenary" or "mercenaries" or "1" => 1, _ => 255 };
        return team != 255;
    }

    private static bool Place(ServerHost host, PlayerController bot, PlayerController player)
    {
        if (bot.Pawn is not { } pawn || player.Pawn is not { } own || host.Map.Physics is not { } physics) return false;
        FVector forward = new FRotator(0, player.Rotation.Yaw, 0).Vector();
        FVector at = own.Location + forward * (own.CollisionRadius + pawn.CollisionRadius + 100f);
        at.Z += pawn.CollisionHeight - own.CollisionHeight + 8f;
        var extent = physics.Driver.GetCylinderExtent(pawn);
        var flags = PhysicsDriver.MoveTraceFlags(pawn);
        // Check the path as well as the destination, so a thin wall cannot be crossed.
        if (physics.Collision.LineCheck(out _, own, at, own.Location + new FVector(0, 0, 8), default, flags) ||
            !physics.Collision.LineCheck(out var floor, pawn, at - new FVector(0, 0, 160), at, extent, flags) || floor.Normal.Z < 0.7f)
            return false;
        at = floor.Location + new FVector(0, 0, 2);
        if (!physics.Driver.SetLocation(pawn, at)) return false;
        pawn.Velocity = pawn.Acceleration = default;
        pawn.bJustTeleported = true;
        pawn.Rotation = bot.Rotation = new FRotator(0, (player.Rotation.Yaw + 32768) & 65535, 0);
        if (bot is SPlayerController pc) pc.MyDesiredRotation = pc.LocalMyDesiredRotation = bot.Rotation;
        return true;
    }
}
