using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// Text chat on the server (spec/hud/chat.md): a player's Say or TeamSay line goes to every player, or to the
/// players of the sender's team, with the original's rules: a spectating player may only chat while dead in a
/// match whose voice mode is not Classic, and all senders together may broadcast 260 characters a second. Lines
/// travel as <see cref="EControlMessage.Chat"/> control messages; the receiving client shows them as the original's
/// <c>TeamMessage</c>. Runs on the host's thread.
/// </summary>
public sealed class ChatRelay
{
    private static readonly Name NAME_s_MultiSpectating = new("s_MultiSpectating");
    private static readonly Name NAME_s_MultiSpectating_WhenKilled = new("s_MultiSpectating_WhenKilled");

    private readonly Func<IReadOnlyList<PlayerSlot>> _players;
    private int _sentText;
    private float _nextReset;

    public ChatRelay(Func<IReadOnlyList<PlayerSlot>> players)
    {
        _players = players ?? throw new ArgumentNullException(nameof(players));
    }

    /// <summary>BroadcastHandler.bMuteSpectators: pure spectators may not chat (a config switch, off by default).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.BroadcastHandler config bMuteSpectators; not set in System/Default.ini")]
    public bool MuteSpectators { get; set; }

    /// <summary>Characters broadcast since the last one-second reset (BroadcastHandler.SentText).</summary>
    public int SentText => _sentText;

    /// <summary>Raised for every line the relay sent (server log, tests).</summary>
    public event Action<ChatLine>? Relayed;

    /// <summary>Raised for every line the rules refused, with the reason.</summary>
    public event Action<PlayerSlot, string>? Refused;

    /// <summary>
    /// A line from <paramref name="sender"/> (PlayerController.Say or TeamSay on the server): checks the rules and
    /// sends it to its recipients. False when it was refused or empty.
    /// </summary>
    [Uc("Engine.PlayerController.Say", Sha1 = "b755cfe67c2c10d9e630af3eb567c9a98c42b9d1")]
    [Uc("Engine.PlayerController.TeamSay", Sha1 = "2da7f907cca3dad61e060736baa1a3056f296c39")]
    [Uc("SBase.SPlayerController.s_MultiSpectating.Say", Sha1 = "dee865891a007f29a309cfcda36b7cc57e0a0c17")]
    [Uc("SBase.SPlayerController.s_MultiSpectating.TeamSay", Sha1 = "c2349de619ff0c99345913d23b85ba1f21ab7622")]
    [PortOf("uc:Engine.PlayerController:Say", Spec = "spec/hud/chat.md#rules")]
    [PortOf("uc:Engine.PlayerController:TeamSay", Spec = "spec/hud/chat.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_MultiSpectating.Say", Spec = "spec/hud/chat.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_MultiSpectating.TeamSay", Spec = "spec/hud/chat.md#rules")]
    [Uc("Engine.BroadcastHandler.Broadcast", Sha1 = "09653945097b12937dee2c676407a99d101f6daa")]
    [Uc("Engine.BroadcastHandler.BroadcastTeam", Sha1 = "fa7d7d3239e752a1d62b4c93d937d064bae1217a")]
    [Uc("Engine.BroadcastHandler.BroadcastText", Sha1 = "9fa907325b958619a91c837db71c75c2c221f2f2")]
    [PortOf("uc:Engine.BroadcastHandler:Broadcast", Spec = "spec/hud/chat.md#rules")]
    [PortOf("uc:Engine.BroadcastHandler:BroadcastTeam", Spec = "spec/hud/chat.md#rules")]
    [PortOf("uc:Engine.BroadcastHandler:BroadcastText", Spec = "spec/hud/chat.md#rules")]
    public bool Receive(PlayerSlot sender, EChatScope scope, string? text)
    {
        ArgumentNullException.ThrowIfNull(sender);
        string line = Clean(text);
        if (line.Length == 0)
        {
            return false;
        }

        if (sender.Controller is SPlayerController pc && pc.IsInState(NAME_s_MultiSpectating) && !IsDedicatedServerMode(pc) &&
            !CanTextChatWhileSpectating(pc))
        {
            Refused?.Invoke(sender, "spectators may not chat");
            return false;
        }

        if (!AllowsBroadcast(sender, line.Length))
        {
            Refused?.Invoke(sender, "too much text this second");
            return false;
        }

        byte team = TeamOf(sender);
        var chat = new ChatLine(scope, sender.PlayerId, NameOf(sender), team, line);
        ControlMessage message = ControlMessage.Chat(chat);
        IReadOnlyList<PlayerSlot> players = _players();
        for (int i = 0; i < players.Count; i++)
        {
            PlayerSlot p = players[i];
            if (scope == EChatScope.Say || TeamOf(p) == team)
            {
                p.Connection.ControlChannel?.Send(message);
            }
        }

        Relayed?.Invoke(chat);
        return true;
    }

