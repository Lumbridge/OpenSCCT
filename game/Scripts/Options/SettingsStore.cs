using System;
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

    private static GameSettings LoadCurrent()
    {
        if (Isolated)
        {
            var defaults = new GameSettings();
            defaults.Clamp();
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

        return s;
    }

    /// <summary>Makes <paramref name="settings"/> the current ones, writes the file and tells the listeners.</summary>
    public static Error Save(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Clamp();
        _current = settings;
        if (Isolated)
        {
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

        SettingsApplier.WriteStartOverrides(settings);
        UiSettings ui = UiSettings.Current;
        ui.PlayerName = settings.PlayerName;
        ui.InvertMouse = settings.InvertY;
        ui.Save();
        Saved?.Invoke(settings);
        return result;
    }

    /// <summary>Forgets the loaded settings (tests).</summary>
    public static void Reset(GameSettings? settings = null) => _current = settings;
}
