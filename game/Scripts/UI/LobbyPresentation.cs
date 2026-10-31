using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Server.Online;

namespace OpenScctGame.UI;

/// <summary>Presentation only: the server remains responsible for accepting launch and ready requests.</summary>
public static class LobbyPresentation
{
    public static string MainAction(bool host, bool ready, bool countdown) =>
        host ? countdown ? "Cancel countdown" : "Start match" : ready ? "Not ready" : "Ready";

    public static string Preparation(bool host, bool localPresent, bool spectator, bool ready, bool countdown,
        int seconds, IEnumerable<string> waiting, IEnumerable<string> mapProblems, string? download = null)
    {
        if (countdown) return "Starting in " + Math.Max(0, seconds) + " seconds";
        if (!localPresent) return "Connecting to the lobby...";
        if (!string.IsNullOrEmpty(download)) return download;
        if (spectator && !host) return "Spectating — join a team to play.";
        string[] problems = mapProblems.ToArray();
        if (problems.Length > 0) return string.Join(" · ", problems);
        if (!host && !ready) return "Choose your equipment, then select Ready.";
        string[] names = waiting.ToArray();
        if (names.Length > 0) return "Waiting for " + string.Join(", ", names) + " to ready up.";
        if (host) return "Players are ready. You can start the match.";
        return ready ? "You are ready. Waiting for the host to start." : "Choose your equipment, then select Ready.";
    }

    public static string Rules(LobbySetup setup)
    {
        string Lives(int lives) => lives == 0 ? "unlimited" : lives.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string goals = setup.Mode == EGameMode.GM_MercDeathMatch ? "Kills to win: " + setup.ScoreLimit :
            setup.Mode == EGameMode.GM_DiskHunt ? "Disks to win: " + setup.DisksToWin : "Spy lives: " + Lives(setup.SpyLives);
        return (setup.TimeLimit > 0 ? setup.TimeLimit + " minute rounds" : "No time limit") + " · " + goals +
            " · Mercenary lives: " + Lives(setup.MercLives) + " · Respawn: " + setup.RespawnSeconds + "s" +
            (setup.Practice ? " · Practice enabled" : "");
    }

    public static string Objective(EGameMode mode) => mode switch
    {
        EGameMode.GM_MercDeathMatch or EGameMode.GM_DeathMatch => "Eliminate the opposing team.",
        EGameMode.GM_DiskHunt => "Collect disks to reach the winning score.",
        EGameMode.GM_Extraction => "Spies extract disks; mercenaries prevent extraction.",
        EGameMode.GM_Sabotage => "Spies plant bombs; mercenaries defend the targets.",
        EGameMode.GM_Neutralization => "Spies neutralize objectives; mercenaries defend them.",
        _ => "Spies pursue the map objectives; mercenaries defend. See Briefing for details.",
    };

    public static string ChangeNotice(LobbySetup before, LobbySetup after, string mapTitle)
    {
        var changes = LobbyRoomModel.Differences(before, after);
        if (changes.Count == 0) return "";
        var parts = new List<string>();
        if (changes.Contains(SessionRow.Map)) parts.Add("map to " + mapTitle);
        if (changes.Contains(SessionRow.Mode)) parts.Add("mode to " + LobbySetup.ModeName(after.Mode));
        int other = changes.Count - (changes.Contains(SessionRow.Map) ? 1 : 0) - (changes.Contains(SessionRow.Mode) ? 1 : 0);
        if (other > 0) parts.Add(other + (other == 1 ? " match rule" : " match rules"));
        return "Host changed " + string.Join(", ", parts) + ".";
    }
}
