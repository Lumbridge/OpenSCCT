using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OpenScctGame.Controls;

/// <summary>What one part of a binding does: hold an input byte, call an exec command on the press, or feed an axis.</summary>
public enum KeyActionKind
{
    /// <summary><c>Button bX</c> or <c>InvButton bX</c>: the byte is 1 while the key is down.</summary>
    Button,

    /// <summary>An exec command run once on the press (<c>Function Fire</c>, <c>SelectGadget 1</c>, <c>Jump</c>).</summary>
    Command,

    /// <summary><c>Axis aX Speed=n</c>: the axis receives n while the key is down (n per count for the mouse).</summary>
    Axis,
}

/// <summary>One resolved part of a key's binding.</summary>
public readonly record struct KeyAction(KeyActionKind Kind, string Name, int Arg = 0, float Speed = 0f)
{
    public override string ToString() => Kind switch
    {
        KeyActionKind.Button => "Button " + Name,
        KeyActionKind.Axis => string.Create(CultureInfo.InvariantCulture, $"Axis {Name} Speed={Speed}"),
        _ => Arg == 0 ? Name : string.Create(CultureInfo.InvariantCulture, $"{Name} {Arg}"),
    };
}

/// <summary>
/// An original key layout (<c>Default_kbd_0.ini</c> for the spy, <c>Default_kbd_1.ini</c> for the merc): the
/// <c>[Engine.Input]</c> aliases and the <c>IK_*</c> bindings, each binding resolved through the aliases into
/// buttons, exec commands and axes, in the order the ini lists them (UE2 runs a binding's parts left to right).
/// </summary>
public sealed class KeyLayout
{
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, string> _bindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Key names (IK_ prefix removed, as the ini spells them: A, Shift, LeftMouse, MouseX) to their raw binding.</summary>
    public IReadOnlyDictionary<string, string> Bindings => _bindings;

    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    /// <summary>Reads a layout file.</summary>
    public static KeyLayout Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads a layout from ini text (only the <c>[Engine.Input]</c> section is used).</summary>
    public static KeyLayout Parse(string ini)
    {
        ArgumentNullException.ThrowIfNull(ini);
        var layout = new KeyLayout();
        bool inInput = true;
        foreach (string raw in ini.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                inInput = string.Equals(line, "[Engine.Input]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inInput)
            {
                continue;
            }

            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (key.StartsWith("Aliases[", StringComparison.OrdinalIgnoreCase))
            {
                if (ParseAlias(value) is { } alias)
                {
                    layout._aliases[alias.Alias] = alias.Command;
                }
            }
            else if (key.StartsWith("IK_", StringComparison.OrdinalIgnoreCase))
            {
                layout._bindings[key[3..]] = value;
            }
        }

        return layout;
    }

    /// <summary>Sets (or with an empty command, clears) a key's raw binding.</summary>
    public void Bind(string key, string command)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (string.IsNullOrWhiteSpace(command))
        {
            _bindings.Remove(key);
        }
        else
        {
            _bindings[key] = command.Trim();
        }
    }

    /// <summary>Read old overlay files or a complete snapshot whose missing keys are intentionally unbound.</summary>
    public static KeyLayout ApplyOverrides(KeyLayout stock, string saved)
    {
        KeyLayout user = Parse(saved);
        if (saved.StartsWith(SnapshotMarker, StringComparison.Ordinal)) return user;
        KeyLayout merged = stock.Clone();
        merged.ImportAliases(user);
        var commands = new HashSet<string>(user.Bindings.Values, StringComparer.OrdinalIgnoreCase);
        foreach (string key in new List<string>(merged.Bindings.Keys))
            if (!user.Bindings.ContainsKey(key) && commands.Contains(merged.Bindings[key])) merged.Bind(key, "");
        foreach ((string key, string command) in user.Bindings) merged.Bind(key, command);
        return merged;
    }

