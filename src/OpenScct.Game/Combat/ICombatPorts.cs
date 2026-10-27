using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Combat;

/// <summary>
/// Calls from the combat and equipment ports into units other areas own: the pawn action stream and its native
/// handler (movement), the walking-state tests and the waiting state (movement), the eye point, the animation
/// channels, bone placement and the repositioning native. Install an implementation with
/// <see cref="Level.SetService{T}"/>; without one, <see cref="CombatPortsLocal"/> gives the plain local behaviour
/// described on each member.
/// </summary>
public interface ICombatPorts
{
    /// <summary>
    /// SBasePawn.ChangeEnumAction: queue pawn action <paramref name="Action"/> (an SA_* constant) for the native action
    /// handler, which plays the clips and sets the action's physics, collision and downed flags.
    /// </summary>
    void ChangeEnumAction(SBasePawn Pawn, int Action, bool Forced);

    /// <summary>SPlayerController.GotoWaitingState: back to the walking state that matches side, stance and weapon.</summary>
    void GotoWaitingState(SPlayerController PC);

    /// <summary>Pawn.EyePosition: the eye offset from the pawn location (merc: EyeHeight on Z plus the view bob).</summary>
    FVector EyePosition(Pawn Pawn);

    /// <summary>
    /// The global and walking-state CanBeGrabbed of states the combat port does not own (true only in the merc
    /// weapon stances and the other states listed in spec/combat/spy-grab.md, evidence).
    /// </summary>
    bool CanBeGrabbed(SPlayerController PC);

    /// <summary>The global, walking and jump CanBeElbowShocked of states the combat port does not own.</summary>
    bool CanBeElbowShocked(SPlayerController PC);

    /// <summary>The global CanBeDropAttacked of states the combat port does not own.</summary>
    bool CanBeDropAttacked(SPlayerController PC);

    /// <summary>SPlayerController.ComputeCrouchOrRoll (movement): the crouch and roll buttons while input is free.</summary>
    void ComputeCrouchOrRoll(SPlayerController PC);

    /// <summary>True when a crouched pawn has room to stand up (the uncrouch test of the movement natives).</summary>
    bool CanUncrouch(SBasePawn Pawn);

    /// <summary>
    /// SBasePawn.SetInfoForVandamme: start the native repositioning (PHYS_Repositionnal) of <paramref name="Pawn"/> to
    /// <paramref name="Position"/> facing <paramref name="Rotation"/>. The native raises TopSynchro on the controller
    /// when the pawn arrives.
    /// </summary>
    void SetInfoForVandamme(SBasePawn Pawn, FVector Position, FRotator Rotation);

    /// <summary>SBasePawn.ChooseDeathAnimation (native 1405): the death direction for a momentum.</summary>
    EDeathDirection ChooseDeathAnimation(SBasePawn Pawn, FVector Momentum);

    /// <summary>True when the pawn's animation channel <paramref name="Channel"/> plays a clip.</summary>
    bool IsAnimating(SBasePawn Pawn, int Channel);

    /// <summary>The clip on the pawn's animation channel, or None.</summary>
    Name AnimSequence(SBasePawn Pawn, int Channel);

    /// <summary>The normalised frame (0 to 1) of the pawn's animation channel (GetAnimParams).</summary>
    float AnimFrame(SBasePawn Pawn, int Channel);

    /// <summary>The world placement of a bone of <paramref name="Actor"/> (GetBoneCoords), for the weapon FX bones.</summary>
    FCoords BoneCoords(Actor Actor, Name Bone);

    /// <summary>SPlayerController.ResetZones (movement): clears the GE zone search state.</summary>
    void ResetZones(SPlayerController PC);
}

/// <summary>
/// <see cref="ICombatPorts"/> over the movement port's pawn members (the action stream, the animation channels, the
/// crouch test, the crouch and roll latch), plus the downed flags of the KO actions (the estimated native contract
/// of spec/combat/knockout-and-terminate.md); the walking-state tests use the state names of the specs; the waiting
/// state is chosen by side, stance and weapon.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Local stand-in for units of other areas")]
public sealed class CombatPortsLocal : ICombatPorts
{
    public static readonly CombatPortsLocal Instance = new();

    private CombatPortsLocal()
    {
    }

    public void ChangeEnumAction(SBasePawn Pawn, int Action, bool Forced)
    {
        Pawn.ChangeEnumAction(Action, Forced);
        CombatNatives.ApplyActionFlags(Pawn, Action);
    }

    public void GotoWaitingState(SPlayerController PC) => PC.GotoState(CombatNatives.WaitingStateFor(PC));

    public FVector EyePosition(Pawn Pawn) => new(0f, 0f, Pawn.EyeHeight);

    public bool CanBeGrabbed(SPlayerController PC) => CombatDispatch.DefaultCanBeGrabbed(PC.GetStateName());

    public bool CanBeElbowShocked(SPlayerController PC) =>
        CombatDispatch.DefaultCanBeElbowShocked(PC.GetStateName(), PC.SBPawn?.bStickyShocking ?? false);

    public bool CanBeDropAttacked(SPlayerController PC) => CombatDispatch.DefaultCanBeDropAttacked(PC.GetStateName());

    public void ComputeCrouchOrRoll(SPlayerController PC) => PC.ComputeCrouchOrRoll();

    public bool CanUncrouch(SBasePawn Pawn) => Pawn.CanUnCrouch();

    public void SetInfoForVandamme(SBasePawn Pawn, FVector Position, FRotator Rotation)
    {
        Pawn.vVandammePos = Position;
        Pawn.vWallJumpDir = Rotation.Vector();
    }

    public EDeathDirection ChooseDeathAnimation(SBasePawn Pawn, FVector Momentum) =>
        CombatNatives.ChooseDeathAnimation(Pawn, Momentum);

    public bool IsAnimating(SBasePawn Pawn, int Channel) => Pawn.PawnAnim.IsAnimating(Channel);

    public Name AnimSequence(SBasePawn Pawn, int Channel) => Pawn.PawnAnim[Channel].Sequence;

    public float AnimFrame(SBasePawn Pawn, int Channel) => Pawn.PawnAnim[Channel].Frame;

    public FCoords BoneCoords(Actor Actor, Name Bone) => CombatNatives.EstimatedBoneCoords(Actor);

    public void ResetZones(SPlayerController PC)
    {
    }
}

/// <summary>Lookups of the combat services of a level.</summary>
public static class CombatServices
{
    /// <summary>The level's combat ports, or the local stand-in.</summary>
    public static ICombatPorts Ports(Actor actor) => actor.XLevel?.GetService<ICombatPorts>() ?? CombatPortsLocal.Instance;

    /// <summary>The level's combat network, or the null one (every player is on this machine).</summary>
    public static ICombatNet Net(Actor actor) => actor.XLevel?.GetService<ICombatNet>() ?? NullCombatNet.Instance;

    /// <summary>The level's hit zone resolver, or the trace over the collision world with the estimated pill map.</summary>
    public static IHitZones HitZones(Actor actor) => actor.XLevel?.GetService<IHitZones>() ?? TraceHitZones.Instance;
}
