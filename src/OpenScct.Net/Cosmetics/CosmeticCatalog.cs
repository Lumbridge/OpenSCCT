using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScct.Net.Cosmetics;

public enum CosmeticSide : byte
{
    Spy = 0,
    Merc = 1,
}

/// <summary>Where an item goes. The gun slots are the merc's three gun classes; the pistol is the spy's.</summary>
public enum CosmeticSlot : byte
{
    Body = 0,
    Light = 1,
    Pistol = 2,
    GunAr = 3,
    GunSmg = 4,
    GunShotgun = 5,
    Suppressed = 6,
    Automatic = 7,
    Magnum44 = 8,
    DesertEagle = 9,
    Unsuppressed = 10,
}

public enum CosmeticKind : byte
{
    /// <summary>A colour multiplied over the retail texture.</summary>
    Tint,

    /// <summary>A camo pattern texture made by the content builder from the retail texture.</summary>
    Texture,

    /// <summary>A new hue for a light (goggles, torch, helmet lamp); brightness and saturation stay retail.</summary>
    Hue,
}

/// <summary>
/// One cosmetic. <see cref="Id"/> and <see cref="Bit"/> are stored (accounts, tickets, saved looks) and never change
/// or get reused; new items take new bits.
/// </summary>
public sealed record CosmeticItem(
    string Id,
    int Bit,
    string Name,
    CosmeticSide Side,
    CosmeticSlot Slot,
    CosmeticKind Kind,
    uint Rgb = 0xFFFFFF,
    string Pattern = "",
    float Hue = 0f)
{
    /// <summary>The tint as 0..1 floats.</summary>
    public (float R, float G, float B) Colour => (((Rgb >> 16) & 0xFF) / 255f, ((Rgb >> 8) & 0xFF) / 255f, (Rgb & 0xFF) / 255f);

    /// <summary>
    /// The texture a camo item replaces <paramref name="retailTexture"/> with (content builder output, no extension):
    /// <c>cosmetic_&lt;pattern&gt;_&lt;retail texture&gt;</c>.
    /// </summary>
    public string TextureFor(string retailTexture) => "cosmetic_" + Pattern + "_" + retailTexture;
}

/// <summary>A camo pattern the content builder draws: its id and its colours (dark to light, 0xRRGGBB).</summary>
public sealed record CamoPattern(string Id, string Name, string Shape, IReadOnlyList<uint> Colours);

/// <summary>
/// Every cosmetic (OpenSCCT's own, not retail). The master unlocks them through achievements; the default look of
/// each slot is the retail one and is not an item.
/// </summary>
public static class CosmeticCatalog
{
    /// <summary>The largest bit an item may take (looks pack bit + 1 into a byte per slot).</summary>
    public const int MaxBit = 253;

