using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.UI;

/// <summary>
/// The gear picker (spec/additions/gear-change.md), drawn as the original's two PC pages:
/// <list type="bullet">
/// <item><c>GUI.Edit_Profile</c> ("Equipment"): the side's portrait twice, dimmed; the four gadget slots as diamond
/// buttons in a cross with the pad-button hints between them; "Quick Use 1/2" by the spy's first two slots; the focused
/// gadget's long name and description; the weapon in a diamond with its name (a merc's cycles through the team's
/// weapons). Page buttons Cancel, Reset and OK.</item>
/// <item><c>GUI.Edit_Gadgets</c> ("Gadgets", opened from a slot): the team's eight options as square buttons in two
/// rows, "Equipped" under the ones a slot holds, banned ones greyed out, and the focused one's name, description
/// and "Quick use". Picking one sets the slot (a slot that held it swaps) and goes back.</item>
/// </list>
/// Positions are the pages' WinLeft/WinTop/WinWidth/WinHeight over a 4:3 area centred on the screen; icons are the
/// HUD atlases' 64 x 64 tiles at each class's IconU/IconV. The Original look draws the converted frames (GUIContent
/// MN_losange, MN_cadre06, MN_cadre01, fond02, the portraits; Menu_PC bouton01-04); the Modern look draws the same
/// shapes in its own colours. The line stating when new gear goes on is our addition.
/// </summary>
public partial class GearMenu : MenuPage
{
    [Provenance(ProvenanceKind.Decoded, "GUI pages are laid out on a 640 x 480 page")]
    private const float PageWidth = 640f, PageHeight = 480f;

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Profile.RefreshGadget/LoadProfile: TexWidth = TexHeight = 64")]
    private const float ProfileTile = 64f;

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Gadgets.HandleParameters: TexLeft = IconU + 1, TexTop = IconV + 1, 62 x 62")]
    private const float GridTile = 62f;

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Profile defaults Gadget1..4 WinLeft/WinTop (0.25 x 0.25 each)")]
    private static readonly Vector2[] SlotAt = [new(0.275f, 0.1f), new(0.12f, 0.245f), new(0.43f, 0.245f), new(0.275f, 0.39f)];

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Gadgets defaults Gadget1..8 WinLeft/WinTop (0.14 x 0.14 each)")]
    private static readonly Vector2[] OptionAt =
    [
        new(0.14f, 0.19f), new(0.3333f, 0.19f), new(0.5266f, 0.19f), new(0.72f, 0.19f),
        new(0.14f, 0.4f), new(0.3333f, 0.4f), new(0.5266f, 0.4f), new(0.72f, 0.4f),
    ];

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Profile defaults ImgButtonA/Y/X/B (Menu_PC bouton04/02/01/03), 0.05 x 0.05")]
    private static readonly (string Texture, Vector2 At)[] PadHints =
    [
        ("bouton04", new(0.375f, 0.39f)), ("bouton02", new(0.325f, 0.345f)), ("bouton01", new(0.375f, 0.3f)), ("bouton03", new(0.425f, 0.345f)),
    ];

    [Provenance(ProvenanceKind.Decoded, "GUI.Edit_Profile defaults Mesh, Mesh2 ImageColor (128, 128, 128, 120)")]
    private static readonly Color PortraitTint = new(128 / 255f, 128 / 255f, 128 / 255f, 120 / 255f);

    private static readonly UcClassIndex Classes = new(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);
    private static readonly Dictionary<string, Inventory?> Items = new(StringComparer.OrdinalIgnoreCase);

    private readonly GearMenuModel? _model;
    private Control? _layer;
    private Button? _reset;
    private Button? _ok;
    private bool _grid;
    private int _slot;
    private Label? _name;
    private Label? _description;
    private Label? _quickUse;

    public GearMenu(GearMenuModel? model)
    {
        Name = "GearMenu";
        _model = model;
    }

    /// <summary>OK: the model holds the choice to save.</summary>
    public event Action<GearMenuModel>? Applied;

