using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Play.Session;

/// <summary>Disposable loopback match with an idle native UDP player for browser/desktop interoperability checks.</summary>
internal sealed class BrowserLabMatch : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _run;
    public OnlineServer Server { get; }
    public OnlineClient Native { get; }

    public BrowserLabMatch(string content, string master, CancellationToken stopping)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        ContentPaths.Configure(content, null);
        var maps = new MapCatalog(Path.Combine(content, "svmap"));
        string folder = maps.FindFolder("TestRange")!;
        var data = SvMap.Load(folder);
        // The server owns its game rules. Only the playing client needs the presentation-aware loader,
        // which creates its own rules for offline play and must not be registered on a dedicated host.
        var nativeMaps = new MapCatalog(Path.Combine(content, "svmap"));
        new PlayMapLoader(data, "", PlayMapLoader.FindCharsDir(folder)).Register(nativeMaps);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var udp = LiteNetLibTransport.Listen(0, 16, loopbackOnly: true);
        Server = new OnlineServer(udp, new OnlineServerSettings
        {
            Name = "Local TestRange · native + browser", Dedicated = true, MaxPlayers = 4,
            Maps = maps, Profile = NetProfile.Enhanced,
            Setup = LobbySetup.ForMap("TestRange", EGameMode.GM_Multi, maps.GetMapSettings("TestRange"), 4) with { Practice = true },
            LaunchCountdownSeconds = 1,
            Master = new MasterLinkSettings { Url = master, ServerKey = "local-lab", NatPunch = false, Relay = false },
        }, 0, udp);
        Native = new OnlineClient(JoinTarget.FromAddress("127.0.0.1:" + udp.LocalPort), new OnlineClientSettings
        { PlayerName = "NativeReference", InstallId = "local-browser-lab-native", PreferredTeam = 1, Maps = nativeMaps, Voice = false }, 0);
        _run = Task.Run(async () =>
        {
            long frame = 0;
            long readyAt = 0;
            bool reportedFailure = false;
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    long now = (long)clock.Elapsed.TotalMicroseconds;
                    long due = now * Sim.Hz / 1_000_000;
                    if (due - frame > 5) frame = due - 5;
                    while (frame < due)
                    {
                        frame++;
                        Server.Tick(frame * 1_000_000 / Sim.Hz);
                        Native.Tick(frame * 1_000_000 / Sim.Hz);
                        if (Native.State == OnlineClientState.Lobby && Native.Lobby.Local is { Ready: false, Map: MapState.Ok } && now >= readyAt)
                        {
                            Native.SetReady(true);
                            readyAt = now + 1_000_000;
                        }
                        if (Native.State == OnlineClientState.Failed && !reportedFailure)
                        {
                            reportedFailure = true;
                            Console.Error.WriteLine("Native fixture failed: " + Native.Error);
                        }
                    }
                    await Task.Delay(2, _stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _run;
        Native.Dispose();
        Server.Dispose();
        _stop.Dispose();
    }
}
