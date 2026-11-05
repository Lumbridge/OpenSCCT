using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Project;
using OpenScctGame.Hub;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Real menu entry points and input, with fake browser/clipboard adapters and no report submission.</summary>
public sealed class HelpFeedbackSmoke : SmokeTest
{
    public const string TestName = "help-feedback";
    public override string Name => TestName;
    private static readonly Vector2I[] Sizes = [new(800, 600), new(1280, 720), new(3840, 2160)];
    private static readonly ReportSession[] Sessions = [ReportSession.Offline, ReportSession.OnlineHost,
        ReportSession.OnlineClient, ReportSession.HubHost, ReportSession.HubClient, ReportSession.Replay];
    private static readonly (string Name, ProjectDestination Destination)[] Links =
    [
        ("ReportBug", ProjectDestination.BugReport), ("SuggestFeature", ProjectDestination.FeatureRequest),
        ("ExistingIssues", ProjectDestination.Issues), ("Documentation", ProjectDestination.Documentation),
        ("Contribute", ProjectDestination.Contributing), ("Development", ProjectDestination.Development),
        ("PullRequests", ProjectDestination.PullRequests), ("NewPullRequest", ProjectDestination.NewPullRequest),
    ];
    private SubViewport? _viewport;
    private Control? _page;
    private Button? _entry;
    private HelpFeedbackScreen? _help;
    private LobbyLayoutSmoke.FixtureLobby? _lobby;
    private Node? _currentScene;
    private ProjectLinkActions? _actions;
    private string _opened = "", _copied = "";
    private bool _copyFails;
    private int _size, _case, _phase, _frames, _gameActions;
    private PlayRoot? _play;
    private PlaySession? _playSession;
    private int _playPhase, _playFrames;
    private long _clientTick, _serverTick;
    private double _openedAt;
    private string? _capturedScreenshot;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_size == Sizes.Length) return PlayStep(context);
        if (_viewport is null) Create(context);
        // Save fully revealed titles rather than a transient frame of the menu's typing animation.
        if (_case == 0 && _phase == 1 && context.ElapsedSeconds - _openedAt < 2.1) return SmokeResult.Running;
        if (++_frames < 6) return SmokeResult.Running;
        _frames = 0;
        string label = $"{Sizes[_size]} case {_case} phase {_phase}: ";
        string? error = null;
        switch (_phase++)
        {
            case 0:
                if (_entry is null || !_entry.IsVisibleInTree()) return SmokeResult.Fail(label + "help entry is missing");
                Rect2 helpRect = _entry.GetGlobalRect();
                if (Math.Abs(helpRect.Position.Y - 24f) > 1f || Math.Abs(helpRect.End.X - (Sizes[_size].X - 24f)) > 1f ||
                    Math.Abs(helpRect.Size.X - 220f) > 1f || Math.Abs(helpRect.Size.Y - 36f) > 1f || !_entry.Flat)
                    return SmokeResult.Fail(label + "help utility differs from the shared top-right placement or styling");
                if (_page is MenuPage menuPage && (menuPage.PageButtons.Contains(_entry) ||
                    menuPage.FindChild("Title", true, false) is Control title && title.GetGlobalRect().Intersects(helpRect)))
                    return SmokeResult.Fail(label + "help is still in the footer or overlaps the title");
                if (_case < 4) Shot("entry-" + _case);
                if (_page is InGameMenu)
                {
                    foreach (Button button in _page.GetChildren().OfType<Button>())
                        if (!new Rect2(Vector2.Zero, Sizes[_size]).Encloses(button.GetRect()) ||
                            (button != _entry && button.GetRect().Intersects(_entry.GetRect())))
                            return SmokeResult.Fail(label + "in-game button is outside the viewport or overlaps help");
                }
                if (_case == 5) Shot("longest-menu");
                _entry.GrabFocus();
                _entry.EmitSignal(Button.SignalName.Pressed);
                _openedAt = context.ElapsedSeconds;
                break;
            case 1:
                _help = _page!.GetNodeOrNull<HelpFeedbackScreen>("HelpFeedback");
                if (_help is null) return SmokeResult.Fail(label + "help replaced the parent instead of opening over it");
                error = LayoutProblem();
                if (error is not null) break;
                if (_viewport!.GuiGetFocusOwner() != Find<Button>("ReportBug"))
                    return SmokeResult.Fail(label + "help did not focus its primary report action");
                if (!Find<ScrollContainer>("BodyScroll").GetGlobalRect().Encloses(Find<Button>("ReportBug").GetGlobalRect()))
                    return SmokeResult.Fail(label + "initial report action is scrolled out of view");
                if (_case == 0) Shot("links");
                _lobby?.AddRemotePlayer();
                Press("ReportBug");
                break;
            case 2:
                if (_lobby is not null && _page!.FindChildren("*", nameof(Control), true, false).OfType<Control>()
                    .Any(control => control != _help && !_help!.IsAncestorOf(control) && control.FocusMode != Control.FocusModeEnum.None))
                    return SmokeResult.Fail(label + "a newly added lobby row can take focus behind help");
                if (Find<Label>("ActionStatus").Text != _actions!.Message || !_actions.Message.Contains("Could not open", StringComparison.Ordinal))
                    return SmokeResult.Fail(label + "browser failure has no readable fallback");
                if (Find<LineEdit>("TargetUrl").Text != _opened)
                    return SmokeResult.Fail(label + "fallback address differs from requested report");
                if (BugReportProblem(_opened, _help!.Context) is { } bugProblem)
                    return SmokeResult.Fail(label + bugProblem);
                error = LayoutProblem();
                if (error is not null) break;
                if (_case == 0) Shot("browser-fallback");
                if (_help!.FindChild("CopyLink", true, false) is not null || !Find<LineEdit>("TargetUrl").Visible)
                    return SmokeResult.Fail(label + "copy-link control remains or browser fallback is hidden");
                foreach (var link in Links)
                {
                    Press(link.Name);
                    if (_opened != ProjectLinks.Url(link.Destination, _help!.Context))
                        return SmokeResult.Fail(label + "wrong destination for " + link.Name);
                    if (link.Destination == ProjectDestination.FeatureRequest &&
                        !_opened.StartsWith(ProjectLinks.Repository + "/issues/new?template=feature_request.yml&", StringComparison.Ordinal))
                        return SmokeResult.Fail(label + "feature requests no longer use the GitHub form");
                }
                Press("CopyDetails");
                if (_copied != ProjectLinks.EnvironmentDetails(_help!.Context) || !_copied.Contains("Source: OpenSCCT game", StringComparison.Ordinal))
                    return SmokeResult.Fail(label + "copy details lost the displayed context");
                if (_case >= 4 && _help.Context.Session != Sessions[_case - 4])
                    return SmokeResult.Fail(label + "play session role was lost");
                if (_case is 1 or 2 && _help.Context.Session != (_case == 1 ? ReportSession.OnlineHost : ReportSession.OnlineClient))
                    return SmokeResult.Fail(label + "lobby role was lost");
                _copyFails = true;
                Press("CopyDetails");
                if (!Find<Label>("ActionStatus").Text.Contains("copy it manually", StringComparison.Ordinal) || Find<TextEdit>("ReportDetails").Editable)
                    return SmokeResult.Fail(label + "clipboard failure removed selectable details");
                _copyFails = false;
                KeyInput(Key.Tab);
                break;
            case 3:
                if (_viewport!.GuiGetFocusOwner() is not { } keyboardFocus || !_help!.IsAncestorOf(keyboardFocus))
                    return SmokeResult.Fail(label + "Tab escaped into the background menu");
                if (_case == 0) Shot("fallback");
                KeyInput(Key.Escape, echo: true);
                if (_help!.IsQueuedForDeletion()) return SmokeResult.Fail(label + "key repeat closed help");
                KeyInput(Key.Escape);
                break;
            case 4:
                error = Preserved(context);
                if (error is not null) break;
                _entry!.EmitSignal(Button.SignalName.Pressed);
                break;
            case 5:
                _help = _page!.GetNodeOrNull<HelpFeedbackScreen>("HelpFeedback");
                if (_help is null) return SmokeResult.Fail(label + "help could not reopen");
                Press("Back");
                break;
            case 6:
                error = Preserved(context);
                if (error is not null) break;
                _entry!.EmitSignal(Button.SignalName.Pressed);
                break;
            case 7:
                _help = _page!.GetNodeOrNull<HelpFeedbackScreen>("HelpFeedback");
                _viewport!.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = true });
                _viewport.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = false });
                break;
            case 8:
                if (_viewport!.GuiGetFocusOwner() is not { } controllerFocus || !_help!.IsAncestorOf(controllerFocus))
                    return SmokeResult.Fail(label + "controller navigation left help");
                bool enabled = SettingsStore.Current.Pad.Enabled;
                try
                {
                    SettingsStore.Current.Pad.Enabled = true;
                    _viewport.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true });
                    _viewport.PushInput(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = false });
                }
                finally { SettingsStore.Current.Pad.Enabled = enabled; }
                break;
            case 9:
                error = Preserved(context);
                if (error is not null) break;
                _viewport!.QueueFree();
                _viewport = null;
                _page = null;
                _lobby = null;
                _phase = 0;
                if (++_case == 4 + Sessions.Length) { _case = 0; _size++; }
                break;
        }
        return error is null ? SmokeResult.Running : SmokeResult.Fail(label + error);
    }

    private void Create(SmokeContext context)
    {
        _currentScene = context.Tree.CurrentScene;
        _viewport = new SubViewport { Size = Sizes[_size], RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        context.Tree.Root.AddChild(_viewport);
        _viewport.AddChild(new PadMenus());
        _gameActions = 0;
        _actions = new ProjectLinkActions(url => { _opened = url; return false; }, text =>
        {
            if (_copyFails) throw new NotSupportedException("Clipboard deliberately unavailable in smoke.");
            _copied = text;
        });
        if (_case == 0)
        {
            var menu = new MainMenu { HelpActions = _actions };
            _viewport.AddChild(menu); _page = menu; _entry = menu.HelpButton;
        }
        else if (_case is 1 or 2)
        {
            _lobby = new LobbyLayoutSmoke.FixtureLobby { CanControl = _case == 1 };
            _lobby.SetReady(true);
            var menu = new LobbyRoomScreen { Session = _lobby, HelpActions = _actions };
            _viewport.AddChild(menu); _page = menu; _entry = menu.HelpButton;
            if (_lobby.CanControl) menu.Parameters!.Load(menu.Parameters.Setup with { TimeLimit = 7 });
            ((LineEdit)menu.FindChild("ChatLine", true, false)).Text = "Unsent chat survives help";
        }
        else if (_case == 3)
        {
            var menu = new HubScreen { HelpActions = _actions };
            _viewport.AddChild(menu); _page = menu; _entry = menu.HelpButton;
            ((LineEdit)menu.FindChild("Address", true, false)).Text = "private-address-not-in-report.invalid";
        }
        else
        {
            ReportSession session = Sessions[_case - 4];
            var menu = new InGameMenu
            {
                Now = () => 2, HelpActions = _actions,
                HelpContext = new ReportContext { Source = ReportSource.Game, Version = "smoke+prefill", Platform = "Test OS / x64",
                    Session = session, Map = "TestBox & Annex", Mode = "Story", Side = session == ReportSession.Replay ? "Spectator" : "Spy" },
            };
            menu.Model.Open(new InGameMenuContext(session is ReportSession.OnlineHost or ReportSession.HubHost,
                session == ReportSession.Offline ? InGameMenuNet.None : InGameMenuNet.Lan, MatchBegun: true, Lives: 3), 0);
            menu.Acted += _ => _gameActions++;
            _viewport.AddChild(menu); _page = menu; _entry = menu.HelpButton;
        }
    }

    private string? Preserved(SmokeContext context)
    {
        if (!ReferenceEquals(context.Tree.CurrentScene, _currentScene) || _page!.IsQueuedForDeletion()) return "help changed the active scene";
        if (_page.GetNodeOrNull<HelpFeedbackScreen>("HelpFeedback") is not null) return "help did not close";
        if (_page is InGameMenu game)
        {
            _entry = game.HelpButton;
            if (game.Model.Page != InGameMenuPage.Main || _gameActions != 0) return "help resumed or left the game";
        }
        if (_viewport!.GuiGetFocusOwner() != _entry) return "help did not restore entry-point focus";
        if (_page is LobbyRoomScreen lobby && (_lobby!.Leaves != 0 || !ReferenceEquals(lobby.Session, _lobby) ||
            !_lobby.Members.First(p => p.IsLocal).Ready || lobby.Parameters!.Setup.TimeLimit != (_lobby.CanControl ? 7 : 10) || _lobby.Setup.TimeLimit != 10 ||
            ((LineEdit)lobby.FindChild("ChatLine", true, false)).Text != "Unsent chat survives help"))
            return "help changed lobby membership, ready state, draft settings or unsent chat";
        if (_page is HubScreen hub && ((LineEdit)hub.FindChild("Address", true, false)).Text != "private-address-not-in-report.invalid")
            return "help changed the hub form";
        return null;
    }

    private SmokeResult PlayStep(SmokeContext context)
    {
        if (_playPhase == 4)
            return ++_playFrames < 6 ? SmokeResult.Running : SmokeResult.Pass("800x600, 720p, 4K: all links and copy failures; keyboard/controller/Back; dynamic lobby focus and drafts; six report contexts; actual TestBox session keeps ticking with movement blocked and Escape returns to its open menu");
        if (_play is null)
        {
            context.Tree.Root.Size = new Vector2I(1280, 720);
            _play = PlayRoot.Enter(context.Tree, new PlayConfig
            {
                Settings = new PlaySettings { Map = "TestBox", Bots = 0, Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Inline, MapRoots = [.. context.App.Options.MapRoots] },
                Render = DisplayServer.GetName() != "headless", Hud = DisplayServer.GetName() != "headless", Audio = false, TicksPerFrame = 1,
            });
            return SmokeResult.Running;
        }
        if (_play.LoadError is { } error) return SmokeResult.Fail("TestBox help integration: " + error);
        if (!_play.IsLoaded || _play.Session?.LocalPawn is null || _play.Menu?.Menu is null) return SmokeResult.Running;
        if (++_playFrames < (_playPhase == 2 ? 24 : 6)) return SmokeResult.Running;
        _playFrames = 0;
        var menu = _play.Menu;
        switch (_playPhase++)
        {
            case 0:
                _playSession = _play.Session;
                // Native input is normally enabled by PlayRoot in a window; enable it for the headless variant too.
                _play.Controls!.Enabled = true;
                menu.Menu.HelpActions = new ProjectLinkActions(url => { _opened = url; return false; }, _ => { });
                menu.Open();
                menu.Menu.HelpButton!.EmitSignal(Button.SignalName.Pressed);
                break;
            case 1:
                if (menu.Menu.GetNodeOrNull<HelpFeedbackScreen>("HelpFeedback") is not { } help ||
                    help.Context.Session != ReportSession.Offline || help.Context.Map != "TestBox" ||
                    help.FindChild("MatchContinues", true, false) is null || _play.Controls!.Enabled)
                    return SmokeResult.Fail("live match help has wrong context, no continuing-match notice, or active player controls");
                ((Button)help.FindChild("ReportBug", true, false)).EmitSignal(Button.SignalName.Pressed);
                if (BugReportProblem(_opened, help.Context) is { } liveBugProblem)
                    return SmokeResult.Fail("live match bug report: " + liveBugProblem);
                if (DisplayServer.GetName() != "headless")
                {
                    help.ScreenshotFolder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "help-feedback-smoke", "captures");
                    ((Button)help.FindChild("CaptureScreenshot", true, false)).EmitSignal(Button.SignalName.Pressed);
                }
                _clientTick = _playSession!.Client.LocalTick;
                _serverTick = _playSession.Host.World.TickCount;
                context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.W, Pressed = true });
                break;
            case 2:
                if (DisplayServer.GetName() != "headless")
                {
                    var captureHelp = menu.Menu.GetNode<HelpFeedbackScreen>("HelpFeedback");
                    _capturedScreenshot = captureHelp.ScreenshotFile;
                    if (_capturedScreenshot is null || !File.Exists(_capturedScreenshot) ||
                        captureHelp.FindChild("ScreenshotPreview", true, false) is not TextureRect { Visible: true, Texture: not null } ||
                        menu.Menu.Modulate.A != 1f)
                        return SmokeResult.Fail("game screenshot was not saved, previewed, or menu visibility was not restored");
                    int uploads = 0;
                    string previousUrl = _opened;
                    captureHelp.UploadScreenshot = (_, _) =>
                    {
                        uploads++;
                        return System.Threading.Tasks.Task.FromException<string>(new System.Net.Http.HttpRequestException("deliberate smoke failure"));
                    };
                    ((Button)captureHelp.FindChild("ReportBug", true, false)).EmitSignal(Button.SignalName.Pressed);
                    if (_opened != previousUrl || !((Label)captureHelp.FindChild("ScreenshotStatus", true, false)).Text.Contains("Could not attach", StringComparison.Ordinal))
                        return SmokeResult.Fail("failed screenshot upload opened an incomplete report or lost the retry message");
                    captureHelp.UploadScreenshot = (png, _) =>
                    {
                        uploads++;
                        return System.Threading.Tasks.Task.FromResult(new string('a', 64));
                    };
                    ((Button)captureHelp.FindChild("ReportBug", true, false)).EmitSignal(Button.SignalName.Pressed);
                    if (!_opened.Contains("&capture=" + new string('a', 64), StringComparison.Ordinal) || uploads != 2)
                        return SmokeResult.Fail("captured screenshot was not automatically attached to the report URL");
                    ((Button)captureHelp.FindChild("ReportBug", true, false)).EmitSignal(Button.SignalName.Pressed);
                    if (uploads != 2) return SmokeResult.Fail("reopening the browser reuploaded the same capture");
                    ((CheckBox)captureHelp.FindChild("IncludeScreenshot", true, false)).ButtonPressed = false;
                    ((Button)captureHelp.FindChild("ReportBug", true, false)).EmitSignal(Button.SignalName.Pressed);
                    if (_opened.Contains("capture=", StringComparison.Ordinal) || uploads != 2)
                        return SmokeResult.Fail("unchecking the screenshot still attached or uploaded it");
                    using Image preview = context.Tree.Root.GetTexture().GetImage();
                    preview.SavePng(Path.Combine(RenderSmokeArgs.RepoRoot, "local", "help-feedback-smoke", "capture-preview.png"));
                    using Image captured = Image.LoadFromFile(_capturedScreenshot);
                    if (captured.IsEmpty() || captured.GetWidth() > 1280 || captured.GetHeight() > 1280 || new FileInfo(_capturedScreenshot).Length > 2 * 1024 * 1024)
                        return SmokeResult.Fail("game screenshot is empty or exceeds attachment limits");
                }
                if (!ReferenceEquals(_play.Session, _playSession) || context.Tree.CurrentScene != _play || _playSession!.IsStopped ||
                    _playSession.ServerFailure is not null || _playSession.Client.LocalTick <= _clientTick ||
                    _playSession.Host.World.TickCount <= _serverTick || _playSession.Input.Get("BT_Up") != 0 ||
                    _playSession.Input.Up != 0 || _play.Controls!.Enabled)
                    return SmokeResult.Fail("help stopped/replaced the live match, paused its simulation, or allowed movement input");
                context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.W, Pressed = false });
                context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = false });
                break;
            case 3:
                if (menu.Model.Page != InGameMenuPage.Main || _play.Controls!.Enabled ||
                    !ReferenceEquals(_play.Session, _playSession) || _playSession!.IsStopped ||
                    context.Tree.Root.GuiGetFocusOwner() != menu.Menu.HelpButton)
                    return SmokeResult.Fail("help Escape resumed/left the live match or failed to restore menu focus");
                menu.Close();
                if (!_play.Controls.Enabled) return SmokeResult.Fail("closing the menu did not restore player controls");
                _playSession.Stop();
                _play.QueueFree();
                break;
        }
        return SmokeResult.Running;
    }

    private static string? BugReportProblem(string url, ReportContext expected)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host != "master.lumbridge.co.uk" || uri.AbsolutePath != "/help/report-bug")
            return "bug report does not open the website Discord reporting flow";
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).ToDictionary(part => Uri.UnescapeDataString(part[0]),
                part => part.Length == 2 ? Uri.UnescapeDataString(part[1].Replace("+", " ", StringComparison.Ordinal)) : string.Empty);
        if (!query.TryGetValue("area", out string? area) || area != "game" ||
            !query.TryGetValue("version", out string? version) || version != "OpenSCCT game " + expected.Version ||
            !query.TryGetValue("platform", out string? platform) || platform != expected.Platform)
            return "website report lost its game area, version or platform prefill";
        if (!query.TryGetValue("environment", out string? environment) || environment != ProjectLinks.EnvironmentDetails(expected))
            return "website report lost the displayed environment details";
        foreach (var (label, value) in new[] { ("Game version", expected.Version), ("Build", expected.Build),
            ("Commit", expected.Commit), ("Platform", expected.Platform), ("Map", expected.Map), ("Mode", expected.Mode), ("Side", expected.Side) })
            if (!string.IsNullOrEmpty(value) && !environment.Split('\n').Contains(label + ": " + value))
                return "website report lost its " + label + " context";
        string? session = expected.Session switch
        {
            ReportSession.Menu => "Menus", ReportSession.Offline => "Offline",
            ReportSession.OnlineHost => "Online host", ReportSession.OnlineClient => "Online client",
            ReportSession.HubHost => "Hub host", ReportSession.HubClient => "Hub client", ReportSession.Replay => "Replay", _ => null,
        };
        if (session is not null && !environment.Split('\n').Contains("Session: " + session))
            return "website report lost its session role";
        if (environment.Contains("private-address-not-in-report.invalid", StringComparison.Ordinal))
            return "website report included the private hub address";
        return null;
    }

    private string? LayoutProblem()
    {
        var problems = LayoutCheck.BoundsProblems(_help!, Sizes[_size]);
        problems.AddRange(LayoutCheck.Problems(_help!, Sizes[_size].X));
        return problems.FirstOrDefault();
    }

    private T Find<T>(string name) where T : Node => (T)_help!.FindChild(name, true, false);
    private void Press(string name) => Find<Button>(name).EmitSignal(Button.SignalName.Pressed);
    private void KeyInput(Key key, bool echo = false)
    {
        _viewport!.PushInput(new InputEventKey { Keycode = key, Pressed = true, Echo = echo });
        _viewport.PushInput(new InputEventKey { Keycode = key, Pressed = false });
    }

    private void Shot(string view)
    {
        if (DisplayServer.GetName() == "headless") return;
        string folder = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "help-feedback-smoke");
        Directory.CreateDirectory(folder);
        using Image image = _viewport!.GetTexture().GetImage();
        image.SavePng(Path.Combine(folder, $"help-{Sizes[_size].X}x{Sizes[_size].Y}-{view}.png"));
    }
}
