using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Options;

/// <summary>
/// The import the launcher queued (its Settings &gt; "Import from Enhanced SCCT Versus"): a file in the user data naming
/// the Enhanced install folder, or empty for the profile and key layouts in ProgramData alone. Carried out once at
/// start, before the settings are applied, and saved at once (there is no draft to stage it in). The file is removed
/// whatever the outcome, so a bad folder is not retried on every start.
/// </summary>
public static class EnhancedImportStartup
{
    /// <summary>The same name the launcher writes (OpenScct.Launcher.Settings.EnhancedImportRequest).</summary>
    public const string FileName = "enhanced_import.request";

    /// <summary>What the last start's import did, for the options screen's note (shown once), or null.</summary>
    public static string? Result { get; private set; }

    /// <summary>Takes <see cref="Result"/>, so it is shown only once.</summary>
    public static string? TakeResult()
    {
        string? result = Result;
        Result = null;
        return result;
    }

    /// <summary>Carries out a queued import. True when settings were imported and saved.</summary>
    public static bool RunPending(string userDataDirectory)
    {
        string request = Path.Combine(userDataDirectory, FileName);
        if (SettingsStore.Isolated || !File.Exists(request)) return false;
        string folder = "";
        try
        {
            folder = File.ReadAllText(request).Trim();
            File.Delete(request);
            EnhancedImport? source = folder.Length == 0 ? EnhancedImport.FindWithoutInstall() : EnhancedImport.Find(folder);
            if (source is null)
            {
                Result = folder.Length == 0
                    ? "The import the launcher queued found no Enhanced profile on this PC."
                    : $"The import the launcher queued found no Enhanced SCCT Versus install at {folder}.";
                GD.PushWarning(Result);
                return false;
            }

            GameSettings settings = SettingsStore.Current.Clone();
            List<string> report = source.Apply(settings);
            KeyLayout? spyKeys = source.Keys(merc: false, settings.MouseWheel);
            KeyLayout? mercKeys = source.Keys(merc: true, settings.MouseWheel);
            if (SettingsStore.Save(settings) != Error.Ok)
            {
                Result = "The import the launcher queued could not save the settings.";
                return false;
            }

            bool keys = (spyKeys is null || InputBindings.WriteUserFile(PlaySide.Spy, spyKeys)) &
                        (mercKeys is null || InputBindings.WriteUserFile(PlaySide.Merc, mercKeys));
            if (spyKeys is not null || mercKeys is not null)
                report.Add(!keys ? "The key layouts could not be written." :
                    source.SplitKeyboard ? "Spy and mercenary key layouts." : "Key layout (one for both roles).");
            Result = $"Imported from {source.Root ?? EnhancedImport.UsualProfileFolder} at start. {string.Join(" ", report)}";
            GD.Print(Result);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Result = $"The import the launcher queued failed{(folder.Length > 0 ? " for " + folder : "")}: {e.Message}";
            GD.PushWarning(Result);
            return false;
        }
    }
}
