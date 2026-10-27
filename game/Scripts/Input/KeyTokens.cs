using System;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScctGame.Controls;

/// <summary>
/// The key tokens of on-screen texts (spec/hud/counters-and-prompts.md, "Key tokens"): every text the canvas or a
/// Magma page draws that holds a backslash goes through the native token pass first (the text helper 0x10a02e40 calls
/// 0x10a02900 when <c>wcschr(text, '\\')</c> finds one). A token names a binding string; the first key, in
/// EInputKey order, whose whole binding equals that string (case ignored) gives its localized key name, which replaces
/// the backslash and the token. A token with no such key, or an unknown one, stays as it is.
/// </summary>
public static class KeyTokens
{
    /// <summary>The token table, in the order the native tests the prefixes (case ignored).</summary>
    [Provenance(ProvenanceKind.Decoded, "token pass 0x10a02900: _wcsnicmp chain, strings 0x10c48314..0x10c48904")]
    public static readonly (string Token, string Binding)[] Tokens =
    [
        ("MOVEF", "AliasHaut"),
        ("MOVEB", "AliasBas"),
        ("MOVEL", "AliasGauche"),
        ("MOVER", "AliasDroite"),
        ("USE", "Use"),
        ("PRIMARYFIRE", "Fire | GadgetQuickChoice 0"),
        ("SECONDARYFIRE", "AltFire | GadgetQuickChoice 1 | HoldBreath"),
        ("JUMP", "Jump"),
        ("CROUCH", "Crouch"),
        ("SPEEDMODE", "ChangeSpeed"),
        ("HACKCOMM", "HackComm"),
        ("SWITCHVISION", "SwitchHeadset 3"),
        ("VISION1", "SwitchHeadset 1"),
        ("VISION2", "SwitchHeadset 2"),
        ("GADGET1", "SelectGadget 0"),
        ("GADGET2", "SelectGadget 1"),
        ("GADGET3", "SelectGadget 2"),
        ("GADGET4", "SelectGadget 3"),
        ("NEXTGADGET", "AliasNextItem"),
        ("PREVIOUSGADGET", "AliasPrevItem"),
        ("ACTION1", "Roll | GadLaserSwitch"),
        ("ACTION2", "FireMode | GadTorcheSwitch"),
        ("COOP", "PerformCoopMvt"),
        ("BTW", "Btw"),
        ("BINOCULARS", "SwitchBinocularVision"),
        ("RELOAD", "Reload"),
        ("FIRERATE", "ROF"),
        ("LASER", "SwitchLaser"),
        ("CAMERA", "PrevStickyCam"),
        ("ZOOMIN", "ZoomIn"),
        ("ZOOMOUT", "ZoomOut"),
        ("ER", "SwitchEnhancedReality"),
        ("HUD", "DisplayHud"),
        ("INGAMEMENU", "ShowMenu"),
    ];

    /// <summary>
    /// The key (IK name without the prefix, as the layout spells it) whose whole binding is <paramref name="binding"/>:
    /// the native walks KEYNAME 0..254 and asks KEYBINDING of each named key (0x10a03060).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "key lookup 0x10a03060: KEYNAME %i for i < 255, KEYBINDING %s, _wcsicmp with the binding")]
    public static string? KeyBoundTo(KeyLayout? layout, string binding)
    {
        if (layout is null)
        {
            return null;
        }

        for (int i = 0; i < 255; i++)
        {
            var key = (EInputKey)i;
            if (!Enum.IsDefined(key))
            {
                continue;
            }

            string name = key.ToString();
            name = name.StartsWith("IK_", StringComparison.Ordinal) ? name[3..] : name;
            if (name.Length > 0 && layout.Bindings.TryGetValue(name, out string? bound) &&
                string.Equals(bound, binding, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// The token pass: each <c>\TOKEN</c> whose binding has a key becomes that key's name (from
    /// <paramref name="keyName"/>, which takes a binding string and gives the localized name of its key, or null).
    /// After a replacement the scan goes on one character past the inserted name, as the native does.
    /// </summary>
    [UcNative("UCanvas::ExpandKeyTokens", Exe = 0x10a02900)]
    public static string Expand(string text, Func<string, string?>? keyName)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (keyName is null || !text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\\' && Match(text, i + 1) is { } t && keyName(t.Binding) is { } name)
            {
                text = string.Concat(text.AsSpan(0, i), name, text.AsSpan(i + 1 + t.Token.Length));
                i += name.Length;
            }

            i++;
        }

        return text;
    }

    private static (string Token, string Binding)? Match(string text, int at)
    {
        foreach ((string token, string binding) in Tokens)
        {
            if (string.Compare(text, at, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0 && at + token.Length <= text.Length)
            {
                return (token, binding);
            }
        }

        return null;
    }
}
