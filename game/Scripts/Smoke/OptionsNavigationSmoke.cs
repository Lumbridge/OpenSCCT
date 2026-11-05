using System.Linq;
using Godot;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Real arrow-key traversal must reach rows below the viewport before leaving for the footer.</summary>
public sealed class OptionsNavigationSmoke : SmokeTest
{
    public const string TestName = "options-navigation";
    public override string Name => TestName;
    private OptionsMenu? _menu;
    private ScrollContainer? _scroll;
    private Control[][] _rows = [];
    private int _page, _frames, _row;
    private bool _started;

    public override SmokeResult Step(SmokeContext context)
    {
        if (_menu is null)
        {
            _menu = (OptionsMenu)FrontEnd.ShowOptions(context.Tree);
            context.Tree.Root.Size = new Vector2I(800, 600);
            context.Tree.Root.ContentScaleSize = new Vector2I(800, 600);
        }
        if (++_frames == 1)
        {
            _menu.Tabs!.CurrentTab = _page == 0 ? 0 : 2;
            if (_page == 0)
            {
                var sections = _menu.Tabs.GetCurrentTabControl().GetNode<TabContainer>("Sections");
                sections.CurrentTab = 0;
                var category = (OptionButton)sections.GetCurrentTabControl().FindChild("BindingCategory", true, false);
                category.Select(0);
                category.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            }
            _started = false;
        }
        if (_frames < 8 || _frames % 3 != 0) return SmokeResult.Running;
        if (!_started)
        {
            _scroll = _menu.FindChildren(_page == 0 ? "ActionList" : "SettingsList", nameof(ScrollContainer), true, false)
                .OfType<ScrollContainer>().First(list => list.IsVisibleInTree());
            Control rows = _scroll.GetChild<Control>(0);
            _rows = rows.GetChildren().OfType<Control>().Select(row => row.FindChildren("*", nameof(Control), true, false)
                .OfType<Control>().Where(control => control.IsVisibleInTree() && control.FocusMode == Control.FocusModeEnum.All &&
                    control is not BaseButton { Disabled: true }).ToArray()).Where(row => row.Length > 0).ToArray();
            if (_rows.Length < 5) return SmokeResult.Fail("navigation fixture needs at least five settings rows");
            _rows[0][0].GrabFocus();
            _row = 0;
            _started = true;
            if (_page == 0)
            {
                var behavior = _menu.FindChildren("BindingBehavior_speed", nameof(OptionButton), true, false)
                    .OfType<OptionButton>().FirstOrDefault(choice => choice.IsVisibleInTree());
                if (behavior?.GetParent().Name != "BindingRow_speed") return SmokeResult.Fail("walk behavior is not inline with its binding");
                string previous = _menu.Editing.SpyKbm.WalkBehavior;
                behavior.Select(previous == "Hold" ? 1 : 0);
                behavior.EmitSignal(OptionButton.SignalName.ItemSelected, (long)behavior.Selected);
                if (_menu.Editing.SpyKbm.WalkBehavior == previous) return SmokeResult.Fail("inline behavior does not edit the draft");
            }
        }
        Control? focused = context.Tree.Root.GuiGetFocusOwner();
        if (!_rows[_row].Contains(focused)) return SmokeResult.Fail($"page {_page} arrow skipped row {_row} for {focused?.Name}");
        if (_row == _rows.Length - 1)
        {
            if (_scroll!.ScrollVertical <= 0) return SmokeResult.Fail($"page {_page} did not scroll to its last row");
            if (focused is null || !_scroll.GetGlobalRect().Grow(2).Intersects(focused.GetGlobalRect()))
                return SmokeResult.Fail($"page {_page} focused last row remains clipped");
            if (++_page == 2) return SmokeResult.Pass("binding and settings arrows traverse clipped rows; behavior edits inline");
            _frames = 0;
            return SmokeResult.Running;
        }
        // Use Godot's ordinary focus input path, rather than following our neighbor properties in the test.
        context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.Down, PhysicalKeycode = Key.Down, Pressed = true });
        context.Tree.Root.PushInput(new InputEventKey { Keycode = Key.Down, PhysicalKeycode = Key.Down, Pressed = false });
        _row++;
        return SmokeResult.Running;
    }
}
