using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenScctGame.Controls;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>
/// Every text the menus and the HUD pages can show is English: the names of the key bindings in the options (the
/// stock layouts bind keys to French aliases such as AliasHaut), the Magma page texts after the game's localisation and
/// our English table, the menu strings of the localisation files, and the string literals of the menu and HUD code.
/// </summary>
public sealed partial class EnglishTextTests
{
    // Accented letters French uses and English menu text does not, and common French menu and binding words (whole
    // words, also inside a command name split into words: AliasGauche reads "Alias Gauche").
    [GeneratedRegex(@"[àâäçéèêëîïôöùûüÿœæÀÂÄÇÉÈÊËÎÏÔÖÙÛÜŸŒÆ]")]
    private static partial Regex Accents();

    private static readonly HashSet<string> FrenchWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "retour", "joueur", "joueurs", "partie", "parties", "quitter", "annuler", "valider", "oui", "espion", "espions",
        "mercenaire", "mercenaires", "serveur", "serveurs", "carte", "jouer", "commandes", "touche", "touches", "haut", "bas",
        "gauche", "droite", "torche", "lunettes", "vision", "nom", "tu", "vois", "pas", "appuyez", "pour", "avec", "les",
        "des", "une", "vous", "votre", "sauvegarder", "options de", "chargement", "équipe", "equipe", "arme", "objectif",
        "vie", "temps", "suivant", "précédent", "precedent", "accroupi", "sauter", "tirer", "recharger", "inventaire",
    };

    // "Vision" is English too; it only counts next to another French word ("Vision nocturne").
    private static readonly HashSet<string> SharedWords = new(StringComparer.OrdinalIgnoreCase) { "vision", "objectif" };

    /// <summary>The French in <paramref name="text"/>, or null when it reads as English.</summary>
    public static string? French(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Accents().Match(text) is { Success: true } accent)
        {
            return "accent '" + accent.Value + "'";
        }

        if (text.Contains("Options de", StringComparison.OrdinalIgnoreCase))
        {
            return "'Options de'";
        }

        string words = EnglishText.Words(Regex.Replace(text, @"[^\p{L}\s]+", " "));
        string[] tokens = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> hits = tokens.Where(t => FrenchWords.Contains(t) && !SharedWords.Contains(t)).ToList();
        return hits.Count > 0 ? "word '" + string.Join("', '", hits.Distinct(StringComparer.OrdinalIgnoreCase)) + "'" : null;
    }

    [Fact]
    public void The_detector_finds_french_and_passes_english()
    {
        Assert.NotNull(French("AliasHaut"));
        Assert.NotNull(French("Roll | GadLaserSwitch | GadTorcheSwitch"));
        Assert.NotNull(French("Retour"));
        Assert.NotNull(French("Options de jeu"));
        Assert.NotNull(French("Tu me vois pas !"));
        Assert.NotNull(French("Nom Gadget"));
        Assert.Null(French("Move forward"));
        Assert.Null(French("Switch Vision"));
        Assert.Null(French("Hack Enemy's Communications"));
        Assert.Null(French("Cycle vision modes"));
    }

    /// <summary>The options' key list for the stock layouts (both sides) and the built-in one.</summary>
    [Fact]
    public void Key_binding_names_are_english()
    {
        BindingLabels.Lookup gui = Gui();
        var failures = new List<string>();
        var shown = new List<string>();
        foreach ((string source, KeyLayout layout) in Layouts())
        {
            foreach ((string key, string command) in layout.Bindings)
            {
                if (command.Length == 0 || command.StartsWith("Axis ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string label = BindingLabels.Label(command, gui);
                shown.Add(label);
                if (French(label) is { } why)
                {
                    failures.Add($"{source} {key}: '{label}' ({why})");
                }
            }
        }

        Assert.NotEmpty(shown);
        Assert.True(failures.Count == 0, "French in the key list:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Key_binding_names_follow_the_original_rows()
    {
        BindingLabels.Lookup english = (section, key) => (section, key) switch
        {
            ("Browser_Keys", "KeyLabel[1]") => "Forward",
            ("Browser_Keys", "KeyLabel[14]") => "Switch Vision",
            ("ExtraKeyRows", "ChatLabel") => "Chat",
            _ => null,
        };
        Assert.Equal("Forward", BindingLabels.Label("AliasHaut", english));
        Assert.Equal("Forward", BindingLabels.Label("aliashaut", english));
        Assert.Equal("Switch Vision", BindingLabels.Label("SwitchHeadset  3", english));
        Assert.Equal("Chat", BindingLabels.Label("Talk", english));
        // No GUI.int: our English, one name per command of the line.
        Assert.Equal("Move left", BindingLabels.Label("AliasGauche", null));
        Assert.Equal("Fire gadget / Throw slot 2 gadget / Hold breath", BindingLabels.Label("AltFire | GadgetQuickChoice 1 | HoldBreath", null));
        Assert.Equal("Show menu thing", BindingLabels.Label("ShowMenuThing", null));
        Assert.True(BindingLabels.Order("AliasHaut") < BindingLabels.Order("Quit"));
        Assert.Equal(BindingLabels.Rows.Count, BindingLabels.Order("SomethingElse"));
    }

    /// <summary>Every text widget of the converted Magma packages (HUD, options, common popups) as the game first shows it.</summary>
    [Fact]
    public void Magma_page_texts_are_english()
    {
        string ui = UiContent();
        Assert.SkipWhen(!File.Exists(Path.Combine(ui, "strings.json")), "The ui content step has not been run into .content/shared/ui.");
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> strings = Strings(ui);
        Dictionary<string, Dictionary<string, string>> magma = strings.GetValueOrDefault("Magma") ?? new(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        int texts = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(ui, "magma"), "*.json"))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (JsonElement area in doc.RootElement.GetProperty("areas").EnumerateArray())
            {
                string areaName = area.GetProperty("name").GetString() ?? "";
                foreach (JsonElement w in Widgets(area))
                {
                    if (!w.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "text")
                    {
                        continue;
                    }

                    string name = w.GetProperty("name").GetString() ?? "";
                    string page = w.TryGetProperty("text", out JsonElement t) ? t.GetString() ?? "" : "";
                    string shown = EnglishText.ResolveMagma(areaName, name, page,
                        (a, k) => magma.TryGetValue(a, out Dictionary<string, string>? keys) && keys.TryGetValue(k, out string? v) ? v : null);
                    texts++;
                    if (French(shown) is { } why)
                    {
                        failures.Add($"{Path.GetFileNameWithoutExtension(file)} {areaName}/{name}: '{shown}' ({why})");
                    }
                }
            }
        }

        Assert.True(texts > 100, $"only {texts} Magma texts");
        Assert.True(failures.Count == 0, "French on the Magma pages:\n" + string.Join("\n", failures));
    }

    /// <summary>The localisation strings the HUD and the menus read (Magma.int, MagmaDynamic.int, Engine.int, the key list's GUI.int sections).</summary>
    [Fact]
    public void Localised_menu_strings_are_english()
    {
        string ui = UiContent();
        Assert.SkipWhen(!File.Exists(Path.Combine(ui, "strings.json")), "The ui content step has not been run into .content/shared/ui.");
        var failures = new List<string>();
        foreach ((string file, Dictionary<string, Dictionary<string, string>> sections) in Strings(ui))
        {
            foreach ((string section, Dictionary<string, string> keys) in sections)
            {
                if (file == "GUI" && section is not ("Browser_Keys" or "ExtraKeyRows" or "Keyboard_Binding"))
                {
                    continue;
                }

                foreach ((string key, string value) in keys)
                {
                    if (French(value) is { } why)
                    {
                        failures.Add($"{file} [{section}] {key}: '{value}' ({why})");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, "French in the localisation:\n" + string.Join("\n", failures));
    }

    /// <summary>String literals of the front end, options and HUD code.</summary>
    [Fact]
    public void Menu_and_hud_code_strings_are_english()
    {
        string game = Path.Combine(RepoRoot(), "game", "Scripts");
        var failures = new List<string>();
        foreach (string dir in new[] { "UI", "Options", "Hud", "Chat" })
        {
            string path = Path.Combine(game, dir);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (file.EndsWith("EnglishText.cs", StringComparison.Ordinal) || file.EndsWith("BindingLabels.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"((?:[^\"\\\r\n]|\\.)*)\""))
                {
                    // Shown text has words; identifiers of the original (area and widget names, class paths) have no spaces.
                    string literal = m.Groups[1].Value;
                    if ((literal.Contains(' ', StringComparison.Ordinal) || Accents().IsMatch(literal)) && French(literal) is { } why)
                    {
                        failures.Add($"{Path.GetRelativePath(game, file)}: \"{literal}\" ({why})");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, "French in the code's strings:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<JsonElement> Widgets(JsonElement parent)
    {
        foreach (string list in new[] { "widgets", "children" })
        {
            if (parent.TryGetProperty(list, out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement w in items.EnumerateArray())
                {
                    yield return w;
                    foreach (JsonElement child in Widgets(w))
                    {
                        yield return child;
                    }
                }
            }
        }
    }

    /// <summary>The stock layouts of the reference install when present, and the layout the input falls back to.</summary>
    private static IEnumerable<(string Source, KeyLayout Layout)> Layouts()
    {
        // The input layer's own source holds the reference install's path and the fallback layout.
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "game", "Scripts", "Input", "InputBindings.cs"));
        string root = Environment.GetEnvironmentVariable("SVM_GAME") is { Length: > 0 } env
            ? env
            : Regex.Match(source, "ReferenceGameFolder = @\"([^\"]+)\"").Groups[1].Value is { Length: > 0 } folder
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder)
                : string.Empty;
        foreach (string name in new[] { "Default_kbd_0.ini", "Default_kbd_1.ini" })
        {
            string path = Path.Combine(root, "System", name);
            if (root.Length > 0 && File.Exists(path))
            {
                yield return (name, KeyLayout.Load(path));
            }
        }

        Match fallback = Regex.Match(source, "FallbackLayout = \"\"\"(.*?)\"\"\"", RegexOptions.Singleline);
        Assert.True(fallback.Success, "no FallbackLayout in InputBindings.cs");
        yield return ("fallback", KeyLayout.Parse(fallback.Groups[1].Value));
    }

    private static BindingLabels.Lookup Gui()
    {
        string ui = UiContent();
        if (!File.Exists(Path.Combine(ui, "strings.json")))
        {
            return (_, _) => null;
        }

        Dictionary<string, Dictionary<string, string>>? gui = Strings(ui).GetValueOrDefault("GUI");
        return (section, key) => gui is not null && gui.TryGetValue(section, out Dictionary<string, string>? keys) && keys.TryGetValue(key, out string? v) ? v : null;
    }

    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> Strings(string ui)
    {
        var table = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(
            File.ReadAllText(Path.Combine(ui, "strings.json"))) ?? new();
        return table.ToDictionary(
            f => f.Key,
            f => f.Value.ToDictionary(s => s.Key, s => new Dictionary<string, string>(s.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string UiContent() =>
        Environment.GetEnvironmentVariable("SVM_CONTENT") is { Length: > 0 } root
            ? Path.Combine(root, "shared", "ui")
            : Path.Combine(RepoRoot(), ".content", "shared", "ui");

    private static string RepoRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "OpenSCCT.slnx")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("OpenSCCT.slnx not found above " + AppContext.BaseDirectory);
    }
}
