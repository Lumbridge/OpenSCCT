using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Controls;

/// <summary>
/// The key layout of one side: the original <c>Default_kbd_0.ini</c> (spy) or <c>Default_kbd_1.ini</c> (merc) from
/// the game's System folder, with the player's own changes from <c>user://svm_keys_spy.ini</c> or
/// <c>user://svm_keys_merc.ini</c> on top (same <c>[Engine.Input]</c> IK_ format, written on the first run so it can
/// be edited). The Godot InputMap gets one action per bound key (<c>svm_IK_W</c> and so on), so the layout is also
/// visible to Godot tools.
/// </summary>
public sealed class InputBindings
{
    public const string ActionPrefix = "svm_IK_";

    /// <summary>The reference install the content pipeline also defaults to (content/svmbuild/paths.py).</summary>
    public static string ReferenceGameRoot => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ReferenceGameFolder);

    /// <summary><see cref="ReferenceGameRoot"/> under the user's profile folder.</summary>
    public const string ReferenceGameFolder = @"Desktop\Enhanced SCCT Versus v4.0 Public Beta 3";

    private readonly Dictionary<string, List<KeyAction>> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _seedsInputMap;

    private InputBindings(KeyLayout layout, string source, string userFile, PlaySide side, bool seedsInputMap)
    {
        Layout = layout;
        Source = source;
        UserFile = userFile;
        Side = side;
        _seedsInputMap = seedsInputMap;
        foreach (string key in layout.Bindings.Keys)
        {
            _resolved[key] = layout.Resolve(key);
        }
    }

    /// <summary>Raised after a rebind wrote a side's user file (the account's profile sync sends the change).</summary>
    public static event Action? UserFileSaved;

    /// <summary>Raised after a rebind changed a side's layout, so a running round reloads that side's keys.</summary>
    public static event Action<PlaySide>? Rebound;

    public KeyLayout Layout { get; }

    /// <summary>The side this layout is for.</summary>
    public PlaySide Side { get; }

    /// <summary>Where the base layout came from (an ini path, or "built-in").</summary>
    public string Source { get; }

    /// <summary>The player's override file (user://).</summary>
    public string UserFile { get; }

    /// <summary>The mouse axis speed of the layout (<c>IK_MouseX=Axis aMouseX Speed=6.00</c>).</summary>
    public float MouseAxisSpeed
    {
        get
        {
            foreach (KeyAction a in Actions("MouseX"))
            {
                if (a.Kind == KeyActionKind.Axis)
                {
                    return a.Speed;
                }
            }

            return 6f;
        }
    }

    /// <summary>The resolved actions of a key (IK name without the prefix).</summary>
    public IReadOnlyList<KeyAction> Actions(string key) =>
        _resolved.TryGetValue(key, out List<KeyAction>? actions) ? actions : Array.Empty<KeyAction>();

    /// <summary>An isolated in-memory layout for runtime checks; does not seed InputMap or read/write user files.</summary>
    internal static InputBindings FromLayout(KeyLayout layout, PlaySide side) => new(layout, "in-memory",
        side == PlaySide.Spy ? "user://svm_keys_spy.ini" : "user://svm_keys_merc.ini", side, seedsInputMap: false);

    /// <summary>Both sides play one layout, the spy's (the setting use_split_spy_merc_keyboard is off).</summary>
    public static bool Shared => !OpenScctGame.Options.SettingsStore.Current.SplitKeyboard;

    /// <summary>Tells running rounds and pages that every side's layout may have changed (the split was switched).</summary>
    public static void RaiseReboundAll()
    {
        Rebound?.Invoke(PlaySide.Spy);
        Rebound?.Invoke(PlaySide.Merc);
    }

    /// <summary>The game root: <c>--svm-game-root=</c>, then <c>SVM_GAME_ROOT</c>, then the reference install.</summary>
    public static string GameRoot(string? option)
    {
        if (!string.IsNullOrWhiteSpace(option))
        {
            return option;
        }

        string? env = System.Environment.GetEnvironmentVariable("SVM_GAME_ROOT");
        return string.IsNullOrWhiteSpace(env) ? ReferenceGameRoot : env;
    }

    /// <summary>Loads the layout of <paramref name="side"/>, applies the user file and seeds the InputMap.</summary>
    public static InputBindings Load(PlaySide side, string? gameRoot = null, bool seedInputMap = true, bool? sharedOverride = null, bool draft = false, bool defaults = false)
    {
        // Shared layouts (rl:input.keyboard, use_split_spy_merc_keyboard off): both sides play the spy's files.
        PlaySide fileSide = (sharedOverride ?? Shared) ? PlaySide.Spy : side;
        string file = fileSide == PlaySide.Spy ? "Default_kbd_0.ini" : "Default_kbd_1.ini";
        string original = Path.Combine(GameRoot(gameRoot), "System", file);
        KeyLayout layout;
        string source;
        if (File.Exists(original))
        {
            layout = KeyLayout.Load(original);
            source = original;
        }
        else if (SharedLayout(file) is { } shared)
        {
            // No install: the stock layout the ui step copied into the shared content (built or downloaded).
            layout = KeyLayout.Load(shared);
            source = shared;
        }
        else
        {
            layout = DefaultKeyLayout.Create();
            source = "built-in";
        }

        string userFile = fileSide == PlaySide.Spy ? "user://svm_keys_spy.ini" : "user://svm_keys_merc.ini";
        string userPath = ProjectSettings.GlobalizePath(userFile);
        string? userText = null;
        bool writeInitialSnapshot = false;
        if (defaults || OpenScctGame.Options.SettingsStore.Isolated)
        {
            // Smoke runs play the stock layout and leave the player's key file alone (SettingsStore.Isolated).
        }
        else if (File.Exists(userPath))
        {
            userText = File.ReadAllText(userPath);
        }
        else if (!draft)
        {
            writeInitialSnapshot = true;
        }

        layout = DefaultKeyLayout.ApplyProfile(layout, userText, OpenScctGame.Options.SettingsStore.Current.MouseWheel);
        // Loading tips can be the first caller. Persist the same complete layout the first round will use.
        if (writeInitialSnapshot) TryWrite(userPath, layout);
        var bindings = new InputBindings(layout, source, userFile, side, seedInputMap);
        if (seedInputMap)
        {
            bindings.SeedInputMap();
        }

        return bindings;
    }

    /// <summary>The stock layout <paramref name="file"/> in the shared content's ui/keys, or null.</summary>
    private static string? SharedLayout(string file)
    {
        string path = Path.Combine(OpenScctGame.Hud.HudAssets.DefaultRoot(), "keys", file);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The gear picker's default key: free in both stock layouts and among the hub's keys.</summary>
    public const string GearKey = DefaultKeyLayout.GearKey;

    /// <summary>
    /// Changes one key and saves the user file. The InputMap is only reseeded by a layout that seeds it (the options
    /// screen's copy does not, so editing the other side's keys leaves the played side's InputMap alone); a running
    /// round picks the change up through <see cref="Rebound"/>.
    /// </summary>
    public InputBindings Rebind(string key, string command)
    {
        Layout.Bind(key, command);
        if (TryWrite(ProjectSettings.GlobalizePath(UserFile), Layout))
        {
            UserFileSaved?.Invoke();
        }

        var updated = new InputBindings(Layout, Source, UserFile, Side, _seedsInputMap);
        if (_seedsInputMap)
        {
            updated.SeedInputMap();
        }

        Rebound?.Invoke(Side);
        if (Shared)
        {
            // One layout for both sides: the other side's round changes too.
            Rebound?.Invoke(Side == PlaySide.Spy ? PlaySide.Merc : PlaySide.Spy);
        }

        return updated;
    }

    /// <summary>Commit a whole options draft. Editing never calls this method.</summary>
    public bool SaveDraft(KeyLayout draft)
    {
        if (!OpenScctGame.Options.SettingsStore.Isolated && !TryWrite(ProjectSettings.GlobalizePath(UserFile), draft)) return false;
        UserFileSaved?.Invoke();
        RaiseReboundAll();
        return true;
    }

    /// <summary>One InputMap action per bound key, holding that key's event.</summary>
    public void SeedInputMap()
    {
        OpenScctGame.Voice.VoiceInput.BindLayout(this);
        foreach (StringName action in InputMap.GetActions())
        {
            if (action.ToString().StartsWith(ActionPrefix, StringComparison.Ordinal))
            {
                InputMap.EraseAction(action);
            }
        }

        foreach (string key in Layout.Bindings.Keys)
        {
            if (GodotKeys.ToEvent(key) is not { } ev)
            {
                continue;
            }

            string action = ActionPrefix + key;
            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, ev);
        }
    }

    /// <summary>Writes the layout to a user file; false when nothing was written (smoke runs, a write error).</summary>
    private static bool TryWrite(string path, KeyLayout layout)
    {
        if (OpenScctGame.Options.SettingsStore.Isolated)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            string temp = path + ".tmp";
            File.WriteAllText(temp, layout.ToIni());
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (IOException e)
        {
            GD.PushWarning($"Could not write the key layout {path}: {e.Message}");
        }
        catch (UnauthorizedAccessException e)
        {
            GD.PushWarning($"Could not write the key layout {path}: {e.Message}");
        }

        return false;
    }
}
