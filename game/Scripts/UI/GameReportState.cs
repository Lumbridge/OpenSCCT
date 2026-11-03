using OpenScct.Project;

namespace OpenScctGame.UI;

/// <summary>Classifies a report without depending on Godot, user identity or connection addresses.</summary>
internal readonly record struct GameReportState(bool Replay = false, bool Online = false, bool OnlineHost = false,
    bool Hub = false, bool HubHost = false, bool Playing = false)
{
    public ReportSession Session => Replay ? ReportSession.Replay
        : Online ? OnlineHost ? ReportSession.OnlineHost : ReportSession.OnlineClient
        : Hub ? HubHost ? ReportSession.HubHost : ReportSession.HubClient
        : Playing ? ReportSession.Offline : ReportSession.Menu;

    public static string? Side(int? team, bool spectator) => spectator ? "Spectator"
        : team switch { 0 => "Spy", 1 => "Mercenary", _ => null };

    /// <summary>Map launch arguments may be local paths. Reports show only the map's file stem.</summary>
    public static string? MapId(string? map)
    {
        if (string.IsNullOrWhiteSpace(map)) return null;
        string name = map.Trim().Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        int extension = name.LastIndexOf('.');
        if (extension > 0) name = name[..extension];
        return name.Length == 0 || name is "." or ".." ? null : name;
    }
}
