using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>
/// The calls the objective and interaction port sends to other machines (spec/objectives/messages.md#network). The
/// net layer implements it and installs it with <see cref="Level.SetService{T}"/>; without one,
/// <see cref="ObjectiveNetLocal"/> treats every player as local.
/// <list type="bullet">
/// <item>Multicasts: the SCCT "to all clients" functions (function flag 0x01000000). The caller has already run the
/// function on this machine; the implementation sends it to every client, where the net layer calls the same C#
/// method on the client's copy (it runs there with Role below Authority and does not send again).</item>
/// <item>Owner-client functions and localized messages: the implementation runs the body on this machine when the
/// receiving player is local, and sends it to the player's connection otherwise.</item>
/// </list>
/// </summary>
public interface IObjectiveNet
{
    /// <summary>Multicast <c>SObjective.AddRemoveObjectiveOnClientHud(Add)</c>.</summary>
    void AddRemoveObjectiveOnClientHud(SObjective Objective, bool Add);

    /// <summary>Multicast <c>SObjectiveTrigger.AddRemoveObjectiveForRadar(bAdd)</c>.</summary>
    void AddRemoveObjectiveForRadar(SObjectiveTrigger Trigger, bool bAdd);

    /// <summary>Multicast <c>STriggerInteraction.AddRemoveActorOnClientHud(Instigator, Add)</c>.</summary>
    void AddRemoveActorOnClientHud(STriggerInteraction Interaction, SPlayerController? Instigator, bool Add);

    /// <summary>Multicast <c>SFlag.SetFlagCarrier(taken)</c>.</summary>
    void SetFlagCarrier(SFlag Flag, bool taken);

    /// <summary>
    /// Multicast <c>SBombTargetInteraction.Update(Remove)</c> (spec/objectives/bomb-target.md rule 15). Without a
    /// network there is no other machine to reach.
    /// </summary>
    void UpdateBombInteraction(SBombTargetInteraction Interaction, bool Remove)
    {
    }

    /// <summary>Reliable owner-client <c>SInteractionManager.AddObjetOnClient(Obj, i)</c>.</summary>
    void AddObjetOnClient(SInteractionManager Manager, SInteractObject? Obj, int i);

    /// <summary>Reliable owner-client <c>SInteractionManager.RemoveInteractionObjOnClient(Object)</c>.</summary>
    void RemoveInteractionObjOnClient(SInteractionManager Manager, SInteractObject? Object);

    /// <summary>
    /// Reliable owner-client <c>PlayerController.ReceiveLocalizedMessage(class'SGameMessages', Switch, None, None,
    /// OptionalObject)</c>. The receiving machine builds the text from the switch and the object.
    /// </summary>
    void ReceiveLocalizedMessage(PlayerController Receiver, int Switch, Actor? OptionalObject);
}

/// <summary>
/// <see cref="IObjectiveNet"/> until the net layer installs its own: multicasts have no other machine to reach,
/// owner-client functions run here (the owner is taken to be local), and a localized message goes out as the game
/// rules send theirs, a <c>ReceiveLocalizedMessage</c> <see cref="GameCall"/> through the level's
/// <see cref="IGameHost"/>.
/// </summary>
[Provenance(ProvenanceKind.Estimated, "Local delivery of SCCT client functions; the real routing belongs to OpenScct.Net")]
public sealed class ObjectiveNetLocal : IObjectiveNet
{
    public static readonly ObjectiveNetLocal Instance = new();

    public static readonly ClassRef SGameMessagesClass = new("SBase.SGameMessages");

    public static readonly Name NAME_ReceiveLocalizedMessage = new("ReceiveLocalizedMessage");

    private ObjectiveNetLocal()
    {
    }

    public void AddRemoveObjectiveOnClientHud(SObjective Objective, bool Add)
    {
    }

    public void AddRemoveObjectiveForRadar(SObjectiveTrigger Trigger, bool bAdd)
    {
    }

    public void AddRemoveActorOnClientHud(STriggerInteraction Interaction, SPlayerController? Instigator, bool Add)
    {
    }

    public void SetFlagCarrier(SFlag Flag, bool taken)
    {
    }

    public void AddObjetOnClient(SInteractionManager Manager, SInteractObject? Obj, int i) => Manager.AddObjetOnClient(Obj, i);

    public void RemoveInteractionObjOnClient(SInteractionManager Manager, SInteractObject? Object) =>
        Manager.RemoveInteractionObjOnClient(Object);

    public void ReceiveLocalizedMessage(PlayerController Receiver, int Switch, Actor? OptionalObject) =>
        (Receiver.XLevel.GetService<IGameHost>() ?? NullGameHost.Instance).ClientCall(Receiver,
            new GameCall(NAME_ReceiveLocalizedMessage) { Class = SGameMessagesClass, Int = Switch, Object = OptionalObject });
}
