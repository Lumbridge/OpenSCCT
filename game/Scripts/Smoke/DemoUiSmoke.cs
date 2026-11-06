using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Play.Demo;
using OpenScctGame.UI;
using LibraryEntry = OpenScctGame.Play.Demo.DemoEntry;
using Playback = OpenScctGame.Play.Demo.DemoPlayer;
using RecentEntry = OpenScct.Net.Discovery.DemoEntry;

namespace OpenScctGame.Smoke;

/// <summary>Demo browsing and playback controls with isolated files and metadata, at small through 4K sizes.</summary>
public sealed class DemoUiSmoke : SmokeTest
{
    public const string TestName = "demo-ui";
    public override string Name => TestName;

    private const int LibraryCases = 10;
    private const int PlaybackCases = 9;
    private const int SettleFrames = 12;
    private static readonly Vector2I[] Sizes =
    [
        new(800, 600), new(1280, 720), new(1920, 1080), new(3840, 2160), new(800, 600),
    ];
    private readonly List<string> _problems = [];
    private DemosScreen? _library;
    private DemoHud? _hud;
    private Playback? _player;
    private string? _demoPath;
    private int _step;
    private int _frames;
    private bool _playback;
    private float _closedBarHeight;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (_playback) return StepPlayback(context);
            if (_library is null)
            {
                PresentLibrary(context);
                return SmokeResult.Running;
            }
            if (++_frames < SettleFrames) return SmokeResult.Running;
            _frames = 0;
            CheckLibrary(context);
            if (++_step < Sizes.Length * LibraryCases)
            {
                PresentLibrary(context);
                return SmokeResult.Running;
            }
            _playback = true;
            _step = 0;
            PresentPlayback(context);
            return SmokeResult.Running;
        }
        catch (Exception error)
        {
            return Finish(SmokeResult.Fail(error.ToString()));
        }
    }

    private void PresentLibrary(SmokeContext context)
    {
        int state = _step % LibraryCases;
        Resize(context, Sizes[_step / LibraryCases]);
        if (state is 0 or 8 or 9)
        {
            _library = FrontEnd.Show(context.Tree, new DemosScreen
            {
                PreviewLocal = state >= 8 ? [] : LocalSamples(),
                PreviewRecent = state == 9 ? new DemoListResponse() : RecentSamples(),
            });
        }
        LineEdit search = Find<LineEdit>(_library!, "DemoSearch");
        OptionButton mode = Find<OptionButton>(_library!, "DemoMode");
        if (_step / LibraryCases == Sizes.Length - 1 && state == 1)
            _library!.ShowError("The recording could not be opened. Select another demo.");
        search.Text = state switch { 1 => "specialist", 3 => "nothing matches this query", _ => string.Empty };
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        int selection = state is 2 or 5 ? 2 : 0;
        mode.Select(selection);
        mode.EmitSignal(OptionButton.SignalName.ItemSelected, (long)selection);
        Press(_library!, state is 4 or 5 or 9 ? "RecentTab" : "LocalTab");
        OptionButton storage = Find<OptionButton>(_library!, "DemoStorage");
        int location = state == 6 ? 2 : state == 7 ? 1 : 0;
        storage.Select(location);
        storage.EmitSignal(OptionButton.SignalName.ItemSelected, (long)location);
    }

    private void CheckLibrary(SmokeContext context)
    {
        int state = _step % LibraryCases;
        string name = $"demos-{Sizes[_step / LibraryCases].X}x{Sizes[_step / LibraryCases].Y}-{state}";
        CheckLayout(_library!, context, name);
        Control[] cards = Find<GridContainer>(_library!, "DemoCards").GetChildren().OfType<Control>()
            .Where(card => card.FindChild("Title", true, false) is Label).ToArray();
        int expected = state switch { 0 or 4 => 3, 1 or 2 or 5 or 6 => 1, 7 => 2, _ => 0 };
        Expect(cards.Length == expected, $"{name}: expected {expected} demos, got {cards.Length}");
        if (state is 0 or 4)
        {
            Button[] watch = cards.Select(card => Find<Button>(card, "Watch")).ToArray();
            Expect(watch.Count(button => button.Disabled) == 1, name + ": incompatible demo should disable only its Watch action");
            Expect(cards.Any(card => Find<Label>(card, "Title").Text.Contains("Aquarius", StringComparison.Ordinal)),
                name + ": map title missing");
        }
        if (state == 0)
            Expect(cards.Count(card => card.FindChild("Keep", true, false) is Button) == 1,
                name + ": only the downloaded demo needs a Keep action");
        if (state == 1)
        {
            Expect(cards.Length == 1 && Find<Label>(cards[0], "Title").Text.Contains("Aquarius", StringComparison.Ordinal),
                name + ": player-name search did not find the matching recording");
            if (_step / LibraryCases == Sizes.Length - 1)
            {
                Expect(Find<Label>(_library!, "Status").Text == "The recording could not be opened. Select another demo.",
                    name + ": changing filters must not discard a playback error");
                Expect(Find<Control>(_library!, "DemoStatusPanel").Visible, name + ": playback error status must remain visible");
            }
        }
        if (state is 2 or 5)
            Expect(cards.Length == 1 && Find<Label>(cards[0], "Title").Text.Contains("Warehouse", StringComparison.Ordinal),
                name + ": Disk Hunt filter did not select its recording");
        if (state == 6)
            Expect(cards.Length == 1 && cards[0].FindChild("Keep", true, false) is Button,
                name + ": downloads filter must show the cached recording");
        if (state == 7)
            Expect(cards.All(card => card.FindChild("Keep", true, false) is null),
                name + ": saved filter must exclude cached recordings");
        if (expected == 0)
        {
            Expect(_library!.FindChild("EmptyDemos", true, false) is Control { Visible: true }, name + ": empty-state guidance is missing");
            Expect(!string.IsNullOrWhiteSpace(Find<Label>(_library!, "EmptyMessage").Text), name + ": empty-state guidance has no text");
        }
        foreach (Button button in _library!.PageButtons)
            Expect(new Rect2(Vector2.Zero, context.Tree.Root.GetVisibleRect().Size).Encloses(button.GetGlobalRect()),
                name + ": footer action is outside the viewport: " + button.Name);
        MenuPage.Shot(_library, name);
        if (_step / LibraryCases == Sizes.Length - 1 && state == 1) _library.ShowError(string.Empty);
        if (_step == 0)
        {
            bool original = OnlineSettings.Current.RecordDemos;
            CheckBox recording = Find<CheckBox>(_library, "DemoRecording");
            recording.ButtonPressed = !original;
            Expect(OnlineSettings.Current.RecordDemos == !original, "library recording preference must update the personal setting");
            recording.ButtonPressed = original;
            Expect(OnlineSettings.Current.RecordDemos == original, "recording preference can be switched back");
        }
    }

    private static IReadOnlyList<(LibraryEntry Entry, bool Cached)> LocalSamples()
    {
        ulong protocol = OpenScct.Server.NetClasses.Create().ComputeProtocolHash();
        return [(LocalSample("Aquarius training grounds with a deliberately long map title", 0, protocol, "Stealth Specialist"), false),
            (LocalSample("Warehouse", 4, protocol, "Guard"), true),
            (LocalSample("Old map", 6, protocol ^ 1, "Veteran"), false)];
    }

    private static LibraryEntry LocalSample(string map, int mode, ulong protocol, string player)
    {
        DemoHeader header = Header(map, mode, protocol);
        var index = new DemoIndex { FirstTick = 100, LastTick = 100 + 7200 };
        index.Players.Add(new OpenScct.Net.DemoPlayer(1, player, 0));
        index.Players.Add(new OpenScct.Net.DemoPlayer(2, "A guard with a deliberately long player name", 1));
        return new LibraryEntry(Path.Combine(Path.GetTempPath(), map + DemoFile.Extension), header, index, 1234567);
    }

    private static DemoListResponse RecentSamples()
    {
        string protocol = MasterApi.Hex(OpenScct.Server.NetClasses.Create().ComputeProtocolHash());
        RecentEntry Sample(long id, string map, int mode, string version) => new()
        {
            Id = id, Map = map, Mode = mode,
            ServerName = "Community server with a deliberately long descriptive name",
            GameVersion = "0.8.0", NetVersion = version, Size = 2345678, DurationSeconds = 125,
            Time = DateTimeOffset.UtcNow.AddHours(-1), Expires = DateTimeOffset.UtcNow.AddDays(6),
            Players = [new() { Name = "Stealth Specialist", Team = 0 }, new() { Name = "A guard with a deliberately long player name", Team = 1 }],
        };
        return new DemoListResponse
        {
            Demos = [Sample(1, "Aquarius training grounds with a deliberately long map title", 0, protocol),
                Sample(2, "Warehouse", 4, protocol), Sample(3, "Old map", 6, "incompatible")],
            Total = 3, PageSize = 20,
        };
    }

    private static DemoHeader Header(string map, int mode, ulong protocol) => new()
    {
        Map = map, GameOptions = "?GameMode=" + mode, ProtocolHash = protocol, GameVersion = "0.8.0",
        ServerName = "Community server with a deliberately long descriptive name",
        StartUnixMs = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(), TickRate = 60,
    };

    private SmokeResult StepPlayback(SmokeContext context)
    {
        if (++_frames < SettleFrames) return SmokeResult.Running;
        _frames = 0;
        if (_step >= Sizes.Length * PlaybackCases)
        {
            string resized = "demo-playback-live-" + (_step == Sizes.Length * PlaybackCases ? "4k" : "800x600");
            CheckLayout(_hud!, context, resized);
            MenuPage.Shot(_hud!, resized);
            if (_step++ == Sizes.Length * PlaybackCases)
            {
                Resize(context, Sizes[0]);
                return SmokeResult.Running;
            }
            Press(_hud!, "LeaveDemo");
            Find<ConfirmBox>(_hud!, "LeaveDemoConfirmation").Answer(true);
            Expect(context.Tree.CurrentScene is DemosScreen, "confirming exit should return to the demo library");
            return Finish(_problems.Count == 0
                ? SmokeResult.Pass("50 library and 47 playback states from 800x600 to 4K and back; filters, pause/speed/camera, X-ray toggle, marker seeking, compact controls, mouse/keyboard restore and exit confirmation")
                : SmokeResult.Fail(string.Join("; ", _problems.Take(18))));
        }
        int state = _step % PlaybackCases;
        string name = $"demo-playback-{Sizes[_step / PlaybackCases].X}x{Sizes[_step / PlaybackCases].Y}-{state}";
        CheckLayout(_hud!, context, name);
        string expected = state switch { 0 or 5 or 7 => "PLAYING", 3 => "SEEKING", 8 => "FINISHED", _ => "PAUSED" };
        Expect(Find<Label>(_hud!, "PlaybackState").Text == expected, name + ": playback state should be " + expected);
        string action = state == 8 ? "Replay" : state is 0 or 5 or 7 ? "Pause" : "Play";
        Expect(Find<Button>(_hud!, "PlayPause").Text == action, name + ": incorrect play/pause action");
        Expect(Find<PanelContainer>(_hud!, "PlaybackBar").Visible == (state != 4), name + ": explicit hide state was lost");
        Expect(Find<Button>(_hud!, "ShowControls").Visible == (state == 4), name + ": hidden controls need a visible restore button");
        Expect(Find<Label>(_hud!, "PlaybackHelp").Visible == (state == 2), name + ": controls help visibility is incorrect");
        Expect(Find<OptionButton>(_hud!, "WatchPlayer").Disabled, name + ": an empty live roster should not offer player selection");
        bool xrayEnabled = state is >= 1 and <= 4;
        Expect(Find<CheckBox>(_hud!, "DemoXRay").ButtonPressed == xrayEnabled && _player!.XRay == xrayEnabled,
            name + ": X-ray setting should match the checkbox after camera, seek and visibility changes");
        if (state == 1)
        {
            _closedBarHeight = Find<PanelContainer>(_hud!, "PlaybackBar").Size.Y;
            Expect(_closedBarHeight <= 190f, name + ": collapsed controls should stay compact");
            Expect(Find<PanelContainer>(_hud!, "PlaybackBar").Size.X <= 881f, name + ": playback dock is too wide");
            if (_step == 1)
            {
                using Playback restarted = _player!.Restart(_player.FirstTick);
                Expect(restarted.XRay, name + ": rewinding should preserve X-ray");
            }
        }
        if (state == 2)
        {
            Expect(_player!.Mode == DemoCameraMode.Free, name + ": camera selector did not change perspective");
            Expect(Find<Label>(_hud!, "CameraHint").Text.Contains("WASD", StringComparison.Ordinal), name + ": free-camera movement hint is missing");
            Expect(Find<PanelContainer>(_hud!, "PlaybackBar").Size.Y > _closedBarHeight + 1f,
                name + ": opening controls help must expand the playback dock");
        }
        if (state == 3)
        {
            Expect(Find<Label>(_hud!, "SeekingStatus").Visible && _player!.SeekTarget == _player.FirstTick + 3600 - 180,
                name + ": marker selection should seek with a three-second lead-in");
            Expect(Find<PanelContainer>(_hud!, "PlaybackBar").Size.Y <= _closedBarHeight + 1f,
                name + ": closing controls help must shrink the playback dock");
        }
        if (state == 6)
            Expect(Find<ConfirmBox>(_hud!, "LeaveDemoConfirmation").Visible, name + ": Escape must ask before exiting");
        if (state == 7)
            Expect(_hud!.FindChild("LeaveDemoConfirmation", true, false) is null, name + ": Escape should cancel the exit confirmation");
        if (state == 8)
            Expect(Find<Label>(_hud!, "PlaybackTime").Text == "2:00 / 2:00", name + ": finished time is incorrect");
        MenuPage.Shot(_hud!, name);
        if (++_step < Sizes.Length * PlaybackCases)
        {
            PresentPlayback(context);
            return SmokeResult.Running;
        }
        Resize(context, Sizes[3]);
        return SmokeResult.Running;
    }

    private void PresentPlayback(SmokeContext context)
    {
        int state = _step % PlaybackCases;
        Resize(context, Sizes[_step / PlaybackCases]);
        if (_demoPath is null)
        {
            _demoPath = Path.Combine(Path.GetTempPath(), "openscct-demo-ui-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
            using var writer = new DemoWriter(_demoPath, Header("Warehouse", 4, OpenScct.Server.NetClasses.Create().ComputeProtocolHash()));
            writer.Index.FirstTick = 100;
            writer.Index.LastTick = 7300;
            writer.NotePlayer(1, "Stealth Specialist", 0);
            writer.NotePlayer(2, "Guard", 1);
            writer.Mark(DemoMarkerKind.RoundStart, "Round started", 100);
            writer.Mark(DemoMarkerKind.Kill, "Guard defeated Stealth Specialist", 3700);
            writer.Mark(DemoMarkerKind.Objective, "Terminal captured", 5500);
            writer.Mark(DemoMarkerKind.RoundEnd, "Round ended", 7300);
            writer.Finish();
        }
        if (state == 0)
        {
            _player?.Dispose();
            _player = Playback.Open(_demoPath);
            // This layout-only fixture has no client session; consume its empty initial frame explicitly.
            _player.BeforeTick();
            var stage = new Control { Name = "DemoPreview" };
            stage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var background = new ColorRect { Color = new Color(0.035f, 0.055f, 0.045f), MouseFilter = Control.MouseFilterEnum.Ignore };
            background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            stage.AddChild(background);
            _hud = new DemoHud(_player);
            stage.AddChild(_hud);
            FrontEnd.Show(context.Tree, stage);
            CheckPlaybackActions();
        }
        else if (state == 1)
        {
            Press(_hud!, "PlayPause");
            Find<CheckBox>(_hud!, "DemoXRay").ButtonPressed = true;
        }
        else if (state == 2)
        {
            OptionButton camera = Find<OptionButton>(_hud!, "CameraMode");
            camera.Select(2);
            camera.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            Press(_hud!, "ControlsHelp");
        }
        else if (state == 3)
        {
            Press(_hud!, "ControlsHelp");
            DemoSeekBar bar = Find<DemoSeekBar>(_hud!, "Timeline");
            Vector2 point = new(bar.Size.X * 0.5f, bar.Size.Y * 0.5f);
            bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = true });
            Expect(bar.Dragging && _player!.SeekTarget == 0, "timeline should preview while dragging without starting repeated seeks");
            bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, Pressed = false });
            Expect(!bar.Dragging && bar.TooltipText.Contains("Death", StringComparison.Ordinal), "timeline marker preview should identify the event");
            uint selected = _player!.SeekTarget;
            bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point * 0.5f, Pressed = true });
            bar._Notification((int)Control.NotificationWMWindowFocusOut);
            bar._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point * 0.5f, Pressed = false });
            Expect(!bar.Dragging && _player.SeekTarget == selected, "losing focus should cancel a timeline drag without seeking");
            bar.EmitSignal(Control.SignalName.MouseExited);
        }
        else if (state == 4)
        {
            _player!.SeekTarget = 0;
            Press(_hud!, "HideControls");
        }
        else if (state == 5)
        {
            Press(_hud!, "ShowControls");
            Expect(Find<PanelContainer>(_hud!, "PlaybackBar").Visible, "Show controls should reopen the hidden panel with the mouse");
            KeyPress(Key.H);
            Expect(Find<Button>(_hud!, "ShowControls").Visible, "H should hide the panel and leave a restore action");
            KeyPress(Key.H);
            Find<CheckBox>(_hud!, "DemoXRay").ButtonPressed = false;
            _player!.Clock.Paused = false;
        }
        else if (state == 6)
        {
            Node scene = context.Tree.CurrentScene;
            KeyPress(Key.Escape);
            Expect(context.Tree.CurrentScene == scene && _player!.Clock.Paused, "Escape should pause and ask without leaving the demo");
            DemoCameraMode camera = _player!.Mode;
            KeyPress(Key.C);
            KeyPress(Key.Space);
            Expect(_player.Mode == camera && _player.Clock.Paused, "confirmation must block playback and camera shortcuts");
        }
        else if (state == 7)
        {
            Find<ConfirmBox>(_hud!, "LeaveDemoConfirmation")._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            Expect(!_player!.Clock.Paused, "canceling exit should restore the previous playback state");
            KeyPress(Key.Escape);
            Expect(!_player.Clock.Paused, "a second Escape in the cancellation frame must not pause playback without a dialog");
        }
        else
        {
            while (_player!.CurrentTick < _player.LastTick) _player.BeforeTick();
        }
    }

    private void CheckPlaybackActions()
    {
        OptionButton speed = Find<OptionButton>(_hud!, "PlaybackSpeed");
        speed.Select(0);
        speed.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        Expect(_player!.Clock.Speed == 0.25, "speed selector must change the playback clock");
        KeyPress(Key.Down);
        Expect(_player.Clock.Speed == 0.25, "speed shortcut should clamp at quarter speed");
        for (int i = 0; i < 6; i++) KeyPress(Key.Up);
        Expect(_player.Clock.Speed == 4, "speed shortcut should clamp at quadruple speed");
        speed.Select(2);
        speed.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        KeyPress(Key.Space);
        Expect(_player.Clock.Paused, "Space should pause playback");
        KeyPress(Key.Space);
        Expect(!_player.Clock.Paused, "Space should resume playback");
        KeyPress(Key.C);
        Expect(_player.Mode == DemoCameraMode.Free, "camera shortcut should cycle to free camera");
        KeyPress(Key.C);
        Expect(_player.Mode == DemoCameraMode.AsPlayed, "camera shortcut should cycle to As played");
        KeyPress(Key.C);
        Expect(_player.Mode == DemoCameraMode.FirstPerson, "camera shortcut should wrap back to first person");
        OptionButton camera = Find<OptionButton>(_hud!, "CameraMode");
        long asPlayed = (long)DemoCameraMode.AsPlayed;
        Expect(camera.GetItemText((int)asPlayed) == "As played", "As played should appear in the camera selector");
        camera.EmitSignal(OptionButton.SignalName.ItemSelected, asPlayed);
        Expect(_player.Mode == DemoCameraMode.AsPlayed, "camera selector should switch to As played");
        _hud!._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Expect(!OpenScctGame.Controls.Pointer.CaptureRequested, "As played should leave the pointer free");
        _hud._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        _player.Mode = DemoCameraMode.ThirdPerson;
        Press(_hud!, "ForwardFive");
        Expect(_player.SeekTarget == _player.FirstTick + 300, "forward button should seek five seconds");
        _player.SeekTarget = 0;
        Expect(DemoHud.Clock(216000, 60) == "1:00:00", "long demos should show hours in the playback clock");
    }

    private void KeyPress(Key key) => _hud!._Input(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });

    private void CheckLayout(Node page, SmokeContext context, string name)
    {
        Vector2 viewport = context.Tree.Root.GetVisibleRect().Size;
        _problems.AddRange(LayoutCheck.Problems(page, viewport.X).Select(problem => name + ": " + problem));
        _problems.AddRange(LayoutCheck.BoundsProblems(page, viewport).Select(problem => name + ": " + problem));
    }

    private void Expect(bool value, string message) { if (!value) _problems.Add(message); }

    private static T Find<T>(Node node, string name) where T : Node =>
        node.FindChild(name, true, false) as T ?? throw new InvalidOperationException("Missing " + name);

    private static void Press(Node node, string name) => Find<Button>(node, name).EmitSignal(BaseButton.SignalName.Pressed);

    private static void Resize(SmokeContext context, Vector2I size)
    {
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(size);
        }
        context.Tree.Root.Size = size;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _hud?.QueueFree();
        _player?.Dispose();
        if (_demoPath is not null && File.Exists(_demoPath)) File.Delete(_demoPath);
        return result;
    }
}
