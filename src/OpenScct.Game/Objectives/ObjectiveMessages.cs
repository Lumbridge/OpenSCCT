using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// Server-side delivery of the SGameMessages switches the objective code sends (spec/objectives/messages.md
/// rules 1 to 3): to every player controller, to one team, or to one player. Each delivery is one reliable
/// <c>ReceiveLocalizedMessage</c> through <see cref="IObjectiveNet"/>; the text is built by the receiver.
/// </summary>
public static class ObjectiveMessages
{
    /// <summary><c>BroadcastLocalizedMessage(class'SGameMessages', Switch, None, None, OptionalObject)</c>: every player controller.</summary>
    [Provenance(ProvenanceKind.Decoded, "messages.md rule 1: Actor.BroadcastLocalizedMessage through GameInfo.BroadcastLocalized to every PlayerController")]
    public static void Broadcast(Actor sender, int Switch, Actor? OptionalObject = null)
    {
        IObjectiveNet net = ObjectiveNatives.Net(sender);
        for (Controller? c = sender.Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is PlayerController pc)
            {
                net.ReceiveLocalizedMessage(pc, Switch, OptionalObject);
            }
        }
    }

    /// <summary><c>SGameInfo.BroadcastTeamLocalizedMessage(Team, class'SGameMessages', Switch, None, None, OptionalObject)</c>.</summary>
    [Provenance(ProvenanceKind.Decoded, "messages.md rule 2: every PlayerController whose PRI.Team.TeamIndex is the team")]
    public static void BroadcastTeam(Actor sender, int TeamIndex, int Switch, Actor? OptionalObject = null)
    {
        IObjectiveNet net = ObjectiveNatives.Net(sender);
        for (Controller? c = sender.Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is PlayerController pc && ObjectiveNatives.TeamIndexOf(pc) == TeamIndex)
            {
                net.ReceiveLocalizedMessage(pc, Switch, OptionalObject);
            }
        }
    }

    /// <summary><c>Receiver.ReceiveLocalizedMessage(class'SGameMessages', Switch, None, None, OptionalObject)</c>.</summary>
    public static void Send(PlayerController Receiver, int Switch, Actor? OptionalObject = null) =>
        ObjectiveNatives.Net(Receiver).ReceiveLocalizedMessage(Receiver, Switch, OptionalObject);
}
