using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SLaserBeam: a laser beam mesh (tripwires, the spy pistol's and merc's lasers, the mines' beams).</summary>
public partial class SLaserBeam
{
    /// <summary>A beam that starts in a rain volume is half as warm (spec/equipment/rain.md rule 5).</summary>
    [Uc("SBase.SLaserBeam.PreBeginPlay", Sha1 = "7cf035cdf3cf022e65ff4fbb199f16863f9d4da9")]
    [PortOf("uc:SBase.SLaserBeam:PreBeginPlay", Spec = "spec/equipment/rain.md#rules")]
    protected override void PreBeginPlay()
    {
        if (GetRainVolume(Location) is not null)
        {
            HeatIntensity *= OpenScct.Game.Devices.SensorK.RainHeatFactor;
        }
    }
}
