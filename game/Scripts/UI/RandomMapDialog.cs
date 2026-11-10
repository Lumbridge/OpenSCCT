using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Server;
using OpenScctGame.Audio;

namespace OpenScctGame.UI;

/// <summary>A host-only map draw. The reel reveals the result, and Use map commits it through the lobby.</summary>
public partial class RandomMapDialog : Control
{
    private const float CardWidth = 210, CardHeight = 148, Step = 222;
    private const int StartIndex = 4, WinnerIndex = 30;
    private static readonly Color ReelBackground = new("0b1210");
    private static readonly Color Gold = new("ffd879");
    private static readonly Color[] CardAccents = [new("6e9b70"), new("68a6ae"), new("9c9261"), new("8a807c")];

    private readonly MapCatalog? _catalog;
    private readonly (MapPlaylist Playlist, (string Id, string Title)[] Maps)[] _playlists;
    private readonly string _currentMap;
    private readonly string? _preferredPlaylist;
    private readonly Control _returnFocus;
    private readonly Action<string> _useMap;
    private readonly Dictionary<Control, FocusModeEnum> _backgroundFocus = new();
    private PanelContainer _panel = null!;
    private OptionButton _playlist = null!;
    private Control _reel = null!, _track = null!, _reticle = null!;
    private Label _status = null!, _result = null!;
    private Button _roll = null!, _use = null!, _close = null!;
    private AudioStreamPlayer? _tick, _lock;
    private Tween? _animation;
    private string? _winner;
    private float _offset;
    private int _lastTick;
    private bool _rolling;

