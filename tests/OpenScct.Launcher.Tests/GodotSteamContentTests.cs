using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Maps;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class GodotSteamContentTests
{
    [Fact]
    public void ResolvesSteamManifestAndRejectsOtherInstalledCopies()
    {
        using var dir = new TempDir();
        string apps = dir.Combine("Steam Library", "steamapps");
        string game = Path.Combine(apps, "common", "Chaos Theory");
        string versus = Path.Combine(game, "Versus");
        Write(versus, "System/SCCT_Versus.exe");
        Write(versus, "Packages/Maps/Entry.sdc");
        Assert.Null(GodotSteamContent.FindSource(game));
        File.WriteAllText(Path.Combine(apps, "appmanifest_13570.acf"), "\"installdir\" \"Another Game\"");
        Assert.Null(GodotSteamContent.FindSource(game));
        File.WriteAllText(Path.Combine(apps, "appmanifest_13570.acf"), "\"installdir\" \"Chaos Theory\"");
        Assert.Equal(versus, GodotSteamContent.FindSource(game));
        Assert.Equal(versus, GodotSteamContent.FindSource(Path.Combine(versus, "System")));
    }

    [Fact]
    public async Task PreviousPacksDoNotQualifyAndFailedBuildRetainsPublishedGeneration()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Path);
        var content = new GodotSteamContent(layout);
        Populate(layout.Packs);
        Write(layout.Packs, "svmap/MyOptionalMap/manifest.json");
        Assert.Null(content.ActiveRoot());
        string first = await content.PublishAsync((root, _) => { Populate(root); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        Assert.Equal(first, content.ActiveRoot());
        string? partial = null;
        await Assert.ThrowsAsync<InvalidDataException>(() => content.PublishAsync((root, _) =>
        {
            partial = root;
            Write(root, "shared/classes.json");
            Assert.Equal(first, SteamContentInstall.ActiveRoot(layout.Content));
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(partial));
        Assert.Equal(first, SteamContentInstall.ActiveRoot(layout.Content));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => content.PublishAsync((_, token) =>
        {
            cancelled.Cancel();
            return Task.FromCanceled(token);
        }, cancelled.Token));
        Assert.Equal(first, SteamContentInstall.ActiveRoot(layout.Content));
        Assert.True(File.Exists(Path.Combine(layout.Packs, "svmap/MyOptionalMap/manifest.json")));
        File.Delete(Path.Combine(first, "svmap", SteamVersusMaps.DefaultHostMap, "sim", "collision.bin"));
        Assert.Null(SteamContentInstall.ActiveRoot(layout.Content));
    }

    [Fact]
    public async Task ConcurrentBuildCannotPublishOverAnInProgressConversion()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Path);
        var content = new GodotSteamContent(layout);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var production = content.PublishAsync(async (root, _) =>
        {
            started.SetResult();
            await finish.Task;
            Populate(root);
        }, TestContext.Current.CancellationToken);
        await started.Task;
        try
        {
            Assert.Null(content.ActiveRoot());
            await Assert.ThrowsAsync<IOException>(() => new GodotSteamContent(layout).PublishAsync((_, _) => Task.CompletedTask, TestContext.Current.CancellationToken));
        }
        finally { finish.SetResult(); }
        Assert.Equal(await production, SteamContentInstall.ActiveRoot(layout.Content));
    }

    [Theory]
    [InlineData("{\"format\":1,\"build\":\"../../packs\"}")]
    [InlineData("{\"format\":99,\"build\":\"00000000000000000000000000000000\"}")]
    [InlineData("incomplete")]
    public void RejectsInvalidPointers(string value)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine(SteamContentInstall.PointerFile), value);
        Assert.Null(SteamContentInstall.ActiveRoot(dir.Path));
    }

    [Fact]
    public void StartsTheGameWithActiveRootAndJoinIntentAsSeparateArguments()
    {
        using var dir = new TempDir();
        string root = dir.Combine("content with spaces", "stock");
        var start = GameStart.Create(dir.Combine("client"), ["--svm-join-server=server"], contentRoot: root);
        Assert.Equal(["--", "--svm-content=" + root, "--svm-join-server=server"], start.ArgumentList.ToArray());
    }

    [Fact]
    public void CommunityBuildsRequireTheCurrentSteamSourceAndBundledTools()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Path);
        var id = Guid.NewGuid().ToString("N");
        string generation = Path.Combine(layout.Content, SteamContentInstall.BuildsDirectory, id);
        Populate(generation);
        File.WriteAllText(Path.Combine(layout.Content, SteamContentInstall.PointerFile),
            System.Text.Json.JsonSerializer.Serialize(new { format = SteamContentInstall.Format, build = id }));
        string apps = dir.Combine("steamapps");
        string versus = Path.Combine(apps, "common", "Chaos Theory", "Versus");
        Write(versus, "System/SCCT_Versus.exe");
        Write(versus, "Packages/Maps/Entry.sdc");
        string manifest = Path.Combine(apps, "appmanifest_13570.acf");
        File.WriteAllText(manifest, "\"installdir\" \"Chaos Theory\"");
        File.WriteAllText(Path.Combine(generation, ContentState.FileName), System.Text.Json.JsonSerializer.Serialize(new { game = versus }));
        string version = layout.VersionDir("0.8.166");
        string tool = Path.Combine(version, "contenttools");
        string python = OperatingSystem.IsLinux() ? "python/bin/python3" : "python/python.exe";
        Write(tool, python);
        Write(tool, "repo/content/svmbuild/__main__.py");
        File.WriteAllText(Path.Combine(tool, "steam-builder-v1"), OperatingSystem.IsLinux() ? "linux-x64" : "win-x64");
        new InstallManifest { Current = "0.8.166" }.Save(layout.Manifest);
        var context = CommunityBuildContext.Resolve(layout.Packs);
        Assert.Equal(versus, context.SteamVersusFolder);
        Assert.Equal(generation, context.ContentRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(tool, python)), Path.GetFullPath(context.Tool.Python));
        File.Delete(manifest);
        Assert.Throws<InvalidOperationException>(() => CommunityBuildContext.Resolve(layout.Packs));
    }

    private static void Populate(string root)
    {
        foreach (string relative in SteamContentInstall.RequiredFiles()) Write(root, relative);
    }

    private static void Write(string root, string relative)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
    }
}
