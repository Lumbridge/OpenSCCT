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

    private Retail.RetailProfiles _retail = new();
    public Retail.RetailProfiles Retail { get => _retail; set => _retail = value ?? new(); }

    /// <summary>A manually located Steam installation used to build Godot's stock content.</summary>
    public string? GodotSteamFolder { get; set; }

    /// <summary>The editor has its own release channel, independent of the game.</summary>
    public bool EditorIncludePrereleases { get; set; }

    /// <summary>Last selected edition or tool: Ps2, Retail, Unreal, Game (Godot), or Editor.</summary>
    public string SelectedProduct { get; set; } = "Game";

    /// <summary>Check for game updates when its launcher tab opens.</summary>
    public bool CheckOnStart { get; set; } = true;

    /// <summary>Check the launcher release feed when opening an original game. Local media setup works offline by default.</summary>
    public bool OriginalCheckOnStart { get; set; }

    /// <summary>Check for editor updates when its launcher tab opens, independent of the game.</summary>
    public bool EditorCheckOnStart { get; set; } = true;

    /// <summary>The Unreal Engine build has its own release channel, independent of the game and the editor.</summary>
    public bool UnrealIncludePrereleases { get; set; }

    /// <summary>Check for Unreal build updates when its launcher tab opens, independent of the other tabs.</summary>
    public bool UnrealCheckOnStart { get; set; } = true;

    /// <summary>
    /// The GitHub repository ("owner/name") the Unreal build's releases come from, or null for the built-in one. An
    /// invalid value is ignored (<see cref="Unreal.UnrealService.IsValidRepository"/>).
    /// </summary>
    public string? UnrealRepository { get; set; }

    /// <summary>A player-selected folder containing compiled SVMap map folders.</summary>
    public string? UnrealMapModRoot { get; set; }

    public bool UnrealMapModsEnabled { get; set; }

    /// <summary>An independent copy for editing preferences before Save is confirmed.</summary>
    public LauncherSettings Copy()
    {
        var copy = (LauncherSettings)MemberwiseClone();
        copy.Retail = Retail with { };
        return copy;
    }

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                LauncherSettings settings = JsonSerializer.Deserialize<LauncherSettings>(json, Json) ?? new LauncherSettings();
                using JsonDocument document = JsonDocument.Parse(json);
                // Older launchers had one startup switch. Keep that choice until each tab is edited independently.
                if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty(nameof(EditorCheckOnStart), out _))
                    settings.EditorCheckOnStart = settings.CheckOnStart;
                if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty(nameof(UnrealCheckOnStart), out _))
                    settings.UnrealCheckOnStart = settings.CheckOnStart;
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
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
