using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.AnimNotify: an object an animation sequence fires at a set time.
public abstract partial class UAnimNotify
{
    /// <summary>
    /// Fired by the animation update when the sequence's frame crosses this notify's time, with the animated actor
    /// as owner. Empty on the base class.
    /// </summary>
    [UcNative("UAnimNotify::Notify")]
    [PortOf("nat:Engine.Actor.PlayAnim", Spec = "spec/devices/animated-mesh.md#rules")]
    public virtual void Notify(Actor Owner)
    {
    }
}
