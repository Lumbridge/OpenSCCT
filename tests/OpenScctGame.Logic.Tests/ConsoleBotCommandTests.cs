using System.Collections.Generic;
using OpenScctGame.Options;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ConsoleBotCommandTests
{
    [Fact]
    public void HelpExplainsCommandsWithoutSendingOrRunningThem()
    {
        var host = new Host();
        var commands = new ConsoleCommands(host);
        var overview = commands.Run("help");
        Assert.InRange(overview.Count, 2, 16);
        Assert.Contains(overview, line => line.Contains("help bots"));
        Assert.Equal(overview, commands.Run("?"));
        var bots = commands.Run("HELP BOTS");
        Assert.InRange(bots.Count, 2, 16);
        Assert.Contains(bots, line => line.Contains("addbot <team> [number]") && line.Contains("defaults to 1"));
        Assert.Contains(bots, line => line.Contains("botstop") && line.Contains("future spawns"));
        var weapon = commands.Run("help BOTWEAPON");
        Assert.Contains(weapon, line => line.Contains("botweapon <rifle|uzi|shotgun>") && line.Contains("living merc bots"));
        Assert.Contains(weapon, line => line.Contains("resets on respawn"));
        Assert.Contains(commands.Run("help kickbot"), line => line.StartsWith("kickbot all"));
        Assert.Contains("Close the game", Assert.Single(commands.Run("help quit")));
        Assert.Contains("No help", Assert.Single(commands.Run("help not-a-command")));
        Assert.Empty(host.Messages);
        Assert.False(host.QuitCalled);
    }

    [Fact]
    public void BotCommandsReachOfflineAndOnlineHostsWithTheirArguments()
    {
        var host = new Host();
        var commands = new ConsoleCommands(host);
        Assert.Equal("not in a game", Assert.Single(commands.Run("botstop")));
        host.CanSendBotCommands = true;
        foreach (string command in new[] { "addbot spy", "kickbot merc 2", "kickbot all", "placebot spy", "botstop", "botresume", "bothealth all 10", "botweapon shotgun", "botteam spy merc 2" })
        {
            Assert.Equal("sent to the server", Assert.Single(commands.Run(command)));
            Assert.Equal("!" + command, host.Messages[^1]);
        }
        Assert.Equal("sent to the server", Assert.Single(commands.Run("ADDBOT MERC 3")));
        Assert.Equal("!addbot MERC 3", host.Messages[^1]);
    }

    private sealed class Host : IConsoleHost
    {
        public GameSettings Settings { get; } = new();
        public bool Online => false;
        public bool CanSendBotCommands { get; set; }
        public List<string> Messages { get; } = [];
        public string? LocationReport => null;
        public bool QuitCalled { get; private set; }
        public void Chat(string text) => Messages.Add(text);
        public void CopyText(string text) { }
        public void SaveSettings(GameSettings settings, bool display) { }
        public void ServerCommand(string command) { }
        public void ServerLogin(string password) { }
        public void Quit() => QuitCalled = true;
    }
}
