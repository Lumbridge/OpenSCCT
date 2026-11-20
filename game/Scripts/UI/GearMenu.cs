using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.UI;

/// <summary>A draft loadout with persistent slots, a directly selectable catalogue and item details.</summary>
public partial class GearMenu : MenuPage
{
    private enum Category { Primary, Sidearm, Gadget }
    private static readonly Color Ink = new("10171d"), Surface = new("18232b"), Edge = new("30424b"),
        Accent = new("a5edba"), TextColour = new("edf3f3"), Muted = new("9aaeb6");
    private GearMenuModel? _model;
    private bool _waitingForTeam;
    private Button? _switchTeam;
    private readonly List<Action> _refreshSelections = new();
    public Func<GearMenuModel?>? TeamModelSource { get; init; }
    private Category _category;
    private int _slot;
    private string _inspected = "", _focusAfterBuild = "";
    private VBoxContainer? _details;
    private static readonly Dictionary<string, Inventory?> Items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture2D?> _icons = new(StringComparer.OrdinalIgnoreCase);

    public GearMenu(GearMenuModel? model)
    {
        Name = "GearMenu";
        _model = model;
        _category = model?.Spy == false ? Category.Primary : Category.Gadget;
    }

    public event Action<GearMenuModel>? Applied;
    public event Action? Closed;
    public event Action? TeamSwapRequested;
    public bool CanSwitchTeam { get; init; }
    public bool OwnKeys { get; init; }

    public override void _Ready()
    {
        BuildFrame("Equipment", 1380f, responsive: true);
        PageButton("Cancel", () => Closed?.Invoke(), "Cancel");
        var swap = PageButton(_model?.Spy == true ? "Switch to Merc" : "Switch to Spy",
            () => { if (CanSwitchTeam) { _waitingForTeam = true; TeamSwapRequested?.Invoke(); } }, "SwitchTeam");
        _switchTeam = swap;
        swap.Disabled = !CanSwitchTeam;
        swap.TooltipText = CanSwitchTeam ? "Change sides and use that team's loadout." : "Teams can be changed in the lobby or practice.";
        PageButton("Reset loadout", () => { _model?.Reset(); Rebuild(); }, "Reset");
        PageButton("Apply loadout", () => { if (_model is { } m) Applied?.Invoke(m); }, "OK");
        Resized += Rebuild;
        Rebuild();
    }

    public override void _Input(InputEvent e)
    {
        if (OwnKeys && IsVisibleInTree() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Closed?.Invoke();
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_waitingForTeam && TeamModelSource?.Invoke() is { } next && next.Spy != _model?.Spy)
        {
            _waitingForTeam = false;
            _model = next;
            _category = next.Spy ? Category.Gadget : Category.Primary;
            _slot = 0;
            _inspected = _focusAfterBuild = "";
            if (_switchTeam is not null) _switchTeam.Text = next.Spy ? "Switch to Merc" : "Switch to Spy";
            Rebuild();
        }
        float gap = LayoutSize.X < 1000 ? 12 : 24;
        float width = Math.Min(220f, (Math.Min(1100f, LayoutSize.X - 48f) - gap * 3f) / 4f);
        foreach (Button button in PageButtons)
            button.CustomMinimumSize = new Vector2(width, 44f);
    }

