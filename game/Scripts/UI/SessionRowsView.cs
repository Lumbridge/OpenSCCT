using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Server.Online;

namespace OpenScctGame.UI;

/// <summary>
/// The Create Session page's rows drawn from a <see cref="CreateSessionModel"/> (docs/MENUS.md): Map / Mode, the
/// session rows (Password, Voice Chat with its hint, Max Players) and the three rule boxes the LAN page lays out at
/// its foot (Revenge, Random Map, Voice in Lobby, Games in a Row; Game Length, Spy Lives, Mercenary Lives; Respawn
/// Length, Active Disks, Disks to Take). A list or number row has the original's arrows either side of its value; a
/// check box row toggles. Read only, it is the clients' Session Parameters page; a changed value blinks for three
/// seconds (<see cref="LobbyRoomModel"/>). Node names: <c>Row_&lt;row&gt;</c>, <c>&lt;row&gt;Prev</c>,
/// <c>&lt;row&gt;Next</c>, <c>&lt;row&gt;Check</c>, <c>&lt;row&gt;Value</c>, <c>PasswordEdit</c>, <c>VoiceHint</c>.
/// </summary>
public partial class SessionRowsView : VBoxContainer
{
    private readonly Dictionary<SessionRow, Control> _rows = new();
    private readonly Dictionary<SessionRow, Label> _values = new();
    private readonly Dictionary<SessionRow, Label> _captions = new();
    private readonly Dictionary<SessionRow, CheckBox> _checks = new();
    private readonly List<BaseButton> _arrows = new();
    private Label? _hint;
    private LineEdit? _password;
    private bool _editable = true;

