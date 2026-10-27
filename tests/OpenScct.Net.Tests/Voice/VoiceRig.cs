using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Net.Voice;

namespace OpenScct.Net.Tests.Voice;

/// <summary>
/// A voice-only server and clients over SimLink: a <see cref="VoiceRelay"/> with a <see cref="StaticVoiceDirectory"/>
/// and one <see cref="VoiceClient"/> per player, each on its own <see cref="VoiceTransport"/>. Steps in 20 ms frames
/// of a virtual clock; a talking player's microphone plays a tone, and every client plays out its talkers each frame.
/// </summary>
internal sealed class VoiceRig : IDisposable
{
    internal sealed class Player
    {
        public required ushort Id;
        public required VoiceClient Client;
        public required VoiceTransport? Transport;
        public float[] Microphone = [];
        public int MicrophoneAt;
        public bool Talking;

        /// <summary>The microphone's sample rate (the audio server's mix rate in the game).</summary>
        public int CaptureRate = VoiceK.SampleRate;

        /// <summary>Everything played out, per talker.</summary>
        public readonly Dictionary<ushort, List<float>> Heard = new();

        public int FramesFrom(ushort talker) => Heard.TryGetValue(talker, out List<float>? s) ? s.Count / VoiceK.FrameSamples : 0;

        public float[] From(ushort talker) => Heard.TryGetValue(talker, out List<float>? s) ? s.ToArray() : [];
    }

    private readonly float[] _frame = new float[VoiceK.FrameSamples];

    public VoiceRig(SimLinkProfile profile, ulong seed = 1, EVoiceChatMode mode = EVoiceChatMode.VCM_Modern)
    {
        Network = new SimLinkNetwork(Clock, seed, profile);
        ServerTransport = new VoiceTransport(Network.Bind("server"));
        Directory.Rules = new VoiceRules(mode, false, true);
        Relay = new VoiceRelay(ServerTransport, Directory, Clock);
    }

    public readonly VirtualClock Clock = new();
    public readonly SimLinkNetwork Network;
    public readonly VoiceTransport ServerTransport;
    public readonly StaticVoiceDirectory Directory = new();
    public readonly VoiceRelay Relay;
    public readonly List<Player> Players = new();

    public Player this[ushort id] => Players.Find(p => p.Id == id) ?? throw new ArgumentException($"No player {id}.");

    /// <summary>A player on <paramref name="team"/> talking over the network.</summary>
    public Player Add(ushort id, int team, VoiceSettings? settings = null, EVoiceLife life = EVoiceLife.Alive)
    {
        Directory.Set(VoiceMember.Create(id, team, life));
        var transport = new VoiceTransport(Network.Bind("player" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        EndpointId server = transport.Connect("server");
        var client = new VoiceClient(settings ?? new VoiceSettings { PushToTalk = true }, Clock) { LocalTalker = id };
        client.Connect(transport, server);
        var player = new Player { Id = id, Client = client, Transport = transport };
        Players.Add(player);
        return player;
    }

    /// <summary>A player in the server's process (a listen host's own player) on a local link.</summary>
    public Player AddLocal(ushort id, int team, VoiceSettings? settings = null)
    {
        Directory.Set(VoiceMember.Create(id, team));
        var link = new VoiceLocalLink();
        Relay.AttachLocal(link);
        var client = new VoiceClient(settings ?? new VoiceSettings { PushToTalk = true }, Clock) { LocalTalker = id };
        client.Connect(link);
        var player = new Player { Id = id, Client = client, Transport = null };
        Players.Add(player);
        return player;
    }

    /// <summary>Starts <paramref name="id"/> talking (push to talk held) with a tone or the given samples.</summary>
    public void Talk(ushort id, float[]? microphone = null, EVoiceChannel channel = EVoiceChannel.All)
    {
        Player p = this[id];
        p.Microphone = microphone ?? VoiceSignals.Tone(440, 10.0);
        p.MicrophoneAt = 0;
        p.Talking = true;
        p.Client.TalkKey = channel == EVoiceChannel.All;
        p.Client.TeamKey = channel == EVoiceChannel.Team;
    }

    public void Silence(ushort id)
    {
        Player p = this[id];
        p.Talking = false;
        p.Client.TalkKey = false;
        p.Client.TeamKey = false;
    }

    /// <summary>Runs <paramref name="frames"/> frames of 20 ms.</summary>
    public void Step(int frames = 1)
    {
        for (int f = 0; f < frames; f++)
        {
            Clock.Advance(VoiceK.FrameMilliseconds * 1000L);
            foreach (Player p in Players)
            {
                p.Client.Update();
                if (p.Microphone.Length > 0)
                {
                    int chunk = p.CaptureRate / VoiceK.FramesPerSecond;
                    int at = p.MicrophoneAt % (p.Microphone.Length - chunk);
                    ReadOnlySpan<float> mic = p.Talking ? p.Microphone.AsSpan(at, chunk) : new float[chunk];
                    p.Client.SubmitCapture(mic);
                    p.MicrophoneAt += chunk;
                }
            }

            ServerTransport.DrainVoiceOnly();
            Relay.Tick();
            foreach (Player p in Players)
            {
                p.Transport?.DrainVoiceOnly();
                p.Client.Update();
                foreach (VoiceTalker talker in p.Client.Talkers)
                {
                    if (talker.ReadFrame(_frame, p.Client.Settings.OutputVolume))
                    {
                        if (!p.Heard.TryGetValue(talker.Id, out List<float>? heard))
                        {
                            heard = new List<float>();
                            p.Heard.Add(talker.Id, heard);
                        }

                        heard.AddRange(_frame);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (Player p in Players)
        {
            p.Client.Dispose();
            p.Transport?.Dispose();
        }

        ServerTransport.Dispose();
    }
}
