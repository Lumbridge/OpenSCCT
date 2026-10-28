using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SSprinklerEmitter: the water of a running sprinkler (spec/devices/sprinkler.md). Its second sub-emitter is the
/// splash on the floor: on each machine the emitter traces down for the floor and starts that sub-emitter's particles
/// at the floor's height.
/// </summary>
public partial class SSprinklerEmitter
{
    [Provenance(ProvenanceKind.Decoded, "SSprinklerEmitter.PreBeginPlay: Trace(Location - vect(0, 0, 500), Location - vect(0, 0, 50), true)")]
    public const float FloorTraceStart = 50f;

    [Provenance(ProvenanceKind.Decoded, "SSprinklerEmitter.PreBeginPlay: Trace(Location - vect(0, 0, 500), Location - vect(0, 0, 50), true)")]
    public const float FloorTraceEnd = 500f;

    /// <summary>
    /// The splash sub-emitter's (Emitters[1]) start height from the emitter, StartLocationRange.Z.Min = Max, when the
    /// floor trace hit; null keeps the class value (0).
    /// </summary>
    public float? SplashHeight { get; private set; }

    /// <summary>The floor below (actors included) sets the splash sub-emitter's start height.</summary>
    [Uc("SBase.SSprinklerEmitter.PreBeginPlay", Sha1 = "74705133d60187131faf880248da1f4fb0316342")]
    [PortOf("uc:SBase.SSprinklerEmitter:PreBeginPlay", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void PreBeginPlay()
    {
        if (MovementNatives.Trace(this, out FVector hit, out _, Location - new FVector(0f, 0f, FloorTraceEnd),
                Location - new FVector(0f, 0f, FloorTraceStart), true) is not null)
        {
            SplashHeight = hit.Z - Location.Z;
        }

        base.PreBeginPlay();
    }
}
