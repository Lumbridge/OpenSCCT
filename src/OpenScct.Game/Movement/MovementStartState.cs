using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The movement part of SPlayerController.EnterStartState (spec/movement/walking.md, transition and resting states):
/// a spy starts crouched unarmed (SA_WANTS_TO_CROUCH and SA_WALK_CR_0H, both forced, crouched walking camera), a merc
/// standing with the rifle (SA_WALK_ST_2H forced, first-person camera, weapon loaded 2).
/// </summary>
public static class MovementStartState
{
    [PortOf("uc:SBase.SPlayerController:EnterStartState", Spec = "spec/movement/walking.md#transition-and-resting-states")]
    public static void Apply(SPlayerController PC, SBasePawn Pawn, bool bSpy)
    {
        IMovementHooks hooks = MovementService.HooksOf(PC);
        if (PC.XLevel is { } level)
        {
            MovementNotifies.Install(level);
        }

        if (bSpy)
        {
            Pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH, true);
            Pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H, true);
            hooks.SetCameraMode(PC, ECamMode.ECM_WalkingCr);
        }
        else
        {
            Pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H, true);
            hooks.SetCameraMode(PC, ECamMode.ECM_FirstPerson);
            hooks.SetWeaponLoaded(Pawn, 2);
        }
    }
}

/// <summary>
/// An <see cref="IGameFlowPorts"/> that adds the movement part of the start state (<see cref="MovementStartState"/>)
/// and passes every call on to another implementation (by default the local stand-in).
/// </summary>
public sealed class MovementGameFlowPorts : IGameFlowPorts
{
    public MovementGameFlowPorts(IGameFlowPorts? inner = null)
    {
        Inner = inner ?? GameFlowPortsLocal.Instance;
    }

    public IGameFlowPorts Inner { get; }

    public void EnterStartState(SPlayerController PC, SBasePawn Pawn, bool bSpy)
    {
        MovementStartState.Apply(PC, Pawn, bSpy);
        Inner.EnterStartState(PC, Pawn, bSpy);
    }

    public void AddDefaultInventory(SBasePawn Pawn, EGameMode Mode) => Inner.AddDefaultInventory(Pawn, Mode);

    public void DeleteAllInventory(SBasePawn Pawn) => Inner.DeleteAllInventory(Pawn);

    public void ResetVisionMode(SPlayerController PC) => Inner.ResetVisionMode(PC);

    public void CountPoisonDrain(SPlayerController PC) => Inner.CountPoisonDrain(PC);

    public void BeginDeath(SPlayerController PC) => Inner.BeginDeath(PC);

    public void EndDeath(SPlayerController PC) => Inner.EndDeath(PC);

    public void SpawnMagmaHud(SPlayerController PC) => Inner.SpawnMagmaHud(PC);

    public void ZoneTipTouch(SZoneTips Tip, Pawn Pawn) => Inner.ZoneTipTouch(Tip, Pawn);

    public void PlayWaiting(Pawn Pawn) => Inner.PlayWaiting(Pawn);

    public void PlayTeleportEffect(Pawn Pawn, bool bOut, bool bSound) => Inner.PlayTeleportEffect(Pawn, bOut, bSound);

    public void NativePlayerInput(SPlayerController PC, float DeltaTime) => Inner.NativePlayerInput(PC, DeltaTime);

    public void UpdateVoiceChatMuting(SGameInfo Game, bool bInit) => Inner.UpdateVoiceChatMuting(Game, bInit);

    public void HearVoiceChatter(SGameInfo Game, PlayerController Listener, PlayerController Speaker, bool Mute) =>
        Inner.HearVoiceChatter(Game, Listener, Speaker, Mute);
}