    /// <param name="compact">Two columns of rows instead of the page's three rule boxes (the lobby's narrower panel).</param>
    public SessionRowsView(CreateSessionModel model, IReadOnlyCollection<SessionRow> rows, bool showPassword, bool compact = false)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Name = "SessionRows";
        _compact = compact;
        Build(rows, showPassword);
        model.Changed += Refresh;
    }

    private readonly bool _compact;

    public CreateSessionModel Model { get; }

    /// <summary>Blinking of values the host changed (clients), or null.</summary>
    public LobbyRoomModel? Blink { get; set; }

    /// <summary>The host edits; others read.</summary>
    public bool Editable
    {
        get => _editable;
        set
        {
            _editable = value;
            foreach (BaseButton b in _arrows)
            {
                b.Visible = value;
            }

            foreach (CheckBox c in _checks.Values)
            {
                c.Disabled = !value;
            }

            if (_password is not null)
            {
                _password.Editable = value;
            }
        }
    }

    public override void _ExitTree() => Model.Changed -= Refresh;

    private void Build(IReadOnlyCollection<SessionRow> rows, bool showPassword)
    {
        AddThemeConstantOverride("separation", 4);
        var top = new VBoxContainer { Name = "MapMode" };
        var session = new VBoxContainer { Name = "Session" };
        var boxes = new HBoxContainer { Name = "RuleBoxes" };
        boxes.AddThemeConstantOverride("separation", 12);
        AddChild(top);
        AddChild(session);
        AddChild(boxes);
        VBoxContainer left, mid, right;
        GridContainer? grid = null;
        if (_compact)
        {
            grid = new GridContainer { Name = "Compact", Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", 12);
            boxes.AddChild(grid);
            left = mid = right = session;
        }
        else
        {
            left = Box(boxes, "BoxLeft");
            mid = Box(boxes, "BoxMid");
            right = Box(boxes, "BoxRight");
        }

        foreach (SessionRow row in CreateSessionModel.Rows)
        {
            if (!Contains(rows, row) || (row == SessionRow.Password && !showPassword))
            {
                continue;
            }

            VBoxContainer parent = row switch
            {
                SessionRow.Map or SessionRow.Mode => top,
                SessionRow.Password or SessionRow.VoiceChat or SessionRow.MaxPlayers => session,
                SessionRow.Revenge or SessionRow.RandomMap or SessionRow.VoiceInLobby or SessionRow.GamesInARow => left,
                SessionRow.TimeLimit or SessionRow.SpyLives or SessionRow.MercLives => mid,
                _ => right,
            };
            Control made = MakeRow(row);
            if (grid is not null && row is not (SessionRow.Map or SessionRow.Mode))
            {
                grid.AddChild(made);
            }
            else
            {
                parent.AddChild(made);
            }

            if (row == SessionRow.VoiceChat)
            {
                _hint = new Label { Name = "VoiceHint", Modulate = UiStyle.MenuFocus, HorizontalAlignment = HorizontalAlignment.Right };
                parent.AddChild(_hint);
            }
        }

        Refresh();
    }

    private static bool Contains(IReadOnlyCollection<SessionRow> rows, SessionRow row)
    {
        foreach (SessionRow r in rows)
        {
            if (r == row)
            {
                return true;
            }
        }

        return false;
    }

    private static VBoxContainer Box(HBoxContainer parent, string name)
    {
        var frame = new PanelContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        parent.AddChild(frame);
        var column = new VBoxContainer { Name = "Column" };
        frame.AddChild(column);
        return column;
    }

    private Control MakeRow(SessionRow row)
    {
        var line = new HBoxContainer { Name = "Row_" + row };
        var caption = new Label
        {
            Name = row + "Caption", Text = Model.Caption(row), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(_compact ? 0f : 150f, 0f), TooltipText = Model.Caption(row),
        };
        line.AddChild(caption);
        _captions[row] = caption;
        if (CreateSessionModel.IsCheck(row))
        {
            var check = new CheckBox { Name = row + "Check" };
            check.Toggled += on =>
            {
                if (_editable)
                {
                    Model.SetChecked(row, on);
                }
            };
            line.AddChild(check);
            _checks[row] = check;
            var value = new Label { Name = row + "Value", CustomMinimumSize = new Vector2(40f, 0f) };
            line.AddChild(value);
            _values[row] = value;
        }
        else if (row == SessionRow.Password)
        {
            _password = new LineEdit
            {
                Name = "PasswordEdit", Secret = true, MaxLength = LobbySetup.MaxPasswordLength, PlaceholderText = "none",
                CustomMinimumSize = new Vector2(220f, 0f),
            };
            _password.TextChanged += text =>
            {
                string clean = CreateSessionModel.FilterPassword(text);
                if (clean != text)
                {
                    int caret = _password.CaretColumn;
                    _password.Text = clean;
                    _password.CaretColumn = Math.Min(caret, clean.Length);
                }

                Model.SetPassword(clean);
            };
            line.AddChild(_password);
        }
        else
        {
            line.AddChild(Arrow(row, "Prev", "◄", -1));
            var value = new Label
            {
                Name = row + "Value", HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(row is SessionRow.Map ? (_compact ? 260f : 300f) : row is SessionRow.Mode or SessionRow.VoiceChat ? (_compact ? 90f : 170f) : (_compact ? 36f : 44f), 0f),
                // Sized to its text (an overrun behaviour would let the row shrink it to "ENHANCED STEEL SQU...").
            };
            line.AddChild(value);
            _values[row] = value;
            line.AddChild(Arrow(row, "Next", "►", 1));
        }

        _rows[row] = line;
        return line;
    }

    private Button Arrow(SessionRow row, string which, string glyph, int delta)
    {
        var b = new Button { Name = row + which, Text = glyph, CustomMinimumSize = new Vector2(_compact ? 26f : 40f, 28f), FocusMode = FocusModeEnum.All };
        b.Pressed += () =>
        {
            if (_editable)
            {
                Model.Step(row, delta);
            }
        };
        _arrows.Add(b);
        return b;
    }

    /// <summary>Redraws the values, captions and which rows show.</summary>
    public void Refresh()
    {
        foreach ((SessionRow row, Control line) in _rows)
        {
            line.Visible = Model.Visible(row);
            _captions[row].Text = Model.Caption(row);
            if (_values.TryGetValue(row, out Label? value))
            {
                value.Text = Model.Text(row);
            }

            if (_checks.TryGetValue(row, out CheckBox? check))
            {
                check.SetPressedNoSignal(Model.Checked(row));
            }
        }

        if (_hint is not null)
        {
            _hint.Text = Model.VoiceHint;
        }
    }

    public override void _Process(double delta)
    {
        if (Blink is null)
        {
            return;
        }

        double now = Time.GetTicksMsec() / 1000.0;
        foreach ((SessionRow row, Control line) in _rows)
        {
            float a = Blink.Lit(row, now) ? 1f : 0.15f;
            if (!Mathf.IsEqualApprox(line.Modulate.A, a))
            {
                line.Modulate = new Color(1f, 1f, 1f, a);
            }
        }
    }

    /// <summary>The control of a row's arrow or check (tests and the menu pilot).</summary>
    public Control? Find(string name) => FindChild(name, recursive: true, owned: false) as Control;
}
