using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenScct.Maps;

/// <summary>Installed tools, local retail packages and the published base used to convert a community map.</summary>
public sealed record CommunityBuildContext(BuilderTool Tool, string SteamVersusFolder, string ContentRoot)
{
    public static CommunityBuildContext Resolve(string packsRoot, string? builderDirectory = null)
    {
        string launcher = Directory.GetParent(Path.GetFullPath(packsRoot))?.FullName
            ?? throw new ArgumentException("Choose a community maps folder inside a launcher installation.", nameof(packsRoot));
        string? configured = Environment.GetEnvironmentVariable("OPENSCCT_GODOT_CONTENT_DIR");
        string content = !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured : Path.Combine(launcher, "content");
        string root = SteamContentInstall.ActiveRoot(content)
            ?? throw new InvalidOperationException("Build the original game content from Steam in the OpenSCCT launcher before installing this map.");
        string? source = ReadString(Path.Combine(root, "content-build.json"), "game");
        if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source) ||
            Directory.GetParent(source) is not { } game || !SteamLibraryManifest.IsManifestInstall(game) ||
            !File.Exists(Path.Combine(source, "System", "SCCT_Versus.exe")) || !File.Exists(Path.Combine(source, "Packages", "Maps", "Entry.sdc")))
            throw new InvalidOperationException("The original Steam installation is needed to build this map. Locate it and rebuild stock content in the OpenSCCT launcher.");
        foreach (string? directory in ToolDirectories(launcher, builderDirectory))
        {
            if (directory is null) continue;
            string tool = Path.Combine(directory, BuilderTool.ToolsFolder);
            string marker = Path.Combine(tool, "steam-builder-v1");
            string python = OperatingSystem.IsLinux() ? Path.Combine(tool, "python", "bin", "python3") : Path.Combine(tool, "python", "python.exe");
            string module = Path.Combine(tool, "repo", "content");
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == (OperatingSystem.IsLinux() ? "linux-x64" : "win-x64") &&
                File.Exists(python) && File.Exists(Path.Combine(module, "svmbuild", "__main__.py")))
                return new CommunityBuildContext(new BuilderTool(python, module), source, root);
        }
        throw new InvalidOperationException("Install or repair the current Godot game or Editor to restore the Steam map builder.");
    }

    private static IEnumerable<string?> ToolDirectories(string launcher, string? explicitDirectory)
    {
        if (explicitDirectory is not null) yield return explicitDirectory;
        var exe = Environment.ProcessPath is { } process ? new FileInfo(process).Directory : null;
        yield return exe?.FullName;
        yield return exe?.Parent?.FullName;
        string? version = ReadString(Path.Combine(launcher, "install.json"), "Current");
        if (version is { Length: > 0 } && version != "." && version != ".." && Path.GetFileName(version) == version)
            yield return Path.Combine(launcher, "versions", version);
        yield return Path.Combine(launcher, "editor");
    }

    private static string? ReadString(string path, string key)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
