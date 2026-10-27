using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Integration.Tests.Online;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Net.Voice;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// Voice chat in the online session (spec/game/lobby-and-match-creation.md, spec/game/voice-chat.md): the session's
/// own relay on every map, the players' <see cref="OnlineVoice"/> following the visit, "Voice in Lobby", "Restrict Mic
/// to Team", the host's voice mode in the round, and the per-player volume a listener sets. Over SimLink on a virtual
/// clock, the same code as over UDP.
/// </summary>
public sealed class LobbyVoiceTests
{
    private const double ToneHz = 440;

    private sealed class Player
    {
        public required OnlineClient Client { get; init; }

        public required OnlineVoice Voice { get; init; }

        public bool Talking { get; set; }

        /// <summary>Samples heard per talker name.</summary>
        public Dictionary<string, List<float>> Heard { get; } = new(StringComparer.Ordinal);

        public void Clear() => Heard.Clear();

        public int HeardFrom(string name) => Heard.TryGetValue(name, out List<float>? s) ? s.Count : 0;
    }

    private sealed class Rig : IDisposable
    {
        private readonly float[] _tone;
        private readonly float[] _frame = new float[VoiceK.FrameSamples];
        private int _tick;

        public Rig(OnlineServerSettings settings)
        {
            var clock = new VirtualClock();
            Network = new SimLinkNetwork(clock, 11, SimLinkProfile.A);
            Online = new OnlineRig(new OnlineServer(Network.Bind("server"), settings, clock.NowMicroseconds), clock);
            Clock = clock;
            _tone = new float[VoiceK.SampleRate];
            for (int i = 0; i < _tone.Length; i++)
            {
                _tone[i] = 0.3f * (float)Math.Sin(2 * Math.PI * ToneHz * i / VoiceK.SampleRate);
            }

            Online.AfterServerTick = _ => { };
        }

        public SimLinkNetwork Network { get; }

        public OnlineRig Online { get; }

        public VirtualClock Clock { get; }

        public List<Player> Players { get; } = new();

        public Player Join(string name, int team, string? hostKey = null)
        {
            int n = 0;
            OnlineClientSettings s = OnlineRig.ClientSettings(name, team) with { HostKey = hostKey };
            var target = JoinTarget.Custom("Voice server", () => Network.Bind(name + "-" + (n++).ToString(CultureInfo.InvariantCulture)), "server");
            OnlineClient client = Online.Add(new OnlineClient(target, s, Online.Now));
            var player = new Player { Client = client, Voice = new OnlineVoice(client, new VoiceClient(new VoiceSettings(), Clock)) };
            Players.Add(player);
            return player;
        }

        /// <summary>One 60 Hz frame; every third one is a 20 ms voice frame (capture, update, playback).</summary>
        public void Step()
        {
            Online.Step();
            if (++_tick % 3 != 0)
            {
                return;
            }

            int at = (_tick / 3 % 50) * VoiceK.FrameSamples;
            foreach (Player p in Players)
            {
                if (p.Talking)
                {
                    p.Voice.Voice.SubmitCapture(_tone.AsSpan(at, VoiceK.FrameSamples));
                }

                p.Voice.Update();
                foreach (VoiceTalker talker in p.Voice.Voice.Talkers)
                {
                    if (talker.ReadFrame(_frame))
                    {
                        string name = p.Voice.NameOf(talker.Id) ?? "#" + talker.Id.ToString(CultureInfo.InvariantCulture);
                        if (!p.Heard.TryGetValue(name, out List<float>? list))
                        {
                            p.Heard[name] = list = new List<float>();
                        }

                        list.AddRange(_frame);
                    }
                }
            }
        }

        public bool RunUntil(Func<bool> done, double seconds)
        {
            long end = Online.Now + (long)(seconds * 1_000_000);
            while (Online.Now < end)
            {
                Step();
                if (done())
                {
                    return true;
                }
            }

            return done();
        }

        public void Run(double seconds) => RunUntil(() => false, seconds);

        public string Describe() =>
            Online.Server.Status() + " | relay " + (Online.Server.VoiceRelay is { } r
                ? $"up={r.Stats.UpPackets} down={r.Stats.DownPackets} unrouted={r.Stats.Unrouted} roster={r.Roster.Count} unknown={r.Stats.UnknownSender}"
                : "none") + " | " + string.Join(" | ", Players.Select(p =>
                $"{p.Client.Settings.PlayerName}: {p.Client.State} map={p.Client.CurrentMap} voice={p.Voice.IsConnected} id={p.Voice.Voice.LocalTalker} pawn={p.Client.Session?.LocalPlayer?.Pawn is not null} names={string.Join("/", Enumerable.Range(1, 4).Select(i => p.Voice.NameOf((ushort)i) ?? "-"))} sent={p.Voice.Voice.Stats.FramesSent} got={p.Voice.Voice.Stats.PacketsReceived} heard={string.Join(",", p.Heard.Select(h => h.Key + ":" + h.Value.Count))}"));

