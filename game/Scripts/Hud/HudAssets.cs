using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>
/// The converted UI content (<c>shared/ui</c>, written by <c>python -m svmbuild ui</c>): Magma packages, their
/// atlases, the bitmap fonts and the Magma strings, loaded once. Textures load lazily and only when asked, so the
/// headless smoke and the data side work without a renderer.
/// </summary>
public sealed class HudAssets
{
    public const string ManifestFile = "manifest.json";
    public const string UiFormat = "svm.ui/1";
    public const string DefaultFont = "HandelGothic BT Regular 20";

    private readonly Dictionary<string, MagmaPackage> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MagmaFont> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, string), Vector2I> _textureSizes = new();
    private readonly Dictionary<(string, string), string> _textureFiles = new();
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _strings = new(StringComparer.OrdinalIgnoreCase);

    private HudAssets(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public MagmaPackage Hud => Package("Hud") ?? throw new InvalidOperationException("the Hud package is missing");

    public MagmaPackage? Package(string name) => _packages.TryGetValue(name, out MagmaPackage? p) ? p : null;

    /// <summary>The ui folder: next to the executable for an export, else the repository's .content/shared/ui.</summary>
    public static string DefaultRoot()
    {
        // An installed build: the content the launcher built (--svm-content).
        if (OpenScct.Server.ContentPaths.Shared is { } content && File.Exists(System.IO.Path.Combine(content, "ui", ManifestFile)))
        {
            return System.IO.Path.Combine(content, "ui");
        }

        string executableDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
        string beside = System.IO.Path.Combine(executableDir, "shared", "ui");
        if (OS.HasFeature("template") || File.Exists(System.IO.Path.Combine(beside, ManifestFile)))
        {
            return beside;
        }

        string projectDir = ProjectSettings.GlobalizePath("res://");
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", ".content", "shared", "ui"));
    }

    /// <summary>Loads the content under <paramref name="root"/>, or returns null with the reason when it is not there.</summary>
    public static HudAssets? TryLoad(string? root, out string? error)
    {
        root ??= DefaultRoot();
        string manifestPath = System.IO.Path.Combine(root, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            error = $"no UI content at {root} (run: python -m svmbuild ui)";
            return null;
        }

        try
        {
            var assets = new HudAssets(root);
            assets.LoadManifest(manifestPath);
            error = null;
            return assets;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or KeyNotFoundException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            error = $"UI content at {root} could not be read: {ex.Message}";
            return null;
        }
    }

    private void LoadManifest(string manifestPath)
    {
        using (FileStream stream = File.OpenRead(manifestPath))
        using (JsonDocument doc = JsonDocument.Parse(stream))
        {
            JsonElement root = doc.RootElement;
            if (root.GetProperty("format").GetString() != UiFormat)
            {
                throw new InvalidDataException($"{manifestPath}: not a {UiFormat} manifest");
            }

            foreach (JsonProperty p in root.GetProperty("packages").EnumerateObject())
            {
                string file = System.IO.Path.Combine(Root, p.Value.GetProperty("file").GetString() ?? "");
                _packages[p.Name] = MagmaPackage.Load(file);
            }

            foreach (JsonProperty p in root.GetProperty("fonts").EnumerateObject())
            {
                _fonts[p.Name] = MagmaFont.Load(System.IO.Path.Combine(Root, p.Value.GetProperty("file").GetString() ?? ""));
            }

            foreach (JsonProperty group in root.GetProperty("textures").EnumerateObject())
            {
                foreach (JsonElement t in group.Value.EnumerateArray())
                {
                    string name = t.GetProperty("name").GetString() ?? "";
                    var key = (group.Name.ToUpperInvariant(), name.ToUpperInvariant());
                    _textureSizes[key] = new Vector2I(t.GetProperty("width").GetInt32(), t.GetProperty("height").GetInt32());
                    _textureFiles[key] = System.IO.Path.Combine(Root, "textures", group.Name, name + ".png");
                }
            }
        }

        string stringsPath = System.IO.Path.Combine(Root, "strings.json");
        if (File.Exists(stringsPath))
        {
            using FileStream stream = File.OpenRead(stringsPath);
            using JsonDocument doc = JsonDocument.Parse(stream);
            foreach (JsonProperty file in doc.RootElement.EnumerateObject())
            {
                var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty section in file.Value.EnumerateObject())
                {
                    var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty key in section.Value.EnumerateObject())
                    {
                        keys[key.Name] = key.Value.GetString() ?? "";
                    }

                    sections[section.Name] = keys;
                }

                _strings[file.Name] = sections;
            }
        }

        ApplyMagmaStrings();
    }

    /// <summary>
    /// Magma.int replaces text widget strings by [Area] Widget=Text, as the game's localisation does; the few widgets
    /// whose page text is French with no Magma.int entry take the English of <see cref="OpenScctGame.UI.EnglishText"/>.
    /// </summary>
    private void ApplyMagmaStrings()
    {
        Dictionary<string, Dictionary<string, string>>? sections = _strings.GetValueOrDefault("Magma");
        foreach (MagmaPackage package in _packages.Values)
        {
            foreach (MagmaArea area in package.Areas)
            {
                Dictionary<string, string>? keys = sections?.GetValueOrDefault(area.Name);
                foreach (MagmaWidget w in area.Widgets)
                {
                    if (w.Kind == MagmaWidgetKind.Text)
                    {
                        w.Text = OpenScctGame.UI.EnglishText.ResolveMagma(area.Name, w.Name, w.Text,
                            (_, widget) => keys is not null && keys.TryGetValue(widget, out string? text) ? text : null);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The name the game shows for a key (Engine.int [BaseGUIController] LocalizedKeyName by EInputKey: "Mouse Wheel
    /// Up", "Right Mouse", "E"), or the IK name itself when the table is missing.
    /// </summary>
    public string KeyDisplayName(string ik)
    {
        ArgumentNullException.ThrowIfNull(ik);
        if (Enum.TryParse("IK_" + ik, ignoreCase: true, out OpenScct.Core.Engine.EInputKey key))
        {
            string index = ((int)key).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Localize("Engine", "BaseGUIController", "LocalizedKeyName[" + index + "]", ik);
        }

        return ik;
    }

    /// <summary>A string of System/_PC_/&lt;file&gt;.int, or <paramref name="fallback"/>.</summary>
    public string Localize(string file, string section, string key, string fallback) =>
        _strings.TryGetValue(file, out Dictionary<string, Dictionary<string, string>>? s) &&
        s.TryGetValue(section, out Dictionary<string, string>? k) && k.TryGetValue(key, out string? v) ? v : fallback;

    public MagmaFont? Font(string name) =>
        _fonts.TryGetValue(name, out MagmaFont? f) ? f : _fonts.TryGetValue(DefaultFont, out MagmaFont? d) ? d : null;

    public Vector2I? TextureSize(string package, string texture) =>
        _textureSizes.TryGetValue((package.ToUpperInvariant(), texture.ToUpperInvariant()), out Vector2I size) ? size : null;

    public bool HasTexture(string package, string texture) =>
        _textureFiles.ContainsKey((package.ToUpperInvariant(), texture.ToUpperInvariant()));

    /// <summary>The texture of a package (loaded on first use), or null when missing.</summary>
    public Texture2D? Texture(string package, string texture)
    {
        if (!_textureFiles.TryGetValue((package.ToUpperInvariant(), texture.ToUpperInvariant()), out string? file))
        {
            return null;
        }

        return LoadTexture(file);
    }

    public Texture2D? LoadTexture(string file)
    {
        if (_textures.TryGetValue(file, out Texture2D? cached))
        {
            return cached;
        }

        Texture2D? texture = null;
        if (File.Exists(file))
        {
            Image image = Image.LoadFromFile(file);
            if (image is not null && !image.IsEmpty())
            {
                texture = ImageTexture.CreateFromImage(image);
            }
        }

        _textures[file] = texture;
        return texture;
    }
}
