using System;
using Godot;
using OpenScctGame.Chat;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Hud;

/// <summary>
/// Joins the HUD to a play scene: the server's HUD calls and the chat lines through the client session, the chat
/// line (F5 / F6), the HUD's commands (F10 connected players, F8 mission status, F9 Enhanced Reality), the mission
/// status page, and the player's settings (mouse look, frame caps, audio, controller, F12 options):
/// <code>
/// PlayHud.Attach(this, session, playInput, hud);   // after HudLayer.Attach
/// </code>
/// Works without a HUD (no UI content): then only the settings apply.
/// </summary>
public static class PlayHud
{
    /// <summary>What <see cref="Attach"/> added.</summary>
    public sealed record Parts(HudLayer? Hud, ChatInput? Chat, HudCommands? Commands, GameStatusPage? Status, OptionsRuntime Options);

    public static Parts Attach(Node scene, PlaySession session, PlayInput? input, HudLayer? hud)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(session);
        OptionsRuntime options = OptionsRuntime.Attach(scene, session, input, hud);
        if (hud is null)
        {
            return new Parts(null, null, null, null, options);
        }

        hud.Connect(session.Client);
        hud.Options.KeyName = command => HudCommands.KeyOf(input?.Bindings, command) is { } key ? hud.Assets?.KeyDisplayName(key) ?? key : null;
        hud.Options.KeyBoundTo = binding => KeyTokens.KeyBoundTo(input?.Bindings?.Layout, binding) is { } key ? hud.Assets?.KeyDisplayName(key) ?? key : null;
        var chat = new ChatInput { Link = new SessionChatLink(session.Client), PlayInput = input };
        scene.AddChild(chat);
        hud.AddOverlay(chat);

        var statusLayer = new CanvasLayer { Name = "StatusLayer", Layer = HudLayer.DefaultLayer + 2 };
        var status = new GameStatusPage { Source = hud.Snapshot, PlayInput = input };
        statusLayer.AddChild(status);
        scene.AddChild(statusLayer);

        var commands = new HudCommands { Hud = hud, Status = status, PlayInput = input, Blocked = () => chat.IsOpen };
        scene.AddChild(commands);
        return new Parts(hud, chat, commands, status, options);
    }
}
