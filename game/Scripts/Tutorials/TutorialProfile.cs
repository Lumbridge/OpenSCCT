using System;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Game.GameFlow;
using OpenScctGame.Options;

namespace OpenScctGame.Tutorials;

/// <summary>
/// The player profile's tutorial part (spec/game/tutorials.md): the tutorial maps done, the exam and the maps the
/// first-time prompt was confirmed for, kept in <c>user://tutorials.json</c> next to the settings file; and the in-game
/// help flag, which the original keeps for the visit only (Visit a Map turns it on, the main menu off).
/// </summary>
public static class TutorialProfile
{
    public const string FilePath = "user://tutorials.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static TutorialProgress? _current;

    /// <summary>The saved progress, loaded once (the defaults in smoke runs, as the settings are).</summary>
    public static TutorialProgress Current => _current ??= Load();

    /// <summary>
    /// SPlayerProfile.bDisplayHelpInGame: set by a Visit a Map launch, cleared by any other launch and when the
    /// Tutorials page opens. Read by the level's profile options (help paths, help-only hints).
    /// </summary>
    public static bool DisplayHelpInGame { get; set; }

    public static string GlobalPath => ProjectSettings.GlobalizePath(FilePath);

    private static TutorialProgress Load()
    {
        if (SettingsStore.Isolated || !File.Exists(GlobalPath))
        {
            return new TutorialProgress();
        }

        try
        {
            return JsonSerializer.Deserialize<TutorialProgress>(File.ReadAllText(GlobalPath), Json) ?? new TutorialProgress();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not read {FilePath}: {e.Message}");
            return new TutorialProgress();
        }
    }

    /// <summary>Writes the progress (Controller.RequestWriteProfile); nothing in smoke runs.</summary>
    public static void Save()
    {
        if (SettingsStore.Isolated)
        {
            return;
        }

        try
        {
            File.WriteAllText(GlobalPath, JsonSerializer.Serialize(Current, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not save {FilePath}: {e.Message}");
        }
    }

    /// <summary>The first-time prompt's OK (FirstTimeToPlayThisMap.OkButtonClick): the map is played, the profile saved.</summary>
    public static void MarkMapPlayed(string map)
    {
        if (Current.setMapAlreadyPlayed(map))
        {
            Save();
        }
    }
}
