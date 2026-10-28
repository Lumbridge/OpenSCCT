using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenScct.Game.Equipment;
using OpenScct.Net.Cosmetics;
using OpenScct.Provenance;

namespace OpenScctGame.Options;

/// <summary>
/// The player's chosen gear per side (an OpenSCCT addition, spec/additions/gear-change.md): class paths of the four
/// gadget slots and the merc's weapon. Read on this machine whenever the server asks for a kit, so a new choice goes
/// on at the next spawn (or at once while the server allows it). Defaults are the fixed kits (loadouts.md rule 2.1).
/// </summary>
public sealed class LoadoutSettings
{
    public const int Slots = 4;

    [JsonPropertyName("spy_gadgets")]
    public string[] SpyGadgets { get; set; } = DefaultSpy();

    [JsonPropertyName("merc_gadgets")]
    public string[] MercGadgets { get; set; } = DefaultMerc();

    [JsonPropertyName("merc_weapon")]
    public string MercWeapon { get; set; } = Loadouts.MercWeapon.Path ?? "";

    public static string[] DefaultSpy() => Array.ConvertAll(Loadouts.SpyFixedGadgets, c => c.Path ?? "");

    public static string[] DefaultMerc() => Array.ConvertAll(Loadouts.MercFixedGadgets, c => c.Path ?? "");

    /// <summary>The slots of a side.</summary>
    public string[] GadgetsOf(bool spy) => spy ? SpyGadgets : MercGadgets;

    internal void Clamp()
    {
        SpyGadgets = Fit(SpyGadgets, DefaultSpy());
        MercGadgets = Fit(MercGadgets, DefaultMerc());
        MercWeapon ??= "";
    }

    private static string[] Fit(string[]? slots, string[] defaults)
    {
        if (slots is null)
        {
            return defaults;
        }

        var fitted = new string[Slots];
        for (int i = 0; i < Slots; i++)
        {
            fitted[i] = i < slots.Length ? (slots[i] ?? "").Trim() : "";
        }

        return fitted;
    }
}

/// <summary>Look sensitivities of one side and device, as Reloaded's control groups hold them.</summary>
public sealed class LookSettings
{
    /// <summary>Spy: free look with the gun holstered (merc: unused).</summary>
    [JsonPropertyName("free_look")]
    public float FreeLook { get; set; } = 1.4f;

    /// <summary>Spy: look with the gun drawn. Merc: the look.</summary>
    [JsonPropertyName("look")]
    public float Look { get; set; } = 1f;

    [JsonPropertyName("scope")]
    public float Scope { get; set; } = 0.75f;

    /// <summary>Merc: the sniper scope's vertical scale (2.45 restores 1:1 from the stock 40.8 %).</summary>
    [JsonPropertyName("scope_vertical")]
    public float ScopeVertical { get; set; } = 2.45f;

    [JsonPropertyName("vertical")]
    public float Vertical { get; set; } = 1f;

    /// <summary>Sticky camera and camera network.</summary>
    [JsonPropertyName("cam")]
    public float Cam { get; set; } = 1f;

    /// <summary>Linear, CurveSoft, CurveMedium or CurveAggressive.</summary>
    [JsonPropertyName("look_response")]
    public string LookResponse { get; set; } = "Linear";

    internal void Clamp()
    {
        FreeLook = Range(FreeLook, 0.05f, 10f);
        Look = Range(Look, 0.05f, 10f);
        Scope = Range(Scope, 0.05f, 10f);
        ScopeVertical = Range(ScopeVertical, 0.05f, 10f);
        Vertical = Range(Vertical, 0.05f, 10f);
        Cam = Range(Cam, 0.05f, 10f);
        if (Array.IndexOf(GameSettings.Responses, LookResponse) < 0)
        {
            LookResponse = "Linear";
        }
    }

    internal static float Range(float v, float min, float max) => float.IsFinite(v) ? Math.Clamp(v, min, max) : min;
}

