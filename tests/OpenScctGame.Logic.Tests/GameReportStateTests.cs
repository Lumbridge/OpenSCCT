using OpenScct.Project;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class GameReportStateTests
{
    [Fact]
    public void ReportsDistinguishSessionsWithoutIdentityOrConnectionData()
    {
        Assert.Equal(ReportSession.Menu, new GameReportState().Session);
        Assert.Equal(ReportSession.Offline, new GameReportState(Playing: true).Session);
        Assert.Equal(ReportSession.OnlineClient, new GameReportState(Online: true, Playing: true).Session);
        Assert.Equal(ReportSession.OnlineHost, new GameReportState(Online: true, OnlineHost: true).Session);
        Assert.Equal(ReportSession.HubClient, new GameReportState(Hub: true, Playing: true).Session);
        Assert.Equal(ReportSession.HubHost, new GameReportState(Hub: true, HubHost: true).Session);
        Assert.Equal(ReportSession.OnlineClient, new GameReportState(Online: true, Hub: true).Session);
        Assert.Equal(ReportSession.Replay, new GameReportState(Replay: true, Online: true, Hub: true).Session);
    }

    [Theory]
    [InlineData(0, false, "Spy")]
    [InlineData(1, false, "Mercenary")]
    [InlineData(0, true, "Spectator")]
    [InlineData(null, true, "Spectator")]
    [InlineData(null, false, null)]
    [InlineData(7, false, null)]
    public void ReportsOnlyKnownSides(int? team, bool spectator, string? expected) =>
        Assert.Equal(expected, GameReportState.Side(team, spectator));

    [Theory]
    [InlineData("TestBox", "TestBox")]
    [InlineData("C:\\Users\\PrivateName\\Converted\\Maps\\AquaE.json", "AquaE.json")]
    [InlineData("/home/private-name/maps/TestBox.tscn", "TestBox.tscn")]
    [InlineData("res://maps/AquaE.json", "AquaE.json")]
    [InlineData("Clocktower.v2", "Clocktower.v2")]
    [InlineData("/home/private-name/maps/Clocktower.v2", "Clocktower.v2")]
    [InlineData("Custom & 雨 #1.v2", "Custom & 雨 #1.v2")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ReportsNeverIncludeLocalMapDirectories(string? input, string? expected) =>
        Assert.Equal(expected, GameReportState.MapId(input));

    [Theory]
    [InlineData(true, InGameMenuNet.None)]
    [InlineData(true, InGameMenuNet.Lan)]
    [InlineData(false, InGameMenuNet.Lan)]
    public void HelpReturnsToTheOpenMenuWithoutLeavingOrResuming(bool host, InGameMenuNet net)
    {
        var context = new InGameMenuContext(host, net, MatchBegun: true, Lives: 3);
        var menu = new InGameMenuModel();
        menu.Open(context, 0);
        var entries = menu.Entries;
        Assert.Equal(InGameMenuAction.None, menu.Click(InGameMenuButton.Help));
        Assert.Equal(InGameMenuPage.Help, menu.Page);
        Assert.Same(entries, menu.Entries);
        Assert.Equal(InGameMenuAction.None, menu.KeyPressed("Escape", 2));
        Assert.Equal(InGameMenuPage.Main, menu.Page);
        Assert.True(menu.IsOpen);
        Assert.Equal(context, menu.Context);
        menu.Click(InGameMenuButton.Help);
        menu.HelpClosed();
        Assert.Equal(InGameMenuPage.Main, menu.Page);
        Assert.Equal(InGameMenuAction.None, menu.EndSession(3));
        Assert.Equal(InGameMenuAction.None, menu.ConfirmLobby());
    }
}
