using System.Linq;
using Godot;
using OpenScct.Net.Discovery;
using OpenScct.Project;
using OpenScct.Server.Online;
using OpenScctGame.Hub;
using OpenScctGame.Online;
using OpenScctGame.Play;

namespace OpenScctGame.UI;

/// <summary>Reads only the small, visible environment summary offered to a player before reporting.</summary>
internal static class GameReportContext
{
    public static ReportContext Capture(SceneTree tree, IOnlineLobbySession? lobby = null)
    {
        var play = tree.CurrentScene as PlayRoot;
        var session = play?.Session;
        var online = tree.Root.GetNodeOrNull<OnlineService>(OnlineService.NodeName);
        var hub = tree.Root.GetNodeOrNull<HubService>(HubService.NodeName);
        var state = new GameReportState(play?.Config.Demo is not null,
            lobby is not null || online?.Client is not null, lobby?.IsHost ?? (online?.Hosting is not null),
            hub?.Client is not null, hub?.Host is not null, session is not null);
        var player = session?.LocalController?.PlayerReplicationInfo;
        var local = lobby?.Members.FirstOrDefault(member => member.IsLocal);
        int? team = player?.Team?.TeamIndex ?? (local?.Side is { } side ? (int)side
            : hub?.Client?.Team ?? (session is null ? null : session.Settings.Team));
        bool spectator = player?.bOnlySpectator ?? (local is not null && local.Side is null);
        var setup = lobby?.Setup ?? online?.Client?.Lobby.Setup;
        return new ReportContext
        {
            Source = ReportSource.Game, Version = BuildInfo.Version, Build = BuildInfo.BuildNumber,
            Commit = BuildInfo.Commit, Platform = OS.GetName(), Session = state.Session,
            Map = GameReportState.MapId(session?.Settings.Map ?? lobby?.Setup.Map ?? online?.Client?.CurrentMap ?? online?.Client?.Lobby.Setup.Map),
            Mode = session?.Settings.Mode ?? (setup is null ? null : LobbySetup.ModeName(setup.Mode)),
            Side = GameReportState.Side(team, spectator),
        };
    }
}
