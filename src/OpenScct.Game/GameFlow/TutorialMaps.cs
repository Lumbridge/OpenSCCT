using System;
using System.Collections.Generic;
using OpenScct.Provenance;

namespace OpenScct.Game.GameFlow;

/// <summary>
/// The tutorial maps in the order the tutorials menu plays them (spec/game/tutorials.md): the spy maps, the exam last,
/// then the merc maps. Team 0 is the spies, team 1 the mercs.
/// </summary>
public static class TutorialMaps
{
    /// <summary>SPersistentData.TutoMapsA: the spy tutorials, the exam last.</summary>
    [PortOf("uc:SBase.SPersistentData:InitTutorials", Spec = "spec/game/tutorials.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SPersistentData.InitTutorials: TutoMapsA, TutoMapsASize 5")]
    public static readonly IReadOnlyList<string> Spy = ["TSP20", "TSP21", "TSP22", "TSP23", "TSP29"];

    /// <summary>SPersistentData.TutoMapsD: the merc tutorials.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPersistentData.InitTutorials: TutoMapsD, TutoMapsDSize 3")]
    public static readonly IReadOnlyList<string> Merc = ["TME30", "TME32", "TME33"];

    /// <summary>The exam's index in <see cref="Spy"/> (the last spy map, MarkTutoAsDone).</summary>
    public static int ExamIndex => Spy.Count - 1;

    /// <summary>The maps of a team (0 spies, 1 mercs).</summary>
    public static IReadOnlyList<string> Of(int NumTeam) => NumTeam == 0 ? Spy : Merc;

    /// <summary>
    /// SPersistentData.CurLevelIsTutorial: true when <paramref name="LevelName"/> is one of the tutorial maps, with its
    /// index and team (the spy list is searched first). The original compares the name exactly; map codes here are
    /// compared without case, as the map files are found.
    /// </summary>
    [PortOf("uc:SBase.SPersistentData:CurLevelIsTutorial", Spec = "spec/game/tutorials.md#rules")]
    public static bool CurLevelIsTutorial(string? LevelName, out int NumTut, out int NumTeam)
    {
        for (NumTeam = 0; NumTeam < 2; NumTeam++)
        {
            IReadOnlyList<string> maps = Of(NumTeam);
            for (NumTut = 0; NumTut < maps.Count; NumTut++)
            {
                if (string.Equals(LevelName, maps[NumTut], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        NumTut = -1;
        NumTeam = -1;
        return false;
    }

    /// <summary>SPersistentData.CurLevelIsTutorial without the index.</summary>
    public static bool CurLevelIsTutorial(string? LevelName) => CurLevelIsTutorial(LevelName, out _, out _);

    /// <summary>
    /// The loading page's pick when no map is named (GUI.loading.Timer, "TutoSpy" / "TutoSentry"): the first map of the
    /// team that is not done, or null when every one is (the original then reloads the current map).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.loading.Timer, offline TutoSpy / TutoSentry without a map: the first map whose GetMapTutorialDone is false")]
    public static string? NextTutorial(int NumTeam, Func<int, bool> Done)
    {
        ArgumentNullException.ThrowIfNull(Done);
        IReadOnlyList<string> maps = Of(NumTeam);
        for (int i = 0; i < maps.Count; i++)
        {
            if (!Done(i))
            {
                return maps[i];
            }
        }

        return null;
    }
}
