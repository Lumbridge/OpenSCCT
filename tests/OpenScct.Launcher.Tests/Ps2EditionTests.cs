using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using OpenScct.Launcher.Ps2;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class Ps2EditionTests
{
    [Theory]
    [InlineData("SLES_532.87", "SLES-53287")]
    [InlineData("SLES_530.07", "SLES-53007")]
    [InlineData("SLUS_211.37", "SLUS-21137")]
    [InlineData("SLPM_661.30", "SLPM-66130")]
    [InlineData("SLPM_664.96", "SLPM-66496")]
    public void ReadsBootExecutableFromIsoRegardlessOfFilename(string bootName, string expected)
    {
        using var dir = new TempDir();
        string iso = WriteIso(dir, "unknown.iso", bootName);
        Assert.Equal(expected, Ps2Disc.ReadSerial(iso));
    }

    [Fact]
    public void RejectsWrongDiscInvalidElfAndTruncatedExtent()
    {
        using var dir = new TempDir();
        string iso = WriteIso(dir, "Splinter Cell Chaos Theory.iso", "SLUS_000.00");
        Assert.Throws<InvalidDataException>(() => new Ps2Edition().WithDisc(iso));
        iso = WriteIso(dir, "bad.iso", "SLES_532.87");
        using (var stream = File.OpenWrite(iso))
        {
            stream.Position = 20 * 2048;
            stream.WriteByte(0);
        }
        Assert.Throws<InvalidDataException>(() => Ps2Disc.ReadSerial(iso));
        using (var stream = File.OpenWrite(iso)) stream.SetLength(19 * 2048);
        Assert.Throws<InvalidDataException>(() => Ps2Disc.ReadSerial(iso));
    }

    [Fact]
    public void SavesIsolatedModSelectionAndRechecksItWhenLaunching()
    {
        using var dir = new TempDir();
        string disc = WriteIso(dir, "original disc.iso", "SLES_532.87");
        string patched = WriteIso(dir, "my mod.iso", "SLES_532.87");
        string other = WriteIso(dir, "US disc.iso", "SLUS_211.37");
        string exe = dir.Combine("PCSX2 test.exe");
        File.WriteAllText(exe, "");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var ps2 = new Ps2Edition().WithDisc(disc).WithEmulator(exe);
        Assert.Throws<InvalidDataException>(() => ps2.WithMod(disc));
        Assert.Throws<InvalidDataException>(() => ps2.WithMod(other));
        ps2 = ps2.WithMod(patched);
        ps2.Save(dir.Path);
        var loaded = Ps2Edition.Load(dir.Path);
        Assert.Equal(ps2, loaded);
        Assert.Equal(Ps2Action.Play, loaded.NextAction);
        var start = loaded.CreateStartInfo();
        Assert.False(start.UseShellExecute);
        Assert.Equal(exe, start.FileName);
        Assert.Equal(["--", patched], start.ArgumentList);
        File.Copy(other, patched, overwrite: true);
        Assert.Throws<InvalidDataException>(() => loaded.CreateStartInfo());
        File.Delete(patched);
        Assert.Equal(Ps2Action.ChooseMod, loaded.NextAction);
        Assert.Equal(["--", disc], loaded.WithoutMod().CreateStartInfo().ArgumentList);
        Assert.Null(loaded.WithDisc(other).ModPath);
    }

    // Only the ISO directory, config and a minimal ELF header. No retail bytes.
    private static string WriteIso(TempDir dir, string name, string bootName)
    {
        const int sector = 2048;
        byte[] iso = new byte[21 * sector];
        Span<byte> pvd = iso.AsSpan(16 * sector, sector);
        pvd[0] = 1;
        "CD001"u8.CopyTo(pvd[1..]);
        pvd[6] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(pvd[80..], 21);
        BinaryPrimitives.WriteUInt16LittleEndian(pvd[128..], sector);
        WriteEntry(pvd[156..], "\0", 18, sector, directory: true);
        string cnf = $"BOOT2 = cdrom0:\\{bootName};1\r\nVER = 1.01\r\n";
        Span<byte> directory = iso.AsSpan(18 * sector, sector);
        int next = WriteEntry(directory, "SYSTEM.CNF;1", 19, cnf.Length);
        WriteEntry(directory[next..], bootName + ";1", 20, 52);
        Encoding.ASCII.GetBytes(cnf).CopyTo(iso, 19 * sector);
        Span<byte> elf = iso.AsSpan(20 * sector);
        "\u007fELF"u8.CopyTo(elf);
        elf[4] = elf[5] = 1;
        elf[18] = 8;
        string path = dir.Combine(name);
        File.WriteAllBytes(path, iso);
        return path;
    }

    private static int WriteEntry(Span<byte> entry, string name, uint sector, int bytes, bool directory = false)
    {
        entry[0] = (byte)((34 + name.Length) & ~1);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[2..], sector);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[10..], (uint)bytes);
        entry[25] = directory ? (byte)2 : (byte)0;
        entry[32] = (byte)name.Length;
        Encoding.ASCII.GetBytes(name, entry[33..]);
        return entry[0];
    }
}
