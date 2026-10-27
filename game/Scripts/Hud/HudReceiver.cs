using System;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Provenance;
using OpenScct.Server;

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

    public HudReceiver(HudLayer hud)
    {
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
    }

    /// <summary>Messages handed to the HUD so far (tests).</summary>
    public int Received { get; private set; }

    /// <summary>Starts receiving for <paramref name="client"/>'s local controller.</summary>
    public void Connect(ClientSession client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (ReferenceEquals(_client, client))
        {
            return;
        }

        _client = client;
        NetFunctionHandler? previous = client.Driver.Functions.Fallback;
        client.Driver.Functions.Fallback = (self, function, args, from) =>
        {
            if (!client.Driver.IsServer && self is PlayerController pc && Handle(pc, NetGameHost.ToGameCall(function, args)))
            {
                return;
            }

            previous?.Invoke(self, function, args, from);
        };
        client.ChatReceived += OnChat;
    }

    /// <summary>A call for <paramref name="pc"/> as the received function's <see cref="GameCall"/>; false when it is not a HUD call.</summary>
    public bool Handle(PlayerController pc, in GameCall call)
    {
        ArgumentNullException.ThrowIfNull(pc);
        switch (call.Function.ToString())
        {
            case "ReceiveLocalizedMessage":
                Received++;
                HudMessages.ReceiveLocalizedMessage(pc, call.Class.Path ?? HudMessages.GameMessagesClass, call.Int, call.Pri1, call.Pri2,
                    call.Object ?? call.Actor);
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
        _hud.Console.Message(pri?.PlayerName, text, type, pc.Level.TimeSeconds);
    }

    /// <summary>A relayed chat line: the TeamMessage of the original, with the sender's name.</summary>
    private void OnChat(ChatLine line)
    {
        Received++;
        float now = (_client?.World.Level.Info.LocalPlayerController as SPlayerController)?.Level.TimeSeconds ?? _client?.World.TimeSeconds ?? 0f;
        _hud.Console.Message(line.SenderName, line.Text, line.Scope == EChatScope.TeamSay ? "TeamSay" : "Say", now);
    }
}
