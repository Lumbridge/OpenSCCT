using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenScctGame.Controls;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class DefaultKeyLayoutTests
{
    [Fact]
    public void NoInstallFallbackIncludesInterfaceAndSecondaryGameplayControls()
    {
        KeyLayout layout = DefaultKeyLayout.Create();
        foreach ((string key, string action) in new[]
        {
            ("T", "PerformCoopMvt"), ("H", "bSpecialsChat"), ("L", "SwitchLaser"), ("V", "PrevStickyCam"),
            ("PageUp", "ZoomIn"), ("PageDown", "ZoomOut"), ("5", "BT_PrevItem"), ("6", "BT_NextItem"),
            ("F3", "DisplayDefaultControls"), ("F4", "DisplayHud"), ("F5", "Talk"), ("F6", "TeamTalk"),
            ("F7", "DisplayStrategicMap"), ("F8", "DisplaySessionStatus"), ("F9", "SwitchEnhancedReality"),
            ("F10", "ShowConnectedPlayers"), ("F11", "Quit"), ("Escape", "ShowMenu"),
        })
            Assert.Contains(layout.Resolve(key), part => part.Name == action);

        Assert.Equal(1, Assert.Single(layout.Resolve("F1")).Arg);
        Assert.Equal(2, Assert.Single(layout.Resolve("F2")).Arg);
        Assert.Equal(3, Assert.Single(layout.Resolve("Tab")).Arg);
    }

    [Theory]
    [InlineData("Default_kbd_0.ini", @"Desktop\Enhanced SCCT Versus v4.0 Public Beta 3")]
    [InlineData("Default_kbd_1.ini", @"Desktop\Enhanced SCCT Versus v4.0 Public Beta 3")]
    [InlineData("Default_kbd_0.ini", @"Desktop\Enhanced SCCT Versus 4.0 Beta 5")]
    [InlineData("Default_kbd_1.ini", @"Desktop\Enhanced SCCT Versus 4.0 Beta 5")]
    public void FallbackResolvesExactlyLikeOriginalLayoutWhenInstalled(string file, string folder)
    {
        string root = Environment.GetEnvironmentVariable("SVM_GAME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder);
        string original = Path.Combine(root, "System", file);
        Assert.SkipUnless(File.Exists(original), "The reference game's stock keyboard layout is not installed.");
        KeyLayout stock = KeyLayout.Load(original);
        KeyLayout fallback = DefaultKeyLayout.Create();
        Assert.Equal(stock.Bindings.Keys.Where(key => stock.Resolve(key).Count > 0), fallback.Bindings.Keys);
        foreach (string key in fallback.Bindings.Keys) Assert.Equal(stock.Resolve(key), fallback.Resolve(key));
    }

    [Theory]
    [InlineData("Gadget", "NextGadget", "PreviousGadget")]
    [InlineData("Speed", "Run", "Walk")]
    public void FirstLoadAndReloadUseIdenticalCompleteLayouts(string mode, string up, string down)
    {
        KeyLayout stock = DefaultKeyLayout.Create();
        KeyLayout first = DefaultKeyLayout.ApplyProfile(stock, saved: null, mode);
        string snapshot = first.ToIni();
        KeyLayout reloaded = DefaultKeyLayout.ApplyProfile(stock, snapshot, mode);

        Assert.Equal(snapshot, reloaded.ToIni());
        Assert.Equal("ChangeGear", Assert.Single(reloaded.Resolve("I")).Name);
        Assert.Equal("SwapWeapon", Assert.Single(reloaded.Resolve("G")).Name);
        Assert.Equal("PushToTalk", Assert.Single(reloaded.Resolve("CapsLock")).Name);
        Assert.Equal("TeamPushToTalk", Assert.Single(reloaded.Resolve("Y")).Name);
        Assert.Equal(new[] { "DisplaySessionStatus", "ToggleVoiceMute" }, reloaded.Resolve("F8").Select(a => a.Name));
        Assert.Equal("Walk", Assert.Single(reloaded.Resolve("MiddleMouse")).Name);
        Assert.Equal(up, Assert.Single(reloaded.Resolve("MouseWheelUp")).Name);
        Assert.Equal(down, Assert.Single(reloaded.Resolve("MouseWheelDown")).Name);
        Assert.False(stock.Bindings.ContainsKey("I"));
        Assert.Equal("ChangeSpeed", Assert.Single(stock.Resolve("MiddleMouse")).Name);
    }

    [Fact]
    public void CompletePlayerSnapshotKeepsUnboundActionsAndRebindings()
    {
        KeyLayout changed = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), null, "Gadget");
        foreach (string key in new[] { "I", "G", "CapsLock", "Y", "F8", "T" }) changed.Bind(key, "");
        changed.Bind("X", "PerformCoopMvt | CustomCommand 17");
        string saved = changed.ToIni();

        KeyLayout loaded = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), saved, "Gadget");

        Assert.Equal(saved, loaded.ToIni());
        Assert.Equal(new[] { "PerformCoopMvt", "CustomCommand" }, loaded.Resolve("X").Select(a => a.Name));
        Assert.Equal(17, loaded.Resolve("X")[1].Arg);
    }

    [Fact]
    public void LegacyOverlayKeepsCustomAdditionsAndDoesNotOverwriteOccupiedKeys()
    {
        const string overlay = """
            [Engine.Input]
            Aliases[0]=(Command="ChangeGear",Alias="MyGear")
            IK_J=MyGear
            IK_G=CustomGadget 9
            IK_CapsLock=CustomVoiceAction
            IK_U=PushToTalk
            IK_F8=CustomStatus
            """;
        KeyLayout loaded = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), overlay, "Gadget");

        Assert.False(loaded.Bindings.ContainsKey("I"));
        Assert.Equal("ChangeGear", Assert.Single(loaded.Resolve("J")).Name);
        Assert.Equal("CustomGadget", Assert.Single(loaded.Resolve("G")).Name);
        Assert.Equal("CustomVoiceAction", Assert.Single(loaded.Resolve("CapsLock")).Name);
        Assert.Equal("PushToTalk", Assert.Single(loaded.Resolve("U")).Name);
        Assert.Equal(new[] { "CustomStatus", "ToggleVoiceMute" }, loaded.Resolve("F8").Select(a => a.Name));
    }

    [Theory]
    [InlineData("Gadget")]
    [InlineData("Speed")]
    public void UntouchedDownloadedMercDefaultsAreRecognizedForEnhancedUpgrade(string wheelMode)
    {
        KeyLayout legacy = LegacyMercLayout();
        Assert.Equal("BT_Up", Assert.Single(legacy.Resolve("Up")).Name);
        Assert.Empty(legacy.Resolve("W"));
        KeyLayout saved = DefaultKeyLayout.ApplyProfile(legacy, null, wheelMode);
        string old = BeforeEnhancedDefaults(saved);
        Assert.True(DefaultKeyLayout.IsLegacyMercDefault(old, wheelMode));
        Assert.False(DefaultKeyLayout.IsLegacyMercDefault(saved.ToIni(), wheelMode));
        Assert.False(DefaultKeyLayout.IsLegacyMercDefault(DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), null, wheelMode).ToIni(), wheelMode));
    }

    [Theory]
    [InlineData("Up", "")]
    [InlineData("G", "CustomAction")]
    [InlineData("E", "GadTorcheSwitch")]
    [InlineData("MouseX", "Axis aMouseX Speed=3.00")]
    public void OnePersonalMercBindingPreventsAutomaticDefaultUpgrade(string key, string command)
    {
        KeyLayout legacy = DefaultKeyLayout.ApplyProfile(LegacyMercLayout(), null, "Gadget");
        legacy.Bind(key, command);
        Assert.False(DefaultKeyLayout.IsLegacyMercDefault(BeforeEnhancedDefaults(legacy), "Gadget"));
    }

    private static string BeforeEnhancedDefaults(KeyLayout layout) =>
        layout.ToIni().Replace(DefaultKeyLayout.EnhancedDefaultsMarker + "\n", "", StringComparison.Ordinal);

    // Captured from the installed public shared pack, whose vanilla merc layout caused the report.
    private static KeyLayout LegacyMercLayout([CallerFilePath] string source = "") =>
        KeyLayout.Load(Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", "legacy-merc-default.ini"));
}
