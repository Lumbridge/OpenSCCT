using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.UI;

namespace OpenScctGame.Audio;

/// <summary>
/// The front end's sounds: the menu music and the lobby's join sound (docs/AUDIO.md "Front end").
/// <list type="bullet">
/// <item>Music: the original plays a menu level's LevelInfo Song when the level loads, and its menus live on two
/// levels: <c>Entry</c> (the main menu and the pages under it, Song Menu_music1) and <c>Lobby</c> (the lobby room,
/// Song Menu_music2, Enhanced's longer lobby track, enh:audio.lobby_music). The rewrite's menus are Godot pages, so
/// this node plays the same Song for the page on screen: the Lobby's in the lobby, the Entry's on the other menu
/// pages, none on the loading pages, the results and in a round (the round's map plays its own). A change of track
/// fades the old one out over the music's 2 s minimum, as PlayMusic does; the music bus carries the music volume.</item>
/// <item>Join: Reloaded's lobby plays a notification sound when a player joins (rl:qol.lobby, rl:audio.join). Which
/// sound it plays is not decoded; ours is the menus' message beep (Interface.Menu.bip_message), flat on the effects bus.</item>
/// </list>
/// The songs come from the converted Entry and Lobby maps (their audio tables and streams, the maps step); without
/// them the menus are silent.
/// </summary>
public partial class FrontEndAudio : Node
{
    public const string NodeName = "FrontEndAudio";

    /// <summary>The menu level of the main menu pages.</summary>
    public const string EntryMap = "Entry";

    /// <summary>The menu level of the lobby.</summary>
    public const string LobbyMap = "Lobby";

    /// <summary>The join notification (our choice: Reloaded's sound is not decoded).</summary>
    public const string JoinSound = "Interface.Menu.bip_message";

    private readonly Dictionary<string, AudioLibrary?> _libraries = new(StringComparer.OrdinalIgnoreCase);
    private AudioStreamPlayer? _music;
    private AudioStreamPlayer? _fading;
    private AudioStreamPlayer? _ui;
    private string? _track;
    private bool _quiet;
    private static FrontEndAudio? _pending;

    /// <summary>The menu level whose Song is playing, or null.</summary>
    public string? Track => _track;

    /// <summary>The node under the tree's root (made on first use).</summary>
    public static FrontEndAudio Get(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Root.GetNodeOrNull<FrontEndAudio>(NodeName) is { } existing)
        {
            return existing;
        }

        // Added once the root is free to take children (a page's _EnterTree runs while the root is busy).
        if (_pending is null || !IsInstanceValid(_pending))
        {
            _pending = new FrontEndAudio { Name = NodeName };
            tree.Root.CallDeferred(Node.MethodName.AddChild, _pending);
        }

        return _pending;
    }

    public override void _Ready()
    {
        // Servers, headless runs and muted test runs play nothing.
        _quiet = DisplayServer.GetName() == "headless" || System.Environment.GetEnvironmentVariable("SVM_AUDIO_MUTE") is "1" or "true";
        _ = new ReverbBuses(null);
        OpenScctGame.Options.SettingsApplier.ApplyAudio(OpenScctGame.Options.SettingsStore.Current);
        _music = new AudioStreamPlayer { Name = "Music", Bus = ReverbBuses.MusicBus };
        _fading = new AudioStreamPlayer { Name = "Fading", Bus = ReverbBuses.MusicBus };
        _ui = new AudioStreamPlayer { Name = "Ui", Bus = ReverbBuses.FlatBus };
        AddChild(_music);
        AddChild(_fading);
        AddChild(_ui);
    }

    public override void _Process(double delta)
    {
        if (_fading is { Playing: true } fading)
        {
            float db = fading.VolumeDb - (float)delta * 80f / AudioK.MusicMinFadeOut;
            if (db <= -80f)
            {
                fading.Stop();
            }
            else
            {
                fading.VolumeDb = db;
            }
        }

        string? wanted = _quiet ? null : TrackFor(GetTree().CurrentScene);
        if (!string.Equals(wanted, _track, StringComparison.OrdinalIgnoreCase))
        {
            Play(wanted);
        }
    }

    /// <summary>The menu level whose music goes with <paramref name="scene"/>, or null for silence.</summary>
    public static string? TrackFor(Node? scene) => scene switch
    {
        LobbyRoomScreen or LobbyScreen => LobbyMap,
        ConnectingScreen or LoadingScreen or ResultsScreen => null,
        Control => EntryMap,
        _ => null,
    };

    /// <summary>The lobby's join notification.</summary>
    public void PlayJoin()
    {
        if (_quiet || _ui is null || Library(EntryMap) is not { } library || library.Find(JoinSound) is not { } sound ||
            library.Stream(sound, loop: false) is not { } stream)
        {
            return;
        }

        _ui.Stream = stream;
        _ui.VolumeDb = Mathf.LinearToDb(Math.Clamp(sound.Volume, AudioK.MinVolume, 1f));
        _ui.Play();
    }

    private void Play(string? track)
    {
        _track = track;
        if (_music is null || _fading is null)
        {
            return;
        }

        // The old track fades out (PlayMusic's 2 s at least); the new one starts at once, as a map's Song does.
        if (_music.Playing)
        {
            _fading.Stop();
            _fading.Stream = _music.Stream;
            _fading.VolumeDb = _music.VolumeDb;
            _fading.Play(_music.GetPlaybackPosition());
            _music.Stop();
        }

        if (track is null || Library(track) is not { Map.LevelSong: { } song } library || library.Find(song) is not { } def ||
            library.Stream(def, loop: true) is not { } stream)
        {
            return;
        }

        // Music plays without the Sound's own gain, at the music volume (the bus).
        _music.Stream = stream;
        _music.VolumeDb = 0f;
        _music.Play();
    }

    private AudioLibrary? Library(string map)
    {
        if (_libraries.TryGetValue(map, out AudioLibrary? cached))
        {
            return cached;
        }

        AudioLibrary? library = null;
        try
        {
            if (OpenScctGame.Render.MapBuilder.FindMapFolder(map) is { } folder)
            {
                string shared = AudioLibrary.SharedDirFor(folder);
                if (System.IO.File.Exists(System.IO.Path.Combine(shared, "audio.json")))
                {
                    library = AudioLibrary.Load(shared, folder);
                }
            }
        }
        catch (Exception e) when (e is System.IO.IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            GD.PushWarning($"SVM_AUDIO the {map} menu level's sounds did not load: {e.Message}");
        }

        _libraries[map] = library;
        return library;
    }
}
