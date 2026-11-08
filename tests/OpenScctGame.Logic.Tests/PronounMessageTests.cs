using OpenScct.Core.Engine;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class PronounMessageTests
{
    [Fact]
    public void OfflinePlayReplicatesTheSavedSelectionToTheLocalPlayer()
    {
        var clock = new VirtualClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox, Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            Bots = 0, PlayerName = "Alex", Pronouns = "she/her",
        }, clock);
        for (int frame = 1; frame <= 300 && session.LocalController?.PlayerReplicationInfo?.SvmPronouns != "she/her"; frame++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(frame, Sim.Hz));
            session.Step(clock.NowMicroseconds);
        }
        Assert.Equal("she/her", session.LocalController?.PlayerReplicationInfo?.SvmPronouns);
        Assert.Equal("Alex used her last life", HudMessages.GetString(56, session.LocalController!.PlayerReplicationInfo, null, null));
    }

    [Theory]
    [InlineData("they/them", "their", "themselves")]
    [InlineData("she/her", "her", "herself")]
    [InlineData("he/him", "his", "himself")]
    public void FeedAndLegacyMessagesUseTheSubjectPlayersSelectedForms(string choice, string possessive, string reflexive)
    {
        var player = new PlayerReplicationInfo { PlayerName = "Himself", SvmPronouns = choice };
        Assert.Equal("Himself used " + possessive + " last life", HudMessages.GetString(56, player, null, null));
        Assert.Equal("Himself eliminated " + reflexive, HudMessages.DeathString(0, player, player));
        Assert.True(HudActivityClassifier.TryCreate(HudMessages.GameMessagesClass, 56, player, null, null, null, 1f, null, out var entry));
        Assert.Equal("Himself used " + possessive + " last life", entry.Text);
        Assert.Equal("Used " + possessive + " last life", HudActivityPresentation.NoticeDetail(entry));
        // An existing notice retains the forms captured with its identity even after a later account update.
        player.SvmPronouns = "they/them";
        player.PlayerName = "Renamed";
        Assert.Equal("Himself", HudActivityPresentation.NoticeTitle(entry));
        Assert.Equal("Used " + possessive + " last life", HudActivityPresentation.NoticeDetail(entry));
    }

    [Fact]
    public void MissingAndUnrecognizedSelectionsUseTheyThem()
    {
        var player = new PlayerReplicationInfo { PlayerName = "Alex", SvmPronouns = "unknown" };
        Assert.Equal("Alex used their last life", HudMessages.GetString(56, player, null, null));
        Assert.True(HudActivityClassifier.TryCreate(HudMessages.GameMessagesClass, 56, null, null, null, null, 1f, null, out var entry));
        Assert.Equal("Unknown player used their last life", entry.Text);
    }
}
