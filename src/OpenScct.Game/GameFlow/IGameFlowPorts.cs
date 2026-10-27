using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.GameFlow;

/// <summary>
/// Calls from the game-flow port (spec/game) into units other areas own and port: inventory and vision (equipment), the controller's input, camera and pawn actions (movement and
/// camera), poison (combat), the HUD, zone tips and voice chat. Install an implementation with
/// <see cref="Level.SetService{T}"/>; without one, <see cref="GameFlowPortsLocal"/> applies the plain local
/// behaviour described on each member.
/// </summary>
public interface IGameFlowPorts
{
    /// <summary>SBasePawn.UpdateInventory then AddDefaultGadgets for the game mode (inventory spec).</summary>
    void AddDefaultInventory(SBasePawn Pawn, EGameMode Mode);

    /// <summary>SBasePawn.DeleteAllInventory.</summary>
    void DeleteAllInventory(SBasePawn Pawn);

    /// <summary>SGoggle.ResetVisionMode on the controller's goggles.</summary>
    void ResetVisionMode(SPlayerController PC);

    /// <summary>SPlayerController.CountPoisonDrain (damage spec).</summary>
    void CountPoisonDrain(SPlayerController PC);

    /// <summary>
    /// The side-specific start of a spawn that other areas own: camera mode, the forced pawn actions, the merc HUD
    /// flash, weapon loading and selection, the reticle (SPlayerController.EnterStartState).
    /// </summary>
    void EnterStartState(SPlayerController PC, SBasePawn Pawn, bool bSpy);

    /// <summary>
    /// The death entry other areas own (SPlayerController s_Dead.BeginState): zip line sound, screen effect, radar
    /// marks, gadget unload, the sticky shock end, voice chat, HUD alarms, the death camera. The disks, the hand item
    /// and the Story disk are dropped by the state itself.
    /// </summary>
    void BeginDeath(SPlayerController PC);

    /// <summary>The owner's death screen effect off (SPlayerController s_Dead.EndState).</summary>
    void EndDeath(SPlayerController PC);

    /// <summary>SPlayerController.SpawnMagmaHud (offline matches).</summary>
    void SpawnMagmaHud(SPlayerController PC);

    /// <summary>SZoneTips.Touch for a zone tip whose cylinder holds the new pawn's start (tips spec).</summary>
    void ZoneTipTouch(SZoneTips Tip, Pawn Pawn);

    /// <summary>The idle animation of a pawn taken over on reconnect (Pawn.PlayWaiting).</summary>
    void PlayWaiting(Pawn Pawn);

    /// <summary>Pawn.PlayTeleportEffect: a noise for AI hearing at the spawn.</summary>
    void PlayTeleportEffect(Pawn Pawn, bool bOut, bool bSound);

    /// <summary>NativePlayerInput: turns the player's raw input into aForward, aTurn and the other axes.</summary>
    void NativePlayerInput(SPlayerController PC, float DeltaTime);

    /// <summary>SGameInfo.UpdateVoiceChatMuting (voice spec).</summary>
    void UpdateVoiceChatMuting(SGameInfo Game, bool bInit);

    /// <summary>GameInfo.HearVoiceChatter (voice spec): mute or unmute <paramref name="Speaker"/> for <paramref name="Listener"/>.</summary>
    void HearVoiceChatter(SGameInfo Game, PlayerController Listener, PlayerController Speaker, bool Mute);
}

/// <summary>
/// <see cref="IGameFlowPorts"/> until the owning areas install theirs: every call does nothing (presentation, input,
/// inventory, vision, voice and poison stay with their areas).
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Local stand-in for units of other areas")]
public sealed class GameFlowPortsLocal : IGameFlowPorts
{
    public static readonly GameFlowPortsLocal Instance = new();

    private GameFlowPortsLocal()
    {
    }

    public void AddDefaultInventory(SBasePawn Pawn, EGameMode Mode)
    {
    }

    public void DeleteAllInventory(SBasePawn Pawn)
    {
    }

    public void ResetVisionMode(SPlayerController PC)
    {
    }

    public void CountPoisonDrain(SPlayerController PC)
    {
    }

    public void EnterStartState(SPlayerController PC, SBasePawn Pawn, bool bSpy)
    {
    }

    public void BeginDeath(SPlayerController PC)
    {
    }

    public void EndDeath(SPlayerController PC)
    {
    }

    public void SpawnMagmaHud(SPlayerController PC)
    {
    }

    public void ZoneTipTouch(SZoneTips Tip, Pawn Pawn)
    {
    }

    public void PlayWaiting(Pawn Pawn)
    {
    }

    public void PlayTeleportEffect(Pawn Pawn, bool bOut, bool bSound)
    {
    }

    public void NativePlayerInput(SPlayerController PC, float DeltaTime)
    {
    }

    public void UpdateVoiceChatMuting(SGameInfo Game, bool bInit)
    {
    }

    public void HearVoiceChatter(SGameInfo Game, PlayerController Listener, PlayerController Speaker, bool Mute)
    {
    }
}

/// <summary>Lookups of the game-flow services of a level.</summary>
public static class GameFlowServices
{
    /// <summary>The level's game-flow ports, or the local stand-in.</summary>
    public static IGameFlowPorts Ports(Actor actor) => actor.XLevel.GetService<IGameFlowPorts>() ?? GameFlowPortsLocal.Instance;

    /// <summary>The level's game host, or the null host.</summary>
    public static IGameHost Host(Actor actor) => actor.XLevel.GetService<IGameHost>() ?? NullGameHost.Instance;
}
