using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.Movement;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The co-op moves' controller functions: starting one (key T, PerformCoopMvt), the mate search natives and the
// server request that starts both halves (spec/movement/coop.md, spec/native/ANSWERS-moveset.md M1 and M2).
public partial class SPlayerController
{
    /// <summary>The co-op key handler of a state (PerformCoopMvt).</summary>
    public interface IPerformCoopMvt
    {
        void PerformCoopMvt(SPlayerController self);
    }

    /// <summary>The co-op key (exec PerformCoopMvt, key T) through the current state.</summary>
    public void eventPerformCoopMvt()
    {
        if (StateNode is IPerformCoopMvt s)
        {
            s.PerformCoopMvt(this);
        }
        else
        {
            PerformCoopMvt();
        }
    }

    /// <summary>The global co-op key: nothing (the walking and hanging states start a move).</summary>
    [Uc("SBase.SPlayerController.PerformCoopMvt", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:PerformCoopMvt", Spec = "spec/movement/coop.md#starting-docoopinteraction-from-performcoopmvt-in-mario-walking-or-onledge")]
    public virtual void PerformCoopMvt()
    {
    }

    /// <summary>
    /// Starts a co-op move: with no ready mate this player becomes the helper (the short ladder at a spot under a
    /// ledge, or the hoist while hanging); with a mate ready to boost or hanging ready to hoist, the climber's
    /// approach. Nothing in the tutorial levels.
    /// </summary>
    [Uc("SBase.SPlayerController.DoCoopInteraction", Sha1 = "3135abcd2b862dfa84816f087c2dee4bc258faa3")]
    [PortOf("uc:SBase.SPlayerController:DoCoopInteraction", Spec = "spec/movement/coop.md#starting-docoopinteraction-from-performcoopmvt-in-mario-walking-or-onledge")]
    public bool DoCoopInteraction()
    {
        if (CoopTutorialLevel() || SBPawn is not { } pawn)
        {
            return false;
        }

        switch (CheckIfCanInteractWithMate(out Actor? mate))
        {
            case 0:
                if (eventCanShortLadderHelp() && pawn.CheckIfCanStartSHL(out FVector destLoc, out FVector destRot))
                {
                    if (destLoc != pawn.Location || destRot != pawn.Rotation.Vector())
                    {
                        pawn.vVandammePos = destLoc;
                        pawn.SetInfoForVandamme(destRot);
                        eventChangeState(MovementStateNames.s_ShortLadder_P_GotoDest);
                    }
                    else
                    {
                        eventChangeState(MovementStateNames.s_ShortLadder_P_Waiting);
                    }

                    return true;
                }

                if (eventCanHoistClimbHelp() && pawn.FindHoistClimbType(true) > 0)
                {
                    eventChangeState(MovementStateNames.s_Hoist_P_Waiting);
                    return true;
                }

                return false;
            case CA_ShortLadder:
                if (AreaIsSafeToInteract(mate))
                {
                    pawn.m_InteractionTarget = mate as SBasePawn;
                    SetMateTarget(mate as SBasePawn);
                    eventChangeState(MovementStateNames.s_ShortLadder_S_CheckIfOK);
                    return true;
                }

                break;
            case CA_HoistClimb:
                pawn.m_InteractionTarget = mate as SBasePawn;
                SetMateTarget(mate as SBasePawn);
                eventChangeState(MovementStateNames.s_Hoist_S_CheckIfOK);
                return true;
        }

        if (eventCanShortLadderHelp())
        {
            eventChangeState(MovementStateNames.s_ShortLadder_P_Waiting);
            return true;
        }

        return false;
    }

    /// <summary>The mate to the server (reliable client to server); on the server, the pawn's interaction target.</summary>
    [Uc("SBase.SPlayerController.SetMateTarget", Sha1 = "bd16a48fda88a48659f37ae2202d84d375d9ec09")]
    [PortOf("uc:SBase.SPlayerController:SetMateTarget", Spec = "spec/movement/coop.md#network")]
    [PortOf("rep:SBase.SPlayerController.SetMateTarget", Spec = "spec/movement/coop.md#network")]
    public void SetMateTarget(SBasePawn? tgt)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            MovementNet.Of(this).SetMateTarget(this, tgt);
            return;
        }

        if (SBPawn is { } pawn)
        {
            pawn.m_InteractionTarget = tgt;
        }
    }

