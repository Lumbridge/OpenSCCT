using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScctGame.Controls;

/// <summary>Hub-only commands share the saved keyboard/mouse and controller action editor.</summary>
public static class HubBindings
{
    public const string Marker = "; OpenSCCT hub bindings v1";
    public static readonly IReadOnlyList<(string Id, string Command, string Key, string Label)> All =
    [
        ("hub_travel", "HubTravel", "F4", "Travel"),
        ("hub_spy", "HubSpy", "J", "Play as spy"),
        ("hub_merc", "HubMerc", "K", "Play as mercenary"),
        ("hub_plaza", "HubPlaza", "N", "Return to plaza"),
        ("hub_emotes", "HubEmotes", "G", "Emote menu"),
        ("hub_use", "HubUse", "U", "Ready up / use activity"),
        ("hub_cancel", "HubCancel", "X", "Cancel activity"),
        ("hub_watch", "HubWatch", "H", "Watch queued match"),
        ("hub_players", "HubPlayers", "P", "Player list"),
        ("hub_armoury", "HubArmoury", "O", "Armoury"),
        .. Enumerable.Range(1, 8).Select(i => ($"hub_emote{i}", $"HubEmote{i}", i.ToString(), $"Choose emote {i}")),
    ];

    public static void Upgrade(KeyLayout layout, string? saved)
    {
        if (saved?.Contains(Marker, StringComparison.Ordinal) == true) return;
        foreach (var binding in All)
        {
            if (layout.Bindings.Keys.Any(k => layout.Resolve(k).Any(a => a.Name == binding.Command))) continue;
            string previous = layout.Bindings.GetValueOrDefault(binding.Key, "");
            // The Hub consumes these commands only in its own context. Preserve the
            // original combat/traversal action sharing that physical key elsewhere.
            layout.Bind(binding.Key, previous.Length == 0 ? binding.Command : previous + " | " + binding.Command);
        }
        if (!layout.Bindings.ContainsKey("Enter")) layout.Bind("Enter", "Talk");
    }

    public static string Hint(KeyLayout? layout, string command) => layout?.Bindings.Keys
        .FirstOrDefault(k => layout.Resolve(k).Any(a => a.Name.Equals(command, StringComparison.OrdinalIgnoreCase))) ?? "Unbound";
}
