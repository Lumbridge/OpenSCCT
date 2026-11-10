using System;
using System.IO;
using System.Linq;
using OpenScct.Launcher.Launch;
using OpenScct.Launcher.Settings;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class GameRendererSettingsTests
{
    [Fact]
    public void FreshProfileUsesVulkanWithoutCreatingSettings()
    {
        using var dir = new TempDir();
        string data = dir.Combine("new-profile");
        Assert.Equal(GameRendererSettings.Vulkan, GameRendererSettings.Load(data));
        Assert.False(Directory.Exists(data));

        GameRendererSettings.Save(data, GameRendererSettings.Vulkan);

        Assert.Equal(GameRendererSettings.Vulkan, GameRendererSettings.Load(data));
        Assert.Equal("[rendering]\nrendering_device/driver.windows=\"vulkan\"\n", File.ReadAllText(Path.Combine(data, "render_override.cfg")));
        Assert.False(File.Exists(Path.Combine(data, "settings.json")));
    }

    [Theory]
    [InlineData(GameRendererSettings.Vulkan, "vulkan")]
    public void SavesTheChoiceAndPreservesOtherSettings(string renderer, string driver)
    {
        using var dir = new TempDir();
        string path = dir.Combine("render_override.cfg");
        const string original = "; player override\r\n[rendering] ; render settings\r\n\r\n  rendering_device/driver.windows = \"unknown\"  ; keep note\r\nrendering_device/vsync/swapchain_image_count=2\r\n[display]\r\nwindow/size/no_focus=true\r\nrendering_device/driver.windows=\"other section\"";
        File.WriteAllText(path, original);

        GameRendererSettings.Save(dir.Path, renderer);

        Assert.Equal(original.Replace("\"unknown\"", "\"" + driver + "\"", StringComparison.Ordinal), File.ReadAllText(path));
        Assert.Equal(renderer, GameRendererSettings.Load(dir.Path));
    }

    [Fact]
    public void DuplicateDriverAssignmentsCannotRetainTheOldChoice()
    {
        using var dir = new TempDir();
        string path = dir.Combine("render_override.cfg");
        File.WriteAllText(path, "[rendering]\nrendering_device/driver.windows=\"vulkan\"\nrendering_device/driver.windows=\"d3d12\" ; last wins\n[display]\nrendering_device/driver.windows=\"keep\"\n[rendering]\nrendering_device/driver.windows=\"d3d12\"\n");
        // Direct3D 12 was a choice until game 0.8.125: a file that still names it reads as Vulkan.
        Assert.Equal(GameRendererSettings.Vulkan, GameRendererSettings.Load(dir.Path));

        GameRendererSettings.Save(dir.Path, GameRendererSettings.Vulkan);

        string updated = File.ReadAllText(path);
        Assert.Equal(3, updated.Split("rendering_device/driver.windows=\"vulkan\"", StringSplitOptions.None).Length - 1);
        Assert.Contains(" ; last wins", updated, StringComparison.Ordinal);
        Assert.Contains("[display]\nrendering_device/driver.windows=\"keep\"", updated, StringComparison.Ordinal);
        Assert.Equal(GameRendererSettings.Vulkan, GameRendererSettings.Load(dir.Path));
    }

    [Theory]
    [InlineData("[rendering]\nrendering_device/vsync/swapchain_image_count=2\n[display]\nwindow/size/no_focus=true\n", "[rendering]\nrendering_device/vsync/swapchain_image_count=2\nrendering_device/driver.windows=\"vulkan\"\n[display]\nwindow/size/no_focus=true\n")]
    [InlineData("[rendering]\nrendering_device/vsync/swapchain_image_count=2", "[rendering]\nrendering_device/vsync/swapchain_image_count=2\nrendering_device/driver.windows=\"vulkan\"\n")]
    [InlineData("; keep comment\n[display]\nwindow/size/no_focus=true", "; keep comment\n[display]\nwindow/size/no_focus=true\n\n[rendering]\nrendering_device/driver.windows=\"vulkan\"\n")]
    public void AddsAMissingDriverWithoutChangingExistingEntries(string original, string expected)
    {
        using var dir = new TempDir();
        string path = dir.Combine("render_override.cfg");
        File.WriteAllText(path, original);

        GameRendererSettings.Save(dir.Path, GameRendererSettings.Vulkan);

        Assert.Equal(expected, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("\"unknown\"")]
    [InlineData("\"d3d12\"")]
    [InlineData("d3d12")]
    [InlineData("\"d3d12")]
    [InlineData("\"d3d12\" extra")]
    [InlineData("")]
    public void UnsupportedOrMalformedDriverUsesVulkan(string value)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("render_override.cfg"), "[rendering]\nrendering_device/driver.windows=" + value + "\n");
        Assert.Equal(GameRendererSettings.Vulkan, GameRendererSettings.Load(dir.Path));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Direct3D 12")]
    [InlineData("OpenGL")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsupportedSaveLeavesFilesUntouched(string? renderer)
    {
        using var dir = new TempDir();
        string path = dir.Combine("render_override.cfg");
        File.WriteAllText(path, "; keep");

        Assert.Throws<ArgumentException>(() => GameRendererSettings.Save(dir.Path, renderer!));

        Assert.Equal("; keep", File.ReadAllText(path));
    }

    [Fact]
    public void UnreadableOverrideIsSurfacedRatherThanTreatedAsMissing()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("render_override.cfg"));

        Assert.Throws<UnauthorizedAccessException>(() => GameRendererSettings.Load(dir.Path));
        Assert.Throws<UnauthorizedAccessException>(() => GameRendererSettings.Save(dir.Path, GameRendererSettings.Vulkan));
    }

    [Theory]
    [InlineData(GameRendererSettings.Vulkan, "vulkan")]
    public void EngineRendererPrecedesWebsiteJoinArguments(string renderer, string driver)
    {
        using var dir = new TempDir();
        const string join = "--svm-join-server=server123";

        var start = GameStart.Create(dir.Combine("OpenSCCT.exe"), [join], renderer);

        Assert.Equal(["--rendering-driver", driver, "--", join], start.ArgumentList.ToArray());
    }

    [Fact]
    public void ExplicitRendererWorksWithoutUserArgumentsAndRejectsUnknownChoice()
    {
        using var dir = new TempDir();
        string exe = dir.Combine("OpenSCCT.exe");
        Assert.Equal(["--rendering-driver", "vulkan"], GameStart.Create(exe, graphicsDriver: GameRendererSettings.Vulkan).ArgumentList.ToArray());
        Assert.Throws<ArgumentException>(() => GameStart.Create(exe, graphicsDriver: "Default"));
    }
}
