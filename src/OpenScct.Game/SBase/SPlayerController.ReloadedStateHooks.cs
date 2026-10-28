using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using K = OpenScct.Game.Objectives.K;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// Reloaded.Core's state entry hooks that change gameplay (spec/native/reloaded-hooks.md "State entry hooks"): Reloaded
// watches every state the controller enters (its GotoState wrapper, native 1175) and acts on a few of them. The ones
// ported here are the fast leave of a hack (rl:fix.ko_after_interrupt) and the time a jump started, which the waist
// ledge grab reads (spec/movement/ledge.md "Reloaded ledge assist").
public partial class SPlayerController
{
    /// <summary>Level.TimeSeconds when this controller last entered s_PlayerJump (-1: never).</summary>
    public float ReloadedJumpStartTime = -1f;

    /// <summary>Set when the player of this machine entered a hack's fast leave: the next tick asks for walking again.</summary>
    public bool bReloadedFastLeavePending;

    /// <summary>
    /// Reloaded's state entry hook, after each state change: entering s_PlayerJump remembers the time; the local
    /// player entering s_Computer_FastLeave or s_Hack_FastLeave sets the fast leave flag for its next tick.
    /// </summary>
    [PortOf("rl:fix.ko_after_interrupt", Spec = "spec/native/reloaded-hooks.md#state-entry-hooks")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core OnNewState 0x10017d70: s_PlayerJump stores the time; s_Computer_FastLeave and s_Hack_FastLeave set a flag (0x10017ca0) that the frame hook 0x100a74d0 turns into one GotoWaitingState call on the next frame")]
    [Provenance(ProvenanceKind.Estimated, "The jump time is kept for every controller (Reloaded keeps it for the local player only), so the server and the owner's prediction agree")]
    private void ReloadedStateEntered(Name newState)
    {
        if (newState == MovementStateNames.s_PlayerJump)
        {
            ReloadedJumpStartTime = Level.TimeSeconds;
        }

        if ((newState == K.s_Computer_FastLeave || newState == K.s_Hack_FastLeave) && IAmOnClient())
        {
            bReloadedFastLeavePending = true;
        }
    }

    /// <summary>
    /// The next tick of the local player after a hack's fast leave: GotoWaitingState once more. The fast leave's
    /// BeginState already went to walking on this machine, but its ChangeState reached the server before the outer
    /// ChangeState of the fast leave itself, so the server stayed in the fast leave (where the spy could not be
    /// knocked out or elbowed). The second call sends walking again.
    /// </summary>
    [PortOf("rl:fix.ko_after_interrupt", Spec = "spec/native/reloaded-hooks.md#state-entry-hooks")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x100a74d0: flag 0x1036ae81 set -> FUN_1000a1d0 (script GotoWaitingState on the local controller), flag cleared")]
    private void ReloadedLocalTick()
    {
        if (!bReloadedFastLeavePending)
        {
            return;
        }

        bReloadedFastLeavePending = false;
        if (SBPawn is not null)
        {
            GotoWaitingState();
        }
    }

    /// <summary>
    /// Reloaded's WaistLedge hook: the waist grab is not sent until 1/64 s after the controller entered s_PlayerJump,
    /// so a jump does not catch a waist ledge in the frame it starts. At the 60 Hz step that is only the same tick.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1001a210 at game 0x10ab2df0 (eventWaistLedge): sent only when the high resolution time since the s_PlayerJump entry is at least 1e9 / 64 ns")]
    private bool ReloadedWaistLedgeBlocked() =>
        ReloadedJumpStartTime >= 0f && Level.TimeSeconds - ReloadedJumpStartTime < MovementK.ReloadedWaistAfterJump;

    /// <summary>The ledge hang test of the wall jump climb (s_WallJumpSalto) takes a deeper window (Reloaded).</summary>
    bool ISPlayerControllerPhysics.bInWallJumpSalto => StateNode.Name == MovementStateNames.s_WallJumpSalto;
}
