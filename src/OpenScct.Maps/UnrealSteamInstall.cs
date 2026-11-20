using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenScct.Maps;

/// <summary>The content generated for the installed Unreal edition and its converter recipe.</summary>
public static class UnrealSteamInstall
{
    public const string RecipeFile = "unreal-steam-builder.json";
    public const string ContentDirectoryVariable = "OPENSCCT_UNREAL_CONTENT_DIR";

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "OpenSCCT", "unreal", "content");

    public static string? ReadRecipe(string toolsDirectory)
    {
        try
        {
            using var file = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(toolsDirectory, RecipeFile)));
            var root = file.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out var schema) || !schema.TryGetInt32(out int version) || version != 1 ||
                !root.TryGetProperty("platform", out var platform) || platform.GetString() != "win-x64" ||
                !root.TryGetProperty("recipeDigest", out var value) || value.GetString() is not { Length: 64 } digest)
                return null;
            return digest.All(c => char.IsAsciiHexDigitLower(c)) ? digest : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string? ActiveRoot(string directory, string? expectedRecipe)
    {
        if (expectedRecipe is null) return null;
        string? active = SteamContentInstall.ActiveRoot(directory);
        return active is not null && ReadRecipe(active) == expectedRecipe ? active : null;
    }
}
