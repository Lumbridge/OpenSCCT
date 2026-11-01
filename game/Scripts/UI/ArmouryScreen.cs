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
/// The Armoury (OpenSCCT's own): the spy's and the merc's cosmetics per slot (suit or armour, light colour, pistol or
/// each merc gun) beside a kit panel with a turning 3D preview of the chosen look (the character in its suit and light
/// colour, or a gun in its camo). Pointing at any item, locked ones too, tries it on in the preview; picking an
/// unlocked one saves it to the settings, which follow the account. Servers check the choice against the account's
/// unlocks when the player joins.
/// </summary>
public partial class ArmouryScreen : MenuPage
{
    private const float BodyWidth = 1180f;
    private const float ListWidth = 712f;
    private const float KitWidth = 448f;
    private const int ThumbSize = 40;

    /// <summary>The frame's title and buttons and the page's side switch above the split.</summary>
    private const float ChromeHeight = 104f + 64f + 52f + 48f;

    private ScrollContainer? _listScroll;
    private ScrollContainer? _kitScroll;
    private VBoxContainer? _content;
    private VBoxContainer? _kit;
    private CosmeticPreview? _preview;
    private Label? _viewName;
    private HFlowContainer? _views;
    private VBoxContainer? _equipped;
    private AccountService? _service;
    private Task<AccountInfo>? _refresh;
    private CosmeticSide _side = CosmeticSide.Spy;
    private CosmeticSlot _view = CosmeticSlot.Body;

    /// <summary>The item being tried on (pointed at) and its slot, or null for the chosen kit.</summary>
    private (CosmeticSlot Slot, CosmeticItem? Item)? _tryOn;

    /// <summary>
    /// Set when the page is opened over the hub or the lobby instead of as a menu screen: Back and Escape call it, and the
    /// Achievements button is left out. Saved choices reach the hub through the saved settings (HubService); the lobby sends them on Back.
    /// </summary>
    public Action? Closed { get; init; }

