using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.Devices;

/// <summary>
/// Constants of the laser tripwire (SLaserTrigger) and the volumetric sensor (SVolumetricSensor), from
/// spec/devices/laser-trigger.md and spec/devices/volumetric-sensor.md.
/// </summary>
public static class SensorK
{
    /// <summary>The class of the laser beam a tripwire spawns on drawing machines.</summary>
    public static readonly ClassRef LaserBeamClass = new("SBase.SLaserBeam");

    /// <summary>The sparks drawn over a broken (chaffed) device.</summary>
    public static readonly ClassRef MalfunctionEmitterClass = new("SBase.SMalfunctionEmitter");

    /// <summary>The bang of a device locked by chaff or a sticky shot.</summary>
    public static readonly AssetRef ChaffExplosionSound = new("Armes_impacts.Impacts.RA_camSurveil_EXPLO");

    /// <summary>The PlaySound radius of that bang.</summary>
    public const float ChaffExplosionRadius = 24f;

    /// <summary>The tripwire's beam in rain: half its heat.</summary>
    public const float RainHeatFactor = 0.5f;

    /// <summary>CheckPresence starts each pass at a 1 s check period.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASLaserTrigger::execCheckPresence 0x10ab69f3: fCheckFrequency = 1.0")]
    public const float LaserIdlePeriod = 1f;

    /// <summary>A pawn touching the beam sets the check period to 0.016 s.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10ab6aca: fCheckFrequency = 0x3c83126f")]
    public const float LaserBlockedPeriod = 0.016f;

    /// <summary>A pawn farther than this (beyond its radius from the beam, flat distance) does not block it.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10ab6aae: compare with 0x10c109f8 (0.0)")]
    public const float LaserTouchDistance = 0f;

    /// <summary>The period from the distance of a pawn not on the beam: distance / 650...</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10ab6c4d: x 0x10c11310 (1/650)")]
    public const float LaserPeriodPerUnit = 1f / 650f;

    /// <summary>...at least 0.01 s, plus 0.01 s...</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10ab6c57..0x10ab6c89: 0x10c109e4 (0.01)")]
    public const float LaserPeriodMin = 0.01f;

    /// <summary>...and at most 1 s.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10ab6c70: 0x10c109fc (1.0)")]
    public const float LaserPeriodMax = 1f;

    /// <summary>
    /// Speed across the beam (pawn speed minus its speed along the beam) up to which the blocking point is found by a
    /// line check against the pawn's cylinder...
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "0x10ab6b5a: 0x10c11240 (250.0)")]
    public const float LaserSlowCross = 250f;

    /// <summary>...and above which as well; between the two the blocking point is the beam point nearest the pawn.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10ab6b67: 0x10c11314 (460.0)")]
    public const float LaserFastCross = 460f;

    /// <summary>The tripwire is relevant to a viewer while (distance squared - LaserLength squared) is at most this.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASLaserTrigger::IsNetRelevantFor 0x10ab6d80: 0x10c10ba0 (6250000.0 = 2500 squared)")]
    public const float LaserRelevantSquared = 6250000f;

    /// <summary>The sensor's alarm loop while something is being confirmed.</summary>
    public static readonly AssetRef SensorAlarmSound = new("Interface.Objectifs.Alarme_Volume");

    public const byte SensorAlarmVolume = 200;

    public const float SensorAlarmRadius = 24f;

    /// <summary>A detection with no delay shows red for 3 s.</summary>
    public const float SensorRedSeconds = 3f;

    /// <summary>The sensor checks all its volumes once every 0.5 s, one volume per step.</summary>
    public const float SensorSweepSeconds = 0.5f;

    /// <summary>bLightState values: 0 broken (chaffed), 1 off, 2 green (watching), 3 red (detected).</summary>
    public const byte LightBroken = 0;

    public const byte LightOff = 1;

    public const byte LightGreen = 2;

    public const byte LightRed = 3;

    /// <summary>The LED skin slot of the sensor's mesh.</summary>
    public const int SensorLedSkin = 1;
}
