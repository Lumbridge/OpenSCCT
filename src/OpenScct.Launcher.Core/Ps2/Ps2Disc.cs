using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace OpenScct.Launcher.Ps2;

/// <summary>Identifies a 2048-byte sector PS2 ISO from its boot configuration and executable.</summary>
public static class Ps2Disc
{
    private const int SectorSize = 2048;
    private readonly record struct Entry(uint Sector, uint Size, bool Directory);

    public static string ReadSerial(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose an uncompressed PlayStation 2 disc image (.iso).");

        using var iso = File.OpenRead(path);
        byte[] sector = new byte[SectorSize];
        for (int descriptor = 16; descriptor < 80; descriptor++)
        {
            iso.Position = (long)descriptor * SectorSize;
            iso.ReadExactly(sector);
            if (!sector.AsSpan(1, 5).SequenceEqual("CD001"u8) || sector[6] != 1 || sector[0] == 255)
                break;
            if (sector[0] != 1) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(sector.AsSpan(128)) != SectorSize)
                throw new InvalidDataException("This image uses an unsupported ISO sector size.");
            long volumeSize = (long)BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(80)) * SectorSize;
            if (volumeSize > iso.Length)
                throw new InvalidDataException("The disc image is incomplete.");
            Entry root = ParseEntry(sector.AsSpan(156));
            if (!root.Directory) break;
            return ReadBootSerial(iso, root);
        }
        throw new InvalidDataException("The file does not contain a PlayStation 2 ISO filesystem.");
    }

    private static string ReadBootSerial(FileStream iso, Entry root)
    {
        byte[] directory = ReadFile(iso, root, 4 * 1024 * 1024);
        Entry config = FindFile(directory, "SYSTEM.CNF");
        string configuration = Encoding.ASCII.GetString(ReadFile(iso, config, 64 * 1024));
        Span<byte> header = stackalloc byte[20];
        foreach (string line in configuration.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || !fields[0].Equals("BOOT2", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!fields[1].StartsWith("cdrom0:", StringComparison.OrdinalIgnoreCase)) break;
            string bootName = fields[1][7..].TrimStart('\\', '/').Split(';')[0].ToUpperInvariant();
            string serial = bootName.Replace('_', '-').Replace(".", string.Empty, StringComparison.Ordinal);
            // Retail serials recorded in PCSX2's bin/resources/GameIndex.yaml.
            if (serial is not ("SLES-53007" or "SLES-53287" or "SLUS-21137" or "SLPM-66130" or "SLPM-66496"))
                throw new InvalidDataException("This disc is not a recognized release of Splinter Cell Chaos Theory.");
            Entry elf = FindFile(directory, bootName);
            SeekFile(iso, elf);
            if (elf.Size >= header.Length)
            {
                iso.ReadExactly(header);
                if (header[..4].SequenceEqual("\u007fELF"u8) && header[4] == 1 && header[5] == 1 &&
                    BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) == 8)
                    return serial;
            }
            throw new InvalidDataException("The disc's boot executable is not a PlayStation 2 ELF.");
        }
        throw new InvalidDataException("SYSTEM.CNF does not name a PlayStation 2 boot executable.");
    }

    private static Entry FindFile(byte[] directory, string fileName)
    {
        for (int offset = 0; offset < directory.Length;)
        {
            int size = directory[offset];
            if (size == 0)
            {
                offset = ((offset / SectorSize) + 1) * SectorSize;
                continue;
            }
            if (size < 34 || size > directory.Length - offset || size > SectorSize - offset % SectorSize)
                throw new InvalidDataException("Invalid ISO directory record.");
            ReadOnlySpan<byte> record = directory.AsSpan(offset, size);
            int nameSize = record[32];
            if (nameSize > size - 33) throw new InvalidDataException("Invalid ISO filename.");
            string name = Encoding.ASCII.GetString(record.Slice(33, nameSize)).Split(';')[0];
            Entry entry = ParseEntry(record);
            if (!entry.Directory && string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
                return entry;
            offset += size;
        }
        throw new InvalidDataException($"The disc is missing {fileName}.");
    }

    private static Entry ParseEntry(ReadOnlySpan<byte> record) =>
        new(BinaryPrimitives.ReadUInt32LittleEndian(record[2..]), BinaryPrimitives.ReadUInt32LittleEndian(record[10..]), (record[25] & 2) != 0);

    private static byte[] ReadFile(FileStream iso, Entry entry, uint limit)
    {
        if (entry.Size > limit) throw new InvalidDataException("The disc directory or boot configuration is too large.");
        SeekFile(iso, entry);
        byte[] bytes = new byte[entry.Size];
        iso.ReadExactly(bytes);
        return bytes;
    }

    private static void SeekFile(FileStream iso, Entry entry)
    {
        long offset = (long)entry.Sector * SectorSize;
        if (offset + entry.Size > iso.Length) throw new InvalidDataException("The disc image is incomplete.");
        iso.Position = offset;
    }
}
