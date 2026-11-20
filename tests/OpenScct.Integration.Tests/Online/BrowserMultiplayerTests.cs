using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using OpenScct.Master;
using OpenScct.Master.Player;
using OpenScct.Master.Web;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class BrowserMultiplayerTests
{
    [Fact]
    public async Task RetiredBrowserRequestsDoNotExhaustTheDynamicPageBudget()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var options = MasterTests.LocalOptions();
        options.RequestsPerMinute = 2;
        await using var master = await MasterHost.StartAsync(options, cancellationToken: ct);
        using var http = new HttpClient { BaseAddress = new Uri(master.HttpAddress) };
        // Stale game clients can retry the same bundle repeatedly after the public game has been retired.
        for (int i = 0; i < 6; i++)
        {
            using var asset = await http.GetAsync("/play/_framework/GodotSharp.dll", ct);
            Assert.Equal(HttpStatusCode.Gone, asset.StatusCode);
        }
        for (int i = 0; i < 3; i++)
        {
            using var page = await http.GetAsync("/missing-page", ct);
            Assert.Equal(i == 2 ? HttpStatusCode.TooManyRequests : HttpStatusCode.NotFound, page.StatusCode);
        }
    }

    [Fact]
    public async Task WebSocketAndUdpPlayersShareALobbyAndTravelIntoTheSameRound()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture required.");
        CancellationToken ct = TestContext.Current.CancellationToken;
        var clock = new WallClock();
        var udp = LiteNetLibTransport.Listen(0, 16, loopbackOnly: true);
        using var rig = new OnlineRig(new OnlineServer(udp, OnlineRig.Settings(MapCatalog.TestBox), clock.NowMicroseconds, udp), clock);
        var options = MasterTests.LocalOptions();
        options.InviteKeys.Clear();
        using var store = AccountStore.Open(":memory:");
        var registry = new ServerRegistry(options);
        var registered = registry.Heartbeat(new HeartbeatRequest { Name = "Browser test", Map = MapCatalog.TestBox,
            GamePort = udp.LocalPort, Protocol = MasterApi.Hex(WalkerRig.Classes.ComputeProtocolHash()), MaxPlayers = 4 }, IPAddress.Loopback, out _, out _)!;
        registered.TrustedName = "Browser test";
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        new PlayerPortal(options, store, registry, _ => IPAddress.Loopback, new ReleaseChangelog(), enableBrowserPrototype: true).Map(app);
        await app.StartAsync(ct);
        string origin = app.Urls.Single();
        options.PublicUrl = origin;
        using var http = new HttpClient { BaseAddress = new Uri(origin) };
        using var wrongOrigin = await http.PostAsync("/play/join", new StringContent(JsonSerializer.Serialize(new { serverId = registered.Id }), Encoding.UTF8, "application/json"), ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongOrigin.StatusCode);
        http.DefaultRequestHeaders.Add("Origin", origin);
        using var arbitraryServer = await http.PostAsync("/play/join", new StringContent("{\"serverId\":\"127.0.0.1:22\"}", Encoding.UTF8, "application/json"), ct);
        Assert.Equal(HttpStatusCode.NotFound, arbitraryServer.StatusCode);
        options.InviteKeys.Add("fixture-invite");
        using var noInvite = await http.PostAsync("/play/join", new StringContent(JsonSerializer.Serialize(new { serverId = registered.Id }), Encoding.UTF8, "application/json"), ct);
        Assert.Equal(HttpStatusCode.Forbidden, noInvite.StatusCode);
        http.DefaultRequestHeaders.Add(MasterApi.KeyHeader, "fixture-invite");
        using var response = await http.PostAsync("/play/join", new StringContent(JsonSerializer.Serialize(new { serverId = registered.Id }), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        using var granted = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        string url = new UriBuilder(new Uri(http.BaseAddress, granted.RootElement.GetProperty("socket").GetString())) { Scheme = "ws" }.Uri.AbsoluteUri;
        var browser = rig.Add(new OnlineClient(JoinTarget.Custom("Browser", () => new WebSocketTransport(origin), url), OnlineRig.ClientSettings("Browser", 0), rig.Now));
        var native = rig.Add(new OnlineClient(JoinTarget.FromAddress("127.0.0.1:" + udp.LocalPort), OnlineRig.ClientSettings("Native", 1), rig.Now));
        Assert.True(rig.RunUntil(() => browser.Lobby.Players.Count == 2 && native.Lobby.Players.Count == 2, 15),
            browser.Error + "; " + string.Join(';', browser.ConnectLog));
        using var replay = new System.Net.WebSockets.ClientWebSocket();
        replay.Options.SetRequestHeader("Origin", origin);
        await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(async () => await replay.ConnectAsync(new Uri(url), ct));
        browser.SetReady(true);
        native.SetReady(true);
        Assert.True(rig.RunUntil(() => browser.State == OnlineClientState.Match && native.State == OnlineClientState.Match, 20),
            $"Browser {browser.State}: {browser.Error}; native {native.State}: {native.Error}");
        Assert.Equal(MapCatalog.TestBox, browser.CurrentMap);
        Assert.Equal(native.CurrentMap, browser.CurrentMap);
        Assert.Equal(2, browser.Lobby.Players.Count);
        browser.Dispose();
        native.Dispose();
        await app.StopAsync(ct);
    }
}
