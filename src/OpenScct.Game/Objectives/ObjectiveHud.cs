using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// The HUD lists objective code feeds (spec/objectives/messages.md rules 8 and 9): <c>SHud.ActorToDraw</c> and the
/// merc radar list <c>SHud.ObjectivesList</c>. Drawing them belongs to the HUD port.
/// </summary>
public static class ObjectiveHud
{
    /// <summary>
    /// <c>SHud.AddRemoveActorOnHud(A, bAdd)</c>: an add puts the actor at the front once; a remove takes out every
    /// copy.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "messages.md rule 8: ActorToDraw added at the front, removed by every match")]
    public static void AddRemoveActorOnHud(SHud hud, Actor A, bool bAdd)
    {
        if (bAdd)
        {
            if (!hud.ActorToDraw.Contains(A))
            {
                hud.ActorToDraw.Insert(0, A);
            }

            return;
        }

        hud.ActorToDraw.RemoveAll(a => ReferenceEquals(a, A));
    }

    /// <summary><c>SHud.AddObjective(Trigger)</c>: the trigger joins the merc radar list once.</summary>
    [UcNative("ASHud::execAddObjective", Exe = 0x10A91C50, Editor = 0x111C31C0)]
    [PortOf("nat:SBase.SHud.AddObjective", Spec = "spec/objectives/messages.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected list upkeep of SHud.ObjectivesList; drawing is the HUD port's")]
    public static void AddObjective(SHud hud, SObjectiveTrigger Trigger)
    {
        if (!hud.ObjectivesList.Contains(Trigger))
        {
            hud.ObjectivesList.Add(Trigger);
        }
    }

    /// <summary><c>SHud.RemoveObjective(Trigger)</c>: every copy leaves the merc radar list.</summary>
    [UcNative("ASHud::execRemoveObjective", Exe = 0x10A91CF0, Editor = 0x111C32C0)]
    [PortOf("nat:SBase.SHud.RemoveObjective", Spec = "spec/objectives/messages.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected list upkeep of SHud.ObjectivesList; drawing is the HUD port's")]
    public static void RemoveObjective(SHud hud, SObjectiveTrigger Trigger) =>
        hud.ObjectivesList.RemoveAll(t => ReferenceEquals(t, Trigger));
}
