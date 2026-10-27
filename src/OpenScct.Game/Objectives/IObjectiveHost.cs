using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// Calls from the objective port into the game rules (<c>SGameInfo.AddPoint</c> and
/// <c>SGameInfo.NotifyObjectiveTaken</c>, spec/game). Install another implementation with
/// <see cref="Level.SetService{T}"/> to observe or replace them; <see cref="ObjectiveHostLocal"/> calls the level's
/// SGameInfo.
/// </summary>
public interface IObjectiveHost
{
    /// <summary><c>SGameInfo.AddPoint(0, Scorer, ActionIndex)</c>.</summary>
    void AddPoint(Controller? Scorer, int ActionIndex);

    /// <summary><c>SGameInfo.NotifyObjectiveTaken(Objective)</c>: jingle, score broadcast time and the end check.</summary>
    void NotifyObjectiveTaken(SObjective Objective);
}

/// <summary><see cref="IObjectiveHost"/> over the level's SGameInfo; nothing happens in a level without one.</summary>
public sealed class ObjectiveHostLocal : IObjectiveHost
{
    public static readonly ObjectiveHostLocal Instance = new();

    private ObjectiveHostLocal()
    {
    }

    [Provenance(ProvenanceKind.Decoded, "missions.md rule 25: AddPoint(0, Scorer, AP_A_TakeObjective)")]
    public void AddPoint(Controller? Scorer, int ActionIndex)
    {
        if (Scorer?.Level.Game is SGameInfo game)
        {
            game.AddPoint(0, Scorer as PlayerController, ActionIndex);
        }
    }

    public void NotifyObjectiveTaken(SObjective Objective) => (Objective.Level.Game as SGameInfo)?.NotifyObjectiveTaken(Objective);
}
