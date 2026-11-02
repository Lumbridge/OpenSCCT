using System;
using System.IO;
using System.Text.Json;
using OpenScct.Net.Voice;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

public sealed class VoiceVolumesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenScct-VoiceVolumes-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "voice-volumes.json");

    [Fact]
    public void SavedVolumesSurviveReloadAndPlayerNameCasingChanges()
    {
        var volumes = new VoiceVolumes();
        volumes.SetVolume("Quiet player", 0.35f);
        volumes.SetVolume("Silent player", 0f);
        volumes.SetVolume("Loud player", 2f);
        volumes.Save(SettingsPath);

        VoiceVolumes loaded = VoiceVolumes.Load(SettingsPath);
        Assert.Equal(0.35f, loaded.VolumeOf("QUIET PLAYER"));
        Assert.Equal(0f, loaded.VolumeOf("silent player"));
        Assert.Equal(2f, loaded.VolumeOf("Loud player"));
        Assert.Equal(1f, loaded.VolumeOf("New player"));
        Assert.False(File.Exists(SettingsPath + ".tmp"));

        loaded.SetVolume("quiet PLAYER", 0.65f);
        loaded.Save(SettingsPath);
        Assert.Equal(0.65f, VoiceVolumes.Load(SettingsPath).VolumeOf("Quiet player"));
    }

    [Fact]
    public void ChangesAreRaisedOnlyWhenTheEffectiveVolumeChanges()
    {
        var volumes = new VoiceVolumes();
        int changes = 0;
        volumes.Changed += () => changes++;

        volumes.SetVolume("Player", 1f);
        volumes.SetVolume("", 0.4f);
        volumes.SetVolume("   ", 0.4f);
        Assert.Equal(0, changes);

        volumes.SetVolume("Player", 0.4f);
        volumes.SetVolume("PLAYER", 0.4f);
        Assert.Equal(1, changes);

        volumes.SetVolume("player", 3f);
        volumes.SetVolume("Player", 2f);
        Assert.Equal(2, changes);

        volumes.SetVolume("player", 1f);
        volumes.SetVolume("PLAYER", 1f);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void InvalidGainsAreNormalizedBeforeSaving()
    {
        var volumes = new VoiceVolumes();
        volumes.SetVolume("Low", -1f);
        volumes.SetVolume("High", 5f);
        volumes.SetVolume("NaN", float.NaN);
        volumes.SetVolume("Positive infinity", float.PositiveInfinity);
        volumes.SetVolume("Negative infinity", float.NegativeInfinity);
        volumes.Save(SettingsPath);

        VoiceVolumes loaded = VoiceVolumes.Load(SettingsPath);
        Assert.Equal(0f, loaded.VolumeOf("Low"));
        Assert.Equal(2f, loaded.VolumeOf("High"));
        Assert.Equal(1f, loaded.VolumeOf("NaN"));
        Assert.Equal(1f, loaded.VolumeOf("Positive infinity"));
        Assert.Equal(1f, loaded.VolumeOf("Negative infinity"));
    }

    [Fact]
    public void ReturningToFullVolumeSurvivesReload()
    {
        var volumes = new VoiceVolumes();
        volumes.SetVolume("Player", 0.25f);
        volumes.Save(SettingsPath);

        VoiceVolumes loaded = VoiceVolumes.Load(SettingsPath);
        loaded.SetVolume("PLAYER", 1f);
        loaded.Save(SettingsPath);
        Assert.Equal(1f, VoiceVolumes.Load(SettingsPath).VolumeOf("player"));
    }

    [Fact]
    public void MissingAndNullFilesUseFullVolume()
    {
        Assert.Equal(1f, VoiceVolumes.Load(SettingsPath).VolumeOf("Player"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "null");
        Assert.Equal(1f, VoiceVolumes.Load(SettingsPath).VolumeOf("Player"));
    }

    [Fact]
    public void MalformedFileIsReportedWithoutOverwritingIt()
    {
        Directory.CreateDirectory(_directory);
        const string broken = "{ \"Player\": ";
        File.WriteAllText(SettingsPath, broken);
        Assert.Throws<JsonException>(() => VoiceVolumes.Load(SettingsPath));
        Assert.Equal(broken, File.ReadAllText(SettingsPath));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_directory)) return;
        File.Delete(SettingsPath);
        File.Delete(SettingsPath + ".tmp");
        Directory.Delete(_directory);
    }
}
