using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class PlayUpdateFlowTests
{
    [Fact]
    public async Task EveryPlayChecksAgainAndOffersLaterReleasesAfterDeferral()
    {
        int checks = 0;
        int updates = 0;
        var offered = new List<string>();

        Task<UpdateCheck> Check(CancellationToken _) => Task.FromResult(Available(++checks == 1 ? "0.2.0" : "0.3.0"));
        PlayUpdateChoice Choose(UpdateCheck check)
        {
            offered.Add(check.Latest!.Version.ToString());
            return PlayUpdateChoice.PlayCurrent;
        }
        Task Update(CancellationToken _)
        {
            updates++;
            return Task.CompletedTask;
        }

        Assert.True(await PlayUpdateFlow.RunAsync(Check, Choose, Update, TestContext.Current.CancellationToken));
        Assert.True(await PlayUpdateFlow.RunAsync(Check, Choose, Update, TestContext.Current.CancellationToken));

        Assert.Equal(new[] { "0.2.0", "0.3.0" }, offered);
        Assert.Equal(2, checks);
        Assert.Equal(0, updates);
    }

    [Theory]
    [InlineData(ReleaseFeedSource.Network)]
    [InlineData(ReleaseFeedSource.NotModified)]
    public async Task UpdateRunsOnlyAfterThePlayerAcceptsAndReturnsToTheLauncher(ReleaseFeedSource source)
    {
        var events = new List<string>();
        bool launch = await PlayUpdateFlow.RunAsync(
            async _ =>
            {
                await Task.Yield();
                events.Add("checked");
                return Available(source: source);
            },
            _ =>
            {
                events.Add("accepted");
                return PlayUpdateChoice.Update;
            },
            async cancel =>
            {
                Assert.Equal(TestContext.Current.CancellationToken, cancel);
                await Task.Yield();
                events.Add("updated");
            },
            TestContext.Current.CancellationToken);

        Assert.False(launch);
        Assert.Equal(new[] { "checked", "accepted", "updated" }, events);
    }

    [Fact]
    public async Task CancelDoesNotLaunchOrUpdate()
    {
        bool launch = await PlayUpdateFlow.RunAsync(
            _ => Task.FromResult(Available()),
            _ => PlayUpdateChoice.Cancel,
            UnexpectedUpdate,
            TestContext.Current.CancellationToken);

        Assert.False(launch);
    }

    [Fact]
    public async Task AnUpToDateGameLaunchesWithoutPrompting()
    {
        bool launch = await PlayUpdateFlow.RunAsync(
            _ => Task.FromResult(Available() with { GameUpdateAvailable = false }),
            UnexpectedChoice,
            UnexpectedUpdate,
            TestContext.Current.CancellationToken);

        Assert.True(launch);
    }

    [Theory]
    [InlineData(ReleaseFeedSource.Cache)]
    [InlineData(ReleaseFeedSource.None)]
    public async Task OfflineOrRateLimitedChecksLaunchWithoutPrompting(ReleaseFeedSource source)
    {
        bool launch = await PlayUpdateFlow.RunAsync(
            _ => Task.FromResult(Available(source: source)),
            UnexpectedChoice,
            UnexpectedUpdate,
            TestContext.Current.CancellationToken);

        Assert.True(launch);
    }

    [Fact]
    public async Task CheckTimeoutAllowsTheInstalledGameToLaunch()
    {
        bool launch = await PlayUpdateFlow.RunAsync(
            async cancel =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
                return Available();
            },
            UnexpectedChoice,
            UnexpectedUpdate,
            TestContext.Current.CancellationToken,
            TimeSpan.FromMilliseconds(20));

        Assert.True(launch);
    }

    [Fact]
    public async Task CallerCancellationDuringCheckDoesNotLaunch()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlayUpdateFlow.RunAsync(
            async token =>
            {
                cancel.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Available();
            },
            UnexpectedChoice,
            UnexpectedUpdate,
            cancel.Token));
    }

    [Fact]
    public async Task CallerCancellationAfterACompletedCheckDoesNotPromptOrLaunch()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlayUpdateFlow.RunAsync(
            _ =>
            {
                cancel.Cancel();
                return Task.FromResult(Available());
            },
            UnexpectedChoice,
            UnexpectedUpdate,
            cancel.Token));
    }

    [Theory]
    [InlineData(PlayUpdateChoice.PlayCurrent)]
    [InlineData(PlayUpdateChoice.Update)]
    [InlineData(PlayUpdateChoice.Cancel)]
    public async Task CallerCancellationWhileChoosingDoesNotLaunchOrUpdate(PlayUpdateChoice choice)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlayUpdateFlow.RunAsync(
            _ => Task.FromResult(Available()),
            _ =>
            {
                cancel.Cancel();
                return choice;
            },
            UnexpectedUpdate,
            cancel.Token));
    }

    [Fact]
    public async Task UpdateErrorsAreNotTreatedAsOfflineChecks()
    {
        var error = new HttpRequestException("Download failed");
        HttpRequestException actual = await Assert.ThrowsAsync<HttpRequestException>(() => PlayUpdateFlow.RunAsync(
            _ => Task.FromResult(Available()),
            _ => PlayUpdateChoice.Update,
            _ => Task.FromException(error),
            TestContext.Current.CancellationToken));

        Assert.Same(error, actual);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task FailedChecksAllowTheInstalledGameToLaunch(string failure)
    {
        Exception error = failure switch
        {
            "network" => new HttpRequestException("No connection"),
            "io" => new IOException("Unreadable release cache"),
            _ => new UnauthorizedAccessException("Release cache access denied"),
        };
        bool launch = await PlayUpdateFlow.RunAsync(
            _ => Task.FromException<UpdateCheck>(error),
            UnexpectedChoice,
            UnexpectedUpdate,
            TestContext.Current.CancellationToken);

        Assert.True(launch);
    }

    [Fact]
    public async Task CheckFailureDoesNotOverrideCallerCancellation()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlayUpdateFlow.RunAsync(
            _ =>
            {
                cancel.Cancel();
                throw new HttpRequestException("Disconnected");
            },
            UnexpectedChoice,
            UnexpectedUpdate,
            cancel.Token));
    }

    private static UpdateCheck Available(string version = "0.2.0", ReleaseFeedSource source = ReleaseFeedSource.Network)
    {
        var release = new ReleaseInfo { TagName = "v" + version };
        return new UpdateCheck(
            new ReleaseFeed([release], source, null, null),
            new VersionedRelease(SemVersion.Parse(version), release),
            SemVersion.Parse("0.1.0"),
            GameUpdateAvailable: true,
            LauncherUpdateAvailable: false);
    }

    private static PlayUpdateChoice UnexpectedChoice(UpdateCheck _) => throw new InvalidOperationException("No update prompt was expected.");

    private static Task UnexpectedUpdate(CancellationToken _) => throw new InvalidOperationException("No update was expected.");
}
