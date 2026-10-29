using System;
using Godot;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Options;

/// <summary>
/// Puts <see cref="GameSettings"/> into effect with Godot: the window (mode, monitor, size, vsync, render scale), the
/// audio bus volumes, the frame cap of the local player's side and the mouse look. The frame cap follows the refresh
/// rate of the window's screen (<see cref="MonitorHz"/>, kept current by <see cref="DisplayFramePacer"/>) and turns
/// into <c>Engine.max_fps</c> by the rules of <see cref="DisplayFrameCap"/>.
/// </summary>
public static class SettingsApplier
{
    public const string MasterBus = "Master";
    public const string VoiceBus = "SvmVoice";

    /// <summary>The refresh rate of the screen the window is on, as last read (60 until then).</summary>
    public static double MonitorHz { get; private set; } = DisplayFrameCap.FallbackHz;

    /// <summary>The screen <see cref="MonitorHz"/> was read from, or -1.</summary>
    public static int MonitorScreen { get; private set; } = -1;

    /// <summary>The side the last frame cap was chosen for (Reloaded: the merc cap outside the game).</summary>
    public static bool LastMerc { get; private set; } = true;

    /// <summary>
    /// At start: the audio volumes, vertical sync and the menus' frame cap always (and the pacer that follows the
    /// monitor); the window only once the player has saved settings (until then the project's own window settings
    /// stay).
    /// </summary>
    public static void ApplyAtStart(Viewport? viewport)
    {
        GameSettings s = SettingsStore.Current;
        if (SettingsStore.Exists)
        {
            ApplyDisplay(s, viewport);
        }
        else
        {
            ApplyPacing(s);
        }

        ApplyAudio(s);
        ApplyRendering(s, viewport);
        if (viewport is not null && DisplayServer.GetName() != "headless")
        {
            DisplayFramePacer.Attach(viewport);
        }
    }

    /// <summary>
    /// The texture filtering (filter_level on the viewport, and as the default of the viewports made later) and the
    /// spot shadows' map size and edge smoothing (shadow_quality, shadow_edge_smoothing: from the next map, when the
    /// shadow atlas is made again).
    /// </summary>
    public static void ApplyRendering(GameSettings s, Viewport? viewport)
    {
        ArgumentNullException.ThrowIfNull(s);
        Viewport.AnisotropicFiltering level = s.FilterLevel switch
        {
            0 => Viewport.AnisotropicFiltering.Disabled,
            2 => Viewport.AnisotropicFiltering.Anisotropy2X,
            4 => Viewport.AnisotropicFiltering.Anisotropy4X,
            8 => Viewport.AnisotropicFiltering.Anisotropy8X,
            _ => Viewport.AnisotropicFiltering.Anisotropy16X,
        };
        ProjectSettings.SetSetting("rendering/textures/default_filters/anisotropic_filtering_level", (int)level);
        if (viewport is not null)
        {
            viewport.AnisotropicFilteringLevel = level;
            ApplyAntiAliasing(s, viewport);
        }

        Render.Materials.MaterialCompiler.AlphaToCoverage = s.Msaa;

        Render.SpotShadowLight.MapSize = Render.SpotShadowLight.MapSizeFor(s.ShadowQuality);
        Render.SpotShadowLight.SoftRadius = s.ShadowEdgeSmoothing ? Render.RenderK.SpotShadowSoftRadius : 0f;
    }

    /// <summary>
    /// Anti-aliasing (rl:gfx.aa) on the viewport that draws the game: MSAA of the 3D scene at msaa_samples when msaa is on,
    /// and SMAA over the finished frame when smaa is on. Both apply at once; Godot runs SMAA after the compositor's
    /// display-space pass, so it smooths the frame as shown, vision modes included. The sky zone, spot shadow, golden
    /// and display check viewports keep both off.
    /// </summary>
    public static void ApplyAntiAliasing(GameSettings s, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(viewport);
        viewport.Msaa3D = !s.Msaa ? Viewport.Msaa.Disabled : s.MsaaSamples switch
        {
            2 => Viewport.Msaa.Msaa2X,
            4 => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Msaa8X,
        };
        viewport.ScreenSpaceAA = s.Smaa ? Viewport.ScreenSpaceAAEnum.Smaa : Viewport.ScreenSpaceAAEnum.Disabled;
    }

