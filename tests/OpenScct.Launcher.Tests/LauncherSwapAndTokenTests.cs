using System;
using System.IO;
using System.Linq;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Update;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class LauncherSwapAndTokenTests
{
    [Fact]
    public void SwapPutsTheNewExeInPlaceAndCleanupRemovesTheOld()
    {
        using var dir = new TempDir();
        string exe = dir.Combine("OpenSCCT.Launcher.exe");
        File.WriteAllText(exe, "old");
        string download = dir.Combine("download.exe");
        File.WriteAllText(download, "new");
        var swap = new LauncherSwap(exe);

        swap.Stage(download);
        Assert.True(swap.HasStaged);
        swap.Swap();
        Assert.Equal("new", File.ReadAllText(exe));
        Assert.True(File.Exists(swap.OldPath));

        swap.CleanupAfterStart(TimeSpan.Zero);
        Assert.False(File.Exists(swap.OldPath));
        Assert.False(swap.HasStaged);
    }

    [Fact]
    public void CleanupFinishesASwapCutShort()
    {
        using var dir = new TempDir();
        string exe = dir.Combine("OpenSCCT.Launcher.exe");
        var swap = new LauncherSwap(exe);
        File.WriteAllText(swap.NewPath, "new");
        File.WriteAllText(swap.OldPath, "old");

        swap.CleanupAfterStart(TimeSpan.Zero);

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.False(File.Exists(swap.OldPath));
    }

    [Fact]
    public void TokenIsStoredProtectedAndRoundTrips()
    {
        using var dir = new TempDir();
        string path = dir.Combine("token.dat");
        var store = new TokenStore(path, new XorProtector());

        store.Save("ghp_example");
        Assert.DoesNotContain("ghp_example", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)), StringComparison.Ordinal);
        Assert.Equal("ghp_example", store.Load());

        store.Save("  ");
        Assert.False(store.HasToken);
        Assert.Null(store.Load());
    }

    [Fact]
    public void UnreadableTokenCountsAsNone()
    {
        using var dir = new TempDir();
        string path = dir.Combine("token.dat");
        File.WriteAllBytes(path, [9, 9, 9]);
        Assert.Null(new TokenStore(path, new XorProtector()).Load());
    }

    [Fact]
    public void SettingsRoundTripWithDefaults()
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        Assert.Equal("Lumbridge", LauncherSettings.Load(path).Owner);

        new LauncherSettings { Owner = " me ", Repo = "", OriginalGamePath = @"D:\Games\SCCT" }.Save(path);
        LauncherSettings read = LauncherSettings.Load(path);
        Assert.Equal("me", read.Owner);
        Assert.Equal("OpenSCCT", read.Repo);
        Assert.Equal(@"D:\Games\SCCT", read.OriginalGamePath);
        Assert.DoesNotContain("token", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayPassesTheGameRootAfterTheSeparator()
    {
        using var dir = new TempDir();
        var info = GameStart.Create(dir.Combine("OpenSCCT.exe"), @"D:\Games\SCCT", contentRoot: dir.Path);
        Assert.Equal(["--", @"--svm-game-root=D:\Games\SCCT", "--svm-content=" + dir.Path], info.ArgumentList.ToArray());
        Assert.Equal(@"D:\Games\SCCT", info.Environment["SVM_GAME_ROOT"]);

        Assert.Empty(GameStart.Create(dir.Combine("OpenSCCT.exe"), null, dir.Combine("missing")).ArgumentList);
    }

    [Fact]
    public void ContentPlanNamesTheInstallAndTheContentFolder()
    {
        var command = ContentBuildPlan.Arguments(@"D:\SCCT", @"C:\c", 4);
        Assert.Contains("--game", command);
        Assert.Contains(@"D:\SCCT", command);
    }
}