        public void Dispose()
        {
            foreach (Player p in Players)
            {
                p.Voice.Dispose();
            }

            Online.Dispose();
        }
    }

    private static bool TestBoxMissing => WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null;

    private static OnlineServerSettings ListenSettings(Func<LobbySetup, LobbySetup>? setup = null)
    {
        OnlineServerSettings s = OnlineRig.Settings(MapCatalog.TestBox, timeLimit: 1) with { Dedicated = false, HostKey = "host-key" };
        return s with { Setup = setup is null ? s.Setup : setup(s.Setup) };
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

    private static double ZeroCrossingHz(List<float> samples, int skip)
    {
        int crossings = 0;
        for (int i = skip + 1; i < samples.Count; i++)
        {
            if ((samples[i - 1] < 0f) != (samples[i] < 0f))
            {
                crossings++;
            }
        }

        double seconds = (samples.Count - skip) / (double)VoiceK.SampleRate;
        return crossings / 2.0 / seconds;
    }

    [Fact]
    public void LobbyVoiceReachesTheHostAtTheVolumeHeSetsForThatPlayer()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        using var rig = new Rig(ListenSettings());
        Player host = rig.Join("Host", 0, "host-key");
        Player guest = rig.Join("Guest", 1);
        Assert.True(rig.RunUntil(() => host.Client.State == OnlineClientState.Lobby && guest.Client.State == OnlineClientState.Lobby &&
            host.Voice.TalkerOf("Guest") is not null && guest.Voice.Voice.LocalTalker != 0, 15), rig.Describe());

        // The guest speaks (the default activation gate opens on the tone): the host hears 440 Hz from "Guest".
        guest.Talking = true;
        Assert.True(rig.RunUntil(() => host.HeardFrom("Guest") > 100 * VoiceK.FrameSamples, 10), rig.Describe());
        List<float> full = host.Heard["Guest"];
        double fullRms = Rms(full, full.Count / 4);
        Assert.InRange(ZeroCrossingHz(full, full.Count / 4), ToneHz - 15, ToneHz + 15);
        Assert.InRange(fullRms, 0.15, 0.3);
        Assert.True(host.Voice.IsSpeaking("Guest"));
        Assert.Equal(0, guest.HeardFrom("Guest"));

        // The host turns the guest down to 40%: what he hears follows; nothing changes for anyone else.
        host.Voice.SetVolume("Guest", 0.4f);
        host.Clear();
        Assert.True(rig.RunUntil(() => host.HeardFrom("Guest") > 100 * VoiceK.FrameSamples, 10), rig.Describe());
        double quiet = Rms(host.Heard["Guest"], host.Heard["Guest"].Count / 4);
        Assert.InRange(quiet / fullRms, 0.36, 0.44);

        // Muted: the server stops sending him to the host.
        host.Voice.SetMuted("Guest", true);
        rig.Run(1);
        host.Clear();
        long before = host.Voice.Voice.Stats.PacketsReceived;
        rig.Run(2);
        Assert.Equal(0, host.HeardFrom("Guest"));
        Assert.True(host.Voice.Voice.Stats.PacketsReceived - before < 5, rig.Describe());
    }

    [Fact]
    public void VoiceInLobbyOffAndRestrictMicToTeamKeepTheLobbyQuiet()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        using var rig = new Rig(ListenSettings(s => s with { VoiceInLobby = false }));
        Player host = rig.Join("Host", 0, "host-key");
        Player mate = rig.Join("Mate", 0);
        Player enemy = rig.Join("Enemy", 1);
        Assert.True(rig.RunUntil(() => rig.Players.All(p => p.Client.State == OnlineClientState.Lobby && p.Voice.Voice.LocalTalker != 0) &&
            host.Voice.TalkerOf("Enemy") is not null, 15), rig.Describe());
        Assert.False(host.Client.Lobby.Setup.VoiceInLobby);

        // "Voice in Lobby" off: nobody hears anybody in the lobby.
        enemy.Talking = true;
        rig.Run(3);
        Assert.Equal(0, host.HeardFrom("Enemy") + mate.HeardFrom("Enemy"));

