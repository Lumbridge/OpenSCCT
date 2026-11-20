using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Ps2;

public enum Ps2Action { ChooseDisc, ChooseEmulator, ChooseMod, Play }

/// <summary>Local file references for the original PS2 edition. PCSX2 owns its BIOS, saves and settings.</summary>
public sealed record Ps2Edition(string? DiscPath = null, string? EmulatorPath = null, string? ModPath = null)
{
    [JsonIgnore]
    public Ps2Action NextAction => !File.Exists(DiscPath) ? Ps2Action.ChooseDisc :
        !File.Exists(EmulatorPath) ? Ps2Action.ChooseEmulator :
        ModPath != null && !File.Exists(ModPath) ? Ps2Action.ChooseMod : Ps2Action.Play;

    [JsonIgnore]
    public string ActionText => NextAction switch
    {
        Ps2Action.ChooseDisc => "Choose disc image",
        Ps2Action.ChooseEmulator => "Choose PCSX2",
        Ps2Action.ChooseMod => "Locate modded ISO",
        _ => "Play in PCSX2",
    };

    [JsonIgnore]
    public string Status => NextAction switch
    {
        Ps2Action.ChooseDisc => DiscPath is null ? "Disc needed" : "Disc image missing",
        Ps2Action.ChooseEmulator => "PCSX2 needed",
        Ps2Action.ChooseMod => "Modded ISO missing",
        _ => ModPath is null ? "Original disc selected" : "Modded ISO selected",
    };

    public static Ps2Edition Load(string launcherRoot)
    {
        try
        {
            string path = SettingsPath(launcherRoot);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Ps2Edition>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // As with the main launcher preferences, start with setup if the saved file is unreadable.
        }
        return new();
    }

    public void Save(string launcherRoot) => AtomicFile.WriteAllText(SettingsPath(launcherRoot), JsonSerializer.Serialize(this));

    public Ps2Edition WithDisc(string path)
    {
        string full = Path.GetFullPath(path);
        Ps2Disc.ReadSerial(full);
        return this with { DiscPath = full, ModPath = SamePath(full, DiscPath) ? ModPath : null };
    }

    public Ps2Edition WithEmulator(string path)
    {
        string full = Path.GetFullPath(path);
        CheckEmulator(full);
        return this with { EmulatorPath = full };
    }

    public Ps2Edition WithMod(string path)
    {
        if (DiscPath is null) throw new InvalidOperationException("Choose your original disc image first.");
        string full = Path.GetFullPath(path);
        CheckMod(full, Ps2Disc.ReadSerial(DiscPath));
        return this with { ModPath = full };
    }

    public Ps2Edition WithoutMod() => this with { ModPath = null };

    public ProcessStartInfo CreateStartInfo()
    {
        if (DiscPath is null || EmulatorPath is null)
            throw new InvalidOperationException("Choose your disc image and PCSX2 executable first.");
        string serial = Ps2Disc.ReadSerial(DiscPath);
        if (ModPath != null) CheckMod(ModPath, serial);
        CheckEmulator(EmulatorPath);
        var start = new ProcessStartInfo(EmulatorPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(EmulatorPath)!,
        };
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(ModPath ?? DiscPath);
        return start;
    }

    private static string SettingsPath(string launcherRoot) => Path.Combine(launcherRoot, "ps2.json");

    private void CheckMod(string path, string originalSerial)
    {
        if (SamePath(path, DiscPath))
            throw new InvalidDataException("Choose a separately patched copy of your disc image.");
        if (Ps2Disc.ReadSerial(path) != originalSerial)
            throw new InvalidDataException($"Choose a modded ISO for the same disc release ({originalSerial}).");
    }

    private static bool SamePath(string left, string? right) =>
        string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void CheckEmulator(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected PCSX2 executable could not be found.", path);
        if (OperatingSystem.IsWindows())
        {
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose the PCSX2 executable (.exe).");
        }
        else if ((File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
        {
            throw new InvalidDataException("Choose a PCSX2 executable or mark its AppImage executable.");
        }
    }
}
