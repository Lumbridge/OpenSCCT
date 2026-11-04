using System;
using Godot;
using OpenScct.Net;
using OpenScctGame.Chat;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Hud;

/// <summary>
/// Joins the HUD to a play scene: the server's HUD calls and the chat lines through the client session, the chat
/// line (F5 / F6), the HUD's commands (F10 connected players, F8 mission status, F9 Enhanced Reality), the mission
/// status page, and the player's settings (mouse look, frame caps, audio, controller, F12 options):
/// <code>
/// PlayHud.Attach(this, session, playInput, hud);   // after HudLayer.Attach
/// </code>
/// Works without the original HUD content: settings and the native chat panel still apply.
/// </summary>
public static class PlayHud
{
    /// <summary>What <see cref="Attach"/> added.</summary>
    public sealed record Parts(HudLayer? Hud, ChatInput? Chat, HudCommands? Commands, GameStatusPage? Status, OptionsRuntime Options)
    {
        public ScoreboardOverlay? Scoreboard { get; init; }
    }

    public static Parts Attach(Node scene, PlaySession session, PlayInput? input, HudLayer? hud)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(session);
        OptionsRuntime options = OptionsRuntime.Attach(scene, session, input, hud);
        var chat = new ChatInput { Link = new SessionChatLink(session.Client), PlayInput = input };
        chat.CanOpen = () => !session.IsStopped &&
            (scene is not PlayRoot root || root.Config.Demo is null && root.Menu?.IsOpen != true);
        var statusLayer = new CanvasLayer { Name = "StatusLayer", Layer = HudLayer.DefaultLayer + 2 };
        var scoreboard = new ScoreboardOverlay { Session = session.Client, Fallback = hud?.Snapshot, Map = session.Settings.Map };
        statusLayer.AddChild(scoreboard);
        scene.AddChild(statusLayer);
        if (hud is null)
        {
            // The hub can adopt this same native composer even when converted Magma assets are unavailable.
            void Received(ChatLine line)
            {
                if (chat.ReceiveMatchMessages)
                    ChatNotifications.ReceiveChat(chat.Transcript, line, chat.Clock());
            }
            session.Client.ChatReceived += Received;
            chat.TreeExiting += () => session.Client.ChatReceived -= Received;
            scene.AddChild(chat);
            var fallbackCommands = new HudCommands { Scoreboard = scoreboard, PlayInput = input,
                Blocked = () => chat.IsOpen || session.IsStopped || (scene is PlayRoot play && play.Menu?.IsOpen == true) };
            scene.AddChild(fallbackCommands);
            return new Parts(null, chat, fallbackCommands, null, options) { Scoreboard = scoreboard };
        }

        hud.Options.KeyName = command => HudCommands.KeyOf(input?.Bindings, command) is { } key ? hud.Assets?.KeyDisplayName(key) ?? key : null;
        hud.Options.KeyBoundTo = binding => KeyTokens.KeyBoundTo(input?.Bindings?.Layout, binding) is { } key ? hud.Assets?.KeyDisplayName(key) ?? key : null;

        var status = new GameStatusPage { Source = hud.Snapshot, PlayInput = input };
        statusLayer.AddChild(status);

        chat.CanOpen = () => !session.IsStopped && !status.IsOpen &&
            (scene is not PlayRoot root || root.Config.Demo is null && root.Menu?.IsOpen != true);
        chat.VisibilityAllowed = () =>
        {
            chat.Panel.Side = hud.Snapshot.Side;
            return hud.Visible && hud.Snapshot.Valid && !hud.Snapshot.HideHud;
        };
        hud.Receiver.ModernChat = chat;
        hud.Console.Clear();
        hud.Connect(session.Client);
        scene.AddChild(chat);

        var commands = new HudCommands { Hud = hud, Status = status, Scoreboard = scoreboard, PlayInput = input,
            Blocked = () => chat.IsOpen || session.IsStopped || (scene is PlayRoot play && play.Menu?.IsOpen == true) };
        scene.AddChild(commands);
        return new Parts(hud, chat, commands, status, options) { Scoreboard = scoreboard };
    }
}
