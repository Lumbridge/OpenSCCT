using System;
using System.Text.Json;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PresenceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void LeasesDeduplicateAccountsPromoteGuestsAndExpire()
    {
        var clock = new Clock();
        var registry = new PresenceRegistry(clock, 3);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        Assert.True(registry.Touch(a, null));
        Assert.True(registry.Touch(a, null));
        Assert.Equal(1, registry.Count);
        registry.Touch(a, 42); registry.Touch(b, 42); registry.Touch(c, null);
        Assert.Equal(2, registry.Count);
        Assert.False(registry.Touch(Guid.NewGuid(), null));
        clock.Now += TimeSpan.FromSeconds(60);
        registry.Touch(a, 42);
        clock.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(1, registry.Count);
        clock.Now += TimeSpan.FromSeconds(60);
        Assert.Equal(0, registry.Count);
        Assert.True(registry.Touch(Guid.NewGuid(), null));
        Assert.False(registry.Touch(Guid.Empty, null));
    }

    [Fact]
    public async Task EndpointCountsGuestsAndExpiresWithoutRosterData()
    {
        var clock = new Clock();
        var ct = TestContext.Current.CancellationToken;
        var options = MasterTests.LocalOptions("key");
        options.Probe = false;
        await using var master = await MasterHost.StartAsync(options, cancellationToken: ct, time: clock);
        using var client = new MasterClient(master.HttpAddress, "key");
        var session = Guid.NewGuid();
        Assert.True((await client.PresenceAsync(session, ct)).Accepted);
        await client.PresenceAsync(session, ct);
        Assert.Equal(1, (await client.StatusAsync(ct)).PlayersOnline);
        await client.PresenceAsync(Guid.NewGuid(), ct);
        Assert.Equal(2, (await client.StatusAsync(ct)).PlayersOnline);
        clock.Now += TimeSpan.FromSeconds(90);
        Assert.Equal(0, (await client.StatusAsync(ct)).PlayersOnline);
        client.SessionToken = "invalid";
        await Assert.ThrowsAsync<MasterException>(() => client.PresenceAsync(session, ct));
        using var stranger = new MasterClient(master.HttpAddress);
        await Assert.ThrowsAsync<MasterException>(() => stranger.PresenceAsync(session, ct));
    }

    [Fact]
    public async Task HeartbeatsUseTheApiRateLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = MasterTests.LocalOptions("key");
        options.Probe = false;
        options.RequestsPerMinute = 1;
        await using var master = await MasterHost.StartAsync(options, cancellationToken: ct);
        using var client = new MasterClient(master.HttpAddress, "key");
        await client.PresenceAsync(Guid.NewGuid(), ct);
        await Assert.ThrowsAsync<MasterException>(() => client.PresenceAsync(Guid.NewGuid(), ct));
        Assert.Equal(1, (await client.StatusAsync(ct)).PlayersOnline);
    }

    [Fact]
    public void OldStatusHasUnknownCount() =>
        Assert.Null(JsonSerializer.Deserialize<MasterStatus>("{}", MasterApi.Json)!.PlayersOnline);
}
