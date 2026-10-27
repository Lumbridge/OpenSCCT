using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine.Emitter: a map particle system. A Trigger fires every sub-emitter (ParticleEmitter.Trigger) on every
/// machine (spec/devices/events.md, Network; spec/native/emitters.md). The particles are drawn by the views, so the
/// simulation raises <see cref="NAME_ReplicateTrigger"/> as a notify of this actor and the views fire the drawn copy.
/// </summary>
public partial class Emitter
{
    /// <summary>The notify a triggered emitter raises (the name of the original's broadcast function).</summary>
    public static readonly Name NAME_ReplicateTrigger = new("ReplicateTrigger");

    /// <summary>
    /// Emitter.Trigger: <c>ReplicateTrigger()</c>. Trigger is not simulated, so a client's copy (Role below
    /// AutonomousProxy) absorbs the call (events.md rule 7); the client fires its particles from the relayed notify.
    /// </summary>
    [Uc("Engine.Emitter.Trigger")]
    [PortOf("uc:Engine.Emitter:Trigger", Spec = "spec/devices/events.md#network")]
    protected internal override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (Role < ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        ReplicateTrigger();
    }

    /// <summary>
    /// Emitter.ReplicateTrigger (simulated, sent from the authority to the clients): every sub-emitter's Trigger.
    /// The sub-emitters live in the views, so this raises the notify they act on; the device-event relay carries it
    /// to the clients.
    /// </summary>
    [Uc("Engine.Emitter.ReplicateTrigger")]
    [PortOf("uc:Engine.Emitter:ReplicateTrigger", Spec = "spec/devices/events.md#network")]
    [Provenance(ProvenanceKind.Estimated,
        "The original broadcasts ReplicateTrigger to clients when bNetworkTrigger makes the emitter a relevant proxy (PreBeginPlay); the rewrite keeps map emitters unreplicated and relays the notify to every client instead, so an emitter without bNetworkTrigger (seen only on the server's own screen in the original) also fires on remote clients")]
    public void ReplicateTrigger() => XLevel.World.Events.Notify(this, NAME_ReplicateTrigger);

    /// <summary>True once <see cref="Kill"/> ended this emitter: the views let its live particles run out.</summary>
    public bool Killed { get; private set; }

    /// <summary>
    /// Emitter.Kill (native 1155): no more particles spawn and the actor goes once the live ones are dead
    /// (spec/native/emitters.md rule 4). The particles live in the views, so the simulation marks the actor and
    /// destroys it now; a view drawing it stops spawning and keeps the particles until they die.
    /// </summary>
    [UcNative("AEmitter::execKill")]
    [PortOf("nat:Engine.Emitter.Kill", Spec = "spec/native/emitters.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The actor is destroyed at once instead of when its last particle dies; only the drawn particles outlive it")]
    public void Kill()
    {
        if (bDeleteMe)
        {
            return;
        }

        Killed = true;
        Destroy();
    }
}
