using System;
using System.IO;
using System.Text.Json;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Settings;

/// <summary>Launcher settings in launcher.json. The GitHub token is not here: see <see cref="TokenStore"/>.</summary>
public sealed class LauncherSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Owner { get; set; } = ReleaseSource.DefaultOwner;

    public string Repo { get; set; } = ReleaseSource.DefaultRepo;

    public bool IncludePrereleases { get; set; }

    /// <summary>Check for updates when the launcher starts.</summary>
    public bool CheckOnStart { get; set; } = true;

    /// <summary>The original Splinter Cell Chaos Theory install, read only, used to build game content.</summary>
    public string? OriginalGamePath { get; set; }

    /// <summary>The text language game content is built in (<see cref="OpenScct.Launcher.Content.ContentBuildPlan.Languages"/>).</summary>
    public string Language { get; set; } = OpenScct.Launcher.Content.ContentBuildPlan.DefaultLanguage;

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                LauncherSettings settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Json) ?? new LauncherSettings();
                settings.Normalise();
                return settings;
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
        Normalise();
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    private void Normalise()
    {
        Owner = string.IsNullOrWhiteSpace(Owner) ? ReleaseSource.DefaultOwner : Owner.Trim();
        Repo = string.IsNullOrWhiteSpace(Repo) ? ReleaseSource.DefaultRepo : Repo.Trim();
        OriginalGamePath = string.IsNullOrWhiteSpace(OriginalGamePath) ? null : OriginalGamePath.Trim();
        Language = OpenScct.Launcher.Content.ContentBuildPlan.NormaliseLanguage(Language);
    }
}
