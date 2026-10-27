using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.LevelInfo game flow: the scoring table and server travel. Written from spec/game/scoring-and-stats.md and
// spec/game/round-travel.md.
public partial class LevelInfo
{
    /// <summary>The scoring tables by mode, indexed by the AP_* constants (19 entries).</summary>
    public static class ScoringTables
    {
        [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo.InitArrayNbPoints, POINT_Objective")]
        public static readonly int[] POINT_Objective = [2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0, -2, -5];

        [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo.InitArrayNbPoints, POINT_OffensiveObjective")]
        public static readonly int[] POINT_OffensiveObjective = [2, 1, 0, 0, 0, 0, 0, 0, 0, 3, 1, 3, 0, 0, 0, 3, 0, -2, -5];

        [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo.InitArrayNbPoints, POINT_Frag")]
        public static readonly int[] POINT_Frag = [2, 0, 0, 0, 0, 0, 0, 0, 0, 3, 1, 3, 0, 0, 0, 0, 0, -2, -5];

        /// <summary>The only table SGameInfo selects.</summary>
        [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo.InitArrayNbPoints, POINT_Multiple")]
        public static readonly int[] POINT_Multiple = [2, 2, 1, 2, 1, 2, 5, 5, 1, 0, 2, 3, 2, 2, 5, 0, 2, -2, -5];

        /// <summary>Any other mode name.</summary>
        [Provenance(ProvenanceKind.Decoded, "Engine.LevelInfo.InitArrayNbPoints, default branch")]
        public static readonly int[] Other = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, -2, -5];
    }

    /// <summary>Fills <see cref="ActionPoints"/> with the points of each scored action for a scoring mode.</summary>
    [Uc("Engine.LevelInfo.InitArrayNbPoints", Sha1 = "72ead77e791e7cec303e7baf1e753b5aa1db2a5c")]
    [PortOf("uc:Engine.LevelInfo:InitArrayNbPoints", Spec = "spec/game/scoring-and-stats.md#rules")]
    public void InitArrayNbPoints(string Mode)
    {
        int[] table = Mode switch
        {
            "POINT_Objective" => ScoringTables.POINT_Objective,
            "POINT_OffensiveObjective" => ScoringTables.POINT_OffensiveObjective,
            "POINT_Frag" => ScoringTables.POINT_Frag,
            "POINT_Multiple" => ScoringTables.POINT_Multiple,
            _ => ScoringTables.Other,
        };

        while (ActionPoints.Count < table.Length)
        {
            ActionPoints.Add(0);
        }

        for (int i = 0; i < table.Length; i++)
        {
            ActionPoints[i] = table[i];
        }
    }

    /// <summary>ActionPoints[Index]; an index past the end reads 0, as a dynamic array read does in UnrealScript.</summary>
    public int GetActionPoints(int Index) => Index >= 0 && Index < ActionPoints.Count ? ActionPoints[Index] : 0;

    /// <summary>
    /// Starts a server travel unless one is pending: stores the URL and lets the game tell the clients, or travels at
    /// once without a game. The engine changes the map when NextSwitchCountdown runs out.
    /// </summary>
    [Uc("Engine.LevelInfo.ServerTravel", Sha1 = "bb8a7d7297ff84e0c884f3d5ddf8ca48d52ce2b8")]
    [PortOf("uc:Engine.LevelInfo:ServerTravel", Spec = "spec/game/round-travel.md#rules")]
    public void ServerTravel(string URL, bool bItems)
    {
        if (NextURL.Length != 0)
        {
            return;
        }

        bNextItems = bItems;
        NextURL = URL ?? string.Empty;
        if (Game is not null)
        {
            Game.ProcessServerTravel(NextURL, bItems);
        }
        else
        {
            NextSwitchCountdown = 0f;
        }
    }

    /// <summary>The controller of this machine's own player (native).</summary>
    [UcNative("ALevelInfo::execGetLocalPlayerController")]
    public PlayerController? GetLocalPlayerController() => LocalPlayerController;
}
