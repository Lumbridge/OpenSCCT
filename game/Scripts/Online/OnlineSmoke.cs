using System;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Match;
using OpenScctGame.Play;
using OpenScctGame.Smoke;
using OpenScctGame.UI;

namespace OpenScctGame.Online;

/// <summary>
/// The online flow in the game client (<c>--svm-smoke=online</c>): host a game on TestBox from the client (loopback
/// UDP, no UPnP), a second player joins it over UDP as a headless visit, both meet in the lobby screen, the host
/// launches, the play scene shows the round on the host's own remote session with its pawn, the results screen
/// and travel are left for the tests, and leaving returns to the main menu while the guest is told the host closed.
/// </summary>
public sealed class OnlineSmoke : SmokeTest
{
    public const string TestName = "online";

    private const double TimeoutSeconds = 90;

    private readonly StopwatchClock _clock = new();
    private int _phase;
    private double _phaseStart;
    private OnlineClient? _guest;
    private long _guestTicks;
    private long _guestStart;
    private string _notes = string.Empty;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        TickGuest();
        if (context.ElapsedSeconds > TimeoutSeconds)
        {
            return Fail($"timed out in phase {_phase}: {Describe(context)}");
        }

        OnlineService service = OnlineService.Get(context.Tree);
        switch (_phase)
        {
            case 0:
            {
                if (context.Tree.CurrentScene is not Boot)
                {
                    return SmokeResult.Running;
                }

                MapCatalog maps = service.Catalog.Maps;
                var request = new HostRequest
                {
                    Name = "Smoke host",
                    Setup = LobbySetup.ForMap(MapCatalog.TestBox, EGameMode.GM_Multi, maps.GetMapSettings(MapCatalog.TestBox), 4),
                    Port = 0, MaxPlayers = 4, LoopbackOnly = true, Upnp = false, LanPort = 0,
                };
                if (!service.Host(request))
                {
                    return Fail("could not host: " + service.LastError);
                }

                return Next(context);
            }

            case 1:
                if (context.Tree.CurrentScene is not LobbyRoomScreen { Session: { } lobby } || !lobby.IsHost)
                {
                    return SmokeResult.Running;
                }

                _guest = new OnlineClient(JoinTarget.FromAddress("127.0.0.1:" + service.Hosting!.Port), new OnlineClientSettings
                {
                    PlayerName = "Guest", InstallId = "smoke-guest", PreferredTeam = 1, Maps = service.Catalog.Maps, Classes = service.Classes,
                    LoopbackOnly = true, InstallGameplayServices = false,
                }, _clock.NowMicroseconds);
                _guestStart = _clock.NowMicroseconds;
                return Next(context);
            case 2:
                if (context.Tree.CurrentScene is not LobbyRoomScreen { Session: { } host } || host.Members.Count < 2 ||
                    _guest is not { State: OnlineClientState.Lobby } guest)
                {
                    return SmokeResult.Running;
                }

                _notes += $" lobby={host.Members.Count} spies={host.Members.Count(m => m.Side == Hud.HudSide.Spy)}";
                guest.SetReady(true);
                return Next(context);
            case 3:
                if (context.Tree.CurrentScene is not LobbyRoomScreen { Session: { } waiting } ||
                    !waiting.Members.Any(m => !m.IsLocal && m.Ready))
                {
                    return SmokeResult.Running;
                }

                waiting.Start(context.Tree);
                return Next(context);
            case 4:
                if (context.Tree.CurrentScene is not PlayRoot { Session: { IsRemote: true } session } root || !root.IsLoaded ||
                    session.LocalPawn is null || _guest?.Session?.LocalPlayer?.Pawn is null)
                {
                    return SmokeResult.Running;
                }

                _notes += $" round={session.Client.MapName} pawn={session.LocalPawn.GetType().Name} guest={_guest.State}";
                service.Leave();
                return Next(context);
            case 5:
                if (context.Tree.CurrentScene is not MainMenu || _guest is not { State: OnlineClientState.Failed } left)
                {
                    return SmokeResult.Running;
                }

                _notes += $" guest error='{left.Error}'";
                _guest.Dispose();
                _guest = null;
                return service.Hosting is null && service.Client is null
                    ? SmokeResult.Pass(_notes.Trim())
                    : Fail("the visit or the host is still running");
            default:
                return Fail("unknown phase");
        }
    }

    private SmokeResult Next(SmokeContext context)
    {
        _phase++;
        _phaseStart = context.ElapsedSeconds;
        GD.Print($"SVM_ONLINE phase {_phase} at {context.ElapsedSeconds:F1} s");
        return SmokeResult.Running;
    }

    private void TickGuest()
    {
        if (_guest is null)
        {
            return;
        }

        long due = (_clock.NowMicroseconds - _guestStart) * Sim.Hz / 1_000_000;
        if (due - _guestTicks > 5)
        {
            _guestTicks = due - 5;
        }

        while (_guestTicks < due)
        {
            _guest.Tick(_guestStart + (_guestTicks + 1) * 1_000_000 / Sim.Hz);
            _guestTicks++;
        }
    }

    private SmokeResult Fail(string reason)
    {
        _guest?.Dispose();
        _guest = null;
        return SmokeResult.Fail(reason);
    }

    private string Describe(SmokeContext context)
    {
        OnlineService service = OnlineService.Get(context.Tree);
        return $"scene={context.Tree.CurrentScene?.Name} host={service.Client?.State} hostError={service.Client?.Error ?? service.LastError} " +
            $"guest={_guest?.State} guestError={_guest?.Error} log={string.Join("/", service.Client?.ConnectLog ?? [])} since={_phaseStart:F1}";
    }
}
