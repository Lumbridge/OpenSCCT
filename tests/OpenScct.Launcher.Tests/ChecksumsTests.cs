using System.IO;
using System.Threading.Tasks;
using OpenScct.Launcher.Update;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class ChecksumsTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void ReadsSha256SumFormat()
    {
        string text = $"# made by New-Release.ps1\n{A}  OpenSCCT-0.2.0-win-x64.zip\r\n{B} *OpenSCCT.Launcher-0.2.0-win-x64.exe\n";
        Assert.Equal(A, Checksums.FindExpected(text, "OpenSCCT-0.2.0-win-x64.zip"));
        Assert.Equal(B.ToLowerInvariant(), Checksums.FindExpected(text, "openscct.launcher-0.2.0-win-x64.exe"));
        Assert.Null(Checksums.FindExpected(text, "other.zip"));
    }

    [Fact]
    public void ReadsABareHash() => Assert.Equal(A, Checksums.FindExpected(A + "\n", "anything.zip"));

    [Fact]
    public void IgnoresPathsInNames() => Assert.Equal(A, Checksums.FindExpected($"{A}  ./out/game.zip", "game.zip"));

    [Fact]
    public void RejectsShortHashes() => Assert.Null(Checksums.FindExpected("abc123  game.zip", "game.zip"));

    [Fact]
    public async Task VerifiesAFile()
    {
        using var dir = new TempDir();
        string file = dir.Combine("f.bin");
        byte[] data = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(file, data, TestContext.Current.CancellationToken);

        Assert.True(await Checksums.VerifyAsync(file, Builds.Sha256(data).ToUpperInvariant(), TestContext.Current.CancellationToken));
        Assert.False(await Checksums.VerifyAsync(file, A, TestContext.Current.CancellationToken));
    }
}
