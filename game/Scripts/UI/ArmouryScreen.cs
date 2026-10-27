using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;
using OpenScctGame.Options;
using OpenScctGame.Render.Characters;

namespace OpenScctGame.UI;

/// <summary>
/// The Armoury (OpenSCCT's own): the spy's and the merc's cosmetics per slot (suit or armour, light colour, pistol or
/// each merc gun). Locked items say which achievement unlocks them; picking one saves it to the settings, which follow
/// the account. Servers check the choice against the account's unlocks when the player joins.
/// </summary>
public partial class ArmouryScreen : MenuPage
{
    private const int ThumbSize = 40;

    private VBoxContainer? _content;
    private AccountService? _service;
    private Task<AccountInfo>? _refresh;
    private CosmeticSide _side = CosmeticSide.Spy;
    private readonly Dictionary<string, Texture2D?> _thumbs = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        Name = "Armoury";
        BuildFrame("Armoury", 900f);
        _service = AccountService.Get(GetTree());
        var sides = new HBoxContainer { Name = "Sides" };
        sides.AddChild(UiStyle.MenuButton("Spy", () => ShowSide(CosmeticSide.Spy), "SideSpy"));
        sides.AddChild(UiStyle.MenuButton("Mercenary", () => ShowSide(CosmeticSide.Merc), "SideMerc"));
        Body.AddChild(sides);
        _content = new VBoxContainer { Name = "Content" };
        _content.AddThemeConstantOverride("separation", 8);
        Body.AddChild(_content);
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();
        if (AccountService.HasMaster && AccountSettings.Current.SignedIn)
        {
            _refresh = AccountService.Call(c => c.AccountAsync());
        }

        ShowSide(_side);
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

    /// <summary>The menu's button for this page.</summary>
    public static Button MenuButton(Node from) =>
        UiStyle.MenuButton("Armoury", () => FrontEnd.ShowArmoury(from.GetTree()), "Armoury");

    private void ShowSide(CosmeticSide side)
    {
        _side = side;
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
                ? "Sign in on the Account page and earn achievements on official servers to unlock these."
                : "Cosmetics are unlocked by achievements on an official master server; none is set up."));
        }

        CosmeticLook look = CosmeticLook.FromIds(side, Chosen(side));
        foreach (CosmeticSlot slot in CosmeticCatalog.SlotsOf(side))
        {
            VBoxContainer panel = Panel(_content, "Slot" + slot, CosmeticCatalog.SlotName(slot, side));
            var grid = new GridContainer { Name = "Items", Columns = 3 };
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
        b.CustomMinimumSize = new Vector2(280f, 44f);
        b.Alignment = HorizontalAlignment.Left;
        b.Disabled = !open;
        b.Icon = item is null ? null : Thumb(item);
        b.ExpandIcon = false;
        b.TooltipText = open ? (item is null ? "The original look." : CosmeticCatalog.Describe(item))
            : "Locked. Unlocked by: " + (from is null ? "an achievement" : from.Title + " (" + from.Description + ")");
        if (chosen)
        {
            b.Modulate = UiStyle.MenuFocus;
        }

        return b;
    }

    private void Choose(CosmeticSide side, CosmeticSlot slot, CosmeticItem? item)
    {
        CosmeticLook look = CosmeticLook.FromIds(side, Chosen(side)).With(slot, item);
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
        ShowSide(side);
    }

    private static string[] Chosen(CosmeticSide side) =>
        side == CosmeticSide.Spy ? SettingsStore.Current.CosmeticsSpy : SettingsStore.Current.CosmeticsMerc;

    /// <summary>A small picture of the item: its camo on the slot's texture, or a colour swatch.</summary>
    private Texture2D? Thumb(CosmeticItem item)
    {
        if (_thumbs.TryGetValue(item.Id, out Texture2D? cached))
        {
            return cached;
        }

        Image? image = null;
        switch (item.Kind)
        {
            case CosmeticKind.Texture when CamoTexture(item) is { } path && File.Exists(path):
                image = Image.LoadFromFile(path);
                image?.Resize(ThumbSize, ThumbSize, Image.Interpolation.Bilinear);
                break;
            case CosmeticKind.Tint:
                (float r, float g, float b) = item.Colour;
                image = Swatch(new Color(r, g, b));
                break;
            case CosmeticKind.Hue:
                image = Swatch(Color.FromHsv(item.Hue / 360f, 0.85f, 1f));
                break;
        }

        Texture2D? texture = image is null ? null : ImageTexture.CreateFromImage(image);
        _thumbs[item.Id] = texture;
        return texture;
    }

    private static Image Swatch(Color colour)
    {
        Image image = Image.CreateEmpty(ThumbSize, ThumbSize, false, Image.Format.Rgba8);
        image.Fill(colour);
        return image;
    }

    /// <summary>The built camo texture that shows the item best (the content builder's file for the slot's main texture).</summary>
    private static string? CamoTexture(CosmeticItem item)
    {
        (string package, string stem) = (item.Slot, item.Side) switch
        {
            (CosmeticSlot.Body, CosmeticSide.Spy) => ("SPerso", "SPersoTextures_Shadw_agent"),
            (CosmeticSlot.Body, CosmeticSide.Merc) => ("SPerso", "SPersoTextures_DEF_01_A_Body"),
            (CosmeticSlot.Pistol, _) => ("SWeaponFirst", "SWeaponTex_p190"),
            (CosmeticSlot.GunAr, _) => ("SWeaponFirst", "SWeaponTex_M160"),
            (CosmeticSlot.GunSmg, _) => ("SWeaponFirst", "SWeaponTex_Hk81"),
            (CosmeticSlot.GunShotgun, _) => ("SWeaponFirst", "SWeaponTex_shotgun"),
            _ => (string.Empty, string.Empty),
        };
        return package.Length == 0
            ? null
            : Path.Combine(SkeletalModel.DefaultSharedRoot(), "chars", package, "textures", "cosmetic_" + item.Pattern + "_" + stem + ".png");
    }

    private static Label Note(string text) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(820f, 0f), Modulate = UiStyle.MenuFocus,
    };
}
