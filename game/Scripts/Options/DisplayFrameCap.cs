using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Provenance;

namespace OpenScctGame.Options;

/// <summary>
/// The frame cap rules (docs/RENDERING.md, frame pacing): what <c>fps_spy</c> and <c>fps_merc</c> hold, what frame
/// rate they ask for on a screen of a given refresh rate, and what the engine's limiter and vertical sync are set to.
/// Godot-free: <c>SettingsApplier</c> puts the result into effect.
/// <para>Reloaded caps each side at 30 to 240 (spec/native/reloaded-features-from-code.md rule 15). Two values are
/// additions Ryan asked for (2026-10-25): <see cref="Monitor"/>, the refresh rate of the screen the window is on (the
/// default, followed when the window moves to another screen), and <see cref="Unlimited"/>.</para>
/// </summary>
public static class DisplayFrameCap
{
    /// <summary>The cap follows the refresh rate of the window's screen (an addition).</summary>
    public const int Monitor = -1;

    /// <summary>No cap (an addition; Reloaded's lowest setting is 30).</summary>
    public const int Unlimited = 0;

    /// <summary>The refresh rate assumed when the screen does not report one.</summary>
    public const double FallbackHz = 60.0;

    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md constants: fps caps 30 to 240")]
    public const int Min = 30;

    public const int Max = 240;

    public const string MonitorLabel = "Monitor";

    public const string UnlimitedLabel = "Unlimited";

    /// <summary>The values the options list offers between 30 and 240 (common screen rates); any other saved value is listed too.</summary>
    public static IReadOnlyList<int> Presets { get; } = [30, 50, 60, 75, 90, 100, 120, 144, 165, 180, 200, 240];

    /// <summary>A saved cap in range: below 0 is <see cref="Monitor"/>, 0 <see cref="Unlimited"/>, else 30 to 240.</summary>
    public static int Clamp(int fps) => fps < 0 ? Monitor : fps == 0 ? Unlimited : Math.Clamp(fps, Min, Max);

    /// <summary>The whole frame rate of a screen's refresh rate (59.94 gives 60); <see cref="FallbackHz"/> when unknown.</summary>
    public static int ScreenFps(double refreshHz) =>
        double.IsFinite(refreshHz) && refreshHz >= 1.0 ? (int)Math.Round(refreshHz, MidpointRounding.AwayFromZero) : (int)FallbackHz;

    /// <summary>The frame rate a cap asks for on a screen of <paramref name="refreshHz"/> (0: no cap).</summary>
    public static int Target(int cap, double refreshHz) => Clamp(cap) switch
    {
        Monitor => ScreenFps(refreshHz),
        int fps => fps,
    };

    /// <summary>
    /// <c>Engine.max_fps</c> for a cap: with vertical sync on and a cap at or above the screen's rate the swap paces the
    /// frames and the limiter is off (a sleep limiter at the swap's own rate misses a refresh now and then); otherwise
    /// the cap's frame rate, 0 for none.
    /// </summary>
    public static int EngineMaxFps(int cap, double refreshHz, bool vsync)
    {
        int target = Target(cap, refreshHz);
        if (vsync && (target == Unlimited || target >= ScreenFps(refreshHz)))
        {
            return 0;
        }

        return target;
    }

    /// <summary>The frame rate the screen will show (the cap, at most the screen's rate with vertical sync), 0 for no limit.</summary>
    public static int ExpectedFps(int cap, double refreshHz, bool vsync)
    {
        int target = Target(cap, refreshHz);
        int screen = ScreenFps(refreshHz);
        return vsync ? (target == Unlimited ? screen : Math.Min(target, screen)) : target;
    }

    /// <summary>The options list's text of a cap; <paramref name="refreshHz"/> names the screen's rate after Monitor.</summary>
    public static string Label(int cap, double? refreshHz = null) => Clamp(cap) switch
    {
        Monitor when refreshHz is { } hz => string.Create(CultureInfo.InvariantCulture, $"{MonitorLabel} ({ScreenFps(hz)} Hz)"),
        Monitor => MonitorLabel,
        Unlimited => UnlimitedLabel,
        int fps => fps.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The cap of an options list text (the inverse of <see cref="Label"/>); null when it is none.</summary>
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string t = text.Trim();
        if (t.StartsWith(MonitorLabel, StringComparison.OrdinalIgnoreCase))
        {
            return Monitor;
        }

        if (t.Equals(UnlimitedLabel, StringComparison.OrdinalIgnoreCase) || t.Equals("Off", StringComparison.OrdinalIgnoreCase))
        {
            return Unlimited;
        }

        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps) ? Clamp(fps) : null;
    }

    /// <summary>The options list: Monitor, Unlimited, then the presets in order with <paramref name="current"/> among them.</summary>
    public static string[] Choices(int current, double? refreshHz = null)
    {
        var values = new SortedSet<int>(Presets);
        int c = Clamp(current);
        if (c > 0)
        {
            values.Add(c);
        }

        var list = new List<string> { Label(Monitor, refreshHz), Label(Unlimited) };
        foreach (int v in values)
        {
            list.Add(Label(v));
        }

        return list.ToArray();
    }
}
