using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Project;
using OpenScctGame.Options;
using OpenScctGame.Play;

namespace OpenScctGame.UI;

/// <summary>A local help page over the current screen. Opening it never replaces or leaves a session.</summary>
public partial class HelpFeedbackScreen : MenuPage
{
    private readonly Dictionary<Control, FocusModeEnum> _backgroundFocus = new();
    private Control? _returnFocus;
    private ProjectLinkActions? _actions;
    private Label? _message;
    private LineEdit? _url;
    private bool _closed;
    private int _initialLayoutFrames = 4;
    private SceneTree? _tree;
    private Viewport? _viewport;
    private Button? _firstAction;
    private TextureRect? _screenshotPreview;
    private Label? _screenshotStatus;
    private bool _capturingScreenshot;
    internal string? ScreenshotFolder { get; set; }
    internal string? ScreenshotFile { get; private set; }
    private Button? _showScreenshot;

    internal ReportContext Context { get; init; } = new() { Source = ReportSource.Game };
    internal ProjectLinkActions? Actions { get; init; }
    internal Action? Closed { get; init; }
    internal bool MatchContinues { get; init; }

    internal static HelpFeedbackScreen? Open(Node parent, Control? returnFocus = null, ReportContext? context = null,
        ProjectLinkActions? actions = null, Action? closed = null, bool? matchContinues = null)
    {
        if (parent.GetNodeOrNull<HelpFeedbackScreen>("HelpFeedback") is { } existing && !existing.IsQueuedForDeletion())
            return existing;
        ReportContext report = context ?? GameReportContext.Capture(parent.GetTree());
        var screen = new HelpFeedbackScreen
        {
            Name = "HelpFeedback", _returnFocus = returnFocus ?? parent.GetViewport().GuiGetFocusOwner(),
            Context = report, Actions = actions, Closed = closed,
            MatchContinues = matchContinues ?? (parent.GetTree().CurrentScene is PlayRoot { Config.Demo: null }),
        };
        parent.AddChild(screen);
        return screen;
    }