    /// <summary>The game timer's BroadcastHandler.UpdateSentText, once a second of game time.</summary>
    [Uc("Engine.BroadcastHandler.UpdateSentText", Sha1 = "4668758fd42afc5cf93b4c1ccbf33fdd69dede31")]
    [PortOf("uc:Engine.BroadcastHandler:UpdateSentText", Spec = "spec/hud/chat.md#rules")]
    public void Tick(float timeSeconds)
    {
        if (timeSeconds >= _nextReset)
        {
            _sentText = 0;
            _nextReset = timeSeconds + ChatK.BudgetPeriod;
        }
    }

    /// <summary>BroadcastHandler.AllowsBroadcast: the line's length is counted even when it is refused.</summary>
    [Uc("Engine.BroadcastHandler.AllowsBroadcast", Sha1 = "00069d1e8e2e141f3577979929ccd409e547bd3a")]
    [PortOf("uc:Engine.BroadcastHandler:AllowsBroadcast", Spec = "spec/hud/chat.md#rules")]
    private bool AllowsBroadcast(PlayerSlot sender, int length)
    {
        if (MuteSpectators && sender.Controller?.PlayerReplicationInfo is { bOnlySpectator: true })
        {
            return false;
        }

        _sentText += length;
        return _sentText < ChatK.BroadcastBudget;
    }

    private static bool IsDedicatedServerMode(SPlayerController pc) =>
        (pc.XLevel.GetService<IGameHost>() ?? NullGameHost.Instance).IsDedicatedServerMode();

    /// <summary>SPlayerController.CanTextChatWhileSpectating: dead in the match and the voice mode is not Classic.</summary>
    [Uc("SBase.SPlayerController.CanTextChatWhileSpectating", Sha1 = "7626e15a6e492c4111b2f05c3277b01631193320")]
    [PortOf("uc:SBase.SPlayerController:CanTextChatWhileSpectating", Spec = "spec/hud/chat.md#rules")]
    public static bool CanTextChatWhileSpectating(SPlayerController pc)
    {
        ArgumentNullException.ThrowIfNull(pc);
        return pc.GameReplicationInfo is { } gri && gri.VoiceChatMode != EVoiceChatMode.VCM_Classic &&
            pc.IsInState(NAME_s_MultiSpectating_WhenKilled);
    }

    /// <summary>
    /// The team the original compares (<c>PlayerReplicationInfo.Team.TeamIndex</c>; a player without a team reads 0,
    /// as UnrealScript reads a property of None). Without game rules, the <c>?Team=</c> login option.
    /// </summary>
    public static byte TeamOf(PlayerSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.Controller is { } pc)
        {
            return (byte)(pc.PlayerReplicationInfo?.Team?.TeamIndex ?? 0);
        }

        string options = slot.LoginOptions;
        int at = options.IndexOf("?Team=", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            int start = at + "?Team=".Length;
            int end = options.IndexOf('?', start);
            string value = end < 0 ? options[start..] : options[start..end];
            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte team))
            {
                return team;
            }
        }

        return 0;
    }

    private static string NameOf(PlayerSlot slot) =>
        slot.Controller?.PlayerReplicationInfo is { PlayerName.Length: > 0 } pri ? pri.PlayerName : slot.Name;

    /// <summary>The line as the chat input could have typed it: printable characters, at most 100, trimmed.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(text.Length, ChatK.MaxChars));
        foreach (char c in text)
        {
            if (sb.Length >= ChatK.MaxChars)
            {
                break;
            }

            if (!char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }
}
