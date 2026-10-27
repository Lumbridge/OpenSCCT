using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Play;
using OpenScctGame.Render;

namespace OpenScctGame.Hub;

/// <summary>
/// The match pads' theme kits (an OpenSCCT addition, docs/HUB.md "Pad themes"): every pad of the hub map carries one
/// render-only dressing per theme (court paint, rail and post skins, props, neon, the theme's light baked in), drawn
/// as gameplay mesh nodes named <c>HubTheme_&lt;pad&gt;_&lt;theme&gt;_&lt;material&gt;</c> (tools/hubmap). The map shows
/// each pad's default theme (fixtures/hubmap/areas.json, <c>pads[].theme</c>); <see cref="Apply(RenderMap, string, string)"/>
/// shows another. Collision is the same under every theme.
/// </summary>
public static class HubPadThemes
{
    public const string NodePrefix = "HubTheme_";

    /// <summary>The neutral theme, shown for a map without a kit of its own.</summary>
    public const string DefaultTheme = "hub";

    /// <summary>The theme ids the hub map has kits for (tools/hubmap/layout.py THEMES).</summary>
    public static IReadOnlyList<string> Themes => ThemeIds;

    private static readonly string[] ThemeIds = ["hub", "polar", "steel", "aquarius", "lab", "museum"];

    /// <summary>Map code prefix to theme (the first four letters of the code: PolaE, SteeD...). Keep in step with THEMES' maps.</summary>
    private static readonly Dictionary<string, string> MapThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pola"] = "polar",
        ["Stee"] = "steel",
        ["Fact"] = "steel",
        ["Ware"] = "steel",
        ["Aqua"] = "aquarius",
        ["Offs"] = "aquarius",
        ["Krau"] = "lab",
        ["Clar"] = "lab",
        ["Deft"] = "lab",
        ["Ziop"] = "lab",
        ["Muse"] = "museum",
        ["Bank"] = "museum",
        ["Pana"] = "museum",
        ["Cine"] = "museum",
        ["Club"] = "museum",
        ["Sanc"] = "museum",
        ["Sche"] = "museum",
    };

    private static readonly Dictionary<string, string> Chosen = new(StringComparer.Ordinal);

    /// <summary>The theme for a map code (<c>PolaE</c> gives <c>polar</c>); the neutral theme for any other map or none.</summary>
    public static string ThemeForMap(string? mapCode) =>
        mapCode is { Length: >= 4 } && MapThemes.TryGetValue(mapCode[..4], out string? theme) ? theme : DefaultTheme;

    /// <summary>
    /// Shows <paramref name="themeId"/> on <paramref name="padId"/> in the current play scene's map (an unknown theme
    /// shows the neutral one). Without a map yet the choice is kept for <see cref="ApplyChosen"/>. Returns the nodes shown.
    /// </summary>
    public static int Apply(string padId, string themeId)
    {
        Chosen[padId] = Known(themeId);
        return CurrentMap() is { } map ? Apply(map, padId, themeId) : 0;
    }

    /// <summary>Shows <paramref name="themeId"/> on <paramref name="padId"/> and hides the pad's other kits. Returns the nodes shown.</summary>
    public static int Apply(RenderMap map, string padId, string themeId)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrEmpty(padId);
        string theme = Known(themeId);
        Chosen[padId] = theme;
        string pad = NodePrefix + padId + "_";
        string shown = pad + theme + "_";
        int count = 0;
        foreach ((string id, GameplayMesh g) in map.Gameplay)
        {
            if (!id.StartsWith(pad, StringComparison.Ordinal))
            {
                continue;
            }

            bool visible = id.StartsWith(shown, StringComparison.Ordinal);
            g.Node.Visible = visible;
            count += visible ? 1 : 0;
        }

        return count;
    }

    /// <summary>Applies every pad's last chosen theme to a newly loaded map (the hub play scene calls it once its map is built).</summary>
    public static void ApplyChosen(RenderMap map)
    {
        foreach ((string pad, string theme) in Chosen)
        {
            Apply(map, pad, theme);
        }
    }

    private static string Known(string? themeId) =>
        themeId is not null && Array.IndexOf(ThemeIds, themeId) >= 0 ? themeId : DefaultTheme;

    private static RenderMap? CurrentMap() =>
        Engine.GetMainLoop() is SceneTree { CurrentScene: PlayRoot { Map: { } map } } ? map : null;
}
