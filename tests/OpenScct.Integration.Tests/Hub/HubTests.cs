using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScct.Net.Voice.Proximity;
using OpenScct.Server;
using OpenScct.Server.Hub;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

/// <summary>
/// The hub mode (spec/additions/hub.md) on the tracked Hub map, over SimLink with a virtual clock: players join at any
/// time on the side they ask for and spawn at the plaza at once, change side, chat, see each other's names, take no
/// damage, and the match never ends.
/// </summary>
[Collection(BotMatchCollection.Name)]
public sealed class HubTests
{
    private readonly ITestOutputHelper _output;

    public HubTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<NavGrid> Grid = new(() => HubSoak.BuildGrid(WalkerRig.Maps, HubK.MapName));

    internal sealed class Rig : IDisposable
    {
        private readonly VirtualClock _clock = new();
        private long _frame;

        public Rig(int bots, string profile = "A", ulong seed = 5, bool interest = true, HubSoakSettings? soak = null)
        {
            Network = new SimLinkNetwork(_clock, seed, SimLinkProfile.Get(profile));
            Server = new HubServer(Network.Bind("server"), new HubSettings
            {
                Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, Seed = seed, InterestManagement = interest, Motd = "Welcome to the test hub.",
            });
            Soak = new HubSoak((soak ?? new HubSoakSettings()) with
            {
                Bots = bots, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, Grid = Grid.Value, Seed = seed,
            }, Network.Bind, "server", _clock.NowMicroseconds);
        }

        public SimLinkNetwork Network { get; }

        public HubServer Server { get; }

        public HubSoak Soak { get; }

        public long Frame => _frame;

        public long Now => _clock.NowMicroseconds;

        public IClock Clock => _clock;

        /// <summary>Wall time of each server tick (milliseconds), for the soak's report.</summary>
        public List<double> ServerTickMs { get; } = new();

        /// <summary>Wall time the bots took, all frames together (seconds).</summary>
        public double BotSeconds { get; private set; }

        public void Run(float seconds, Action? afterFrame = null)
        {
            long until = _frame + (long)(seconds * Sim.Hz);
            var watch = new System.Diagnostics.Stopwatch();
            while (_frame < until)
            {
                _frame++;
                _clock.AdvanceTo(VirtualClock.TickToMicroseconds(_frame, Sim.Hz));
                watch.Restart();
                Server.Tick(_clock.NowMicroseconds);
                ServerTickMs.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                Soak.Step(_clock.NowMicroseconds);
                BotSeconds += watch.Elapsed.TotalSeconds;
                afterFrame?.Invoke();
            }
        }

        public void Dispose()
        {
            Soak.Dispose();
            Server.Dispose();
        }
    }

    [Fact]
    public void PlayersJoinOnTheirSideAndSpawnAtThePlaza()
    {
        using var rig = new Rig(4, soak: new HubSoakSettings { ChatPerMinute = 0f, SideChangesPerMinute = 0f });
        rig.Run(12f);
        _output.WriteLine(rig.Server.Status());
        Assert.Equal(4, rig.Server.Players.Count);
        int spies = 0;
        foreach (HubPlayer p in rig.Server.Players)
        {
            SPlayerController pc = Assert.IsAssignableFrom<SPlayerController>(p.Slot.Controller);
            SBasePawn pawn = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
            Assert.False(pc.PlayerReplicationInfo!.bOnlySpectator);
            int team = SGameInfo.TeamIndexOf(pc);
            spies += team == 0 ? 1 : 0;
            Assert.Equal(team == 0 ? PawnType.SPAWNOFFENSE : PawnType.SPAWNDEFENSE, pawn.SPawnType);
            _output.WriteLine($"{p.Slot.Name}: team {team} at {pawn.Location} state {pc.GetStateName()}");
        }

        Assert.Equal(2, spies);
        foreach (BotClient bot in rig.Soak.Bots)
        {
            Assert.NotNull(bot.Controller?.Pawn);
            Assert.True(bot.ThinkFrames > 0, bot.Name);
        }

        // The match runs on, with nobody's end in sight.
        Assert.True(rig.Server.Host.Game!.GameReplicationInfo!.bMatchHasBegun);
        Assert.False(rig.Server.Host.Game.bGameEnded);
        foreach (BotClient b in rig.Soak.Bots)
        {
            _output.WriteLine($"{b.Name}: {b.Brain.Status} at {b.Controller?.Pawn?.Location}");
        }
    }

