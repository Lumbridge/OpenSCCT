using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Audio;

/// <summary>Exercise actual mixer voice ownership, wildcard stops and allocation under a full source pool.</summary>
public static class AudioPlaybackChecks
{
    private const string Clip = "Sound_Persos.onomatopees.breath_masqGaz";

    public static string? Verify(Node parent, AudioLibrary library)
    {
        if (VerifyBankOnlyStream(library) is { } bankProblem) return bankProblem;
        var sound = new SoundSystem { LocalPawnId = 1, LocalControllerId = 10 };
        parent.AddChild(sound);
        sound.SetProcess(false);
        // No map ambients: isolate source ownership from whatever map the smoke loaded.
        sound.Bind(library, null);
        sound.StopAll();
        bool Play(int actor, int slot = 0, bool keep = false) =>
            sound.PlaySound(actor, Clip, slot, keep, default, 0.5f, 80, 1);
        try
        {
            if (!Play(1) || !Play(2) || !Play(10)) return "stop fixture did not start all three sources";
            sound.LocalPawnId = 3;
            sound.LocalControllerId = 30;
            sound.StopSound(10, null);
            if (sound.CaptureVoices() is not [{ ActorId: 2 }])
                return "controller wildcard did not stop its original listener-bound source after possession changed";
            sound.StopSound(99, null);
            if (sound.ActiveVoices != 1) return "actor wildcard stopped another actor";
            if (!Play(2)) return "second source did not start";
            sound.Handle(new SimEvent { Kind = SimEventKind.StopSound, ActorId = 2, What = default });
            if (sound.ActiveVoices != 0) return "None event did not stop all sounds on its actor";

            if (!Play(2) || sound.PlayMusic(Clip, 0) == 0) return "global stop fixture did not start";
            sound.StopSound(0, "None");
            if (sound.ActiveVoices != 1 || sound.CaptureVoices().Single().ActorId != 0)
                return "global wildcard must preserve music";
            sound.StopSound(0, Clip);
            if (sound.ActiveVoices != 0) return "global named stop did not stop its matching music";

            if (!Play(2, 1)) return "contention fixture did not start its slotted source";
            for (int i = 0; i < AudioK.Voices - 1; i++)
                if (!Play(100 + i)) return "contention fixture could not fill all 32 voices";
            if (sound.ActiveVoices != AudioK.Voices) return "contention fixture did not fill 32 sources";
            if (Play(2, 1, keep: true)) return "bNoOverride replaced a busy source with the same slot";
            if (!Play(2, 1)) return "Misc slot did not replace itself at full voice count";
            if (sound.ActiveVoices != AudioK.Voices || sound.CaptureVoices().Count(v => v.ActorId == 2) != 1)
                return "slotted replacement changed source count or duplicated the actor";
            if (Play(999)) return "SLOT_None replaced an equal-priority full-pool source";
            return VerifyDemoSeek(sound, library);
        }
        finally
        {
            sound.StopAll();
            sound.QueueFree();
        }
    }

    private static string? VerifyDemoSeek(SoundSystem sound, AudioLibrary library)
    {
        static SimEvent Call(int actor) => new()
        {
            Kind = SimEventKind.PlaySound, ActorId = actor, What = new Name(Clip),
            Volume = 1f, Radius = 100f, Pitch = 1f, Flat = true,
        };

        if (library.Find(Clip) is not { Duration: > 1.5f } clip)
            return "demo seek fixture requires a sound longer than 1.5 seconds";
        sound.StopAll();
        long starts = sound.StartCount;
        sound.SetDemoSeek(true);
        sound.OnSimEvents(1, [Call(1)]);
        sound.AdvanceDemoTick(0);
        sound.AdvanceDemoVoices(clip.Duration + 0.1);
        sound.Handle(Call(2));
        sound.AdvanceDemoVoices(1.0);
        AudioStreamPlayer[] players = sound.GetChildren().OfType<AudioStreamPlayer>().ToArray();
        if (sound.StartCount != starts || players.Any(player => player.Playing))
            return "demo seeking started an audible voice while processing historical events";
        if (sound.CaptureVoices() is not [{ ActorId: 2 }])
            return "demo seeking retained an expired voice or dropped the surviving voice";

        sound.SetDemoSeek(false, paused: true);
        if (players.Any(player => player.Playing && !player.StreamPaused))
            return "a paused demo seek played the surviving voice";
        sound.SetDemoSeek(false);
        AudioStreamPlayer[] audible = players.Where(player => player.Playing && !player.StreamPaused).ToArray();
        if (audible.Length != 1 || sound.CaptureVoices() is not [{ ActorId: 2 }])
            return "resuming a demo seek should play only the surviving voice";
        double position = audible[0].GetPlaybackPosition();
        return Math.Abs(position - 1.0) <= 0.2 ? null : $"resumed demo voice started at {position:F3}s instead of 1s";
    }

    private static string? VerifyBankOnlyStream(AudioLibrary original)
    {
        string? ogg = original.MapDir is { } map ? Directory.EnumerateFiles(map, "*.ogg", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (ogg is null) return "map bank has no Ogg fixture for self-contained stream playback";
        string folder = Path.Combine(Path.GetTempPath(), "openscct-bank-audio-" + Guid.NewGuid().ToString("N"));
        string shared = Path.Combine(folder, "shared"), mapFolder = Path.Combine(folder, "map"), audio = Path.Combine(mapFolder, "audio");
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(audio);
        AudioLibrary? bank = null;
        try
        {
            File.WriteAllText(Path.Combine(shared, "audio.json"), "{\"format\":\"svm.audio/1\",\"sounds\":{}}");
            File.WriteAllText(Path.Combine(audio, "audio.json"), JsonSerializer.Serialize(new
            {
                format = "svm.mapaudio/1", actors = Array.Empty<object>(), zones = Array.Empty<object>(),
                sounds = new Dictionary<string, object>
                {
                    ["Audit.BankOnly"] = new { kind = "stream", flags = AudioK.FlagStream | AudioK.Flag2D, volume = 0.5f,
                        radius = 64f, pitch = 1f, loop = false, positional = false, file = ogg, root = "map", duration = 1f },
                },
            }));
            bank = AudioLibrary.Load(shared, mapFolder);
            if (bank.Find("Audit.BankOnly") is not { Flags: AudioK.FlagStream | AudioK.Flag2D, Volume: 0.5f, Radius: 64f,
                Positional: false, File: not null } sound || bank.Stream(sound, false) is null)
                return "self-contained map stream did not retain playback metadata and decode without shared definition";
            return null;
        }
        finally
        {
            bank?.ReleaseStreams();
            File.Delete(Path.Combine(shared, "audio.json"));
            File.Delete(Path.Combine(audio, "audio.json"));
            Directory.Delete(audio);
            Directory.Delete(mapFolder);
            Directory.Delete(shared);
            Directory.Delete(folder);
        }
    }
}
