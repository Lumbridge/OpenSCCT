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

    /// <param name="compact">The lobby's layout (<see cref="BuildCompact"/>) instead of the page's three rule boxes.</param>
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
        if (_compact)
        {
            BuildCompact(rows);
            Refresh();
            return;
        }

        var top = new VBoxContainer { Name = "MapMode" };
        var session = new VBoxContainer { Name = "Session" };
        var boxes = new HBoxContainer { Name = "RuleBoxes" };
        boxes.AddThemeConstantOverride("separation", 12);
        AddChild(top);
        AddChild(session);
        AddChild(boxes);
        VBoxContainer left = Box(boxes, "BoxLeft");
        VBoxContainer mid = Box(boxes, "BoxMid");
        VBoxContainer right = Box(boxes, "BoxRight");
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
                SessionRow.Revenge or SessionRow.RandomMap or SessionRow.VoiceInLobby or SessionRow.AutoBalance or SessionRow.Alternate or
                    SessionRow.GamesInARow or SessionRow.Practice => left,
                SessionRow.TimeLimit or SessionRow.SpyLives or SessionRow.MercLives => mid,
                _ => right,
            };
            parent.AddChild(MakeRow(row));
            if (row == SessionRow.VoiceChat)
            {
                _hint = new Label { Name = "VoiceHint", Modulate = UiStyle.MenuFocus, HorizontalAlignment = HorizontalAlignment.Right };
                parent.AddChild(_hint);
            }
        }

        Refresh();
    }

    /// <summary>
    /// The lobby's layout: Map and Mode as two left-aligned lines, then three columns grouped by what they set (the
    /// match's length and lives; voice and bots; rule switches), every value in a cell of one width so the columns
    /// line up. A check box row has no Yes/No beside the box, and the voice hint is the Voice Chat row's tooltip.
    /// </summary>
    private void BuildCompact(IReadOnlyCollection<SessionRow> rows)
    {
        var top = new VBoxContainer { Name = "MapMode" };
        top.AddThemeConstantOverride("separation", 2);
        AddChild(top);
        AddChild(new Control { Name = "Gap", CustomMinimumSize = new Vector2(0f, 6f) });
        var columns = new HBoxContainer { Name = "Columns" };
        columns.AddThemeConstantOverride("separation", 32);
        AddChild(columns);
        VBoxContainer match = CompactColumn(columns, "Match");
        VBoxContainer players = CompactColumn(columns, "Players");
        VBoxContainer rules = CompactColumn(columns, "Rules");
        foreach (SessionRow row in CreateSessionModel.Rows)
        {
            if (!Contains(rows, row) || row is SessionRow.Password or SessionRow.MaxPlayers)
            {
                continue;
            }

            VBoxContainer parent = row switch
            {
                SessionRow.Map or SessionRow.Mode => top,
                SessionRow.TimeLimit or SessionRow.Respawn or SessionRow.SpyLives or SessionRow.MercLives or SessionRow.GamesInARow or
                    SessionRow.ActiveDisks or SessionRow.DisksToTake => match,
                SessionRow.VoiceChat or SessionRow.VoiceInLobby or SessionRow.Bots or SessionRow.BotsPerSide or SessionRow.BotSkill or
                    SessionRow.AutoBalance => players,
                _ => rules,
            };
            parent.AddChild(MakeRow(row));
        }

        // Within a column: lengths before lives, voice before bots.
        Order(match, SessionRow.TimeLimit, SessionRow.Respawn, SessionRow.SpyLives, SessionRow.MercLives, SessionRow.GamesInARow,
            SessionRow.ActiveDisks, SessionRow.DisksToTake);
        Order(players, SessionRow.VoiceChat, SessionRow.VoiceInLobby, SessionRow.Bots, SessionRow.BotsPerSide, SessionRow.BotSkill,
            SessionRow.AutoBalance);
    }

    private static VBoxContainer CompactColumn(HBoxContainer parent, string name)
    {
        var column = new VBoxContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 2);
        parent.AddChild(column);
        return column;
    }

    private void Order(VBoxContainer column, params SessionRow[] order)
    {
        int at = 0;
        foreach (SessionRow row in order)
        {
            if (_rows.TryGetValue(row, out Control? line) && line.GetParent() == column)
            {
                column.MoveChild(line, at++);
            }
        }
    }

    /// <summary>The fixed width of a compact row's value cell (arrows and value, or the check box).</summary>
    private const float CompactCell = 124f;

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
        bool wide = row is SessionRow.Map or SessionRow.Mode;
        var caption = new Label
        {
            Name = row + "Caption", Text = Model.Caption(row), TooltipText = Model.Caption(row), MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = _compact && wide ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(_compact ? (wide ? 140f : 0f) : 150f, 0f),
        };
        line.AddChild(caption);
        _captions[row] = caption;

        // Compact rows put their control in a cell of one width, so values line up down a column.
        Container cell = line;
        if (_compact && !wide)
        {
            var box = new HBoxContainer
            {
                Name = row + "Cell", CustomMinimumSize = new Vector2(CompactCell, 0f), Alignment = BoxContainer.AlignmentMode.Center,
            };
            line.AddChild(box);
            cell = box;
        }

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
            cell.AddChild(check);
            _checks[row] = check;
            if (!_compact)
            {
                var value = new Label { Name = row + "Value", CustomMinimumSize = new Vector2(40f, 0f) };
                cell.AddChild(value);
                _values[row] = value;
            }
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
            cell.AddChild(_password);
        }
        else
        {
            cell.AddChild(Arrow(row, "Prev", "◄", -1));
            var value = new Label
            {
                Name = row + "Value", HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsHorizontal = _compact && !wide ? SizeFlags.ExpandFill : SizeFlags.Fill,
                CustomMinimumSize = new Vector2(
                    _compact ? (wide ? 280f : 0f) : row is SessionRow.Map ? 300f : row is SessionRow.Mode or SessionRow.VoiceChat ? 170f : 44f, 0f),
                // Sized to its text (an overrun behaviour would let the row shrink it to "ENHANCED STEEL SQU...").
            };
            cell.AddChild(value);
            _values[row] = value;
            cell.AddChild(Arrow(row, "Next", "►", 1));
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
        else if (_compact && _captions.TryGetValue(SessionRow.VoiceChat, out Label? voice))
        {
            voice.TooltipText = Model.Caption(SessionRow.VoiceChat) + ": " + Model.VoiceHint;
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
