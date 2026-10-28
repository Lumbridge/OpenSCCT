using System.Collections.Generic;
using Godot;
using OpenScct.Server.Online;
using OpenScctGame.Chat;
using OpenScctGame.Online;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>
/// The developer console (spec/reloaded/FEATURES.md rl:qol.console and rl:input.console): the key left of 1 (by its
/// place on the keyboard, whatever the layout; with <c>override_console_key</c> off, Chaos Theory's own key, the
/// apostrophe key) opens and closes it, in the menus and in play. It takes a line of text in a plain monospaced font
/// and runs it through <see cref="ConsoleCommands"/>. While it is open no key, click or mouse movement reaches the
/// game or the menus (key and button releases still pass, so nothing stays held); Escape closes it. The server's
/// system lines (the answers to <c>cmd</c>, <c>!pings</c>...) are shown in it as they arrive.
/// </summary>
public partial class DevConsole : CanvasLayer, IConsoleHost
{
    public const string NodeName = "DevConsole";

    private const int MaxLines = 200;
    private const int ShownLines = 16;
    private const int MaxTyped = 255;

    private readonly List<string> _log = new();
    private readonly List<string> _history = new();
    private ConsoleCommands? _commands;
    private ColorRect? _band;
    private Label? _text;
    private Label? _input;
    private string _typed = string.Empty;
    private int _historyAt;
    private OnlineClient? _client;

    public DevConsole()
    {
        Name = NodeName;
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>The console is open.</summary>
    public bool IsOpen { get; private set; }

    public override void _Ready()
    {
        _commands = new ConsoleCommands(this);
        var font = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New", "monospace"] };
        _band = new ColorRect { Name = "Band", Color = new Color(0f, 0f, 0f, 0.8f), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _band.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        AddChild(_band);
        var box = new VBoxContainer { Name = "Lines" };
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.OffsetLeft = 8f;
        box.OffsetRight = -8f;
        box.OffsetTop = 4f;
        box.OffsetBottom = -4f;
        _band.AddChild(box);
        _text = new Label { Name = "Log", SizeFlagsVertical = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Bottom, ClipText = true };
        _input = new Label { Name = "Input", ClipText = true };
        foreach (Label label in new[] { _text, _input })
        {
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", 16);
            label.AddThemeColorOverride("font_color", new Color(0.85f, 0.95f, 0.85f));
            box.AddChild(label);
        }

        Print("OpenSCCT console. Type help for the commands; Escape closes it.");
    }

    public override void _ExitTree() => Watch(null);

    public override void _Process(double delta)
    {
        // Last in the tree, so its input comes before every scene's.
        if (GetParent() is { } parent && parent.GetChild(-1) != this)
        {
            parent.MoveChild(this, -1);
        }

        Watch(GetTree().Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName)?.Client);
        if (IsOpen && _band is not null)
        {
            Vector2 size = GetViewport().GetVisibleRect().Size;
            _band.Size = new Vector2(size.X, Mathf.Round(size.Y * 0.45f));
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false } key && IsConsoleKey(key) && CanOpen())
            {
                Open();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        // Releases go on, so a key held when the console opened is not left down.
        if (@event is InputEventKey { Pressed: false } or InputEventMouseButton { Pressed: false })
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        if (@event is not InputEventKey { Pressed: true } typed)
        {
            return;
        }

        if (!typed.Echo && IsConsoleKey(typed))
        {
            Close();
            return;
        }

        switch (typed.Keycode)
        {
            case Key.Escape:
                Close();
                return;
            case Key.Enter or Key.KpEnter:
                Submit();
                return;
            case Key.Backspace:
                if (_typed.Length > 0)
                {
                    _typed = _typed[..^1];
                }

                break;
            case Key.Up or Key.Down:
                StepHistory(typed.Keycode == Key.Up ? -1 : 1);
                break;
            default:
                if (typed.Unicode >= 32 && !char.IsControl((char)typed.Unicode) && _typed.Length < MaxTyped)
                {
                    _typed += (char)typed.Unicode;
                }

                break;
        }

        Redraw();
    }

    /// <summary>Opens the console.</summary>
    public void Open()
    {
        IsOpen = true;
        _typed = string.Empty;
        _historyAt = _history.Count;
        if (_band is not null)
        {
            _band.Visible = true;
        }

        Redraw();
    }

    /// <summary>Closes the console, dropping the typed line.</summary>
    public void Close()
    {
        IsOpen = false;
        _typed = string.Empty;
        if (_band is not null)
        {
            _band.Visible = false;
        }
    }

    /// <summary>Adds a line to the console's log.</summary>
    public void Print(string line)
    {
        _log.Add(line);
        if (_log.Count > MaxLines)
        {
            _log.RemoveRange(0, _log.Count - MaxLines);
        }

        Redraw();
    }

    private bool IsConsoleKey(InputEventKey key) =>
        key.PhysicalKeycode == (SettingsStore.Current.OverrideConsoleKey ? Key.Quoteleft : Key.Apostrophe);

    /// <summary>Not over an open chat line or a text box that has the focus (the key is typed there).</summary>
    private bool CanOpen() =>
        ChatInput.Typing is null && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit);

    private void Submit()
    {
        string line = _typed.Trim();
        _typed = string.Empty;
        if (line.Length == 0)
        {
            Redraw();
            return;
        }

        if (_history.Count == 0 || _history[^1] != line)
        {
            _history.Add(line);
        }

        _historyAt = _history.Count;
        Print("> " + line);
        foreach (string reply in _commands!.Run(line))
        {
            Print(reply);
        }
    }

    private void StepHistory(int step)
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyAt = Mathf.Clamp(_historyAt + step, 0, _history.Count);
        _typed = _historyAt < _history.Count ? _history[_historyAt] : string.Empty;
    }

    private void Redraw()
    {
        if (_text is null || _input is null)
        {
            return;
        }

        int from = System.Math.Max(0, _log.Count - ShownLines);
        _text.Text = string.Join("\n", _log.GetRange(from, _log.Count - from));
        _input.Text = "> " + _typed + "_";
    }

    /// <summary>Follows the visit's client, to show the server's system lines.</summary>
    private void Watch(OnlineClient? client)
    {
        if (ReferenceEquals(client, _client))
        {
            return;
        }

        if (_client is not null)
        {
            _client.MessageReceived -= OnMessage;
        }

        _client = client;
        if (_client is not null)
        {
            _client.MessageReceived += OnMessage;
        }
    }

    private void OnMessage(SessionMessage message)
    {
        if (message.IsSystem)
        {
            Print(message.Text);
        }
    }

    // ---- IConsoleHost ---------------------------------------------------------------------------------------

    GameSettings IConsoleHost.Settings => SettingsStore.Current;

    void IConsoleHost.SaveSettings(GameSettings settings, bool display)
    {
        SettingsStore.Save(settings);
        if (display)
        {
            SettingsApplier.ApplyDisplay(settings, GetViewport());
        }
        else
        {
            SettingsApplier.ApplyPacing(settings);
        }
    }

    bool IConsoleHost.Online => _client is { State: OnlineClientState.Lobby or OnlineClientState.Loading or OnlineClientState.Match or OnlineClientState.Results };

    void IConsoleHost.ServerCommand(string command) => _client?.Command(command);

    void IConsoleHost.ServerLogin(string password) => _client?.Admin(password);

    void IConsoleHost.Chat(string text) => _client?.Chat(text);

    void IConsoleHost.Quit() => GetTree().Quit();
}
