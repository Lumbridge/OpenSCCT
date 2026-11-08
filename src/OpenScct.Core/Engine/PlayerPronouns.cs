using System;
using System.Collections.Generic;

namespace OpenScct.Core.Engine;

public sealed record PronounForms(string Subject, string Object, string Possessive, string IndependentPossessive, string Reflexive);

/// <summary>Player-selected grammar. Missing or invalid selections always use neutral language.</summary>
public static class PlayerPronouns
{
    public const string Default = "they/them";
    public const int MaxLength = 16;
    public static IReadOnlyList<string> Presets { get; } = Array.AsReadOnly(new[] { Default, "she/her", "he/him" });

    private static readonly PronounForms Neutral = new("they", "them", "their", "theirs", "themselves");
    private static PronounForms? Preset(string value) => value switch
    {
        Default => Neutral,
        "she/her" => new("she", "her", "her", "hers", "herself"),
        "he/him" => new("he", "him", "his", "his", "himself"),
        _ => null,
    };

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = Default;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.Length > MaxLength) return false;
        string text = value.Trim().ToLowerInvariant();
        if (Preset(text) is null) return false;
        normalized = text;
        return true;
    }

    public static string Normalize(string? value) => TryNormalize(value, out string normalized) ? normalized : Default;

    public static PronounForms For(string? value) => Preset(Normalize(value)) ?? Neutral;
}
