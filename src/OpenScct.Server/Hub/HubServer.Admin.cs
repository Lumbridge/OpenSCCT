using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Game.SBase;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>
/// The hub's moderation: the master's view of its players (the admin portal's server page), the master's account bans,
/// and the admin commands a trusted hub takes from the portal or its own console (<see cref="Execute"/>).
/// </summary>
public sealed partial class HubServer
{
    /// <summary>The signed-in players' accounts, for the master's heartbeat.</summary>
    private IReadOnlyList<long> SignedInAccounts()
    {
        var ids = new List<long>();
        foreach (HubPlayer p in _players)
        {
            if (p.AccountId is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Every player for the master's admin portal.</summary>
    private List<HeartbeatPlayer> PlayerDetailsForMaster()
    {
        var list = new List<HeartbeatPlayer>(_players.Count);
        foreach (HubPlayer p in _players)
        {
            list.Add(new HeartbeatPlayer
            {
                Id = p.Slot.PlayerId, Name = p.Slot.Name, Team = SGameInfo.TeamIndexOf(p.Slot.Controller),
                PingMs = PingOf(p), AccountId = p.AccountId, Admin = p.IsAdmin,
            });
        }

        return list;
    }

    /// <summary>A player's round trip in milliseconds.</summary>
    public static int PingOf(HubPlayer p) => (int)(p.Slot.Connection.Stats.RttMicros / 1000);

    /// <summary>The master banned these accounts: their players leave (the hub's own ban list is not touched).</summary>
    private void OnMasterKicks(IReadOnlyList<long> accounts)
    {
        var banned = new HashSet<long>(accounts);
        foreach (HubPlayer p in _players.ToArray())
        {
            if (p.AccountId is { } id && banned.Contains(id))
            {
                Kick(p, "You are banned from this master.");
            }
        }
    }

    /// <summary>
    /// Admin commands from the master's portal (trusted hubs only): each runs as a console line and its output goes
    /// back with the next heartbeat. Closing the hub is not taken from the master.
    /// </summary>
    private void OnMasterCommands(IReadOnlyList<MasterCommand> commands)
    {
        foreach (MasterCommand command in commands)
        {
            string verb = command.Line.Trim().Split(' ', 2)[0].ToLowerInvariant();
            IReadOnlyList<string> output = verb is "close" or "quit" or "exit"
                ? ["the master cannot close the hub"]
                : Execute(command.Line);
            Log("master admin: " + command.Line + (output.Count > 0 ? " -> " + string.Join(" | ", output) : string.Empty));
            Master?.AddCommandResult(command.Id, output.Count > 0 ? string.Join("\n", output) : "done");
        }
    }

    /// <summary>Sends a player out of the hub with a reason.</summary>
    public void Kick(HubPlayer player, string reason)
    {
        Log($"{player.Slot.Name} kicked: {reason}");
        player.Slot.Connection.Close(reason);
    }

    /// <summary>
    /// One admin console line: help, status, players, say &lt;text&gt;, kick &lt;id|name&gt; [reason],
    /// ban &lt;id|name&gt; [minutes] [reason], unban &lt;account|install id|address|name&gt;. Returns what it printed.
    /// </summary>
    public IReadOnlyList<string> Execute(string line)
    {
        string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return [];
        }

        string rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        switch (parts[0].ToLowerInvariant())
        {
            case "help":
                return ["help, status, players, say <text>, kick <id|name> [reason], ban <id|name> [minutes] [reason], unban <account|install id|address|name>"];
            case "status":
                return [Status()];
            case "players":
            {
                var lines = new List<string>(_players.Count + 1) { $"{_players.Count} player(s)" };
                foreach (HubPlayer p in _players)
                {
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"  {p.Slot.PlayerId,3} {p.Slot.Name,-20} {(SGameInfo.TeamIndexOf(p.Slot.Controller) == 1 ? "merc" : "spy"),-5} {PingOf(p),4} ms  {p.Address}{(p.IsAdmin ? " admin" : "")}{(p.AccountId is { } a ? $" (signed in, account {a})" : " (guest)")}"));
                }

                return lines;
            }

            case "say":
                if (rest.Length == 0)
                {
                    return ["say what?"];
                }

                Say("[admin] " + rest);
                return ["said"];
            case "kick":
            {
                (HubPlayer? who, string reason) = Target(rest);
                if (who is null)
                {
                    return ["no such player"];
                }

                Kick(who, reason.Length > 0 ? reason : "Kicked by an admin.");
                return [$"kicked {who.Slot.Name}"];
            }

            case "ban":
            {
                (HubPlayer? who, string after) = Target(rest);
                if (who is null)
                {
                    return ["no such player"];
                }

                if (Settings.Bans is not { } bans)
                {
                    return ["this hub keeps no ban list"];
                }

                string[] words = after.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                int minutes = words.Length > 0 && int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out int m) ? m : 0;
                string reason = words.Length > 0 && minutes > 0 ? (words.Length > 1 ? words[1] : string.Empty) : after;
                bans.Add(who.InstallId, who.Address, who.Slot.Name, reason, minutes, who.AccountId);
                Kick(who, reason.Length > 0 ? "Banned: " + reason : "Banned by an admin.");
                return [$"banned {who.Slot.Name}{(minutes > 0 ? $" for {minutes} min" : " for good")}"];
            }

            case "unban":
                if (Settings.Bans is not { } list)
                {
                    return ["this hub keeps no ban list"];
                }

                return [$"removed {list.Remove(rest)} ban(s)"];
            default:
                return [$"unknown command \"{parts[0]}\" (help lists them)"];
        }
    }

    /// <summary>The player a command names (his id or his name, then the rest of the line).</summary>
    private (HubPlayer? Player, string Remainder) Target(string text)
    {
        string[] words = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return (null, string.Empty);
        }

        string rest = words.Length > 1 ? words[1].Trim() : string.Empty;
        bool byId = int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id);
        foreach (HubPlayer p in _players)
        {
            if ((byId && p.Slot.PlayerId == id) || string.Equals(p.Slot.Name, words[0], StringComparison.OrdinalIgnoreCase))
            {
                return (p, rest);
            }
        }

        return (null, rest);
    }
}
