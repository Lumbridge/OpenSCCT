using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenScctGame.Controls;

namespace OpenScctGame.Options;

/// <summary>
/// Reads a player's settings out of an Enhanced SCCT Versus (Reloaded) install so the options can take them over:
/// <list type="bullet">
/// <item>Reloaded's <c>System\SCCT_Versus.config</c> (JSON with comments; <see cref="GameSettings"/> reuses its keys):
/// sensitivities, display, graphics, HUD, voice and controller settings.</item>
/// <item>The game profile <c>&lt;name&gt;_prf.ini</c>: inverted axes, volumes, mouse wheel, gear.</item>
/// <item>The profile's key layouts <c>&lt;name&gt;_kbd_0.ini</c> (spy) and <c>&lt;name&gt;_kbd_1.ini</c> (merc).</item>
/// </list>
/// The profile files are not in the install: they sit in the folder <c>System\_Profiles_.lnk</c> points to
/// (ProgramData\Ubisoft\...\Saved Games\Versus), shared by every install on the machine. Godot-free; nothing is
/// written here, the options screen stages the result in its draft.
/// </summary>
public sealed class EnhancedImport
{
    public const string ConfigName = "SCCT_Versus.config";

    private const string ProfileSuffix = "_prf.ini";

    private readonly Dictionary<string, string> _profile = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonElement _config;
    private readonly string? _keys0;
    private readonly string? _keys1;

    private EnhancedImport(string? root, string? configPath, string? profilePath)
    {
        Root = root;
        ConfigPath = configPath;
        ProfilePath = profilePath;
        if (configPath is not null)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            _config = document.RootElement.Clone();
        }

        if (profilePath is not null)
        {
            string file = Path.GetFileName(profilePath);
            ProfileName = file[..^ProfileSuffix.Length];
            foreach (string raw in File.ReadAllLines(profilePath, Encoding.Latin1))
            {
                int eq = raw.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0) _profile[raw[..eq].Trim()] = raw[(eq + 1)..].Trim();
            }

