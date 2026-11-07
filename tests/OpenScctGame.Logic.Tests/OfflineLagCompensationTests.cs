using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class OfflineLagCompensationTests
{
    [Fact]
    public void Chat_reports_and_changes_the_hosts_lag_compensation_without_becoming_a_public_message()
    {
        var clock = new VirtualClock();
        PlaySession session;
        try
        {
            session = new PlaySession(new PlaySettings
            {
                Map = MapCatalog.TestBox, OfflineRules = true, HostMode = PlayHostMode.Inline,
                SimLink = SimLinkProfile.C,
            }, clock);
        }
        catch (FileNotFoundException error) { Assert.Skip(error.Message); return; }
        using (session)
        {
            bool Until(Func<bool> condition)
            {
                for (int i = 0; i < 15 * Sim.Hz; i++)
                {
                    clock.Advance(1_000_000 / Sim.Hz);
                    session.Step(clock.NowMicroseconds);
                    if (condition()) return true;
                }
                return condition();
            }
            Assert.True(Until(() => session.LocalPawn is not null));
            var messages = new List<ChatLine>();
            session.Client.ChatReceived += messages.Add;
            string Command(string text)
            {
                messages.Clear();
                session.Client.SendChat(EChatScope.Say, "!" + text);
                Assert.True(Until(() => messages.Count > 0), text);
                Assert.All(messages, line => Assert.Equal(0, line.SenderId));
                return messages[^1].Text;
            }

            Assert.Equal("lagcomp: on - ceiling 200ms", Command("lagcomp"));
            Assert.Equal("lagcomp: off", Command("lagcomp off"));
            Assert.False(session.Host.LagCompensation.Enabled);
            Assert.Equal("lagcomp: on - ceiling 150ms", Command("lagcomp 150"));
            Assert.True(session.Host.LagCompensation.Enabled);
            Assert.Equal(150, session.Host.LagCompensation.CeilingMs);
            Assert.Equal("lagcomp: on - ceiling 200ms", Command("lagcomp reset"));
            Assert.Equal("lagcomp: !lagcomp [on|off|reset|<ceiling>]", Command("lagcomp invalid"));
            Assert.Equal(200, session.Host.LagCompensation.CeilingMs);
            Assert.Contains("local server", Command("help lagcomp"));
        }
    }
}