    public override void _Ready()
    {
        Name = "Armoury";
        BuildFrame("Armoury", BodyWidth);
        _service = AccountService.Get(GetTree());
        _service.AppearanceChanged += OnAppearanceChanged;
        Body.AddChild(new AppearanceSyncPanel());
        var sides = new HBoxContainer { Name = "Sides" };
        sides.AddChild(UiStyle.MenuButton("Spy", () => ShowSide(CosmeticSide.Spy), "SideSpy"));
        sides.AddChild(UiStyle.MenuButton("Mercenary", () => ShowSide(CosmeticSide.Merc), "SideMerc"));
        sides.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        sides.AddChild(VisibilityBox("HideOthers", "Hide others' cosmetics", SettingsStore.Current.CosmeticsHideOthers,
            (s, on) => s.CosmeticsHideOthers = on));
        sides.AddChild(VisibilityBox("HideMine", "Hide my cosmetics from others", SettingsStore.Current.CosmeticsHideMine,
            (s, on) => s.CosmeticsHideMine = on));
        Body.AddChild(sides);

        var split = new HBoxContainer { Name = "Split" };
        split.AddThemeConstantOverride("separation", 20);
        Body.AddChild(split);
        _listScroll = new ScrollContainer
        {
            Name = "ListScroll", CustomMinimumSize = new Vector2(ListWidth, 0f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        split.AddChild(_listScroll);
        _content = new VBoxContainer { Name = "Content", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 8);
        _listScroll.AddChild(_content);

        _kitScroll = new ScrollContainer
        {
            Name = "KitScroll", CustomMinimumSize = new Vector2(KitWidth, 0f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        split.AddChild(_kitScroll);
        var kitFrame = new VBoxContainer { Name = "KitFrame", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _kitScroll.AddChild(kitFrame);
        _kit = Panel(kitFrame, "Kit", "Your kit");
        _kit.AddThemeConstantOverride("separation", 6);
        BuildKit();

        if (Closed is { } closed)
        {
            PageButton("Back", closed, "Back").GrabFocus();
        }
        else
        {
            PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();
            PageButton("Achievements", () => FrontEnd.ShowAchievements(GetTree()), "Achievements");
        }

        GetViewport().SizeChanged += FitHeight;
        FitHeight();
        if (AccountService.HasMaster && AccountSettings.Current.SignedIn)
        {
            _refresh = AccountService.Call(c => c.AccountAsync());
        }

        ShowSide(_side);
    }

    public override void _ExitTree()
    {
        if (_service is not null) _service.AppearanceChanged -= OnAppearanceChanged;
        if (GetViewport() is { } viewport)
        {
            viewport.SizeChanged -= FitHeight;
        }
    }

    private void OnAppearanceChanged()
    {
        if (_content is not null && _kit is not null) ShowSide(_side);
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
                ShowSide(_side);
            }
        }
    }

    /// <summary>Over the hub, Escape closes the page; no other key reaches the game behind it.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (Closed is not { } closed || @event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        if (key.Keycode == Key.Escape && !key.Echo)
        {
            closed();
        }

        GetViewport().SetInputAsHandled();
    }

    /// <summary>The menu's button for this page.</summary>
    public static Button MenuButton(Node from) =>
        UiStyle.MenuButton("Armoury", () => FrontEnd.ShowArmoury(from.GetTree()), "Armoury");

    /// <summary>The list and the kit panel fill the window's height between the side switch and the page buttons.</summary>
    private void FitHeight()
    {
        float height = Math.Max(320f, GetViewportRect().Size.Y - ChromeHeight);
        _listScroll!.CustomMinimumSize = new Vector2(ListWidth, height);
        _kitScroll!.CustomMinimumSize = new Vector2(KitWidth, height);
    }

    private void BuildKit()
    {
        var frame = new PanelContainer { Name = "PreviewFrame" };
        frame.AddThemeStyleboxOverride("panel", UiStyle.ThumbBox());
        _kit!.AddChild(frame);
        _preview = new CosmeticPreview(new Vector2(KitWidth - 44f, 330f));
        frame.AddChild(_preview);
        _viewName = new Label { Name = "ViewName", Modulate = UiStyle.MenuFocus, HorizontalAlignment = HorizontalAlignment.Center };
        _kit.AddChild(_viewName);
        _views = new HFlowContainer { Name = "Views" };
        _views.AddThemeConstantOverride("h_separation", 6);
        _views.AddThemeConstantOverride("v_separation", 6);
        _kit.AddChild(_views);
        var caption = new Label { Name = "EquippedCaption", Text = "Equipped", Modulate = UiStyle.Dim, Uppercase = true };
        caption.AddThemeFontSizeOverride("font_size", 13);
        _kit.AddChild(caption);
        _equipped = new VBoxContainer { Name = "Equipped" };
        _kit.AddChild(_equipped);
        var hint = new Label
        {
            Text = "Point at an item to try it on. Drag the model to turn it.", Modulate = UiStyle.Dim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(KitWidth - 44f, 0f),
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
        _kit.AddChild(hint);
    }

    private void ShowSide(CosmeticSide side)
    {
        if (side != _side)
        {
            _view = CosmeticSlot.Body;
        }

        _side = side;
        _tryOn = null;
        foreach (Node child in _content!.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        AccountInfo? account = _service?.Account;
        var unlocked = new HashSet<string>(account?.Unlocked ?? [], StringComparer.Ordinal);
        if (account is null)
        {
            _content.AddChild(Note(AccountService.HasMaster
                ? "Sign in on the Account page and earn achievements on official servers to unlock these. You can still try everything on."
                : "Cosmetics are unlocked by achievements on an official master server; none is set up. You can still try everything on."));
        }

        CosmeticLook look = Look(side);
        foreach (CosmeticSlot slot in CosmeticCatalog.SlotsOf(side))
        {
            VBoxContainer panel = Panel(_content, "Slot" + slot, CosmeticCatalog.SlotName(slot, side));
            var grid = new GridContainer { Name = "Items", Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 4);
            panel.AddChild(grid);
            grid.AddChild(ItemButton(side, slot, null, look[slot] is null, true, null));
            foreach (CosmeticItem item in CosmeticCatalog.For(side, slot))
            {
                AchievementInfo? from = account?.Achievements.FirstOrDefault(a => a.Rewards.Contains(item.Id));
                grid.AddChild(ItemButton(side, slot, item, ReferenceEquals(look[slot], item), unlocked.Contains(item.Id), from));
            }
        }

        RefreshKit();
    }

    private Button ItemButton(CosmeticSide side, CosmeticSlot slot, CosmeticItem? item, bool chosen, bool open, AchievementInfo? from)
    {
        string name = item?.Name ?? "Retail";
        string text = (chosen ? "> " : string.Empty) + name;
        if (!open && from is { Target: > 1 })
        {
            text += string.Create(CultureInfo.InvariantCulture, $"  {from.Current}/{from.Target}");
        }

        Button b = UiStyle.MenuButton(text, () => Choose(side, slot, item), "Item" + (item?.Id ?? slot + ".retail").Replace('.', '_'));
        b.CustomMinimumSize = new Vector2(340f, 44f);
        b.Alignment = HorizontalAlignment.Left;
        b.Disabled = !open;
        b.Icon = item is null ? null : CosmeticThumbs.For(item, ThumbSize);
        b.ExpandIcon = false;
        b.TooltipText = open ? (item is null ? "The original look." : CosmeticCatalog.Describe(item))
            : "Locked. Unlocked by: " + (from is null ? LevelSkinRequirement(item) : from.Title + " (" + from.Description + ")");
        if (chosen)
        {
            b.Modulate = UiStyle.MenuFocus;
        }

        // Trying on: pointing at (or moving the keyboard focus to) any item shows it in the preview, locked or not.
        b.MouseEntered += () => TryOn(slot, item);
        b.FocusEntered += () => TryOn(slot, item);
        b.MouseExited += EndTryOn;
        b.FocusExited += EndTryOn;
        return b;
    }

    private void TryOn(CosmeticSlot slot, CosmeticItem? item)
    {
        _tryOn = (slot, item);
        // A light is seen on the character's head; a suit on the whole character; a gun on its own.
        _view = slot;
        RefreshKit();
    }

    private void EndTryOn()
    {
        if (_tryOn is null)
        {
            return;
        }

        _tryOn = null;
        RefreshKit();
    }

    /// <summary>Redraws the kit panel: the preview of the chosen look (with the item tried on), the views and the list.</summary>
    private void RefreshKit()
    {
        if (_preview is null)
        {
            return;
        }

        CosmeticLook look = Look(_side);
        if (_tryOn is { } t)
        {
            look = look.With(t.Slot, t.Item);
        }

        _preview.ShowLook(_side, look, _view);
        CosmeticItem? shown = look[_view];
        _viewName!.Text = CosmeticCatalog.SlotName(_view, _side) + ": " + (shown?.Name ?? "Retail") + (_tryOn is not null ? "  (trying on)" : string.Empty);

        foreach (Node child in _views!.GetChildren())
        {
            _views.RemoveChild(child);
            child.QueueFree();
        }

        foreach (CosmeticSlot slot in CosmeticCatalog.SlotsOf(_side))
        {
            CosmeticSlot s = slot;
            var chip = new Button
            {
                Name = "View" + slot, Text = CosmeticCatalog.SlotName(slot, _side), ToggleMode = true, ButtonPressed = slot == _view,
                CustomMinimumSize = new Vector2(0f, 34f), ThemeTypeVariation = UiStyle.TileVariation, FocusMode = FocusModeEnum.None,
            };
            chip.AddThemeFontSizeOverride("font_size", 13);
            chip.Pressed += () =>
            {
                _view = s;
                RefreshKit();
            };
            _views.AddChild(chip);
        }

        foreach (Node child in _equipped!.GetChildren())
        {
            _equipped.RemoveChild(child);
            child.QueueFree();
        }

        CosmeticLook chosen = Look(_side);
        foreach (CosmeticSlot slot in CosmeticCatalog.SlotsOf(_side))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var icon = new TextureRect
            {
                Texture = chosen[slot] is { } item ? CosmeticThumbs.For(item, 24) : null, CustomMinimumSize = new Vector2(24f, 24f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            row.AddChild(icon);
            row.AddChild(new Label { Text = CosmeticCatalog.SlotName(slot, _side), Modulate = UiStyle.Dim, CustomMinimumSize = new Vector2(130f, 0f) });
            row.AddChild(new Label { Text = chosen[slot]?.Name ?? "Retail", Modulate = UiStyle.MenuText });
            _equipped.AddChild(row);
        }
    }

    private void Choose(CosmeticSide side, CosmeticSlot slot, CosmeticItem? item)
    {
        CosmeticLook look = Look(side).With(slot, item);
        string[] ids = CosmeticCatalog.SlotsOf(side).Select(s => look[s]?.Id).Where(id => id is not null).Select(id => id!).ToArray();
        GameSettings settings = SettingsStore.Current;
        if (side == CosmeticSide.Spy)
        {
            settings.CosmeticsSpy = ids;
        }
        else
        {
            settings.CosmeticsMerc = ids;
        }

        SettingsStore.Save(settings);
        _view = slot;
        ShowSide(side);
    }

    private static CosmeticLook Look(CosmeticSide side) =>
        CosmeticLook.FromIds(side, side == CosmeticSide.Spy ? SettingsStore.Current.CosmeticsSpy : SettingsStore.Current.CosmeticsMerc);

    /// <summary>
    /// A cosmetics visibility switch, saved when flipped. Hiding your own takes effect at the next join (the hub
    /// takes it at once); hiding others' redraws straight away.
    /// </summary>
    private static CheckBox VisibilityBox(string name, string text, bool on, Action<GameSettings, bool> set)
    {
        var box = new CheckBox { Name = name, Text = text, ButtonPressed = on };
        box.Toggled += pressed =>
        {
            GameSettings settings = SettingsStore.Current;
            set(settings, pressed);
            SettingsStore.Save(settings);
        };
        return box;
    }

    private static Label Note(string text) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(ListWidth - 40f, 0f),
        Modulate = UiStyle.MenuFocus,
    };
    private static string LevelSkinRequirement(CosmeticItem? item)
    {
        if (item is not null && (int)item.Slot >= 6 && (int)item.Slot < 6 + OpenScct.Core.Engine.Sidearms.All.Count)
        {
            var pistol = OpenScct.Core.Engine.Sidearms.Get((OpenScct.Core.Engine.SidearmKind)((int)item.Slot - 6));
            if (item.Pattern is "night_ops" or "cryo") return "account level " + (pistol.Level + (item.Pattern == "night_ops" ? 5 : 10));
        }
        return "an achievement";
    }

}
