using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server;

namespace OpenScctGame.Audio;

/// <summary>Audio segments recover their playback offset from replicated sequence clocks, including late joins.</summary>
public partial class AnimationSequenceAudio : Node3D
{
    public Level? World { get; set; }
    public AudioLibrary? Library { get; set; }
    private readonly Dictionary<string, AudioStreamPlayer3D> _players = new(StringComparer.Ordinal);
    public override void _Process(double delta)
    {
        if (World is null || Library is null || World.GetService<AnimationWorld>() is null) return;
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (AnimationController controller in World.AllActors<AnimationController>())
            foreach (var audio in controller.Audio())
            {
                active.Add(audio.Id);
                if (!_players.TryGetValue(audio.Id, out AudioStreamPlayer3D? player))
                {
                    SoundDef? sound = Library.Find(audio.Sound); if (sound is null || Library.Stream(sound, false) is not { } stream) continue;
                    player = new AudioStreamPlayer3D { Stream = stream, PitchScale = MathF.Max(.01f, audio.Rate), UnitSize = 1000, MaxDistance = 0 };
                    AddChild(player); _players.Add(audio.Id, player); player.Play(audio.Offset);
                }
                var actor = World.GetService<AnimationWorld>()?.Actor(audio.Target);
                player.Position = UeSpace.ToGodot(actor?.Location ?? controller.Location);
                if (player.Playing && Math.Abs(player.GetPlaybackPosition() - audio.Offset) > .25) player.Seek(audio.Offset);
            }
        foreach (string id in new List<string>(_players.Keys))
            if (!active.Contains(id)) { _players[id].QueueFree(); _players.Remove(id); }
    }
}
