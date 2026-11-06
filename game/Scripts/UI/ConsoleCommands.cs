using System;
using System.Collections.Generic;
using OpenScctGame.Options;
using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>What the console's commands act on (the game, or a list in tests).</summary>
public interface IConsoleHost
{
    /// <summary>The player's settings now.</summary>
    GameSettings Settings { get; }

    /// <summary>Saves new settings and applies them (the window too when <paramref name="display"/>).</summary>
    void SaveSettings(GameSettings settings, bool display);

    /// <summary>The player is on a server (the lobby or a round).</summary>
    bool Online { get; }

    /// <summary>An admin command for the server (<c>cmd</c>); its answers come back as system lines.</summary>
    void ServerCommand(string command);

    /// <summary>Logs in as the server's admin.</summary>
    void ServerLogin(string password);

    /// <summary>A chat line to the server (the <c>!</c> commands).</summary>
    void Chat(string text);

    /// <summary>Bot commands also work in an offline Visit Map session.</summary>
    bool CanSendBotCommands => Online;
    void BotCommand(string command) => Chat("!" + command);

    /// <summary>The local player's current reproduction details, or null when there is no player in a map.</summary>
    string? LocationReport { get; }

    /// <summary>Copies text only when the player explicitly requests it.</summary>
    void CopyText(string text);

    /// <summary>Closes the game.</summary>
    void Quit();
}

/// <summary>
/// The developer console's commands (spec/reloaded/FEATURES.md rl:qol.console): <c>set</c>, <c>get</c> and
/// <c>current</c> on the settings file, a settings key typed alone (<c>max_fps</c>, <c>max_fps 144</c>),
/// the server shortcuts Reloaded has (<c>resign</c>, <c>pings</c>,
/// <c>practice</c>, <c>rmap_next</c>, <c>rmap_prev</c>, <c>map</c>, sent as the matching chat command), the admin's
/// <c>login</c> and <c>cmd</c>, and <c>quit</c>. No Godot here.
/// </summary>
public sealed class ConsoleCommands(IConsoleHost host)
{
    public const string Help =
        "help [command|bots] - Show commands or detailed help.\n" +
        "Settings: set <key> <value> | get <key> | current | <key> [value]\n" +
        "Server: login <password> | cmd <server command> | say <text>\n" +
        "Match: resign | pings | map <name> [mode] | rmap_next | rmap_prev\n" +
        "Practice: practice [on|off] | heal | regen | tank | kill | ammo\n" +
        "Bots (practice / Visit Map): addbot | kickbot | placebot | botstop | botresume\n" +
        "Bot settings: bothealth | botweapon (mercs only) | botteam. Type help bots for usage.\n" +
        "Location: whereami [copy] | Exit: quit";