    /// <summary>Base maps for the map achievements and camos: code prefix, name, palette (dark to light).</summary>
    public static IReadOnlyList<(string Code, string Name, uint[] Palette)> Maps { get; } =
    [
        ("Aqua", "Aquarius", [0x0B2A3A, 0x1F6E7E, 0x7FC8C8]),
        ("Bank", "Bank", [0x2A2418, 0x6E5A2E, 0xC8B27A]),
        ("Cine", "Cinema", [0x2A0F14, 0x7A1E28, 0xC89A5A]),
        ("Clar", "Clarity Soft", [0x1C2430, 0x4A6A8A, 0xC8D8E8]),
        ("Club", "Club House", [0x1E1428, 0x5A2E6E, 0xC87AB2]),
        ("Cpus", "Cpusa", [0x1A1E1A, 0x4E5A4A, 0xA8B49A]),
        ("Deft", "Deftech Belew", [0x14181E, 0x3A4656, 0x9AA8B8]),
        ("Fact", "Factory", [0x261A10, 0x6A4626, 0xB89A6A]),
        ("Ghos", "Ghost Town", [0x2A2620, 0x6E6250, 0xC8B89A]),
        ("Krau", "Krauser Lab", [0x0E1E14, 0x2E6A3E, 0x9AD8A8]),
        ("Miss", "Missile Strike", [0x1E1E14, 0x5A5A2E, 0xB8B87A]),
        ("Moun", "Mount Hospital", [0x1E2428, 0x56646E, 0xD8E0E4]),
        ("Muse", "Museum", [0x2A1E14, 0x7A5A36, 0xD8C29A]),
        ("Offs", "Offshore Oilrig", [0x101418, 0x3A2E1E, 0xD88A2E]),
        ("Orph", "Orphanage", [0x1A1A22, 0x46465A, 0xA8A0B8]),
        ("Pana", "Panama Bank", [0x0E2014, 0x2E5A2A, 0xC8C27A]),
        ("Pola", "Polar Base", [0x2A3440, 0x8AA0B4, 0xF0F4F8]),
        ("Pris", "Prison", [0x202020, 0x585048, 0xB0A890]),
        ("Pyro", "Pyrocryptic", [0x200A06, 0x8A2A0E, 0xF0A030]),
        ("Rive", "River Mall", [0x14202A, 0x2E5A7A, 0xC8D8C2]),
        ("Sanc", "Sanctuary", [0x1E140E, 0x5A3A22, 0xC8A27A]),
        ("Sche", "Schermerhorn", [0x161616, 0x4A3E36, 0xB8A898]),
        ("Shad", "Shadow Moses", [0x1A2228, 0x52606A, 0xE4ECF0]),
        ("Skys", "Skyscraper", [0x10141E, 0x2E3E6E, 0xA8B8E8]),
        ("Slau", "Slaughterhouse", [0x161414, 0x2E2A2A, 0x5A0C10, 0x9A161C]),
        ("Stat", "Station", [0x1A1A14, 0x5A5236, 0xC2B47A]),
        ("Stee", "Steel Squat", [0x14161A, 0x464E5A, 0xA8AEB8]),
        ("Stro", "Stronghold", [0x1A1810, 0x4E4630, 0x9A8E6A]),
        ("Term", "Terminus", [0x101A1E, 0x2E4E56, 0x8AB8B8]),
        ("Vert", "Vertigo Plaza", [0x0E0E1A, 0x3A2E6E, 0xE89A3A]),
        ("Ware", "Warehouse", [0x1E1A14, 0x5A4E3A, 0xA8987A]),
        ("Ziop", "Zioplex", [0x140E1E, 0x46226A, 0x3AE8D8]),
    ];

    /// <summary>The camo patterns: the CoD-style ladder, OpenSCCT's originals and one per base map.</summary>
    public static IReadOnlyList<CamoPattern> Patterns { get; }

    public static IReadOnlyList<CosmeticItem> All { get; }

    private static readonly Dictionary<string, CosmeticItem> s_byId;
    private static readonly CosmeticItem?[] s_byBit;

    /// <summary>The base map code of a map ("SteeE" to "Stee"), or null when it has none.</summary>
    public static string? BaseMap(string? map)
    {
        if (string.IsNullOrEmpty(map) || map.Length < 4)
        {
            return null;
        }

        string prefix = map[..4];
        return Maps.FirstOrDefault(m => string.Equals(m.Code, prefix, StringComparison.OrdinalIgnoreCase)).Code;
    }

    public static CosmeticItem? Find(string? id) => id is not null && s_byId.TryGetValue(id, out CosmeticItem? item) ? item : null;

    public static CosmeticItem? ByBit(int bit) => bit >= 0 && bit < s_byBit.Length ? s_byBit[bit] : null;

    public static CamoPattern? PatternOf(string id) => Patterns.FirstOrDefault(p => p.Id == id);

    /// <summary>The slots a side's look uses.</summary>
    public static IReadOnlyList<CosmeticSlot> SlotsOf(CosmeticSide side) => side == CosmeticSide.Spy
        ? [CosmeticSlot.Body, CosmeticSlot.Light, CosmeticSlot.Pistol]
        : [CosmeticSlot.Body, CosmeticSlot.Light, CosmeticSlot.GunAr, CosmeticSlot.GunSmg, CosmeticSlot.GunShotgun, CosmeticSlot.Unsuppressed, CosmeticSlot.Automatic, CosmeticSlot.Suppressed, CosmeticSlot.Magnum44, CosmeticSlot.DesertEagle];

