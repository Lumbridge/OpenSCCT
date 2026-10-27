using System.Linq;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Update;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class ReleasePickerTests
{
    private static ReleaseInfo Release(string tag, bool pre = false, bool draft = false, params string[] assets) => new()
    {
        TagName = tag,
        Prerelease = pre,
        Draft = draft,
        Assets = assets.Select((n, i) => new ReleaseAsset { Id = i + 1, Name = n, Url = "https://api.example/" + n }).ToList(),
    };

    [Fact]
    public void LatestSkipsDraftsPrereleasesAndOddTags()
    {
        ReleaseInfo[] releases =
        [
            Release("v0.3.0", draft: true),
            Release("v0.2.1-beta.1", pre: true),
            Release("nightly"),
            Release("v0.2.0"),
            Release("v0.1.9"),
        ];

        Assert.Equal("0.2.0", ReleasePicker.Latest(releases, includePrereleases: false)!.Version.ToString());
        Assert.Equal("0.2.1-beta.1", ReleasePicker.Latest(releases, includePrereleases: true)!.Version.ToString());
    }

    [Fact]
    public void PrereleaseTagWithoutTheGitHubFlagIsStillAPrerelease()
    {
        ReleaseInfo[] releases = [Release("v1.0.0-rc.1"), Release("v0.9.0")];
        Assert.Equal("0.9.0", ReleasePicker.Latest(releases, includePrereleases: false)!.Version.ToString());
    }

    [Fact]
    public void NothingWhenNoTagIsAVersion() => Assert.Null(ReleasePicker.Latest([Release("nightly")], includePrereleases: true));

    [Fact]
    public void PicksGameLauncherAndChecksumAssets()
    {
        VersionedRelease r = ReleasePicker.Latest(
            [Release("v0.2.0", false, false, "OpenSCCT-0.2.0-win-x64.zip", "OpenSCCT.Launcher-0.2.0-win-x64.exe", "checksums.txt", "source.zip")],
            false)!;

        ReleaseAsset game = ReleasePicker.GameAsset(r)!;
        Assert.Equal("OpenSCCT-0.2.0-win-x64.zip", game.Name);
        Assert.Equal("OpenSCCT.Launcher-0.2.0-win-x64.exe", ReleasePicker.LauncherAsset(r)!.Name);
        Assert.Equal("checksums.txt", ReleasePicker.ChecksumAsset(r, game)!.Name);
    }

    [Fact]
    public void PerAssetChecksumWinsOverChecksumsTxt()
    {
        VersionedRelease r = ReleasePicker.Latest(
            [Release("v0.2.0", false, false, "OpenSCCT-0.2.0-win-x64.zip", "checksums.txt", "OpenSCCT-0.2.0-win-x64.zip.sha256")],
            false)!;
        Assert.Equal("OpenSCCT-0.2.0-win-x64.zip.sha256", ReleasePicker.ChecksumAsset(r, ReleasePicker.GameAsset(r)!)!.Name);
    }

    [Fact]
    public void LauncherExeIsNeverTakenForTheGame()
    {
        VersionedRelease r = ReleasePicker.Latest([Release("v0.2.0", false, false, "OpenSCCT.Launcher-0.2.0-win-x64.exe")], false)!;
        Assert.Null(ReleasePicker.GameAsset(r));
    }
}
