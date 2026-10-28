using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController.PlayerTick, its last part: the mission reminder (spec/hud/messages.md "Mission reminder").
public partial class SPlayerController
{
    private static readonly ClassRef ReminderBombClass = new("SBase.SGadBomb");
    private static readonly SGameMessages ReminderTexts = new();

    /// <summary>
    /// A player whose pawn has stood still for <c>fTimeMissionMessage</c> (20 s) is reminded of the job: a spy
    /// carrying the Story disk is told to go to the drop zone (message 53), a spy in Sabotage without a charged
    /// modem is told so (message 55), and otherwise each mission not yet taken shows its description (the spies')
    /// or DescriptionDEF (the mercs') in the game message queue, which restarts the wait. Runs on the player's own
    /// machine, from the global PlayerTick (states that do not call it, like s_Dead, give no reminder).
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/hud/messages.md#mission-reminder")]
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PlayerTick tail: fLastTimeMoved, fTimeMissionMessage, messages 53 and 55, SetGameMessage(Description, 3)")]
    private void MissionReminder(SBasePawn pawn, float now)
    {
        if (!pawn.Velocity.IsZero())
        {
            fLastTimeMoved = now;
            return;
        }

        if (now - fLastTimeMoved <= fTimeMissionMessage || myHUD is not SHud hud)
        {
            return;
        }

        bool spy = pawn.SPawnType == PawnType.SPAWNOFFENSE;
        if (spy && pawn.CarriedFlag is not null)
        {
            // ReceiveLocalizedMessage(SGameMessages, 53): a queue switch with no sound.
            hud.SetGameMessage(ReminderTexts.ExtractionAtt, GameMessagePriority);
            return;
        }

        // A None modem reads a charge of 0 in the script.
        if (spy && GameReplicationInfo?.GameMode == EGameMode.GM_Sabotage &&
            (PawnInventory.FindGadgetType(pawn, ReminderBombClass)?.Charge ?? 0) == 0)
        {
            hud.SetGameMessage(ReminderTexts.SabotageNoModem, GameMessagePriority);
            return;
        }

        foreach (SMission mission in XLevel.AllActors<SMission>())
        {
            if (!mission.ObjectiveTaken)
            {
                hud.SetGameMessage(spy ? mission.Description : mission.DescriptionDEF, MissionMessagePriority);
                fLastTimeMoved = now;
            }
        }
    }

    [Provenance(ProvenanceKind.Decoded, "SHud.LocalizedMessage: SGameMessages queue switches use SetGameMessage(text, 2)")]
    private const byte GameMessagePriority = 2;

    [Provenance(ProvenanceKind.Decoded, "SPlayerController.PlayerTick: SetGameMessage(Mission.Description, 3)")]
    private const byte MissionMessagePriority = 3;
}