    /// <summary>Cancel on the Equipment page.</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        BuildFrame(GearMenuModel.Title);
        if (UiStyle.MenuTexture("GUIContent", "fond02") is { } fond)
        {
            // Edit_Profile.Background: fond02 in place of the pages' usual fond.
            var background = new TextureRect
            {
                Name = "Fond02", Texture = fond, AnchorRight = 1f, AnchorBottom = 1f, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(background);
            MoveChild(background, 1);
        }

        PageButton(GearMenuModel.Cancel, OnCancel, "Cancel");
        _reset = PageButton(GearMenuModel.ResetCaption, OnReset, "Reset");
        _ok = PageButton(GearMenuModel.Ok, () =>
        {
            if (_model is not null)
            {
                Applied?.Invoke(_model);
            }
        }, "OK");

        _layer = new Control { Name = "Page", MouseFilter = MouseFilterEnum.Ignore };
        _layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_layer);
        Resized += Rebuild;
        Rebuild();
    }

    /// <summary>
    /// The page handles Escape on Equipment itself (Cancel), as over the Lobby Room; in a match the In-Game Menu does.
    /// </summary>
    public bool OwnKeys { get; init; }

    /// <summary>Escape on the Gadgets page goes back to Equipment (on Equipment: Cancel when <see cref="OwnKeys"/>).</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            return;
        }

        if (_grid)
        {
            ShowEquipment();
            GetViewport().SetInputAsHandled();
        }
        else if (OwnKeys)
        {
            GetViewport().SetInputAsHandled();
            Closed?.Invoke();
        }
    }

    private void OnCancel()
    {
        if (_grid)
        {
            ShowEquipment();
            return;
        }

        Closed?.Invoke();
    }

    private void OnReset()
    {
        _model?.Reset();
        Rebuild();
    }

    private void ShowEquipment()
    {
        _grid = false;
        TitleText = GearMenuModel.Title;
        Rebuild();
    }

    private void ShowGadgets(int slot)
    {
        _grid = true;
        _slot = slot;
        TitleText = GearMenuModel.GadgetsTitle;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_layer is null)
        {
            return;
        }

        foreach (Node child in _layer.GetChildren())
        {
            _layer.RemoveChild(child);
            child.QueueFree();
        }

        _name = _description = _quickUse = null;
        if (_reset is not null)
        {
            _reset.Visible = !_grid && _model is not null;
        }

        if (_ok is not null)
        {
            _ok.Visible = !_grid && _model is not null;
        }

        if (_model is null)
        {
            Text("Join a side to choose your gear.", Page(0.02f, 0.45f, 0.96f, 32f / PageHeight), 15f, HorizontalAlignment.Center, UiStyle.MenuText);
            return;
        }

        if (_grid)
        {
            BuildGadgets(_model);
        }
        else
        {
            BuildEquipment(_model);
        }
    }

    // ---- Edit_Profile ------------------------------------------------------------------------------------------

    private void BuildEquipment(GearMenuModel m)
    {
        if (Texture(m.Portrait) is { } portrait)
        {
            foreach (float left in new[] { 0.05f, 0.57f })
            {
                // Edit_Profile Mesh and Mesh2: the side's MaterialOfMesh[0], ISTY_Scaled, dimmed.
                Rect2 r = Page(left, 0.1f, 0.4f, 0.53f);
                _layer!.AddChild(new TextureRect
                {
                    Texture = portrait, Position = r.Position, Size = r.Size, Modulate = PortraitTint,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                });
            }
        }

        if (UiStyle.IsOriginal)
        {
            foreach ((string texture, Vector2 at) in PadHints)
            {
                if (UiStyle.MenuTexture("Menu_PC", texture) is { } hint)
                {
                    Rect2 r = Page(at.X, at.Y, 0.05f, 0.05f);
                    _layer!.AddChild(new TextureRect
                    {
                        Texture = hint, Position = r.Position, Size = r.Size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
                    });
                }
            }
        }

        // Edit_Profile.refreshQuickUse: a spy's first two slots say when their gadget can be thrown quickly.
        if (m.Spy)
        {
            if (Item(m.Gadgets[0]) is { CanQuickLaunch: true })
            {
                Text(GearMenuModel.QuickUse1, Page(0.125f, 0.15f, 0.2f, 32f / PageHeight), 11f, HorizontalAlignment.Right, UiStyle.MenuFocus);
            }

            if (Item(m.Gadgets[1]) is { CanQuickLaunch: true })
            {
                Text(GearMenuModel.QuickUse2, Page(0.025f, 0.25f, 0.2f, 32f / PageHeight), 11f, HorizontalAlignment.Right, UiStyle.MenuFocus);
            }
        }

        GearTile? first = null;
        for (int i = 0; i < SlotAt.Length; i++)
        {
            int slot = i;
            string path = m.Gadgets[i];
            GearTile tile = Tile(true, Page(SlotAt[i].X, SlotAt[i].Y, 0.25f, 0.25f), GadgetAtlas(), Item(path), 0f, ProfileTile);
            tile.Name = $"Slot{i + 1}";
            tile.Disabled = slot >= m.Slots;
            tile.Pressed += () => ShowGadgets(slot);
            tile.FocusEntered += () => ShowItem(path, false);
            if (slot == _slot && !tile.Disabled)
            {
                first = tile;
            }

            first ??= tile.Disabled ? null : tile;
        }

        // The focused gadget's name and description (Edit_Profile NameGadget, DescGadget).
        _name = Text("", Page(0.02f, 0.63f, 0.96f, 32f / PageHeight), 15f, HorizontalAlignment.Left, UiStyle.MenuFocus);
        _description = Text("", Page(0.02f, 0.69f, 0.7f, 96f / PageHeight), 11f, HorizontalAlignment.Left, UiStyle.MenuText);
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _description.VerticalAlignment = VerticalAlignment.Top;

        // The weapon (Edit_Profile Weapon, WeaponImage): a spy's is fixed; a merc's cycles through the team's.
        string weapon = m.Spy ? OpenScct.Game.Equipment.Loadouts.SpyWeapon.Path ?? "" : m.ChosenWeapon;
        Text(Item(weapon)?.ItemName is { Length: > 0 } wn ? wn : GearMenuModel.NameOf(weapon), Page(0.625f, 0.55f, 0.4f, 32f / PageHeight), 13f,
            HorizontalAlignment.Center, UiStyle.MenuFocus);
        GearTile weaponTile = Tile(true, Page(0.7f, 0.38f, 0.25f, 0.2f), WeaponAtlas(), Item(weapon), 0f, ProfileTile);
        weaponTile.Name = "Weapon";
        weaponTile.Disabled = m.Spy || m.Weapons.Count < 2;
        weaponTile.Pressed += () =>
        {
            m.CycleWeapon(1);
            Rebuild();
            _layer?.GetNodeOrNull<Control>("Weapon")?.CallDeferred(Control.MethodName.GrabFocus);
        };

        // Our addition: when new gear goes on.
        Label rule = Text(m.Rule, Page(0.02f, 0.84f, 0.96f, 40f / PageHeight), 10f, HorizontalAlignment.Center, UiStyle.Dim);
        rule.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        ShowItem(m.Gadgets[Math.Clamp(_slot, 0, 3)], false);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    // ---- Edit_Gadgets ------------------------------------------------------------------------------------------

    private void BuildGadgets(GearMenuModel m)
    {
        GearTile? first = null;
        for (int i = 0; i < OptionAt.Length && i < m.Options.Count; i++)
        {
            string option = m.Options[i];
            Vector2 at = OptionAt[i];
            GearTile tile = Tile(false, Page(at.X, at.Y, 0.14f, 0.14f), GadgetAtlas(), Item(option), 1f, GridTile);
            tile.Name = $"Option{i + 1}";
            tile.Disabled = !m.IsAllowed(option);
            tile.FocusMode = tile.Disabled ? FocusModeEnum.None : FocusModeEnum.All;
            tile.Pressed += () =>
            {
                m.Choose(_slot, option);
                ShowEquipment();
            };
            tile.FocusEntered += () => ShowItem(option, true);
            if (m.IsEquipped(option))
            {
                Text(GearMenuModel.Equipped, Page(at.X, at.Y + 0.14f, 0.15f, 0.05f), 11f, HorizontalAlignment.Center, UiStyle.MenuFocus);
            }

            if (!tile.Disabled && (first is null || string.Equals(option, m.Gadgets[_slot], StringComparison.OrdinalIgnoreCase)))
            {
                first = tile;
            }
        }

        // Edit_Gadgets GadName, QuickUse, GadDesc.
        _name = Text("", Page(0.02f, 0.62f, 0.96f, 32f / PageHeight), 15f, HorizontalAlignment.Center, UiStyle.MenuFocus);
        _quickUse = Text(GearMenuModel.QuickUse, Page(0.3f, 0.65f, 0.4f, 32f / PageHeight), 11f, HorizontalAlignment.Center, UiStyle.MenuText);
        _description = Text("", Page(0.02f, 0.7f, 0.96f, 96f / PageHeight), 11f, HorizontalAlignment.Center, UiStyle.MenuText);
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _description.VerticalAlignment = VerticalAlignment.Top;
        ShowItem(m.Gadgets[_slot], true);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    // ---- shared --------------------------------------------------------------------------------------------------

    /// <summary>Edit_Profile.RefreshGadgetName / Edit_Gadgets.ButtonActivate: the item's long name and description.</summary>
    private void ShowItem(string path, bool quickUse)
    {
        Inventory? item = Item(path);
        if (_name is not null)
        {
            _name.Text = item?.ItemLongName is { Length: > 0 } longName ? longName : GearMenuModel.NameOf(path);
        }

        if (_description is not null)
        {
            _description.Text = item?.ItemDescription ?? "";
        }

        if (_quickUse is not null)
        {
            _quickUse.Visible = quickUse && item is { CanQuickLaunch: true };
        }
    }

    /// <summary>A rectangle of the 640 x 480 page (fractions), on a 4:3 area centred on the screen.</summary>
    private Rect2 Page(float left, float top, float width, float height)
    {
        Vector2 screen = Size;
        float h = screen.Y;
        float w = Math.Min(screen.X, h * PageWidth / PageHeight);
        float x0 = (screen.X - w) * 0.5f;
        return new Rect2(x0 + left * w, top * h, width * w, height * h);
    }

    /// <summary>Page pixels to screen pixels (the page's 480 rows fill the screen height).</summary>
    private float PageScale => Size.Y / PageHeight;

    private GearTile Tile(bool diamond, Rect2 rect, Texture2D? atlas, Inventory? item, float inset, float tile)
    {
        var t = new GearTile
        {
            Diamond = diamond, Position = rect.Position, Size = rect.Size, Atlas = atlas, IconScale = PageScale,
            Region = item is null ? new Rect2() : new Rect2(item.IconU + inset, item.IconV + inset, tile, tile),
            HasIcon = item is not null,
        };
        _layer!.AddChild(t);
        return t;
    }

    private Label Text(string text, Rect2 rect, float pageSize, HorizontalAlignment align, Color colour)
    {
        var label = new Label
        {
            Text = text, Position = rect.Position, Size = rect.Size, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = false,
            LabelSettings = new LabelSettings { FontSize = Math.Max(10, (int)MathF.Round(pageSize * PageScale)), FontColor = colour },
        };
        _layer!.AddChild(label);
        return label;
    }

    /// <summary>SHud Tex_All_Gadgets (the HUD's atlas: shown in both looks).</summary>
    private static Texture2D? GadgetAtlas() => UiStyle.Assets?.Texture("SHudTextures", "Icones_Gadgets");

    /// <summary>SHud Tex_All_Weapons.</summary>
    private static Texture2D? WeaponAtlas() => UiStyle.Assets?.Texture("SHudTextures", "Icones_Weapons");

    /// <summary>
    /// A material path such as GUIContent.New_Menu.MN_ATTperso as a converted texture (package, last name). Character
    /// art, like the icons, so both looks show it.
    /// </summary>
    private static Texture2D? Texture(string path)
    {
        string[] parts = path.Split('.');
        return parts.Length >= 2 ? UiStyle.Assets?.Texture(parts[0], parts[^1]) : null;
    }

    /// <summary>The class defaults of an inventory class (icon, names, description, quick launch), or null.</summary>
    private static Inventory? Item(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (!Items.TryGetValue(path, out Inventory? item))
        {
            item = Classes.Find(path)?.Create() as Inventory;
            Items[path] = item;
        }

        return item;
    }
}

/// <summary>
/// A GUIGFXButton of the gear pages: the style's frame (DiamondButton: MN_losange; SquareButton: MN_cadre06, MN_cadre01
/// when focused) tinted by the menu state, with the item's icon tile centred at its page size.
/// </summary>
public partial class GearTile : Button
{
    [Provenance(ProvenanceKind.Decoded, "GUI.GUIStyles defaults ImgColors(0, 1) (69, 85, 69); ImgColors(2) white")]
    private static readonly Color OriginalNormal = new(69 / 255f, 85 / 255f, 69 / 255f);

    [Provenance(ProvenanceKind.Decoded, "GUI.STY_DiamondButton, STY_SquareButton defaults ImgColors(4) (80, 80, 80, 64)")]
    private static readonly Color DisabledTint = new(80 / 255f, 80 / 255f, 80 / 255f, 64 / 255f);

    public GearTile()
    {
        Flat = true;
        FocusMode = FocusModeEnum.All;
        foreach (string box in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
        {
            AddThemeStyleboxOverride(box, new StyleBoxEmpty());
        }

        // GUI controls take the focus when the pointer is over them (Edit_Profile and Edit_Gadgets act on focus).
        MouseEntered += () =>
        {
            if (!Disabled)
            {
                GrabFocus();
            }
        };
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    /// <summary>DiamondButton (Edit_Profile) or SquareButton (Edit_Gadgets).</summary>
    public bool Diamond { get; set; }

    public Texture2D? Atlas { get; set; }

    public Rect2 Region { get; set; }

    public bool HasIcon { get; set; }

    /// <summary>Screen pixels per page pixel.</summary>
    public float IconScale { get; set; } = 1f;

    public override void _Draw()
    {
        bool focus = HasFocus() && !Disabled;
        Color tint = Disabled ? DisabledTint : focus ? (UiStyle.IsOriginal ? Colors.White : UiStyle.MenuFocus) :
            UiStyle.IsOriginal ? OriginalNormal : UiStyle.MenuText;
        var rect = new Rect2(Vector2.Zero, Size);
        string frameName = Diamond ? "MN_losange" : focus ? "MN_cadre01" : "MN_cadre06";
        if (UiStyle.MenuTexture("GUIContent", frameName) is { } frame)
        {
            DrawTextureRect(frame, rect, false, tint);
        }
        else if (Diamond)
        {
            var c = Size * 0.5f;
            Vector2[] points = [new(c.X, 0f), new(Size.X, c.Y), new(c.X, Size.Y), new(0f, c.Y), new(c.X, 0f)];
            DrawColoredPolygon(points[..4], tint with { A = tint.A * 0.18f });
            DrawPolyline(points, tint, 2f, true);
        }
        else
        {
            DrawRect(rect, tint with { A = tint.A * 0.18f });
            DrawRect(rect, tint, false, 2f);
        }

        if (HasIcon && Atlas is not null)
        {
            Vector2 size = Region.Size * IconScale;
            Color icon = Disabled ? DisabledTint : Colors.White;
            DrawTextureRectRegion(Atlas, new Rect2((Size - size) * 0.5f, size), Region, icon);
        }
    }
}
