using System;
using System.IO;
using System.Text.Json;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Settings;

/// <summary>Player preferences in launcher.json; releases come from the built-in official source.</summary>
public sealed class LauncherSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public bool IncludePrereleases { get; set; }

    /// <summary>Check for updates when the launcher starts.</summary>
    public bool CheckOnStart { get; set; } = true;

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Json) ?? new LauncherSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Defaults; the damaged file is replaced on the next save.
        }

        return new LauncherSettings();
    }

    public void Save(string path)
    {
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
