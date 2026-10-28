using System;
using System.Collections.Generic;
using System.Text;
using OpenScctGame.Controls;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Tutorials;

/// <summary>
/// The tutorial pages' words, read from the converted GUI.int (the ui content step's strings.json): single captions,
/// the pages' localized string arrays (written in the ini as <c>("a","b",...)</c>), the <c>|</c> line breaks of the
/// legends and the key tokens of the tips (<c>\USE</c> becomes the key bound to Use). Without the converted strings
/// each call gives its fallback, so the pages still open (with their titles and empty texts).
/// </summary>
public static class TutorialText
{
    /// <summary>[Section] Key of GUI.int, or <paramref name="fallback"/>.</summary>
    public static string Caption(string section, string key, string fallback) =>
        UiStyle.Assets?.Localize("GUI", section, key, fallback) is { Length: > 0 } s ? s : fallback;

    /// <summary>A localized array of GUI.int (<c>("a","b")</c>), or an empty list.</summary>
    public static IReadOnlyList<string> Array(string section, string key) =>
        ParseArray(UiStyle.Assets?.Localize("GUI", section, key, string.Empty) ?? string.Empty);

    /// <summary>One entry of a localized array, or an empty string.</summary>
    public static string Item(string section, string key, int index)
    {
        IReadOnlyList<string> items = Array(section, key);
        return index >= 0 && index < items.Count ? items[index] : string.Empty;
    }

    /// <summary>The ini's array form <c>("a","b")</c>: quoted items, <c>\\</c> and <c>\"</c> unescaped.</summary>
    public static IReadOnlyList<string> ParseArray(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var items = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            var item = new StringBuilder();
            i++;
            while (i < text.Length && text[i] != '"')
            {
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '\\' or '"')
                {
                    i++;
                }

                item.Append(text[i]);
                i++;
            }

            items.Add(item.ToString());
            i++;
        }

        return items;
    }

    /// <summary>The legends' and HUD labels' <c>|</c> as line breaks.</summary>
    public static string Lines(string text) => text.Replace('|', '\n');

    /// <summary>The key tokens of a tip (<c>\USE</c>, <c>\PRIMARYFIRE</c>) as the keys of the player's layout for that side.</summary>
    public static string WithKeys(string text, PlaySide side)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        KeyLayout layout = InputBindings.Load(side, null, seedInputMap: false).Layout;
        return KeyTokens.Expand(text, binding =>
            KeyTokens.KeyBoundTo(layout, binding) is { } key ? UiStyle.Assets?.KeyDisplayName(key) ?? key : null);
    }
}
