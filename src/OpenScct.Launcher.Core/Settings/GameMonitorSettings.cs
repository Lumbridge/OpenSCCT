using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Settings;

/// <summary>
/// The monitor the game opens on: <c>display_monitor</c> in the game's own <c>settings.json</c> (numbered from 1),
/// the same value its Options screen edits. Monitor 1 is the primary screen and the others follow from left to
/// right, then top to bottom, as the game numbers them (SettingsApplier.ScreenForMonitor).
/// </summary>
public static class GameMonitorSettings
{
    private const string FileName = "settings.json";
    private const string Key = "display_monitor";

    /// <summary>The saved monitor number; 1 when the game has saved no settings or the file does not read.</summary>
    public static int Load(string userDataDirectory)
    {
        return Read(userDataDirectory)?[Key] is JsonValue value && value.TryGetValue(out int monitor) && monitor >= 1 ? monitor : 1;
    }

    /// <summary>
    /// Changes only the monitor, keeping every other setting in the file. A game that has saved no settings yet gets
    /// a file holding just this value; it fills in its defaults for the rest.
    /// </summary>
    public static void Save(string userDataDirectory, int monitor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(monitor, 1);
        JsonObject settings = Read(userDataDirectory) ?? new JsonObject();
        settings[Key] = monitor;
        Directory.CreateDirectory(userDataDirectory);
        AtomicFile.WriteAllText(Path.Combine(userDataDirectory, FileName),
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// The screens in the game's monitor order (entry 0 is monitor 1): the primary first, then by position.
    /// Returns the indexes into <paramref name="screens"/>.
    /// </summary>
    public static IReadOnlyList<int> Order(IReadOnlyList<(bool Primary, int X, int Y)> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        return Enumerable.Range(0, screens.Count)
            .OrderBy(i => screens[i].Primary ? 0 : 1)
            .ThenBy(i => screens[i].X)
            .ThenBy(i => screens[i].Y)
            .ToList();
    }

    private static JsonObject? Read(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataDirectory);
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(userDataDirectory, FileName)),
                documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
    }
}
