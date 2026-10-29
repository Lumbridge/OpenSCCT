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

    /// <summary>The first sensitivity clutch: a held button multiplies the look by this (rl:input.clutch).</summary>
    [JsonPropertyName("clutch_one")]
    public float ClutchOne { get; set; }

    [JsonPropertyName("clutch_one_enabled")]
    public bool ClutchOneEnabled { get; set; }

    /// <summary>The second clutch (Reloaded gives it to the merc only).</summary>
    [JsonPropertyName("clutch_two")]
    public float ClutchTwo { get; set; }

    [JsonPropertyName("clutch_two_enabled")]
    public bool ClutchTwoEnabled { get; set; }

    /// <summary>Controller groups: the left stick click and the left bumper trade places (rl:input.pad).</summary>
    [JsonPropertyName("swap_ls_lb")]
    public bool SwapLsLb { get; set; }

    /// <summary>Controller groups: the right stick click and the right bumper trade places (rl:input.pad).</summary>
    [JsonPropertyName("swap_rs_rb")]
    public bool SwapRsRb { get; set; }

    /// <summary>
    /// The pad control that is read for <paramref name="control"/>: with a swap on, LB reads the left stick click and
    /// LS the left bumper (RB and RS the same with the right swap).
    /// </summary>
    public string PhysicalControl(string control) => control switch
    {
        "LB" when SwapLsLb => "LS",
        "LS" when SwapLsLb => "LB",
        "RB" when SwapRsRb => "RS",
        "RS" when SwapRsRb => "RB",
        _ => control,
    };

    /// <summary>The multiplier of the clutches held (1 with none): each enabled clutch with a value above 0 counts.</summary>
    public float ClutchOf(bool one, bool two) =>
        (one && ClutchOneEnabled && ClutchOne > 0f ? ClutchOne : 1f) * (two && ClutchTwoEnabled && ClutchTwo > 0f ? ClutchTwo : 1f);

    internal void Clamp()
    {
        ClutchOne = Range(ClutchOne, 0f, 10f);
        ClutchTwo = Range(ClutchTwo, 0f, 10f);
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

    /// <summary>The pad bind held for the first sensitivity clutch (Reloaded's "Right Clutch" for the spy, "Left Clutch" for the merc).</summary>
    public const string ClutchOneAction = "Clutch1";

    /// <summary>The pad bind held for the second clutch (the merc's).</summary>
    public const string ClutchTwoAction = "Clutch2";

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

    /// <summary>Controller vibration (Reloaded's controller_rumble_enabled, off on a first run).</summary>
    [JsonPropertyName("rumble")]
    [Provenance(ProvenanceKind.Decoded, "rl:input.rumble controller_rumble_enabled = false")]
    public bool Rumble { get; set; }

    /// <summary>The vibration's strength, 0 to 1 (controller_rumble_strength).</summary>
    [JsonPropertyName("rumble_strength")]
    [Provenance(ProvenanceKind.Decoded, "rl:input.rumble controller_rumble_strength = 1.0")]
    public float RumbleStrength { get; set; } = 1f;

    /// <summary>A part push of the left stick walks slower (Reloaded's analog slow walk); off, only its direction counts.</summary>
    [JsonPropertyName("slow_walk")]
    [Provenance(ProvenanceKind.Decoded, "rl:input.pad controller_left_stick_slow_walk_enabled = true")]
    public bool SlowWalk { get; set; } = true;

    /// <summary>
    /// The controller prompt was answered (Reloaded's first-run "Would you like to enable native controller support?"):
    /// asked once, when a controller is connected while controller support is off.
    /// </summary>
    [JsonPropertyName("prompted")]
    public bool Prompted { get; set; }

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

    /// <summary>controls_spy_config_controller (cam 1.8, free look 1.6 aggressive, gun look 1.2 soft, vertical 3.5, clutch 2.0 on).</summary>
    [JsonPropertyName("controls_spy_pad")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md appendix A controls_spy_config_controller")]
    public LookSettings SpyPad { get; set; } = new()
    {
        FreeLook = 1.6f, Look = 1.2f, Scope = 0.75f, Vertical = 3.5f, Cam = 1.8f, LookResponse = "CurveAggressive",
        ClutchOne = 2f, ClutchOneEnabled = true,
    };

    /// <summary>controls_merc_config_controller (look 1.8 medium, scope 0.75, scope_vertical 2.45, cam 1.0, clutches 0.5 and 2.0 on).</summary>
    [JsonPropertyName("controls_merc_pad")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md appendix A controls_merc_config_controller")]
    public LookSettings MercPad { get; set; } = new()
    {
        FreeLook = 1f, Look = 1.8f, Scope = 0.75f, ScopeVertical = 2.45f, Vertical = 1f, Cam = 1f, LookResponse = "CurveMedium",
        ClutchOne = 0.5f, ClutchOneEnabled = true, ClutchTwo = 2f, ClutchTwoEnabled = true,
    };

    [JsonPropertyName("controller")]
    public PadSettings Pad { get; set; } = new();

    /// <summary>
    /// The keyboard or mouse key (UE2 name, as the key layouts write it: "LeftAlt", "MiddleMouse") held for the first
    /// sensitivity clutch; empty for none. The controller's clutches are pad binds (<see cref="PadSettings.ClutchOneAction"/>).
    /// </summary>
    [JsonPropertyName("clutch_one_key")]
    public string ClutchOneKey { get; set; } = "";

    /// <summary>The key held for the second clutch (the merc's).</summary>
    [JsonPropertyName("clutch_two_key")]
    public string ClutchTwoKey { get; set; } = "";

    /// <summary>Profile InvertX: the spy's free look turns the other way left and right (Enhanced's "Invert X Axis (Spy)").</summary>
    [JsonPropertyName("invert_x_spy")]
    [Provenance(ProvenanceKind.Decoded, "SPlayerProfile InvertX (no default: false); SetPadInvertX sets bInvertHorizontalAxisCamSpy")]
    public bool InvertXSpy { get; set; }

    /// <summary>
    /// Drawing the Shocker turns the spy toward the nearest merc in front of him (the stock lock-on snap); Enhanced lets
    /// it be turned off (enh:rules.shocker_snap).
    /// </summary>
    [JsonPropertyName("shocker_snap")]
    public bool ShockerSnap { get; set; } = true;

    /// <summary>The mouse wheel: "Gadget" (profile SelectedMouse 0, changes gadgets) or "Speed" (1, walk and run).</summary>
    [JsonPropertyName("mouse_wheel")]
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT choice: Gadget, as the rewrite has always played; DefaultProfile.ini has SelectedMouse=1 (Speed)")]
    public string MouseWheel { get; set; } = "Gadget";

    public static readonly string[] WheelModes = ["Gadget", "Speed"];

    /// <summary>
    /// Separate spy and mercenary key layouts (rl:input.keyboard). Off, both sides play the spy's layout
    /// (svm_keys_spy.ini) and a change on either side changes both.
    /// </summary>
    [JsonPropertyName("use_split_spy_merc_keyboard")]
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT choice: on, so layouts saved before the toggle existed keep both sides (Reloaded's first-run default is false, Ryan's config true)")]
    public bool SplitKeyboard { get; set; } = true;

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

    /// <summary>Reloaded's widescreen: the Hor+ view; off stretches the 4:3 picture (Options/ViewFov).</summary>
    [JsonPropertyName("widescreen")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.widescreen widescreen = true")]
    public bool Widescreen { get; set; } = true;

    /// <summary>Reloaded's widescreen HUD: each HUD area keeps the 4:3 scale, anchored to its side; off stretches it as stock.</summary>
    [JsonPropertyName("widescreen_hud")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.hud widescreen_hud = true")]
    public bool WidescreenHud { get; set; } = true;

    /// <summary>Reloaded's ws_er_small: Enhanced Reality icons keep their 4:3 size on a wide screen instead of growing.</summary>
    [JsonPropertyName("ws_er_small")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.hud ws_er_small = true")]
    public bool WideErSmall { get; set; } = true;

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

    /// <summary>Where a window goes in windowed mode: Default (the screen's top left), Centered or Stretched (the whole screen).</summary>
    [JsonPropertyName("window_position")]
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT choice: Centered, as the rewrite placed windows; Reloaded's first run has Stretched (rl:gfx.modes)")]
    public string WindowPosition { get; set; } = "Centered";

    public static readonly string[] WindowPositions = ["Default", "Centered", "Stretched"];

    /// <summary>Anisotropic filtering of the textures: 0 (off), 2, 4, 8 or 16 (Reloaded's filter_level, first run 4).</summary>
    [JsonPropertyName("filter_level")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.filtering filter_level first-run 4, range 0 to 16")]
    public int FilterLevel { get; set; } = 4;

    /// <summary>Multisample anti-aliasing of the 3D scene (Reloaded's msaa, first run off); masked surfaces then use alpha to coverage (from the next map).</summary>
    [JsonPropertyName("msaa")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.aa msaa first-run false")]
    public bool Msaa { get; set; }

    /// <summary>MSAA samples per pixel: 2, 4 or 8 (Reloaded's msaa_samples, first run 4).</summary>
    [JsonPropertyName("msaa_samples")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.aa msaa_samples first-run 4")]
    public int MsaaSamples { get; set; } = 4;

    /// <summary>SMAA over the finished frame (Reloaded's smaa, first run off); it combines with MSAA.</summary>
    [JsonPropertyName("smaa")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.aa smaa first-run false")]
    public bool Smaa { get; set; }

    /// <summary>Shadow map scale (Reloaded's shadow_quality, 0.5 to 16, first run 3): a spot light's map is 256 x this texels high.</summary>
    [JsonPropertyName("shadow_quality")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.shadows shadow_quality first-run 3.0, range 0.5 to 16.0")]
    public float ShadowQuality { get; set; } = 3f;

    /// <summary>Soft shadow edges (eight blended lookups on a disc); off keeps the stock single lookup.</summary>
    [JsonPropertyName("shadow_edge_smoothing")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.shadows shadow_edge_smoothing = true")]
    public bool ShadowEdgeSmoothing { get; set; } = true;

    /// <summary>
    /// The original Video Options' "Post-Render Quality" (SPlayerProfile.GraphPostQual: 0 Normal, 1 High, default 1).
    /// What the native GRAPHICPOSTQUALITY command changes is not decoded; the rewrite's Normal leaves the glow out of
    /// the normal view's composite (the gate still runs), High draws it.
    /// </summary>
    [JsonPropertyName("post_quality")]
    [Provenance(ProvenanceKind.Estimated, "SPlayerProfile.GraphPostQual (DefaultProfile.ini 1); its effect (native GRAPHICPOSTQUALITY, clamped 0..1) not decoded: Normal drops the glow")]
    public int PostQuality { get; set; } = 1;

    /// <summary>Video_Settings' sQuality captions for the post-render row (Normal, High).</summary>
    public static readonly string[] PostQualities = ["Normal", "High"];

    /// <summary>"Reduce input lag": Off, On (one frame queued) or Ultra (one frame queued, two swap chain images). Needs a restart.</summary>
    [JsonPropertyName("low_latency_mode")]
    [Provenance(ProvenanceKind.Estimated, "rl:gfx.pacing: Reloaded's default is On; the rewrite starts Off until frame queue 1 is measured on Vulkan and D3D12")]
    public string LowLatencyMode { get; set; } = "Off";

    public static readonly string[] LatencyModes = ["Off", "On", "Ultra"];

    /// <summary>Windows: opt the game out of power throttling (EcoQoS) at start (Reloaded's throttle_stop, experimental).</summary>
    [JsonPropertyName("throttle_stop")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:gfx.system throttle_stop = false")]
    public bool ThrottleStop { get; set; }

    // ---- audio ---------------------------------------------------------------------------------------------

    [JsonPropertyName("master_volume")]
    public float MasterVolume { get; set; } = 1f;

    /// <summary>Sound effects (z_cache_sound_volume 0.5 on a first run).</summary>
    [JsonPropertyName("sound_volume")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:audio.cache z_cache_sound_volume 0.5")]
    public float SoundVolume { get; set; } = 0.5f;

    [JsonPropertyName("music_volume")]
    public float MusicVolume { get; set; } = 0.5f;

    /// <summary>Profile bObjectiveSounds: the objective jingles play (Enhanced's "Objective Sounds").</summary>
    [JsonPropertyName("objective_sounds")]
    [Provenance(ProvenanceKind.Decoded, "SPlayerProfile defaults bObjectiveSounds=True")]
    public bool ObjectiveSounds { get; set; } = true;

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

    /// <summary>"Auto-Adjust Mic Volume": automatic gain; Microphone Volume is not used while it is on.</summary>
    [JsonPropertyName("voice_auto_gain")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.gain voice_auto_gain = true")]
    public bool VoiceAutoGain { get; set; } = true;

    /// <summary>"Noise suppression": steady background noise removed before the activation gate.</summary>
    [JsonPropertyName("voice_noise_suppression")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.noise voice_noise_suppression = true")]
    public bool VoiceNoiseSuppression { get; set; } = true;

    /// <summary>How hard noise is suppressed: Low (15 dB), Standard (30), High (45) or Extreme (60).</summary>
    [JsonPropertyName("voice_noise_suppression_level")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.noise voice_noise_suppression_level = \"Standard\"; Low 15, Standard 30, High 45, Extreme 60 dB")]
    public string VoiceNoiseLevel { get; set; } = "Standard";

    /// <summary>The suppression levels in the order the Voice page lists them.</summary>
    public static readonly string[] NoiseLevels = ["Low", "Standard", "High", "Extreme"];

    /// <summary>The most attenuation of a suppression level, in dB.</summary>
    public static int NoiseLevelDecibels(string level) => level switch
    {
        "Low" => 15,
        "High" => 45,
        "Extreme" => 60,
        _ => 30,
    };

    /// <summary>"Echo cancellation" (experimental): other players' voices taken back out of the microphone.</summary>
    [JsonPropertyName("voice_echo_cancellation")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:voice.echo voice_echo_cancellation = false")]
    public bool VoiceEchoCancellation { get; set; }

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

    /// <summary>
    /// The team size at which the connected players list splits into a column per team, sized on the bigger team
    /// (Reloaded's scoreboard_split_team_size, 1 to 4).
    /// </summary>
    [JsonPropertyName("scoreboard_split_team_size")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:qol.scoreboard scoreboard_split_team_size = 3 (1 to 4)")]
    public int ScoreboardSplitTeamSize { get; set; } = 3;

    [JsonPropertyName("show_fps")]
    public bool ShowFps { get; set; }

    /// <summary>Network readout under the frame-rate readout: ping, jitter, loss, bandwidth, corrections (rl:net.stats).</summary>
    [JsonPropertyName("show_net")]
    public bool ShowNet { get; set; }

    /// <summary>Packet loss received and sent in the network readout (Reloaded's show_net_loss, added after v4.0b).</summary>
    [JsonPropertyName("show_net_loss")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:net.stats show_net_loss = false")]
    public bool ShowNetLoss { get; set; }

    /// <summary>The name of the place the player is in (the map's zone and volume names), at the top of the screen.</summary>
    [JsonPropertyName("show_location")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:qol.location show_location = true")]
    public bool ShowLocation { get; set; } = true;

    /// <summary>Enhanced Reality icon opacity while its objective is being hacked, 0 to 255 (shown as a percentage).</summary>
    [JsonPropertyName("hack_alpha")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:qol.hackalpha hack_alpha = 178")]
    public int HackAlpha { get; set; } = 178;

    /// <summary>The merc crosshair at the original off-centre spot (3 units left and 3 down) instead of centred.</summary>
    [JsonPropertyName("classic_crosshair")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:qol.crosshair classic_crosshair = false; Reloaded.Core 0x100a9140 / 0x100a9160")]
    public bool ClassicCrosshair { get; set; }

    /// <summary>The merc's CS2-style custom crosshair (off: the original reticle).</summary>
    [JsonPropertyName("crosshair")]
    public CrosshairSettings Crosshair { get; set; } = new();

    /// <summary>Profile bInFeet: the HUD's distances in feet instead of metres.</summary>
    [JsonPropertyName("hud_feet")]
    [Provenance(ProvenanceKind.Decoded, "SPlayerProfile bInFeet (no default: false, metres)")]
    public bool HudFeet { get; set; }

    // ---- network -------------------------------------------------------------------------------------------

    /// <summary>
    /// Fire prediction (rl:net.fireprediction): your own bullet impacts show at once instead of a round trip later.
    /// On by default, as Reloaded always predicts; takes effect from the next server you join.
    /// </summary>
    [JsonPropertyName("fire_prediction")]
    public bool FirePrediction { get; set; } = true;

    /// <summary>
    /// The map playlist the host's map lists show and the Choose Map arrows step through (rl:qol.lobbymaps): a playlist
    /// name of the map catalog (Standard, Extended, Classic, 3v3, Merc Deathmatch) or "All" (every installed map).
    /// </summary>
    [JsonPropertyName("active_map_playlist")]
    [Provenance(ProvenanceKind.Decoded, "spec/reloaded/FEATURES.md rl:qol.lobbymaps active_map_playlist first-run Standard")]
    public string ActiveMapPlaylist { get; set; } = "Standard";

    // ---- Discord -------------------------------------------------------------------------------------------

    /// <summary>Shows what the player is doing on their Discord profile, with Join for friends (docs/DISCORD.md).</summary>
    [JsonPropertyName("discord_presence")]
    public bool DiscordPresence { get; set; } = true;

    // ---- console -------------------------------------------------------------------------------------------

    /// <summary>
    /// The developer console opens on the key left of 1 (backtick or tilde, by its place on the keyboard, whatever the
    /// layout) instead of Chaos Theory's own console key, the apostrophe key (rl:input.console).
    /// </summary>
    [JsonPropertyName("override_console_key")]
    public bool OverrideConsoleKey { get; set; } = true;

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
        Crosshair ??= new CrosshairSettings();
        Crosshair.Clamp();
        Loadout.Clamp();
        SpyKbm.Clamp();
        MercKbm.Clamp();
        SpyPad.Clamp();
        MercPad.Clamp();
        Pad.Clamp();
        ClutchOneKey = (ClutchOneKey ?? "").Trim();
        ClutchTwoKey = (ClutchTwoKey ?? "").Trim();
        MouseWheel = OneOf(WheelModes, MouseWheel);
        WindowPosition = OneOf(WindowPositions, WindowPosition, "Centered");
        LowLatencyMode = OneOf(LatencyModes, LowLatencyMode);
        FilterLevel = FilterLevel <= 0 ? 0 : FilterLevel <= 2 ? 2 : FilterLevel <= 4 ? 4 : FilterLevel <= 8 ? 8 : 16;
        ShadowQuality = LookSettings.Range(ShadowQuality, 0.5f, 16f);
        PostQuality = Math.Clamp(PostQuality, 0, 1);
        ScoreboardSplitTeamSize = Math.Clamp(ScoreboardSplitTeamSize, 1, 4);
        MsaaSamples = MsaaSamples <= 2 ? 2 : MsaaSamples <= 4 ? 4 : 8;
        HackAlpha = Math.Clamp(HackAlpha, 0, 255);
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
        VoiceNoiseLevel = OneOf(NoiseLevels, VoiceNoiseLevel, "Standard");
    }

    /// <summary><paramref name="value"/> when it is one of <paramref name="items"/> (any case, given back as listed), else the fallback or the first.</summary>
    private static string OneOf(string[] items, string? value, string? fallback = null)
    {
        int i = Array.FindIndex(items, x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? items[i] : fallback ?? items[0];
    }

    /// <summary>The wheel changes gadgets (profile SelectedMouse 0) rather than walk and run (1).</summary>
    public bool WheelSelectsGadgets => MouseWheel == "Gadget";

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
