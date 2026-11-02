using System;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Provenance;
using OpenScct.Server;
using OpenScctGame.Chat;

namespace OpenScctGame.Hud;

/// <summary>
/// Brings the calls the server sends to the local controller for its HUD onto the HUD: <c>ReceiveLocalizedMessage</c>
/// (objective, death and game messages), <c>TeamMessage</c> and <c>ClientMessage</c> (console lines),
/// <c>ClientNotifyNoise</c> (the merc's sound arrows), and the chat
/// lines of the control channel. It sits behind the client's received-call fallback (calls no port runs), passing
/// everything else on to the fallback that was there before.
/// </summary>
public sealed class HudReceiver
{
    private readonly HudLayer _hud;
    private ClientSession? _client;
    private NetFunctionHandler? _previousFallback;
    private NetFunctionHandler? _fallback;

    public HudReceiver(HudLayer hud)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
    }

    /// <summary>Messages handed to the HUD so far (tests).</summary>
    public int Received { get; private set; }

    /// <summary>The native transcript, when the play scene has attached its chat composer.</summary>
    public ChatInput? ModernChat { get; set; }

    /// <summary>Starts receiving for <paramref name="client"/>'s local controller.</summary>
    public void Connect(ClientSession client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (ReferenceEquals(_client, client))
        {
            return;
        }

        Disconnect();
        _client = client;
        NetFunctionHandler? previous = client.Driver.Functions.Fallback;
        _previousFallback = previous;
        _fallback = (self, function, args, from) =>
        {
            if (ReferenceEquals(_client, client) && !client.Driver.IsServer && self is PlayerController pc && Handle(pc, NetGameHost.ToGameCall(function, args)))
            {
                return;
            }

            previous?.Invoke(self, function, args, from);
        };
        client.Driver.Functions.Fallback = _fallback;
        client.ChatReceived += OnChat;
    }

    /// <summary>A round leaving the tree must stop receiving from a session which can outlive its HUD.</summary>
    public void Disconnect()
    {
        _hud.Activity.Reset();
        if (_client is { } client)
        {
            client.ChatReceived -= OnChat;
            if (ReferenceEquals(client.Driver.Functions.Fallback, _fallback))
                client.Driver.Functions.Fallback = _previousFallback;
        }
        _client = null;
        _fallback = null;
        _previousFallback = null;
    }

    /// <summary>A call for <paramref name="pc"/> as the received function's <see cref="GameCall"/>; false when it is not a HUD call.</summary>
    public bool Handle(PlayerController pc, in GameCall call)
    {
        ArgumentNullException.ThrowIfNull(pc);
        switch (call.Function.ToString())
        {
            case "ReceiveLocalizedMessage":
                Received++;
                _hud.Activity.Bind(pc, pc.Level.TimeSeconds);
                HudMessages.ReceiveLocalizedMessage(pc, call.Class.Path ?? HudMessages.GameMessagesClass, call.Int, call.Pri1, call.Pri2,
                    call.Object ?? call.Actor, _hud.Activity);
                return true;
            case "TeamMessage":
                Received++;
                TeamMessage(pc, call.Pri1, call.Text ?? "", call.Name1.IsNone ? "" : call.Name1.ToString());
                return true;
            case "ClientMessage":
                Received++;
                ClientMessage(pc, call.Text ?? "", call.Name1.IsNone ? "" : call.Name1.ToString());
                return true;
            case "ClientNotifyNoise":
                // The sound arrows: the port runs on this machine's HUD (SPlayerController.ClientNotifyNoise).
                Received++;
                pc.ClientNotifyNoise(call.Actor ?? call.Object as Actor, call.Float1);
                return true;
            default:
                return false;
        }
    }

    /// <summary><c>PlayerController.ClientMessage</c>: a TeamMessage from the own PRI, typed Event when untyped.</summary>
    [Uc("Engine.PlayerController.ClientMessage", Sha1 = "bb153449391ce907ce821a02562cfbf9f659a342")]
    [PortOf("uc:Engine.PlayerController:ClientMessage", Spec = "spec/hud/chat.md#rules")]
    public void ClientMessage(PlayerController pc, string text, string type) =>
        TeamMessage(pc, pc.PlayerReplicationInfo, text, type.Length == 0 ? "Event" : type);

    /// <summary><c>PlayerController.TeamMessage</c>: the HUD's Message.</summary>
    [Uc("Engine.PlayerController.TeamMessage", Sha1 = "2b760cceb8fd04c0a00cd7cb9707548e077b2bc7")]
    [PortOf("uc:Engine.PlayerController:TeamMessage", Spec = "spec/hud/chat.md#rules")]
    public void TeamMessage(PlayerController pc, PlayerReplicationInfo? pri, string text, string type)
    {
        ArgumentNullException.ThrowIfNull(pc);
        if (ModernChat is { } chat)
        {
            bool playerMessage = type is "Say" or "TeamSay";
            if (playerMessage && !chat.ReceiveMatchMessages) return;
            chat.Transcript.Add(type == "TeamSay" ? ChatChannel.Team : type == "Say" ? ChatChannel.All : ChatChannel.System,
                text, playerMessage ? pri?.PlayerName ?? "" : "", pri?.PlayerID ?? 0, pri?.Team?.TeamIndex ?? ChatK.NoTeam, chat.Clock());
        }
        else _hud.Console.Message(pri?.PlayerName, text, type, pc.Level.TimeSeconds);
    }

    /// <summary>A relayed chat line: the TeamMessage of the original, with the sender's name.</summary>
    private void OnChat(ChatLine line)
    {
        Received++;
        if (ModernChat is { } chat)
        {
            if (chat.ReceiveMatchMessages)
                chat.Transcript.Add(line.Scope == EChatScope.TeamSay ? ChatChannel.Team : ChatChannel.All,
                    line.Text, line.SenderName, line.SenderId, line.SenderTeam, chat.Clock());
            return;
        }
        float now = (_client?.World.Level.Info.LocalPlayerController as SPlayerController)?.Level.TimeSeconds ?? _client?.World.TimeSeconds ?? 0f;
        _hud.Console.Message(line.SenderName, line.Text, line.Scope == EChatScope.TeamSay ? "TeamSay" : "Say", now);
    }
}