    public RandomMapDialog(MapCatalog? catalog, IReadOnlyList<(string Id, string Title)> maps, string currentMap,
        string? preferredPlaylist, Control returnFocus, Action<string> useMap)
    {
        Name = "RandomMapDialog";
        _catalog = catalog;
        _currentMap = currentMap;
        _preferredPlaylist = preferredPlaylist;
        _returnFocus = returnFocus;
        _useMap = useMap;

        var available = maps.DistinctBy(map => map.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var all = new MapPlaylist(MapCatalog.AllMapsPlaylist, "Every map available to this lobby.", default, [], []);
        var filters = catalog?.GetMapFilters() ?? [];
        var playlists = catalog?.GetPlaylists() ?? [];
        _playlists = filters.Prepend(all).Concat(playlists).DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p, p.Name == MapCatalog.AllMapsPlaylist ? available :
                available.Where(map => p.Maps.Contains(map.Id, StringComparer.OrdinalIgnoreCase)).ToArray()))
            .ToArray();
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = UiStyle.MenuTheme;
        foreach (Control control in GetParent().FindChildren("*", nameof(Control), true, false).OfType<Control>())
            if (control != this && !IsAncestorOf(control) && control.FocusMode != FocusModeEnum.None)
            {
                _backgroundFocus[control] = control.FocusMode;
                control.FocusMode = FocusModeEnum.None;
            }

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.78f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "RandomMapPanel" };
        _panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        AddChild(_panel);
        var margin = new MarginContainer();
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 10);
        _panel.AddChild(margin);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        margin.AddChild(body);

        var title = new Label { Text = "Random map", Modulate = UiStyle.MenuFocus };
        title.AddThemeFontSizeOverride("font_size", 26);
        body.AddChild(title);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        body.AddChild(row);
        row.AddChild(new Label { Text = "Playlist" });
        _playlist = new OptionButton
        {
            Name = "RandomMapPlaylist", CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        row.AddChild(_playlist);
        int selected = 0;
        foreach (var (playlist, maps) in _playlists)
        {
            int index = _playlist.ItemCount;
            _playlist.AddItem($"{MapPickerView.PlaylistTitle(playlist)}  ·  {maps.Length}");
            _playlist.SetItemDisabled(index, maps.Length == 0);
            _playlist.GetPopup().SetItemTooltip(index, playlist.Description);
            if (maps.Length > 0 && string.Equals(playlist.Name, _preferredPlaylist, StringComparison.OrdinalIgnoreCase))
                selected = index;
        }
        _playlist.Select(selected);
        _playlist.ItemSelected += _ => PreviewPlaylist();

        _status = new Label { Name = "ReelStatus", Modulate = UiStyle.MenuFocus };
        _status.AddThemeFontSizeOverride("font_size", 13);
        body.AddChild(_status);
        BuildReel(body);
        _result = new Label
        {
            Name = "RandomMapResult", ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(0, 25),
        };
        body.AddChild(_result);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        body.AddChild(buttons);
        _close = LobbyPanels.Button("Close", Close, "CloseRandomMap");
        _roll = LobbyPanels.Button("Spin", Roll, "SpinMap");
        _use = LobbyPanels.Button("Use map", Accept, "UseRandomMap");
        _use.AddThemeColorOverride("font_color", Gold);
        foreach (var button in new[] { _close, _roll, _use })
        {
            button.CustomMinimumSize = new Vector2(100, 40);
            buttons.AddChild(button);
        }

        if (DisplayServer.GetName() != "headless" && System.Environment.GetEnvironmentVariable("SVM_AUDIO_MUTE") is not ("1" or "true"))
        {
            _tick = AddSound(MenuFeedback.Tone(0.035, 1200, 760), -23);
            _lock = AddSound(MenuFeedback.Tone(0.29, 420, 680), -17);
        }
        Resized += FitPanel;
        FitPanel();
        PreviewPlaylist();
        _playlist.GrabFocus();
    }

    private void FitPanel() => LobbyPanels.FitDialog(_panel, Size, new Vector2(1000, 430));

    private void BuildReel(Container parent)
    {
        _reel = new ColorRect
        {
            Name = "MapReel", Color = ReelBackground, ClipContents = true, MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 166), SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        parent.AddChild(_reel);
        _track = new Control { Name = "ReelTrack", MouseFilter = MouseFilterEnum.Ignore };
        _reel.AddChild(_track);
        _reticle = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _reel.AddChild(_reticle);
        _reticle.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _reticle.Draw += DrawReticle;
        _reticle.Resized += _reticle.QueueRedraw;
        _reel.Resized += PositionTrack;
    }

    private void DrawReticle()
    {
        float width = _reticle.Size.X, height = _reticle.Size.Y, x = width * 0.5f;
        float shade = width * 0.2f;
        Color edge = ReelBackground with { A = 0.97f };
        Color clear = ReelBackground with { A = 0 };
        _reticle.DrawPolygon([new(0, 0), new(shade, 0), new(shade, height), new(0, height)], [edge, clear, clear, edge]);
        _reticle.DrawPolygon([new(width - shade, 0), new(width, 0), new(width, height), new(width - shade, height)], [clear, edge, edge, clear]);
        Color accent = _winner is not null ? Gold : UiStyle.MenuFocus;
        _reticle.DrawRect(new Rect2(0, 0, width, height), accent with { A = 0.35f }, false);
        if (_winner is null)
        {
            _reticle.DrawLine(new Vector2(x, 0), new Vector2(x, height), accent with { A = 0.24f }, 8);
            _reticle.DrawLine(new Vector2(x, 0), new Vector2(x, height), accent with { A = 0.78f }, 2);
        }
        _reticle.DrawColoredPolygon([new(x - 9, 0), new(x + 9, 0), new(x, 14)], accent);
        _reticle.DrawColoredPolygon([new(x - 9, height), new(x + 9, height), new(x, height - 14)], accent);
    }

    private (string Id, string Title)[] SelectedMaps => _playlists[_playlist.Selected].Maps;

    private void PreviewPlaylist()
    {
        _animation?.Kill();
        _winner = null;
        var maps = SelectedMaps;
        _status.Text = maps.Length == 0 ? "PLAYLIST EMPTY" : "READY TO SPIN";
        _result.Text = maps.Length == 0 ? "This playlist has no maps available to the lobby." : "Spin to draw a map from this playlist.";
        _result.Modulate = UiStyle.MenuText;
        _use.Disabled = true;
        _roll.Disabled = maps.Length == 0;
        _roll.Text = "Spin";
        BuildTrack(maps, StartIndex * 2 + 1);
        _offset = StartIndex * Step;
        PositionTrack();
        _reticle.QueueRedraw();
    }

    private Panel? BuildTrack((string Id, string Title)[] maps, int count, int winnerMapIndex = -1)
    {
        foreach (Node oldCard in _track.GetChildren())
        {
            _track.RemoveChild(oldCard);
            oldCard.QueueFree();
        }
        if (maps.Length == 0) return null;

        Panel? winnerCard = null;
        int next = Random.Shared.Next(maps.Length);
        for (int i = 0; i < count; i++)
        {
            if (i == WinnerIndex - 1 && next == winnerMapIndex && maps.Length > 1)
                next = (next + Random.Shared.Next(1, maps.Length)) % maps.Length;
            if (i == WinnerIndex && winnerMapIndex >= 0) next = winnerMapIndex;
            var map = maps[next];
            var card = new Panel
            {
                Name = $"ReelCard{i}", Size = new Vector2(CardWidth, CardHeight), Position = new Vector2(i * Step, 0),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            card.AddThemeStyleboxOverride("panel", CardStyle(CardAccents[i % CardAccents.Length]));
            _track.AddChild(card);
            var picture = new TextureRect
            {
                Texture = MapImages.Wide(_catalog, map.Id), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore,
            };
            card.AddChild(picture);
            picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 4);
            var caption = new ColorRect
            {
                Color = new Color(0.035f, 0.055f, 0.045f, 0.94f), MouseFilter = MouseFilterEnum.Ignore,
                AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1, OffsetTop = -50, OffsetLeft = 4, OffsetRight = -4, OffsetBottom = -4,
            };
            card.AddChild(caption);
            var label = new Label
            {
                Name = "MapTitle", Text = map.Title, VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MaxLinesVisible = 2, MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", 15);
            caption.AddChild(label);
            label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
            if (i == WinnerIndex) winnerCard = card;
            if (maps.Length > 1) next = (next + Random.Shared.Next(1, maps.Length)) % maps.Length;
        }
        return winnerCard;
    }

    private static StyleBoxFlat CardStyle(Color accent) => new()
    {
        BgColor = ReelBackground.Lightened(0.08f), BorderColor = accent,
        BorderWidthTop = 4, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthBottom = 2,
    };

    private void PositionTrack() => _track.Position = new Vector2(
        (_reel.Size.X - CardWidth) * 0.5f - _offset, (_reel.Size.Y - CardHeight) * 0.5f);

    private void Roll()
    {
        if (_rolling || SelectedMaps.Length == 0) return;
        var maps = SelectedMaps;
        if (maps.Length > 1)
            maps = maps.Where(map => !string.Equals(map.Id, _currentMap, StringComparison.OrdinalIgnoreCase)).ToArray();
        int winnerMapIndex = Random.Shared.Next(maps.Length);
        _animation?.Kill();
        _winner = null;
        Panel winnerCard = BuildTrack(maps, WinnerIndex + 5, winnerMapIndex)!;
        _rolling = true;
        _close.GrabFocus();
        _playlist.Disabled = _roll.Disabled = _use.Disabled = true;
        _status.Text = "REEL IN MOTION";
        _result.Text = "Drawing a map…";
        _result.Modulate = UiStyle.MenuText;
        _reticle.QueueRedraw();
        _lastTick = StartIndex;
        AdvanceReel(StartIndex * Step);

        _animation = CreateTween();
        _animation.TweenMethod(Callable.From<float>(AdvanceReel), StartIndex * Step, WinnerIndex * Step, 4.1)
            .SetTrans(Tween.TransitionType.Quart).SetEase(Tween.EaseType.Out);
        _animation.TweenCallback(Callable.From(() => Reveal(maps[winnerMapIndex], winnerCard)));
    }

    private void AdvanceReel(float offset)
    {
        _offset = offset;
        PositionTrack();
        int tick = Mathf.RoundToInt(offset / Step);
        if (tick != _lastTick) _tick?.Play();
        _lastTick = tick;
    }

    private void Reveal((string Id, string Title) winner, Panel card)
    {
        _rolling = false;
        _winner = winner.Id;
        _playlist.Disabled = _roll.Disabled = _use.Disabled = false;
        _roll.Text = "Spin again";
        _status.Text = "MAP LOCKED";
        _result.Text = winner.Title;
        _result.Modulate = Gold;
        card.Name = "WinningMapCard";
        _reticle.QueueRedraw();
        _tick?.Stop();
        _lock?.Play();
        _use.GrabFocus();

        var style = CardStyle(Gold);
        style.ShadowColor = Gold with { A = 0.7f };
        style.ShadowSize = 14;
        card.AddThemeStyleboxOverride("panel", style);
        card.PivotOffset = card.Size * 0.5f;
        card.Scale = Vector2.One * 1.05f;
        foreach (Panel other in _track.GetChildren().OfType<Panel>())
            if (other != card) other.Modulate = Colors.White with { A = 0.43f };
        _animation = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _animation.TweenProperty(card, "scale", Vector2.One, 0.5);
        _animation.Parallel().TweenProperty(style, "shadow_color", Gold with { A = 0.26f }, 0.5);
    }

    private AudioStreamPlayer AddSound(AudioStream stream, float volume)
    {
        var sound = new AudioStreamPlayer { Bus = ReverbBuses.MenuSoundBus, Stream = stream, VolumeDb = volume };
        AddChild(sound);
        return sound;
    }

    private void Accept()
    {
        if (_winner is not { } map || IsQueuedForDeletion()) return;
        Close();
        _useMap(map);
    }

    public void Close()
    {
        _animation?.Kill();
        QueueFree();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    public override void _ExitTree()
    {
        foreach (var entry in _backgroundFocus)
            if (IsInstanceValid(entry.Key)) entry.Key.FocusMode = entry.Value;
        if (IsInstanceValid(_returnFocus) && _returnFocus.IsVisibleInTree()) _returnFocus.GrabFocus();
    }
}
