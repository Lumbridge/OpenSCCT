using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenScct.Net.Voice;

/// <summary>Local per-player listening volumes, shared across visits and keyed by case-insensitive player name.</summary>
public sealed class VoiceVolumes
{
    private readonly Dictionary<string, float> _volumes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised only when a player's volume changes, so the front end can save it.</summary>
    public event Action? Changed;

    /// <summary>A player's listening volume, from 0 to 2; defaults to 1.</summary>
    public float VolumeOf(string playerName) => _volumes.GetValueOrDefault(playerName ?? string.Empty, 1f);

    public void SetVolume(string playerName, float volume)
    {
        ArgumentNullException.ThrowIfNull(playerName);
        if (string.IsNullOrWhiteSpace(playerName)) return;
        volume = float.IsFinite(volume) ? Math.Clamp(volume, 0f, 2f) : 1f;
        if (VolumeOf(playerName) == volume) return;
        if (volume == 1f)
            _volumes.Remove(playerName);
        else
            _volumes[playerName] = volume;
        Changed?.Invoke();
    }

    /// <summary>Loads saved volumes; a missing file starts at the default volume for every player.</summary>
    public static VoiceVolumes Load(string path)
    {
        var volumes = new VoiceVolumes();
        if (File.Exists(path))
        {
            Dictionary<string, float>? saved = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(path));
            if (saved is not null)
                foreach ((string name, float volume) in saved)
                    volumes.SetVolume(name, volume);
        }

        return volumes;
    }

    /// <summary>Replaces the saved file only after all volumes have been written successfully.</summary>
    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_volumes));
        File.Move(temporary, path, overwrite: true);
    }
}
