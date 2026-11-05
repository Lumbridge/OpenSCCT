using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Editor;
using OpenScct.Launcher.Install;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed partial class EditorServiceTests
{
    public static bool UninstallOnWindows => OperatingSystem.IsWindows();
    private static async Task<EditorService> InstalledEditorAsync(TempDir dir, LauncherLayout layout, HttpClient http)
    {
        string archive = dir.Combine("uninstall-editor.zip");
        await File.WriteAllBytesAsync(archive, Archive(), TestContext.Current.CancellationToken);
        await EditorService.ExtractAndValidateAsync(archive, layout.Editor, V, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(layout.Editor, EditorService.ManagedMarker), "1", TestContext.Current.CancellationToken);
        return new EditorService(layout, http) { RunningOverride = _ => false, ProbeOverride = (_, _, _) => Task.CompletedTask };
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public async Task UninstallRemovesOnlyManagedEditorAndLeavesGameContentAndSettings()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        Prerequisites(layout);
        Directory.CreateDirectory(layout.Maps);
        File.WriteAllText(Path.Combine(layout.Maps, "my-map.sdc"), "map");
        File.WriteAllText(layout.Settings, "launcher preferences");
        File.WriteAllText(layout.Manifest, "game installation");
        Directory.CreateDirectory(layout.EditorCache);
        File.WriteAllText(Path.Combine(layout.EditorCache, "cache.json"), "cache");
        var phases = new List<EditorProgress>();

        EditorUninstallResult result = await service.UninstallAsync(new CaptureProgress(phases), TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(layout.Editor));
        Assert.Null(service.Installed());
        Assert.Null(result.RetainedPath);
        Assert.Empty(result.Failures);
        Assert.Equal("launcher preferences", File.ReadAllText(layout.Settings));
        Assert.Equal("game installation", File.ReadAllText(layout.Manifest));
        Assert.Equal("game", File.ReadAllText(Path.Combine(layout.VersionDir("0.8.94"), "OpenSCCT.exe")));
        Assert.True(File.Exists(Path.Combine(layout.Packs, "shared", "classes.json")));
        Assert.Equal("map", File.ReadAllText(Path.Combine(layout.Maps, "my-map.sdc")));
        Assert.Equal("cache", File.ReadAllText(Path.Combine(layout.EditorCache, "cache.json")));
        Assert.False(phases.Single(p => p.Phase == "Uninstalling editor").CanCancel);
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public async Task UninstallPreservesModifiedAndUnlistedFilesAndAllowsFreshInstall()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        var fixture = new ReleaseFixture();
        using var http = new HttpClient(new FakeHandler(fixture.Respond));
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        string work = Path.Combine(layout.Editor, "Projects", "my-map.osmap");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "project.json"), "my project");
        File.WriteAllText(Path.Combine(layout.Editor, "notes.txt"), "my notes");
        Directory.CreateDirectory(Path.Combine(layout.Editor, "empty-project-folder"));
        string dll = Path.Combine("data_OpenScctEditor_windows_x86_64", "OpenScctEditor.dll");
        File.WriteAllText(Path.Combine(layout.Editor, dll), "modified package file");

        EditorUninstallResult result = await service.UninstallAsync(null, TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(layout.Editor));
        Assert.NotNull(result.RetainedPath);
        Assert.Equal(layout.Root, Path.GetDirectoryName(result.RetainedPath));
        Assert.StartsWith("editor-preserved-", Path.GetFileName(result.RetainedPath), StringComparison.Ordinal);
        Assert.Equal("my project", File.ReadAllText(Path.Combine(result.RetainedPath, "Projects", "my-map.osmap", "project.json")));
        Assert.Equal("my notes", File.ReadAllText(Path.Combine(result.RetainedPath, "notes.txt")));
        Assert.Equal("modified package file", File.ReadAllText(Path.Combine(result.RetainedPath, dll)));
        Assert.True(Directory.Exists(Path.Combine(result.RetainedPath, "empty-project-folder")));
        Assert.False(File.Exists(Path.Combine(result.RetainedPath, EditorService.ExeName)));
        Assert.False(File.Exists(Path.Combine(result.RetainedPath, "version.json")));
        Assert.Empty(result.Failures);

        await service.InstallOrUpdateAsync(EditorService.Offer(fixture.Release)!, null, TestContext.Current.CancellationToken);
        Assert.NotNull(service.Installed());
        Assert.Equal("my project", File.ReadAllText(Path.Combine(result.RetainedPath, "Projects", "my-map.osmap", "project.json")));
    }

    [Theory(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    [InlineData("running")]
    [InlineData("update")]
    [InlineData("recovery")]
    [InlineData("unmanaged")]
    public async Task UninstallRefusesActiveOrUnmanagedEditorBeforeMovingFiles(string state)
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        FileStream? held = null;
        try
        {
            if (state == "running") service.RunningOverride = _ => true;
            if (state is "update" or "recovery") Directory.CreateDirectory(Path.Combine(layout.Editor, ".update"));
            if (state == "update") held = new FileStream(Path.Combine(layout.Editor, ".update", "swap.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            if (state == "recovery") File.WriteAllText(Path.Combine(layout.Editor, ".update", "journal.json"), "recovery");
            if (state == "unmanaged") File.Delete(Path.Combine(layout.Editor, EditorService.ManagedMarker));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync(null, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(layout.Editor, EditorService.ExeName)));
            Assert.Empty(Directory.GetDirectories(layout.Root, "editor-preserved-*"));
        }
        finally { held?.Dispose(); }
    }

    [Theory(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("null-file")]
    [InlineData("traversal")]
    [InlineData("case-duplicate")]
    [InlineData("device-alias")]
    [InlineData("trailing-dot")]
    [InlineData("file-parent")]
    public async Task UninstallRejectsMalformedOrAmbiguousManifestWithoutDeletingAnything(string damage)
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        string path = Path.Combine(layout.Editor, "version.json");
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonArray files = manifest["files"]!.AsArray();
        switch (damage)
        {
            case "missing": File.Delete(path); break;
            case "corrupt": File.WriteAllText(path, "not json"); break;
            case "null-file": files.Add((JsonNode?)null); break;
            case "traversal": files[0]!["path"] = "../outside.txt"; break;
            case "case-duplicate":
                JsonNode copy = files[0]!.DeepClone();
                copy["path"] = EditorService.ExeName.ToUpperInvariant();
                files.Add(copy);
                break;
            case "device-alias": files[1]!["path"] = "ose/NUL.txt"; break;
            case "trailing-dot": files[1]!["path"] = "ose/ose.exe."; break;
            case "file-parent":
                JsonNode parent = files[1]!.DeepClone();
                parent["path"] = "ose";
                files.Add(parent);
                break;
        }
        if (damage is not ("missing" or "corrupt")) File.WriteAllText(path, manifest.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.UninstallAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal("editor", File.ReadAllText(Path.Combine(layout.Editor, EditorService.ExeName)));
        Assert.Empty(Directory.GetDirectories(layout.Root, "editor-preserved-*"));
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public async Task UninstallRefusesNestedDirectoryLinkAndLeavesItsTargetAlone()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        string target = dir.Combine("my-projects");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "valuable.osmap"), "work");
        string link = Path.Combine(layout.Editor, "Projects");
        Directory.CreateSymbolicLink(link, target);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync(null, TestContext.Current.CancellationToken));
            Assert.Equal("work", File.ReadAllText(Path.Combine(target, "valuable.osmap")));
            Assert.True(File.Exists(Path.Combine(layout.Editor, EditorService.ExeName)));
        }
        finally { Directory.Delete(link); }
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public async Task UninstallHonoursCancellationBeforeMoveAndKeepsUnreadablePackageFileAfterMove()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UninstallAsync(null, new CancellationToken(canceled: true)));
        Assert.True(File.Exists(Path.Combine(layout.Editor, EditorService.ExeName)));
        // This synthetic non-executable package file can be moved but denies reads, exercising cleanup retention.
        string dll = Path.Combine("data_OpenScctEditor_windows_x86_64", "OpenScctEditor.dll");
        FileStream? held = null;
        try
        {
            var progress = new ActionProgress(p =>
            {
                if (p.Phase != "Removing unchanged editor files") return;
                string retained = Directory.GetDirectories(layout.Root, "editor-preserved-*").Single();
                held = new FileStream(Path.Combine(retained, dll), FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            });
            EditorUninstallResult result = await service.UninstallAsync(progress, TestContext.Current.CancellationToken);
            Assert.False(Directory.Exists(layout.Editor));
            Assert.NotNull(result.RetainedPath);
            Assert.True(File.Exists(Path.Combine(result.RetainedPath, dll)));
            Assert.Contains(result.Failures, f => f.StartsWith(dll.Replace('\\', '/'), StringComparison.Ordinal));
        }
        finally { held?.Dispose(); }
    }

    [Theory(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninstallRecoversItsMarkedLockBarrierAfterInterruption(bool beforeBarrierMove)
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        string token = Guid.NewGuid().ToString("N");
        string barrier = Path.Combine(layout.Editor, ".update", beforeBarrierMove ? ".uninstall-barrier-" + token : "swap.lock");
        Directory.CreateDirectory(barrier);
        File.WriteAllText(Path.Combine(barrier, ".launcher-editor-uninstall"), token);
        string holding = Path.Combine(layout.Root, ".editor-uninstall-lock-" + token);
        File.WriteAllText(holding, "");

        EditorUninstallResult result = await service.UninstallAsync(null, TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(layout.Editor));
        Assert.False(File.Exists(holding));
        Assert.Null(result.RetainedPath);
        Assert.Empty(result.Failures);
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public async Task UninstallRefusesForeignLockDirectoryAndLiveUninstallBarrier()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        string barrier = Path.Combine(layout.Editor, ".update", "swap.lock");
        Directory.CreateDirectory(barrier);
        File.WriteAllText(Path.Combine(barrier, "user.txt"), "work");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal("work", File.ReadAllText(Path.Combine(barrier, "user.txt")));
        File.Delete(Path.Combine(barrier, "user.txt"));
        string token = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(barrier, ".launcher-editor-uninstall"), token);
        using var held = new FileStream(Path.Combine(layout.Root, ".editor-uninstall-lock-" + token), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UninstallAsync(null, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(layout.Editor, EditorService.ExeName)));
        Assert.Equal(token, File.ReadAllText(Path.Combine(barrier, ".launcher-editor-uninstall")));
    }

    [Fact(Skip = "Editor uninstall uses Windows file handles.", SkipUnless = nameof(UninstallOnWindows))]
    public async Task FailedDirectoryMoveRestoresUpdaterLockAndKeepsWholeEditor()
    {
        using var dir = new TempDir();
        var layout = new LauncherLayout(dir.Combine("install"), LauncherPlatform.WindowsX64);
        using var http = new HttpClient();
        EditorService service = await InstalledEditorAsync(dir, layout, http);
        string dll = Path.Combine(layout.Editor, "data_OpenScctEditor_windows_x86_64", "OpenScctEditor.dll");
        using (var held = new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => service.UninstallAsync(null, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(layout.Editor, EditorService.ExeName)));
        Assert.True(File.Exists(Path.Combine(layout.Editor, ".update", "swap.lock")));
        Assert.False(Directory.Exists(Path.Combine(layout.Editor, ".update", "swap.lock")));
        Assert.Empty(Directory.GetFileSystemEntries(layout.Root, ".editor-uninstall-lock-*"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(layout.Editor, ".update"), ".uninstall-barrier-*"));
        Assert.Null((await service.UninstallAsync(null, TestContext.Current.CancellationToken)).RetainedPath);
    }

    private sealed class ActionProgress(Action<EditorProgress> action) : IProgress<EditorProgress>
    {
        public void Report(EditorProgress value) => action(value);
    }

    private sealed class CaptureProgress(List<EditorProgress> values) : IProgress<EditorProgress>
    {
        public void Report(EditorProgress value) => values.Add(value);
    }
}