/// <summary>Native controller settings (Reloaded's controller_* keys).</summary>
public sealed class PadSettings
{
    /// <summary>On by default: a plugged-in controller works without a trip to the options.</summary>
    [JsonPropertyName("enabled")]
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT choice: on by default (Reloaded's controller_enabled default is false, rl:input.pad)")]
    public bool Enabled { get; set; } = true;

    /// <summary>The pad controls a layout binds, in the order the options list them (Xbox names).</summary>
    public static readonly string[] Controls =
    [
        "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "LS", "RS", "DpadUp", "DpadDown", "DpadLeft", "DpadRight", "Start", "Back",
    ];

    /// <summary>The menu command (<c>ShowMenu</c>; Escape when the layout binds no key to it).</summary>
    public const string MenuAction = "ShowMenu";

    /// <summary>The prefix of a binding that presses a key directly (the hub's own keys, which are not layout commands).</summary>
    public const string KeyPrefix = "Key:";

    /// <summary>The default pad layout: stock action names on the usual pad positions (Reloaded's own is not decoded).</summary>
    [Provenance(ProvenanceKind.Estimated, "Default pad layout per team not decoded (FEATURES open question 4)")]
    /// <remarks>The merc's has nothing on the spy-only back to wall (LB) and walk speed (LS) buttons.</remarks>
    public static Dictionary<string, string> DefaultButtons(bool merc = false) => new(StringComparer.Ordinal)
    {
        ["A"] = "Jump",
        ["B"] = "bCrouch",
        ["X"] = "bUsing",
        ["Y"] = "SwitchHeadset",
        ["LB"] = merc ? "" : "bUseBackToWall",
        ["RB"] = "bRoll",
        ["LT"] = "bAltFire",
        ["RT"] = "bFire",
        ["LS"] = merc ? "" : "ChangeSpeed",
        ["RS"] = "SwitchBinocularVision",
        ["DpadUp"] = "WheelUp",
        ["DpadDown"] = "WheelDown",
        ["DpadLeft"] = "bFireMode",
        ["DpadRight"] = "bReload",
        ["Start"] = MenuAction,
        ["Back"] = "DisplaySessionStatus",
    };

    /// <summary>The spy's pad layout: control name to action (a layout command or button, <see cref="KeyPrefix"/> + a key, or "" for none).</summary>
    [JsonPropertyName("spy_buttons")]
    public Dictionary<string, string> SpyButtons { get; set; } = DefaultButtons();

    [JsonPropertyName("merc_buttons")]
    public Dictionary<string, string> MercButtons { get; set; } = DefaultButtons(merc: true);

    public Dictionary<string, string> ButtonsOf(bool merc) => merc ? MercButtons : SpyButtons;

    [JsonPropertyName("move")]
    public bool Move { get; set; } = true;

    [JsonPropertyName("aim")]
    public bool Aim { get; set; } = true;

    [JsonPropertyName("binds")]
    public bool Binds { get; set; } = true;

    [JsonPropertyName("deadzone_left")]
    [Provenance(ProvenanceKind.Decoded, "rl:input.pad controller_left_thumb_deadzone 0.25")]
    public float DeadzoneLeft { get; set; } = 0.25f;

    [JsonPropertyName("deadzone_right")]
    [Provenance(ProvenanceKind.Decoded, "rl:input.pad controller_right_thumb_deadzone 0.15")]
    public float DeadzoneRight { get; set; } = 0.15f;

    [JsonPropertyName("vertical_scale")]
    [Provenance(ProvenanceKind.Decoded, "rl:input.pad controller_right_thumb_vertical_sensitivty 0.75")]
    public float VerticalScale { get; set; } = 0.75f;

    [JsonPropertyName("run_threshold")]
    [Provenance(ProvenanceKind.Decoded, "rl:input.pad controller_walk_threshold 0.95")]
    public float RunThreshold { get; set; } = 0.95f;

    [JsonPropertyName("rumble")]
    public bool Rumble { get; set; }

    [JsonPropertyName("rumble_strength")]
    public float RumbleStrength { get; set; } = 1f;

    /// <summary>Snap spy movement to eight directions.</summary>
    [JsonPropertyName("spy_move8")]
    public bool SpyMove8 { get; set; }

    [JsonPropertyName("merc_move8")]
    public bool MercMove8 { get; set; }

    /// <summary>Stick speed in look counts a second at full deflection (the pad's look scale before the side's multipliers).</summary>
    [JsonPropertyName("look_speed")]
    [Provenance(ProvenanceKind.Estimated, "Reloaded's stick look scaling is not decoded (FEATURES open question 1)")]
    public float LookSpeed { get; set; } = 900f;

    internal void Clamp()
    {
        DeadzoneLeft = LookSettings.Range(DeadzoneLeft, 0f, 0.9f);
        DeadzoneRight = LookSettings.Range(DeadzoneRight, 0f, 0.9f);
        VerticalScale = LookSettings.Range(VerticalScale, 0.1f, 3f);
        RunThreshold = LookSettings.Range(RunThreshold, 0.1f, 1f);
        RumbleStrength = LookSettings.Range(RumbleStrength, 0f, 1f);
        LookSpeed = LookSettings.Range(LookSpeed, 50f, 5000f);
        SpyButtons = CleanButtons(SpyButtons, merc: false);
        MercButtons = CleanButtons(MercButtons, merc: true);
    }

    /// <summary>Known controls only; a control the file leaves out gets its default (an empty value stays unbound).</summary>
    private static Dictionary<string, string> CleanButtons(Dictionary<string, string>? saved, bool merc)
    {
        Dictionary<string, string> clean = DefaultButtons(merc);
        if (saved is null)
        {
            return clean;
        }

        foreach (string control in Controls)
        {
            if (saved.TryGetValue(control, out string? action))
            {
                clean[control] = action ?? "";
            }
        }

        return clean;
    }
}

/// <summary>
/// The player's settings (spec/reloaded/FEATURES.md section 3: one <c>user://settings.json</c>, JSON with comments,
/// keys named after Reloaded's where the meaning is the same). Godot-free: the options screen edits it and
/// <c>SettingsApplier</c> puts it into effect. Defaults are Reloaded's first-run values.
/// </summary>
public sealed class GameSettings
{
    /// <summary>2: the frame caps can follow the monitor (the default) and vertical sync is off by default.</summary>
    public const int Version = 2;

