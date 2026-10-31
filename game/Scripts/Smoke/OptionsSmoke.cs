using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Draft behavior and every role subsection at multiple viewport sizes, including the in-game overlay.</summary>
public sealed class OptionsSmoke : SmokeTest
{
    public const string TestName = "options";
    public override string Name => TestName;
    private OptionsMenu? _options;
    private readonly List<string> _problems = new();
    private readonly Vector2I[] _sizes = [new(1280, 720), new(1024, 768), new(1920, 1080)];
    private int _size, _tab, _section, _frames;
    private bool _advanced;

    public override SmokeResult Step(SmokeContext context)
    {
        SceneTree tree = context.Tree;
        if (_options is null)
        {
            _options = (OptionsMenu)FrontEnd.ShowOptions(tree);
            _options.Overlay = true;
            CheckBehavior(_options);
            _options.QueueFree();
            _options = new OptionsMenu { Overlay = true };
            tree.Root.AddChild(_options);
            tree.CurrentScene = _options;
            Resize(tree, _sizes[_size]);
        }
        if (++_frames == 1)
        {
            _options.Tabs!.CurrentTab = _tab;
            if (_options.Tabs.GetCurrentTabControl().GetNodeOrNull<TabContainer>("Sections") is { } sections)
            {
                sections.CurrentTab = _section;
                foreach (Node node in sections.GetCurrentTabControl().FindChildren("*", nameof(CheckButton), true, false))
                    if (node is CheckButton button && button.Text == "Advanced settings") button.ButtonPressed = _advanced;
            }
        }
        if (_frames < 8) return SmokeResult.Running;
        string where = $"{_sizes[_size].X} options {_tab}/{_section} advanced={_advanced}";
        var problems = LayoutCheck.Problems(_options, _sizes[_size].X);
        _problems.AddRange(problems.Select(p => where + ": " + p));
        MenuPage.Shot(tree.Root, $"options-{_sizes[_size].X}-{_tab}-{_section}-{_advanced}");
        _frames = 0;
        if (_tab < 2)
        {
            if (!_advanced && _section is 1 or 2) { _advanced = true; return SmokeResult.Running; }
            _advanced = false;
            if (++_section < 4) return SmokeResult.Running;
            _section = 0;
        }
        if (++_tab < _options.Tabs!.GetTabCount()) return SmokeResult.Running;
        _tab = 0;
        if (++_size < _sizes.Length) { Resize(tree, _sizes[_size]); return SmokeResult.Running; }
        return _problems.Count == 0 ? SmokeResult.Pass("draft/apply/discard, role bindings, native controller actions, all subsections at 1024/1280/1920")
            : SmokeResult.Fail(string.Join("; ", _problems.Take(12)));
    }

    private void Expect(bool value, string message) { if (!value) _problems.Add(message); }

