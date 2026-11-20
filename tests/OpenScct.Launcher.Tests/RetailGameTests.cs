using System;
using System.IO;
using OpenScct.Launcher.Retail;
using OpenScct.Launcher.Settings;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class RetailGameTests
{
    [Fact]
    public void FindsWholeGameAcrossSteamLibrariesAndBothSystemFolders()
    {
        using var dir = new TempDir();
        string library = dir.Combine("Games on another drive");
        string game = Path.Combine(library, "steamapps", "common", "Chaos Theory");
        string campaign = Touch(game, "System", "splintercell3.exe");
        Directory.CreateDirectory(Path.Combine(game, "Data", "Maps"));
        MakeVersus(Path.Combine(game, "Versus"));
        File.WriteAllText(Touch(dir.Path, "steamapps", "libraryfolders.vdf"),
            "\"path\"\t\"" + library.Replace("\\", "\\\\") + "\"");
        File.WriteAllText(Touch(library, "steamapps", "appmanifest_13570.acf"), "\"installdir\"\t\"Chaos Theory\"");

        var found = RetailSteam.Find([dir.Path]);
        Assert.NotNull(found);
        Assert.Equal(game, found.Folder);
        Assert.Equal(campaign, found.CampaignExecutable);
        Assert.True(found.HasVersus);
        Assert.Equal(found, RetailGame.Inspect(Path.Combine(game, "System")));
        Assert.Equal(found, RetailGame.Inspect(Path.Combine(game, "Versus", "System")));
        Assert.Equal(RetailGame.SteamLaunchUri, RetailGame.CreateCampaignStart(found, found).FileName);
        Assert.Equal(campaign, RetailGame.CreateCampaignStart(found, null).FileName);
    }

    [Fact]
    public void PortableModChangesOnlyVersusAndCanBeTurnedOff()
    {
        using var dir = new TempDir();
        string game = dir.Combine("retail");
        MakeVersus(Path.Combine(game, "Versus"));
        Touch(game, "System", "splintercell3.exe");
        Directory.CreateDirectory(Path.Combine(game, "Data", "Maps"));
        string enhanced = dir.Combine("Enhanced SCCT Versus");
        MakeVersus(enhanced);
        Touch(enhanced, "System", "Reloaded.Core.dll");
        var settings = new LauncherSettings();
        var steam = RetailGame.Inspect(game);
        RetailGame.SetMod(settings.Retail, enhanced, steam);
        settings.Save(dir.Combine("launcher.json"));
        var profiles = LauncherSettings.Load(dir.Combine("launcher.json")).Retail;
        var selected = RetailGame.Selected(profiles, steam);
        Assert.NotNull(selected);
        Assert.True(selected.HasReloaded);
        Assert.Equal(Path.Combine(enhanced, "System"), RetailGame.CreateStart(selected).WorkingDirectory);
        Assert.Equal(steam, RetailGame.Base(profiles, steam));
        Assert.Equal(steam, RetailGame.Campaign(profiles, steam));
        Assert.Throws<InvalidOperationException>(() => RetailGame.SetMod(profiles, game, steam));
        Assert.Throws<InvalidOperationException>(() => RetailGame.SetBase(profiles, enhanced));
        File.Delete(selected.Executable);
        Assert.Null(RetailGame.Selected(profiles, steam));
        profiles.UseMod = false;
        Assert.Equal(steam, RetailGame.Selected(profiles, steam));
    }

    [Fact]
    public void RecognizesCampaignOnlyAndRejectsIncompleteMod()
    {
        using var dir = new TempDir();
        string campaign = Touch(dir.Path, "System", "splintercell3.exe");
        string exeOnly = Touch(dir.Path, "System", "SCCT_Versus.exe");
        Assert.Null(RetailGame.Inspect(dir.Path));
        Directory.CreateDirectory(dir.Combine("Data", "Maps"));
        var found = RetailGame.Inspect(dir.Path);
        Assert.NotNull(found);
        Assert.Equal(campaign, found.CampaignExecutable);
        Assert.False(found.HasVersus);
        var profiles = new RetailProfiles();
        RetailGame.SetMod(profiles, dir.Path, null);
        Assert.Equal(found, RetailGame.Campaign(profiles, null));
        Assert.Null(RetailGame.Selected(profiles, null));
        Assert.Throws<FileNotFoundException>(() => RetailGame.CreateStart(found));
        Assert.True(File.Exists(exeOnly));
    }

    private static void MakeVersus(string root)
    {
        Touch(root, "System", "SCCT_Versus.exe");
        Touch(root, "Packages", "Maps", "Entry.sdc");
    }

    private static string Touch(string root, params string[] parts)
    {
        string file = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "fixture");
        return file;
    }
}
