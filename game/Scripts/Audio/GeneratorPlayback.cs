using System;
using Godot;

namespace OpenScctGame.Audio;

/// <summary>Gets generator playback with native ownership that lasts through the mixer's asynchronous stop.</summary>
public static class GeneratorPlayback
{
    public static AudioStreamGeneratorPlayback Get(AudioStreamPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Stream is not AudioStreamGenerator generator)
            throw new ArgumentException("The player must have a generator stream.", nameof(player));

        var playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
        // Godot 4.7.2's playback keeps a raw generator pointer. Stop() queues a final fade mix, so clearing the
        // player's stream can otherwise destroy the generator while the audio thread still reads its sample rate.
        // A native Variant reference follows playback through mixer retirement, even after C# wrappers and the
        // scene tree are disposed. The generator has no reference back to playback, so this does not form a cycle.
        using Variant owner = Variant.From(generator);
        playback.SetMeta("_openscct_generator", owner);
        return playback;
    }
}
