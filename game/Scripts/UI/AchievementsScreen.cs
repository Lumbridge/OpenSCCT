using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>
/// Achievements (OpenSCCT's own): every achievement the game knows as a grid of tiles by group (picture of what it
/// unlocks, title, progress bar), and beside it the picked one in detail: what to do, how far along the signed-in
/// account is, and a turning 3D preview of each cosmetic it unlocks. The list comes from the shared catalogue, so it is
/// complete even when the master is older; progress and earned dates come from the master's account answer.
/// </summary>
public partial class AchievementsScreen : MenuPage
{
    private const float BodyWidth = 1180f;
    private const float ListWidth = 712f;
    private const float DetailWidth = 448f;
    private const int TileColumns = 3;
    private static readonly Vector2 TileSize = new(222f, 84f);
    private const int TileIcon = 52;

    /// <summary>The page's own rows above the split (summary, overall bar, filters) and the frame's title and buttons.</summary>
    private const float ChromeHeight = 156f + 104f;

    private readonly ButtonGroup _tileGroup = new();
    private readonly ButtonGroup _rewardGroup = new();
    private readonly Dictionary<string, Button> _tiles = new(StringComparer.Ordinal);
    private Label? _summary;
    private UiProgress? _overall;
    private ScrollContainer? _listScroll;
    private ScrollContainer? _detailScroll;
    private VBoxContainer? _list;
    private VBoxContainer? _detail;
    private CosmeticPreview? _preview;
    private Label? _itemName;
    private AccountService? _service;
    private Task<AccountInfo>? _refresh;
    private AchievementCategory? _filter;
    private string? _selected;
    private string? _shown;
    private Dictionary<string, AchievementInfo> _reported = new(StringComparer.Ordinal);
    private AccountProgress _local = new();