    private void Rebuild()
    {
        if (Body is null) return;
        foreach (Node child in Body.GetChildren()) { Body.RemoveChild(child); child.QueueFree(); }
        _details = null;
        _refreshSelections.Clear();
        if (_model is not { } m)
        {
            Body.AddChild(Text("Join a side to choose your equipment.", 19));
            return;
        }

        float width = Math.Min(1380f, AvailableBodyWidth);
        var heading = new HBoxContainer();
        Body.AddChild(heading);
        var title = Text(m.Spy ? "SPY  /  INFILTRATION KIT" : "MERCENARY  /  DEFENCE KIT", 15, Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(title);
        var count = Text($"{m.Slots} GADGET SLOTS", 13, Muted);
        count.AutowrapMode = TextServer.AutowrapMode.Off;
        heading.AddChild(count);
        Body.AddChild(Text("Choose a slot, compare equipment, then apply your loadout.", 14, Muted));
        Body.AddChild(new HSeparator());

        var main = new HBoxContainer { Name = "LoadoutWorkbench" };
        main.AddThemeConstantOverride("separation", 18);
        Body.AddChild(main);
        var kit = Column(214);
        main.AddChild(kit);
        kit.AddChild(Text("YOUR LOADOUT", 13, Muted));
        string primary = m.Spy ? Loadouts.SpyWeapon.Path ?? "" : m.ChosenWeapon;
        Slot(kit, "Weapon", "PRIMARY WEAPON", primary, Category.Primary, 0, m.Spy);
        if (!m.Spy) Slot(kit, "Sidearm", "SIDEARM", m.ChosenSidearm, Category.Sidearm, 0, !m.SidearmsEnabled);
        else kit.AddChild(Text("Cloak is standard equipment.\nIt uses no gadget slot.", 13, Muted));
        kit.AddChild(new HSeparator());
        for (int i = 0; i < 4; i++)
            Slot(kit, "Slot" + (i + 1), "GADGET " + (i + 1) +
                (m.Spy && i < 2 && Item(m.Gadgets[i]) is { CanQuickLaunch: true } ? "  /  QUICK USE " + (i + 1) : ""),
                m.Gadgets[i], Category.Gadget, i, i >= m.Slots);

        var centre = Column(0);
        centre.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        main.AddChild(centre);
        string caption = _category switch { Category.Primary => "PRIMARY WEAPONS", Category.Sidearm => "SIDEARMS", _ => "GADGETS  /  SLOT " + (_slot + 1) };
        centre.AddChild(Text(caption, 15, Accent));
        centre.AddChild(Text(_category == Category.Primary ? "Select a weapon. Use your Scope binding to aim down its sights." :
            _category == Category.Sidearm ? "Your sidearm has its own slot." : "Equipped gadgets swap slots when selected again.", 13, Muted));

        bool wide = width >= 1170;
        float catalogueWidth = width - 232 - (wide ? 324 : 0);
        var grid = new GridContainer { Name = "EquipmentCatalogue", Columns = catalogueWidth >= 650 ? 3 : catalogueWidth >= 390 ? 2 : 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        centre.AddChild(grid);
        IReadOnlyList<string> options = _category switch { Category.Primary => m.Weapons, Category.Sidearm => m.SidearmOptions, _ => m.Options };
        for (int i = 0; i < options.Count; i++) AddOption(grid, options[i], i);
        if (options.Count == 0) centre.AddChild(Text("No equipment is available for this slot.", 15, Muted));

        _details = Column(0);
        _details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // Keep changing descriptions and statistics out of the catalogue's minimum size.
        var detailsScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(wide ? 274 : 0, 540),
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        detailsScroll.AddChild(_details);
        var detailsPanel = Panel(detailsScroll);
        detailsPanel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        detailsPanel.Name = "EquipmentDetails";
        if (wide) main.AddChild(detailsPanel);
        else { centre.AddChild(new HSeparator()); centre.AddChild(detailsPanel); }
        string selected = CurrentSelection();
        if (_inspected.Length == 0 || !options.Contains(_inspected)) _inspected = selected;
        ShowDetails(_inspected);
        Body.AddChild(new HSeparator());
        Body.AddChild(Text(m.Rule, 13, Muted));
        string focus = _focusAfterBuild;
        _focusAfterBuild = "";
        var target = Body.FindChild(focus.Length > 0 ? focus : "Option1", true, false) as Control;
        if (target is Button { Disabled: false }) target.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Slot(VBoxContainer parent, string nodeName, string caption, string path, Category category, int slot, bool disabled)
    {
        bool active = _category == category && (category != Category.Gadget || _slot == slot);
        var button = new Button { Name = nodeName, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 67),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = disabled,
            TooltipText = disabled ? category == Category.Sidearm ? "Sidearms are disabled for this session." : "This slot is unavailable." : GearMenuModel.NameOf(path) };
        Style(button, active);
        var box = Column(0);
        Fill(button, box, 13);
        box.AddChild(Text(caption, 11, active ? Accent : Muted));
        var slotName = Text(GearMenuModel.NameOf(path), 16, disabled ? Muted : TextColour);
        box.AddChild(slotName);
        _refreshSelections.Add(() =>
        {
            string current = category == Category.Primary ? (_model!.Spy ? Loadouts.SpyWeapon.Path ?? "" : _model!.ChosenWeapon) :
                category == Category.Sidearm ? _model!.ChosenSidearm : _model!.Gadgets[slot];
            slotName.Text = GearMenuModel.NameOf(current);
            button.TooltipText = disabled ? button.TooltipText : slotName.Text;
        });
        button.Pressed += () => { _category = category; _slot = slot; _inspected = CurrentSelection(); _focusAfterBuild = nodeName; Rebuild(); };
        parent.AddChild(button);
    }

    private void AddOption(GridContainer grid, string path, int index)
    {
        var m = _model!;
        bool allowed = m.IsAllowed(path);
        bool selected = string.Equals(path, CurrentSelection(), StringComparison.OrdinalIgnoreCase);
        var card = new GearTile { Name = "Option" + (index + 1), Disabled = !allowed,
            CustomMinimumSize = new Vector2(180, _category == Category.Primary ? 167 : 147), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = allowed ? GearMenuModel.NameOf(path) : Restriction(path) };
        card.Atlas = Icon(path);
        card.Modulate = allowed ? Colors.White : new Color(.55f, .55f, .55f);
        card.HasIcon = card.Atlas is not null;
        card.Region = card.Atlas is null ? new Rect2() : new Rect2(Vector2.Zero, card.Atlas.GetSize());
        Style(card, selected);
        var stack = Column(0);
        Fill(card, stack, 12);
        var status = Text("", 10);
        stack.AddChild(status);
        void RefreshSelection()
        {
            bool chosen = string.Equals(path, CurrentSelection(), StringComparison.OrdinalIgnoreCase);
            bool held = _category == Category.Primary ? chosen : m.IsEquipped(path);
            Style(card, chosen);
            status.Text = chosen ? "SELECTED" : held ? "EQUIPPED" : !allowed ? "LOCKED" :
                MercRifles.Find(path)?.Suppressed == true ? "SUPPRESSED" : _category == Category.Primary ? "PRIMARY" : "AVAILABLE";
            status.LabelSettings.FontColor = chosen ? Accent : Muted;
        }
        _refreshSelections.Add(RefreshSelection);
        RefreshSelection();
        stack.AddChild(Picture(path, _category == Category.Primary ? 76 : 56));
        stack.AddChild(Text(GearMenuModel.NameOf(path), 17, allowed ? TextColour : Muted));
        string sub = !allowed && m.RequiredLevel(path) > 0 ? "REQUIRES LEVEL " + m.RequiredLevel(path) :
            MercRifles.Find(path) is { } rifle ? $"{rifle.Magazine} RDS  /  {rifle.Rpm} RPM" :
            Sidearms.Find(path) is { } sidearm ? "LEVEL " + sidearm.Level : Item(path) is { CanQuickLaunch: true } ? "QUICK USE" : "FIELD EQUIPMENT";
        stack.AddChild(Text(sub, 10, Muted));
        card.MouseEntered += () => ShowDetails(path);
        card.FocusEntered += () => ShowDetails(path);
        card.Pressed += () =>
        {
            if (_category == Category.Primary) m.ChooseWeapon(path);
            else if (_category == Category.Sidearm) m.ChooseSidearm(path);
            else m.Choose(_slot, path);
            _inspected = path;
            foreach (var refresh in _refreshSelections) refresh();
            ShowDetails(path);
        };
        grid.AddChild(card);
    }

    private string CurrentSelection() => _model is not { } m ? "" : _category switch
    { Category.Primary => m.ChosenWeapon, Category.Sidearm => m.ChosenSidearm, _ => m.Gadgets[_slot] };

    private void ShowDetails(string path)
    {
        if (_details is null) return;
        _inspected = path;
        foreach (Node child in _details.GetChildren()) { _details.RemoveChild(child); child.QueueFree(); }
        _details.AddChild(Text("EQUIPMENT INTEL", 11, Muted));
        _details.AddChild(Picture(path, 125));
        _details.AddChild(Text(GearMenuModel.NameOf(path), 25));
        var rifle = MercRifles.Find(path);
        if (rifle is not null) _details.AddChild(Text(rifle.Role, 11, Accent));
        _details.AddChild(new HSeparator());
        _details.AddChild(Text(rifle?.Description ?? StockDescription(path) ?? Sidearms.Find(path)?.Description ?? Item(path)?.ItemDescription ?? "Select equipment to see its details.", 14));
        bool allowed = _model!.IsAllowed(path);
        if (!allowed) _details.AddChild(Text(Restriction(path), 13, new Color("efb27d")));
        if (WeaponInfo(path) is { } weapon)
        {
            _details.AddChild(new HSeparator());
            Stat("DAMAGE", "Head / body");
            foreach (int metres in new[] { 0, 10, 25, 50 })
            {
                int body = WeaponRules.DistanceDamage(weapon, metres * 100f);
                int head = body * 3;
                if (weapon is SWeaponDefense_Shotgun shotgun)
                {
                    body = (int)(body * 9 * shotgun.TotalShotgunDamageMultipler);
                    head = (int)(head * 9 * shotgun.TotalShotgunDamageMultipler);
                }
                Stat(metres + " m", metres * 100f > weapon.ShootingRange ? "Out of range" : $"{head} / {body}");
            }
            _details.AddChild(Text(weapon is SWeaponDefense_Shotgun
                ? "Maximum if all 9 pellets hit the same zone. Spread usually reduces damage."
                : "Against a standing Spy. Limbs deal less damage. Damage falls continuously with distance.", 11, Muted));
            Stat("MAGAZINE / RESERVE", $"{weapon.ClipMaxAmmo} / {weapon.MaxAmmo - weapon.ClipMaxAmmo}");
            Stat("RATE OF FIRE", rifle?.BurstDefault == true ? "667 auto / 800 burst RPM" : MathF.Round(60f / weapon.RateOfFire) + " RPM");
            float reload = rifle?.ReloadSeconds ?? Sidearms.Find(path)?.ReloadSeconds ??
                ViewModelClips.DataOf(weapon.AFirstReloSt).Seconds(1f);
            if (rifle?.EmptyReloadSeconds is float emptyReload)
                Stat("RELOAD / EMPTY", $"{reload:0.00} / {emptyReload:0.00} s");
            else
                Stat("RELOAD", reload.ToString("0.00") + " s");
            Stat("SIGHTS", rifle is null ? weapon.bCanSnipe ? "Scope" : "Hip fire" : rifle.Id == "M4A1S" ? "Red dot" : "Iron sights");
            if (_model!.RequiredLevel(path) > 0) Stat("UNLOCK", "Level " + _model.RequiredLevel(path));
        }
        else if (Item(path) is SGadget gadget)
        {
            _details.AddChild(new HSeparator());
            Stat("USES", path == OpenScct.Game.Svm.CloneGadget.ClassPath ? "Recharges every 60 s" : path == OpenScct.Game.Svm.MineHackGadget.ClassPath ? "2 per life" : gadget.Charge > 0 ? gadget.Charge.ToString() : "Reusable");
            if (path == OpenScct.Game.Svm.MineHackGadget.ClassPath) Stat("HACK TIME", "Hold for 3 s");
            if (_model!.RequiredLevel(path) > 0) Stat("UNLOCK", "Level " + _model.RequiredLevel(path));
        }
    }

    private void Stat(string caption, string value)
    {
        var row = new HBoxContainer();
        var key = Text(caption, 10, Muted); key.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        key.AutowrapMode = TextServer.AutowrapMode.Off;
        var detail = Text(value, 13); detail.AutowrapMode = TextServer.AutowrapMode.Off;
        row.AddChild(key); row.AddChild(detail); _details!.AddChild(row);
    }

    private string Restriction(string path) => _model!.Restriction(path);

    private Control Picture(string path, int height) => new TextureRect { Texture = Icon(path), CustomMinimumSize = new Vector2(0, height),
        SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };

    private Texture2D? Icon(string path)
    {
        if (_icons.TryGetValue(path, out var known)) return known;
        string? file = MercRifles.Find(path) is { } rifle ? Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", "OpenScctRifles", "textures", rifle.Id.ToLowerInvariant() + "_icon.png") :
            Sidearms.Find(path) is { } sidearm ? Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", "OpenScctSidearms", "textures", sidearm.Id + "_icon.png") : null;
        if (StockAsset(path) is { } stock)
            file = Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", "SWeaponFirst", "textures", stock + "_icon.png");
        Texture2D? icon = null;
        if (file is not null && File.Exists(file)) icon = ImageTexture.CreateFromImage(Image.LoadFromFile(file));
        else if (Item(path) is { } item)
        {
            var atlas = UiStyle.Assets?.Texture("SHudTextures", item is SWeapon ? "Icones_Weapons" : "Icones_Gadgets");
            if (atlas is not null) icon = new AtlasTexture { Atlas = atlas, Region = new Rect2(item.IconU, item.IconV, 64, 64) };
        }
        _icons[path] = icon;
        return icon;
    }

    private static string? StockAsset(string path) => path switch
    {
        "SBase.SWeaponDefense" => "M160",
        "SBase.SWeaponDefense_Uzi" => "Hk81",
        "SBase.SWeaponDefense_Shotgun" => "BM401",
        _ => null,
    };

    private static string? StockDescription(string path) => path switch
    {
        "SBase.SWeaponDefense" => "The FN2000 is the standard Merc assault rifle. Switch between automatic and three-round burst fire, or use its telescopic scope for precise single shots.",
        "SBase.SWeaponDefense_Uzi" => "A compact submachine gun with a large magazine and fast automatic fire. Strong at close range, with faster damage loss over distance and no scope.",
        "SBase.SWeaponDefense_Shotgun" => "A pump-action shotgun firing nine pellets in a circular spread. Devastating nearby when the pellets connect; spread and damage loss limit its reach.",
        _ => null,
    };

    private static SWeaponDefense? WeaponInfo(string path)
    {
        if (Sidearms.Find(path) is { } sidearm)
        {
            var pistol = new MercSidearm { Kind = (int)sidearm.Kind };
            pistol.Configure(true);
            return pistol;
        }
        return Item(path) as SWeaponDefense;
    }

    private static Inventory? Item(string path)
    {
        if (!Items.TryGetValue(path, out var item))
        {
            item = Sidearms.Find(path) is { } pistol ? new SidearmCarry { Kind = (int)pistol.Kind } : DeviceClasses.Index.Find(path)?.Create() as Inventory;
            Items[path] = item;
        }
        return item;
    }

    private static VBoxContainer Column(float minimum) => new() { CustomMinimumSize = new Vector2(minimum, 0) };
    private static Label Text(string text, int size, Color? colour = null) => new()
    {
        Text = text, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        LabelSettings = new LabelSettings { Font = UiArt.BodyFont, FontSize = size, FontColor = colour ?? TextColour },
    };
    private static PanelContainer Panel(Control child)
    {
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", Box(Surface, Edge, 16)); panel.AddChild(child); return panel;
    }
    private static StyleBoxFlat Box(Color fill, Color border, int margin = 0) => new()
    {
        BgColor = fill, BorderColor = border, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
    };
    private static void Style(Button b, bool selected)
    {
        b.AddThemeStyleboxOverride("normal", Box(selected ? new Color("213b37") : Ink, selected ? Accent : Edge));
        b.AddThemeStyleboxOverride("hover", Box(new Color("243940"), Accent));
        b.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), Accent));
        b.AddThemeStyleboxOverride("pressed", Box(new Color("294b42"), Accent));
        b.AddThemeStyleboxOverride("disabled", Box(new Color("131a20"), new Color("26323a")));
    }
    private static void Fill(Control parent, Control child, int margin)
    {
        parent.AddChild(child); child.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        child.OffsetLeft = child.OffsetTop = margin; child.OffsetRight = child.OffsetBottom = -margin;
        child.MouseFilter = MouseFilterEnum.Ignore;
    }
}

/// <summary>A focusable equipment card whose content and selection frame are provided by GearMenu.</summary>
public partial class GearTile : Button
{
    public Texture2D? Atlas { get; set; }
    public bool HasIcon { get; set; }
    public Rect2 Region { get; set; }
}
