using System;
using System.Collections.Generic;
using OpenScctGame.Options;

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

    /// <summary>Closes the game.</summary>
    void Quit();
}

/// <summary>
/// The developer console's commands (spec/reloaded/FEATURES.md rl:qol.console): <c>set</c>, <c>get</c> and
/// <c>current</c> on the settings file, a settings key typed alone (<c>fps_spy</c>, <c>fps_spy 144</c>, as Reloaded's
/// <c>fps_spy</c>, <c>ws_fov</c>, <c>widescreen</c>), the server shortcuts Reloaded has (<c>resign</c>, <c>pings</c>,
/// <c>practice</c>, <c>rmap_next</c>, <c>rmap_prev</c>, <c>map</c>, sent as the matching chat command), the admin's
/// <c>login</c> and <c>cmd</c>, and <c>quit</c>. No Godot here.
/// </summary>
public sealed class ConsoleCommands(IConsoleHost host)
{
    public const string Help =
        "set <key> <value> | get <key> | current | <key> [value] | login <password> | cmd <server command> | " +
        "resign | pings | practice [on|off] | heal | regen | tank | kill | rmap_next | rmap_prev | map <name> [mode] | say <text> | quit";

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
                output.Add(Help);
                break;
            case "current":
                output.AddRange(SettingsConsole.Current(_host.Settings));
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