    public override void _Ready()
    {
        BuildFrame("Help & feedback", 900f, responsive: true);
        MouseFilter = MouseFilterEnum.Stop;
        _actions = Actions ?? new ProjectLinkActions(url => OS.ShellOpen(url) == Error.Ok, DisplayServer.ClipboardSet);
        DisableBackgroundFocus(GetParent());
        Body.AddThemeConstantOverride("separation", 12);
        Text("Introduction", "Feature requests and pull requests open in your browser and use GitHub sign-in.");
        if (MatchContinues) Text("MatchContinues", "The match continues while this menu is open.", warning: true);

        var feedback = Panel(Body, "Feedback", "Tell us about it");
        _firstAction = AddLink(feedback, "Suggest a feature", ProjectDestination.FeatureRequest, "SuggestFeature");
        AddLink(feedback, "Browse existing issues", ProjectDestination.Issues, "ExistingIssues");

        if (GetTree().CurrentScene is PlayRoot && DisplayServer.GetName() != "headless")
        {
            var capture = UiStyle.MenuButton("Save game screenshot", CaptureScreenshot, "CaptureScreenshot");
            capture.CustomMinimumSize = new Vector2(0f, 42f);
            Body.AddChild(capture);
            _screenshotPreview = new TextureRect { Name = "ScreenshotPreview", Visible = false,
                CustomMinimumSize = new Vector2(0, 180), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
            Body.AddChild(_screenshotPreview);
            _showScreenshot = UiStyle.MenuButton("Show saved screenshot", () =>
            {
                if (ScreenshotFile is { } file) OS.ShellShowInFileManager(file);
            }, "ShowScreenshot");
            _showScreenshot.Visible = false;
            Body.AddChild(_showScreenshot);
            _screenshotStatus = Text("ScreenshotStatus", string.Empty);
        }

        Text("DetailsHint", "Game and session details");
        var details = new TextEdit
        {
            Name = "SessionDetails", Text = ProjectLinks.EnvironmentDetails(Context), Editable = false,
            CustomMinimumSize = new Vector2(0f, 140f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        details.AddThemeColorOverride("font_readonly_color", UiStyle.MenuText);
        Body.AddChild(details);
        Button copyDetails = UiStyle.MenuButton("Copy details", () => { _actions.CopyDetails(Context); RefreshMessage(); }, "CopyDetails");
        copyDetails.CustomMinimumSize = new Vector2(0f, 40f);
        Body.AddChild(copyDetails);
        _message = Text("ActionStatus", string.Empty);
        _url = new LineEdit { Name = "TargetUrl", Editable = false, Visible = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0f, 40f) };
        _url.AddThemeColorOverride("font_uneditable_color", UiStyle.MenuText);
        Body.AddChild(_url);
        AddLink(Body, "Documentation", ProjectDestination.Documentation, "Documentation");
        var contribute = Panel(Body, "Project", "Contribute");
        AddLink(contribute, "Contribution guide", ProjectDestination.Contributing, "Contribute");
        AddLink(contribute, "Developer guide", ProjectDestination.Development, "Development");
        AddLink(contribute, "View pull requests", ProjectDestination.PullRequests, "PullRequests");
        AddLink(contribute, "Open a pull request", ProjectDestination.NewPullRequest, "NewPullRequest");
        Text("ContributionHint", "To contribute code, make your changes on a branch or fork first, then open a pull request on GitHub.");
        PageButton("Back", Close, "Back");
        _tree = GetTree();
        _viewport = GetViewport();
        _tree.NodeAdded += OnBackgroundNodeAdded;
        _viewport.GuiFocusChanged += KeepFocusInside;
        _firstAction.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        // TextEdit and nested containers settle over several frames; keep the first action visible on entry.
        if (_initialLayoutFrames > 0 && --_initialLayoutFrames == 0 &&
            PageCanvas.GetNodeOrNull<ScrollContainer>("BodyScroll") is { } scroll) scroll.ScrollVertical = 0;
    }

    private async void CaptureScreenshot()
    {
        if (_capturingScreenshot || _closed) return;
        _capturingScreenshot = true;
        // Hide the whole menu ancestor, including its dimming panel, for one rendered frame.
        CanvasItem hidden = GetParent() as InGameMenu ?? (CanvasItem)this;
        Color modulate = hidden.Modulate;
        try
        {
            hidden.Modulate = new Color(modulate.R, modulate.G, modulate.B, 0f);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (_closed || !IsInsideTree()) return;
            using Image image = GetViewport().GetTexture().GetImage();
            string folder = ScreenshotFolder ?? ProjectSettings.GlobalizePath("user://screenshots");
            Error directory = DirAccess.MakeDirRecursiveAbsolute(folder);
            string file = System.IO.Path.Combine(folder, "screenshot-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            if (directory != Error.Ok || image.SavePng(file) != Error.Ok)
            {
                _screenshotStatus!.Text = "Could not save the screenshot.";
                return;
            }
            _screenshotPreview!.Texture = ImageTexture.CreateFromImage(image);
            _screenshotPreview.Show();
            _screenshotStatus!.Text = "Screenshot saved.";
            ScreenshotFile = file;
            _showScreenshot!.Show();
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            if (!_closed) _screenshotStatus!.Text = "Could not save the screenshot.";
        }
        finally
        {
            if (IsInstanceValid(hidden)) hidden.Modulate = modulate;
            _capturingScreenshot = false;
        }
    }

    private Label Text(string name, string text, bool warning = false)
    {
        var label = new Label { Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = warning ? UiStyle.MenuFocus : UiStyle.Text };
        Body.AddChild(label);
        return label;
    }

    private Button AddLink(Container parent, string caption, ProjectDestination destination, string name)
    {
        Button button = UiStyle.MenuButton(caption, () => OpenLink(destination), name);
        button.TooltipText = "Opens your browser";
        button.CustomMinimumSize = new Vector2(0f, 42f);
        parent.AddChild(button);
        return button;
    }

    private void OpenLink(ProjectDestination destination)
    {
        if (_closed) return;
        _actions!.Open(destination, Context);
        RefreshMessage();
    }

    private void RefreshMessage()
    {
        _message!.Text = _actions!.Message;
        _url!.Text = _actions.TargetUrl ?? string.Empty;
        _url.Visible = _actions.Message.StartsWith("Could not open", StringComparison.Ordinal);
        // Keep browser failures and their selectable fallback address visible.
        Callable.From(() =>
        {
            if (!_closed && IsInsideTree() && PageCanvas.GetNodeOrNull<ScrollContainer>("BodyScroll") is { } scroll)
                scroll.EnsureControlVisible(_url.Visible ? _url : _message);
        }).CallDeferred();
    }

    private void DisableBackgroundFocus(Node parent)
    {
        foreach (Node node in parent.FindChildren("*", nameof(Control), recursive: true, owned: false))
        {
            if (node is not Control control || control == this || IsAncestorOf(control)) continue;
            DisableFocus(control);
        }
    }

    private void DisableFocus(Control control)
    {
        _backgroundFocus.TryAdd(control, control.FocusMode);
        control.FocusMode = FocusModeEnum.None;
    }

    private void OnBackgroundNodeAdded(Node node)
    {
        if (node is not Control control || !GetParent().IsAncestorOf(control) || IsAncestorOf(control)) return;
        // Wait for the new control's _Ready/theme setup before remembering its normal focus mode.
        Callable.From(() =>
        {
            if (!_closed && IsInsideTree() && IsInstanceValid(control) && !control.IsQueuedForDeletion() &&
                GetParent().IsAncestorOf(control) && !IsAncestorOf(control)) DisableFocus(control);
        }).CallDeferred();
    }

    private void KeepFocusInside(Control control)
    {
        if (_closed || IsAncestorOf(control) || !GetParent().IsAncestorOf(control)) return;
        DisableFocus(control);
        _firstAction?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } or
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } ||
            (SettingsStore.Current.Pad.Enabled && input is InputEventJoypadButton
                { Pressed: true, ButtonIndex: JoyButton.B or JoyButton.Back or JoyButton.Start }))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        // Keyboard navigation is handled by controls; remaining keys cannot activate the underlying page.
        if (input is InputEventKey) GetViewport().SetInputAsHandled();
    }

    private void RestoreFocus()
    {
        foreach (var pair in _backgroundFocus)
            if (IsInstanceValid(pair.Key) && !pair.Key.IsQueuedForDeletion()) pair.Key.FocusMode = pair.Value;
        _backgroundFocus.Clear();
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        RestoreFocus();
        QueueFree();
        Closed?.Invoke();
        if (IsInstanceValid(_returnFocus) && !_returnFocus!.IsQueuedForDeletion() && _returnFocus.IsVisibleInTree())
            _returnFocus.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_tree)) _tree!.NodeAdded -= OnBackgroundNodeAdded;
        if (IsInstanceValid(_viewport)) _viewport!.GuiFocusChanged -= KeepFocusInside;
        RestoreFocus();
    }
}
