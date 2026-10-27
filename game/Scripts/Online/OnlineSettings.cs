using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Net.Discovery;

namespace OpenScctGame.Online;

/// <summary>
/// The player's online settings, kept in <c>user://online.cfg</c>: the master (our own; never another game's), its
/// invite key, the manual server list (rl:net.serverlist), how to connect, the hosting choices (port, listing, UPnP,
/// password) and this install's id (bans and returning to a session).
/// </summary>
public sealed class OnlineSettings
{
    public const string FilePath = "user://online.cfg";

    /// <summary>Our master on AWS (docs/ONLINE.md, "Deploying on a VPS"), used until the player sets another.</summary>
    public const string DefaultMasterUrl = "https://master.lumbridge.co.uk";

    /// <summary>
    /// The default master's invite key. It ships in every build, so it keeps the list away from strangers, not from
    /// anyone who has a build.
    /// </summary>
    public const string DefaultInviteKey = "b7cee2a094feca62d43bdac7f80be755a98c62a85c5977a0";

    /// <summary>
    /// Raised when the default master changes: a saved file below it with no master set picks up the default once.
    /// A player who clears the master afterwards keeps it cleared.
    /// </summary>
    private const int MasterDefaultsVersion = 1;

    private static OnlineSettings? _current;

    /// <summary>
    /// The master's address (empty: no internet list, LAN and direct addresses only). <see cref="Load"/> starts from
    /// <see cref="DefaultMasterUrl"/>; smoke runs keep it empty so they never reach the real master.
    /// </summary>
    public string MasterUrl { get; set; } = string.Empty;

    public string InviteKey { get; set; } = string.Empty;

    /// <summary>Servers the browser always asks ("host:port"), as Reloaded's server_list.</summary>
    public List<string> ServerList { get; set; } = new();

    public string LastAddress { get; set; } = "127.0.0.1";

    /// <summary>auto, direct, intro or relay.</summary>
    public string ConnectMode { get; set; } = "auto";

    public string HostName { get; set; } = string.Empty;

    /// <summary>The game port a host opens (28888); <c>SVM_HOST_PORT</c> moves it for tests on a machine that may host a real game.</summary>
    public int HostPort { get; set; } = EnvPort("SVM_HOST_PORT", BuildInfo.DefaultGamePort);

    /// <summary>Max Players of the Create Session page (2 to 8, 4 by default: spec/game/lobby-and-match-creation.md).</summary>
    public int HostMaxPlayers { get; set; } = OpenScct.Server.Online.LobbySetup.DefaultMaxPlayers;

    /// <summary>The Create Session page's last values (LobbySetup options), as the original keeps them in the profile.</summary>
    public string HostSetup { get; set; } = string.Empty;

    /// <summary>
    /// The LAN discovery port a host answers on and the browser searches (28889). <c>SVM_LAN_PORT</c> moves it, so a test
    /// on this machine never meets a game someone else hosts.
    /// </summary>
    public int LanPort { get; set; } = EnvPort("SVM_LAN_PORT", BuildInfo.DefaultLanPort);

    /// <summary>
    /// Smoke runs: the defaults, whatever the player saved, and <see cref="Save"/> writes nothing
    /// (<see cref="OpenScctGame.Options.SettingsStore.Isolated"/>).
    /// </summary>
    public static bool Isolated => OpenScctGame.Options.SettingsStore.Isolated;

    private static int EnvPort(string name, int fallback) =>
        int.TryParse(System.Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out int port) && port is > 0 and < 65536 ? port : fallback;

    /// <summary>List a hosted game on the master (rl:net.listing).</summary>
    public bool HostListed { get; set; } = true;

    /// <summary>List a hub hosted from the game in the master's hub list (docs/HUB.md).</summary>
    public bool HubListed { get; set; } = true;

    /// <summary>Map the host's game port on the router (rl:net.upnp).</summary>
    public bool HostUpnp { get; set; } = true;

    public string HostPassword { get; set; } = string.Empty;

    /// <summary>Only signed-in players (with the master's ticket) may join the hosted game.</summary>
    public bool HostRequireAccounts { get; set; }

    /// <summary>This install's id, made once.</summary>
    public string InstallId { get; set; } = string.Empty;

