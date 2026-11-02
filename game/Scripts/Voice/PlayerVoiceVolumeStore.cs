using System;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Net.Voice;
using OpenScctGame.Options;

namespace OpenScctGame.Voice;

/// <summary>Per-player volumes saved separately from options, which edit and replace settings snapshots.</summary>
public static class PlayerVoiceVolumeStore
{
    public const string FilePath = "user://voice-volumes.json";

    private static VoiceVolumes? _current;

    /// <summary>Smoke clients have independent, unsaved volumes and never read the player's preferences.</summary>
    public static VoiceVolumes Current => SettingsStore.Isolated ? new VoiceVolumes() : _current ??= Load();

    private static VoiceVolumes Load()
    {
        string path = ProjectSettings.GlobalizePath(FilePath);
        VoiceVolumes volumes;
        try
        {
            volumes = VoiceVolumes.Load(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            GD.PushWarning($"Could not load {FilePath}: {e.Message}");
            volumes = new VoiceVolumes();
        }

        volumes.Changed += () =>
        {
            if (SettingsStore.Isolated) return;
            try
            {
                volumes.Save(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"Could not save {FilePath}: {e.Message}");
            }
        };
        return volumes;
    }
}
