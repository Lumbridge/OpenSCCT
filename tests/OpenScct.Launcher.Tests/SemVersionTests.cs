using OpenScct.Launcher.Versioning;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class SemVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v0.10.0", 0, 10, 0, "")]
    [InlineData("V2.0.0-rc.1", 2, 0, 0, "rc.1")]
    [InlineData("1.0.0-alpha+exp.sha.5114f85", 1, 0, 0, "alpha")]
    public void Parses(string text, int major, int minor, int patch, string pre)
    {
        SemVersion v = SemVersion.Parse(text);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(pre, string.Join('.', v.Prerelease));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-a..b")]
    [InlineData("latest")]
    [InlineData("1.2.x")]
    public void RejectsInvalid(string text) => Assert.False(SemVersion.TryParse(text, out _));

    [Fact]
    public void OrdersAsTheSpecExample()
    {
        string[] ordered =
        [
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11",
            "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0", "10.0.0",
        ];
        for (int i = 1; i < ordered.Length; i++)
        {
            Assert.True(SemVersion.Parse(ordered[i - 1]) < SemVersion.Parse(ordered[i]), $"{ordered[i - 1]} < {ordered[i]}");
        }
    }

    [Fact]
    public void BuildMetadataIsIgnoredWhenComparing()
    {
        Assert.Equal(SemVersion.Parse("1.0.0+a"), SemVersion.Parse("v1.0.0+b"));
        Assert.Equal("1.0.0", SemVersion.Parse("v1.0.0+abc").ToString());
    }

    [Fact]
    public void LongNumericPrereleaseDoesNotOverflow() =>
        Assert.True(SemVersion.Parse("1.0.0-99999999999999999999") > SemVersion.Parse("1.0.0-9"));
}