    [Fact]
    public void SideChangeChatNamesAndNoDamage()
    {
        using var rig = new Rig(3, soak: new HubSoakSettings { ChatPerMinute = 0f, SideChangesPerMinute = 0f });
        rig.Run(10f);
        var bots = new List<BotClient>(rig.Soak.Bots);
        PlayerSlot first = rig.Server.Players[0].Slot;
        Assert.Equal(0, SGameInfo.TeamIndexOf(first.Controller));

        // Side change: the player is a merc now, with a merc pawn, on the server and on his own client.
        var hub = new HubClient(bots[0].Session);
        try
        {
            hub.RequestSide(1);
            rig.Run(4f);
            Assert.Equal(1, SGameInfo.TeamIndexOf(first.Controller));
            Assert.Equal(PawnType.SPAWNDEFENSE, Assert.IsAssignableFrom<SBasePawn>(first.Controller!.Pawn).SPawnType);
            Assert.Equal(1, hub.Team);
            Assert.Equal(PawnType.SPAWNDEFENSE, Assert.IsAssignableFrom<SBasePawn>(bots[0].Controller!.Pawn).SPawnType);

            // Chat reaches everyone, with the sender's name.
            hub.Say("hello hub");
            var other = new HubClient(bots[1].Session);
            try
            {
                rig.Run(1f);
                Assert.Contains(other.Chat, c => c.Text == "hello hub" && c.Name == first.Name);
            }
            finally
            {
                other.Dispose();
            }

            // Names over heads: each client sees the other players' pawns with their names and sides.
            var tags = new List<HubNameTag>();
            foreach (BotClient bot in bots)
            {
                HubClient.CollectNameTags(bot.Session.World.Level, bot.Controller, bot.Controller!.Pawn!.Location, 100_000f, tags);
                _output.WriteLine($"{bot.Name} sees {string.Join(", ", tags.ConvertAll(t => $"{t.Name}/{t.Team}"))}");
                Assert.Equal(bots.Count - 1, tags.Count);
                Assert.DoesNotContain(tags, t => t.Name == bot.Name);
            }

            // No damage: a full magazine's worth of hits leaves every pawn untouched and alive.
            SBasePawn victim = Assert.IsAssignableFrom<SBasePawn>(rig.Server.Players[1].Slot.Controller!.Pawn);
            int health = victim.Health;
            var shooter = (SPlayerController)first.Controller!;
            for (int i = 0; i < 10; i++)
            {
                shooter.DamagePawn_C2S(victim, 50, shooter.Pawn, victim.Location, new FVector(0f, 0f, 1f), SDamageType.SDTBase);
            }

            rig.Run(1f);
            Assert.Equal(health, victim.Health);
            Assert.False(victim.bDeleteMe);
            Assert.False(rig.Server.Host.Game!.bGameEnded);
        }
        finally
        {
            hub.Dispose();
        }
    }

    [Fact]
    public void LeaversDoNotEndTheMatchAndLateJoinersPlay()
    {
        using var rig = new Rig(2, soak: new HubSoakSettings { ChatPerMinute = 0f, SideChangesPerMinute = 0f });
        rig.Run(8f);
        var bots = new List<BotClient>(rig.Soak.Bots);
        bots[1].Session.Disconnect();
        rig.Run(4f);
        Assert.Single(rig.Server.Players);
        Assert.False(rig.Server.Host.Game!.bGameEnded);

        // A late joiner plays at once.
        using var late = new HubClient(rig.Network.Bind("Late"), "server", rig.Now, new HubClientSettings
        {
            PlayerName = "Late", Team = 1, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
        });
        rig.Run(5f, () => late.Tick(rig.Now));
        Assert.True(late.Welcomed);
        Assert.Equal(1, late.Team);
        Assert.NotNull(late.LocalPlayer?.Pawn);
        Assert.Contains(late.Chat, c => c.IsSystem && c.Text.Contains("test hub", StringComparison.Ordinal));
    }

