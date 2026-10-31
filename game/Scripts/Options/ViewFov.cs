using System;
using OpenScct.Provenance;

namespace OpenScctGame.Options;

/// <summary>
/// The field of view on the screen from a camera's FovAngle (horizontal over a 4:3 picture), as Reloaded's widescreen
/// settings make it: with <c>widescreen</c> on the 4:3 vertical view is kept and wider screens add width (Hor+) up to
/// 16:9 (a wider screen gets the 16:9 picture stretched across it, <see cref="OpenScctGame.Hud.HudLayout"/>), then every
/// view but the sticky camera and the camera network is capped at <c>ws_fov</c>; with it off the 4:3 horizontal angle
/// is stretched over the whole screen (the unpatched game). Godot-free so the rules are unit tested.
/// </summary>
public static class ViewFov
{
    /// <summary>The picture aspect FovAngle is horizontal for.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 20: 4:3 FOV converted with 3/4 x aspect")]
    public const float ReferenceAspect = 4f / 3f;

    /// <summary>The widest aspect the picture is laid out for; wider screens stretch it.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a4ac0: the FOV's aspect is min(W, H x 1.7777777777777777) / H")]
    public const float WidestAspect = 16f / 9f;

    /// <summary>The controller states whose views Reloaded leaves uncapped (its GotoState hook clears the cap flag on every other state).</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x10017d70: name indices 0x192 and 0x7ef set the exempt flag 0x1036abbc; read live as s_UseObject_StickyCam and s_UseObject_CamNetwork")]
    public static readonly string[] UncappedStates = ["s_UseObject_StickyCam", "s_UseObject_CamNetwork"];

    /// <summary>The horizontal angle across a screen of <paramref name="aspect"/> (width over height, capped at 16:9), in degrees.</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/reloaded-features-from-code.md rule 20: fov = 2 atan(tan(fov43 / 2) x 3/4 x aspect), capped at ws_fov")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a7080: 2 atan(tan(2 atan(0.75 tan(fov / 2)) / 2) x aspect), then min(ws_fov) unless 0x1036abbc")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core hooks 0x100a71f0 / 0x100a72f0 on game 0x10a0b126 / 0x109ec46b: the main view's FovAngle takes this path only, ws_er_small not read; projection hook 0x100a6260 keeps the horizontal scale")]
    public static float ScreenHorizontal(float fov43, float aspect, bool widescreen, float? cap)
    {
        if (!(aspect > 0f) || !(fov43 > 0f))
        {
            return fov43;
        }

        float h = fov43;
        if (widescreen)
        {
            double half = fov43 * Math.PI / 360.0;
            h = (float)(Math.Atan(Math.Tan(half) * Math.Min(aspect, WidestAspect) / ReferenceAspect) * 360.0 / Math.PI);
        }

        if (cap is { } c && c > 0f && h > c)
        {
            h = c;
        }

        return h;
    }

    /// <summary>The vertical angle of a screen whose horizontal angle is <paramref name="horizontal"/>, in degrees.</summary>
    public static float VerticalOf(float horizontal, float aspect)
    {
        if (!(aspect > 0f))
        {
            aspect = ReferenceAspect;
        }

        double half = horizontal * Math.PI / 360.0;
        return (float)(Math.Atan(Math.Tan(half) / aspect) * 360.0 / Math.PI);
    }

    /// <summary>
    /// The vertical angle the camera gets for <paramref name="fov43"/> under <paramref name="settings"/>, on a picture of
    /// <paramref name="aspect"/>. <paramref name="capped"/> is false only in the views Reloaded leaves uncapped
    /// (<see cref="UncappedStates"/>). With widescreen on, the vertical angle is the 4:3 one up to 16:9; a wider picture
    /// is drawn at 16:9 and stretched by the play client (a picture that is not stretched keeps that vertical angle).
    /// </summary>
    public static float Vertical(float fov43, float aspect, GameSettings settings, bool capped)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float h = ScreenHorizontal(fov43, aspect, settings.Widescreen, capped && settings.Widescreen ? settings.MercFovCap : null);
        return VerticalOf(h, settings.Widescreen ? Math.Min(aspect, WidestAspect) : aspect);
    }

    /// <summary>True when the ws_fov cap applies in the controller state <paramref name="state"/>.</summary>
    public static bool CapApplies(string? state) => state is null || Array.IndexOf(UncappedStates, state) < 0;
}
