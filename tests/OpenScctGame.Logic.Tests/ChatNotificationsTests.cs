using System;
using OpenScct.Net;
using OpenScctGame.Chat;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ChatNotificationsTests
{
    [Theory]
    [InlineData(0, ChatK.NoTeam, true)]
    [InlineData(0, 0, false)]
    [InlineData(7, ChatK.NoTeam, false)]
    [InlineData(7, 0, false)]
    public void ReservedServerIdentityDistinguishesSystemNoticeFromPlayerNamedServer(int id, byte team, bool system)
    {
        var transcript = new ChatTranscript();
        ChatNotifications.ReceiveChat(transcript, new ChatLine(EChatScope.Say, id, "Server", team, "[b]literal notice[/b]"), 10);
        ChatEntry entry = Assert.Single(transcript.Entries);
        Assert.Equal(system ? ChatChannel.System : ChatChannel.All, entry.Channel);
        Assert.Equal("[b]literal notice[/b]", entry.Text);
    }

    [Fact]
    public void PlayerChatPreservesScopeIdentityAndLiteralText()
    {
        var transcript = new ChatTranscript();
        ChatNotifications.ReceiveChat(transcript, new ChatLine(EChatScope.TeamSay, 42, "[admin] Player", 1, "team text"), 10);
        ChatEntry entry = Assert.Single(transcript.Entries);
        Assert.Equal(ChatChannel.Team, entry.Channel);
        Assert.Equal(42, entry.SenderId);
        Assert.Equal(1, entry.SenderTeam);
        Assert.Equal("[admin] Player", entry.SenderName);
    }

    [Fact]
    public void NoticeCanExceedPlayerComposerLimitWithoutLosingLiteralMarkup()
    {
        var transcript = new ChatTranscript();
        string text = "[b]Maintenance notice[/b] " + new string('x', 150);
        ChatNotifications.ReceiveNotice(transcript, text, 10);
        ChatEntry entry = Assert.Single(transcript.Entries);
        Assert.Equal(ChatChannel.System, entry.Channel);
        Assert.Equal(text, entry.Text);
    }

    [Fact]
    public void LegacyChatThenFullNoticeUpgradesSingleEntryAndNotifiesDisplay()
    {
        var transcript = new ChatTranscript();
        string full = "[b]Scheduled maintenance[/b] " + new string('x', 150);
        int changes = 0;
        transcript.Changed += () => changes++;
        ChatNotifications.ReceiveChat(transcript, ServerLine(full[..100]), 10);
        long id = transcript.Entries[0].Id;
        ChatNotifications.ReceiveNotice(transcript, "Server: " + full, 10.2);
        ChatEntry entry = Assert.Single(transcript.Entries);
        Assert.Equal(id, entry.Id);
        Assert.Equal(ChatChannel.System, entry.Channel);
        Assert.Contains(full, entry.Text, StringComparison.Ordinal);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void RepeatingSameLegacyPairKeepsBothAnnouncements()
    {
        var transcript = new ChatTranscript();
        for (int i = 0; i < 2; i++)
        {
            ChatNotifications.ReceiveChat(transcript, ServerLine("Maintenance soon"), 10 + i * .3);
            ChatNotifications.ReceiveNotice(transcript, "Server: Maintenance soon", 10.1 + i * .3);
        }
        Assert.Equal(2, transcript.Entries.Count);
        Assert.NotEqual(transcript.Entries[0].Id, transcript.Entries[1].Id);
        Assert.All(transcript.Entries, entry => Assert.Equal(ChatChannel.System, entry.Channel));
    }

    [Fact]
    public void StaleOrUnrelatedNoticeDoesNotReplaceServerChat()
    {
        var transcript = new ChatTranscript();
        ChatNotifications.ReceiveChat(transcript, ServerLine("Maintenance soon"), 10);
        ChatNotifications.ReceiveNotice(transcript, "Server: Maintenance soon", 12.1);
        ChatNotifications.ReceiveNotice(transcript, "Map changed", 12.2);
        Assert.Equal(3, transcript.Entries.Count);
        Assert.Equal("Maintenance soon", transcript.Entries[0].Text);
        Assert.Equal("Map changed", transcript.Entries[2].Text);
    }

    [Fact]
    public void InterveningPlayerMessagePreventsCoalescingAcrossConversation()
    {
        var transcript = new ChatTranscript();
        ChatNotifications.ReceiveChat(transcript, ServerLine("Maintenance soon"), 10);
        ChatNotifications.ReceiveChat(transcript, new ChatLine(EChatScope.Say, 7, "Server", 0, "Maintenance soon"), 10.1);
        ChatNotifications.ReceiveNotice(transcript, "Server: Maintenance soon", 10.2);
        Assert.Equal(3, transcript.Entries.Count);
        Assert.Equal(ChatChannel.All, transcript.Entries[1].Channel);
    }

    private static ChatLine ServerLine(string text) => new(EChatScope.Say, 0, "Server", ChatK.NoTeam, text);
}
