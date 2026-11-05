using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Update;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class LauncherSwapAndSettingsTests
{
    [Fact]
    public void SwapPutsTheNewExeInPlaceAndCleanupRemovesTheOld()
    {
        using var dir = new TempDir();
        string exe = dir.Combine("OpenSCCT.Launcher.exe");
        File.WriteAllText(exe, "old");
        string download = dir.Combine("download.exe");
        File.WriteAllText(download, "new");
        var swap = new LauncherSwap(exe, LauncherPlatform.WindowsX64);

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
        var swap = new LauncherSwap(exe, LauncherPlatform.WindowsX64);
        File.WriteAllText(swap.NewPath, "new");
        File.WriteAllText(swap.OldPath, "old");

        swap.CleanupAfterStart(TimeSpan.Zero);

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.False(File.Exists(swap.OldPath));
    }

    [Fact]
    public void SettingsRoundTripWithDefaults()
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        LauncherSettings defaults = LauncherSettings.Load(path);
        Assert.True(defaults.CheckOnStart);
        Assert.True(defaults.EditorCheckOnStart);
        Assert.False(defaults.IncludePrereleases);

        new LauncherSettings { CheckOnStart = false, IncludePrereleases = true }.Save(path);
        LauncherSettings read = LauncherSettings.Load(path);
        Assert.False(read.CheckOnStart);
        Assert.True(read.IncludePrereleases);
    }

    [Fact]
    public void LegacyGitHubAndOriginalInstallSettingsAreIgnoredWhileUpdatePreferencesSurvive()
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        File.WriteAllText(path, """
            {
              "Owner": "old-private-owner",
              "Repo": "old-private-repository",
              "Token": "old-unused-token",
              "IncludePrereleases": true,
              "CheckOnStart": false,
              "OriginalGamePath": "D:\\Games\\SCCT",
              "Language": "deu"
            }
            """);

        LauncherSettings settings = LauncherSettings.Load(path);
        Assert.True(settings.IncludePrereleases);
        Assert.False(settings.CheckOnStart);
        Assert.False(settings.EditorCheckOnStart);

        settings.Save(path);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(saved.RootElement.TryGetProperty("Owner", out _));
        Assert.False(saved.RootElement.TryGetProperty("Repo", out _));
        Assert.False(saved.RootElement.TryGetProperty("Token", out _));
        Assert.False(saved.RootElement.TryGetProperty("OriginalGamePath", out _));
        Assert.False(saved.RootElement.TryGetProperty("Language", out _));
        Assert.True(saved.RootElement.GetProperty("IncludePrereleases").GetBoolean());
        Assert.False(saved.RootElement.GetProperty("CheckOnStart").GetBoolean());
        Assert.False(saved.RootElement.GetProperty("EditorCheckOnStart").GetBoolean());
        Assert.Equal(5, saved.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void PlayUsesDefaultDownloadedPacksAndLeavesLegacyConvertedContentUnused()
    {
        using var dir = new TempDir();
        string oldContent = dir.Combine("content", "shared");
        Directory.CreateDirectory(oldContent);
        string marker = Path.Combine(oldContent, "keep.txt");
        File.WriteAllText(marker, "existing converted content");
        string packs = dir.Combine("packs");
        Directory.CreateDirectory(packs);
        string exe = dir.Combine("OpenSCCT.exe");
        var info = GameStart.Create(exe);

        Assert.Empty(info.ArgumentList);
        Assert.Equal(exe, info.FileName);
        Assert.Equal(dir.Path, info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
        Assert.False(info.Environment.ContainsKey("SVM_GAME_ROOT"));
        Assert.False(info.Environment.ContainsKey("SVM_CONTENT"));
        Assert.Equal("existing converted content", File.ReadAllText(marker));
    }

    [Fact]
    public void EditorSelectionAndPreviewPreferenceRoundTripIndependentlyOfGame()
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        new LauncherSettings
        {
            SelectedProduct = "Editor",
            EditorIncludePrereleases = true,
            IncludePrereleases = false,
            CheckOnStart = false,
            EditorCheckOnStart = true,
        }.Save(path);

        LauncherSettings read = LauncherSettings.Load(path);
        Assert.Equal("Editor", read.SelectedProduct);
        Assert.True(read.EditorIncludePrereleases);
        Assert.False(read.IncludePrereleases);
        Assert.False(read.CheckOnStart);
        Assert.True(read.EditorCheckOnStart);
    }

    [Fact]
    public void ExistingSettingsDefaultToGameAndStableEditorReleases()
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        File.WriteAllText(path, """{"IncludePrereleases":true,"CheckOnStart":false}""");

        LauncherSettings read = LauncherSettings.Load(path);
        Assert.Equal("Game", read.SelectedProduct);
        Assert.False(read.EditorIncludePrereleases);
        Assert.True(read.IncludePrereleases);
        Assert.False(read.CheckOnStart);
        Assert.False(read.EditorCheckOnStart);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyStartupPreferenceInitializesBothProducts(bool enabled)
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { CheckOnStart = enabled, SelectedProduct = "Editor" }));

        LauncherSettings read = LauncherSettings.Load(path);

        Assert.Equal(enabled, read.CheckOnStart);
        Assert.Equal(enabled, read.EditorCheckOnStart);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ExplicitStartupPreferencesRemainIndependent(bool game, bool editor)
    {
        using var dir = new TempDir();
        string path = dir.Combine("launcher.json");
        new LauncherSettings { CheckOnStart = game, EditorCheckOnStart = editor }.Save(path);

        LauncherSettings read = LauncherSettings.Load(path);

        Assert.Equal(game, read.CheckOnStart);
        Assert.Equal(editor, read.EditorCheckOnStart);
    }

    [Fact]
    public void PlayDoesNotInheritOriginalInstallOrConvertedContentOverrides()
    {
        using var dir = new TempDir();
        string? originalGame = Environment.GetEnvironmentVariable("SVM_GAME_ROOT");
        string? originalContent = Environment.GetEnvironmentVariable("SVM_CONTENT");
        try
        {
            Environment.SetEnvironmentVariable("SVM_GAME_ROOT", dir.Combine("old-original-install"));
            Environment.SetEnvironmentVariable("SVM_CONTENT", dir.Combine("old-converted-content"));
            var info = GameStart.Create(dir.Combine("OpenSCCT.exe"));

            Assert.False(info.Environment.ContainsKey("SVM_GAME_ROOT"));
            Assert.False(info.Environment.ContainsKey("SVM_CONTENT"));
            Assert.Empty(info.ArgumentList);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SVM_GAME_ROOT", originalGame);
            Environment.SetEnvironmentVariable("SVM_CONTENT", originalContent);
        }
    }

    [Fact]
    public void PlayKeepsWebsiteJoinArgumentsWithDownloadedPacks()
    {
        using var dir = new TempDir();
        const string join = "--svm-join-server=server123";

        var info = GameStart.Create(dir.Combine("OpenSCCT.exe"), [join]);

        Assert.Equal(["--", join], info.ArgumentList.ToArray());
    }

    [Fact]
    public void ContentPlanNamesTheInstallAndTheContentFolder()
    {
        var command = ContentBuildPlan.Arguments(@"D:\SCCT", @"C:\c", 4);
        Assert.Contains("--game", command);
        Assert.Contains(@"D:\SCCT", command);
    }
}
