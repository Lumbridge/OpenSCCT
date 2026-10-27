using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// Who a chat line is for, as the original's message types name it: <c>Say</c> reaches every player,
/// <c>TeamSay</c> the players of the sender's team (Engine.BroadcastHandler.Broadcast and BroadcastTeam).
/// </summary>
public enum EChatScope : byte
{
    Say = 0,
    TeamSay = 1,
    MAX = 2,
}

/// <summary>A chat line as the server relays it: the sender (id, name and team) and the text.</summary>
public readonly record struct ChatLine(EChatScope Scope, int SenderId, string SenderName, byte SenderTeam, string Text)
{
    /// <summary>The line the original HUD shows for it (HUD.Message: <c>PlayerName $ ": " $ Msg</c>).</summary>
    public string Display => SenderName.Length > 0 ? SenderName + ": " + Text : Text;
}

/// <summary>Chat limits.</summary>
public static class ChatK
{
    /// <summary>Longest line the chat input takes, in characters (the console's typed line stops at 100).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.Console s_ChatTyping.KeyType: Len(TypedStr) < 100")]
    public const int MaxChars = 100;

    /// <summary>Team byte of a sender without a team.</summary>
    public const byte NoTeam = 255;

    /// <summary>
    /// Characters every sender together may broadcast before the server's next one-second reset
    /// (BroadcastHandler.AllowsBroadcast: SentText &lt; 260, cleared by UpdateSentText from the game timer).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.BroadcastHandler.AllowsBroadcast SentText < 260; SGameInfo.Timer (1 s) calls UpdateSentText")]
    public const int BroadcastBudget = 260;

    /// <summary>Seconds between resets of <see cref="BroadcastBudget"/> (the game timer).</summary>
    [Provenance(ProvenanceKind.Decoded, "SGameInfo SetTimer(1.0, true); Timer calls BroadcastHandler.UpdateSentText")]
    public const float BudgetPeriod = 1f;
}
