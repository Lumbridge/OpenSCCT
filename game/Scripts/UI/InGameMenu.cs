using System;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>
/// Draws the In-Game Menu of an <see cref="InGameMenuModel"/> over the game (spec/hud/in-game-menu.md): the "In-Game
/// Menu" page with its buttons where the original puts them (centred, half the screen wide, at their fractions of
/// the height), the quit and lobby confirmations in their dialog box, the leaving page, the options screen
/// (<see cref="OptionsMenu"/> as an overlay) for Options, and the strategic map (<see cref="StrategicMapScreen"/>).
/// The original's GUI pages are drawn in the front end's style. Clicks go to the model; what the play scene must do
/// is raised through <see cref="Acted"/>. While shown it is in the group <see cref="Group"/>.
/// </summary>
public partial class InGameMenu : Control
{
    /// <summary>The group a visible pause menu is in (docs/TESTING-PLAY.md).</summary>
    public const string Group = "svm_pause_menu";

    private const float ButtonHeight = 44f;

    private InGameMenuPage _shown = (InGameMenuPage)(-1);
    private Vector2 _shownSize;
    private OptionsMenu? _options;

    public InGameMenu()
    {
        Name = "InGameMenu";
    }

    public InGameMenuModel Model { get; set; } = new();

    /// <summary>The clock the model's times are read on (seconds).</summary>
    public Func<double> Now { get; set; } = () => Time.GetTicksMsec() / 1000.0;

    /// <summary>The gear picker for the player's side now (null before he has one).</summary>
    public Func<GearMenuModel?>? GearSource { get; set; }

    /// <summary>The strategic map page for the map being played (spec/hud/strategic-map.md), made when the page opens.</summary>
    public Func<StrategicMapScreen>? StrategicMapSource { get; set; }

    /// <summary>Apply on the gear picker: save the choice and ask the server to put it on.</summary>
    public event Action<GearMenuModel>? GearApplied;

    /// <summary>Raised with what a click asks the play scene to do (never <see cref="InGameMenuAction.None"/>).</summary>
    public event Action<InGameMenuAction>? Acted;

    /// <summary>The options screen while Options is open, else null.</summary>
    public OptionsMenu? Options => _options;

    public override void _Ready()
    {
        Theme = UiStyle.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddToGroup(Group);
        Sync();
    }

    public override void _Process(double delta) => Sync();

    /// <summary>Rebuilds the page when the model's page or the screen size changed.</summary>
    public void Sync()
    {
        Vector2 size = GetViewportRect().Size;
        InGameMenuPage page = Model.Page;
        Visible = page != InGameMenuPage.Closed;
        if (page == _shown && size == _shownSize)
        {
            return;
        }

        _shown = page;
        _shownSize = size;
        Build(page, size);
    }

    private void Build(InGameMenuPage page, Vector2 size)
    {
        foreach (Node child in GetChildren())
        {
            if (ReferenceEquals(child, _options))
            {
                continue;
            }

            RemoveChild(child);
            child.QueueFree();
        }

        if (page != InGameMenuPage.Options && _options is not null)
        {
            RemoveChild(_options);
            _options.QueueFree();
            _options = null;
        }

        switch (page)
        {
            case InGameMenuPage.Main:
                Dim(0.6f);
                MainPage(size);
                break;
            case InGameMenuPage.ConfirmQuit:
                Dim(0.6f);
                Dialog(size, quit: true);
                break;
            case InGameMenuPage.ConfirmLobby:
                Dim(0.6f);
                Dialog(size, quit: false);
                break;
            case InGameMenuPage.Gear:
                GearPage();
                break;
            case InGameMenuPage.Leaving:
                Dim(0.85f);
                Text(InGameMenuK.LeavingText, new Rect2(0f, size.Y * 0.45f, size.X, 40f));
                break;
            case InGameMenuPage.StrategicMap:
                StrategicMapPage();
                break;
            case InGameMenuPage.Options when _options is null:
                _options = new OptionsMenu { Overlay = true };
                _options.Closed += OnOptionsClosed;
                AddChild(_options);
                break;
        }
    }

