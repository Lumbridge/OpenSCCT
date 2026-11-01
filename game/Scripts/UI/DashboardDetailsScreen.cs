using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>Readable in-game release notes and announcements, using the dashboard's cached data.</summary>
public partial class DashboardDetailsScreen : MenuPage
{
    private const string ReleasesUrl = "https://github.com/Lumbridge/OpenSCCT/releases";
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private MenuDashboardService? _data;
    private IDisposable? _watch;
    private Label? _status, _announcement;
    private VBoxContainer? _entries;
    private ChangelogResponse? _shown;
    private bool _initialEntries = true;
    private Button? _back;
    private ScrollContainer? _scroll;
    private float _stickScroll;

    public bool IsAnnouncement { get; init; }
    public static DashboardDetailsScreen ShowChangelog(SceneTree tree) => FrontEnd.Show(tree, new DashboardDetailsScreen());
    public static DashboardDetailsScreen ShowAnnouncement(SceneTree tree) => FrontEnd.Show(tree, new DashboardDetailsScreen { IsAnnouncement = true });

    public override void _Ready()
    {
        Name = IsAnnouncement ? "AnnouncementDetails" : "ChangelogDetails";
        BuildFrame(IsAnnouncement ? "Announcement" : "What’s new", 940f, responsive: true);
        _scroll = PageCanvas.GetNode<ScrollContainer>("BodyScroll");
        Body.AddThemeConstantOverride("separation", 14);
        Label hint = TextLine(Body, "ScrollHint", "Scroll: mouse wheel · Page Up / Down · controller right stick");
        hint.Modulate = UiStyle.Dim;
        hint.AddThemeFontSizeOverride("font_size", 14);
        _status = TextLine(Body, "FeedStatus", string.Empty);
        _status.Modulate = UiStyle.Dim;
        if (IsAnnouncement)
        {
            var panel = Panel(Body, "Announcement", "Community briefing");
            _announcement = TextLine(panel, "AnnouncementText", string.Empty);
        }
        else
        {
            _entries = new VBoxContainer { Name = "Releases" };
            _entries.AddThemeConstantOverride("separation", 14);
            Body.AddChild(_entries);
            var all = UiStyle.MenuButton("All releases on GitHub — opens browser", () => OS.ShellOpen(ReleasesUrl), "AllReleases");
            all.CustomMinimumSize = new Vector2(0f, 44f);
            Body.AddChild(all);
        }
        _back = PageButton("Back", Back, "Back");
        _back.GrabFocus();
        _data = MenuDashboardService.Get(GetTree());
        _data.Changed += Refresh;
        _watch = _data.Watch();
        Refresh();
    }

