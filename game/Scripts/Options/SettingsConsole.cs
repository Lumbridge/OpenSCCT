using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenScctGame.Options;

/// <summary>
/// The console's <c>get</c> and <c>set</c> on the settings file (rl:qol.console): a key is a settings.json key, a
/// group's member is reached with a dot (<c>controls_spy_kbm.look</c>). A value is written as the file would hold it
/// (<c>on</c>/<c>off</c> also work for switches), and the settings' own limits apply afterwards. No Godot here.
/// </summary>
public static class SettingsConsole
{
    /// <summary>Keys whose change needs the window set again.</summary>
    public static readonly IReadOnlySet<string> DisplayKeys =
        new HashSet<string>(StringComparer.Ordinal) { "display_mode", "display_monitor", "resolution", "resolution_scale", "vsync" };

    /// <summary>The value of a key as the file holds it, or null when there is no such key.</summary>
    public static string? Get(GameSettings settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Find(JsonNode.Parse(settings.ToJson()), key, out _, out _) is { } node ? node.ToJsonString() : null;
    }

    /// <summary>
    /// The settings with <paramref name="key"/> set to <paramref name="value"/> (and clamped), or null with the
    /// reason in <paramref name="problem"/>.
    /// </summary>
    public static GameSettings? Set(GameSettings settings, string key, string value, out string problem)
    {
        ArgumentNullException.ThrowIfNull(settings);
        JsonNode? root = JsonNode.Parse(settings.ToJson());
        if (Find(root, key, out JsonObject? parent, out string? name) is not { } old || parent is null || name is null)
        {
            problem = $"no setting '{key}' (try: current)";
            return null;
        }

        if (Parse(old, value.Trim()) is not { } next)
        {
            problem = $"'{value}' is not a value for {key} (it holds {old.ToJsonString()})";
            return null;
        }

        parent[name] = next;
        try
        {
            problem = string.Empty;
            GameSettings result = GameSettings.FromJson(root!.ToJsonString());
            result.LoadNote = settings.LoadNote;
            return result;
        }
        catch (JsonException e)
        {
            problem = $"'{value}' is not a value for {key}: {e.Message}";
            return null;
        }
    }

    /// <summary>Every top-level key with its value, one line each (groups on one line).</summary>
    public static IReadOnlyList<string> Current(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var lines = new List<string>();
        if (JsonNode.Parse(settings.ToJson()) is JsonObject root)
        {
            foreach ((string key, JsonNode? value) in root)
            {
                lines.Add(key + " = " + (value?.ToJsonString() ?? "null"));
            }
        }

        return lines;
    }

    private static JsonNode? Find(JsonNode? root, string key, out JsonObject? parent, out string? name)
    {
        parent = null;
        name = null;
        JsonNode? node = root;
        foreach (string part in key.Trim().Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out JsonNode? child))
            {
                return null;
            }

            parent = obj;
            name = part;
            node = child;
        }

        return node;
    }

    /// <summary>The typed value read as the old one's kind: a switch, a number, a text, or JSON for lists and groups.</summary>
    private static JsonNode? Parse(JsonNode old, string text)
    {
        JsonValueKind kind = old.GetValueKind();
        switch (kind)
        {
            case JsonValueKind.True or JsonValueKind.False:
                return text.ToLowerInvariant() switch
                {
                    "1" or "on" or "true" or "yes" => JsonValue.Create(true),
                    "0" or "off" or "false" or "no" => JsonValue.Create(false),
                    _ => null,
                };
            case JsonValueKind.Number:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
                {
                    return JsonValue.Create(whole);
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
                    ? JsonValue.Create(number)
                    : null;
            case JsonValueKind.String:
                return JsonValue.Create(text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text);
            default:
                try
                {
                    return JsonNode.Parse(text);
                }
                catch (JsonException)
                {
                    return null;
                }
        }
    }
}
