using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Combat;

/// <summary>
/// The combat side of controller functions that several areas share. The state interfaces themselves live on
/// <see cref="SPlayerController"/> (the movement port declares IPlayerMove, INotifyBump, ICanBeGrabbed and the other
/// permission predicates); the combat states implement them. The routers here ask the current state when it defines
/// the function, else the state tables of the combat specs (so states no port has defined yet answer as the
/// original), else the ports.
/// </summary>
public static class CombatStateEvents
{
    /// <summary>A state version of DoInteraction (the Use key's interaction step; grab and terminate states refuse it).</summary>
    public interface IDoInteraction
    {
        bool DoInteraction(SPlayerController self);
    }

    /// <summary>A state version of ReceiveAnEvent (SE_* events between a player's owner and the server).</summary>
    public interface IReceiveAnEvent
    {
        void ReceiveAnEvent(SPlayerController self, SentEventType_ Event);
    }

    /// <summary>The current state's DoInteraction. <paramref name="handled"/> is false when no combat state defines it.</summary>
    public static bool DoInteraction(SPlayerController pc, out bool handled)
    {
        handled = pc.StateNode is IDoInteraction;
        return pc.StateNode is IDoInteraction s && s.DoInteraction(pc);
    }

    /// <summary>CanBeGrabbed through the current state, else the grab spec's table of states that allow it.</summary>
    public static bool CanBeGrabbed(SPlayerController pc) => PlayRules.Of(pc).PawnsAttackPawns &&
        (pc.StateNode is SPlayerController.ICanBeGrabbed ? pc.eventCanBeGrabbed() : CombatServices.Ports(pc).CanBeGrabbed(pc));

    /// <summary>CanBeElbowShocked through the current state, else the melee spec's states.</summary>
    public static bool CanBeElbowShocked(SPlayerController pc) => PlayRules.Of(pc).PawnsAttackPawns &&
        (pc.StateNode is SPlayerController.ICanBeElbowShocked ? pc.eventCanBeElbowShocked() : CombatServices.Ports(pc).CanBeElbowShocked(pc));

    /// <summary>CanBeDropAttacked through the current state, else the walking family and the scope.</summary>
    public static bool CanBeDropAttacked(SPlayerController pc) => PlayRules.Of(pc).PawnsAttackPawns &&
        (pc.StateNode is SPlayerController.ICanBeDropAttacked ? pc.eventCanBeDropAttacked() : CombatServices.Ports(pc).CanBeDropAttacked(pc));

    /// <summary>CanActivateLaser through the current state, else the shocker spec's state table.</summary>
    public static bool CanActivateLaser(SPlayerController pc) =>
        pc.StateNode is SPlayerController.ICanActivateLaser ? pc.eventCanActivateLaser() : CombatDispatch.DefaultCanActivateLaser(pc.GetStateName());

    /// <summary>CanIUseBinocular through the current state, else the scope spec's state table.</summary>
    public static bool CanIUseBinocular(SPlayerController pc) =>
        pc.StateNode is SPlayerController.ICanIUseBinocular ? pc.eventCanIUseBinocular() : CombatDispatch.DefaultCanIUseBinocular(pc.GetStateName());

    /// <summary>CanInteract through the current state (false when no state defines it).</summary>
    public static bool CanInteract(SPlayerController pc) => pc.eventCanInteract();

    /// <summary>What the current state does with a function it has no ported version of.</summary>
    public enum StateFunction
    {
        /// <summary>No state on the chain defines or ignores it: the class version runs.</summary>
        Global,

        /// <summary>The state (or a super state) lists it in its ignores.</summary>
        Ignored,

        /// <summary>The state defines it with an empty body.</summary>
        Empty,

        /// <summary>The state defines it with a body no port provides yet (see <see cref="UnportedSha1"/>).</summary>
        Unported,
    }

    /// <summary>
    /// The current state's treatment of <paramref name="Function"/> from the class metadata, for states another area
    /// ports without that function: the state chain's ignores, an empty override, or a defined body (whose hash
    /// <paramref name="UnportedSha1"/> lets the caller run a shared port of that body).
    /// </summary>
    public static StateFunction Lookup(Actor Self, string Function, out string? UnportedSha1)
    {
        UnportedSha1 = null;
        for (ActorState? s = Self.StateNode; s is not null && !ReferenceEquals(s, ActorState.None); s = s.Super)
        {
            if (s is not UcActorState { Info: { } info })
            {
                continue;
            }

            if (info.IgnoresFunction(Function))
            {
                return StateFunction.Ignored;
            }

            if (info.FindFunction(Function) is { } f)
            {
                if (f.Sha1 == Equipment.K.EmptyBodySha1)
                {
                    return StateFunction.Empty;
                }

                UnportedSha1 = f.Sha1;
                return StateFunction.Unported;
            }
        }

        return StateFunction.Global;
    }
}
