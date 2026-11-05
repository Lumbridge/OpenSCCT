using System.IO;
using System.Linq;
using Godot;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Renders and drives the live match resignation dialog at compact and wide viewport sizes.</summary>
public sealed class ResignMenuSmoke : SmokeTest
{
    public const string TestName = "resign-menu";
    public override string Name => TestName;
    private SubViewport? _viewport;
    private InGameMenu? _menu;
    private int _phase, _frames, _size, _resigns;
    private int _teardown;
    private static readonly Vector2I[] Sizes = [new(800, 600), new(960, 540), new(1280, 720)];
    private static readonly InGameMenuContext Eligible = new(true, InGameMenuNet.Lan, MatchBegun: true, Lives: 3, Practice: true);

    public override SmokeResult Step(SmokeContext context)
    {
        if (_size == Sizes.Length)
            return ++_teardown < 5 ? SmokeResult.Running : SmokeResult.Pass("800x600 + 960x540 + 1280x720: practice team button, separate top-right help, quit last with safe focus navigation, Cancel focus, cancellation, stale eligibility and one-shot confirmation");
        if (_viewport is null)
        {
            _viewport = new SubViewport { Size = Sizes[_size], RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            context.Tree.Root.AddChild(_viewport);
            _menu = new InGameMenu { Now = () => 2 };
            _menu.Model.Open(Eligible, 0);
            _menu.Acted += action => { if (action == InGameMenuAction.Resign) _resigns++; };
            _viewport.AddChild(_menu);
        }
        if (++_frames < 5) return SmokeResult.Running;
        _frames = 0;
        Button? Find(string text) => _menu!.GetChildren().OfType<Button>().FirstOrDefault(b => b.Text == text);
        if (_phase == 0)
        {
            foreach (Button button in _menu!.GetChildren().OfType<Button>())
                if (!new Rect2(Vector2.Zero, Sizes[_size]).Encloses(button.GetRect())) return SmokeResult.Fail("main menu button outside viewport");
            var panel = _menu.GetNode<Panel>("ButtonPanel");
            if (_menu.HelpButton is not { } help || panel.GetRect().Intersects(help.GetRect()) ||
                help.GetRect().End.Y + 24f > panel.Position.Y || !help.Flat)
                return SmokeResult.Fail("Help & feedback must be a separate utility above the session panel");
            Button[] primary = _menu.GetChildren().OfType<Button>().Where(b => b != help).OrderBy(b => b.Position.Y).ToArray();
            if (primary[^1].Name != nameof(InGameMenuButton.QuitSession) ||
                primary[^1].FocusNeighborBottom != primary[0].GetPath() ||
                primary[0].FocusNeighborTop != primary[^1].GetPath() || primary[0].FocusNeighborRight != help.GetPath())
                return SmokeResult.Fail("Quit Session must remain last, with up/down inside the primary actions and Right reaching Help");
            void KeyInput(Key key)
            {
                _viewport.PushInput(new InputEventKey { Keycode = key, Pressed = true });
                _viewport.PushInput(new InputEventKey { Keycode = key, Pressed = false });
            }
            primary[^1].GrabFocus();
            KeyInput(Key.Down);
            if (_viewport.GuiGetFocusOwner() != primary[0]) return SmokeResult.Fail("Down from Quit Session did not wrap to Back");
            KeyInput(Key.Right);
            if (_viewport.GuiGetFocusOwner() != help) return SmokeResult.Fail("Right did not reach the separate Help utility");
            KeyInput(Key.Left);
            if (_viewport.GuiGetFocusOwner() != primary[0]) return SmokeResult.Fail("Left from Help did not return to session actions");
            if (Find("Switch Team") is null) return SmokeResult.Fail("practice menu has no Switch Team button");
            if (Find(InGameMenuK.Resign) is not { } resign) return SmokeResult.Fail("eligible player has no Resign button");
            Shot("main");
            resign.EmitSignal(Button.SignalName.Pressed);
        }
        else if (_phase == 1)
        {
            if (_menu!.Model.Page != InGameMenuPage.ConfirmResign || Find(InGameMenuK.Cancel) is not { } cancel ||
                _viewport.GuiGetFocusOwner() != cancel) return SmokeResult.Fail("confirmation did not focus Cancel");
            Shot("confirm");
            cancel.EmitSignal(Button.SignalName.Pressed);
        }
        else if (_phase == 2)
        {
            if (_menu!.Model.Page != InGameMenuPage.Main || _resigns != _size) return SmokeResult.Fail("Cancel resigned or failed to return");
            Find(InGameMenuK.Resign)!.EmitSignal(Button.SignalName.Pressed);
            _menu.Model.UpdateContext(Eligible with { Lives = 0 });
            _menu.Sync();
        }
        else if (_phase == 3)
        {
            if (_menu!.Model.Page != InGameMenuPage.Main || Find(InGameMenuK.Resign) is not null) return SmokeResult.Fail("stale resignation stayed available");
            _menu.Model.UpdateContext(Eligible);
            _menu.Sync();
            Find(InGameMenuK.Resign)!.EmitSignal(Button.SignalName.Pressed);
        }
        else
        {
            Find(InGameMenuK.Resign)!.EmitSignal(Button.SignalName.Pressed);
            if (_menu!.Model.IsOpen || _resigns != _size + 1) return SmokeResult.Fail("confirmed resignation was not one-shot");
            _viewport.QueueFree();
            _viewport = null;
            _phase = -1;
            _size++;
        }
        _phase++;
        return SmokeResult.Running;
    }

    private void Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        string output = Path.Combine(RenderSmokeArgs.RepoRoot, "local", "b5-fixes", "content", "resign-menu");
        Directory.CreateDirectory(output);
        using Image image = _viewport!.GetTexture().GetImage();
        image.SavePng(Path.Combine(output, $"{Sizes[_size].X}-{name}.png"));
    }
}
