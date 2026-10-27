using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SVolumetricSensor: a motion sensor watching one or more volumes (spec/devices/volumetric-sensor.md). It looks
/// into one volume at a time, all of them every 0.5 s; a pawn of DetectedClass moving at MinDetectedVelocity or more
/// fires the Event with that pawn as instigator, at once (DetectionDelay 0) or when it is still there and moving
/// DetectionDelay seconds later. Its LED (skin 1) shows green while watching, red on a detection, black when off or
/// broken; chaff or a sticky shot breaks it for StickyNeutralizationTime.
/// </summary>
public partial class SVolumetricSensor
{
    public static new readonly StateTable ClassStates =
        new(SBasicGamePlayObject.ClassStates, s_Active.Instance, s_Inactive.Instance, s_LockedByChaff.Instance, s_SomethingDetected.Instance);

    public override StateTable GetStateTable() => ClassStates;

    internal static readonly Name NAME_s_Active = new("s_Active");
    internal static readonly Name NAME_s_Inactive = new("s_Inactive");
    internal static readonly Name NAME_s_SomethingDetected = new("s_SomethingDetected");

    [Uc("SBase.SVolumetricSensor.PostBeginPlay", Sha1 = "a85c4a6a738aab94f58daa07d48bb986095110be")]
    [PortOf("uc:SBase.SVolumetricSensor:PostBeginPlay", Spec = "spec/devices/volumetric-sensor.md#rules")]
    protected override void PostBeginPlay()
    {
        CurrentIndex = 0;
        base.PostBeginPlay();
    }

    [Uc("SBase.SVolumetricSensor.Destroyed", Sha1 = "595358df53f64e7aa614499cd737b0305698edc7")]
    [PortOf("uc:SBase.SVolumetricSensor:Destroyed", Spec = "spec/devices/volumetric-sensor.md#rules")]
    protected override void Destroyed()
    {
        if (MalfunctionEmitter is not null)
        {
            SLaserTrigger.KillEmitter(MalfunctionEmitter);
        }

        base.Destroyed();
    }

    /// <summary>
    /// The LED follows bLightState: green (2), red (3), black (0 and 1); broken (0) also shows the malfunction sparks.
    /// </summary>
    [Uc("SBase.SVolumetricSensor.LightStateChange", Sha1 = "8aadf848966a7e3e660ce5791774c36d5f6f9354")]
    [PortOf("uc:SBase.SVolumetricSensor:LightStateChange", Spec = "spec/devices/volumetric-sensor.md#light")]
    public void LightStateChange()
    {
        bLocalLightState = bLightState;
        ApplyLight();
    }

    /// <summary>The LED skin and the sparks for bLocalLightState (the body LightStateChange and the chaffed Tick share).</summary>
    internal void ApplyLight()
    {
        while (Skins.Count <= SensorK.SensorLedSkin)
        {
            Skins.Add(AssetRef.None);
        }

        if (bLightState == SensorK.LightGreen)
        {
            Skins[SensorK.SensorLedSkin] = LightGreen;
        }
        else if (bLightState == SensorK.LightRed)
        {
            Skins[SensorK.SensorLedSkin] = LightRed;
        }
        else if (bLightState < SensorK.LightGreen)
        {
            Skins[SensorK.SensorLedSkin] = LightOff;
        }

        if (bLocalLightState == SensorK.LightBroken)
        {
            MalfunctionEmitter ??= DeviceClasses.Spawn(this, SensorK.MalfunctionEmitterClass, this, Name.None, Location, Rotation) as SMalfunctionEmitter;
        }
        else if (MalfunctionEmitter is not null)
        {
            SLaserTrigger.KillEmitter(MalfunctionEmitter);
            MalfunctionEmitter = null;
        }
    }

    /// <summary>
    /// The native side calls LightStateChange when bLightState changes (a client receiving it, or the server setting
    /// it); here every machine compares after its tick.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "The native caller of the LightStateChange event is not decoded")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (!bDeleteMe && bLightState != bLocalLightState)
        {
            LightStateChange();
        }
    }

    /// <summary>Locked for StickyNeutralizationTime, whatever the chaff's length.</summary>
    [Uc("SBase.SVolumetricSensor.SetLockedByChaff", Sha1 = "0a81162abb30c5c8d48bda129c0e2aa6d6b3be5a")]
    [PortOf("uc:SBase.SVolumetricSensor:SetLockedByChaff", Spec = "spec/devices/volumetric-sensor.md#rules")]
    public override void SetLockedByChaff(float Length) => base.SetLockedByChaff(StickyNeutralizationTime);

    /// <summary>
    /// s_Active.CheckPresence: the next volume (wrapping round); each pawn touching it of DetectedClass and moving at
    /// MinDetectedVelocity or more becomes DetectedActor and either fires the Event at once (red for 3 s) or starts
    /// the DetectionDelay confirmation.
    /// </summary>
    internal void CheckPresence()
    {
        CurrentIndex++;
        if (CurrentIndex >= Volumes.Count)
        {
            CurrentIndex = 0;
        }

        if (CurrentIndex >= Volumes.Count || Volumes[CurrentIndex] is not { } volume)
        {
            return;
        }

        foreach (Pawn p in TouchingPawns(volume))
        {
            if (!Detects(p))
            {
                continue;
            }

            DetectedActor = p;
            if (DetectionDelay == 0f)
            {
                TriggerEvent(Event, this, p);
                bLightState = SensorK.LightRed;
                SetTimer(SensorK.SensorRedSeconds, false);
                continue;
            }

            GotoState(NAME_s_SomethingDetected);
        }
    }

    /// <summary>A pawn of DetectedClass moving at MinDetectedVelocity or more.</summary>
    internal bool Detects(Pawn p) =>
        ClassIsChildOf(p.GetUcClass(), DetectedClass) && p.Velocity.Size() >= MinDetectedVelocity;

    /// <summary>Volume.TouchingActors(class'Pawn'): a copy, so state changes during the walk do not disturb it.</summary>
    internal static Pawn[] TouchingPawns(Volume volume)
    {
        var list = new System.Collections.Generic.List<Pawn>();
        foreach (Actor a in volume.Touching)
        {
            if (a is Pawn p && !p.bDeleteMe)
            {
                list.Add(p);
            }
        }

        return list.ToArray();
    }

    /// <summary>s_Active's latent step: 0.5 s shared by the volumes.</summary>
    internal float StepSeconds => SensorK.SensorSweepSeconds / Volumes.Count;

    internal void SleepStep() => Sleep(StepSeconds);

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        DetectedActor = Live(DetectedActor);
        MalfunctionEmitter = Live(MalfunctionEmitter);
    }
}
