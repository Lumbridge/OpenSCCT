using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Net.Voice;
using OpenScct.Server.Hub;
using OpenScct.Server.Online;
using OpenScctGame.Hub;
using OpenScctGame.Match;
using OpenScctGame.Online;
using OpenScctGame.Voice;

namespace OpenScctGame.Smoke;

/// <summary>Native audio ownership across repeated online/hub visits, without a microphone or network server.</summary>
public sealed class VoiceLifecycleSmoke : SmokeTest
{
    public const string TestName = "voice-lifecycle";
    private const int Iterations = 16;
    private readonly StopwatchClock _clock = new();
    private readonly List<Output> _retired = new();
    private readonly byte[] _opus = new byte[VoiceK.MaxOpusPacketBytes];
    private Node? _fixture;
    private VoiceAudio? _direct;
    private OnlineVoiceChat? _online;
    private HubVoiceChat? _hub;
    private OnlineClient? _onlineClient;
    private HubClient? _hubClient;
    private ITransport? _server;
    private int _opusBytes, _iteration, _phase;
    private bool _collected;
    private string? _failure;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_phase == 0)
            {
                Build(context.Tree);
                _phase = 1;
                return SmokeResult.Running;
            }

            CheckRetired();
            if (_phase == 2)
            {
                Require(_fixture is null || !GodotObject.IsInstanceValid(_fixture), "the fixture survived queued deletion");
                if (_failure is not null) return SmokeResult.Fail(_failure);
                if (!_collected)
                {
                    _collected = true;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    return SmokeResult.Running;
                }
                if (context.ElapsedSeconds >= 15)
                    throw new InvalidOperationException($"the mixer did not release retired native resources: " +
                        $"playbacks={_retired.Count(output => Alive(output.PlaybackRef))}, " +
                        $"generators={_retired.Count(output => Alive(output.GeneratorRef))}");
                if (_retired.Count > 0) return SmokeResult.Running;
                // The runner quits immediately after this result: cover a live output's _ExitTree too.
                var shutdown = new VoiceAudio { Name = "ActiveVoiceAtShutdown", Client = NewVoice(), Pacing = VoicePlaybackPacing.Clock };
                context.Tree.Root.AddChild(shutdown);
                shutdown.SetProcess(false);
                Feed(shutdown, shutdown.Client!, 1).Dispose();
                return SmokeResult.Pass($"{Iterations} online, hub and direct audio visits; replacement, same-client assignment, repeated detach, native generators retained through mixing then released; active output at shutdown");
            }

            Exercise(_direct!, () => NewVoice(), voice => _direct!.Client = voice, () => _direct!.Client = null);
            Exercise(_online!.Audio!, () => { _online.Attach(_onlineClient!, _clock); return _online.Voice!.Voice; },
                _ => { }, _online.Detach);
            Exercise(_hub!.Audio!, () => { _hub.Attach(_hubClient!, _clock); return _hub.Voice!.Voice; },
                _ => { }, _hub.Detach);
            if (++_iteration == Iterations) BeginCleanup();
            return SmokeResult.Running;
        }
        catch (Exception exception)
        {
            _failure ??= exception.Message;
            if (_phase == 2) return SmokeResult.Fail(_failure);
            BeginCleanup();
            return SmokeResult.Running;
        }
    }

    private void Build(SceneTree tree)
    {
        _fixture = new Node { Name = "VoiceLifecycleFixture" };
        tree.Root.AddChild(_fixture);
        _direct = new VoiceAudio { Name = "DirectAudio" };
        _online = new OnlineVoiceChat { ReadKeys = false };
        _hub = new HubVoiceChat { ReadKeys = false };
        _fixture.AddChild(_direct);
        _fixture.AddChild(_online);
        _fixture.AddChild(_hub);
        // Exercise the audio boundary deterministically; ordinary scene frames still mix and delete native nodes.
        foreach (Node node in new Node[] { _direct, _online, _online.Audio!, _hub, _hub.Audio! }) node.SetProcess(false);
        foreach (VoiceAudio audio in new[] { _direct, _online.Audio!, _hub.Audio! }) audio.Pacing = VoicePlaybackPacing.Clock;
        var network = new LoopbackNetwork();
        _server = network.Bind("absent");
        _onlineClient = new OnlineClient(JoinTarget.Custom("voice fixture", () => network.Bind("online"), "absent"),
            new OnlineClientSettings { PlayerName = "Voice fixture" }, _clock.NowMicroseconds);
        _hubClient = new HubClient(network.Bind("hub"), "absent", _clock.NowMicroseconds, ownsTransport: true);
        var samples = new short[VoiceK.FrameSamples];
        for (int i = 0; i < samples.Length; i++) samples[i] = (short)(8000 * Math.Sin(i * 2 * Math.PI * 440 / VoiceK.SampleRate));
        _opusBytes = new VoiceEncoder().Encode(samples, _opus);
    }

    private VoiceClient NewVoice() => new(new VoiceSettings(), _clock);

    private void Exercise(VoiceAudio audio, Func<VoiceClient> attach, Action<VoiceClient> assign, Action detach)
    {
        // Force stop and wrapper disposal to precede the next mix. A fixed delay could miss the use-after-free.
        AudioServer.Lock();
        try { ExerciseLocked(audio, attach, assign, detach); }
        finally { AudioServer.Unlock(); }
    }

    private void ExerciseLocked(VoiceAudio audio, Func<VoiceClient> attach, Action<VoiceClient> assign, Action detach)
    {
        VoiceClient first = attach();
        assign(first);
        ushort talker = (ushort)(_iteration + 1);
        Output old = Feed(audio, first, talker);
        audio.Client = first;
        Require(!old.Player.IsQueuedForDeletion() && old.Player.Playing && old.Playback.NativeInstance != IntPtr.Zero,
            "assigning the same client reset active playback");

        VoiceClient replacement = attach();
        assign(replacement);
        first.Dispose();
        CheckReleased(old);
        long played = audio.PlayedFrames;
        audio._Process(0.04);
        Require(audio.PlayedFrames == played && ActivePlayers(audio).Length == 0, "replacement inherited the previous visit's audio");
        Output current = Feed(audio, replacement, talker);
        Require(current.Player.GetInstanceId() != old.Player.GetInstanceId(), "replacement reused the old talker's native output");
        detach();
        detach();
        replacement.Dispose();
        CheckReleased(current);
        played = audio.PlayedFrames;
        audio._Process(0.04);
        Require(audio.Client is null && audio.PlayedFrames == played && ActivePlayers(audio).Length == 0,
            "detached audio still has an active client or playback");
    }

    private Output Feed(VoiceAudio audio, VoiceClient client, ushort talker)
    {
        client.Settings.Enabled = true;
        client.SetVolume(talker, 1);
        VoiceTalker remote = client.FindTalker(talker)!;
        // Actual Opus decoding and generator writes; transport behavior has its own end-to-end smoke.
        for (ushort sequence = 0; sequence < 4; sequence++)
            remote.Buffer.Push(sequence, sequence == 0 ? EVoiceFlags.TalkStart : EVoiceFlags.None, EVoiceChannel.All, _opus.AsSpan(0, _opusBytes));
        long played = audio.PlayedFrames;
        audio._Process(0.04);
        AudioStreamPlayer[] players = ActivePlayers(audio);
        Require(audio.PlayedFrames > played && players.Length == 1, "encoded voice did not create one native output");
        AudioStreamPlayer player = players[0];
        return new Output(player, (AudioStreamGenerator)player.Stream, (AudioStreamGeneratorPlayback)player.GetStreamPlayback());
    }

    private static AudioStreamPlayer[] ActivePlayers(VoiceAudio audio) => audio.GetChildren().OfType<AudioStreamPlayer>()
        .Where(player => !player.IsQueuedForDeletion()).ToArray();

    private void CheckReleased(Output output)
    {
        Require(!output.Player.Playing && output.Player.Stream is null && output.Player.IsQueuedForDeletion(),
            "old talker player was not stopped and queued for deletion");
        Require(output.Playback.NativeInstance == IntPtr.Zero && output.Generator.NativeInstance == IntPtr.Zero,
            "old playback or generator managed wrapper was not disposed after detach");
        Require(Alive(output.PlaybackRef) && Alive(output.GeneratorRef),
            "the generator did not stay alive for the mixer's pending playback after wrapper disposal");
        _retired.Add(output);
    }

    private void CheckRetired()
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            Output output = _retired[i];
            Require(!GodotObject.IsInstanceValid(output.Player), "old talker player survived the next scene frame");
            bool playbackAlive = Alive(output.PlaybackRef);
            bool generatorAlive = Alive(output.GeneratorRef);
            Require(!playbackAlive || generatorAlive, "the mixer retained playback after its generator was freed");
            if (!playbackAlive && !generatorAlive)
            {
                output.Dispose();
                _retired.RemoveAt(i);
            }
        }
    }

    private void BeginCleanup()
    {
        _direct?.Client?.Dispose();
        if (_direct is not null) _direct.Client = null;
        _online?.Detach();
        _hub?.Detach();
        _onlineClient?.Dispose();
        _hubClient?.Dispose();
        _server?.Dispose();
        _fixture?.QueueFree();
        _phase = 2;
    }

    private static bool Alive(WeakRef reference)
    {
        // IsInstanceIdValid calls InstanceFromId in C# and creates an owning wrapper. Inspect the native Variant
        // without converting its object to C#, then release this temporary reference immediately.
        using Variant value = reference.GetRef();
        return value.VariantType != Variant.Type.Nil;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Output(AudioStreamPlayer Player, AudioStreamGenerator Generator, AudioStreamGeneratorPlayback Playback) : IDisposable
    {
        public WeakRef GeneratorRef { get; } = GodotObject.WeakRef(Generator)!;
        public WeakRef PlaybackRef { get; } = GodotObject.WeakRef(Playback)!;
        public void Dispose() { GeneratorRef.Dispose(); PlaybackRef.Dispose(); }
    }
}