        // The host turns it on from the lobby: the lobby hears everyone, whatever the side (voice-chat.md rule 5).
        host.Client.SetSetup(host.Client.Lobby.Setup with { VoiceInLobby = true });
        Assert.True(rig.RunUntil(() => host.HeardFrom("Enemy") > 50 * VoiceK.FrameSamples && mate.HeardFrom("Enemy") > 50 * VoiceK.FrameSamples, 10),
            rig.Describe());

        // The enemy restricts his mic to his team: the other side stops hearing him; the flag reaches every lobby.
        enemy.Client.SetTeamVoiceOnly(true);
        Assert.True(rig.RunUntil(() => host.Client.Lobby.Players.Any(p => p.Name == "Enemy" && p.TeamVoiceOnly), 5), rig.Describe());
        rig.Run(1);
        host.Clear();
        mate.Clear();
        rig.Run(2);
        Assert.Equal(0, host.HeardFrom("Enemy") + mate.HeardFrom("Enemy"));

        // A mate on his side still hears him.
        mate.Client.SetTeam(1);
        Assert.True(rig.RunUntil(() => mate.HeardFrom("Enemy") > 50 * VoiceK.FrameSamples, 10), rig.Describe());
        Assert.Equal(0, host.HeardFrom("Enemy"));
    }

    [Fact]
    public void TheRoundCarriesVoiceByTheHostsModeAndKeepsTheVolumes()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        using var rig = new Rig(ListenSettings());
        Player host = rig.Join("Host", 0, "host-key");
        Player mate = rig.Join("Mate", 0);
        Player enemy = rig.Join("Enemy", 1);
        Assert.True(rig.RunUntil(() => rig.Players.All(p => p.Client.State == OnlineClientState.Lobby), 15), rig.Describe());

        // Modern (the default): in the round the enemy is heard by nobody on the other side, his mate is.
        host.Voice.SetVolume("Mate", 0.5f);
        mate.Client.SetReady(true);
        enemy.Client.SetReady(true);
        Assert.True(rig.RunUntil(() => mate.Client.Lobby.Players.Count(p => p.Ready) >= 2, 5), rig.Describe());
        host.Client.Launch();
        Assert.True(rig.RunUntil(() => rig.Players.All(p => p.Client.State == OnlineClientState.Match && p.Client.Session?.LocalPlayer?.Pawn is not null &&
            p.Voice.Voice.LocalTalker != 0) && host.Voice.TalkerOf("Mate") is not null, 40), rig.Describe());
        Assert.Equal(EVoiceChatMode.VCM_Modern, rig.Online.Server.Host.Game!.GameReplicationInfo!.VoiceChatMode);
        enemy.Talking = true;
        mate.Talking = true;
        Assert.True(rig.RunUntil(() => host.HeardFrom("Mate") > 100 * VoiceK.FrameSamples, 10), rig.Describe());
        Assert.Equal(0, host.HeardFrom("Enemy"));

        // The volume set in the lobby holds on the new map (the talker id changed with the map).
        double mateRms = Rms(host.Heard["Mate"], host.Heard["Mate"].Count / 4);
        Assert.InRange(mateRms, 0.06, 0.13);
        Assert.Equal(0.5f, host.Voice.VolumeOf("Mate"));
    }

    [Fact]
    public void OpenModeLetsEveryoneHearEveryoneInTheRound()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        using var rig = new Rig(ListenSettings(s => s with { VoiceMode = EVoiceChatMode.VCM_Open }));
        Player host = rig.Join("Host", 0, "host-key");
        Player enemy = rig.Join("Enemy", 1);
        Assert.True(rig.RunUntil(() => rig.Players.All(p => p.Client.State == OnlineClientState.Lobby), 15), rig.Describe());
        Assert.Equal(EVoiceChatMode.VCM_Open, enemy.Client.Lobby.Setup.VoiceMode);
        enemy.Client.SetReady(true);
        Assert.True(rig.RunUntil(() => host.Client.Lobby.Players.Any(p => p.Name == "Enemy" && p.Ready), 5), rig.Describe());
        host.Client.Launch();
        Assert.True(rig.RunUntil(() => rig.Players.All(p => p.Client.State == OnlineClientState.Match && p.Voice.Voice.LocalTalker != 0) &&
            host.Voice.TalkerOf("Enemy") is not null, 40), rig.Describe());
        Assert.Equal(EVoiceChatMode.VCM_Open, rig.Online.Server.Host.Game!.GameReplicationInfo!.VoiceChatMode);
        enemy.Talking = true;
        Assert.True(rig.RunUntil(() => host.HeardFrom("Enemy") > 100 * VoiceK.FrameSamples, 10), rig.Describe());
        Assert.Equal(SessionPhase.Match, rig.Online.Server.Phase);
    }
}
