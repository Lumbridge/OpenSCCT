using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenScct.Maps;

/// <summary>Resolves the last complete conversion of the player's Steam installation.</summary>
public static class SteamContentInstall
{
    public const int Format = 1;
    public const string BuildsDirectory = "steam-builds";
    public const string PointerFile = "steam-current.json";

    public static string? ActiveRoot(string contentDirectory)
    {
        try
        {
            using JsonDocument file = JsonDocument.Parse(File.ReadAllText(Path.Combine(contentDirectory, PointerFile)));
            var pointer = file.RootElement;
            if (pointer.ValueKind != JsonValueKind.Object ||
                !pointer.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Number ||
                !format.TryGetInt32(out int version) || version != Format ||
                !pointer.TryGetProperty("build", out var build) || build.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(build.GetString(), "N", out var id)) return null;
            string root = Path.Combine(contentDirectory, BuildsDirectory, id.ToString("N"));
            return IsComplete(root) ? root : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static IEnumerable<string> RequiredFiles()
    {
        yield return "shared/classes.json";
        yield return "shared/maps/catalog.json";
        yield return "shared/audio/audio.json";
        yield return "shared/chars/SPerso/ATT_01.glb";
        yield return "shared/chars/SPerso/DEF_01.glb";
        yield return "shared/ui/manifest.json";
        yield return "shared/render/render.json";
        yield return "shared/fx/effects.json";
        foreach (string code in SteamVersusMaps.All)
        {
            string map = "svmap/" + code + "/";
            yield return map + "manifest.json";
            yield return map + "sim/gameplay.json";
            yield return map + "sim/collision.bin";
            yield return map + "audio/audio.json";
            yield return map + "render/world.glb";
        }
    }

    public static bool IsComplete(string root) => MissingFile(root) is null;

    public static string? MissingFile(string root) =>
        RequiredFiles().FirstOrDefault(name => new FileInfo(Path.Combine(root, name)) is not { Exists: true, Length: > 0 });
}
