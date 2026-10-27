using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenScct.Net.Cosmetics;

/// <summary>
/// What a player wears on one side: one item per slot, packed as a byte per <see cref="CosmeticSlot"/> holding the
/// item's bit + 1 (0 is the retail look). Sent at join as <c>?SvmLook=</c> (hex) and replicated to every client.
/// </summary>
public readonly record struct CosmeticLook(long Packed)
{
    public static CosmeticLook Default => new(0);

    public CosmeticItem? this[CosmeticSlot slot] => CosmeticCatalog.ByBit((int)((Packed >> (8 * (int)slot)) & 0xFF) - 1);

    public CosmeticLook With(CosmeticSlot slot, CosmeticItem? item)
    {
        int shift = 8 * (int)slot;
        long value = item is null ? 0 : item.Bit + 1;
        return new CosmeticLook((Packed & ~(0xFFL << shift)) | (value << shift));
    }

    /// <summary>The look from item ids (unknown ids, and items of another side or slot, are left out).</summary>
    public static CosmeticLook FromIds(CosmeticSide side, IEnumerable<string> ids)
    {
        CosmeticLook look = Default;
        foreach (string id in ids)
        {
            if (CosmeticCatalog.Find(id) is { } item && item.Side == side)
            {
                look = look.With(item.Slot, item);
            }
        }

        return look;
    }

    /// <summary>
    /// The look with every slot that is not the side's, not of the right item, or not unlocked set back to retail.
    /// </summary>
    public CosmeticLook Validate(CosmeticSide side, CosmeticUnlocks unlocks)
    {
        CosmeticLook clean = Default;
        foreach (CosmeticSlot slot in CosmeticCatalog.SlotsOf(side))
        {
            if (this[slot] is { } item && item.Side == side && item.Slot == slot && unlocks.Has(item))
            {
                clean = clean.With(slot, item);
            }
        }

        return clean;
    }

    public string ToHex() => Packed.ToString("x", CultureInfo.InvariantCulture);

    public static CosmeticLook ParseHex(string? text) =>
        long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long packed) ? new CosmeticLook(packed) : Default;

    /// <summary>The low and high halves, for replication as two ints.</summary>
    public (int Low, int High) Split() => ((int)(Packed & 0xFFFFFFFFL), (int)(Packed >> 32));

    public static CosmeticLook Join(int low, int high) => new(((long)high << 32) | (uint)low);
}

/// <summary>A set of unlocked items by bit, as the master's signed ticket carries it.</summary>
public sealed class CosmeticUnlocks
{
    private readonly byte[] _bits;

    public CosmeticUnlocks(byte[]? bits = null)
    {
        _bits = bits ?? new byte[(CosmeticCatalog.MaxBit + 8) / 8];
    }

    public static CosmeticUnlocks None => new();

    public static CosmeticUnlocks Of(IEnumerable<string> ids)
    {
        var u = new CosmeticUnlocks();
        foreach (string id in ids)
        {
            if (CosmeticCatalog.Find(id) is { } item)
            {
                u._bits[item.Bit / 8] |= (byte)(1 << (item.Bit % 8));
            }
        }

        return u;
    }

    public bool Has(CosmeticItem item) => item.Bit / 8 < _bits.Length && (_bits[item.Bit / 8] & (1 << (item.Bit % 8))) != 0;

    /// <summary>The bytes up to the last set one (what the ticket stores).</summary>
    public byte[] ToBytes()
    {
        int length = _bits.Length;
        while (length > 0 && _bits[length - 1] == 0)
        {
            length--;
        }

        return _bits.AsSpan(0, length).ToArray();
    }
}
