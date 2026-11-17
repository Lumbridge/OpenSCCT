using OpenScct.Core.Engine;
using OpenScctGame.Hud;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class NeutralMessageTests
{
    [Fact]
    public void FeedAndLegacyMessagesUseNeutralWordingAndPreserveNames()
    {
        var player = new PlayerReplicationInfo { PlayerName = "Himself" };
        Assert.Equal("Himself used their last life", HudMessages.GetString(56, player, null, null));
        Assert.Equal("Himself eliminated themselves", HudMessages.DeathString(0, player, player));
        Assert.True(HudActivityClassifier.TryCreate(HudMessages.GameMessagesClass, 56, player, null, null, null, 1f, null, out var entry));
        Assert.Equal("Himself used their last life", entry.Text);
        Assert.Equal("Used their last life", HudActivityPresentation.NoticeDetail(entry));
        player.PlayerName = "Renamed";
        Assert.Equal("Himself", HudActivityPresentation.NoticeTitle(entry));
    }

    [Fact]
    public void MissingPlayerUsesNeutralWording()
    {
        Assert.True(HudActivityClassifier.TryCreate(HudMessages.GameMessagesClass, 56, null, null, null, null, 1f, null, out var entry));
        Assert.Equal("Unknown player used their last life", entry.Text);
    }
}