    private readonly IConsoleHost _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>Runs one typed line and returns what it printed.</summary>
    public IReadOnlyList<string> Run(string line)
    {
        var output = new List<string>();
        string[] parts = (line ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return output;
        }

        string verb = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        switch (verb)
        {
            case "help" or "?":
                output.AddRange(GetHelp(arg));
                break;
            case "current":
                output.AddRange(SettingsConsole.Current(_host.Settings));
                break;
            case "whereami":
                if (arg.Length > 0 && !arg.Equals("copy", StringComparison.OrdinalIgnoreCase))
                {
                    output.Add("usage: whereami [copy]");
                }
                else if (_host.LocationReport is not { } report)
                {
                    output.Add("whereami: no local player in a map");
                }
                else
                {
                    output.AddRange(report.Split('\n'));
                    if (arg.Length > 0)
                    {
                        _host.CopyText(report);
                        output.Add("Location report copied to clipboard.");
                    }
                    else
                    {
                        output.Add("Use whereami copy to copy this report.");
                    }
                }

                break;
            case "get":
                output.Add(SettingsConsole.Get(_host.Settings, arg) is { } value ? arg + " = " + value : $"no setting '{arg}' (try: current)");
                break;
            case "set":
            {
                string[] kv = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                output.Add(kv.Length < 2 ? "usage: set <key> <value>" : Set(kv[0], kv[1]));
                break;
            }

            case "quit" or "exit":
                _host.Quit();
                break;
            case "login" or "admin":
                output.Add(Server(() => _host.ServerLogin(arg), arg.Length > 0, "usage: login <password>"));
                break;
            case "cmd":
                // The server runs it for its host and logged-in admins only (and says nothing to anyone else).
                output.Add(Server(() => _host.ServerCommand(arg), arg.Length > 0, "usage: cmd <server command> (host or admin)"));
                break;
            case "say":
                output.Add(Server(() => _host.Chat(arg), arg.Length > 0, "usage: say <text>"));
                break;
            case "practice" when arg.Length > 0:
                // The host or an admin turns the server's practice mode on or off.
                output.Add(Server(() => _host.ServerCommand("practice " + arg), true, string.Empty));
                break;
            case "addbot" or "kickbot" or "placebot" or "botstop" or "botresume" or "bothealth" or "botweapon" or "botteam":
                if (!_host.CanSendBotCommands) output.Add("not in a game");
                else
                {
                    _host.BotCommand(verb + (arg.Length > 0 ? " " + arg : ""));
                    output.Add("sent to the server");
                }
                break;
            case "resign" or "pings" or "practice" or "heal" or "regen" or "tank" or "kill" or "ammo":
                output.Add(Server(() => _host.Chat("!" + verb), true, string.Empty));
                break;
            case "rmap_next" or "rmap_prev":
                output.Add(Server(() => _host.Chat(verb == "rmap_next" ? "!next" : "!prev"), true, string.Empty));
                break;
            case "map":
                output.Add(Server(() => _host.Chat("!map " + arg), arg.Length > 0, "usage: map <name> [mode]"));
                break;
            default:
                // A settings key alone shows it; with a value it sets it.
                if (SettingsConsole.Get(_host.Settings, verb) is { } current)
                {
                    output.Add(arg.Length > 0 ? Set(verb, arg) : verb + " = " + current);
                }
                else
                {
                    output.Add($"unknown command '{verb}' (type help)");
                }

                break;
        }

        return output;
    }

    private static IReadOnlyList<string> GetHelp(string topic)
    {
        if (topic.Length == 0) return Help.Split('\n');
        if (PracticeBotCommands.GetHelp(topic) is { } bots) return bots;
        string? detail = topic.ToLowerInvariant() switch
        {
            "help" or "?" => "help [command|bots] - Show commands or detailed help; available in menus too.",
            "set" => "set <key> <value> - Change and save a setting. Use current to list keys.",
            "get" => "get <key> - Show a setting's current value.",
            "current" => "current - List all settings and their current values.",
            "login" or "admin" => "login <password> - Log in as the server's admin.",
            "cmd" => "cmd <server command> - Run a host/admin command. Use cmd help for the server's list.",
            "say" => "say <text> - Send a chat message to the server.",
            "resign" => "resign - Give up the current round by spending your remaining lives.",
            "pings" => "pings - Show players' ping, jitter and packet loss.",
            "map" => "map <name> [mode] - Select a map and optional game mode; requires session-control permission.",
            "rmap_next" => "rmap_next - Select the next map; requires session-control permission.",
            "rmap_prev" => "rmap_prev - Select the previous map; requires session-control permission.",
            "practice" => "practice - Show practice information. practice on|off changes the mode (host/admin).",
            "heal" => "heal - Restore your health and cure poison in practice.",
            "regen" => "regen - Toggle your health regeneration in practice.",
            "tank" => "tank - Toggle immunity to damage in practice (suicide still works).",
            "kill" => "kill - Kill your current pawn in practice.",
            "ammo" => "ammo - Refill your ammunition in practice.",
            "whereami" => "whereami [copy] - Show map, position and view details; copy also copies them to the clipboard.",
            "quit" or "exit" => "quit - Close the game.",
            _ => null,
        };
        return [detail ?? $"No help for '{topic}'. Type help for commands or help bots for bot commands."];
    }

    private string Set(string key, string value)
    {
        if (SettingsConsole.Set(_host.Settings, key, value, out string problem) is not { } next)
        {
            return problem;
        }

        _host.SaveSettings(next, SettingsConsole.DisplayKeys.Contains(key));
        return key + " = " + (SettingsConsole.Get(next, key) ?? value);
    }

    private string Server(Action send, bool valid, string usage)
    {
        if (!valid)
        {
            return usage;
        }

        if (!_host.Online)
        {
            return "not on a server";
        }

        send();
        return "sent to the server";
    }
}