    private void Dim(float alpha)
    {
        var dim = new ColorRect { Name = "Dim", Color = new Color(0f, 0f, 0f, alpha), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
    }

    private void MainPage(Vector2 size)
    {
        Label title = UiStyle.Title(InGameMenuK.Title, HudK.SpyColour, 36);
        title.Name = "Title";
        title.Position = new Vector2(0f, size.Y * 0.12f);
        title.Size = new Vector2(size.X, 48f);
        AddChild(title);

        float width = size.X * InGameMenuK.ButtonWidth;
        Button? first = null;
        foreach (InGameMenuEntry e in Model.Entries)
        {
            InGameMenuButton which = e.Button;
            Button b = UiStyle.MenuButton(e.Caption, () => Click(which), which.ToString());
            b.Position = new Vector2((size.X - width) * 0.5f, (size.Y * e.Top) - (ButtonHeight * 0.5f));
            b.Size = new Vector2(width, ButtonHeight);
            AddChild(b);
            first ??= b;
        }

        // The keyboard can move between the buttons as the original's pad and arrow keys do.
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Dialog(Vector2 size, bool quit)
    {
        // The original's boxes and rows, laid out symmetrically: its two buttons' WinLeft (0.35, 0.65) are taken as
        // centres, so they sit evenly about the box's middle, and Quit Game is centred under the line with the
        // box's bottom margin below it (at the original 0.65 it sat on the box's bottom edge). The question fills the
        // box above the buttons.
        var box = new Panel
        {
            Name = "Box",
            Position = new Vector2(size.X * InGameMenuK.DialogLeft, size.Y * InGameMenuK.DialogTop),
            Size = new Vector2(size.X * InGameMenuK.DialogWidth, size.Y * InGameMenuK.DialogHeight),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        AddChild(box);
        Vector2 buttonSize = new(Math.Max(size.X * InGameMenuK.DialogButtonWidth, 170f), Math.Max(size.Y * InGameMenuK.DialogButtonHeight, 36f));
        // The lobby dialog's row (0.65 in the original) is raised to keep a margin under it, as the quit dialog has.
        float row = size.Y * (quit ? InGameMenuK.QuitDialogRow : InGameMenuK.DialogRow - 0.04f);
        float questionBottom = row - size.Y * 0.02f;
        string question = (Model.Question ?? "").Replace('|', '\n');
        Text(question, new Rect2(box.Position, new Vector2(box.Size.X, questionBottom - box.Position.Y)));

        string yes = quit ? InGameMenuK.EndSession : InGameMenuK.LobbyYes;
        Button yesButton = DialogButton(yes, size.X * InGameMenuK.DialogYesLeft, row, buttonSize,
            quit ? () => Act(Model.EndSession(Now())) : () => Act(Model.ConfirmLobby()));
        Button cancel = DialogButton(InGameMenuK.Cancel, size.X * InGameMenuK.DialogNoLeft, row, buttonSize, () =>
        {
            Model.Cancel();
            Sync();
        });
        if (quit)
        {
            float boxBottom = box.Position.Y + box.Size.Y;
            float lineY = row + buttonSize.Y + (boxBottom - row - 2f * buttonSize.Y) / 3f;
            float inset = box.Size.X * 0.08f;
            var line = new ColorRect
            {
                Name = "Line",
                Color = UiStyle.Dim,
                Position = new Vector2(box.Position.X + inset, lineY),
                Size = new Vector2(box.Size.X - 2f * inset, Math.Max(1f, size.Y * 0.002f)),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(line);
            float quitTop = lineY + (boxBottom - row - 2f * buttonSize.Y) / 3f;
            Button quitButton = DialogButton(InGameMenuK.QuitGame, box.Position.X + box.Size.X * 0.5f, quitTop, buttonSize, () => Act(Model.QuitGame()));
            cancel.FocusNeighborBottom = quitButton.GetPath();
            yesButton.FocusNeighborBottom = quitButton.GetPath();
            quitButton.FocusNeighborTop = yesButton.GetPath();
        }

        yesButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A dialog button of <paramref name="buttonSize"/> centred on <paramref name="centreX"/>, its top at <paramref name="top"/> (pixels).</summary>
    private Button DialogButton(string text, float centreX, float top, Vector2 buttonSize, Action pressed)
    {
        var b = new Button { Name = text.Replace(" ", "", StringComparison.Ordinal), Text = text };
        b.Pressed += pressed;
        b.Position = new Vector2(centreX - buttonSize.X * 0.5f, top);
        b.Size = buttonSize;
        AddChild(b);
        return b;
    }

    private void Text(string text, Rect2 rect)
    {
        var label = new Label
        {
            Name = "Question",
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Position = rect.Position,
            Size = rect.Size,
            Modulate = UiStyle.Text,
        };
        AddChild(label);
    }

    private void GearPage()
    {
        var gear = new GearMenu(GearSource?.Invoke());
        gear.Applied += m =>
        {
            GearApplied?.Invoke(m);
            Act(Model.GearApplied());
        };
        gear.Closed += () => Act(Model.GearBack());
        AddChild(gear);
    }

    private void StrategicMapPage()
    {
        StrategicMapScreen map = StrategicMapSource?.Invoke() ?? new StrategicMapScreen(StrategicMapData.Empty);
        map.Closed += () => Act(Model.StrategicMapBack());
        AddChild(map);
    }

    private void Click(InGameMenuButton button) => Act(Model.Click(button));

    private void OnOptionsClosed()
    {
        Model.OptionsClosed();
        Sync();
    }

    private void Act(InGameMenuAction action)
    {
        Sync();
        if (action != InGameMenuAction.None)
        {
            Acted?.Invoke(action);
        }
    }
}
