using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PracticeBotCommandTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LiveCommandsRespectPracticePermissionsAndReplicateTheRoster(bool practice)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false, MaxPlayers = 8,
            Bots = OnlineServerBots.Factory(new AiNavCache(string.Empty)),
        };
        settings = settings with { Setup = settings.Setup with { Practice = practice, MaxPlayers = 8 } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 981, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        OnlineClient Join(string name, int team, bool host) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Practice", () => network.Bind(name), "server"),
            OnlineRig.ClientSettings(name, team) with { HostKey = host ? "k" : null }, rig.Now));
        var host = Join("Host", 0, true);
        var guest = Join("Guest", 1, false);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 2), 10));
        guest.SetReady(true);
        Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && p.Ready), 5));
        host.Launch();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null), 20));
        var messages = new List<string>();
        host.MessageReceived += m => { if (m.IsSystem) messages.Add(m.Text); };
        string Command(string command)
        {
            messages.Clear();
            host.Chat("!" + command);
            Assert.True(rig.RunUntil(() => messages.Count > 0, 5), command);
            return messages[^1];
        }
        host.Chat("!help bots");
        Assert.True(rig.RunUntil(() => messages.Any(line => line.StartsWith("Use !help <command>", StringComparison.Ordinal)), 5));
        Assert.Contains(messages, line => line.StartsWith("!botweapon", StringComparison.Ordinal) && line.Contains("merc bots", StringComparison.Ordinal));
        messages.Clear();
        host.Chat("!help botweapon");
        Assert.True(rig.RunUntil(() => messages.Any(line => line.StartsWith("Example:", StringComparison.Ordinal)), 5));
        Assert.Contains(messages, line => line.Contains("resets on respawn", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Server.Players, p => p.IsBot);
        Assert.Null(rig.Server.RoundBots);
        if (!practice)
        {
            foreach (string command in new[] { "addbot spy", "kickbot all", "placebot merc", "botstop", "botresume", "bothealth all 10", "botweapon rifle", "botteam spy merc" })
                Assert.Contains("only", Command(command));
            Assert.DoesNotContain(rig.Server.Players, p => p.IsBot);
            return;
        }

        guest.Chat("!addbot spy");
        rig.RunUntil(() => false, 0.5);
        Assert.DoesNotContain(rig.Server.Players, p => p.IsBot);
        foreach (string command in new[] { "addbot invalid", "addbot spy 0", "addbot spy -1", "addbot spy 999999999999", "addbot spy 1 extra", "placebot merc 2", "botstop extra" })
            Assert.StartsWith("usage:", Command(command));
        Assert.Contains("stopped", Command("botstop"));
        Assert.Equal("No living merc bots to equip.", Command("botweapon shotgun"));
        Assert.Contains("Added 2", Command("addbot MERC 2"));
        Assert.True(rig.Server.RoundBots!.Stopped);
        Assert.Contains("Added 1", Command("addbot spy"));
        Assert.True(rig.RunUntil(() => guest.Lobby.Players.Count(p => p.IsBot) == 3, 5));
        var bots = rig.Server.Players.Where(p => p.IsBot).Select(p => rig.Server.RoundBots!.ControllerOf(p.Id)!).ToArray();
        Assert.All(bots, pc => Assert.NotNull(pc.Pawn));
        Assert.All(bots, pc => Assert.True(pc.PlayerReplicationInfo!.NumLives > 0));
        rig.RunUntil(() => false, 1);
        var positions = bots.Select(pc => pc.Pawn!.Location).ToArray();
        rig.RunUntil(() => false, 1);
        for (int i = 0; i < bots.Length; i++)
        {
            Assert.InRange((bots[i].Pawn!.Location - positions[i]).Size(), 0, 0.1f);
            var pc = Assert.IsType<SPlayerController>(bots[i]);
            Assert.Equal(0, pc.bFire | pc.bAltFire | pc.bUsing | pc.BT_Up | pc.BT_Down | pc.BT_Left | pc.BT_Right);
        }
        Assert.Contains("3 bot(s)", Command("bothealth all 35"));
        Assert.All(bots, pc => Assert.Equal(35, pc.Pawn!.Health));
        Assert.True(rig.RunUntil(() =>
        {
            int seen = 0;
            foreach (var pawn in guest.Session!.World.Level.AllActors<Pawn>())
                if (pawn.PlayerReplicationInfo?.PlayerName.StartsWith("MercBot", StringComparison.Ordinal) == true && pawn.Health == 35) seen++;
            return seen == 2;
        }, 5));
        var spy = Assert.IsAssignableFrom<SPawnAttaque>(bots.Single(pc => SGameInfo.TeamIndexOf(pc) == 0).Pawn);
        var spyWeapon = spy.MyWeapon;
        var human = Assert.IsAssignableFrom<SPAWNDEFENSE>(rig.Server.Host.Players.Single(p => p.Controller?.PlayerReplicationInfo?.PlayerName == "Guest").Controller!.Pawn);
        var humanWeapon = human.MyWeapon;
        Assert.Equal("Equipped 2 merc bot(s) with shotgun.", Command("botweapon SHOTGUN"));
        Assert.All(bots.Where(pc => SGameInfo.TeamIndexOf(pc) == 1), pc =>
        {
            var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(pc.Pawn);
            var weapon = Assert.IsType<SWeaponDefense_Shotgun>(pawn.MyWeapon);
            Assert.NotNull(weapon.Dummy);
            Assert.Same(weapon.Dummy, pawn.WeaponDummy);
        });
        Assert.Same(spyWeapon, spy.MyWeapon);
        Assert.Same(humanWeapon, human.MyWeapon);
        foreach (string command in new[] { "botweapon", "botweapon shocker", "botweapon default", "botweapon merc shotgun", "botweapon all rifle" })
            Assert.Equal("usage: botweapon <rifle|uzi|shotgun> (all living merc bots)", Command(command));
        Assert.Equal("Equipped 2 merc bot(s) with uzi.", Command("botweapon uzi"));
        Assert.All(bots.Where(pc => SGameInfo.TeamIndexOf(pc) == 1), pc => Assert.IsType<SWeaponDefense_Uzi>(((SBasePawn)pc.Pawn!).MyWeapon));
        Assert.Equal("Equipped 2 merc bot(s) with rifle.", Command("botweapon rifle"));
        Assert.All(bots.Where(pc => SGameInfo.TeamIndexOf(pc) == 1), pc => Assert.IsType<SWeaponDefense>(((SBasePawn)pc.Pawn!).MyWeapon));
        Assert.Contains("Moved 1", Command("botteam spy merc"));
        Assert.All(bots, pc => Assert.IsAssignableFrom<SPAWNDEFENSE>(pc.Pawn));
        Assert.True(rig.Server.RoundBots!.Stopped);
        Assert.Contains("Removed 1", Command("kickbot merc"));
        Assert.True(rig.RunUntil(() => guest.Lobby.Players.Count(p => p.IsBot) == 2, 5));
        Assert.Contains("resumed", Command("botresume"));
        Assert.False(rig.Server.RoundBots.Stopped);
        Assert.Contains("Removed 2", Command("kickbot all"));
        Assert.True(rig.RunUntil(() => guest.Lobby.Players.All(p => !p.IsBot), 5));
        Assert.All(bots, pc => Assert.True(pc.bDeleteMe));
        Assert.False(rig.Server.Host.Game!.bGameEnded);
        Assert.Equal(0, rig.Server.Setup.SpyBots + rig.Server.Setup.MercBots);

        Assert.Contains("stopped", Command("botstop"));
        Assert.Contains("Added 6", Command("addbot merc 24"));
        Assert.Contains("full", Command("addbot spy"));
        Assert.Equal(8, rig.Server.Players.Count(p => !p.IsSpectator));
        Assert.Contains("Removed 6", Command("kickbot all"));
        Assert.False(rig.Server.Host.Game.bGameEnded);

        var previousSession = host.Session;
        host.Command("restart");
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby), 10));
        foreach (var client in rig.Clients) client.SetReady(true);
        Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Match && host.Session is not null && !ReferenceEquals(host.Session, previousSession) && host.Session.LocalPlayer?.Pawn is not null, 30), rig.Server.Status() + " | " + host.State + " | " + host.Error + " | " + string.Join(" | ", messages));
        Assert.Null(rig.Server.RoundBots);
        Assert.Contains("Added 1", Command("addbot merc"));
        Assert.True(rig.Server.RoundBots!.Stopped);
        var respawning = Assert.IsType<SPlayerController>(rig.Server.RoundBots.ControllerOf(rig.Server.Players.Single(p => p.IsBot).Id));
        var beforeDeath = respawning.Pawn;
        respawning.PawnTakeDamage(10000, respawning.Pawn!, respawning.Pawn!.Location, default, SDamageType.Suicided);
        Assert.True(rig.RunUntil(() => respawning.Pawn is { Health: > 0 } pawn && !ReferenceEquals(pawn, beforeDeath), 15));
        rig.RunUntil(() => false, 1);
        var respawnPosition = respawning.Pawn!.Location;
        rig.RunUntil(() => false, 1);
        Assert.InRange((respawning.Pawn.Location - respawnPosition).Size(), 0, 0.1f);
        Assert.Equal(0, respawning.bFire | respawning.bUsing | respawning.BT_Up | respawning.BT_Left);
    }
}
