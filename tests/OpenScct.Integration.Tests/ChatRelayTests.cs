using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>Text chat through the server: Say reaches everyone, TeamSay the sender's team, and the broadcast budget holds.</summary>
public sealed class ChatRelayTests
{
    private sealed class ChatRig : IDisposable
    {
        private readonly VirtualClock _clock = new();
        private readonly List<ITransport> _transports = new();
        private int _frame;

        public ChatRig(params string[] loginOptions)
        {
            var network = new SimLinkNetwork(_clock, 11, SimLinkProfile.B);
            SimLinkTransport serverTransport = network.Bind("server");
            _transports.Add(serverTransport);
            Server = new ServerHost(serverTransport, new ServerSettings { Classes = WalkerRig.Classes, Maps = WalkerRig.Maps });
            Clients = new ClientSession[loginOptions.Length];
            Received = new List<ChatLine>[loginOptions.Length];
            for (int i = 0; i < loginOptions.Length; i++)
            {
                SimLinkTransport t = network.Bind("client" + i.ToString(CultureInfo.InvariantCulture));
                _transports.Add(t);
                Clients[i] = new ClientSession(t, "server", 0, new ClientSettings
                {
                    PlayerName = "P" + i.ToString(CultureInfo.InvariantCulture),
                    LoginOptions = loginOptions[i],
                    Classes = WalkerRig.Classes,
                    Maps = WalkerRig.Maps,
                });
                var list = new List<ChatLine>();
                Received[i] = list;
                Clients[i].ChatReceived += list.Add;
            }

            Run(240);
        }

        public ServerHost Server { get; }

        public ClientSession[] Clients { get; }

        public List<ChatLine>[] Received { get; }

        public void Run(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _frame++;
                _clock.AdvanceTo(VirtualClock.TickToMicroseconds(_frame, Sim.Hz));
                Server.Tick(_clock.NowMicroseconds);
                foreach (ClientSession c in Clients)
                {
                    c.Tick(_clock.NowMicroseconds);
                }
            }
        }

        public void Dispose()
        {
            foreach (ClientSession c in Clients)
            {
                c.Dispose();
            }

            Server.Dispose();
            foreach (ITransport t in _transports)
            {
                t.Dispose();
            }
        }
    }

    [Fact]
    public void SayReachesEveryoneAndTeamSayOnlyTheTeam()
    {
        using var rig = new ChatRig("?Team=0", "?Team=0", "?Team=1");
        Assert.All(rig.Clients, c => Assert.Equal(EClientState.Joined, c.State));

        rig.Clients[0].SendChat(EChatScope.Say, "hello all");
        rig.Run(60);
        rig.Clients[2].SendChat(EChatScope.TeamSay, "  mercs only \u0007 ");
        rig.Run(60);

        for (int i = 0; i < 3; i++)
        {
            Assert.Contains(rig.Received[i], l => l.Text == "hello all" && l.Scope == EChatScope.Say && l.SenderName == "P0" && l.SenderTeam == 0);
        }

        Assert.Contains(rig.Received[2], l => l.Text == "mercs only" && l.Scope == EChatScope.TeamSay && l.SenderTeam == 1);
        Assert.DoesNotContain(rig.Received[0], l => l.Scope == EChatScope.TeamSay);
        Assert.DoesNotContain(rig.Received[1], l => l.Scope == EChatScope.TeamSay);
        Assert.Equal("P0: hello all", rig.Received[1][0].Display);
    }

    [Fact]
    public void TheBroadcastBudgetRefusesTextBeyond260CharactersASecond()
    {
        using var rig = new ChatRig("?Team=0", "?Team=1");
        var refused = new List<string>();
        rig.Server.Chat.Refused += (_, reason) => refused.Add(reason);
        string line = new('a', ChatK.MaxChars);

        // Three full lines inside one second: 100 + 100 pass, the third reaches 300 and is refused.
        rig.Clients[0].SendChat(EChatScope.Say, line);
        rig.Clients[0].SendChat(EChatScope.Say, line);
        rig.Clients[1].SendChat(EChatScope.Say, line);
        rig.Run(20);
        Assert.Equal(2, rig.Received[0].Count);
        Assert.Single(refused);

        // After the next one-second reset the senders may talk again.
        rig.Run(70);
        rig.Clients[1].SendChat(EChatScope.Say, "again");
        rig.Run(30);
        Assert.Contains(rig.Received[0], l => l.Text == "again");
    }

    [Fact]
    public void LinesAreCleanedAndCut()
    {
        Assert.Equal(ChatK.MaxChars, ChatRelay.Clean(new string('x', 500)).Length);
        Assert.Equal("ab", ChatRelay.Clean("\ta\nb  "));
        Assert.Equal(string.Empty, ChatRelay.Clean("   "));
        Assert.Equal(string.Empty, ChatRelay.Clean(null));
    }
}