    /// <summary>The project settings file the engine reads at start (project.godot's application/config/project_settings_override).</summary>
    public const string StartOverridePath = "user://render_override.cfg";

    /// <summary>
    /// The settings that only take effect at start (Reloaded's low_latency_mode, rl:gfx.pacing), written for the next
    /// start: On and Ultra queue one swap chain image less (2, Godot's lowest; its default is 3). The frame queue stays
    /// at 2, the lowest Godot's setting allows, so Ultra is On for now. Off removes the file.
    /// </summary>
    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Estimated, "rl:gfx.pacing open question: Godot 4.7 offers frame queue 2 to 3 and 2 to 4 swap chain images; Reloaded's latency modes are not decoded")]
    public static void WriteStartOverrides(GameSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string path = ProjectSettings.GlobalizePath(StartOverridePath);
        try
        {
            if (s.LowLatencyMode == "Off")
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }

                return;
            }

            System.IO.File.WriteAllText(path,
                "; Written by OpenSCCT from settings.json (low_latency_mode); removed when it is Off.\n" +
                "[rendering]\n\nrendering_device/vsync/swapchain_image_count=2\n");
        }
        catch (System.IO.IOException e)
        {
            GD.PushWarning($"Could not write {StartOverridePath}: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            GD.PushWarning($"Could not write {StartOverridePath}: {e.Message}");
        }
    }

    /// <summary>
    /// Reads the refresh rate of the window's screen when the window is on another screen than last time, or always
    /// with <paramref name="force"/>; true when the rate or the screen changed.
    /// </summary>
    public static bool RefreshMonitor(bool force = false)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return false;
        }

        int screen = DisplayServer.WindowGetCurrentScreen();
        if (!force && screen == MonitorScreen)
        {
            return false;
        }

        double hz = DisplayServer.ScreenGetRefreshRate(screen);
        if (!(hz >= 1.0))
        {
            hz = DisplayFrameCap.FallbackHz;
        }

        bool changed = screen != MonitorScreen || Math.Abs(hz - MonitorHz) > 0.01;
        MonitorScreen = screen;
        MonitorHz = hz;
        return changed;
    }

    /// <summary>Vertical sync and the frame cap (nothing headless); the options screen's save and the frame-rate smoke use it.</summary>
    public static void ApplyPacing(GameSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        DisplayServer.VSyncMode vsync = s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled;
        if (DisplayServer.WindowGetVsyncMode() != vsync)
        {
            DisplayServer.WindowSetVsyncMode(vsync);
        }

        RefreshMonitor(force: true);
        ApplyFrameCap(s, LastMerc);
    }

    /// <summary>The window as the settings ask (nothing headless).</summary>
    public static void ApplyDisplay(GameSettings s, Viewport? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        int screens = DisplayServer.GetScreenCount();
        int screen = Math.Clamp(s.Monitor - 1, 0, Math.Max(0, screens - 1));
        DisplayServer.WindowMode mode = s.DisplayMode switch
        {
            "Fullscreen" => DisplayServer.WindowMode.ExclusiveFullscreen,
            "Windowed" => DisplayServer.WindowMode.Windowed,
            _ => DisplayServer.WindowMode.Fullscreen,
        };
        if (DisplayServer.WindowGetCurrentScreen() != screen)
        {
            DisplayServer.WindowSetCurrentScreen(screen);
        }

        DisplayServer.WindowSetMode(mode);
        if (mode == DisplayServer.WindowMode.Windowed)
        {
            PlaceWindow(s, screen);
        }

        if (viewport is not null)
        {
            viewport.Scaling3DScale = s.RenderScale;
        }

        ApplyRendering(s, viewport);
        ApplyPacing(s);
    }

    /// <summary>
    /// A window's size and place (window_position, rl:gfx.modes): Stretched fills the screen's usable area; Centered
    /// centres a window of the chosen size; Default leaves the place to the system.
    /// </summary>
    private static void PlaceWindow(GameSettings s, int screen)
    {
        if (s.WindowPosition == "Stretched")
        {
            Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
            DisplayServer.WindowSetSize(usable.Size);
            DisplayServer.WindowSetPosition(usable.Position);
            return;
        }

        if (GameSettings.ParseResolution(s.Resolution) is not { } size)
        {
            return;
        }

        var wanted = new Vector2I(size.Width, size.Height);
        DisplayServer.WindowSetSize(wanted);
        if (s.WindowPosition == "Centered")
        {
            Vector2I area = DisplayServer.ScreenGetSize(screen);
            Vector2I origin = DisplayServer.ScreenGetPosition(screen);
            DisplayServer.WindowSetPosition(origin + (area - wanted) / 2);
        }
    }

    /// <summary>
    /// Bus volumes: Master, the effect buses (SvmWorld, SvmFlat and the zones' reverb buses), SvmMusic and the voice
    /// bus. Buses made later by the sound system get theirs at the next call.
    /// </summary>
    public static void ApplyAudio(GameSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        EnsureBus(VoiceBus);
        for (int i = 0; i < AudioServer.BusCount; i++)
        {
            string name = AudioServer.GetBusName(i);
            float? volume = name switch
            {
                MasterBus => s.MasterVolume,
                Audio.ReverbBuses.MusicBus => s.MusicVolume,
                VoiceBus => s.VoiceVolume,
                Audio.ReverbBuses.WorldBus or Audio.ReverbBuses.FlatBus => s.SoundVolume,
                _ when name.StartsWith("SvmReverb", StringComparison.Ordinal) => s.SoundVolume,
                _ => null,
            };
            if (volume is { } v)
            {
                AudioServer.SetBusVolumeDb(i, v <= 0f ? -80f : Mathf.LinearToDb(v));
            }
        }
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
        {
            return;
        }

        AudioServer.AddBus();
        int index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, MasterBus);
    }

    /// <summary>
    /// The frame cap of the side the local player is on (Reloaded: spy cap in game as a spy, else merc) on the
    /// window's screen, with the settings' vertical sync (<see cref="DisplayFrameCap.EngineMaxFps"/>).
    /// </summary>
    public static void ApplyFrameCap(GameSettings s, bool merc)
    {
        ArgumentNullException.ThrowIfNull(s);
        LastMerc = merc;
        int fps = DisplayFrameCap.EngineMaxFps(s.FpsFor(merc), MonitorHz, s.VSync);
        if (Engine.MaxFps != fps)
        {
            Engine.MaxFps = fps;
        }
    }

    /// <summary>
    /// The mouse look of the play session from the profile sensitivity, the sides' look multipliers per view and the
    /// profile's inversions (the clutch is left as it is: <see cref="OptionsRuntime"/> sets it while one is held).
    /// </summary>
    /// <param name="pad">The controller groups' multipliers instead of the mouse's (the controller's view scaling).</param>
    public static MouseLook MouseLookOf(GameSettings s, MouseLook? current = null, bool pad = false)
    {
        ArgumentNullException.ThrowIfNull(s);
        MouseLook basis = current ?? new MouseLook();
        LookSettings spy = s.LookOf(merc: false, pad);
        LookSettings merc = s.LookOf(merc: true, pad);
        return basis with
        {
            MouseSensitivity = s.MouseSensitivity,
            Pad = pad,
            SpyFreeLookResponse = spy.LookResponse,
            SpyFreeLook = spy.FreeLook,
            SpyGunLook = spy.Look,
            SpyScope = spy.Scope,
            SpyVertical = spy.Vertical,
            SpyCam = spy.Cam,
            MercLook = merc.Look,
            MercScope = merc.Scope,
            MercScopeVertical = merc.ScopeVertical,
            MercVertical = merc.Vertical,
            MercCam = merc.Cam,
            InvertY = s.InvertY,
            InvertXSpy = s.InvertXSpy,
        };
    }
}
