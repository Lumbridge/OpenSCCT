using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SGrenadeExplosion: the explosion visual of a blast; it lights its surroundings for an instant.</summary>
public partial class SGrenadeExplosion
{
    /// <summary>Spawns the short flash light at the explosion.</summary>
    [Uc("SBase.SGrenadeExplosion.PostBeginPlay", Sha1 = "ec6019410ff51ff4a839dcdbfbeb59f30b019ae5")]
    [PortOf("uc:SBase.SGrenadeExplosion:PostBeginPlay", Spec = "spec/devices/gameplay-object.md#explosion-visual")]
    protected override void PostBeginPlay() => XLevel.Spawn<SFlashLight_FragGrenade>(Location, Rotation, null, Instigator);

    [Uc("SBase.SGrenadeExplosion.Tick", Sha1 = "86b79fc22e73f4da23fe7a77e2919d37f6b935e2")]
    [PortOf("uc:SBase.SGrenadeExplosion:Tick", Spec = "spec/devices/gameplay-object.md#explosion-visual")]
    protected override void Tick(float DeltaTime) => base.Tick(DeltaTime);

    [Uc("SBase.SGrenadeExplosion.Timer", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGrenadeExplosion:Timer", Spec = "spec/devices/gameplay-object.md#explosion-visual")]
    protected override void Timer()
    {
    }
}
