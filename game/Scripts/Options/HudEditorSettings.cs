using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenScctGame.Options;

public sealed class HudElementSettings
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Scale { get; set; } = 1;
    public float Opacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public void Clamp()
    {
        X = Safe(X, -1, 1, 0); Y = Safe(Y, -1, 1, 0);
        Scale = Safe(Scale, .25f, 3, 1); Opacity = Safe(Opacity, .1f, 1, 1);
    }
    private static float Safe(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}

public sealed class HudEditorSettings
{
    public Dictionary<string, HudElementSettings> Spy { get; set; } = new();
    public Dictionary<string, HudElementSettings> Merc { get; set; } = new();
    public Dictionary<string, HudElementSettings> For(bool merc) => merc ? Merc : Spy;
    public HudElementSettings Element(bool merc, string id)
    {
        var layout = For(merc);
        if (!layout.TryGetValue(id, out var element)) layout[id] = element = new();
        return element;
    }
    public void Clamp()
    {
        Spy ??= new(); Merc ??= new();
        foreach (var layout in new[] { Spy, Merc })
            foreach (string key in new List<string>(layout.Keys))
            {
                if (!Elements.ContainsKey(key) || layout[key] is null) layout.Remove(key);
                else layout[key].Clamp();
            }
    }
    public HudEditorSettings Clone() => Parse(JsonSerializer.Serialize(this));
    public static HudEditorSettings Parse(string json)
    {
        if (json.Length > 65536) throw new JsonException("Layout is too large.");
        var result = JsonSerializer.Deserialize<HudEditorSettings>(json) ?? throw new JsonException("Missing layout.");
        result.Clamp(); return result;
    }
    public static readonly Dictionary<string, string> Elements = new()
    {
        ["Chat"] = "Chat", ["Activity"] = "Event feed",
        ["Vie"] = "Health", ["Energie"] = "Energy", ["Radar"] = "Radar", ["Location"] = "Zone name",
        ["Fps"] = "FPS", ["Network"] = "Network",
        ["Chrono"] = "Match timer", ["Objectif"] = "Objective counter", ["Gadget"] = "Gadget",
        ["Ammo"] = "Ammunition", ["ChronoHack"] = "Hacking countdown", ["ChronoObjectif"] = "Objective countdown",
        ["ChronoAlarm"] = "Alarm", ["AlertMessage"] = "Alert message", ["SystemMessage"] = "System messages",
        ["StateMessage"] = "State messages", ["ZoneMessage"] = "Zone messages", ["Speak"] = "Interaction prompt",
        ["Coop"] = "Co-op prompt", ["Spy_Speed"] = "Movement indicator", ["Pos_Merc"] = "Merc posture",
        ["Ballistic"] = "Throw charge", ["ChronoObjectif2"] = "Secondary objective countdown",
        ["GameMessage"] = "Game messages", ["TutoMessage"] = "Tutorial prompts"
    };
}
