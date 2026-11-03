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
    private readonly Vector2I[] _sizes =
    [
        new(1280, 720), new(800, 600), new(1024, 768), new(1280, 800), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 1080), new(3840, 2160), new(1280, 720),
    ];
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
            if (_tab == 6)
            {
                var role = _options.FindChild("HudEditorRole", true, false) as OptionButton;
                var aspect = _options.FindChild("HudEditorAspect", true, false) as OptionButton;
                role?.Select(_section / 3); role?.EmitSignal(OptionButton.SignalName.ItemSelected, _section / 3);
                aspect?.Select(_section % 3); aspect?.EmitSignal(OptionButton.SignalName.ItemSelected, _section % 3);
            }
            if (_options.Tabs.GetCurrentTabControl().GetNodeOrNull<TabContainer>("Sections") is { } sections)
            {
                sections.CurrentTab = _section;
                foreach (Node node in sections.GetCurrentTabControl().FindChildren("*", nameof(CheckButton), true, false))
                    if (node is CheckButton button && button.Text == "Advanced settings") button.ButtonPressed = _advanced;
            }
        }
        if (_frames < 8) return SmokeResult.Running;
        string where = $"{_sizes[_size].X}x{_sizes[_size].Y} options {_tab}/{_section} advanced={_advanced}";
        var problems = LayoutCheck.Problems(_options, _sizes[_size].X);
        problems.AddRange(LayoutCheck.BoundsProblems(_options, _sizes[_size]));
        if (_tab == 6 && _options.FindChildren("*", nameof(HudEditorPreview), true, false).OfType<HudEditorPreview>().FirstOrDefault() is { } preview)
        {
            for (Node? parent = preview.GetParent(); parent is not null; parent = parent.GetParent())
                if (parent is ScrollContainer scroll && !scroll.GetGlobalRect().Grow(2f).Encloses(preview.GetGlobalRect()))
                    problems.Add("HUD editor preview is cropped by its scroll area");
            foreach (string id in new[] { "Radar", "Location", "Activity", "Fps", "Network" })
            {
                if (!preview.ElementBounds.TryGetValue(id, out Rect2 bounds) || bounds.Size.X <= 0f || bounds.Size.Y <= 0f)
                    problems.Add("HUD editor has no selectable " + id + " preview");
                else if (!new Rect2(Vector2.Zero, preview.Size).Grow(1f).Encloses(bounds))
                    problems.Add("HUD editor " + id + " outline leaves the preview");
            }
        }
        foreach (Node node in _options.FindChildren("ActionList", nameof(ScrollContainer), true, false))
        {
            if (node is not ScrollContainer list || !list.IsVisibleInTree()) continue;
            if (list.Size.Y < 96f) problems.Add($"bindings list has only {list.Size.Y:0}px of visible height");
            if (list.ScrollVertical == 0 && list.FindChild("Actions", true, false) is GridContainer actions &&
                actions.GetChildren().OfType<Button>().FirstOrDefault() is { } first &&
                first.GetGlobalRect().End.Y > list.GetGlobalRect().End.Y + 1f)
                problems.Add("initial bindings view hides the first action below its instructions");
        }
        _problems.AddRange(problems.Select(p => where + ": " + p));
        MenuPage.Shot(tree.Root, $"options-{_sizes[_size].X}x{_sizes[_size].Y}-{_tab}-{_section}-{_advanced}");
        _frames = 0;
        if (_tab < 2)
        {
            if (!_advanced && _section is 1 or 2) { _advanced = true; return SmokeResult.Running; }
            _advanced = false;
            if (++_section < _options.Tabs!.GetCurrentTabControl().GetNode<TabContainer>("Sections").GetTabCount()) return SmokeResult.Running;
            _section = 0;
        }
        if (_tab == 6) { if (++_section < 6) return SmokeResult.Running; _section = 0; }
        if (++_tab < _options.Tabs!.GetTabCount()) return SmokeResult.Running;
        _tab = 0;
        if (++_size < _sizes.Length) { Resize(tree, _sizes[_size]); return SmokeResult.Running; }
        return _problems.Count == 0 ? SmokeResult.Pass("draft/apply/discard, role bindings, all subsections and advanced settings from 800x600 to 4K, including shrinking after 4K")
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
            CheckHudEditor(options);
            var volume = (NotchedSlider)options.Controls["master_volume"];
            double originalVolume = volume.Value;
            Expect(volume.Resolve(24, released: true) == 25, "pointer release snaps near a quarter volume");
            Expect(volume.Resolve(22, released: true) == 22, "pointer remains precise outside a notch");
            Expect(volume.Resolve(24) == 24, "non-pointer changes bypass notches");
            volume.SetValueNoSignal(originalVolume);
            Expect(options.Tabs!.GetTabTitle(0) == "Spy" && options.Tabs.GetTabTitle(1) == "Mercenary", "role tabs");
            Expect(!options.Controls.ContainsKey("controls_spy_kbm.look_response"), "mouse response selector removed");
            void PickFps(string key, int fps) => ((FrameRateSlider)options.Controls[key]).Select(fps);
            string ShownFps(string key) => ((FrameRateSlider)options.Controls[key]).Text;
            void PickTelemetry(string key, int index)
            {
                var picker = (OptionButton)options.Controls[key];
                picker.Select(index);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
            }
            void SlideTelemetry(string key, float value)
            {
                var slider = (HSlider)options.Controls[key];
                slider.SetValueNoSignal(value);
                slider.EmitSignal(Godot.Range.SignalName.ValueChanged, (double)value);
            }
            PickTelemetry("show_fps", 1);
            Expect(options.Editing.ShowFps && options.Editing.FpsTelemetry.DisplayMode == "Always", "FPS Always selector enables the readout");
            PickTelemetry("show_fps", 0);
            Expect(!options.Editing.ShowFps, "FPS Never selector disables the readout");
            PickTelemetry("show_fps", 2);
            PickTelemetry("show_net", 2);
            Expect(options.Editing.ShowNet && options.Editing.NetworkTelemetry.DisplayMode == "Issues", "network Issues selector enables conditional display");
            PickTelemetry("show_net", 0);
            Expect(!options.Editing.ShowNet, "network Never selector disables the readout");
            PickTelemetry("show_net", 1);
            PickTelemetry("fps_telemetry.graph", 2);
            PickTelemetry("network_telemetry.graph", 1);
            foreach (string key in new[] { "fps_telemetry", "network_telemetry" })
                Expect(!options.Controls.ContainsKey(key + ".position_x") && !options.Controls.ContainsKey(key + ".position_y"),
                    "telemetry position controls live only in HUD editor");
            var fpsPlacement = options.Editing.HudLayout.Element(false, "Fps");
            fpsPlacement.X = -.23f; fpsPlacement.Y = .24f;
            var netPlacement = options.Editing.HudLayout.Element(false, "Network");
            netPlacement.X = -.19f; netPlacement.Y = .39f;
            SlideTelemetry("fps_telemetry.scale", 135f);
            SlideTelemetry("fps_telemetry.opacity", 42f);
            SlideTelemetry("fps_telemetry.graph_seconds", 12f);
            SlideTelemetry("fps_telemetry.issue_hold_seconds", 4.5f);
            SlideTelemetry("fps_telemetry.frame_time_threshold_ms", 25f);
            SlideTelemetry("network_telemetry.scale", 85f);
            SlideTelemetry("network_telemetry.opacity", 68f);
            SlideTelemetry("network_telemetry.graph_seconds", 17f);
            SlideTelemetry("network_telemetry.issue_hold_seconds", 1.5f);
            SlideTelemetry("network_telemetry.ping_threshold_ms", 95f);
            SlideTelemetry("network_telemetry.jitter_threshold_ms", 18f);
            SlideTelemetry("network_telemetry.loss_threshold_percent", 0.5f);
            void ExpectEditedTelemetry(GameSettings settings, string when)
            {
                FpsTelemetrySettings fps = settings.FpsTelemetry;
                NetworkTelemetrySettings net = settings.NetworkTelemetry;
                Expect(settings.ShowFps && fps.DisplayMode == "Issues" && settings.ShowNet && net.DisplayMode == "Always", when + " retains independent telemetry display modes");
                Expect(settings.HudLayout.Spy["Fps"].X == -.23f && settings.HudLayout.Spy["Fps"].Y == .24f &&
                    settings.HudLayout.Spy["Network"].X == -.19f && settings.HudLayout.Spy["Network"].Y == .39f,
                    when + " retains independent telemetry positions in the HUD editor");
                Expect(fps.Scale == 135f && fps.Opacity == 42f && net.Scale == 85f && net.Opacity == 68f,
                    when + " retains independent telemetry appearance");
                Expect(fps.Graph == "Bars" && fps.GraphSeconds == 12f && fps.IssueHoldSeconds == 4.5f && fps.FrameTimeThresholdMs == 25f &&
                    net.Graph == "Line" && net.GraphSeconds == 17f && net.IssueHoldSeconds == 1.5f && net.PingThresholdMs == 95f &&
                    net.JitterThresholdMs == 18f && net.LossThresholdPercent == 0.5f, when + " retains telemetry graphs and issue thresholds");
            }
            ExpectEditedTelemetry(options.Editing, "control signals");
            Expect(original == SettingsStore.Current.ToJson(), "telemetry control signals modify only the draft");
            PickFps("max_fps", 137);
            Expect(options.Editing.FpsSpy == 137 && options.Editing.FpsMerc == 137, "custom FPS retains single-frame precision");
            PickFps("max_fps", 144);
            Expect(options.Editing.FpsSpy == 144 && options.Editing.FpsMerc == 144 &&
                ShownFps("fps_spy") == "144" && ShownFps("fps_merc") == "144", "shared FPS updates both role drafts and selectors");
            PickFps("fps_spy", 60);
            Expect(options.Editing.FpsMerc == 144 && ShownFps("max_fps") == "Per-role limits", "role FPS edit retains the other limit");
            PickFps("max_fps", DisplayFrameCap.Unlimited);
            Expect(options.Editing.FpsSpy == 0 && options.Editing.FpsMerc == 0, "Unlimited works after mixed role limits");
            PickFps("max_fps", DisplayFrameCap.Monitor);
            Expect(options.Editing.FpsSpy == DisplayFrameCap.Monitor && options.Editing.FpsMerc == DisplayFrameCap.Monitor, "Monitor works after rebuilding choices");
            PickFps("max_fps", 120);
            options.LoadLayout(PlaySide.Spy);
            var draft = options.Keys!.Draft!;
            string baseline = draft.Layout.ToIni();
            var breath = ControlActions.All.Single(a => a.Id == "breath");
            draft.Assign(breath, "X");
            draft.Assign(ControlActions.All.Single(a => a.Id == "voice"), "J");
            options.Editing.SoundVolume = 0.27f;
            Expect(writes == 0 && original == SettingsStore.Current.ToJson(), "edits do not write or apply");
            Expect(options.HasUnsavedChanges, "dirty after binding edit");
            options.Overlay = false;
            options.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            options.Overlay = true;
            var confirm = options.GetNodeOrNull<ConfirmBox>("OptionsConfirmation");
            Expect(confirm is not null, "front-end Escape confirms discard");
            options.GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            Expect(confirm?.IsQueuedForDeletion() == true, "Escape dismisses discard confirmation");
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
            GameSettings savedFps = GameSettings.FromJson(SettingsStore.Current.ToJson());
            Expect(savedFps.FpsSpy == 120 && savedFps.FpsMerc == 120 && ShownFps("max_fps") == "120", "shared FPS survives save and menu rebuild");
            ExpectEditedTelemetry(savedFps, "Apply serialization");
            ExpectEditedTelemetry(options.Editing, "Apply draft rebuild");
            Expect(((OptionButton)options.Controls["show_fps"]).Selected == 2 && ((OptionButton)options.Controls["show_net"]).Selected == 1 &&
                ((HSlider)options.Controls["fps_telemetry.opacity"]).Value == 42d && ((HSlider)options.Controls["network_telemetry.scale"]).Value == 85d,
                "Apply rebuild restores telemetry selectors and sliders");
            options.LoadLayout(PlaySide.Spy);
            Expect(options.Keys!.Draft!.Keys(breath).Contains("X"), "Apply retains draft binding");
            options.Defaults();
            Expect(options.HasUnsavedChanges, "reset stages changes");
            Expect(ShownFps("max_fps") == DisplayFrameCap.Label(new GameSettings().FpsSpy, SettingsApplier.MonitorHz), "reset restores shared FPS selector");
            Expect(!options.Editing.ShowFps && !options.Editing.ShowNet && options.Editing.FpsTelemetry.DisplayMode == "Always" &&
                options.Editing.NetworkTelemetry.DisplayMode == "Always", "reset disables both telemetry readouts and restores default display modes");
            foreach (string key in new[] { "fps_telemetry", "network_telemetry" })
            {
                Expect(((OptionButton)options.Controls[key + ".graph"]).Selected == 0 &&
                    ((HSlider)options.Controls[key + ".scale"]).Value == 100d && ((HSlider)options.Controls[key + ".opacity"]).Value == 100d &&
                    ((HSlider)options.Controls[key + ".graph_seconds"]).Value == 5d && ((HSlider)options.Controls[key + ".issue_hold_seconds"]).Value == 3d,
                    "reset restores " + key + " graph and appearance controls");
            }
            Expect(((OptionButton)options.Controls["show_fps"]).Selected == 0 && ((OptionButton)options.Controls["show_net"]).Selected == 0,
                "reset restores both telemetry visibility selectors");
            ExpectEditedTelemetry(SettingsStore.Current, "unapplied reset");
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
            input.ClearHeld(local);
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RB:perspective", true);
            input.FillTick(local);
            Expect(local.Commands.All(c => c.Name != "SetSpyFirstPerson" || c.Arg == 0), "merc ignores spy perspective action");
            input.ClearHeld(local);
            local.EndTick();
            input.Bindings = InputBindings.Load(PlaySide.Spy, seedInputMap: false, draft: true);
            behaviorSettings.SpyKbm.PerspectiveBehavior = "Hold";
            input.Key("X", true);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 1, "hold perspective keyboard press");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RB:perspective", true);
            input.Key("X", false);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 1, "controller keeps perspective held after keyboard release");
            local.EndTick();
            input.Key(ControlActions.VirtualPrefix + "RB:perspective", false);
            input.FillTick(local);
            Expect(local.Commands.Any(c => c.Name == "SetSpyFirstPerson" && c.Arg == 0) && !local.Commands.Any(c => c.Name == "SetSpyFirstPerson" && c.Arg == 1), "last perspective hold release restores third person");
            local.EndTick();
            behaviorSettings.SpyKbm.PerspectiveBehavior = "Toggle";
            input.Key("X", true);
            input.Key("X", true);
            input.Key("X", false);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 1, "perspective toggle survives release and ignores repeated keydown");
            local.EndTick();
            input.Key("X", true);
            input.FillTick(local);
            Expect(local.Commands.LastOrDefault(c => c.Name == "SetSpyFirstPerson").Arg == 0, "second perspective press restores third person");
            input.Key("X", false);
            input.Key("X", true);
            input.FillTick(local);
            input.ClearHeld(local);
            Expect(local.Commands.Any(c => c.Name == "SetSpyFirstPerson" && c.Arg == 0), "focus release clears perspective");
            input.Free();
            // Restore the isolated settings so the layout pass starts with real defaults.
            SettingsStore.Reset(GameSettings.FromJson(original));
        }
        finally { InputBindings.UserFileSaved -= Wrote; }
    }

    private void CheckHudEditor(OptionsMenu options)
    {
        var items = new List<OpenScctGame.Hud.Magma.MagmaDrawItem>
        {
            new() { Path = "Vie/label", Kind = OpenScctGame.Hud.Magma.MagmaWidgetKind.Text,
                Rect = new Rect2(10, 20, 30, 40), HeightPx = 12, Color = Colors.White },
            new() { Path = "Flash/fullscreen", Kind = OpenScctGame.Hud.Magma.MagmaWidgetKind.Text,
                Rect = new Rect2(0, 0, 100, 100), HeightPx = 12 }
        };
        var layout = new HudEditorSettings();
        var health = layout.Element(false, "Vie"); health.X = .1f; health.Y = .2f; health.Scale = 2; health.Opacity = .5f;
        OpenScctGame.Hud.HudEditorTransform.Apply(items, layout.Spy, new Vector2(1000, 500));
        Expect(items[0].Rect == new Rect2(110, 120, 60, 80) && items[0].HeightPx == 24 && items[0].Color.A == .5f, "HUD edit transforms text position, dimensions and opacity");
        Expect(items[1].Rect == new Rect2(0, 0, 100, 100), "HUD editor preserves gameplay screen effects");
        health.Visible = false;
        OpenScctGame.Hud.HudEditorTransform.Apply(items, layout.Spy, new Vector2(1000, 500));
        Expect(items.Count == 1 && items[0].Path == "Flash/fullscreen", "HUD visibility removes only its selected group");
        var assets = OpenScctGame.Hud.HudAssets.TryLoad(null, out _);
        if (assets is null) { Expect(false, "HUD editor preview assets available"); return; }
        CheckRadarAndLocationLayout(assets);
        var page = new OpenScctGame.Hud.SpyHudPage(assets);
        var sample = new OpenScctGame.Hud.HudSnapshot { Valid = true, HasPawn = true, Health = 100 };
        page.Update(sample, .1f); page.Draw(new Vector2(1280, 720), sample);
        Expect(page.DrawList.Any("Vie/"), "stock health is drawn");
        page.Options.Layout.Element(false, "Vie").Visible = false;
        page.Draw(new Vector2(1280, 720), sample);
        Expect(!page.DrawList.Any("Vie/"), "actual HUD page applies layout visibility");
        page.Options.Layout.Spy.Clear(); page.Draw(new Vector2(1280, 720), sample);
        Expect(page.DrawList.Any("Vie/"), "reset restores actual HUD without changing game visibility");
        foreach (bool merc in new[] { false, true })
        {
            OpenScctGame.Hud.HudPage rolePage = merc ? new OpenScctGame.Hud.MercHudPage(assets) : new OpenScctGame.Hud.SpyHudPage(assets);
            sample.Side = merc ? OpenScctGame.Hud.HudSide.Merc : OpenScctGame.Hud.HudSide.Spy;
            sample.ShowPlayers = true;
            sample.Players.Clear(); sample.Score.Clear();
            for (int n = 0; n < 6; n++) sample.Players.Add(new("Player " + n, n % 2, false, n, 3, 10));
            for (int n = 0; n < 6; n++) sample.Score.Add(new("Player " + n, n % 2, false, n, n, 0, 3, 10, n == 0));
            sample.Blips.Clear(); sample.Blips.Add(new(OpenScctGame.Hud.HudBlipKind.Teammate, new OpenScct.Core.FVector(100, 100, 0)));
            sample.Ballistic = .6f;
            sample.Tutorial = "Tutorial sample";
            sample.GameMessages.Clear(); sample.GameMessages.Add("Match in progress"); sample.GameMessages.Add("Queued message");
            Rect2? radar = null;
            for (int frame = 0; frame < 120; frame++)
            {
                HudEditorPreview.PreparePage(rolePage, sample);
                rolePage.Draw(new Vector2(1280, 720), sample);
                var bounds = OpenScctGame.Hud.HudEditorTransform.Bounds(rolePage.DrawList.Items);
                Expect(bounds.TryGetValue("Radar", out var current), "preview radar exists");
                if (radar is { } prior) Expect(current.IsEqualApprox(prior), "preview radar stays fixed across redraws");
                radar = current;
                var charge = rolePage.Area(merc ? "Ballistic/Ballistic" : "Ballistic");
                Expect(charge?.Frame == 6, "preview preserves the driven charge frame for both roles");
                Expect(rolePage.DrawList.TextOf("GameMessage/messageGame") == "Match in progress", "preview shows sample game message alongside tutorial, not placeholder or queue");
                Expect(rolePage.Area("TutoMessage/Tuto")?.Frame == 1, "preview preserves held-open tutorial frame");
            }
            foreach (float value in new[] { .2f, .8f })
            {
                sample.Ballistic = value;
                HudEditorPreview.PreparePage(rolePage, sample);
                Expect(rolePage.Area(merc ? "Ballistic/Ballistic" : "Ballistic")?.Frame == (int)(10 * value), "preview charge follows sample rather than fixed display frame");
            }
            Expect(rolePage.DrawList.Items.Any(i => i.Path.StartsWith("Extras/Split")), "sample exercises split scoreboard");
            rolePage.Options.Layout.Element(merc, "Scoreboard").Visible = false;
            rolePage.Draw(new Vector2(1280, 720), sample);
            Expect(!rolePage.DrawList.Items.Any(i => OpenScctGame.Hud.HudEditorTransform.Group(i) == "Scoreboard"), "scoreboard hides both teams, backgrounds and ping columns");
        }
        var chatSetting = new HudElementSettings { X = .2f, Y = -.1f, Scale = 1.4f, Opacity = .5f };
        var chat = new OpenScctGame.Chat.ChatPanel { PreviewScreen = new Vector2(1280, 720), Clock = () => 1, LayoutOverride = () => chatSetting };
        chat.Transcript.Add(OpenScctGame.Chat.ChatChannel.All, "Sample chat", "Player", 1, 0, 0);
        options.AddChild(chat);
        chat._Process(0);
        var recent = chat.GetNode<Control>("Recent");
        var expected = OpenScctGame.Hud.HudEditorTransform.Transform(chat.PassiveBaseBounds, chatSetting, new Vector2(1280, 720));
        Expect(recent.Position.IsEqualApprox(expected.Position) && recent.Scale.IsEqualApprox(Vector2.One * 1.4f) && chat.Modulate.A == .5f, "native chat follows HUD position, scale and opacity");
        chatSetting.Visible = false; chat._Process(0); Expect(!chat.Visible, "passive chat can be hidden");
        chat.Expanded = true; chat._Process(0); Expect(chat.Visible, "explicitly opened composer remains usable");
        options.RemoveChild(chat); chat.QueueFree();
        var feed = new OpenScctGame.Hud.HudActivityFeedDrawer(); feed.Configure(assets);
        var events = new[] { new OpenScctGame.Hud.HudActivityEntry(1, OpenScctGame.Hud.HudActivityKind.Join, null, null, OpenScctGame.Hud.HudActivityCause.None, "JOIN", "Player joined", 0, false) };
        feed.Draw(new Vector2(1280, 720), events, 1, null);
        Expect(feed.DrawList.Items.Count > 0, "event feed sample renders");
        layout.Element(false, "Activity").Visible = false;
        OpenScctGame.Hud.HudEditorTransform.Apply(feed.DrawList.Items, layout.Spy, new Vector2(1280, 720));
        Expect(feed.DrawList.Items.Count == 0, "event feed visibility covers all row graphics");

    }

    private void CheckRadarAndLocationLayout(OpenScctGame.Hud.HudAssets assets)
    {
        foreach (bool merc in new[] { false, true })
        foreach (Vector2 screen in new[] { new Vector2(800, 600), new Vector2(1280, 720), new Vector2(3440, 1440) })
        {
            OpenScctGame.Hud.HudPage stock = merc ? new OpenScctGame.Hud.MercHudPage(assets) : new OpenScctGame.Hud.SpyHudPage(assets);
            OpenScctGame.Hud.HudPage edited = merc ? new OpenScctGame.Hud.MercHudPage(assets) : new OpenScctGame.Hud.SpyHudPage(assets);
            var sample = new OpenScctGame.Hud.HudSnapshot
            {
                Valid = true, HasPawn = true, Health = 100,
                Side = merc ? OpenScctGame.Hud.HudSide.Merc : OpenScctGame.Hud.HudSide.Spy,
            };
            sample.Blips.Add(new(OpenScctGame.Hud.HudBlipKind.Teammate, new OpenScct.Core.FVector(100, 100, 0)));
            var setting = edited.Options.Layout.Element(merc, "Radar");
            // Exercise scaling in the interior and translation clamped at the screen edge. Both previously
            // drifted as the rotating radar ring changed the group's axis-aligned bounds.
            foreach (bool edge in new[] { false, true })
            {
                setting.Scale = edge ? 1f : 1.6f;
                setting.X = edge ? -.9f : merc ? -.2f : .2f;
                setting.Y = edge ? -.9f : .2f;
                Vector2? firstCentre = null, firstRingPoint = null;
                bool ringAnimated = false;
                for (int frame = 0; frame < 120; frame++)
                {
                    sample.Yaw = frame * 317;
                    stock.Update(sample, .05f); edited.Update(sample, .05f);
                    stock.Draw(screen, sample); edited.Draw(screen, sample);
                    var original = stock.DrawList.Find("Radar/fond/Element2");
                    var moved = edited.DrawList.Find("Radar/fond/Element2");
                    var ring = edited.DrawList.Find("Radar/fond/Element3");
                    Expect(original is not null && moved is not null && ring is not null, "live radar frame and animation render for both roles");
                    if (original is null || moved is null || ring is null) break;
                    Rect2 basis = stock.LayoutBounds(screen)["Radar"];
                    Rect2 placed = OpenScctGame.Hud.HudEditorTransform.Transform(basis, setting, screen);
                    Vector2 expected = placed.Position + (Centre(original) - basis.Position) * setting.Scale;
                    Vector2 actual = Centre(moved);
                    Expect(actual.DistanceTo(expected) < .01f, "live radar uses the same fixed layout frame as the editor at every size");
                    if (firstCentre is { } prior) Expect(actual.DistanceTo(prior) < .01f, "moved radar stays still while its ring, yaw and blips animate");
                    else firstCentre = actual;
                    if (firstRingPoint is { } point) ringAnimated |= ring.Points[0].DistanceTo(point) > 1f;
                    else firstRingPoint = ring.Points[0];
                }
                Expect(ringAnimated, "HUD placement preserves the live radar ring animation");
            }

            var location = new OpenScctGame.Hud.HudLocation();
            location.Draw(screen, "Warehouse", true, stock);
            var label = location.DrawList.Find("Location");
            Expect(label is not null && label.Rect.Size.X < screen.X / 2f, "zone name has selectable text bounds rather than a screen-wide box");
            if (label is not null)
            {
                Rect2 before = label.Rect;
                var layout = new HudEditorSettings();
                var zone = layout.Element(merc, "Location");
                zone.X = merc ? -.15f : .15f; zone.Y = .15f;
                OpenScctGame.Hud.HudEditorTransform.Apply(location.DrawList.Items, layout.For(merc), screen);
                Expect(label.Rect.Position.DistanceTo(before.Position + new Vector2(zone.X, zone.Y) * screen) < .01f,
                    "zone name can move horizontally and vertically independently of the radar");
                Expect(layout.Clone().For(merc).ContainsKey("Location"), "zone name placement survives layout persistence");
            }
        }

        static Vector2 Centre(OpenScctGame.Hud.Magma.MagmaDrawItem item)
        {
            Vector2 sum = Vector2.Zero;
            foreach (Vector2 point in item.Points) sum += point;
            return sum / item.Points.Length;
        }
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
