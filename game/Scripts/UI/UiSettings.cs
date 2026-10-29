using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// The player's front-end settings: name, mouse inversion, and the last joined address, kept in
/// <c>user://settings.cfg</c>. Key bindings are the play input's own layouts (OpenScctGame.Controls.InputBindings: the
/// original Default_kbd_0/1.ini with the player's changes in user://svm_keys_spy.ini and svm_keys_merc.ini), which
/// the options screen edits.
/// </summary>
public sealed class UiSettings
{
    public const string FilePath = "user://settings.cfg";

    private static UiSettings? _current;

    public string PlayerName { get; set; } = "Player";

    public bool InvertMouse { get; set; }

    public string LastAddress { get; set; } = "127.0.0.1";

    /// <summary>The settings loaded at first use.</summary>
    public static UiSettings Current => _current ??= Load();

    public static UiSettings Load(string path = FilePath)
    {
        var settings = new UiSettings();
        var file = new ConfigFile();
        if (file.Load(path) != Error.Ok)
        {
            return settings;
        }

        settings.PlayerName = file.GetValue("player", "name", settings.PlayerName).AsString();
        settings.InvertMouse = file.GetValue("input", "invert", settings.InvertMouse).AsBool();
        settings.LastAddress = file.GetValue("network", "last_address", settings.LastAddress).AsString();
        return settings;
    }

    public Error Save(string path = FilePath)
    {
        var file = new ConfigFile();
        file.SetValue("player", "name", PlayerName);
        file.SetValue("input", "invert", InvertMouse);
        file.SetValue("network", "last_address", LastAddress);
        return file.Save(path);
    }
}