    public static IEnumerable<CosmeticItem> For(CosmeticSide side, CosmeticSlot slot) => All.Where(i => i.Side == side && i.Slot == slot);

    public static string SlotName(CosmeticSlot slot, CosmeticSide side) => slot switch
    {
        CosmeticSlot.Body => side == CosmeticSide.Spy ? "Suit" : "Armour",
        CosmeticSlot.Light => side == CosmeticSide.Spy ? "Goggle light" : "Torch and lamp",
        CosmeticSlot.Pistol => "Pistol",
        CosmeticSlot.GunAr => "Assault rifle",
        CosmeticSlot.GunSmg => "SMG",
        CosmeticSlot.GunShotgun => "Shotgun",
        CosmeticSlot.Suppressed => "Suppressed pistol",
        CosmeticSlot.Automatic => "Automatic pistol",
        CosmeticSlot.Magnum44 => ".44 Magnum",
        CosmeticSlot.DesertEagle => "Desert Eagle",
        CosmeticSlot.Unsuppressed => "Unsuppressed pistol",
        _ => slot.ToString(),
    };

    /// <summary>What an item is for a player: "Woodland: merc assault rifle", "Blue: spy goggle light".</summary>
    public static string Describe(CosmeticItem item) =>
        item.Name + ": " + (item.Side == CosmeticSide.Spy ? "spy " : "merc ") + SlotName(item.Slot, item.Side).ToLowerInvariant();

    /// <summary>The item names of a list of ids, joined for one line ("" when none are known).</summary>
    public static string DescribeAll(IEnumerable<string> ids) =>
        string.Join(", ", ids.Select(Find).Where(i => i is not null).Select(i => Describe(i!)));

    // Retail goggle glow is a yellow green, about hue 71, and the merc torch orange about hue 30 (RenderK.TorchHue 21 of 255);
    // the game turns the retail colour by the difference (PawnCosmetics.HueTurn).
    private static readonly (string Id, string Name, float Hue)[] s_spyHues =
    [
        ("blue", "Blue", 215f), ("red", "Red", 0f), ("amber", "Amber", 40f), ("violet", "Violet", 275f), ("cyan", "Cyan", 185f),
        ("magenta", "Magenta", 310f),
    ];

    private static readonly (string Id, string Name, float Hue)[] s_mercHues =
    [
        ("blue", "Blue", 215f), ("red", "Red", 0f), ("green", "Green", 120f), ("violet", "Violet", 275f), ("cyan", "Cyan", 185f),
        ("magenta", "Magenta", 310f),
    ];

    private static readonly (string Id, string Name, float Hue)[] s_gadgetSpyHues = [("orange", "Orange", 25f), ("teal", "Teal", 165f)];

    private static readonly (string Id, string Name, float Hue)[] s_gadgetMercHues =
        [("yellow", "Yellow", 55f), ("orange", "Orange", 25f), ("amber", "Amber", 40f)];

    private static readonly (string Id, string Name, uint Rgb)[] s_tints =
    [
        ("urban", "Urban grey", 0x9AA0A8), ("sand", "Sand", 0xD8C49A), ("olive", "Olive", 0x8A9A5A), ("midnight", "Midnight", 0x50586E),
        ("arctic", "Arctic", 0xE8F0F8), ("crimson", "Crimson", 0xC85050), ("navy", "Navy", 0x5A6EA8), ("rust", "Rust", 0xB8784A),
        ("ash", "Ash", 0x787470),
    ];

