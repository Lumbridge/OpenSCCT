using System;
using System.Linq;
using System.Text;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class GuestServerNameTests
{
    [Theory]
    [InlineData("x", 80, 58)]
    [InlineData("é", 32, 29)]
    [InlineData("😀", 32, 14)]
    public void DiscoveryRetainsTheWholeGuestTagOnLongTitles(string character, int repetitions, int kept)
    {
        const string installId = "guest-host-install";
        string title = string.Concat(Enumerable.Repeat(character, repetitions));
        string expected = string.Concat(Enumerable.Repeat(character, kept)) + " #" + PlayerNames.GuestTag(installId);
        var info = new ServerInfo { Name = PlayerNames.GuestServerName(title, installId) };
        Assert.Equal(expected, info.Name);
        Assert.True(Encoding.UTF8.GetByteCount(info.Name) <= 64);

        byte[] bytes = new byte[1024];
        int length = info.Write(bytes);
        Assert.True(ServerInfo.TryRead(bytes.AsSpan(0, length), out ServerInfo decoded));
        Assert.Equal(expected, decoded.Name);
        Assert.DoesNotContain("\uFFFD", decoded.Name, StringComparison.Ordinal);
    }
}
