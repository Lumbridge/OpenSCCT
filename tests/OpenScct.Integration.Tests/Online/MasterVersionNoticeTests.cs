using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class MasterVersionNoticeTests
{
    private sealed class Clock : TimeProvider
    {
        private long _offset;
        public override long GetTimestamp() => base.GetTimestamp() + Interlocked.Read(ref _offset);
        public void AdvanceMinute() => Interlocked.Add(ref _offset, 61 * TimestampFrequency);
    }

    [Fact]
    public async Task OperatorCanRecommendOneVersionWithoutChangingTheMinimumOrRestarting()
    {
        var ct = TestContext.Current.CancellationToken;
        var temp = Directory.CreateTempSubdirectory("OpenSCCT-version-notice-");
        try
        {
            var options = MasterTests.LocalOptions();
            options.DataDirectory = temp.FullName;
            options.Discord.ClientId = "test";
            options.Discord.ClientSecret = "test";
            options.MinVersion = "0.8.56";
            using var store = AccountStore.Open(options.ResolvePath(options.Database));
            store.SetSettingsAtomic(new Dictionary<string, string?>
                { ["minVersion"] = "0.8.75", ["latestVersion"] = "0.8.162", ["motd"] = "original" });
            var clock = new Clock();
            await using var host = await MasterHost.StartAsync(options, cancellationToken: ct, time: clock);
            using var client = new MasterClient(host.HttpAddress);
            Assert.Equal("0.8.162", (await client.StatusAsync(cancellationToken: ct)).LatestVersion);
            int Recommend(string version) => AdminCommands.Run(["recommend-version", version], options, TextWriter.Null, TextWriter.Null);
            Assert.NotEqual(0, Recommend("0.8.bad"));
            Assert.NotEqual(0, Recommend("0.8.60"));
            Assert.Equal(0, Recommend("0.8.165"));
            Assert.Equal(0, Recommend("0.8.165"));
            Assert.Single(store.DetailedAudit("", "cli", "settings.update", "settings", null, null, 0, 10));
            clock.AdvanceMinute();
            var status = await client.StatusAsync(cancellationToken: ct);
            for (int waits = 0; status.LatestVersion != "0.8.165" && waits < 40; waits++)
            {
                await Task.Delay(100, ct);
                status = await client.StatusAsync(cancellationToken: ct);
            }
            Assert.Equal("0.8.165", status.LatestVersion);
            Assert.Equal("0.8.75", status.MinVersion);
            Assert.Equal("original", status.Motd);
        }
        finally { temp.Delete(true); }
    }
}
