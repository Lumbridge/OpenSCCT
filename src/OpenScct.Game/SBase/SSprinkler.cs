using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SSprinkler: a ceiling sprinkler. A Trigger (or damage, when bDamageable) starts the water: a sprinkler
/// emitter at the head and a puddle (SFlaque) that grows on the floor below; after Duration seconds, or on UnTrigger,
/// the water stops and the puddle shrinks away. Server-authoritative; the emitter reference and the puddle's growth
/// replicate. See spec/devices/sprinkler.md.
/// </summary>
public partial class SSprinkler
{
    /// <summary>A hidden sprinkler stays relevant but draws nothing (bHidden off, DT_None).</summary>
    [Uc("SBase.SSprinkler.PostBeginPlay", Sha1 = "db4c5c2a3d326e0e2979063954f92fc687cb2e70")]
    [PortOf("uc:SBase.SSprinkler:PostBeginPlay", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (bHidden)
        {
            bHidden = false;
            SetDrawType(EDrawType.DT_None);
        }
    }

    /// <summary>
    /// Starts the water: the emitter at the head, and the puddle on the first hit of a 500 uu trace down from
    /// Location.Z + WetVolumeZOffset - WetVolumeHeight (spawned 5 uu above the hit, or the existing one grows again);
    /// a positive Duration arms the stop timer.
    /// </summary>
    [Uc("SBase.SSprinkler.SpawnWater", Sha1 = "3d37f22f13f2027bca477a016f8bab1369d1fff2")]
    [PortOf("uc:SBase.SSprinkler:SpawnWater", Spec = "spec/devices/sprinkler.md#rules")]
    public void SpawnWater()
    {
        FVector v = Location;
        v.Z += WetVolumeZOffset - WetVolumeHeight;
        WaterEmitter = XLevel.Spawn<SSprinklerEmitter>(Location, Rotation, this, Instigator);
        if (Flaque is null)
        {
            if (MovementNatives.Trace(this, out FVector hitLocation, out _, v - new FVector(0f, 0f, 500f), v, true) is not null)
            {
                Flaque = XLevel.Spawn<SFlaque>(hitLocation + new FVector(0f, 0f, 5f), Rotation, this, Instigator);
                Flaque?.Grow();
            }
        }
        else
        {
            Flaque.Grow();
        }

        if (Duration > 0)
        {
            SetTimer(Duration, false);
        }
    }

    /// <summary>Stops the water: the emitter is killed and the puddle starts shrinking.</summary>
    [Uc("SBase.SSprinkler.StopWater", Sha1 = "3587418ce9eeaebdb1256e279ce061b8b023a238")]
    [PortOf("uc:SBase.SSprinkler:StopWater", Spec = "spec/devices/sprinkler.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Emitter.Kill (stop spawning, let the particles die, then destroy) is not ported; the emitter is destroyed at once")]
    public void StopWater()
    {
        if (WaterEmitter is not null)
        {
            WaterEmitter.Destroy();
            WaterEmitter = null;
        }

        Flaque?.Reduce();
    }

    /// <summary>Damage starts the water when bDamageable and it is not already running.</summary>
    [Uc("SBase.SSprinkler.TakeDamage", Sha1 = "626afd9462334fb6d1f79529b292a0c95c457a4b")]
    [PortOf("uc:SBase.SSprinkler:TakeDamage", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (!bDamageable)
        {
            return;
        }

        if (WaterEmitter is null)
        {
            SpawnWater();
        }
    }

    [Uc("SBase.SSprinkler.Trigger", Sha1 = "88b23dd28f6fd32e12fcc3687dd864818ff8eb6d")]
    [PortOf("uc:SBase.SSprinkler:Trigger", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (WaterEmitter is null)
        {
            SpawnWater();
        }
    }

    [Uc("SBase.SSprinkler.UnTrigger", Sha1 = "0cd89dc806af2fd333e5553b393a03c711571ad8")]
    [PortOf("uc:SBase.SSprinkler:UnTrigger", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => StopWater();

    [Uc("SBase.SSprinkler.Timer", Sha1 = "0cd89dc806af2fd333e5553b393a03c711571ad8")]
    [PortOf("uc:SBase.SSprinkler:Timer", Spec = "spec/devices/sprinkler.md#rules")]
    protected override void Timer() => StopWater();
}
