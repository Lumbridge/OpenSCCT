using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    public static InputBindings Load(PlaySide side, string? gameRoot = null, bool seedInputMap = true)
    {
        // Shared layouts (rl:input.keyboard, use_split_spy_merc_keyboard off): both sides play the spy's files.
        PlaySide fileSide = Shared ? PlaySide.Spy : side;
        string file = fileSide == PlaySide.Spy ? "Default_kbd_0.ini" : "Default_kbd_1.ini";
        string original = Path.Combine(GameRoot(gameRoot), "System", file);
        KeyLayout layout;
        string source;
        if (File.Exists(original))
        {
            layout = KeyLayout.Load(original);
            source = original;
        }
        else
        {
            layout = KeyLayout.Parse(FallbackLayout);
            source = "built-in";
        }

        string userFile = fileSide == PlaySide.Spy ? "user://svm_keys_spy.ini" : "user://svm_keys_merc.ini";
        string userPath = ProjectSettings.GlobalizePath(userFile);
        if (OpenScctGame.Options.SettingsStore.Isolated)
        {
            // Smoke runs play the stock layout and leave the player's key file alone (SettingsStore.Isolated).
        }
        else if (File.Exists(userPath))
        {
            KeyLayout user = KeyLayout.Parse(File.ReadAllText(userPath));
            DropMovedStockKeys(layout, user);
            foreach (KeyValuePair<string, string> b in user.Bindings)
            {
                layout.Bind(b.Key, b.Value);
            }
        }
        else
        {
            TryWrite(userPath, layout);
        }

        AddGearKey(layout);
        var bindings = new InputBindings(layout, source, userFile, side, seedInputMap);
        if (seedInputMap)
        {
            bindings.SeedInputMap();
        }

        return bindings;
    }

    /// <summary>
    /// The user file holds the whole layout as it was saved, so a stock key it leaves out was moved or cleared by
    /// the player: that key is dropped when the user file binds its command elsewhere. Only a stock key whose
    /// command the user file has nowhere (a newer stock layout) is kept.
    /// </summary>
    private static void DropMovedStockKeys(KeyLayout layout, KeyLayout user)
    {
        var userCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string command in user.Bindings.Values)
        {
            if (command.Length > 0)
            {
                userCommands.Add(command);
            }
        }

        foreach (string key in new List<string>(layout.Bindings.Keys))
        {
            if (!user.Bindings.ContainsKey(key) && userCommands.Contains(layout.Bindings[key]))
            {
                layout.Bind(key, "");
            }
        }
    }

    /// <summary>The gear picker's default key: free in both stock layouts and among the hub's keys.</summary>
    public const string GearKey = "I";

    /// <summary>
    /// An OpenSCCT addition (spec/additions/gear-change.md): the stock layouts have no gear picker command, so
    /// <see cref="GearKey"/> runs it when no key does and that key is free. A key the player moves it to is kept by
    /// the user file like any other binding.
    /// </summary>
    private static void AddGearKey(KeyLayout layout)
    {
        foreach (string command in layout.Bindings.Values)
        {
            if (string.Equals(command, OpenScctGame.Play.PlayMenu.GearCommand, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        if (!layout.Bindings.ContainsKey(GearKey))
        {
            layout.Bind(GearKey, OpenScctGame.Play.PlayMenu.GearCommand);
        }
    }

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

    /// <summary>One InputMap action per bound key, holding that key's event.</summary>
    public void SeedInputMap()
    {
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
            var sb = new StringBuilder();
            sb.Append("; OpenSCCT key layout. Same format as the game's Default_kbd_*.ini: IK_<key>=<alias or command>.\n");
            sb.Append("[Engine.Input]\n");
            foreach (KeyValuePair<string, string> b in layout.Bindings)
            {
                sb.Append("IK_").Append(b.Key).Append('=').Append(b.Value).Append('\n');
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, sb.ToString());
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

    /// <summary>
    /// The control scheme used when the game's System folder is missing: the commands the input system needs, bound
    /// to the keys the task names (WASD, Shift Use, Space Jump, C crouch, Z back to wall, Q roll, E fire mode, F rate
    /// of fire, mouse buttons, 1 to 4 gadgets, Tab / F1 / F2 vision, B binoculars, R reload, middle mouse speed).
    /// </summary>
    private const string FallbackLayout = """
        [Engine.Input]
        IK_W=Button BT_Up
        IK_S=Button BT_Down
        IK_A=Button BT_Left
        IK_D=Button BT_Right
        IK_Shift=InvButton bUsing
        IK_Space=Function Jump | Axis aUp Speed=+280.0
        IK_C=Button bCrouch
        IK_Z=InvButton bUseBackToWall
        IK_Q=Button bRoll | GadLaserSwitch
        IK_E=InvButton bFireMode | GadTorcheSwitch
        IK_F=Button bROF
        IK_R=Button bReload
        IK_B=SwitchBinocularVision
        IK_LeftMouse=Button bFire | Function Fire | GadgetQuickChoice 0
        IK_RightMouse=Button bAltFire | Function AltFire | GadgetQuickChoice 1 | Button bHoldBreath
        IK_MiddleMouse=ChangeSpeed
        IK_1=SelectGadget 0
        IK_2=SelectGadget 1
        IK_3=SelectGadget 2
        IK_4=SelectGadget 3
        IK_Tab=SwitchHeadset 3
        IK_F1=SwitchHeadset 1
        IK_F2=SwitchHeadset 2
        IK_MouseX=Axis aMouseX Speed=6.00
        IK_MouseY=Axis aMouseY Speed=6.00
        IK_MouseWheelUp=WheelUp
        IK_MouseWheelDown=WheelDown
        """;
}