    private void Back() => FrontEnd.Show(GetTree(), new MainMenu { InitialFocus = IsAnnouncement ? "ReadAnnouncement" : "WhatsNew" });

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_scroll is not null && IsVisibleInTree() && !IsQueuedForDeletion() && input is InputEventKey { Pressed: true } key)
        {
            int page = Math.Max(1, (int)(_scroll.Size.Y * 0.8f));
            switch (key.Keycode)
            {
                case Key.Pagedown: _scroll.ScrollVertical += page; break;
                case Key.Pageup: _scroll.ScrollVertical -= page; break;
                case Key.Home: _scroll.ScrollVertical = 0; break;
                case Key.End: _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue; break;
                default: base._UnhandledKeyInput(input); return;
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        base._UnhandledKeyInput(input);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_scroll is null || !IsVisibleInTree() || IsQueuedForDeletion() || !SettingsStore.Current.Pad.Enabled) return;
        int device = PadMenus.CurrentDevice();
        float axis = device < 0 ? 0f : Input.GetJoyAxis(device, JoyAxis.RightY);
        if (Math.Abs(axis) <= 0.25f) { _stickScroll = 0f; return; }
        _stickScroll += axis * 480f * (float)delta;
        int movement = (int)_stickScroll;
        _stickScroll -= movement;
        _scroll.ScrollVertical += movement;
    }

    private void Refresh()
    {
        if (_data is null) return;
        if (IsAnnouncement)
        {
            _announcement!.Text = string.IsNullOrWhiteSpace(_data.Announcement) ? "There are no current announcements." : _data.Announcement;
            _status!.Text = _data.Status.Stale ? "Showing the last announcement received. The service is currently unavailable."
                : _data.Status.Loading ? "Checking for announcements…" : _data.Status.Unavailable ? "Announcements are currently unavailable." : string.Empty;
            _status.Visible = _status.Text.Length > 0;
            return;
        }
        MenuDashboardFeed<ChangelogResponse> feed = _data.Changelog;
        bool unavailable = feed.Unavailable || feed.Value?.Unavailable == true;
        _status!.Text = unavailable ? "Release notes are currently unavailable." + (feed.Value?.Entries.Count > 0 ? " Showing the last notes received." : string.Empty)
            : feed.Loading ? "Checking for updates…" : feed.Value?.Entries.Count > 0 ? "The latest five releases · newest first" : "No published updates yet.";
        if (ReferenceEquals(_shown, feed.Value)) return;
        _shown = feed.Value;
        string? focus = GetViewport().GuiGetFocusOwner() is { } owner && _entries!.IsAncestorOf(owner) ? owner.Name.ToString() : null;
        foreach (Node child in _entries!.GetChildren())
        {
            _entries.RemoveChild(child);
            child.QueueFree();
        }
        int index = 0;
        foreach (ChangelogEntry release in feed.Value?.Entries.Take(5) ?? [])
        {
            if (_initialEntries && index == 0) _expanded.Add(release.Tag);
            var panel = Panel(_entries, "Release" + index, release.Tag + " · " + release.Published.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
            panel.AddThemeConstantOverride("separation", 10);
            TextLine(panel, "ReleaseTitle", release.Name);
            var toggle = UiStyle.MenuButton("Read release notes", () => { }, "ReleaseToggle" + index);
            toggle.CustomMinimumSize = new Vector2(0f, 40f);
            panel.AddChild(toggle);
            var notes = new VBoxContainer { Name = "Notes", Visible = _expanded.Contains(release.Tag) };
            notes.AddThemeConstantOverride("separation", 10);
            panel.AddChild(notes);
            string text = MenuDashboardModel.PlainNotes(release.Notes);
            TextLine(notes, "ReleaseNotes", text.Length == 0 ? "No release notes were provided for this version." : text);
            // The destination is constructed from a fixed HTTPS origin, never executed from remote markup.
            string url = ReleasesUrl + "/tag/" + Uri.EscapeDataString(release.Tag);
            var link = UiStyle.MenuButton("View on GitHub — opens browser", () => OS.ShellOpen(url), "ReleaseLink" + index);
            link.CustomMinimumSize = new Vector2(0f, 40f);
            notes.AddChild(link);
            toggle.Text = notes.Visible ? "Hide release notes" : "Read release notes";
            toggle.Pressed += () =>
            {
                notes.Visible = !notes.Visible;
                if (notes.Visible) _expanded.Add(release.Tag); else _expanded.Remove(release.Tag);
                toggle.Text = notes.Visible ? "Hide release notes" : "Read release notes";
            };
            index++;
        }
        if (index > 0) _initialEntries = false;
        if (focus is not null)
        {
            Control? restored = _entries.FindChild(focus, true, false) as Control;
            (restored is not null && restored.IsVisibleInTree() ? restored : _back)?.GrabFocus();
        }
    }

    private static Label TextLine(Container parent, string name, string text)
    {
        var label = new Label
        {
            Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore,
        };
        parent.AddChild(label);
        return label;
    }

    public override void _ExitTree()
    {
        if (_data is not null) _data.Changed -= Refresh;
        _watch?.Dispose();
    }
}
