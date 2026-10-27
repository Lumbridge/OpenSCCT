using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController as the SCCT natives see it (docs/PHYSICS.md, pawn movement API): the fields the physics
// writes, handed out by reference, and the events it raises (the dispatchers live with the movement functions).
public partial class SPlayerController : ISPlayerControllerPhysics
{
    ref float ISPlayerControllerPhysics.fZMaxFalling => ref fZMaxFalling;

    ref float ISPlayerControllerPhysics.fLastTimeOnAMover => ref fLastTimeOnAMover;

    bool ISPlayerControllerPhysics.bTestBackFeetGrab => bTestBackFeetGrab;

    float ISPlayerControllerPhysics.fDistanceForContextualJump => fDistanceForContextualJump;

    public interface IStickyBlast
    {
        void StickyBlast(SPlayerController self, Actor? Other);
    }

    /// <summary>
    /// The walking or falling physics found a sticky-skinned pawn to land on (drop-attack.md rule 13): the state's
    /// version, nothing in the states that ignore it (the blast states), else the global StickyBlast.
    /// </summary>
    public void eventStickyBlast(Actor? Other)
    {
        if (StateNode is IStickyBlast s)
        {
            s.StickyBlast(this, Other);
            return;
        }

        if (Combat.CombatStateEvents.Lookup(this, "StickyBlast", out _) != Combat.CombatStateEvents.StateFunction.Ignored)
        {
            StickyBlast(Other);
        }
    }

    /// <summary>UE2 Controller.MayFall: nothing in this game (no bot path following).</summary>
    public void eventMayFall()
    {
    }

    public interface IClientStopRM
    {
        void ClientStopRM(SPlayerController self, int TheAction);
    }

    /// <summary>The server refused a root-motion action of this client (the global version is empty).</summary>
    [Uc("SBase.SPlayerController.ClientStopRM", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:ClientStopRM", Spec = "spec/native/physics-rootmotion-repositioning.md#bservertestrootmotionaction-vmove-yaw-server")]
    public void eventClientStopRM(int TheAction)
    {
        if (StateNode is IClientStopRM s)
        {
            s.ClientStopRM(this, TheAction);
        }
    }
}