    /// <summary>
    /// The climber reached the spot (reliable client to server): the server starts both halves at once (the
    /// climber's jump and the helper's wait for it), or sends the climber back to walking when the mate is gone,
    /// dead or no longer ready.
    /// </summary>
    [Uc("SBase.SPlayerController.AskServerToStartCoopInteraction", Sha1 = "a3b71d829c20d0344f8383abd4debdbc0d198efd")]
    [PortOf("uc:SBase.SPlayerController:AskServerToStartCoopInteraction", Spec = "spec/movement/coop.md#short-ladder-climber")]
    [PortOf("rep:SBase.SPlayerController.AskServerToStartCoopInteraction", Spec = "spec/movement/coop.md#network")]
    public void AskServerToStartCoopInteraction()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            MovementNet.Of(this).AskServerToStartCoopInteraction(this);
            return;
        }

        if (SBPawn is not { } pawn || pawn.m_InteractionTarget is not { } mate || pawn.Health <= 0 || mate.Health <= 0)
        {
            GotoWaitingState();
        }
        else if (mate.bReadyForHoistClimb)
        {
            if (!IsInState(MovementStateNames.s_Hoist_S_Jump))
            {
                eventMirrorState(MovementStateNames.s_Hoist_S_Jump);
                (mate.Controller as SPlayerController)?.eventMirrorState(MovementStateNames.s_Hoist_P_WaitForHoist);
            }
        }
        else if (mate.bReadyForShortLadder)
        {
            if (!IsInState(MovementStateNames.s_ShortLadder_S_Jump))
            {
                eventMirrorState(MovementStateNames.s_ShortLadder_S_Jump);
                (mate.Controller as SPlayerController)?.eventMirrorState(MovementStateNames.s_ShortLadder_P_WaitForShortLadder);
            }
        }
        else
        {
            GotoWaitingState();
        }
    }

    /// <summary>
    /// The mate search (ANSWERS-moveset.md M2): 1 for a team mate ready to boost in front and facing this player
    /// (the boost spot's ledge then goes to ZipP), 3 for one hanging ready to hoist above, 0 for none. The pawns are
    /// walked in the level's order; the first match decides.
    /// </summary>
    [UcNative("ASPlayerController::CheckIfCanInteractWithMate", Exe = 0x10aae290, Editor = 0x111effc0)]
    [Uc("SBase.SPlayerController.CheckIfCanInteractWithMate", Sha1 = "e38e27ade6e94c6332c3c305ca9ce51c631b021e")]
    [PortOf("nat:SBase.SPlayerController.CheckIfCanInteractWithMate", Spec = "spec/native/ANSWERS-moveset.md#m2-checkifcaninteractwithmateout-mate---byte-editor-0x111effc0-exec-passes-a-second-argument-0")]
    [Provenance(ProvenanceKind.Estimated, "ANSWERS-moveset.md Open 1: the level's SBasePawns in spawn order stand for the native's pawn array")]
    public byte CheckIfCanInteractWithMate(out Actor? MateFound) => CheckIfCanInteractWithMate(out MateFound, false);

    /// <summary>
    /// The mate search with the native's second argument: with <paramref name="bNoBoostSpot"/> (the spy HUD's coop
    /// icons) a ready boost helper in reach answers 1 at once, without the boost spot test and without touching
    /// ZipP; the key's call (exec) passes false.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "CheckIfCanInteractWithMate game 0x10aadc30: param_2 skips FUN_10ae6d40 and the ZipP writes; the spy HUD driver 0x10b2db20 passes 1")]
    public byte CheckIfCanInteractWithMate(out Actor? MateFound, bool bNoBoostSpot)
    {
        MateFound = null;
        if (SBPawn is not { } pawn || MovementNatives.Driver(pawn) is not { } driver)
        {
            return CA_None;
        }

        foreach (Actor a in XLevel.Actors)
        {
            if (a is not SBasePawn m || ReferenceEquals(m, pawn) || m.bDeleteMe || m.SPawnType != SPawnType)
            {
                continue;
            }

            if (!m.bReadyForShortLadder)
            {
                if (m.bReadyForHoistClimb && driver.CoopHoistMateInReach(pawn, m))
                {
                    MateFound = m;
                    return CA_HoistClimb;
                }

                continue;
            }

            if (!driver.CoopBoostMateInReach(pawn, m, fMaxDistToShortLadder))
            {
                continue;
            }

            MateFound = m;
            if (bNoBoostSpot)
            {
                return CA_ShortLadder;
            }

            if (!driver.CheckIfCanStartSHL(m, false, out _, out _, out FVector p1, out FVector p2))
            {
                return CA_None;
            }

            pawn.ZipP1 = p1;
            pawn.ZipP2 = p2;
            pawn.SendZipPToServer();
            return CA_ShortLadder;
        }

        return CA_None;
    }

    /// <summary>Room for the boost approach: the spot 70 before the mate reachable and on a floor (ANSWERS-moveset.md M1).</summary>
    [UcNative("ASPlayerController::execAreaIsSafeToInteract", Exe = 0x10aad9f0, Editor = 0x111e6d20)]
    [Uc("SBase.SPlayerController.AreaIsSafeToInteract", Sha1 = "f5e91d51d184226bc1ffe2e2a1aafc62cbf27706")]
    [PortOf("nat:SBase.SPlayerController.AreaIsSafeToInteract", Spec = "spec/native/ANSWERS-moveset.md#m1-areaissafetointeracttarget---bool-editor-exec-0x111e6d20")]
    public bool AreaIsSafeToInteract(Actor? Target) =>
        Target is not null && SBPawn is { } pawn && MovementNatives.Driver(pawn) is { } driver && driver.AreaIsSafeToInteract(pawn, Target);

    /// <summary>SPersistentData.CurLevelIsTutorial for the co-op key (<see cref="TutorialMaps"/>).</summary>
    public bool CoopTutorialLevel() =>
        TutorialMaps.CurLevelIsTutorial(GameFlowServices.Host(this).GetURLMap());

    /// <summary>
    /// The helpers' cancel inputs (short ladder and hoist waiting states): reload, rate of fire, jump, fire mode,
    /// fire, or the stick outside the 100 square.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "s_ShortLadder_P_Waiting.PlayerMove, s_Hoist_P_Waiting.PlayerMove: the 100 square")]
    private bool CoopHelperCancels() =>
        bReload > 0 || bROF > 0 || bPressedJump || bFireMode > 0 || bFire > 0 || aForward > MovementK.CoopCancelStick ||
        aForward < -MovementK.CoopCancelStick || aStrafe > MovementK.CoopCancelStick || aStrafe < -MovementK.CoopCancelStick;
}