    public static readonly string[] Responses = ["Linear", "CurveSoft", "CurveMedium", "CurveAggressive"];

    public static readonly string[] DisplayModes = ["BorderlessFullscreen", "Fullscreen", "Windowed"];

    /// <summary>Mouse sensitivity range of the profile (the options slider of the original).</summary>
    [Provenance(ProvenanceKind.Decoded, "MouseLook: profile MouseSensitivity clamped to 4..40, default 40")]
    public const float MinSensitivity = 4f;

    public const float MaxSensitivity = 40f;

    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 20: ws_fov range 95 to 112")]
    public const float MinMercFov = 95f;

    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 20; FEATURES ws_fov = 112")]
    public const float MaxMercFov = 112f;

    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md constants: fps caps 30 to 240")]
    public const int MinFps = DisplayFrameCap.Min;

    public const int MaxFps = DisplayFrameCap.Max;

    [JsonPropertyName("version")]
    public int FileVersion { get; set; } = Version;

    // ---- player and mouse --------------------------------------------------------------------------------------

    [JsonPropertyName("player_name")]
    public string PlayerName { get; set; } = "Player";

    /// <summary>The profile's MouseSensitivity (4 to 40); the game divides it by 10.</summary>
    [JsonPropertyName("mouse_sensitivity")]
    [Provenance(ProvenanceKind.Decoded, "DefaultProfile.ini MouseSensitivity=40.0")]
    public float MouseSensitivity { get; set; } = 40f;

    [JsonPropertyName("invert_y")]
    public bool InvertY { get; set; }

