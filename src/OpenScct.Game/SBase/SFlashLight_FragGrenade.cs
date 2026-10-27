using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SFlashLight_FragGrenade: the explosion flash (brightness 255, radius 22), gone after 0.5 s.</summary>
public partial class SFlashLight_FragGrenade
{
    [Uc("SBase.SFlashLight_FragGrenade.PostBeginPlay", Sha1 = "5aa406372647d87ad51b43aa5bac1becff9033d5")]
    [PortOf("uc:SBase.SFlashLight_FragGrenade:PostBeginPlay", Spec = "spec/devices/gameplay-object.md#explosion-visual")]
    protected override void PostBeginPlay()
    {
        fDateDepart = Level.TimeSeconds;
        LifeSpan = fTempsDisparition;
    }
}
