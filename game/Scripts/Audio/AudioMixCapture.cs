using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace OpenScctGame.Audio;

/// <summary>Bounded, opt-in PCM evidence from an audio bus before Master is muted by unattended tests.</summary>
public sealed class AudioMixCapture : IDisposable
{
    private readonly int _bus;
    private readonly AudioEffectCapture _effect = new() { BufferLength = 2f };
    private readonly List<Vector2> _frames = [];
    private readonly int _limit = (int)(AudioServer.GetMixRate() * 15);
    public AudioMixCapture(string bus)
    {
        _bus = AudioServer.GetBusIndex(bus);
        if (_bus < 0) throw new ArgumentException("Audio bus is missing: " + bus, nameof(bus));
        AudioServer.AddBusEffect(_bus, _effect);
    }

    public void Reset()
    {
        _frames.Clear();
        _effect.ClearBuffer();
    }

    public void Sample()
    {
        int count = _effect.GetFramesAvailable();
        if (count <= 0) return;
        foreach (Vector2 frame in _effect.GetBuffer(count))
            if (_frames.Count < _limit) _frames.Add(frame);
    }

    public double Energy()
    {
        double sum = 0;
        foreach (Vector2 frame in _frames) sum += (double)frame.X * frame.X + (double)frame.Y * frame.Y;
        return sum;
    }

    public object Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = new BinaryWriter(File.Create(path));
        int bytes = _frames.Count * 2 * sizeof(float);
        output.Write("RIFF"u8); output.Write(bytes + 36); output.Write("WAVEfmt "u8);
        output.Write(16); output.Write((ushort)3); output.Write((ushort)2);
        output.Write((int)AudioServer.GetMixRate()); output.Write((int)AudioServer.GetMixRate() * 8);
        output.Write((ushort)8); output.Write((ushort)32); output.Write("data"u8); output.Write(bytes);
        float peak = 0;
        foreach (Vector2 frame in _frames)
        {
            output.Write(frame.X); output.Write(frame.Y);
            peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
        }
        return new { frames = _frames.Count, rate = AudioServer.GetMixRate(), energy = Energy(), peak,
            discarded = _effect.GetDiscardedFrames(), path };
    }

    public void Dispose()
    {
        for (int i = AudioServer.GetBusEffectCount(_bus) - 1; i >= 0; i--)
            if (AudioServer.GetBusEffect(_bus, i) == _effect) AudioServer.RemoveBusEffect(_bus, i);
        _effect.Dispose();
    }
}