    public override void _Ready()
    {
        Name = "Achievements";
        BuildFrame("Achievements", BodyWidth);
        _service = AccountService.Get(GetTree());

        var header = new HBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", 16);
        _summary = new Label
        {
            Name = "Summary", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(_summary);
        _overall = new UiProgress { Name = "Overall", CustomMinimumSize = new Vector2(260f, 6f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        header.AddChild(_overall);
        Body.AddChild(header);

        var filters = new HFlowContainer { Name = "Filters" };
        filters.AddChild(Filter("All", null));
        foreach (AchievementCategory c in Enum.GetValues<AchievementCategory>())
        {
            filters.AddChild(Filter(CategoryName(c), c));
        }

        Body.AddChild(filters);

        var split = new HBoxContainer { Name = "Split" };
        split.AddThemeConstantOverride("separation", 20);
        Body.AddChild(split);
        _listScroll = new ScrollContainer
        {
            Name = "ListScroll", CustomMinimumSize = new Vector2(ListWidth, 0f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        split.AddChild(_listScroll);
        _list = new VBoxContainer { Name = "List", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 10);
        _listScroll.AddChild(_list);

        _detailScroll = new ScrollContainer
        {
            Name = "DetailScroll", CustomMinimumSize = new Vector2(DetailWidth, 0f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        split.AddChild(_detailScroll);
        var detailFrame = new VBoxContainer { Name = "DetailFrame", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detailScroll.AddChild(detailFrame);
        _detail = Panel(detailFrame, "Detail", null);
        _detail.AddThemeConstantOverride("separation", 6);

        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();
        PageButton("Armoury", () => FrontEnd.ShowArmoury(GetTree()), "Armoury");
        GetViewport().SizeChanged += FitHeight;
        FitHeight();
        if (AccountService.HasMaster && AccountSettings.Current.SignedIn)
        {
            _refresh = AccountService.Call(c => c.AccountAsync());
        }

        Rebuild();
    }

    public override void _ExitTree()
    {
        if (GetViewport() is { } viewport)
        {
            viewport.SizeChanged -= FitHeight;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_refresh is { IsCompleted: true } refresh)
        {
            _refresh = null;
            if (refresh.IsCompletedSuccessfully)
            {
                _service!.Update(refresh.Result);
                Rebuild();
            }
        }
    }

    /// <summary>The menu's button for this page.</summary>
    public static Button MenuButton(Node from) =>
        UiStyle.MenuButton("Achievements", () => FrontEnd.ShowAchievements(from.GetTree()), "AchievementsPage");

    public static string CategoryName(AchievementCategory c) => c switch
    {
        AchievementCategory.Spy => "Spy",
        AchievementCategory.Merc => "Mercenary",
        AchievementCategory.Weapons => "Weapons",
        AchievementCategory.Gadgets => "Gadgets",
        AchievementCategory.Map => "Maps",
        _ => "General",
    };

    private static string CategoryNote(AchievementCategory c) => c switch
    {
        AchievementCategory.Weapons => "A camo ladder per merc gun, then Gilded for the golden guns",
        AchievementCategory.Gadgets => "Both kits, Change Gear gadgets too, then a side's whole kit for its Mastercraft suit and guns",
        AchievementCategory.Map => "Win on each map 3 times as a spy and 3 times as a mercenary",
        _ => string.Empty,
    };

    /// <summary>The list and the detail fill the page's height and scroll on their own, so the preview stays in view.</summary>
    private void FitHeight()
    {
        float height = Math.Max(320f, GetViewportRect().Size.Y - ChromeHeight);
        _listScroll!.CustomMinimumSize = new Vector2(ListWidth, height);
        _detailScroll!.CustomMinimumSize = new Vector2(DetailWidth, height);
    }

    private Button Filter(string text, AchievementCategory? category)
    {
        Button b = UiStyle.MenuButton(text, () =>
        {
            _filter = category;
            Rebuild();
        }, "Filter" + text);
        b.CustomMinimumSize = new Vector2(150f, 40f);
        return b;
    }

    // ---- progress ---------------------------------------------------------------------------------------------

    private readonly record struct Progress(bool Done, int Current, int Target, DateTimeOffset? Earned)
    {
        public float Fraction => Done ? 1f : Target > 0 ? Math.Min(Current, Target) / (float)Target : 0f;
    }

    private Progress ProgressOf(Achievement a)
    {
        AchievementInfo? reported = _reported.GetValueOrDefault(a.Id);
        if (reported?.Earned is { } earned)
        {
            return new Progress(true, 1, 1, earned);
        }

        (int current, int target) = reported is { Target: > 0 } r ? (r.Current, r.Target) : a.Goal(_local);
        return new Progress(false, current, target, null);
    }

    private static string StatusText(Progress p, bool longDate) =>
        p.Done
            ? "Earned " + p.Earned!.Value.ToLocalTime().ToString(longDate ? "d MMMM yyyy" : "d MMM yyyy", CultureInfo.InvariantCulture)
            : p.Target > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Min(p.Current, p.Target)} / {p.Target}")
            : "Not earned yet";

    private static List<CosmeticItem> RewardsOf(Achievement a) =>
        a.Rewards.Select(CosmeticCatalog.Find).Where(i => i is not null).Select(i => i!).ToList();

    // ---- the list ---------------------------------------------------------------------------------------------

    private void Rebuild()
    {
        foreach (Node child in _list!.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        _tiles.Clear();
        _shown = null;
        AccountInfo? account = _service?.Account;
        _reported = account?.Achievements.ToDictionary(a => a.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, AchievementInfo>(StringComparer.Ordinal);

        // Without the master's numbers for an achievement (an older master), the account's stats still give the
        // progress of the stat-based ones; feats count from zero.
        _local = new AccountProgress
        {
            Stats = account?.Stats ?? new AccountStats(),
            Earned = new HashSet<string>(_reported.Values.Where(a => a.Earned is not null).Select(a => a.Id), StringComparer.Ordinal),
        };

        int earned = Achievements.All.Count(a => _reported.TryGetValue(a.Id, out AchievementInfo? r) && r.Earned is not null);
        _overall!.Value = Achievements.All.Count == 0 ? 0f : earned / (float)Achievements.All.Count;
        _overall.Visible = account is not null;
        _summary!.Text = account is null
            ? AccountService.HasMaster
                ? "Sign in on the Account page to earn these on official servers. Each one unlocks cosmetics for the Armoury."
                : "Achievements are earned on servers linked to an official master server; none is set up."
            : string.Create(CultureInfo.InvariantCulture,
                $"{earned} of {Achievements.All.Count} earned. Only rounds on official servers with a signed-in player on each side count.");

        Achievement? first = null;
        foreach (IGrouping<AchievementCategory, Achievement> group in Achievements.All.GroupBy(a => a.Category))
        {
            if (_filter is { } f && f != group.Key)
            {
                continue;
            }

            int groupEarned = group.Count(a => ProgressOf(a).Done);
            _list.AddChild(GroupHeader(group.Key, groupEarned, group.Count()));
            var grid = new GridContainer { Name = "Group" + group.Key, Columns = TileColumns };
            grid.AddThemeConstantOverride("h_separation", 8);
            grid.AddThemeConstantOverride("v_separation", 8);
            _list.AddChild(grid);
            foreach (Achievement a in group)
            {
                first ??= a;
                grid.AddChild(Tile(a));
            }
        }

        Achievement? selected = Achievements.All.FirstOrDefault(a => a.Id == _selected && _tiles.ContainsKey(a.Id)) ?? first;
        Select(selected);
    }

    private static Control GroupHeader(AchievementCategory c, int earned, int total)
    {
        var row = new HBoxContainer { Name = "Header" + c };
        row.AddThemeConstantOverride("separation", 12);
        var name = new Label { Text = CategoryName(c), Modulate = UiStyle.MenuFocus, Uppercase = true };
        name.AddThemeFontSizeOverride("font_size", 18);
        if (!UiStyle.IsOriginal && UiArt.CaptionFont is { } mono)
        {
            name.AddThemeFontOverride("font", mono);
        }

        row.AddChild(name);
        row.AddChild(new Label
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"{earned} / {total}"), Modulate = UiStyle.Dim,
            VerticalAlignment = VerticalAlignment.Center,
        });
        string note = CategoryNote(c);
        if (note.Length > 0)
        {
            row.AddChild(new Label
            {
                Text = note, Modulate = UiStyle.Dim, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            });
        }

        return row;
    }

    private Button Tile(Achievement a)
    {
        Progress p = ProgressOf(a);
        var tile = new Button
        {
            Name = "Achievement_" + a.Id, ToggleMode = true, ButtonGroup = _tileGroup, CustomMinimumSize = TileSize,
            FocusMode = FocusModeEnum.All, ThemeTypeVariation = UiStyle.TileVariation, TooltipText = a.Title + ": " + a.Description,
        };
        var row = new HBoxContainer { Name = "Row", MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 10);
        tile.AddChild(row);

        // The picture: the first thing it unlocks, greyed until earned.
        var icon = new TextureRect
        {
            Name = "Icon", CustomMinimumSize = new Vector2(TileIcon, TileIcon), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore, Modulate = p.Done ? Colors.White : new Color(0.55f, 0.55f, 0.55f, 0.75f),
        };
        CosmeticItem? reward = RewardsOf(a).FirstOrDefault(i => CosmeticThumbs.For(i, TileIcon) is not null);
        icon.Texture = reward is null ? null : CosmeticThumbs.For(reward, TileIcon);
        var iconFrame = new PanelContainer { Name = "IconFrame", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        iconFrame.AddThemeStyleboxOverride("panel", UiStyle.ThumbBox());
        iconFrame.AddChild(icon);
        row.AddChild(iconFrame);

        var column = new VBoxContainer { Name = "Text", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 2);
        column.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(column);
        var title = new Label
        {
            Name = "Title", Text = a.Title, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore, Modulate = p.Done ? UiStyle.MenuFocus : UiStyle.MenuText,
        };
        title.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(title);
        var status = new Label
        {
            Name = "Status", Text = p.Done ? "EARNED" : StatusText(p, false), MouseFilter = MouseFilterEnum.Ignore,
            Modulate = p.Done ? UiStyle.MenuFocus : UiStyle.Dim,
        };
        status.AddThemeFontSizeOverride("font_size", 12);
        column.AddChild(status);
        column.AddChild(new UiProgress { Name = "Bar", Value = p.Fraction, Fill = p.Done ? UiArt.Accent : UiArt.AccentDim });

        if (!p.Done)
        {
            tile.SelfModulate = new Color(1f, 1f, 1f, 0.8f);
        }

        tile.Pressed += () => Select(a);
        tile.FocusEntered += () => Select(a);
        _tiles[a.Id] = tile;
        return tile;
    }

    // ---- the detail -------------------------------------------------------------------------------------------

    private void Select(Achievement? a)
    {
        if (a is not null && _tiles.TryGetValue(a.Id, out Button? tile))
        {
            tile.SetPressedNoSignal(true);
        }

        // Focus and a click on the same tile both select it: build the detail (and load the preview) once.
        if (a?.Id == _shown)
        {
            return;
        }

        _selected = a?.Id;
        _shown = a?.Id;
        BuildDetail(a);
    }

    private void BuildDetail(Achievement? a)
    {
        foreach (Node child in _detail!.GetChildren())
        {
            _detail.RemoveChild(child);
            child.QueueFree();
        }

        _preview = null;
        _itemName = null;
        if (a is null)
        {
            return;
        }

        Progress p = ProgressOf(a);
        var caption = new Label { Name = "Category", Text = CategoryName(a.Category), Modulate = UiStyle.Dim, Uppercase = true };
        caption.AddThemeFontSizeOverride("font_size", 13);
        _detail.AddChild(caption);
        var title = new Label
        {
            Name = "Title", Text = a.Title, Modulate = p.Done ? UiStyle.MenuFocus : UiStyle.MenuTitle,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeFontSizeOverride("font_size", 26);
        _detail.AddChild(title);
        _detail.AddChild(Wrapped(a.Description, UiStyle.MenuText));

        var status = new HBoxContainer { Name = "Status" };
        status.AddThemeConstantOverride("separation", 10);
        status.AddChild(new Label { Text = StatusText(p, true), Modulate = p.Done ? UiStyle.MenuFocus : UiStyle.MenuText });
        status.AddChild(new UiProgress
        {
            Name = "Bar", Value = p.Fraction, Fill = p.Done ? UiArt.Accent : UiArt.AccentDim, CustomMinimumSize = new Vector2(0f, 6f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
        });
        _detail.AddChild(status);

        List<CosmeticItem> rewards = RewardsOf(a);
        if (rewards.Count == 0)
        {
            _detail.AddChild(Wrapped("Unlocks no cosmetic: this one is for the record.", UiStyle.Dim));
            return;
        }

        var unlocks = new Label { Name = "UnlocksCaption", Text = p.Done ? "Unlocked" : "Unlocks", Modulate = UiStyle.Dim, Uppercase = true };
        unlocks.AddThemeFontSizeOverride("font_size", 13);
        _detail.AddChild(unlocks);

        var frame = new PanelContainer { Name = "PreviewFrame" };
        frame.AddThemeStyleboxOverride("panel", UiStyle.ThumbBox());
        _detail.AddChild(frame);
        var stack = new Control { Name = "Stack", CustomMinimumSize = new Vector2(DetailWidth - 44f, 250f), MouseFilter = MouseFilterEnum.Pass };
        frame.AddChild(stack);
        _preview = new CosmeticPreview(new Vector2(DetailWidth - 44f, 250f));
        _preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        stack.AddChild(_preview);
        if (!p.Done)
        {
            var locked = new Label
            {
                Name = "Locked", Text = "LOCKED", Modulate = UiStyle.Dim, MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            locked.AddThemeFontSizeOverride("font_size", 12);
            locked.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize, 8);
            stack.AddChild(locked);
        }

        _itemName = new Label { Name = "ItemName", Modulate = UiStyle.MenuFocus, HorizontalAlignment = HorizontalAlignment.Center };
        _detail.AddChild(_itemName);

        if (rewards.Count > 1)
        {
            var chips = new HFlowContainer { Name = "Rewards" };
            chips.AddThemeConstantOverride("h_separation", 6);
            chips.AddThemeConstantOverride("v_separation", 6);
            _detail.AddChild(chips);
            foreach (CosmeticItem item in rewards)
            {
                chips.AddChild(RewardChip(item, ReferenceEquals(item, rewards[0])));
            }
        }

        ShowReward(rewards[0]);
    }

    private Button RewardChip(CosmeticItem item, bool first)
    {
        var chip = new Button
        {
            Name = "Reward_" + item.Id.Replace('.', '_'), Text = (item.Side == CosmeticSide.Spy ? "Spy " : "Merc ") +
                CosmeticCatalog.SlotName(item.Slot, item.Side).ToLowerInvariant(),
            ToggleMode = true, ButtonGroup = _rewardGroup, ButtonPressed = first, Icon = CosmeticThumbs.For(item, 24), ExpandIcon = false,
            CustomMinimumSize = new Vector2(0f, 36f), ThemeTypeVariation = UiStyle.TileVariation,
        };
        chip.AddThemeFontSizeOverride("font_size", 13);
        chip.Pressed += () => ShowReward(item);
        return chip;
    }

    private void ShowReward(CosmeticItem item)
    {
        _preview?.ShowItem(item);
        if (_itemName is not null)
        {
            _itemName.Text = CosmeticCatalog.Describe(item);
        }
    }

    private static Label Wrapped(string text, Color colour) => new()
    {
        Text = text, Modulate = colour, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(DetailWidth - 44f, 0f),
    };
}
