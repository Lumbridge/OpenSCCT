using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using OpenScctGame.Options;

namespace OpenScctGame.Online;

/// <summary>
/// The settings record that follows a signed-in player between PCs (the master keeps it as opaque text): JSON
/// <c>{"version":1, "settings": {...}, "keys_spy": "...", "keys_merc": "..."}</c>, where <c>settings</c> is
/// <c>user://settings.json</c> without the fields that belong to one PC (<see cref="LocalKeys"/>: display, frame caps,
/// the microphone) and the two key fields are the text of <c>user://svm_keys_spy.ini</c> and
/// <c>user://svm_keys_merc.ini</c> (null when the file is missing). <see cref="AccountService"/> decides when to send
/// and when to take it; this class only builds and applies it (on the main thread).
/// </summary>
public static class ProfileSync
{
    public const int Version = 1;

    public const string SpyKeysFile = "user://svm_keys_spy.ini";

    public const string MercKeysFile = "user://svm_keys_merc.ini";

    /// <summary>The settings keys each PC keeps for itself (GameSettings' JSON names).</summary>
    public static readonly string[] LocalKeys =
    [
        "display_mode", "display_monitor", "resolution", "resolution_scale", "vsync", "fps_spy", "fps_merc", "voice_input_device",
    ];

    /// <summary>This PC's record, from the current settings and the key files.</summary>
    public static string Build()
    {
        JsonObject settings = JsonNode.Parse(SettingsStore.Current.ToJson())?.AsObject() ?? new JsonObject();
        foreach (string key in LocalKeys)
        {
            settings.Remove(key);
        }

        var record = new JsonObject
        {
            ["version"] = Version,
            ["settings"] = settings,
            ["keys_spy"] = ReadText(SpyKeysFile),
            ["keys_merc"] = ReadText(MercKeysFile),
        };
        return record.ToJsonString();
    }

    /// <summary>
    /// Takes a record from the master: its settings over the current ones (this PC's own fields kept), saved through
    /// <see cref="SettingsStore"/> and put into effect, and its key layouts written (a missing one leaves this PC's
    /// file as it is). False when the record cannot be read. The play scene reads the key files at each round's start.
    /// </summary>
    public static bool Apply(string data)
    {
        JsonObject record;
        try
        {
            record = JsonNode.Parse(data)?.AsObject() ?? throw new JsonException("empty record");
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            GD.PushWarning("Profile sync: the account's settings could not be read: " + e.Message);
            return false;
        }

        if (record["version"] is not JsonValue v || !v.TryGetValue(out int version) || version > Version)
        {
            GD.PushWarning("Profile sync: the account's settings come from a newer build; left alone.");
            return false;
        }

        if (record["settings"] is JsonObject remote)
        {
            JsonObject local = JsonNode.Parse(SettingsStore.Current.ToJson())?.AsObject() ?? new JsonObject();
            foreach (string key in LocalKeys)
            {
                remote.Remove(key);
                if (local[key] is { } value)
                {
                    remote[key] = value.DeepClone();
                }
            }

            try
            {
                GameSettings merged = GameSettings.FromJson(remote.ToJsonString());
                SettingsStore.Save(merged);
                SettingsApplier.ApplyAudio(merged);
            }
            catch (JsonException e)
            {
                GD.PushWarning("Profile sync: the account's settings could not be read: " + e.Message);
            }
        }

        WriteText(SpyKeysFile, record["keys_spy"]);
        WriteText(MercKeysFile, record["keys_merc"]);
        return true;
    }

    private static string? ReadText(string file)
    {
        string path = ProjectSettings.GlobalizePath(file);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Profile sync: could not read {file}: {e.Message}");
            return null;
        }
    }

    private static void WriteText(string file, JsonNode? value)
    {
        if (value is not JsonValue text || !text.TryGetValue(out string? contents) || contents is null)
        {
            return;
        }

        string path = ProjectSettings.GlobalizePath(file);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, contents);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Profile sync: could not write {file}: {e.Message}");
        }
    }
}
