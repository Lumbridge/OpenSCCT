using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScctGame.Hub;
using OpenScctGame.UI;

namespace OpenScctGame.Online;

/// <summary>Queues website joins until the game is ready, then uses its own account and normal connection screens.</summary>
public partial class WebsiteJoinService : Node
{
    public const string ArgumentPrefix = "--svm-join-server=";
    public const string PipeName = "OpenSCCT.WebJoin.Game";
    private readonly CancellationTokenSource _stop = new();
    private NamedPipeServerStream? _pipe;
    private string? _incoming;
    private string? _startupId;
    private Task<bool>? _forward;
    private bool _forwardRetried;
    private string? _pending;
    private bool _accountShown;
    private bool _settingsShown;
    private bool _contentShown;
    private bool _busyShown;
    private Task<LookupResult>? _lookup;
    private string? _lookupToken;
    private CancellationTokenSource? _lookupStop;
    private double _checkAfter;
    private CanvasLayer? _toast;
    private double _toastSeconds;

    private sealed record LookupResult(AccountInfo? Account = null, ServerEntry? Server = null, string? Error = null, bool SignedOut = false);

    public static bool IsServerId(string? id)
    {
        if (id is not { Length: >= 1 and <= 128 }) return false;
        foreach (char c in id)
            if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')) return false;
        return true;
    }

