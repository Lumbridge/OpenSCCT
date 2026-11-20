using System;
using System.Diagnostics;
using System.IO;

namespace OpenScct.Launcher.Retail;

/// <summary>The original installation and an optional, separately installed PC mod.</summary>
public sealed record RetailProfiles
{
    public string? InstallFolder { get; set; }
    public string? ModFolder { get; set; }
    public bool UseMod { get; set; }
}

public sealed record RetailInstallation(string Folder, string Executable, bool HasVersus, bool HasReloaded,
    string? CampaignExecutable);

public static class RetailGame
{
    public const string SteamLaunchUri = "steam://rungameid/13570";
    public const string EnhancedWebsite = "https://github.com/Joshhhuaaa/EnhancedSCCTVersus";
    public const string ReloadedWebsite = "https://allypal.github.io/SCCT_Versus_Reloaded/";

    /// <summary>Accept the game root, the Versus root, or either System directory.</summary>
    public static RetailInstallation? Inspect(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
            return null;
        var root = new DirectoryInfo(Path.TrimEndingDirectorySeparator(folder));
        if (root.Name.Equals("System", StringComparison.OrdinalIgnoreCase) && root.Parent is { } parent)
            root = parent;

        DirectoryInfo game = root.Name.Equals("Versus", StringComparison.OrdinalIgnoreCase) && root.Parent is { } gameRoot
            ? gameRoot : root;
        string campaign = Path.Combine(game.FullName, "System", "splintercell3.exe");
        bool hasCampaign = File.Exists(campaign) && Directory.Exists(Path.Combine(game.FullName, "Data", "Maps"));
        string versus = Directory.Exists(Path.Combine(root.FullName, "Versus")) ? Path.Combine(root.FullName, "Versus") : root.FullName;
        string system = Path.Combine(versus, "System");
        string exe = Path.Combine(system, "SCCT_Versus.exe");
        bool hasVersus = File.Exists(exe) && File.Exists(Path.Combine(versus, "Packages", "Maps", "Entry.sdc"));
        if (!hasCampaign && !hasVersus) return null;
        return new RetailInstallation(hasCampaign ? game.FullName : versus, exe, hasVersus,
            hasVersus && File.Exists(Path.Combine(system, "Reloaded.Core.dll")), hasCampaign ? campaign : null);
    }

    public static RetailInstallation? FindSteam() => RetailSteam.Find();

    public static RetailInstallation? Base(RetailProfiles profiles, RetailInstallation? steam) =>
        string.IsNullOrWhiteSpace(profiles.InstallFolder) ? steam : Inspect(profiles.InstallFolder);

    /// <summary>A Versus-only mod leaves campaign and co-op available from the base game.</summary>
    public static RetailInstallation? Campaign(RetailProfiles profiles, RetailInstallation? steam)
    {
        if (profiles.UseMod && Inspect(profiles.ModFolder) is { CampaignExecutable: not null } campaign) return campaign;
        return Base(profiles, steam);
    }

    /// <summary>The Versus installation for the profile. A missing active mod requires repair or deselection.</summary>
    public static RetailInstallation? Selected(RetailProfiles profiles, RetailInstallation? steam)
    {
        if (!profiles.UseMod) return Base(profiles, steam);
        var mod = Inspect(profiles.ModFolder);
        if (mod is null) return null;
        return mod.HasVersus ? mod : Base(profiles, steam);
    }

    public static void SetBase(RetailProfiles profiles, string folder)
    {
        var install = Inspect(folder) ?? throw new ArgumentException("Choose a Chaos Theory game or Versus installation folder.");
        if (IsSame(install, Inspect(profiles.ModFolder)))
            throw new InvalidOperationException("Choose a separate folder for the base game and the modded copy.");
        profiles.InstallFolder = install.Folder;
    }

    public static void SetMod(RetailProfiles profiles, string folder, RetailInstallation? steam)
    {
        var mod = Inspect(folder) ?? throw new ArgumentException("Choose a separate Chaos Theory installation containing the modded campaign or Versus files.");
        if (IsSame(mod, Base(profiles, steam)))
            throw new InvalidOperationException("This is the base installation. Choose a separate modded game copy.");
        profiles.ModFolder = mod.Folder;
        profiles.UseMod = true;
    }

    public static bool IsSame(RetailInstallation? left, RetailInstallation? right) =>
        left is not null && right is not null && string.Equals(left.Folder, right.Folder,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static ProcessStartInfo CreateStart(RetailInstallation install)
    {
        if (!install.HasVersus || !File.Exists(install.Executable))
            throw new FileNotFoundException("The Versus executable is missing.", install.Executable);
        return StartExecutable(install.Executable);
    }

    public static ProcessStartInfo CreateCampaignStart(RetailInstallation install, RetailInstallation? steam)
    {
        if (install.CampaignExecutable is not { } campaign || !File.Exists(campaign))
            throw new FileNotFoundException("The campaign and co-op executable is missing.");
        return IsSame(install, steam)
            ? new ProcessStartInfo(SteamLaunchUri) { UseShellExecute = true }
            : StartExecutable(campaign);
    }

    private static ProcessStartInfo StartExecutable(string exe) =>
        new(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
}