    public void UpgradeWheelBindings(string legacyMode)
    {
        foreach (var binding in Bindings.ToArray())
        {
            var parts = Expanded(binding.Value);
            bool changed = false;
            for (int i = 0; i < parts.Count; i++)
            {
                string replacement = parts[i].ToUpperInvariant() switch
                {
                    "WHEELUP" => legacyMode == "Speed" ? "Run" : "NextGadget",
                    "WHEELDOWN" => legacyMode == "Speed" ? "Walk" : "PreviousGadget",
                    "CHANGESPEED" => "Walk", _ => parts[i],
                };
                changed |= replacement != parts[i];
                parts[i] = replacement;
            }
            if (changed) Bind(binding.Key, string.Join(" | ", parts));
        }
    }

    public void ImportAliases(KeyLayout source)
    {
        foreach ((string name, string command) in source.Aliases) _aliases[name] = command;
    }

    public const string SnapshotMarker = "; OpenSCCT complete layout v2";

    /// <summary>Lossless draft copy, including custom aliases and command arguments.</summary>
    public KeyLayout Clone() => Parse(ToIni());

    public string ToIni()
    {
        var text = new System.Text.StringBuilder(SnapshotMarker + "\n" + DefaultKeyLayout.PerspectiveBindingMarker + "\n[Engine.Input]\n");
        int index = 0;
        foreach ((string alias, string command) in _aliases)
        {
            text.Append($"Aliases[{index++}]=(Command=\"{command}\",Alias=\"{alias}\")\n");
        }
        foreach ((string key, string command) in _bindings)
        {
            text.Append("IK_").Append(key).Append('=').Append(command).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>Expand aliases without parsing away custom arguments or axis modifiers.</summary>
    public List<string> Expanded(string command, int depth = 0)
    {
        var parts = new List<string>();
        foreach (string raw in command.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (depth < 8 && _aliases.TryGetValue(raw, out string? alias))
                parts.AddRange(Expanded(alias, depth + 1));
            else
                parts.Add(raw);
        }
        return parts;
    }

    /// <summary>The resolved parts of <paramref name="key"/>'s binding, empty when unbound.</summary>
    public List<KeyAction> Resolve(string key)
    {
        var actions = new List<KeyAction>();
        if (_bindings.TryGetValue(key, out string? binding))
        {
            ResolveCommand(binding, actions, 0);
        }

        return actions;
    }

    private void ResolveCommand(string command, List<KeyAction> into, int depth)
    {
        foreach (string part in command.Split('|'))
        {
            string p = part.Trim();
            if (p.Length == 0)
            {
                continue;
            }

            string[] words = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string head = words[0];
            if ((string.Equals(head, "Button", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(head, "InvButton", StringComparison.OrdinalIgnoreCase)) && words.Length > 1)
            {
                into.Add(new KeyAction(KeyActionKind.Button, words[1]));
            }
            else if (string.Equals(head, "Axis", StringComparison.OrdinalIgnoreCase) && words.Length > 1)
            {
                float speed = 1f;
                foreach (string w in words[1..])
                {
                    if (w.StartsWith("Speed=", StringComparison.OrdinalIgnoreCase) &&
                        float.TryParse(w.AsSpan(6), NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                    {
                        speed = s;
                    }
                }

                into.Add(new KeyAction(KeyActionKind.Axis, words[1], 0, speed));
            }
            else if (string.Equals(head, "Function", StringComparison.OrdinalIgnoreCase) && words.Length > 1)
            {
                into.Add(new KeyAction(KeyActionKind.Command, words[1], Arg(words, 2)));
            }
            else if (depth < 8 && words.Length == 1 && _aliases.TryGetValue(head, out string? aliased))
            {
                ResolveCommand(aliased, into, depth + 1);
            }
            else
            {
                into.Add(new KeyAction(KeyActionKind.Command, head, Arg(words, 1)));
            }
        }
    }

    private static int Arg(string[] words, int index) =>
        words.Length > index && int.TryParse(words[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    /// <summary>The <c>Command="..."</c> and <c>Alias="..."</c> of an alias entry.</summary>
    private static (string Alias, string Command)? ParseAlias(string value)
    {
        string? command = Quoted(value, "Command=");
        string? alias = Quoted(value, "Alias=");
        return command is null || alias is null ? null : (alias, command);
    }

    private static string? Quoted(string text, string key)
    {
        int i = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        int start = text.IndexOf('"', i + key.Length);
        int end = start < 0 ? -1 : text.IndexOf('"', start + 1);
        return start < 0 || end < 0 ? null : text[(start + 1)..end];
    }
}