    /// <summary>Website IDs only belong to the official HTTPS master; a link never chooses where credentials go.</summary>
    public static bool UsesOfficialMaster => Uri.TryCreate(OnlineSettings.Current.MasterUrl, UriKind.Absolute, out Uri? configured) &&
        configured.Scheme == Uri.UriSchemeHttps && configured.Host.Equals(new Uri(OnlineSettings.DefaultMasterUrl).Host, StringComparison.OrdinalIgnoreCase) &&
        configured.IsDefaultPort && configured.AbsolutePath == "/" && configured.Query.Length == 0 &&
        configured.Fragment.Length == 0 && configured.UserInfo.Length == 0;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith(ArgumentPrefix, StringComparison.Ordinal) && IsServerId(arg[ArgumentPrefix.Length..]))
                _startupId = arg[ArgumentPrefix.Length..];
        if (_startupId is { } id) _forward = ForwardAsync(id, _stop.Token);
        else StartListening();
    }

    public override void _ExitTree()
    {
        _stop.Cancel();
        _pipe?.Dispose();
        CancelLookup();
        _stop.Dispose();
    }

    public override void _Process(double delta)
    {
        if (_toast is not null && (_toastSeconds -= delta) <= 0)
        {
            _toast.QueueFree();
            _toast = null;
        }
        if (_forward is { IsCompleted: true } forward)
        {
            _forward = null;
            if (forward.Result)
            {
                GetTree().Quit();
                return;
            }
            if (!StartListening())
            {
                // Two games can start together. Only the pipe owner joins; the other forwards once it is ready.
                if (!_forwardRetried && _startupId is { } retry)
                {
                    _forwardRetried = true;
                    _forward = ForwardAsync(retry, _stop.Token);
                    return;
                }
                Toast("Could not send the website join to the running game. Close the other game and try Join again.");
                _startupId = null;
            }
            else
            {
                Interlocked.Exchange(ref _incoming, _startupId);
                _startupId = null;
            }
        }
        if (_forward is not null) return;
        if (Interlocked.Exchange(ref _incoming, null) is { } incoming)
        {
            CancelLookup();
            _pending = incoming;
            _accountShown = _settingsShown = _contentShown = _busyShown = false;
            _checkAfter = 0;
        }
        if (_pending is null || (_checkAfter -= delta) > 0) return;
        _checkAfter = 0.25;

        SceneTree tree = GetTree();
        OnlineService? online = tree.Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName);
        HubService? hub = tree.Root.GetNodeOrNull<HubService>(HubService.NodeName);
        if (online?.Client is not null || online?.Hosting is not null || hub?.Busy == true || hub?.Host is not null ||
            tree.CurrentScene is not (MainMenu or AccountScreen or ServerBrowserScreen or HubScreen))
        {
            if (!_busyShown && tree.CurrentScene is not (Boot or LinuxSetupScreen))
            {
                _busyShown = true;
                Toast("Website join queued. Return to the main menu when you are ready to join the selected server.");
            }
            return;
        }
        if (!UsesOfficialMaster)
        {
            CancelLookup();
            if (!_settingsShown)
            {
                _settingsShown = true;
                ConfirmBox.Open(tree.CurrentScene, new ConfirmBox
                {
                    Name = "WebsiteJoinMaster", Question = "This website join uses " + OnlineSettings.DefaultMasterUrl +
                        ". Your game uses another master. Set the official master in Find a Session to continue, or cancel this join.",
                    YesCaption = "Find a Session", NoCaption = "Cancel join",
                    Yes = () => FrontEnd.ShowServerBrowser(tree), No = CancelPending,
                });
            }
            return;
        }
        if (!LinuxSetupContent.SharedReady(ContentPaths.Shared))
        {
            if (!_contentShown)
            {
                _contentShown = true;
                ConfirmBox.Open(tree.CurrentScene, new ConfirmBox
                {
                    Name = "WebsiteJoinContent", Question = "Install the shared game files before joining. Open Content, finish the downloads, then return to the main menu to continue.",
                    YesCaption = "Open Content", NoCaption = "Cancel join",
                    Yes = () => FrontEnd.ShowContent(tree), No = CancelPending,
                });
            }
            return;
        }
        if (!AccountSettings.Current.SignedIn || AccountSettings.Current.AccountName is null)
        {
            CancelLookup();
            if (!_accountShown)
            {
                _accountShown = true;
                const string notice = "Sign in to OpenSCCT and choose your player name to join the server selected on the website. Your join will continue automatically.";
                // Do not replace an Account page already polling an in-progress Discord sign-in.
                if (tree.CurrentScene is AccountScreen) Toast(notice);
                else FrontEnd.Show(tree, new AccountScreen { Notice = notice });
            }
            return;
        }
        if (_lookup is null)
        {
            _lookupToken = AccountSettings.Current.Token;
            _lookupStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            _lookup = LookupAsync(_pending, _lookupToken, OnlineSettings.Current.InviteKey, _lookupStop.Token);
            Toast("Checking your game account and the selected server…");
            return;
        }
        if (!_lookup.IsCompleted) return;
        LookupResult result = _lookup.Result;
        bool sameAccount = _lookupToken == AccountSettings.Current.Token;
        CancelLookup();
        if (!sameAccount) return;
        if (result.SignedOut)
        {
            AccountService.Get(tree).SignOutLocally();
            _accountShown = false;
            return;
        }
        if (result.Error is { } error)
        {
            CancelPending();
            FrontEnd.ShowMainMenu(tree).ShowStatus("Website join: " + error);
            return;
        }
        AccountService.Get(tree).Update(result.Account!);
        if (AccountSettings.Current.AccountName is null)
        {
            _accountShown = false;
            return;
        }
        string serverId = _pending;
        ServerEntry server = result.Server!;
        CancelPending();
        if (server.Kind == ServerKind.Hub)
        {
            HubScreen.Show(tree);
            HubService.Get(tree).JoinListed(serverId, server.Name, AccountSettings.Current.AccountName!, 0, requireAccount: true);
        }
        else
        {
            FrontEnd.Show(tree, new ServerBrowserScreen
            {
                DiscordJoinId = serverId, JoinIdName = "the selected server", AskJoinPassword = server.Password, RequireSignedInJoin = true,
            });
        }
    }

    private void CancelLookup()
    {
        _lookupStop?.Cancel();
        _lookupStop?.Dispose();
        _lookupStop = null;
        _lookup = null;
        _lookupToken = null;
    }

    private void CancelPending()
    {
        _pending = null;
        CancelLookup();
        if (_toast is not null)
        {
            _toast.QueueFree();
            _toast = null;
        }
    }

    private static async Task<LookupResult> LookupAsync(string id, string token, string inviteKey, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new MasterClient(OnlineSettings.DefaultMasterUrl, inviteKey) { SessionToken = token };
            AccountInfo account = await client.AccountAsync(cancellationToken).ConfigureAwait(false);
            ServerEntry server = await client.ServerAsync(id, cancellationToken).ConfigureAwait(false);
            return new(account, server);
        }
        catch (MasterException e)
        {
            return new(Error: e.Message, SignedOut: e.StatusCode == 401 && !e.Message.Contains("invite key", StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException) { return new(Error: "The request was cancelled. Try Join again."); }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            return new(Error: "The master server could not be reached. Try Join again.");
        }
    }

    private void Toast(string text)
    {
        _toast?.QueueFree();
        _toast = new CanvasLayer { Layer = 110 };
        var panel = new PanelContainer
        {
            AnchorRight = 1f, OffsetLeft = 24f, OffsetTop = 16f, OffsetRight = -24f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        panel.AddChild(new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = UiStyle.MenuFocus,
        });
        _toast.AddChild(panel);
        AddChild(_toast);
        _toastSeconds = 8;
    }

    private bool StartListening()
    {
        if (_pipe is not null) return true;
        try
        {
            _pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 256, 256);
            _ = ListenAsync(_pipe, _stop.Token);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            GD.Print("SVM_WEB_JOIN another game owns the website join channel, or this platform does not support it.");
            return false;
        }
    }

    private async Task ListenAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(1));
                    string? id = await ReadIdAsync(pipe, deadline.Token).ConfigureAwait(false);
                    if (id is not null)
                    {
                        Interlocked.Exchange(ref _incoming, id);
                        await pipe.WriteAsync("OK\n"u8.ToArray(), deadline.Token).ConfigureAwait(false);
                        await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception e) when (e is IOException or OperationCanceledException) { }
                finally { if (pipe.IsConnected) pipe.Disconnect(); }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { }
    }

    private static async Task<string?> ReadIdAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[129];
        int count = 0;
        while (count < bytes.Length)
        {
            int received = await stream.ReadAsync(bytes.AsMemory(count, 1), cancellationToken).ConfigureAwait(false);
            if (received == 0) return null;
            if (bytes[count] == '\n')
            {
                string id = Encoding.ASCII.GetString(bytes, 0, count);
                return IsServerId(id) ? id : null;
            }
            if (bytes[count] > 127) return null;
            count++;
        }
        return null;
    }

    private static async Task<bool> ForwardAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(1));
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await pipe.WriteAsync(Encoding.ASCII.GetBytes(id + "\n"), deadline.Token).ConfigureAwait(false);
            await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            var answer = new byte[3];
            await pipe.ReadExactlyAsync(answer, deadline.Token).ConfigureAwait(false);
            return answer.AsSpan().SequenceEqual("OK\n"u8);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
