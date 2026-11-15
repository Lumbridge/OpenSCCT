using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>Capture the actual AquaE mixer at the reported clipping position, with physical output muted.</summary>
public sealed class AquaAudioRegressionSmoke : SmokeTest
{
    public const string TestName = "aqua-audio-regression";
    public override string Name => TestName;
    private PlayRoot? _root;
    private readonly Dictionary<string, AudioMixCapture> _captures = new();
    private readonly List<object> _rows = [];
    private string _folder = "";
    private double _began = -1, _sampleAt;
    private int _leg = -1;
    private AudioEffectHardLimiter? _outputGuard;
    private int Seconds => int.TryParse(System.Environment.GetEnvironmentVariable("SVM_AQUA_AUDIO_SECONDS"), out int seconds) ? seconds : 540;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioServer.SetBusMute(0, false);
        AudioServer.SetBusVolumeDb(0, -80f);
        if (_root is null)
        {
            _folder = System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ?? Path.GetFullPath("../local/aqua-audio-regression");
            Directory.CreateDirectory(_folder);
            // Keep real mixing/capture active without audible unattended output, including a runaway DSP.
            _outputGuard = new AudioEffectHardLimiter { PreGainDb = 0, CeilingDb = -6 };
            AudioServer.AddBusEffect(0, _outputGuard);
            var launch = PlayConfig.FromOptions(context.App.Options);
            _root = PlayRoot.Create(launch with
            {
                Settings = launch.Settings with { Map = "AquaE", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Inline, Bots = 0 }, Render = true, Hud = false, Audio = true, TicksPerFrame = 0, Pilot = null,
            });
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }
        if (_root.LoadError is { } error) return Finish(SmokeResult.Fail(error));
        if (_root.Session is not { LocalPawn: { }, ServerController: { Pawn: SBasePawn pawn } controller } ||
            _root.Audio?.Sound is not { Buses: { } buses } sound) return SmokeResult.Running;
        if (_root.Controls is { } controls) { controls.ReleasePointer(); controls.Enabled = false; }
        if (_began < 0) _began = context.ElapsedSeconds;
        double elapsed = context.ElapsedSeconds - _began;
        int leg = elapsed < 5 ? 0 : elapsed < Seconds - 25 ? 1 : 2;
        if (_leg != leg)
        {
            _leg = leg;
            FVector location = leg == 0 ? new(2625.0415f, -79.413574f, 217.005f) : new(7368.3413f, 3708.7278f, 217.005f);
            pawn.SetLocation(location); pawn.Velocity = pawn.Acceleration = default;
            pawn.Rotation = new FRotator(0, -24927, 0); controller.SetViewRotation(new FRotator(-941, -24927, 0));
            pawn.ResetNetworkHistory();
            // Exercise both spatializer paths, preserving the reported position for most of the run.
            sound.Headphones = leg != 2;
        }
        foreach (string? bus in new[] { ReverbBuses.WorldBus, buses.EnvironmentBus }.Concat(buses.CaptureSourceRooms().Select(r => r.Bus)))
            if (bus is not null && !_captures.ContainsKey(bus))
            {
                _captures[bus] = new AudioMixCapture(bus);
                if (bus.StartsWith("SvmSourceRoom", StringComparison.Ordinal))
                {
                    _captures[bus + "-input"] = new AudioMixCapture(bus, 0);
                    _captures[bus + "-wet"] = new AudioMixCapture(bus, 2);
                }
            }
        foreach (var capture in _captures.Values) capture.Sample();
        if (elapsed < _sampleAt) return SmokeResult.Running;
        _sampleAt = elapsed + (System.Environment.GetEnvironmentVariable("SVM_AQUA_AUDIO_DETAIL") == "1" ? .1 : 1);
        bool invalid = false;
        var levels = new Dictionary<string, double>();
        foreach (var (bus, capture) in _captures)
        {
            double peak = 0;
            foreach (Vector2 frame in capture.Samples)
            {
                if (!float.IsFinite(frame.X) || !float.IsFinite(frame.Y)) invalid = true;
                peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
            }
            levels[bus] = double.IsFinite(peak) ? peak : double.MaxValue;
            if (peak > 16) invalid = true;
        }
        _rows.Add(new { elapsed, leg, sound.Headphones, sound.ListenerZone, levels,
            frames = _captures.ToDictionary(p => p.Key, p => p.Value.Frames),
            voices = sound.CaptureVoices(), eax = buses.CurrentEnvironment?.ToEaxArray(),
            packets = buses.CaptureEnvironmentPackets(),
            geometry = buses.CaptureGeometry(), rooms = buses.CaptureSourceRooms() });
        File.WriteAllText(Path.Combine(_folder, "aqua-levels.json"), JsonSerializer.Serialize(_rows,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        if (invalid) return Finish(SmokeResult.Fail("Nonfinite or runaway PCM in AquaE; captured exact voices/environments in " + _folder));
        if (elapsed > 5 && _captures.Values.All(c => c.Frames == 0)) return Finish(SmokeResult.Fail("Audio mixer did not deliver PCM; no stability verdict is possible."));
        if (elapsed >= Seconds) return Finish(SmokeResult.Pass($"{Seconds}s actual AquaE PCM at reported position; finite bounded source/reverb output; {_folder}"));
        foreach (var capture in _captures.Values) capture.Reset();
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        foreach (var (bus, capture) in _captures)
        {
            capture.Save(Path.Combine(_folder, bus + ".wav")); capture.Dispose();
        }
        _root?.QueueFree();
        return result;
    }
}