    /// <summary>controls_spy_config_kbm (free_look 1.4, gun look 1.0, scope 0.75, vertical 3.5, cam 1.0).</summary>
    [JsonPropertyName("controls_spy_kbm")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens controls_spy_config_kbm")]
    public LookSettings SpyKbm { get; set; } = new() { FreeLook = 1.4f, Look = 1f, Scope = 0.75f, Vertical = 3.5f, Cam = 1f };

    /// <summary>controls_merc_config_kbm (look 1.0, scope 0.75, scope_vertical 2.45, vertical 1.0, cam 1.0).</summary>
    [JsonPropertyName("controls_merc_kbm")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:input.sens controls_merc_config_kbm")]
    public LookSettings MercKbm { get; set; } = new() { FreeLook = 1f, Look = 1f, Scope = 0.75f, ScopeVertical = 2.45f, Vertical = 1f, Cam = 1f };

    /// <summary>controls_spy_config_controller (cam 1.8, free look 1.6 aggressive, gun look 1.2 soft, vertical 3.5).</summary>
    [JsonPropertyName("controls_spy_pad")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md appendix A controls_spy_config_controller")]
    public LookSettings SpyPad { get; set; } = new() { FreeLook = 1.6f, Look = 1.2f, Scope = 0.75f, Vertical = 3.5f, Cam = 1.8f, LookResponse = "CurveAggressive" };

    /// <summary>controls_merc_config_controller (look 1.8 medium, scope 0.75, scope_vertical 2.45, cam 1.0).</summary>
    [JsonPropertyName("controls_merc_pad")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md appendix A controls_merc_config_controller")]
    public LookSettings MercPad { get; set; } = new() { FreeLook = 1f, Look = 1.8f, Scope = 0.75f, ScopeVertical = 2.45f, Vertical = 1f, Cam = 1f, LookResponse = "CurveMedium" };

    [JsonPropertyName("controller")]
    public PadSettings Pad { get; set; } = new();

    /// <summary>The chosen gear (spec/additions/gear-change.md).</summary>
    [JsonPropertyName("loadout")]
    public LoadoutSettings Loadout { get; set; } = new();

    /// <summary>The chosen spy cosmetics (OpenScct.Net.Cosmetics catalogue ids, one per slot; none is the retail look).</summary>
    [JsonPropertyName("cosmetics_spy")]
    public string[] CosmeticsSpy { get; set; } = [];

    /// <summary>The chosen merc cosmetics (catalogue ids).</summary>
    [JsonPropertyName("cosmetics_merc")]
    public string[] CosmeticsMerc { get; set; } = [];

    /// <summary>Draw every other player in the retail look (their cosmetics are not shown on this machine).</summary>
    [JsonPropertyName("cosmetics_hide_others")]
    public bool CosmeticsHideOthers { get; set; }

    /// <summary>Keep this player's cosmetics to themselves: servers get the retail look, only this machine draws the chosen one.</summary>
    [JsonPropertyName("cosmetics_hide_mine")]
    public bool CosmeticsHideMine { get; set; }

    /// <summary>The look sent to servers for a side: the chosen one, or retail while <see cref="CosmeticsHideMine"/> is on.</summary>
    public CosmeticLook SharedLook(CosmeticSide side) =>
        CosmeticsHideMine ? CosmeticLook.Default : OwnLook(side);

    /// <summary>The look chosen in the Armoury for a side.</summary>
    public CosmeticLook OwnLook(CosmeticSide side) =>
        CosmeticLook.FromIds(side, side == CosmeticSide.Spy ? CosmeticsSpy : CosmeticsMerc);

    /// <summary>The title shown under the name (OpenScct.Net.Cosmetics.TitleCatalog id), or null for none.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // ---- display -------------------------------------------------------------------------------------------

    /// <summary>Fullscreen, BorderlessFullscreen or Windowed.</summary>
    [JsonPropertyName("display_mode")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.modes display_mode BorderlessFullscreen")]
    public string DisplayMode { get; set; } = "BorderlessFullscreen";

    /// <summary>Monitor, numbered from 1.</summary>
    [JsonPropertyName("display_monitor")]
    public int Monitor { get; set; } = 1;

    /// <summary>Window size in windowed mode and the exclusive fullscreen mode, "WxH"; empty follows the desktop.</summary>
    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = "";

    /// <summary>Vertical sync; off as the original's render device (UseVSync=False), whose frames Reloaded's limiter paces.</summary>
    [JsonPropertyName("vsync")]
    [Provenance(ProvenanceKind.Decoded, "System/Default.ini [D3DDrv.D3DRenderDevice] UseVSync=False")]
    public bool VSync { get; set; }

    /// <summary>Reloaded's widescreen: Hor+ view and the anchored HUD; off stretches the 4:3 picture and HUD (Options/ViewFov).</summary>
    [JsonPropertyName("widescreen")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.widescreen widescreen = true")]
    public bool Widescreen { get; set; } = true;

    /// <summary>ws_fov: the merc first-person field of view cap in degrees, horizontal at the current aspect.</summary>
    [JsonPropertyName("ws_fov")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.widescreen ws_fov = 112.0")]
    public float MercFovCap { get; set; } = 112f;

    /// <summary>
    /// The frame cap in game as a spy (Reloaded's fps_spy, 30 to 240); -1 follows the monitor (the default) and 0 is
    /// unlimited, both additions (Options/DisplayFrameCap). Reloaded's first run has 120.
    /// </summary>
    [JsonPropertyName("fps_spy")]
    public int FpsSpy { get; set; } = DisplayFrameCap.Monitor;

    /// <summary>The frame cap as a merc, in the menus and the lobby (Reloaded's fps_merc); values as <see cref="FpsSpy"/>.</summary>
    [JsonPropertyName("fps_merc")]
    public int FpsMerc { get; set; } = DisplayFrameCap.Monitor;

    [JsonPropertyName("resolution_scale")]
    public float RenderScale { get; set; } = 1f;

    // ---- audio ---------------------------------------------------------------------------------------------

    [JsonPropertyName("master_volume")]
    public float MasterVolume { get; set; } = 1f;

    /// <summary>Sound effects (z_cache_sound_volume 0.5 on a first run).</summary>
    [JsonPropertyName("sound_volume")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:audio.cache z_cache_sound_volume 0.5")]
    public float SoundVolume { get; set; } = 0.5f;

    [JsonPropertyName("music_volume")]
    public float MusicVolume { get; set; } = 0.5f;

    /// <summary>Other players' voices (voice_output_volume).</summary>
    [JsonPropertyName("voice_output_volume")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md appendix A voice_output_volume 1.0")]
    public float VoiceVolume { get; set; } = 1f;

    // ---- voice (Reloaded's voice page: spec/game/lobby-and-match-creation.md, docs/VOICE.md) -----------------

    /// <summary>"Enable voice chat": the game's own Allow VoiceChat profile setting. Off: hear nobody, nobody hears you.</summary>
    [JsonPropertyName("allow_voice_chat")]
    [Provenance(ProvenanceKind.Decoded, "DefaultProfile.ini allowVoiceChat=True; Reloaded voice page 'Enable voice chat'")]
    public bool VoiceEnabled { get; set; } = true;

    /// <summary>Transmit only while Push To Talk is held, instead of the activation threshold.</summary>
    [JsonPropertyName("voice_push_to_talk")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.activation voice_push_to_talk = false")]
    public bool VoicePushToTalk { get; set; }

    /// <summary>Mute Microphone: nothing is sent, everyone is still heard.</summary>
    [JsonPropertyName("voice_muted")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.mix voice_muted = false")]
    public bool VoiceMuted { get; set; }

    /// <summary>How loud the microphone must be to transmit, 0 (always) to 1 (never).</summary>
    [JsonPropertyName("voice_activation_threshold")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.activation voice_activation_threshold = 0.02")]
    public float VoiceThreshold { get; set; } = 0.02f;

    /// <summary>"Release time": how long transmission goes on after you stop speaking, in milliseconds.</summary>
    [JsonPropertyName("voice_activation_hold_ms")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.activation voice_activation_hold_ms = 300")]
    public int VoiceHoldMs { get; set; } = 300;

    /// <summary>"Microphone Volume": gain before encoding (1 = unchanged).</summary>
    [JsonPropertyName("voice_input_gain")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.gain voice_input_gain = 1.0")]
    public float VoiceInputGain { get; set; } = 1f;

    /// <summary>"Buffering": the jitter buffer in 20 ms frames, 1 to 12.</summary>
    [JsonPropertyName("voice_jitter_frames")]
    [Provenance(ProvenanceKind.Estimated, "docs/VOICE.md: default 3 (Reloaded ships 2 to 4), range 1 to 12")]
    public int VoiceJitterFrames { get; set; } = 3;

    /// <summary>The microphone by name; empty follows the system's choice.</summary>
    [JsonPropertyName("voice_input_device")]
    public string VoiceInputDevice { get; set; } = "";

    // ---- HUD -----------------------------------------------------------------------------------------------

    /// <summary>The connected players list (F10) is on by default and remembered.</summary>
    [JsonPropertyName("show_scoreboard")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:qol.scoreboard show_scoreboard = true")]
    public bool ShowScoreboard { get; set; } = true;

    [JsonPropertyName("show_ping")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:qol.scoreboard show_ping first-run false")]
    public bool ShowPing { get; set; }

    [JsonPropertyName("show_fps")]
    public bool ShowFps { get; set; }

    /// <summary>Network readout under the frame-rate readout: ping, jitter, loss, bandwidth, corrections (rl:net.stats).</summary>
    [JsonPropertyName("show_net")]
    public bool ShowNet { get; set; }

    // ---- network -------------------------------------------------------------------------------------------

    /// <summary>
    /// Fire prediction (rl:net.fireprediction): your own bullet impacts show at once instead of a round trip later.
    /// On by default, as Reloaded always predicts; takes effect from the next server you join.
    /// </summary>
    [JsonPropertyName("fire_prediction")]
    public bool FirePrediction { get; set; } = true;

    // ---- Discord -------------------------------------------------------------------------------------------

    /// <summary>Shows what the player is doing on their Discord profile, with Join for friends (docs/DISCORD.md).</summary>
    [JsonPropertyName("discord_presence")]
    public bool DiscordPresence { get; set; } = true;

    /// <summary>Where the settings came from, for the options screen (not saved).</summary>
    [JsonIgnore]
    public string LoadNote { get; set; } = "";

    /// <summary>
    /// A file from an older version: version 1 wrote Reloaded's first-run caps (120 and 120) for a player who never
    /// chose one, so those follow the monitor now; any other cap is the player's and stays.
    /// </summary>
    private void Upgrade()
    {
        if (FileVersion < 2 && FpsSpy == 120 && FpsMerc == 120)
        {
            FpsSpy = DisplayFrameCap.Monitor;
            FpsMerc = DisplayFrameCap.Monitor;
        }

        FileVersion = Version;
    }

    /// <summary>Puts every value back in its range.</summary>
    public void Clamp()
    {
        PlayerName = PlayerName.Trim();
        if (PlayerName.Length == 0)
        {
            PlayerName = "Player";
        }

        if (PlayerName.Length > 24)
        {
            PlayerName = PlayerName[..24];
        }

        MouseSensitivity = LookSettings.Range(MouseSensitivity, MinSensitivity, MaxSensitivity);
        SpyKbm ??= new LookSettings();
        MercKbm ??= new LookSettings();
        SpyPad ??= new LookSettings();
        MercPad ??= new LookSettings();
        Pad ??= new PadSettings();
        Loadout ??= new LoadoutSettings();
        CosmeticsSpy ??= [];
        CosmeticsMerc ??= [];
        Loadout.Clamp();
        SpyKbm.Clamp();
        MercKbm.Clamp();
        SpyPad.Clamp();
        MercPad.Clamp();
        Pad.Clamp();
        if (Array.IndexOf(DisplayModes, DisplayMode) < 0)
        {
            DisplayMode = DisplayModes[0];
        }

        Monitor = Math.Max(1, Monitor);
        Resolution = ParseResolution(Resolution) is { } r ? FormatResolution(r.Width, r.Height) : "";
        MercFovCap = LookSettings.Range(MercFovCap, MinMercFov, MaxMercFov);
        FpsSpy = ClampFps(FpsSpy);
        FpsMerc = ClampFps(FpsMerc);
        RenderScale = LookSettings.Range(RenderScale, 0.5f, 1f);
        MasterVolume = LookSettings.Range(MasterVolume, 0f, 1f);
        SoundVolume = LookSettings.Range(SoundVolume, 0f, 1f);
        MusicVolume = LookSettings.Range(MusicVolume, 0f, 1f);
        VoiceVolume = LookSettings.Range(VoiceVolume, 0f, 1f);
        VoiceThreshold = LookSettings.Range(VoiceThreshold, 0f, 1f);
        VoiceHoldMs = Math.Clamp(VoiceHoldMs, 0, 2000);
        VoiceInputGain = LookSettings.Range(VoiceInputGain, 0f, 8f);
        VoiceJitterFrames = Math.Clamp(VoiceJitterFrames, 1, 12);
        VoiceInputDevice ??= "";
    }

    /// <summary>A frame cap: below 0 follows the monitor, 0 is unlimited, otherwise 30 to 240.</summary>
    public static int ClampFps(int fps) => DisplayFrameCap.Clamp(fps);

    /// <summary>"1920x1080" to (1920, 1080), or null.</summary>
    public static (int Width, int Height)? ParseResolution(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.ToLowerInvariant().Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && w >= 640 && h >= 480 &&
            w <= 16384 && h <= 16384)
        {
            return (w, h);
        }

        return null;
    }

    public static string FormatResolution(int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");

    /// <summary>The look group of a side and device.</summary>
    public LookSettings LookOf(bool merc, bool pad) => merc ? (pad ? MercPad : MercKbm) : (pad ? SpyPad : SpyKbm);

    /// <summary>The frame cap of a side (Reloaded picks it from the local player's team).</summary>
    public int FpsFor(bool merc) => merc ? FpsMerc : FpsSpy;

    // ---- the file ------------------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,

        // A group the file names only in part keeps its side's defaults for the rest.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    /// <summary>The settings as the file holds them.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads settings; unknown keys are ignored and missing ones keep their defaults.</summary>
    public static GameSettings FromJson(string json)
    {
        GameSettings s = JsonSerializer.Deserialize<GameSettings>(json, Json) ?? new GameSettings();
        s.Upgrade();
        s.Clamp();
        return s;
    }

    /// <summary>
    /// Loads <paramref name="path"/>: missing gives the defaults; unreadable JSON is kept as <c>.bak</c> and the
    /// defaults are used (the note says so).
    /// </summary>
    public static GameSettings Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
        {
            return new GameSettings { LoadNote = "defaults" };
        }

        try
        {
            GameSettings s = FromJson(File.ReadAllText(path));
            s.LoadNote = path;
            return s;
        }
        catch (JsonException e)
        {
            TryBackup(path);
            return new GameSettings { LoadNote = $"defaults: {Path.GetFileName(path)} was not valid ({e.Message}); kept as .bak" };
        }
        catch (IOException e)
        {
            return new GameSettings { LoadNote = "defaults: " + e.Message };
        }
        catch (UnauthorizedAccessException e)
        {
            return new GameSettings { LoadNote = "defaults: " + e.Message };
        }
    }

    /// <summary>Writes the file (through a temporary file, so a crash never leaves half a file).</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Clamp();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, "// OpenSCCT settings. JSON with comments; keys follow Reloaded's where they mean the same.\n" + ToJson() + "\n");
        File.Move(temp, path, overwrite: true);
    }

    private static void TryBackup(string path)
    {
        try
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A copy (the options screen edits a copy and saves it).</summary>
    public GameSettings Clone() => FromJson(ToJson());

    /// <summary>Every key the file holds, for the settings console and tests.</summary>
    public static IReadOnlyList<string> Keys()
    {
        var keys = new List<string>();
        foreach (System.Reflection.PropertyInfo p in typeof(GameSettings).GetProperties())
        {
            if (p.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false) is [JsonPropertyNameAttribute name])
            {
                keys.Add(name.Name);
            }
        }

        return keys;
    }
}
