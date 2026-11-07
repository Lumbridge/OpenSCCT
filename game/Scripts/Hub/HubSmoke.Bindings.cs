using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Hub;

public sealed partial class HubSmoke
{
    private string? CheckBindings(PlayRoot root)
    {
        var input = root.Controls!;
        var saved = input.Bindings!;
        var hud = root.GetNode<HubHud>("HubHud");
        bool Active(string name) => name == "HubTravel" ? hud.TravelMenuOpen : hud.PlayerListOpen;
        void Send(InputEvent ev)
        {
            root.GetTree().Root.EmitSignal(Window.SignalName.WindowInput, ev);
            root.GetTree().Root.PushInput(ev);
        }
        void Key(Key key)
        {
            Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        }
        void Mouse(MouseButton button)
        {
            Send(new InputEventMouseButton { ButtonIndex = button, Pressed = true, CtrlPressed = true });
            Send(new InputEventMouseButton { ButtonIndex = button, Pressed = false, CtrlPressed = true });
        }
        try
        {
            root.GetTree().Root.GuiReleaseFocus();
            input.Bindings = InputBindings.FromLayout(KeyLayout.Parse("""
                [Engine.Input]
                IK_Ctrl+MiddleMouse=HubTravel
                IK_F12=ShowMenu
                IK_F10=HubPlayers
                IK_U=HubUse
                """), PlaySide.Merc);
            hud._Process(0);
            Key(Godot.Key.F4);
            if (Active("HubTravel")) return "old F4 still opened travel after rebind";
            Mouse(MouseButton.Middle);
            if (!Active("HubTravel")) return "rebound mouse chord did not open travel";
            Key(Godot.Key.Escape);
            if (!Active("HubTravel")) return "old Escape still closed travel after rebind";
            Key(Godot.Key.F12);
            if (Active("HubTravel") || !input.Enabled) return "rebound menu key did not close travel";
            Key(Godot.Key.F10);
            if (!Active("HubPlayerList")) return "rebound player list did not open";
            Key(Godot.Key.P);
            if (!Active("HubPlayerList")) return "old P still closed player list";
            Key(Godot.Key.F12);
            if (Active("HubPlayerList")) return "rebound menu key did not close player list";
            var settings = new GameSettings();
            settings.Pad.MercButtons.Clear();
            settings.Pad.MercButtons["A"] = "Action:hub_travel";
            settings.Pad.MercButtons["B"] = "Action:menu";
            settings.Pad.MercButtons["X"] = "Action:hub_emotes";
            settings.Pad.MercButtons["Y"] = "Action:hub_emote1";
            var pad = new GamepadInput { PlayInput = input, Settings = settings };
            var dispatched = new List<string>();
            input.LocalCommand += dispatched.Add;
            void Button(string button)
            {
                pad.UpdateButtons(input, true, new HashSet<string> { button }, .1);
                pad.UpdateButtons(input, true, new HashSet<string>(), .1);
            }
            try
            {
                Button("A");
                if (!Active("HubTravel") || input.Enabled) return $"controller travel binding failed: commands={string.Join(',', dispatched)} enabled={input.Enabled} menu={root.Menu?.IsOpen} modal={input.ModalInputActive?.Invoke()} typing={hud.Typing}";
                Button("B");
                if (Active("HubTravel") || root.Menu?.IsOpen == true || !input.Enabled) return "controller could not close modal travel";
                Button("X");
                if (input.Enabled) return "controller emote menu did not open";
                Button("Y");
                if (!input.Enabled) return "controller emote selection did not work while gameplay paused";
            }
            finally { input.LocalCommand -= dispatched.Add; pad.Free(); }
            _notes += " rebound-hub=keyboard/mouse/controller";
            return null;
        }
        finally
        {
            if (input.ModalInputActive?.Invoke() == true) input.SendLocalCommand("ShowMenu");
            input.Bindings = saved;
            hud._Process(0);
        }
    }
}
