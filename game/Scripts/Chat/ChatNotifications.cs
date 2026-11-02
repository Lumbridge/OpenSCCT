using System;
using OpenScct.Net;
using OpenScct.Server;

namespace OpenScctGame.Chat;

/// <summary>Maps authoritative server notices and player chat onto the shared transcript.</summary>
public static class ChatNotifications
{
    public static void ReceiveChat(ChatTranscript transcript, ChatLine line, double now)
    {
        bool server = line.SenderId == 0 && line.SenderTeam == ChatK.NoTeam;
        transcript.Add(server ? ChatChannel.System : line.Scope == EChatScope.TeamSay ? ChatChannel.Team : ChatChannel.All,
            line.Text, line.SenderName, line.SenderId, line.SenderTeam, now);
    }

    public static void ReceiveNotice(ChatTranscript transcript, string text, double now)
    {
        const string legacyPrefix = "Server: ";
        // Older servers emit a short server ChatLine followed immediately by this full system copy.
        // Upgrade that authoritative echo in place; a player merely named Server cannot match it.
        if (text.StartsWith(legacyPrefix, StringComparison.Ordinal) && transcript.Entries.Count > 0)
        {
            ChatEntry last = transcript.Entries[^1];
            string body = text[legacyPrefix.Length..];
            if (last is { Channel: ChatChannel.System, SenderId: 0, SenderTeam: ChatK.NoTeam, SenderName: "Server" } &&
                now >= last.ReceivedAt && now - last.ReceivedAt <= 2d && last.Text == ChatRelay.Clean(body))
            {
                // Clear the echo marker so a second independent notice is still a distinct message.
                transcript.ReplaceLast(last with { Text = body, SenderName = "", ReceivedAt = now });
                return;
            }
        }
        transcript.Add(ChatChannel.System, text, "", 0, ChatK.NoTeam, now);
    }
}