            string folder = Path.GetDirectoryName(profilePath) ?? ".";
            _keys0 = Existing(Path.Combine(folder, ProfileName + "_kbd_0.ini"));
            _keys1 = Existing(Path.Combine(folder, ProfileName + "_kbd_1.ini"));
        }
    }

    /// <summary>The install folder (the one holding System), or null when only a profile folder was chosen.</summary>
    public string? Root { get; }

    public string? ConfigPath { get; }

    public string? ProfilePath { get; }

    /// <summary>The profile's name (the file name before <c>_prf.ini</c>), empty without a profile.</summary>
    public string ProfileName { get; } = "";

    /// <summary>Reloaded's use_split_spy_merc_keyboard; an install without the config plays one layout (stock).</summary>
    public bool SplitKeyboard => Flag(_config, "use_split_spy_merc_keyboard") ?? false;

    /// <summary>The wheel mode of the profile (SelectedMouse 0 changes gadgets, 1 walk and run), or null without one.</summary>
    public string? WheelMode => ProfileInt("SelectedMouse") switch { 0 => "Gadget", 1 => "Speed", _ => null };

    /// <summary>
    /// Looks for an install at or above <paramref name="folder"/> (any folder inside the install will do), or takes
    /// <paramref name="folder"/> itself when it holds profiles. Null when there is nothing to import.
    /// </summary>
    public static EnhancedImport? Find(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        string? root = InstallRoot(folder);
        string? profiles = root is not null ? ProfileFolder(root) : folder;
        string? config = root is null ? null : Existing(Path.Combine(root, "System", ConfigName));
        string? profile = profiles is null ? null : NewestProfile(profiles);
        return config is null && profile is null ? null : new EnhancedImport(root, config, profile);
    }

    private static string? InstallRoot(string folder)
    {
        var dir = new DirectoryInfo(folder);
        for (int depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            string system = Path.Combine(dir.FullName, "System");
            if (File.Exists(Path.Combine(system, ConfigName)) || File.Exists(Path.Combine(system, "SCCT_Versus.exe")) ||
                File.Exists(Path.Combine(system, "Default_kbd_0.ini")))
                return dir.FullName;
        }

        return null;
    }

    /// <summary>Where the install keeps its profiles: the shortcut's target, else the game's usual ProgramData folder.</summary>
    private static string? ProfileFolder(string root)
    {
        string link = Path.Combine(root, "System", "_Profiles_.lnk");
        if (File.Exists(link) && ShortcutTarget(File.ReadAllBytes(link)) is { } target && Directory.Exists(target)) return target;
        return Directory.Exists(UsualProfileFolder) ? UsualProfileFolder : null;
    }

    /// <summary>Where every install on the machine keeps its Versus profiles and key layouts.</summary>
    public static string UsualProfileFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Ubisoft", "Tom Clancy's Splinter Cell Chaos Theory", "Saved Games", "Versus");

    /// <summary>
    /// The import without an install: the profile and key layouts from <see cref="UsualProfileFolder"/>. Reloaded's
    /// own settings (sensitivities, display, voice) are in the install and are not part of it. Null without a profile.
    /// </summary>
    public static EnhancedImport? FindWithoutInstall() =>
        Directory.Exists(UsualProfileFolder) && NewestProfile(UsualProfileFolder) is { } profile ? new EnhancedImport(null, null, profile) : null;

    /// <summary>The profile played last (the game rewrites it on every run).</summary>
    private static string? NewestProfile(string folder) => new DirectoryInfo(folder)
        .EnumerateFiles("*" + ProfileSuffix).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    /// <summary>
    /// The local path a Windows shortcut points to (MS-SHLLINK: the header, an optional item list, then LinkInfo with
    /// the local base path and a common path suffix), or null.
    /// </summary>
    internal static string? ShortcutTarget(byte[] link)
    {
        const int Header = 0x4C;
        if (link.Length < Header + 4 || BitConverter.ToUInt32(link, 0) != Header) return null;
        uint flags = BitConverter.ToUInt32(link, 0x14);
        int at = Header;
        if ((flags & 1) != 0) at += 2 + BitConverter.ToUInt16(link, at);
        if ((flags & 2) == 0 || at + 36 > link.Length) return null;
        uint headerSize = BitConverter.ToUInt32(link, at + 4);
        if ((BitConverter.ToUInt32(link, at + 8) & 1) == 0) return null;
        string path = headerSize >= 0x24
            ? Chars(at + (int)BitConverter.ToUInt32(link, at + 28), wide: true) + Chars(at + (int)BitConverter.ToUInt32(link, at + 32), wide: true)
            : Chars(at + (int)BitConverter.ToUInt32(link, at + 16), wide: false) + Chars(at + (int)BitConverter.ToUInt32(link, at + 24), wide: false);
        return path.Length > 0 ? path : null;

        string Chars(int start, bool wide)
        {
            if (start < 0 || start >= link.Length) return "";
            int step = wide ? 2 : 1;
            int end = start;
            while (end + step <= link.Length && (link[end] != 0 || (wide && link[end + 1] != 0))) end += step;
            return (wide ? Encoding.Unicode : Encoding.Latin1).GetString(link, start, end - start);
        }
    }

    // ---- key layouts ---------------------------------------------------------------------------------------

    /// <summary>
    /// The profile's key layout for a side, complete and ready to save (stock aliases included, OpenSCCT's own actions
    /// added on free keys as on a first run), or null when the profile has no key file. With Reloaded's split
    /// keyboards off, both sides get the layout the profile selects (SelectedKeyboard).
    /// </summary>
    public KeyLayout? Keys(bool merc, string wheelMode)
    {
        bool second = SplitKeyboard ? merc : ProfileInt("SelectedKeyboard") == 1;
        string? path = second ? _keys1 ?? _keys0 : _keys0 ?? _keys1;
        if (path is null) return null;
        string? stockPath = Root is null ? null : Existing(Path.Combine(Root, "System", second ? "Default_kbd_1.ini" : "Default_kbd_0.ini"));
        KeyLayout stock;
        if (stockPath is not null) stock = KeyLayout.Load(stockPath);
        else
        {
            // The profile's key files name the stock aliases without defining them.
            stock = DefaultKeyLayout.Create();
            stock.ImportAliases(KeyLayout.Parse(StockAliases));
        }

        return DefaultKeyLayout.ApplyProfile(stock, File.ReadAllText(path, Encoding.Latin1), wheelMode);
    }

    private const string StockAliases = """
        [Engine.Input]
        Aliases[0]=(Command="Button bFire | Function Fire",Alias="Fire")
        Aliases[1]=(Command="Button bAltFire | Function AltFire",Alias="AltFire")
        Aliases[2]=(Command="Button BT_Up",Alias="AliasHaut")
        Aliases[3]=(Command="Button BT_Down",Alias="AliasBas")
        Aliases[4]=(Command="Button BT_Left",Alias="AliasGauche")
        Aliases[5]=(Command="Button BT_Right",Alias="AliasDroite")
        Aliases[6]=(Command="Button BT_PrevItem",Alias="AliasPrevItem")
        Aliases[7]=(Command="Button BT_NextItem",Alias="AliasNextItem")
        Aliases[8]=(Command="Function Jump | Axis aUp Speed=+280.0",Alias="Jump")
        Aliases[9]=(Command="Button bSpecialsChat",Alias="HackComm")
        Aliases[10]=(Command="InvButton bUseBackToWall",Alias="BTW")
        Aliases[11]=(Command="InvButton bFireMode",Alias="FireMode")
        Aliases[12]=(Command="InvButton bUsing",Alias="Use")
        Aliases[13]=(Command="Button bHoldBreath",Alias="HoldBreath")
        Aliases[14]=(Command="Button bCrouch",Alias="Crouch")
        Aliases[15]=(Command="Button bRoll",Alias="Roll")
        Aliases[16]=(Command="Button bReload",Alias="Reload")
        Aliases[17]=(Command="Button bROF",Alias="ROF")
        Aliases[18]=(Command="Button bZoomIn | Function ZoomIn",Alias="ZoomIn")
        Aliases[19]=(Command="Button bZoomOut | Function ZoomOut",Alias="ZoomOut")
        """;

    // ---- settings ------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts everything the install holds into <paramref name="s"/> (values it does not hold stay as they are) and
    /// returns one line per group taken over or left out, for the player.
    /// </summary>
    public List<string> Apply(GameSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var report = new List<string>();
        if (ConfigPath is not null) ApplyConfig(s, report);
        else report.Add($"No {ConfigName} (it is in the install's System folder): sensitivities, display and voice settings were not imported.");
        if (ProfilePath is not null) ApplyProfile(s, report);
        else report.Add("No game profile found: volumes, gear and key layouts were not imported.");
        s.Clamp();
        return report;
    }

    private void ApplyConfig(GameSettings s, List<string> report)
    {
        JsonElement c = _config;
        Look(c, "controls_spy_config_kbm", s.SpyKbm, spy: true);
        Look(c, "controls_merc_config_kbm", s.MercKbm, spy: false);
        report.Add("Mouse sensitivities for both roles.");

        {
            Look(c, "controls_spy_config_controller", s.SpyPad, spy: true);
            Look(c, "controls_merc_config_controller", s.MercPad, spy: false);
            PadSettings pad = s.Pad;
            // Controller support is only ever switched on: off in Enhanced says nothing about playing with a pad here.
            if (Flag(c, "controller_enabled") == true) pad.Enabled = true;
            Flag(c, "controller_binds_enabled", v => pad.Binds = v);
            Flag(c, "controller_left_stick_enabled", v => pad.Move = v);
            Flag(c, "controller_right_stick_enabled", v => pad.Aim = v);
            Flag(c, "controller_left_stick_slow_walk_enabled", v => pad.SlowWalk = v);
            Flag(c, "controller_left_stick_8_directional_spy", v => pad.SpyMove8 = v);
            Flag(c, "controller_left_stick_8_directional_merc", v => pad.MercMove8 = v);
            Flag(c, "controller_rumble_enabled", v => pad.Rumble = v);
            Number(c, "controller_rumble_strength", v => pad.RumbleStrength = v);
            Number(c, "controller_left_thumb_deadzone", v => pad.DeadzoneLeft = v);
            Number(c, "controller_right_thumb_deadzone", v => pad.DeadzoneRight = v);
            Number(c, "controller_right_thumb_vertical_sensitivty", v => pad.VerticalScale = v);
            Number(c, "controller_walk_threshold", v => pad.RunThreshold = v);
            report.Add("Controller sensitivities and tuning (button layouts are not stored by Reloaded).");
        }

        Flag(c, "use_split_spy_merc_keyboard", v => s.SplitKeyboard = v);
        Flag(c, "override_console_key", v => s.OverrideConsoleKey = v);

        Text(c, "display_mode", v => s.DisplayMode = v);
        Number(c, "display_monitor", v => s.Monitor = (int)v);
        Text(c, "window_position", v => s.WindowPosition = v);
        Flag(c, "widescreen", v => s.Widescreen = v);
        Flag(c, "widescreen_hud", v => s.WidescreenHud = v);
        Flag(c, "ws_er_small", v => s.WideErSmall = v);
        Number(c, "ws_fov", v => s.MercFovCap = v);
        Number(c, "resolution_scale", v => s.RenderScale = v);
        Number(c, "filter_level", v => s.FilterLevel = (int)v);
        Flag(c, "msaa", v => s.Msaa = v);
        Number(c, "msaa_samples", v => s.MsaaSamples = (int)v);
        Flag(c, "smaa", v => s.Smaa = v);
        Flag(c, "ambient_occlusion", v => s.AmbientOcclusion = v);
        Number(c, "shadow_quality", v => s.ShadowQuality = v);
        Flag(c, "shadow_edge_smoothing", v => s.ShadowEdgeSmoothing = v);
        Text(c, "low_latency_mode", v => s.LowLatencyMode = v);
        if (!Flag(c, "throttle_stop_v2", v => s.ThrottleStop = v)) Flag(c, "throttle_stop", v => s.ThrottleStop = v);
        // One cap here; Reloaded keeps one per role (the merc's, as older OpenSCCT settings files are read).
        if (!Number(c, "fps_merc", v => s.FpsLimit = (int)v)) Number(c, "fps_spy", v => s.FpsLimit = (int)v);
        report.Add("Display and graphics settings.");

        Flag(c, "show_fps", v => s.ShowFps = v);
        Flag(c, "show_net_stats", v => s.ShowNet = v);
        Flag(c, "show_net_loss", v => s.ShowNetLoss = v);
        Flag(c, "show_location", v => s.ShowLocation = v);
        Number(c, "hack_alpha", v => s.HackAlpha = (int)v);
        Flag(c, "classic_crosshair", v => s.ClassicCrosshair = v);
        Text(c, "active_map_playlist", v => s.ActiveMapPlaylist = v);
        report.Add("HUD settings.");

        Flag(c, "voice_push_to_talk", v => s.VoicePushToTalk = v);
        Flag(c, "voice_muted", v => s.VoiceMuted = v);
        Number(c, "voice_activation_threshold", v => s.VoiceThreshold = v);
        Number(c, "voice_activation_hold_ms", v => s.VoiceHoldMs = (int)v);
        Number(c, "voice_input_gain", v => s.VoiceInputGain = v);
        Flag(c, "voice_auto_gain", v => s.VoiceAutoGain = v);
        Flag(c, "voice_noise_suppression", v => s.VoiceNoiseSuppression = v);
        Text(c, "voice_noise_suppression_level", v => s.VoiceNoiseLevel = v);
        Flag(c, "voice_echo_cancellation", v => s.VoiceEchoCancellation = v);
        Number(c, "voice_jitter_frames", v => s.VoiceJitterFrames = (int)v);
        Number(c, "voice_output_volume", v => s.VoiceVolume = v);
        report.Add("Voice settings (the microphone device is not imported: choose it on the Voice page).");

        if (ProfilePath is null)
        {
            // Reloaded's copies of the profile's volumes.
            Number(c, "z_cache_sound_volume", v => SetSoundVolume(s, v, objectives: true));
            Number(c, "z_cache_music_volume", v => s.MusicVolume = s.MenuMusicVolume = v);
        }
    }

    /// <summary>One of Reloaded's control groups. The spy's has a free look and a gun look, each with a response.</summary>
    private static void Look(JsonElement config, string name, LookSettings look, bool spy)
    {
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty(name, out JsonElement g) || g.ValueKind != JsonValueKind.Object) return;
        if (spy)
        {
            Number(g, "free_look", v => look.FreeLook = v);
            if (!Number(g, "gun_look", v => look.Look = v)) Number(g, "look", v => look.Look = v);
            if (!Text(g, "free_look_response", v => look.LookResponse = v)) Text(g, "look_response", v => look.LookResponse = v);
            Text(g, "gun_look_response", v => look.GunLookResponse = v);
        }
        else
        {
            Number(g, "look", v => look.Look = v);
            Text(g, "look_response", v => look.LookResponse = v);
            Number(g, "scope_vertical", v => look.ScopeVertical = v);
            Number(g, "clutch_two", v => look.ClutchTwo = v);
            Flag(g, "clutch_two_enabled", v => look.ClutchTwoEnabled = v);
        }

        Number(g, "scope", v => look.Scope = v);
        Number(g, "vertical", v => look.Vertical = v);
        Number(g, "cam", v => look.Cam = v);
        Number(g, "clutch_one", v => look.ClutchOne = v);
        Flag(g, "clutch_one_enabled", v => look.ClutchOneEnabled = v);
        Flag(g, "swap_ls_lb", v => look.SwapLsLb = v);
        Flag(g, "swap_rs_rb", v => look.SwapRsRb = v);
    }

    /// <summary>The player name is left alone: it is set up in OpenSCCT itself.</summary>
    private void ApplyProfile(GameSettings s, List<string> report)
    {
        if (ProfileFlag("InvertY") is { } invertY) s.InvertY = invertY;
        if (ProfileFlag("InvertX") is { } invertX) s.InvertXSpy = invertX;
        if (ProfileFlag("bInFeet") is { } feet) s.HudFeet = feet;
        if (ProfileFlag("allowVoiceChat") is { } voice) s.VoiceEnabled = voice;
        if (ProfileInt("GraphPostQual") is { } post) s.PostQuality = post;
        if (WheelMode is { } wheel) s.MouseWheel = wheel;
        if (ProfileNumber("iEffectVolume") is { } sound) SetSoundVolume(s, sound, ProfileFlag("bObjectiveSounds") ?? true);
        if (ProfileNumber("iMusicVolume") is { } music) s.MusicVolume = s.MenuMusicVolume = music;
        // Reloaded's own voice volume wins when the config has one.
        if (ConfigPath is null && ProfileNumber("iVoiceChatVolume") is { } voiceVolume) s.VoiceVolume = voiceVolume;

        bool gear = Gear("ATTEquipment", s.Loadout.SpyGadgets) | Gear("DEFEquipment", s.Loadout.MercGadgets);
        if (ProfileClass("DEFWeapClass") is { } weapon && weapon.StartsWith("SBase.SWeapon", StringComparison.Ordinal))
        {
            s.Loadout.MercWeapon = weapon;
            gear = true;
        }

        report.Add($"Profile \"{ProfileName}\": inverted axes, volumes, mouse wheel{(gear ? ", gear" : "")}.");
    }

    /// <summary>The original has one effects volume; it sets the three OpenSCCT split it into.</summary>
    private static void SetSoundVolume(GameSettings s, float volume, bool objectives)
    {
        s.SoundVolume = s.MenuSoundVolume = volume;
        s.ObjectiveVolume = objectives ? volume : 0f;
    }

    /// <summary>The four gadget slots of a side, taken only when the profile names all four.</summary>
    private bool Gear(string key, string[] slots)
    {
        var classes = new string[LoadoutSettings.Slots];
        for (int i = 0; i < classes.Length; i++)
        {
            if (ProfileClass($"{key}[{i}]") is not { } gadget || !gadget.StartsWith("SBase.SGad", StringComparison.Ordinal)) return false;
            classes[i] = gadget;
        }

        if (slots.Length != classes.Length) return false;
        classes.CopyTo(slots, 0);
        return true;
    }

    // ---- value readers -------------------------------------------------------------------------------------

    private static bool? Flag(JsonElement group, string key) =>
        group.ValueKind == JsonValueKind.Object && group.TryGetProperty(key, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static bool Flag(JsonElement group, string key, Action<bool> set)
    {
        if (Flag(group, key) is not { } value) return false;
        set(value);
        return true;
    }

    private static bool Number(JsonElement group, string key, Action<float> set)
    {
        if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty(key, out JsonElement v) || v.ValueKind != JsonValueKind.Number ||
            !v.TryGetDouble(out double number) || !double.IsFinite(number)) return false;
        set((float)number);
        return true;
    }

    private static bool Text(JsonElement group, string key, Action<string> set)
    {
        if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty(key, out JsonElement v) || v.ValueKind != JsonValueKind.String ||
            v.GetString() is not { Length: > 0 } text) return false;
        set(text);
        return true;
    }

    private bool? ProfileFlag(string key) =>
        _profile.TryGetValue(key, out string? v) && bool.TryParse(v, out bool flag) ? flag : null;

    private int? ProfileInt(string key) =>
        _profile.TryGetValue(key, out string? v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;

    private float? ProfileNumber(string key) =>
        _profile.TryGetValue(key, out string? v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) && float.IsFinite(n) ? n : null;

    /// <summary>
    /// A class value as the profile writes it, <c>Class§(677)'SBase§(180).SGadSmokeGrenade§(707695)'</c> (each name
    /// followed by a marker byte and a number), as a plain class path, or null.
    /// </summary>
    private string? ProfileClass(string key)
    {
        if (!_profile.TryGetValue(key, out string? v)) return null;
        System.Text.RegularExpressions.Match m = Regex.Match(Regex.Replace(v, @"[^\x20-\x7E]\(\d+\)", ""), @"'([A-Za-z0-9_.]+)'");
        return m.Success ? m.Groups[1].Value : null;
    }
}
