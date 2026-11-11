using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;
using Godot;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

/// <summary>
/// The player's settings file, <c>user://settings.json</c> (<see cref="GameSettings"/>), loaded once. The front end's
/// older <c>user://settings.cfg</c> (<see cref="UiSettings"/>: name, mouse, last address) is kept in step: a first run
/// takes the name and mouse values from it, and every save writes them back.
/// </summary>
public static class SettingsStore
{
    public const string FilePath = "user://settings.json";

    private static GameSettings? _current;
    private static IReadOnlyList<KeyValuePair<string, string>>? _overrides;
    private static bool _overridesReported;

    /// <summary>Raised after <see cref="Save"/> with the saved settings (the play scene applies them).</summary>
    public static event Action<GameSettings>? Saved;

    /// <summary>The file exists (the player saved settings once); display settings apply at start only then.</summary>
    public static bool Exists => System.IO.File.Exists(GlobalPath);

    public static string GlobalPath => ProjectSettings.GlobalizePath(FilePath);

    public static GameSettings Current => _current ??= LoadCurrent();

    /// <summary>
    /// True in smoke runs: the settings are the defaults, whatever the player saved, and <see cref="Save"/> writes no
    /// file, so tests do not depend on the player's settings or change them.
    /// </summary>
    public static bool Isolated { get; set; }

    /// <summary>
    /// The <c>--svm-setting=key=value</c> launch overrides (<see cref="LaunchOptions.SettingOverrides"/>). They apply
    /// to every load and save of this process; while there are any, <see cref="Save"/> writes no file.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Overrides =>
        _overrides ??= LaunchOptions.Parse(OS.GetCmdlineUserArgs()).SettingOverrides;

    private static GameSettings LoadCurrent()
    {
        if (Isolated)
        {
            var defaults = new GameSettings();
            defaults.Clamp();
            ApplyOverrides(defaults);
            return defaults;
        }

        bool existed = Exists;
        GameSettings s = GameSettings.Load(GlobalPath);
        if (!existed)
        {
            UiSettings ui = UiSettings.Current;
            s.PlayerName = ui.PlayerName;
            s.InvertY = ui.InvertMouse;
            s.Clamp();
        }

        ApplyOverrides(s);
        return s;
    }

    /// <summary>
    /// Sets the launch overrides on <paramref name="settings"/> in place, so callers that apply the object they saved
    /// see them too. The first call prints each override and reports a bad one as an error.
    /// </summary>
    private static void ApplyOverrides(GameSettings settings)
    {
        if (Overrides.Count == 0) return;
        bool report = !_overridesReported;
        _overridesReported = true;
        GameSettings result = settings;
        foreach ((string key, string value) in Overrides)
        {
            if (SettingsConsole.Set(result, key, value, out string problem) is { } next)
            {
                result = next;
                if (report) GD.Print($"Setting override for this run only: {key} = {SettingsConsole.Get(next, key)}");
            }
            else if (report)
            {
                GD.PushError($"--svm-setting={key}={value} ignored: {problem}");
            }
        }

        if (ReferenceEquals(result, settings)) return;
        foreach (PropertyInfo property in typeof(GameSettings).GetProperties())
        {
            if (property.SetMethod is not null && property.IsDefined(typeof(JsonPropertyNameAttribute), false))
                property.SetValue(settings, property.GetValue(result));
        }
    }

    /// <summary>Makes <paramref name="settings"/> the current ones, writes the file and tells the listeners.</summary>
    public static Error Save(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplyOverrides(settings);
        settings.Clamp();
        // Launch overrides are for this run only: keep the player's file as it is.
        if (Isolated || Overrides.Count > 0)
        {
            _current = settings;
            Saved?.Invoke(settings);
            return Error.Ok;
        }

        Error result = Error.Ok;
        try
        {
            settings.Save(GlobalPath);
        }
        catch (System.IO.IOException e)
        {
            GD.PushWarning($"Could not save {FilePath}: {e.Message}");
            result = Error.FileCantWrite;
        }
        catch (UnauthorizedAccessException e)
        {
            GD.PushWarning($"Could not save {FilePath}: {e.Message}");
            result = Error.FileNoPermission;
        }

        if (result != Error.Ok) return result;
        _current = settings;
        SettingsApplier.WriteStartOverrides(settings);
        UiSettings ui = UiSettings.Current;
        ui.PlayerName = settings.PlayerName;
        ui.InvertMouse = settings.InvertY;
        ui.Save();
        Saved?.Invoke(settings);
        return result;
    }

    /// <summary>
    /// Writes the current settings after a key toggled one value during play. Listeners are not told: nothing else
    /// changed, and applying everything again would undo other toggles made during the round. Like
    /// <see cref="Save"/>, smoke runs and runs with launch overrides leave the player's file alone.
    /// </summary>
    public static void Persist()
    {
        if (Isolated || Overrides.Count > 0 || _current is null) return;
        try
        {
            _current.Save(GlobalPath);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not save {FilePath}: {e.Message}");
        }
    }

    /// <summary>Forgets the loaded settings (tests).</summary>
    public static void Reset(GameSettings? settings = null) => _current = settings;
}
