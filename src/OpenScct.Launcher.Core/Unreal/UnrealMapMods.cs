using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenScct.Launcher.Unreal;

/// <summary>Checks the loose map collection sent to Unreal's --svm-maps option. The game verifies the simulation hash on load.</summary>
public static class UnrealMapMods
{
    private static readonly string[] RequiredFiles = ["sim/gameplay.json", "sim/traversal.json", "sim/collision.bin", "render/world.glb"];

    public static bool TryGetMapRoot(string? folder, out string root, out string error, string? installedMaps = null)
    {
        root = string.Empty;
        error = "Choose a folder containing compiled SVMap map folders.";
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || folder.Contains('"') || !Directory.Exists(folder))
            return false;

        try
        {
            string collection = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (File.Exists(Path.Combine(collection, "manifest.json")))
            {
                error = "Choose the folder containing this map. All compiled maps directly inside that folder are enabled together.";
                return false;
            }

            bool found = false;
            foreach (string map in Directory.EnumerateDirectories(collection))
            {
                string manifest = Path.Combine(map, "manifest.json");
                if (!File.Exists(manifest)) continue;

                string id = Path.GetFileName(map);
                using FileStream input = File.OpenRead(manifest);
                using JsonDocument document = JsonDocument.Parse(input);
                JsonElement json = document.RootElement;
                if (Text(json, "format") != "svmap" || !Version.TryParse(Text(json, "formatVersion"), out Version? version)
                    || version.Major != 1 || !string.Equals(Text(json, "map"), id, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"The manifest in {id} must be an SVMap 1.x manifest with a matching map ID.";
                    return false;
                }
                // Unreal's menu and match bridge carry the map ID in a 32-character buffer.
                if (id.Length > 31 || !(char.IsAsciiLetter(id[0]) || id[0] == '_')
                    || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                {
                    error = $"The map ID {id} must fit Unreal's 31-character map name and use letters, digits, underscores or hyphens.";
                    return false;
                }
                if (!json.TryGetProperty("source", out var source) || Text(source, "kind") is not ("authored" or "converted"))
                {
                    error = $"{id} needs an authored or converted Versus map build to appear in Play offline.";
                    return false;
                }
                foreach (string relative in RequiredFiles)
                {
                    if (File.Exists(Path.Combine(map, relative))) continue;
                    error = $"The compiled map {id} is missing {relative}.";
                    return false;
                }
                if (installedMaps is not null && File.Exists(Path.Combine(installedMaps, id, "manifest.json")))
                {
                    error = $"{id} is already installed with the game. Choose a collection with unique map IDs, or disable map mods to play.";
                    return false;
                }
                found = true;
            }
            if (!found) return false;
            root = collection;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            error = $"Cannot read the compiled map collection. {ex.Message}";
            return false;
        }
    }

    private static string? Text(JsonElement json, string key) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