    static CosmeticCatalog()
    {
        var patterns = new List<CamoPattern>
        {
            new("woodland", "Woodland", "blotch", [0x2A2416, 0x3E5A2A, 0x6E5A36, 0x5A7A3A]),
            new("desert", "Desert", "blotch-soft", [0x6E5236, 0xA8875A, 0xD2B88A]),
            new("digital", "Digital", "pixel", [0x2E3640, 0x56687A, 0x8A9AAE, 0xB4C2D0]),
            new("red_tiger", "Red Tiger", "stripe", [0x0A0A0A, 0xC01818]),
            new("blue_tiger", "Blue Tiger", "stripe", [0x0A0A0A, 0x1C5AD8]),
            new("golden", "Golden", "gold", [0x9A7020, 0xE6B840, 0xFFF2B0]),
            new("pyre", "Pyre", "flames", [0x0A0A0C, 0x6A0C02, 0xC82408, 0xF07010, 0xFFC038, 0xFFF4B8]),
            new("kaido", "Kaido", "livery", [0xE0301E, 0x1A2238, 0xF4F2EE, 0xF2C21A, 0x0C0C0E]),
            new("cryo", "Cryo", "cryo", [0xF0F2F4, 0x0E1014, 0x1E86D8, 0x8EDCFF]),
            new("laser_grid", "Laser Grid", "scanlines", [0x140808, 0xE82020]),
            new("night_ops", "Night Ops", "blotch", [0x0C0C0E, 0x1C1C20, 0x2C2C30]),
            new("static", "Static", "noise", [0x101418, 0x5A6A7A, 0xD8E4F0]),
            new("shockwave", "Shockwave", "arcs", [0x08101E, 0x2A8AFF, 0xC8F0FF]),
            new("breaker", "Breaker", "cracks", [0x2A2A2A, 0x6E6E6A, 0xA8A8A0]),
            new("frag", "Frag", "shrapnel", [0x0E0C0A, 0x3A2A1E, 0xD86A1E]),
            new("lockdown", "Lockdown", "hazard", [0x101010, 0xE8C018]),
            new("smoke", "Smoke Screen", "smoke", [0x2A2C30, 0x5A5E66, 0x9AA0A8, 0xD0D4DA]),
            new("chaff", "Chaff", "chaff", [0x14161A, 0x7A828C, 0xE0E6EE]),
            new("turnabout", "Turnabout", "turnabout", [0x1A0E08, 0x6A2A0E, 0xF07818]),
            new("minefield", "Minefield", "minefield", [0x1A2214, 0x3E4A2A, 0xE8C018]),
            new("voltage", "Voltage", "voltage", [0x0A0A10, 0x2A2A10, 0xF0D820, 0xFFF8C0]),
            new("gadgeteer", "Gadgeteer", "cogs", [0x1A1C20, 0x4A5058, 0x8A929C, 0xC8A050]),
            new("mastercraft", "Mastercraft", "mastercraft", [0x2A1E0A, 0x9A7020, 0xE6B840, 0xFFF2B0]),
            new("flashbang", "Flashbang", "flashes", [0x0C0E12, 0x3A4250, 0xBFD8FF, 0xFFFFFF]),
            new("skeleton", "Skeleton", "skeleton", [0x0A0A0C, 0xE6DFC8, 0x8C846C, 0x030303]),
            new("galaxy", "Galaxy", "galaxy", [0x020206, 0x061436, 0x10328C, 0x5A24B0, 0xD040A8, 0x8AE0FF, 0xF4F6FF]),
            new("inferno", "Inferno", "inferno", [0x0A0A0C, 0x6A0C02, 0xC82408, 0xF07010, 0xFFC038, 0xFFF4B8]),
        };
        foreach ((string code, string name, uint[] palette) in Maps)
        {
            patterns.Add(new("map_" + code.ToLowerInvariant(), name, code == "Slau" ? "splatter" : "blotch", palette));
        }

        Patterns = patterns;

        // Bits are assigned in this order; only ever append (new items at the end).
        var items = new List<CosmeticItem>();
        int bit = 0;
        void Add(string id, string name, CosmeticSide side, CosmeticSlot slot, CosmeticKind kind, uint rgb = 0xFFFFFF, string pattern = "",
            float hue = 0f) => items.Add(new CosmeticItem(id, bit++, name, side, slot, kind, rgb, pattern, hue));

        foreach (CosmeticSide side in new[] { CosmeticSide.Spy, CosmeticSide.Merc })
        {
            string s = side == CosmeticSide.Spy ? "spy" : "merc";
            foreach ((string id, string name, uint rgb) in s_tints)
            {
                Add($"{s}.body.{id}", name, side, CosmeticSlot.Body, CosmeticKind.Tint, rgb);
            }

            foreach ((string id, string name, float hue) in side == CosmeticSide.Spy ? s_spyHues : s_mercHues)
            {
                Add($"{s}.light.{id}", name, side, CosmeticSlot.Light, CosmeticKind.Hue, hue: hue);
            }

            string[] bodyCamos = side == CosmeticSide.Spy
                ? ["woodland", "desert", "digital", "red_tiger", "blue_tiger", "night_ops", "static", "breaker"]
                : ["woodland", "desert", "digital", "red_tiger", "blue_tiger", "laser_grid", "frag", "lockdown"];
            foreach (string camo in bodyCamos)
            {
                string drawn = SuitPattern(side, camo);
                Add($"{s}.body.{camo}", PatternName(patterns, drawn), side, CosmeticSlot.Body, CosmeticKind.Texture, pattern: drawn);
            }
        }

        foreach (string camo in new[] { "woodland", "desert", "digital", "red_tiger", "blue_tiger", "static", "shockwave", "golden" })
        {
            Add($"spy.pistol.{camo}", PatternName(patterns, camo), CosmeticSide.Spy, CosmeticSlot.Pistol, CosmeticKind.Texture, pattern: camo);
        }

        foreach ((CosmeticSlot slot, string gun) in new[] { (CosmeticSlot.GunAr, "ar"), (CosmeticSlot.GunSmg, "smg"), (CosmeticSlot.GunShotgun, "shotgun") })
        {
            foreach (string camo in new[] { "woodland", "desert", "digital", "red_tiger", "blue_tiger", "golden", "laser_grid", "frag" })
            {
                Add($"merc.{gun}.{camo}", PatternName(patterns, camo), CosmeticSide.Merc, slot, CosmeticKind.Texture, pattern: camo);
            }
        }

        foreach ((string code, string name, _) in Maps)
        {
            string camo = "map_" + code.ToLowerInvariant();
            Add($"spy.body.{camo}", name, CosmeticSide.Spy, CosmeticSlot.Body, CosmeticKind.Texture, pattern: camo);
            Add($"merc.body.{camo}", name, CosmeticSide.Merc, CosmeticSlot.Body, CosmeticKind.Texture, pattern: camo);
        }

        // The painted gun skins (OpenSCCT's own): the spy pistol and every merc gun.
        foreach (string camo in new[] { "pyre", "kaido", "cryo" })
        {
            Add($"spy.pistol.{camo}", PatternName(patterns, camo), CosmeticSide.Spy, CosmeticSlot.Pistol, CosmeticKind.Texture, pattern: camo);
            foreach ((CosmeticSlot slot, string gun) in new[] { (CosmeticSlot.GunAr, "ar"), (CosmeticSlot.GunSmg, "smg"), (CosmeticSlot.GunShotgun, "shotgun") })
            {
                Add($"merc.{gun}.{camo}", PatternName(patterns, camo), CosmeticSide.Merc, slot, CosmeticKind.Texture, pattern: camo);
            }
        }

        // The gadget achievement rewards (OpenSCCT's own): new light hues, suits, pistols, armour and merc guns.
        foreach ((string id, string name, float hue) in s_gadgetSpyHues)
        {
            Add($"spy.light.{id}", name, CosmeticSide.Spy, CosmeticSlot.Light, CosmeticKind.Hue, hue: hue);
        }

        foreach ((string id, string name, float hue) in s_gadgetMercHues)
        {
            Add($"merc.light.{id}", name, CosmeticSide.Merc, CosmeticSlot.Light, CosmeticKind.Hue, hue: hue);
        }

        foreach (string camo in new[] { "smoke", "chaff", "turnabout", "minefield", "gadgeteer", "mastercraft" })
        {
            string drawn = SuitPattern(CosmeticSide.Spy, camo);
            Add($"spy.body.{camo}", PatternName(patterns, drawn), CosmeticSide.Spy, CosmeticSlot.Body, CosmeticKind.Texture, pattern: drawn);
        }

        foreach (string camo in new[] { "chaff", "turnabout", "gadgeteer", "smoke", "mastercraft" })
        {
            Add($"spy.pistol.{camo}", PatternName(patterns, camo), CosmeticSide.Spy, CosmeticSlot.Pistol, CosmeticKind.Texture, pattern: camo);
        }

        foreach (string camo in new[] { "voltage", "minefield", "gadgeteer", "mastercraft" })
        {
            Add($"merc.body.{camo}", PatternName(patterns, camo), CosmeticSide.Merc, CosmeticSlot.Body, CosmeticKind.Texture, pattern: camo);
            foreach ((CosmeticSlot slot, string gun) in new[] { (CosmeticSlot.GunAr, "ar"), (CosmeticSlot.GunSmg, "smg"), (CosmeticSlot.GunShotgun, "shotgun") })
            {
                Add($"merc.{gun}.{camo}", PatternName(patterns, camo), CosmeticSide.Merc, slot, CosmeticKind.Texture, pattern: camo);
            }
        }

        // The flash-bang achievement reward (OpenSCCT's own): a spy suit and pistol.
        Add("spy.body.flashbang", PatternName(patterns, "flashbang"), CosmeticSide.Spy, CosmeticSlot.Body, CosmeticKind.Texture, pattern: "flashbang");
        Add("spy.pistol.flashbang", PatternName(patterns, "flashbang"), CosmeticSide.Spy, CosmeticSlot.Pistol, CosmeticKind.Texture,
            pattern: "flashbang");

        // The skeleton (OpenSCCT's own): bones drawn where they lie on the body, for both sides.
        Add("spy.body.skeleton", PatternName(patterns, "skeleton"), CosmeticSide.Spy, CosmeticSlot.Body, CosmeticKind.Texture, pattern: "skeleton");
        Add("merc.body.skeleton", PatternName(patterns, "skeleton"), CosmeticSide.Merc, CosmeticSlot.Body, CosmeticKind.Texture, pattern: "skeleton");

        // Galaxy on the guns (OpenSCCT's own): the spy pistol and every merc gun, given with the Galaxy suits.
        Add("spy.pistol.galaxy", PatternName(patterns, "galaxy"), CosmeticSide.Spy, CosmeticSlot.Pistol, CosmeticKind.Texture, pattern: "galaxy");
        foreach ((CosmeticSlot slot, string gun) in new[] { (CosmeticSlot.GunAr, "ar"), (CosmeticSlot.GunSmg, "smg"), (CosmeticSlot.GunShotgun, "shotgun") })
        {
            Add($"merc.{gun}.galaxy", PatternName(patterns, "galaxy"), CosmeticSide.Merc, slot, CosmeticKind.Texture, pattern: "galaxy");
        }

        foreach (var pistol in OpenScct.Core.Engine.Sidearms.All)
            foreach (string camo in new[] { "night_ops", "cryo", "woodland", "desert", "digital", "red_tiger", "blue_tiger", "golden" })
                Add($"merc.{pistol.Id}.{camo}", PatternName(patterns, camo), CosmeticSide.Merc,
                    (CosmeticSlot)(6 + (int)pistol.Kind), CosmeticKind.Texture, pattern: camo);

        if (bit - 1 > MaxBit)
        {
            throw new InvalidOperationException("Too many cosmetics for the look's byte per slot.");
        }

        All = items;
        s_byId = items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        s_byBit = new CosmeticItem?[bit];
        foreach (CosmeticItem i in items)
        {
            s_byBit[i.Bit] = i;
        }
    }

    private static string PatternName(List<CamoPattern> patterns, string id) => patterns.First(p => p.Id == id).Name;

    /// <summary>
    /// The pattern a suit item draws: the spy's Breaker and Chaff suits (too like Smoke Screen and Night Ops) and the
    /// merc's Frag and Digital suits (too like Voltage and Gadgeteer) became Inferno and Galaxy, keeping their ids, bits
    /// and achievements; every other suit draws its own id's pattern.
    /// </summary>
    private static string SuitPattern(CosmeticSide side, string camo) => (side, camo) switch
    {
        (CosmeticSide.Spy, "breaker") => "inferno",
        (CosmeticSide.Spy, "chaff") => "galaxy",
        (CosmeticSide.Merc, "frag") => "inferno",
        (CosmeticSide.Merc, "digital") => "galaxy",
        _ => camo,
    };
}
