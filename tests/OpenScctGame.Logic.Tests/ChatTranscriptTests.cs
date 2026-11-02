using System.Linq;
using OpenScctGame.Chat;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ChatTranscriptTests
{
    [Fact]
    public void RetainsLatestMessagesInArrivalOrderWithStableIdentity()
    {
        var transcript = new ChatTranscript();
        int changes = 0;
        transcript.Changed += () => changes++;
        for (int i = 0; i < 80; i++)
            transcript.Add(ChatChannel.Team, "message " + i, "Ghost", 7, 0, i);

        Assert.Equal(64, transcript.Entries.Count);
        Assert.Equal("message 16", transcript.Entries[0].Text);
        Assert.Equal("message 79", transcript.Entries[^1].Text);
        Assert.Equal(80, changes);
        Assert.Equal(Enumerable.Range(17, 64).Select(i => (long)i), transcript.Entries.Select(e => e.Id));
        Assert.All(transcript.Entries, e =>
        {
            Assert.Equal(ChatChannel.Team, e.Channel);
            Assert.Equal("Ghost", e.SenderName);
            Assert.Equal(7, e.SenderId);
            Assert.Equal(0, e.SenderTeam);
        });
    }

    [Theory]
    [InlineData(9, 1)]
    [InlineData(10, 1)]
    [InlineData(17.99, 1)]
    [InlineData(18, 1)]
    [InlineData(18.5, 0.5)]
    [InlineData(19, 0)]
    [InlineData(30, 0)]
    public void PassiveMessagesLiveEightSecondsThenFadeForOne(double now, double expected)
    {
        var entry = new ChatEntry(1, ChatChannel.All, "hello", "Ghost", 3, 0, 10);
        Assert.Equal(expected, entry.Opacity(now), 5);
    }

    [Fact]
    public void ExpiryDoesNotRemoveHistoryOrFlattenChannelsAndPlayerText()
    {
        var transcript = new ChatTranscript();
        transcript.Add(ChatChannel.All, "[color=red]literal[/color]", "[b]name[/b]", 2, 1, 0);
        transcript.Add(ChatChannel.Nearby, "near you", "Sam", 3, 0, 0);
        transcript.Add(ChatChannel.System, "Joined the hub", "", 0, -1, 0);

        Assert.All(transcript.Entries, e => Assert.Equal(0, e.Opacity(100)));
        Assert.Equal(3, transcript.Entries.Count);
        Assert.Equal("[color=red]literal[/color]", transcript.Entries[0].Text);
        Assert.Equal("[b]name[/b]", transcript.Entries[0].SenderName);
        Assert.Equal(new[] { ChatChannel.All, ChatChannel.Nearby, ChatChannel.System }, transcript.Entries.Select(e => e.Channel));
    }
}
