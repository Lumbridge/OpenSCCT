using System;
using System.IO;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The settings file: Reloaded's first-run defaults, ranges, comments and recovery.</summary>
public sealed class GameSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "svm-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void TeammateNamesPersistIndependentlyPerRole()
    {
        var defaults = GameSettings.FromJson("{}");
        Assert.False(defaults.TeammateNamesSpy);
        Assert.False(defaults.TeammateNamesMerc);
        var spy = new GameSettings { TeammateNamesSpy = true }.Clone();
        Assert.True(spy.TeammateNamesSpy);
        Assert.False(spy.TeammateNamesMerc);
        var merc = GameSettings.FromJson("{\"teammate_names_merc\":true}");
        Assert.False(merc.TeammateNamesSpy);
        Assert.True(merc.TeammateNamesMerc);
    }

    [Fact]
    public void RollFreeLookIsOffForNewAndExistingProfilesAndPersistsWhenEnabled()
    {
        Assert.False(new GameSettings().SpyRollFreeLook);
        Assert.False(GameSettings.FromJson("{}").SpyRollFreeLook);
        GameSettings enabled = GameSettings.FromJson("{\"spy_roll_free_look\":true}").Clone();
        Assert.True(enabled.SpyRollFreeLook);
        string path = Path.Combine(_dir, "settings.json");
        enabled.Save(path);
        Assert.True(GameSettings.Load(path).SpyRollFreeLook);
        enabled.SpyRollFreeLook = false;
        enabled.Save(path);
        Assert.False(GameSettings.Load(path).SpyRollFreeLook);
    }

    [Fact]
    public void DefaultsAreReloadedsFirstRunValues()
    {
        var s = new GameSettings();
        Assert.Equal(112f, s.MercFovCap);
        Assert.True(s.Widescreen);
        Assert.Equal(DisplayFrameCap.Monitor, s.FpsSpy);   // an addition: Reloaded's first run has 120 (FramePacingTests)
        Assert.Equal(DisplayFrameCap.Monitor, s.FpsMerc);
        Assert.False(s.VSync);
        Assert.Equal("BorderlessFullscreen", s.DisplayMode);
        Assert.Equal(1.4f, s.SpyKbm.FreeLook);
        Assert.Equal(1f, s.SpyKbm.Look);
        Assert.Equal(1f, s.MercKbm.Look);
        Assert.Equal(2.45f, s.MercKbm.ScopeVertical);
        Assert.False(s.Pad.Enabled);
        Assert.Equal(0.25f, s.Pad.DeadzoneLeft);
        Assert.Equal(0.15f, s.Pad.DeadzoneRight);
        Assert.Equal(0.75f, s.Pad.VerticalScale);
        Assert.Equal(0.95f, s.Pad.RunThreshold);
        Assert.Equal("CurveAggressive", s.SpyPad.LookResponse);
        Assert.Equal("CurveSoft", s.SpyPad.GunLookResponse);
        Assert.Equal("CurveMedium", s.MercPad.LookResponse);
        Assert.Equal(0.5f, s.SoundVolume);
        Assert.Equal(1f, s.VoiceVolume);
        Assert.True(s.ShowScoreboard);
        Assert.False(s.ShowPing);
    }

    [Fact]
    public void MissingFileGetsReloadedControllerLayoutAndIndependentSpyCurves()
    {
        GameSettings s = GameSettings.Load(Path.Combine(_dir, "first-run.json"));
        Assert.False(s.Pad.Enabled);
        Assert.Equal(1, s.Pad.LayoutVersionOf(false));
        Assert.Equal(1, s.Pad.LayoutVersionOf(true));
        Assert.Equal(7680f / 32767f, s.Pad.TriggerThreshold);
        Assert.Equal("Action:use", s.Pad.SpyButtons["A"]);
        Assert.Equal("Action:jump", s.Pad.SpyButtons["Y"]);
        Assert.Equal("Action:vision2", s.Pad.MercButtons["DpadRight"]);
        Assert.Equal("Action:slot4", s.Pad.MercButtons["LS+A"]);
        Assert.Equal("Action:launch1", s.Pad.SpyButtons["LB+Y"]);
        Assert.Equal("Action:rof", s.Pad.MercButtons["LB+DpadDown"]);
        Assert.Equal("CurveSoft", s.Clone().SpyPad.GunLookResponse);
    }

    [Fact]
    public void UnversionedPartialProfileKeepsOldDefaultsWithoutNewCombinations()
    {
        GameSettings s = GameSettings.FromJson("""
            { "controller": { "spy_buttons": { "A": "Key:O", "RB": "" }, "merc_buttons": { "LB": "" } },
              "controls_spy_pad": { "look_response": "CurveMedium" } }
            """);
        Assert.True(s.Pad.Enabled);
        Assert.Equal(0, s.Pad.LayoutVersionOf(false));
        Assert.Equal(0.5f, s.Pad.TriggerThreshold);
        Assert.Equal("Key:O", s.Pad.SpyButtons["A"]);
        Assert.Equal("", s.Pad.SpyButtons["RB"]);
        Assert.Equal("SwitchHeadset", s.Pad.SpyButtons["Y"]);
        Assert.Equal("", s.Pad.MercButtons["LB"]);
        Assert.Equal("Jump", s.Pad.MercButtons["A"]);
        Assert.Equal("CurveMedium", s.SpyPad.GunLookResponse);
        Assert.DoesNotContain("LS+A", s.Pad.SpyButtons.Keys);
        Assert.DoesNotContain("LB+Y", s.Pad.MercButtons.Keys);
        GameSettings roundTrip = s.Clone();
        Assert.Equal(s.Pad.SpyButtons, roundTrip.Pad.SpyButtons);
        Assert.Equal(s.Pad.MercButtons, roundTrip.Pad.MercButtons);
        Assert.Equal("CurveMedium", roundTrip.SpyPad.GunLookResponse);
    }

    [Fact]
    public void ExistingProfileWithoutControllerGroupKeepsItsPriorControllerBehavior()
    {
        GameSettings s = GameSettings.FromJson("{}");
        Assert.True(s.Pad.Enabled);
        Assert.Equal(0, s.Pad.LayoutVersion);
        Assert.Equal(0.5f, s.Pad.TriggerThreshold);
        Assert.Equal("Jump", s.Pad.SpyButtons["A"]);
        Assert.Equal("Action:swapweapon", s.Pad.MercButtons["LB"]);
        Assert.Equal("CurveAggressive", s.SpyPad.GunLookResponse);
        Assert.DoesNotContain("B:Hold", s.Pad.SpyButtons.Keys);
    }

    [Fact]
    public void ResettingOneControllerRolePreservesTheOtherRoleAndSavedGlobalSettings()
    {
        GameSettings s = GameSettings.FromJson("""
            { "controller": { "enabled": false, "trigger_threshold": 0.4,
                "merc_buttons": { "A": "Action:reload", "RT": "", "LS+A": "" } } }
            """);
        s.Pad.ResetButtons(merc: false);
        GameSettings back = s.Clone();
        Assert.Equal(1, back.Pad.LayoutVersionOf(false));
        Assert.Equal(0, back.Pad.LayoutVersionOf(true));
        Assert.Equal("Action:use", back.Pad.SpyButtons["A"]);
        Assert.Equal("Action:slot4", back.Pad.SpyButtons["LS+A"]);
        Assert.Equal("Action:reload", back.Pad.MercButtons["A"]);
        Assert.Equal("", back.Pad.MercButtons["RT"]);
        Assert.Equal("", back.Pad.MercButtons["LS+A"]);
        Assert.DoesNotContain("LB+Back", back.Pad.MercButtons.Keys);
        Assert.False(back.Pad.Enabled);
        Assert.Equal(0.4f, back.Pad.TriggerThreshold);
    }

    [Fact]
    public void VersionedPartialLayoutFillsOnlyItsOwnDefaultsAndKeepsExplicitUnbinds()
    {
        GameSettings s = GameSettings.FromJson("""
            { "controller": { "layout_version": 1, "spy_buttons": { "A": "", "LS+A": "" } } }
            """);
        Assert.False(s.Pad.Enabled);
        Assert.Equal("", s.Pad.SpyButtons["A"]);
        Assert.Equal("", s.Pad.SpyButtons["LS+A"]);
        Assert.Equal("Action:jump", s.Pad.SpyButtons["Y"]);
        Assert.Equal("Action:launch4", s.Pad.SpyButtons["LB+A"]);
        Assert.Equal("CurveSoft", s.SpyPad.GunLookResponse);
        Assert.Equal(7680f / 32767f, s.Pad.TriggerThreshold);
    }

    [Fact]
    public void ValuesAreClampedToTheirRanges()
    {
        var s = new GameSettings
        {
            MercFovCap = 500f, FpsSpy = 10, FpsMerc = 999, MasterVolume = 3f, MusicVolume = -1f,
            Monitor = 0, DisplayMode = "Nonsense", Resolution = "12x9", PlayerName = "  ", RenderScale = 0.1f,
        };
        s.Pad.DeadzoneLeft = 5f;
        s.SpyKbm.LookResponse = "Wobbly";
        s.Clamp();
        Assert.Equal(GameSettings.MaxMercFov, s.MercFovCap);
        Assert.Equal(GameSettings.MinFps, s.FpsSpy);
        Assert.Equal(GameSettings.MaxFps, s.FpsMerc);
        Assert.Equal(1f, s.MasterVolume);
        Assert.Equal(0f, s.MusicVolume);
        Assert.Equal(1, s.Monitor);
        Assert.Equal("BorderlessFullscreen", s.DisplayMode);
        Assert.Equal("", s.Resolution);
        Assert.Equal("Player", s.PlayerName);
        Assert.Equal(0.5f, s.RenderScale);
        Assert.Equal(0.9f, s.Pad.DeadzoneLeft);
        Assert.Equal("Linear", s.SpyKbm.LookResponse);
        Assert.Equal(0, GameSettings.ClampFps(0));
        Assert.Equal(GameSettings.MinMercFov, LookFov(50f));

        static float LookFov(float v)
        {
            var t = new GameSettings { MercFovCap = v };
            t.Clamp();
            return t.MercFovCap;
        }
    }

    [Fact]
    public void TheFileTakesCommentsTrailingCommasAndUnknownKeys()
    {
        const string json = """
            // the player's settings
            {
              "player_name": "Ryan", // inline comment
              "ws_fov": 105,
              "fps_spy": 240,
              "controls_spy_kbm": { "free_look": 2.0, },
              "controller": { "enabled": true, "deadzone_left": 0.3 },
              "some_future_key": [1, 2, 3],
            }
            """;
        GameSettings s = GameSettings.FromJson(json);
        Assert.Equal("Ryan", s.PlayerName);
        Assert.Equal(105f, s.MercFovCap);
        Assert.Equal(240, s.FpsSpy);
        Assert.Equal(2f, s.SpyKbm.FreeLook);
        Assert.Equal(1f, s.SpyKbm.Look);      // a group read in part keeps its side's defaults for the rest
        Assert.Equal(3.5f, s.SpyKbm.Vertical);
        Assert.True(s.Pad.Enabled);
        Assert.Equal(0.3f, s.Pad.DeadzoneLeft, 3);
        Assert.Equal(DisplayFrameCap.Monitor, s.FpsMerc);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        string path = Path.Combine(_dir, "settings.json");
        var s = new GameSettings { PlayerName = "Sam", FpsMerc = 144, Resolution = "1920x1080", ShowPing = true };
        s.MercKbm.Look = 1.25f;
        s.Save(path);
        Assert.False(File.Exists(path + ".tmp"));
        GameSettings back = GameSettings.Load(path);
        Assert.Equal("Sam", back.PlayerName);
        Assert.Equal(144, back.FpsMerc);
        Assert.Equal("1920x1080", back.Resolution);
        Assert.True(back.ShowPing);
        Assert.Equal(1.25f, back.MercKbm.Look);
        Assert.Equal(path, back.LoadNote);
        Assert.StartsWith("//", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenFileIsKeptAsBackupAndTheDefaultsAreUsed()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ \"ws_fov\": ");
        GameSettings s = GameSettings.Load(path);
        Assert.Equal(112f, s.MercFovCap);
        Assert.Contains(".bak", s.LoadNote, StringComparison.Ordinal);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal("defaults", GameSettings.Load(Path.Combine(_dir, "missing.json")).LoadNote);
    }

    [Fact]
    public void ResolutionsParseAndKeysAreListed()
    {
        Assert.Equal((2560, 1440), GameSettings.ParseResolution("2560x1440"));
        Assert.Equal((1280, 720), GameSettings.ParseResolution(" 1280 X 720 "));
        Assert.Null(GameSettings.ParseResolution("320x200"));
        Assert.Null(GameSettings.ParseResolution("wide"));
        Assert.Contains("ws_fov", GameSettings.Keys());
        Assert.Contains("fps_merc", GameSettings.Keys());
        Assert.Contains("controls_spy_kbm", GameSettings.Keys());
        Assert.Same(new GameSettings().SpyKbm.GetType(), new GameSettings().LookOf(merc: false, pad: false).GetType());
        var s = new GameSettings();
        Assert.Same(s.MercPad, s.LookOf(merc: true, pad: true));
        Assert.Equal(s.FpsMerc, s.FpsFor(merc: true));
    }
}
