using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// Mission counters other classes read (spec/objectives/missions.md rule 32). The match start (rules 2 to 4) is in
/// <c>SGameInfo.StartMatch</c> and the end check's objective clause is <see cref="SObjective.AllObjectivesTaken"/>.
/// </summary>
public static class MissionRules
{
    /// <summary>
    /// <c>SPlayerController.GetObjectivesLeft()</c> (rule 32): the TotalObjectivesLeft of every mission that has a
    /// game mode. The HUD shows it against GRI.NumObjectives.
    /// </summary>
    [Uc("SBase.SPlayerController.GetObjectivesLeft", Sha1 = "40341ef5a95a6dce04a883be7fc6af5584869b41")]
    [PortOf("uc:SBase.SPlayerController:GetObjectivesLeft", Spec = "spec/objectives/missions.md#rules")]
    [PortOf("nat:Engine.Actor.AllObjectives", Spec = "spec/objectives/missions.md#rules")]
    public static int GetObjectivesLeft(Level level)
    {
        int total = 0;
        foreach (SMission mission in level.AllActors<SMission>())
        {
            if (mission.GameMode != EGameMode.GM_Undefined)
            {
                total += mission.TotalObjectivesLeft;
            }
        }

        return total;
    }
}
