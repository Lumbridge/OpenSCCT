using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

public sealed class JoinFocusSmoke : SmokeTest
{
    public const string TestName = "join-focus";
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        void Send(InputEvent input)
        {
            BindingInput.Current?.Resolve(input);
            context.Tree.Root.PushInput(input);
        }
        Pointer.Detached = true;
        Pointer.Release();
        if (context.Tree.CurrentScene is { } previous)
        {
            context.Tree.Root.RemoveChild(previous);
            previous.QueueFree();
        }
        var menu = new LineEdit { MouseFilter = Control.MouseFilterEnum.Ignore };
        context.Tree.Root.AddChild(menu);
        menu.GrabFocus();
        var local = new LocalInput();
        var input = new PlayInput
        {
            Target = local,
            Bindings = InputBindings.Load(PlaySide.Merc, seedInputMap: false, defaults: true),
        };
        context.Tree.Root.AddChild(input);
        input._Process(0);
        input._Process(0);
        bool captured = input.IsCaptured && context.Tree.Root.GuiGetFocusOwner() is null;
        Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        Send(new InputEventMouseMotion { Relative = new Vector2(12, -8) });
        input.FillTick(local);
        bool delivered = local.Get("BT_Up") == 1 && local.MouseX == 12 && local.MouseY == 8;
        string received = $"move={local.Get("BT_Up")} mouse={local.MouseX},{local.MouseY}";
        input._Notification((int)Node.NotificationWMWindowFocusOut);
        bool cleared = local.Get("BT_Up") == 0 && input.PendingMouse == (0f, 0f) && !input.IsCaptured;
        Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = false });
        input._Notification((int)Node.NotificationWMWindowFocusIn);
        input._Process(0);
        bool deferred = !input.IsCaptured;
        input._Process(0);
        bool restored = input.IsCaptured;
        input.ReleasePointer();
        input._Process(0);
        bool released = !input.IsCaptured;
        // The click arrives before the next process frame after Windows reports focus-in.
        input._Notification((int)Node.NotificationWMWindowFocusIn);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        input._Process(0);
        input._Process(0);
        Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        Send(new InputEventMouseMotion { Relative = new Vector2(7, -3) });
        input.FillTick(local);
        bool clickRestored = input.IsCaptured && local.Get("BT_Up") == 1 && local.MouseX == 7 && local.MouseY == 3;
        bool safeClick = !local.Commands.Exists(c => c.Name is "Fire" or "GadgetQuickChoice") && local.Get("bFire") == 0;
        local.EndTick();
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        input.FillTick(local);
        bool nextClickFires = local.Commands.Exists(c => c.Name == "Fire");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = false });
        // Windows changes the foreground without Godot delivering a focus-out notification.
        bool foreground = false;
        input.ForegroundProbe = () => foreground;
        input._Process(0);
        bool backgroundReleased = !input.IsCaptured;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        input._Process(0);
        input._Process(0);
        bool waitsForForeground = !input.IsCaptured;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        foreground = true;
        input._Notification((int)Node.NotificationWMWindowFocusIn);
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        input._Process(0);
        input._Process(0);
        Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        Send(new InputEventMouseMotion { Relative = new Vector2(4, -2) });
        input.FillTick(local);
        bool staleFocusRecovered = input.IsCaptured && local.Get("BT_Up") == 1 && local.MouseX == 4 && local.MouseY == 2 &&
            !local.Commands.Exists(c => c.Name is "Fire" or "GadgetQuickChoice");
        local.EndTick();
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        input.FillTick(local);
        bool recoveredClickFires = local.Commands.Exists(c => c.Name == "Fire");
        input.ReleasePointer();
        input.QueueFree();
        menu.QueueFree();
        return captured && delivered && cleared && deferred && restored && released && clickRestored && safeClick && nextClickFires &&
            backgroundReleased && waitsForForeground && staleFocusRecovered && recoveredClickFires
            ? SmokeResult.Pass("join capture, movement/look, focus recovery; refocus click restores control without firing, next click fires")
            : SmokeResult.Fail($"captured={captured} delivered={delivered} ({received}) cleared={cleared} deferred={deferred} restored={restored} released={released} clickRestored={clickRestored} safeClick={safeClick} nextClickFires={nextClickFires} backgroundReleased={backgroundReleased} waitsForForeground={waitsForForeground} staleFocusRecovered={staleFocusRecovered} recoveredClickFires={recoveredClickFires}");
    }
}
