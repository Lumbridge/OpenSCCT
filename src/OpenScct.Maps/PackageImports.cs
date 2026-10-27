using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OpenScct.Maps;

/// <summary>
/// The packages an Unreal package (a map, texture, mesh or sound package of SCCT) imports from: the top-level
/// imports of its import table (<c>Map17_TXT</c>, <c>Amb_Mount</c>, <c>Engine</c>). Reads only the header, the name
/// table and the import table, the way <c>tools/scctpkg/scctpkg/package.py</c> does: an optional zlib container,
/// names XOR-obfuscated by file position unless plain, and SCCT name references (local then global compact index).
/// </summary>
public static class PackageImports
{
    private const uint Tag = 0x9E2A83C1;

    /// <summary>The names of the packages <paramref name="file"/> imports from, sorted, without duplicates.</summary>
    /// <exception cref="InvalidDataException">The file is not a package this reader understands.</exception>
    public static IReadOnlyList<string> RootPackages(string file) => RootPackages(Unwrap(File.ReadAllBytes(file), Path.GetFileName(file)));

    public static IReadOnlyList<string> RootPackages(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            if (data.Length < 36 || BitConverter.ToUInt32(data, 0) != Tag)
            {
                throw new InvalidDataException("not an Unreal package");
            }

            int nameCount = BitConverter.ToInt32(data, 12);
            int nameOffset = BitConverter.ToInt32(data, 16);
            int importCount = BitConverter.ToInt32(data, 28);
            int importOffset = BitConverter.ToInt32(data, 32);
            if (nameCount < 0 || importCount < 0 || nameCount > 1_000_000 || importCount > 1_000_000)
            {
                throw new InvalidDataException("bad table sizes");
            }

            string[] names = ReadNames(data, nameOffset, nameCount);
            var roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int p = importOffset;
            for (int i = 0; i < importCount; i++)
            {
                NameRef(data, ref p);                       // class package
                NameRef(data, ref p);                       // class name
                int outer = BitConverter.ToInt32(data, p);
                p += 4;
                int objectName = NameRef(data, ref p);
                if (outer == 0)
                {
                    if ((uint)objectName >= (uint)names.Length)
                    {
                        throw new InvalidDataException("bad name index in the import table");
                    }

                    roots.Add(names[objectName]);
                }
            }

            return roots.ToList();
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            throw new InvalidDataException("damaged package tables", ex);
        }
    }

    /// <summary>The package bytes, out of the zlib container some packages are stored in.</summary>
    private static byte[] Unwrap(byte[] raw, string what)
    {
        if (raw.Length >= 8 && BitConverter.ToUInt32(raw, 0) != Tag)
        {
            uint uncompressed = BitConverter.ToUInt32(raw, 0);
            uint compressed = BitConverter.ToUInt32(raw, 4);
            if (uncompressed == 0 || uncompressed > int.MaxValue || compressed + 8L > raw.Length)
            {
                throw new InvalidDataException($"{what}: not an Unreal package");
            }

            using var input = new ZLibStream(new MemoryStream(raw, 8, (int)compressed), CompressionMode.Decompress);
            var output = new MemoryStream((int)uncompressed);
            input.CopyTo(output);
            if (output.Length != uncompressed)
            {
                throw new InvalidDataException($"{what}: container size mismatch");
            }

            return output.ToArray();
        }

        return raw;
    }

    private static string[] ReadNames(byte[] d, int offset, int count)
    {
        // Plain tables start with printable text ending in a NUL; the rest are XOR-obfuscated by position.
        int p0 = offset;
        int n0 = Compact(d, ref p0);
        bool plain = n0 is > 1 and <= 1024 && d[p0 + n0 - 1] == 0 && Enumerable.Range(p0, n0 - 1).All(i => d[i] is >= 32 and < 127);
        var names = new string[count];
        int p = offset;
        for (int i = 0; i < count; i++)
        {
            int n = Compact(d, ref p);
            if (n is <= 0 or > 1024)
            {
                throw new InvalidDataException($"bad name length {n}");
            }

            var chunk = new byte[n];
            for (int j = 0; j < n; j++)
            {
                chunk[j] = plain ? d[p + j] : (byte)(d[p + j] ^ ((p + j) & 0xFF));
            }

            names[i] = Encoding.Latin1.GetString(chunk).TrimEnd('\0');
            p += n + 4;                                     // + name flags
        }

        return names;
    }

    /// <summary>An SCCT name reference: the local index, then a cooked global index that is not used.</summary>
    private static int NameRef(byte[] d, ref int p)
    {
        int local = Compact(d, ref p);
        Compact(d, ref p);
        return local;
    }

    /// <summary>A UE compact index: sign and "more" bits in the first byte, then 7-bit groups.</summary>
    private static int Compact(byte[] d, ref int p)
    {
        byte b = d[p++];
        long n = b & 0x3F;
        bool negative = (b & 0x80) != 0;
        bool more = (b & 0x40) != 0;
        int shift = 6;
        while (more)
        {
            if (shift > 27)
            {
                throw new InvalidDataException("bad compact index");
            }

            b = d[p++];
            n |= (long)(b & 0x7F) << shift;
            shift += 7;
            more = (b & 0x80) != 0;
        }

        return (int)(negative ? -n : n);
    }
}
