using OpenScct.Game.Equipment;

namespace OpenScct.Game.SBase;

// The mid-round gear change (an OpenSCCT addition, spec/additions/gear-change.md): the server decides whether a
// player's newly chosen kit goes on now or at the next spawn. The next spawn needs nothing here: every spawn asks
// the player's machine for its kit, which reads the player's saved choice.
public partial class SGameInfo
{
    /// <summary>
    /// The match rule: the new kit goes on now while the pawn is alive, spawned at most
    /// <see cref="GearK.SwapWindowSeconds"/> ago and has used nothing of its kit; otherwise at the next spawn. In a
    /// practice round it goes on now whenever the pawn is alive.
    /// </summary>
    public virtual GearChangeResult CanChangeGearNow(SPlayerController PC, out float WaitSeconds)
    {
        WaitSeconds = 0f;
        if (PC.Pawn is SBasePawn { bDeleteMe: false, Health: > 0 } pawn &&
            (Modes.PracticeRules.IsOn(this) ||
             (Level.TimeSeconds - pawn.LastStartTime <= GearK.SwapWindowSeconds && !Loadouts.HasUsedKit(pawn))))
        {
            return GearChangeResult.Now;
        }

        return GearChangeResult.NextSpawn;
    }

    /// <summary>
    /// Swaps the pawn's kit now: the selected gadget is unloaded and the whole inventory deleted (as at a death),
    /// then the spawn inventory is asked for again from the owner's machine. The pawn's spawn time is kept, so the
    /// swap window does not start again.
    /// </summary>
    public void ChangeGearNow(SBasePawn Pawn)
    {
        Pawn.SelectedGadget?.Unload();
        Ports.DeleteAllInventory(Pawn);
        AddDefaultInventory(Pawn);
    }
}
