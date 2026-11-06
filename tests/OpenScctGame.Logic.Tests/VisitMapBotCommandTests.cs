using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class VisitMapBotCommandTests
{
    private sealed class Blocker : Actor { }

    [Fact]
    public void OfflineChatSpawnsPlacesStopsAndRemovesBots()
    {
        var clock = new VirtualClock();
        PlaySession session;
        try
        {
            session = new PlaySession(new PlaySettings
            {
                Map = MapCatalog.TestBox, Rules = PlayRules.MapSettings, OfflineRules = true,
                UnlimitedGear = true, HostMode = PlayHostMode.Inline,
            }, clock);
        }
        catch (FileNotFoundException e) { Assert.Skip(e.Message); return; }
        using (session)
        {
            bool Until(Func<bool> condition, int seconds = 5)
            {
                for (int i = 0; i < seconds * Sim.Hz; i++)
                {
                    clock.Advance(1_000_000 / Sim.Hz);
                    session.Step(clock.NowMicroseconds);
                    if (condition()) return true;
                }
                return condition();
            }
            Assert.True(Until(() => session.LocalPawn is not null, 15));
            Assert.True(Until(() => session.Client.Scoreboard?.Rows.Count == 1));
            var localRow = Assert.Single(session.Client.Scoreboard!.Rows);
            Assert.False(localRow.Bot);
            Assert.Equal(localRow.ParticipantId, session.Client.ScoreboardLocalParticipantId);
            var messages = new List<string>();
            session.Client.ChatReceived += line => messages.Add(line.Text);
            string Command(string text)
            {
                messages.Clear();
                session.Client.SendChat(EChatScope.Say, "!" + text);
                Assert.True(Until(() => messages.Count > 0), text);
                return messages[^1];
            }
            Command("help");
            Assert.True(Until(() => messages.Any(line => line.StartsWith("Use !help <command>", StringComparison.Ordinal))));
            Assert.Contains(messages, line => line.StartsWith("!addbot <team>", StringComparison.Ordinal));
            Assert.Contains(messages, line => line.StartsWith("!botweapon", StringComparison.Ordinal) && line.Contains("merc bots", StringComparison.Ordinal));
            Command("help botweapon");
            Assert.True(Until(() => messages.Any(line => line.StartsWith("Example:", StringComparison.Ordinal))));
            Assert.Contains(messages, line => line.Contains("resets on respawn", StringComparison.Ordinal));
            Assert.Empty(session.Bots!.Bots);
            string stop = Command("botstop");
            Assert.True(stop.Contains("stopped", StringComparison.Ordinal), $"{stop} ended={session.Loader.Game!.bGameEnded} begun={session.Loader.Game.GameReplicationInfo!.bMatchHasBegun} waiting={session.Loader.Game.bWaitingToStartMatch} pawn={session.ServerController?.Pawn is not null}");
            Assert.Contains("Added 1", Command("addbot merc"));
            var first = Assert.Single(session.Bots!.Bots);
            Assert.True(first.Stopped);
            Assert.True(Until(() => session.Client.Scoreboard?.Rows.Count == 2));
            Assert.False(session.Client.Scoreboard!.Rows.Single(row => row.ParticipantId == localRow.ParticipantId).Bot);
            Assert.True(session.Client.Scoreboard.Rows.Single(row => row.ParticipantId != localRow.ParticipantId).Bot);
            Until(() => false, 1);
            FVector at = first.Controller.Pawn!.Location;
            Until(() => false, 1);
            Assert.InRange((first.Controller.Pawn.Location - at).Size(), 0, 0.1f);
            Assert.Contains("Removed 1", Command("kickbot all"));

            // Try the four cardinal directions from the fixture spawn; at least one has open floor.
            bool placed = false;
            for (int yaw = 0; yaw < 65536 && !placed; yaw += 16384)
            {
                session.LocalController!.Rotation = new FRotator(0, yaw, 0);
                Until(() => false, 1);
                placed = Command("placebot merc").StartsWith("Placed", StringComparison.Ordinal);
            }
            Assert.True(placed, string.Join(" | ", messages));
            var placedBot = Assert.Single(session.Bots.Bots);
            Assert.True(placedBot.Stopped);
            Assert.Contains("resumed", Command("botresume"));
            long thought = placedBot.ThinkTicks;
            Assert.True(Until(() => placedBot.ThinkTicks > thought + 5));
            Assert.Contains("Removed 1", Command("kickbot all"));
            Assert.Empty(session.Bots.Bots);
            var own = session.ServerController!.Pawn!;
            var forward = new FRotator(0, session.ServerController.Rotation.Yaw, 0).Vector();
            var wall = session.Host.World.Level.SpawnActor(new Blocker
            {
                bCollideActors = true, bBlockActors = true, bBlockPlayers = true,
                CollisionRadius = 80, CollisionHeight = 200,
            }, own.Location + forward * 130, default)!;
            Assert.Contains("No safe space", Command("placebot merc"));
            Assert.Empty(session.Bots.Bots);
            Assert.False(session.Loader.Game!.bGameEnded);
            wall.Destroy();
        }
    }
}
