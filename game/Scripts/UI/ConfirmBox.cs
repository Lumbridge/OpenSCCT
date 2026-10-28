using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// A question over the current page with two buttons, as the original's GUIYesNoPage draws it (a framed box in the
/// middle of the screen, the question, then the No and Yes buttons). Used by the main menu's quit confirmation
/// (GUI.ConfirmQuitPage) and the options' "keep these settings?" countdown (GUI.Validate_VideoSettings). Escape
/// answers No. The box frees itself once answered.
/// </summary>
public partial class ConfirmBox : Control
{
    private Label? _text;
    private bool _answered;

    public ConfirmBox()
    {
        Name = "ConfirmBox";
    }

    public string Question { get; init; } = string.Empty;

    public string YesCaption { get; init; } = "Yes";

    public string NoCaption { get; init; } = "No";

    /// <summary>Runs when Yes is pressed.</summary>
    public Action? Yes { get; init; }

    /// <summary>Runs when No (or Escape) is pressed.</summary>
    public Action? No { get; init; }

    public Button? YesButton { get; private set; }

    public Button? NoButton { get; private set; }

    /// <summary>Opens <paramref name="box"/> over <paramref name="parent"/>; null when a box of that name is already open there.</summary>
    public static ConfirmBox? Open(Node parent, ConfirmBox box)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(box);
        if (parent.GetNodeOrNull(box.Name.ToString()) is not null)
        {
            box.QueueFree();
            return null;
        }

        parent.AddChild(box);
        return box;
    }

    /// <summary>Changes the question (the countdown's seconds).</summary>
    public void SetQuestion(string text)
    {
        if (_text is not null)
        {
            _text.Text = text;
        }
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = UiStyle.MenuTheme;

        // A dim layer over the page keeps it from taking clicks while the question is open.
        var dim = new ColorRect { Name = "Dim", Color = new Color(0f, 0f, 0f, 0.55f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        // GUIYesNoPage's BckGround: WinLeft 0.2, WinTop 0.3, WinWidth 0.6, WinHeight 0.4.
        var panel = new PanelContainer { Name = "Box", AnchorLeft = 0.2f, AnchorRight = 0.8f, AnchorTop = 0.3f, AnchorBottom = 0.7f };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        AddChild(panel);
        var column = new VBoxContainer { Name = "Column", Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 24);
        _text = new Label
        {
            Name = "Text", Text = Question, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = UiStyle.MenuFocus,
        };
        column.AddChild(_text);
        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 48);
        NoButton = UiStyle.MenuButton(NoCaption, () => Answer(false), "No");
        YesButton = UiStyle.MenuButton(YesCaption, () => Answer(true), "Yes");
        NoButton.CustomMinimumSize = YesButton.CustomMinimumSize = new Vector2(180f, 44f);
        buttons.AddChild(NoButton);
        buttons.AddChild(YesButton);
        column.AddChild(buttons);
        panel.AddChild(column);
        NoButton.GrabFocus();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Answer(false);
        }
    }

    /// <summary>Answers the box as if a button was pressed (the countdown's end answers No).</summary>
    public void Answer(bool yes)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        QueueFree();
        (yes ? Yes : No)?.Invoke();
    }
}
