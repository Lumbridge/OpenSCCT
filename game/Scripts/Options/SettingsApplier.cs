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
        if (viewport is not null && DisplayServer.GetName() != "headless")
        {
            DisplayFramePacer.Attach(viewport);
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
        if (mode == DisplayServer.WindowMode.Windowed && GameSettings.ParseResolution(s.Resolution) is { } size)
        {
            DisplayServer.WindowSetSize(new Vector2I(size.Width, size.Height));
            Vector2I area = DisplayServer.ScreenGetSize(screen);
            Vector2I origin = DisplayServer.ScreenGetPosition(screen);
            DisplayServer.WindowSetPosition(origin + (area - new Vector2I(size.Width, size.Height)) / 2);
        }

        if (viewport is not null)
        {
            viewport.Scaling3DScale = s.RenderScale;
        }

        ApplyPacing(s);
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

    /// <summary>The mouse look of the play session from the profile sensitivity and the sides' look multipliers.</summary>
    public static MouseLook MouseLookOf(GameSettings s, MouseLook? current = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        MouseLook basis = current ?? new MouseLook();
        return basis with
        {
            MouseSensitivity = s.MouseSensitivity,
            SpyFreeLook = s.SpyKbm.FreeLook,
            SpyGunLook = s.SpyKbm.Look,
            MercLook = s.MercKbm.Look,
            InvertY = s.InvertY,
        };
    }
}
