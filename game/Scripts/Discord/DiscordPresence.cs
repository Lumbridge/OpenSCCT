using System;
using DiscordRPC;
using DiscordRPC.Message;
using Godot;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using OpenScctGame.Hub;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Discord;

/// <summary>
/// Discord Rich Presence (docs/DISCORD.md): shows what the player is doing on their Discord profile through the
/// Discord app running on this PC, and takes a friend's Join back into the game. Lives under the App autoload for the
/// whole run, never in smoke, headless or background runs, and follows the Options toggle live.
/// </summary>
public partial class DiscordPresence : Node
{
    public const string NodeName = "DiscordPresence";

    /// <summary>The OpenSCCT application on the Discord Developer Portal.</summary>
    public const string ApplicationId = "1564377811014389862";

    private const double UpdateSeconds = 1.0;

    private DiscordRpcClient? _client;
    private bool _canJoin;
    private bool _readyLogged;
    private bool _failLogged;
    private double _sinceUpdate = UpdateSeconds;
    private PresenceState? _shown;
    private DateTime _since = DateTime.UtcNow;
    private DiscordJoinSecret? _pendingJoin;

    /// <summary>False for runs that must not touch the player's Discord (smoke tests, headless, background windows).</summary>
    public static bool Wanted(App app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return !app.IsSmokeRun &&
            DisplayServer.GetName() != "headless" &&
            !BackgroundWindow.Wanted(smokeRun: false, OS.GetCmdlineUserArgs());
    }

    public override void _Ready()
    {
        SettingsStore.Saved += OnSettingsSaved;
        if (SettingsStore.Current.DiscordPresence)
        {
            Start();
        }
    }

    public override void _ExitTree()
    {
        SettingsStore.Saved -= OnSettingsSaved;
        Stop();
    }

    private void OnSettingsSaved(GameSettings settings)
    {
        if (settings.DiscordPresence && _client is null)
        {
            Start();
        }
        else if (!settings.DiscordPresence && _client is not null)
        {
            Stop();
        }
    }

    private void Start()
    {
        // Events wait for Invoke in _Process, so they run on Godot's main thread.
        var client = new DiscordRpcClient(ApplicationId, -1, null, false) { SkipIdenticalPresence = true };
        client.OnReady += (_, e) =>
        {
            if (!_readyLogged)
            {
                _readyLogged = true;
                GD.Print("SVM_DISCORD connected as " + e.User?.Username);
            }

            _failLogged = false;
        };
        client.OnConnectionFailed += (_, _) =>
        {
            if (!_failLogged)
            {
                // Discord is not running: the client keeps retrying quietly.
                _failLogged = true;
                GD.Print("SVM_DISCORD no Discord app to talk to; status off until it starts.");
            }
        };
        client.OnJoin += OnJoin;

        _canJoin = RegisterLaunch(client);
        try
        {
            client.Initialize();
            if (_canJoin)
            {
                client.Subscribe(EventType.Join);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            GD.PushWarning("Discord status could not start: " + e.Message);
            client.Dispose();
            return;
        }

        _client = client;
        _shown = null;
        _sinceUpdate = UpdateSeconds;
    }

    private void Stop()
    {
        if (_client is not { } client)
        {
            return;
        }

        _client = null;
        _shown = null;
        try
        {
            client.ClearPresence();
        }
        catch (InvalidOperationException)
        {
            // Not connected: nothing to clear.
        }

        client.Dispose();
    }

    /// <summary>
    /// Tells Discord how to start the game for a friend's Join (discord-&lt;app id&gt;:// under the current user's
    /// classes). The command is the exe alone, without the library's URI argument (Godot would read it as a scene): the
    /// join itself arrives over the pipe once the game is up. Exported Windows builds only; the editor would start Godot
    /// without the project.
    /// </summary>
    private static bool RegisterLaunch(DiscordRpcClient client)
    {
        if (OS.HasFeature("editor") || !OperatingSystem.IsWindows())
        {
            return false;
        }

        string exe = OS.GetExecutablePath().Replace('/', '\\');
        try
        {
            if (!client.RegisterUriScheme(null, exe))
            {
                return false;
            }

            using Microsoft.Win32.RegistryKey? command = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\discord-" + ApplicationId + @"\shell\open\command");
            command?.SetValue(string.Empty, "\"" + exe + "\"");
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            GD.PushWarning("Discord Join is off: could not register the game with Discord (" + e.Message + ").");
            return false;
        }
    }

    public override void _Process(double delta)
    {
        if (_client is not { } client)
        {
            return;
        }

        client.Invoke();
        TryJoin();
        _sinceUpdate += delta;
        if (_sinceUpdate < UpdateSeconds || !client.IsInitialized)
        {
            return;
        }

        _sinceUpdate = 0;
        PresenceState state = PresenceSnapshot.Build(GetTree());
        if (!_canJoin && state.Join is not null)
        {
            state = state with { Join = null };
        }

        if (state == _shown)
        {
            return;
        }

        if (state.Details != _shown?.Details)
        {
            _since = DateTime.UtcNow;
        }

        _shown = state;
        client.SetPresence(ToRichPresence(state, _since));
    }

    private static RichPresence ToRichPresence(PresenceState state, DateTime since)
    {
        var presence = new RichPresence
        {
            Details = state.Details,
            State = state.State.Length > 0 ? state.State : null,
            Timestamps = new Timestamps(since),
            Assets = new Assets
            {
                LargeImageKey = "logo",
                LargeImageText = "OpenSCCT " + BuildInfo.Version,
                SmallImageKey = state.Side,
                SmallImageText = state.Side switch
                {
                    "spy" => "Spy",
                    "merc" => "Mercenary",
                    _ => null,
                },
            },
        };

        if (state.PartyId is { } partyId && state.PartyMax > 0)
        {
            presence.Party = new Party
            {
                ID = partyId,
                Size = state.PartySize,
                Max = state.PartyMax,
                Privacy = Party.PrivacySetting.Public,
            };
            if (state.Join?.Format() is { } secret)
            {
                presence.Secrets = new Secrets { JoinSecret = secret };
            }
        }

        return presence;
    }

    private void OnJoin(object sender, JoinMessage message)
    {
        _pendingJoin = DiscordJoinSecret.Parse(message.Secret);
        GD.Print(_pendingJoin is null
            ? "SVM_DISCORD ignored a Join this build does not understand."
            : "SVM_DISCORD Join to " + _pendingJoin.Kind);
    }

    /// <summary>
    /// Starts a friend's Join once the player is in the menus (a running match waits until it is left), the way the
    /// browser, the address box or the hub page would.
    /// </summary>
    private void TryJoin()
    {
        if (_pendingJoin is not { } join)
        {
            return;
        }

        SceneTree tree = GetTree();
        if (tree.CurrentScene is not Control)
        {
            return;
        }

        _pendingJoin = null;
        HubService hub = HubService.Get(tree);
        OnlineService online = OnlineService.Get(tree);
        switch (join.Kind)
        {
            case DiscordJoinKind.Hub:
                if (online.Client is not null)
                {
                    online.Leave();
                }

                HubScreen.Show(tree);
                hub.Join(join.Value, AccountSettings.Current.AccountName ?? SettingsStore.Current.PlayerName, 0);
                break;
            case DiscordJoinKind.Master:
                if (hub.Client is not null)
                {
                    hub.Leave(showMenu: false);
                }

                FrontEnd.ShowServerBrowser(tree, join.Value);
                break;
            default:
                if (hub.Client is not null)
                {
                    hub.Leave(showMenu: false);
                }

                online.Join(JoinTarget.FromAddress(join.Value));
                break;
        }
    }
}
