using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The spy binoculars' objective mark (spec/equipment/spy-binoculars.md rule 8): the HUD keeps one marked objective;
/// the Use key toggles the objective under the reticle.
/// </summary>
public static class SpyBinoculars
{
    /// <summary>
    /// The objective under the reticle: the first objective trigger in front of the view whose direction lies within
    /// the reticle cone, or null.
    /// </summary>
    [PortOf("nat:SBase.SHud.DrawObjectivesBinocularsAtt", Spec = "spec/equipment/spy-binoculars.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The HUD tests the projected marker against a 40 px box at the screen centre; the port tests the view direction (cone cosine 0.999)")]
    public static SObjectiveTrigger? ObjectiveUnderReticle(SPlayerController PC)
    {
        if (PC.Pawn is not { } pawn || PC.XLevel is not { } level)
        {
            return null;
        }

        FVector eye = pawn.Location + CombatServices.Ports(PC).EyePosition(pawn);
        PC.Rotation.GetAxes(out FVector x, out _, out _);
        foreach (SObjectiveTrigger objective in level.AllActors<SObjectiveTrigger>())
        {
            FVector to = objective.Location - eye;
            float d = to.Size();
            if (d <= 0f)
            {
                continue;
            }

            if (to.Dot(x) / d >= K.SpyBinoMarkConeCos)
            {
                return objective;
            }
        }

        return null;
    }

    /// <summary>
    /// LockObjectiveBinocularsAtt (native, decoded 0x10a90f40): the mark is cleared when it already is the objective under
    /// the reticle, else it becomes that objective (none when nothing is under the reticle).
    /// </summary>
    [UcNative("ASHud::execLockObjectiveBinocularsAtt", Exe = 0x10a90f40)]
    [PortOf("nat:SBase.SHud.LockObjectiveBinocularsAtt", Spec = "spec/equipment/spy-binoculars.md#rules")]
    public static void LockObjectiveBinocularsAtt(SHud Hud, SPlayerController PC)
    {
        SObjectiveTrigger? current = ObjectiveUnderReticle(PC);
        Hud.LockedObjective = ReferenceEquals(Hud.LockedObjective, current) ? null : current;
        GadgetFx.Notify(PC, GadgetFx.BinocularsMark, default, Hud.LockedObjective?.ActorId ?? 0);
    }
}