    public static OnlineSettings Current => _current ??= Load();

    public OpenScct.Server.Online.ConnectPath Path => ConnectMode.ToLowerInvariant() switch
    {
        "direct" => OpenScct.Server.Online.ConnectPath.Direct,
        "intro" or "punch" => OpenScct.Server.Online.ConnectPath.Intro,
        "relay" => OpenScct.Server.Online.ConnectPath.Relay,
        _ => OpenScct.Server.Online.ConnectPath.Auto,
    };

    public static OnlineSettings Load(string path = FilePath)
    {
        var s = new OnlineSettings();
        var file = new ConfigFile();
        bool upgraded = false;
        if (!Isolated)
        {
            s.MasterUrl = DefaultMasterUrl;
            s.InviteKey = DefaultInviteKey;
        }

        if (!Isolated && file.Load(path) == Error.Ok)
        {
            s.MasterUrl = file.GetValue("master", "url", s.MasterUrl).AsString();
            s.InviteKey = file.GetValue("master", "invite_key", s.InviteKey).AsString();
            if (file.GetValue("master", "defaults", 0).AsInt32() < MasterDefaultsVersion && s.MasterUrl.Length == 0)
            {
                s.MasterUrl = DefaultMasterUrl;
                s.InviteKey = DefaultInviteKey;
                upgraded = true;
            }

            s.ServerList = new List<string>(file.GetValue("browser", "server_list", Array.Empty<string>()).AsStringArray());
            s.LastAddress = file.GetValue("browser", "last_address", s.LastAddress).AsString();
            s.ConnectMode = file.GetValue("browser", "connect_path", s.ConnectMode).AsString();
            s.HostName = file.GetValue("host", "name", s.HostName).AsString();
            s.HostPort = Math.Clamp(file.GetValue("host", "port", s.HostPort).AsInt32(), 1, 65535);
            s.HostMaxPlayers = file.GetValue("host", "max_players", s.HostMaxPlayers).AsInt32();
            if (s.HostMaxPlayers is < OpenScct.Server.Online.LobbySetup.MinMaxPlayers or > OpenScct.Server.Online.LobbySetup.MaxMaxPlayers)
            {
                s.HostMaxPlayers = OpenScct.Server.Online.LobbySetup.DefaultMaxPlayers;
            }

            s.HostSetup = file.GetValue("host", "setup", s.HostSetup).AsString();
            s.HostListed = file.GetValue("host", "listed", s.HostListed).AsBool();
            s.HostUpnp = file.GetValue("host", "upnp", s.HostUpnp).AsBool();
            s.HubListed = file.GetValue("hub", "listed", s.HubListed).AsBool();
            s.HostPassword = file.GetValue("host", "password", s.HostPassword).AsString();
            s.HostRequireAccounts = file.GetValue("host", "require_accounts", s.HostRequireAccounts).AsBool();
            s.InstallId = file.GetValue("player", "install_id", s.InstallId).AsString();
        }

        if (s.InstallId.Length != 16)
        {
            s.InstallId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            upgraded = true;
        }

        if (upgraded)
        {
            s.Save(path);
        }

        return s;
    }

    public Error Save(string path = FilePath)
    {
        if (Isolated)
        {
            return Error.Ok;
        }

        var file = new ConfigFile();
        file.SetValue("master", "url", MasterUrl);
        file.SetValue("master", "invite_key", InviteKey);
        file.SetValue("master", "defaults", MasterDefaultsVersion);
        file.SetValue("browser", "server_list", ServerList.ToArray());
        file.SetValue("browser", "last_address", LastAddress);
        file.SetValue("browser", "connect_path", ConnectMode);
        file.SetValue("host", "name", HostName);
        file.SetValue("host", "port", HostPort);
        file.SetValue("host", "max_players", HostMaxPlayers);
        file.SetValue("host", "setup", HostSetup);
        file.SetValue("host", "listed", HostListed);
        file.SetValue("host", "upnp", HostUpnp);
        file.SetValue("hub", "listed", HubListed);
        file.SetValue("host", "password", HostPassword);
        file.SetValue("host", "require_accounts", HostRequireAccounts);
        file.SetValue("player", "install_id", InstallId);
        return file.Save(path);
    }
}