    /// <summary>One hub visitor with voice: his visit, his voice and what he heard from each talker.</summary>
    private sealed class Visitor(HubClient client, HubVoice voice) : IDisposable
    {
        public HubClient Client { get; } = client;

        public HubVoice Voice { get; } = voice;

        public Dictionary<string, List<float>> Heard { get; } = new(StringComparer.Ordinal);

        public int HeardFrom(string name) => Heard.TryGetValue(name, out List<float>? s) ? s.Count : 0;

        public void Dispose()
        {
            Voice.Dispose();
            Client.Dispose();
        }
    }

    private static double Rms(List<float> samples, int skip)
    {
        double sum = 0;
        int n = 0;
        for (int i = skip; i < samples.Count; i++)
        {
            sum += samples[i] * (double)samples[i];
            n++;
        }

        return n == 0 ? 0 : Math.Sqrt(sum / n);
    }

    [Fact]
    public void ProximityVoiceReachesNearListenersOfBothSidesWithFalloffAndNotFarOnes()
    {
        using var rig = new Rig(0, soak: new HubSoakSettings { ChatPerMinute = 0f, SideChangesPerMinute = 0f });
        var visitors = new List<Visitor>();
        Visitor Join(string name, int team)
        {
            var client = new HubClient(rig.Network.Bind(name), "server", rig.Now, new HubClientSettings
            {
                PlayerName = name, Team = team, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            });
            var v = new Visitor(client, new HubVoice(client, new VoiceClient(new VoiceSettings(), rig.Clock)));
            visitors.Add(v);
            return v;
        }

        try
        {
            Visitor talker = Join("Talker", 0);
            Visitor near = Join("Near", 1);
            Visitor far = Join("Far", 0);
            var tone = new float[VoiceK.SampleRate];
            for (int i = 0; i < tone.Length; i++)
            {
                tone[i] = 0.3f * (float)Math.Sin(2 * Math.PI * 440 * i / VoiceK.SampleRate);
            }

            bool talking = false;
            int tick = 0;
            long played = 0;
            const int perTick = VoiceK.SampleRate / Sim.Hz;
            var frame = new float[VoiceK.FrameSamples];
            void Step()
            {
                foreach (Visitor v in visitors)
                {
                    v.Client.Tick(rig.Now);
                }

                // Each 60 Hz frame: a 60th of a second of microphone and the voice update; playback takes the 20 ms
                // frames the clock has made due since the last frame.
                int at = tick++ % Sim.Hz * perTick;
                long due = tick * (long)VoiceK.FramesPerSecond / Sim.Hz;
                int frames = (int)(due - played);
                played = due;
                foreach (Visitor v in visitors)
                {
                    if (talking && ReferenceEquals(v, talker))
                    {
                        v.Voice.Voice.SubmitCapture(tone.AsSpan(at, perTick));
                    }

                    v.Voice.Update(null, 1f / Sim.Hz);
                    for (int f = 0; f < frames; f++)
                    {
                        foreach (VoiceTalker t in v.Voice.Voice.Talkers)
                        {
                            if (t.ReadFrame(frame))
                            {
                                string name = v.Voice.NameOf(t.Id) ?? "#" + t.Id.ToString(CultureInfo.InvariantCulture);
                                if (!v.Heard.TryGetValue(name, out List<float>? list))
                                {
                                    v.Heard[name] = list = new List<float>();
                                }

                                list.AddRange(frame);
                            }
                        }
                    }
                }
            }

            rig.Run(6f, Step);
            HubVoice[] voices = [talker.Voice, near.Voice, far.Voice];
            Assert.All(visitors, v => Assert.NotNull(v.Client.LocalPlayer?.Pawn));
            Assert.All(voices, v => Assert.True(v.IsConnected && v.Voice.LocalTalker != 0));
            Assert.Equal(EVoiceChatMode.VCM_Open, rig.Server.Game.GameReplicationInfo!.VoiceChatMode);

            // Place the three on the plaza floor: the merc 1500 uu from the talking spy, the other spy 4000 uu away.
            Pawn PawnOf(string name) => rig.Server.Players.First(p => p.Slot.Name == name).Slot.Controller!.Pawn!;
            void Place(string name, float x, float y)
            {
                Pawn pawn = PawnOf(name);
                Assert.True(pawn.SetLocation(new FVector(x, y, pawn.Location.Z)), name + " placed");
                pawn.Velocity = default;
            }

            Place("Talker", 5000f, 5000f);
            Place("Near", 6500f, 5000f);
            Place("Far", 5000f, 1000f);
            rig.Run(3f, Step);
            ushort talkerId = near.Voice.TalkerOf("Talker") ?? 0;
            string seen = near.Voice.Positions.TryGetPosition(talkerId, out FVector seenAt) ? seenAt.ToString() : "-";
            _output.WriteLine($"server: talker {PawnOf("Talker").Location} near {PawnOf("Near").Location} far {PawnOf("Far").Location}");
            _output.WriteLine($"as the merc sees it: ear {near.Voice.LastEar}, talker at {seen}");

            talking = true;
            rig.Run(4f, Step);
            VoiceRelay relay = rig.Server.VoiceRelay!;
            _output.WriteLine(rig.Server.Status());
            _output.WriteLine($"near heard {near.HeardFrom("Talker")} far heard {far.HeardFrom("Talker")} far got {far.Voice.Voice.Stats.PacketsReceived}");

            // Open mode across sides, in range: the merc hears the spy.
            Assert.True(near.HeardFrom("Talker") > 100 * VoiceK.FrameSamples, $"near heard {near.HeardFrom("Talker")}");

            // Out of range: the server never sent the far spy anything.
            Assert.Equal(0, far.Voice.Voice.Stats.PacketsReceived);
            Assert.Equal(0, far.HeardFrom("Talker"));
            Assert.True(relay.Stats.OutOfRange > 100, $"out of range {relay.Stats.OutOfRange}");

            // The near listener plays him at the distance gain of what he sees, about 0.6 at 1500 uu.
            Assert.True(near.Voice.Positions.TryGetPosition(talkerId, out FVector mouth));
            float distance = (mouth - near.Voice.LastEar!.Value).Size();
            float expected = ProximityVoice.DistanceGain(distance, rig.Server.Settings.ProximityVoice);
            VoiceMix mix = near.Voice.MixOf("Talker");
            _output.WriteLine($"distance {distance:F0} expected gain {expected:F3} mix {mix}");
            Assert.InRange(distance, 1300f, 1700f);
            Assert.Equal(expected, mix.Gain, 2);
            Assert.InRange(mix.Gain, 0.4f, 0.8f);
            List<float> heard = near.Heard["Talker"];
            double level = Rms(heard, heard.Count / 2) / (0.3 / Math.Sqrt(2));
            _output.WriteLine($"level relative to the tone {level:F3}");
            Assert.InRange(level, expected - 0.15, expected + 0.15);

            // The listener's own volume for the talker scales the mix; his mute stops the server sending him.
            near.Voice.SetVolume("Talker", 0.5f);
            rig.Run(1f, Step);
            Assert.Equal(expected * 0.5f, near.Voice.MixOf("Talker").Gain, 2);
            near.Voice.SetMuted("Talker", true);
            rig.Run(1f, Step);
            long got = near.Voice.Voice.Stats.PacketsReceived;
            rig.Run(1f, Step);
            Assert.Equal(got, near.Voice.Voice.Stats.PacketsReceived);
            Assert.True(near.Voice.MixOf("Talker").Gain < 0.001f);

            // The far spy walks up (1000 uu): now the server sends him the talker too.
            Place("Far", 5000f, 4000f);
            rig.Run(3f, Step);
            Assert.True(far.HeardFrom("Talker") > 50 * VoiceK.FrameSamples, $"far heard {far.HeardFrom("Talker")} after walking up");
        }
        finally
        {
            foreach (Visitor v in visitors)
            {
                v.Dispose();
            }
        }
    }
}