    private void CheckBehavior(OptionsMenu options)
    {
        string original = SettingsStore.Current.ToJson();
        int writes = 0;
        void Wrote() => writes++;
        InputBindings.UserFileSaved += Wrote;
        try
        {
            Expect(!options.HasUnsavedChanges, "fresh editor is clean");
            Expect(options.Tabs!.GetTabTitle(0) == "Spy" && options.Tabs.GetTabTitle(1) == "Mercenary", "role tabs");
            Expect(!options.Controls.ContainsKey("controls_spy_kbm.look_response"), "mouse response selector removed");
            options.LoadLayout(PlaySide.Spy);
            var draft = options.Keys!.Draft!;
            string baseline = draft.Layout.ToIni();
            var breath = ControlActions.All.Single(a => a.Id == "breath");
            draft.Assign(breath, "X");
            draft.Assign(ControlActions.All.Single(a => a.Id == "voice"), "J");
            options.Editing.SoundVolume = 0.27f;
            Expect(writes == 0 && original == SettingsStore.Current.ToJson(), "edits do not write or apply");
            Expect(options.HasUnsavedChanges, "dirty after binding edit");
            options.Back();
            var confirm = options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation");
            Expect(confirm is not null, "Back confirms discard");
            confirm?.Answer(false);
            Expect(draft.Dirty, "cancel discard retains draft");
            options.Editing.SplitKeyboard = false;
            options.LoadLayout(PlaySide.Merc);
            Expect(ReferenceEquals(draft, options.Keys!.Draft), "shared edits use same draft");
            options.Editing.SplitKeyboard = true;
            options.LoadLayout(PlaySide.Merc);
            Expect(!ReferenceEquals(draft, options.Keys!.Draft), "separate role draft retained");
            Expect(options.Save() == Error.Ok, "Apply succeeds");
            Expect(!options.HasUnsavedChanges && writes > 0, "Apply accepts draft without closing");
            Expect(InputMap.ActionGetEvents(OpenScctGame.Voice.VoiceInput.PushToTalk).OfType<InputEventKey>().Any(k => k.PhysicalKeycode == Key.J), "Apply updates voice shortcut InputMap");
            Expect(Math.Abs(SettingsStore.Current.SoundVolume - 0.27f) < 0.001f, "Apply publishes settings");
            options.LoadLayout(PlaySide.Spy);
            Expect(options.Keys!.Draft!.Keys(breath).Contains("X"), "Apply retains draft binding");
            options.Defaults();
            Expect(options.HasUnsavedChanges, "reset stages changes");
            Expect(Math.Abs(SettingsStore.Current.SoundVolume - 0.27f) < 0.001f, "reset does not apply");
            options.Editing.SpyKbm.Look = 2f;
            options.Editing.MercKbm.Look = 3f;
            var resetSpy = options.FindChild("ResetSpy", true, false) as Button;
            resetSpy?.EmitSignal(BaseButton.SignalName.Pressed);
            options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation")?.Answer(true);
            Expect(options.Editing.SpyKbm.Look == new GameSettings().SpyKbm.Look && options.Editing.MercKbm.Look == 3f, "role reset leaves other role values");
            bool closed = false;
            options.Closed += () => closed = true;
            int appliedWrites = writes;
            options.Back();
            options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation")?.Answer(true);
            Expect(closed && writes == appliedWrites && Math.Abs(SettingsStore.Current.SoundVolume - 0.27f) < 0.001f, "confirmed discard closes without applying");
            closed = false;
            var saveClose = options.FindChild("SaveAndClose", true, false) as Button;
            saveClose?.EmitSignal(BaseButton.SignalName.Pressed);
            Expect(closed && !options.HasUnsavedChanges, "Save & Close applies and closes");
            // A direct controller action works without a physical key and keeps the held byte + callback together.
            var input = new PlayInput { Bindings = InputBindings.Load(PlaySide.Spy, seedInputMap: false, draft: true), RequireCapture = false };
            var local = new LocalInput();
            input.Key(ControlActions.VirtualPrefix + "RT:altfire", true);
            input.FillTick(local);
            Expect(local[LocalInput.IndexOf("bAltFire")] == 1 && local.Commands.Any(c => c.Name == "AltFire"), "direct pad action dispatch");
            Expect(local[LocalInput.IndexOf("bHoldBreath")] == 0 && local.Commands.All(c => c.Name != "GadgetQuickChoice"), "direct pad action excludes combined keyboard actions");
            input.ClearHeld(local);
            local.EndTick();
            var behaviorSettings = new GameSettings();
            behaviorSettings.SpyKbm.WalkBehavior = "Hold";
            SettingsStore.Reset(behaviorSettings);
            input.Key(ControlActions.VirtualPrefix + "LS:speed", true);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetWalk" && c.Arg == 1), "hold Walk press");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "LS:speed", false);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetWalk" && c.Arg == 0), "hold Walk release returns to run");
            input.ClearHeld(local);
            local.EndTick();
            behaviorSettings.MercKbm.ScopeBehavior = "Hold";
            input.Bindings = InputBindings.Load(PlaySide.Merc, seedInputMap: false, draft: true);
            input.Key(ControlActions.VirtualPrefix + "RS:scope", true);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetScope" && c.Arg == 1), "hold scope press");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RS:scope", false);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetScope" && c.Arg == 0) && !local.Commands.Any(c => c.Name == "SetScope" && c.Arg == 1), "hold scope release");
            input.Free();
            // Restore the isolated settings so the layout pass starts with real defaults.
            SettingsStore.Reset(GameSettings.FromJson(original));
        }
        finally { InputBindings.UserFileSaved -= Wrote; }
    }

    private static void Resize(SceneTree tree, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
        }
        tree.Root.Size = size;
    }
}
