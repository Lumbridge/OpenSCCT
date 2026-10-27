using System;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the merc's Uzi and shotgun (spec/equipment/merc-guns.md): their first-person clips, the shotgun's
// native pellet trace (spec/native/natives2-damage.md rule 7) and its pellet decals.
public static partial class K
{
    // ---- first-person clips (SWeaponFirst.ukx, frames at 15 fps) -------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstShooStFd2U 3 frames at 15 fps")]
    public static readonly ClipData FirstShooStFd2U = new(3, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstShooStFd2Ub 5 frames at 15 fps")]
    public static readonly ClipData FirstShooStFd2Ub = new(5, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstReloStFd2U 31 frames at 15 fps")]
    public static readonly ClipData FirstReloStFd2U = new(31, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstShooStFd2S 16 frames at 15 fps")]
    public static readonly ClipData FirstShooStFd2S = new(16, 15f);

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstReloStFd2S 46 frames at 15 fps")]
    public static readonly ClipData FirstReloStFd2S = new(46, 15f);

    /// <summary>
    /// The shell notifies of FirstReloStFd2S (EventReloadFirst1 to 5), in seconds into the clip: after shell n the
    /// reload of n shells jumps to frame 39 (EndOfReloadFrame).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstReloStFd2S AnimNotify_Script times 0.25695655, 0.3619565, 0.47000003, 0.6258696, 0.74326086 of 46 frames")]
    public static ReadOnlySpan<float> ShotgunShellNotifySeconds => [0.7880001f, 1.11f, 1.4413334f, 1.9193333f, 2.2793334f];

    /// <summary>The pump sound of FirstShooStFd2S (AnimNotify_Sound Reload_Def_Pump), in seconds into the clip.</summary>
    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstShooStFd2S AnimNotify_Sound time 0.5 of 16 frames, Volume 100, Radius 100")]
    public const float ShotgunPumpNotifySeconds = 0.53333336f;

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstShooStFd2S AnimNotify_Sound Volume 100")]
    public const float ShotgunPumpVolume = 100f;

    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstShooStFd2S AnimNotify_Sound Radius 100")]
    public const float ShotgunPumpRadius = 100f;

    // ---- shotgun pellets (native TraceFire, natives2-damage.md rule 7) -------------------------------------------

    /// <summary>The spread of the pattern's centre: 0.067 x ShootingRange x Accuracy on each axis.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASWeaponDefense_Shotgun::execTraceFire editor 0x111d69c0: 0.067")]
    public const float ShotgunSpread = 0.067f;

    /// <summary>The four ring pellets sit 0.194 x ShootingRange up, down, right and left of the centre.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASWeaponDefense_Shotgun::execTraceFire editor 0x111d69c0: 0.194 and 0.388 steps")]
    public const float ShotgunRing = 0.194f;

    /// <summary>The four diagonal pellets sit 0.097 x ShootingRange on each axis.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASWeaponDefense_Shotgun::execTraceFire editor 0x111d69c0: 0.097 step")]
    public const float ShotgunDiagonal = 0.097f;

    [Provenance(ProvenanceKind.Decoded, "ASWeaponDefense_Shotgun::execTraceFire editor 0x111d69c0: ring pellet factor 0.3")]
    public const float ShotgunRingFactor = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "ASWeaponDefense_Shotgun::execTraceFire editor 0x111d69c0: diagonal pellet factor 0.5")]
    public const float ShotgunDiagonalFactor = 0.5f;

    /// <summary>The pellets of one shot: the centre, four ring and four diagonal pellets.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASWeaponDefense_Shotgun::execTraceFire editor 0x111d69c0: nine traces, nine actor slots")]
    public const int ShotgunPellets = 9;

    /// <summary>The impact dummy's far range is at most this much past the near one.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASWeaponDefense_Shotgun::execTraceFire editor 0x111d8d0a: min(farthest, nearest + 200)")]
    public const int ShotgunImpactWindow = 200;

    // ---- shotgun pellet decals (SWeaponDefense_Shotgun / SWeaponDefenseDummy_Shotgun.SpawnImpacts) ----------------

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: for (i = 0; i < 8; i++)")]
    public const int ShotgunDecals = 8;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: orig = FX origin - X * 50")]
    public const float ShotgunDecalPullBack = 50f;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: the projectors start 20 short of the near range; the two side traces widen the range by 20")]
    public const int ShotgunDecalMargin = 20;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: + Z * 7 - Y * 7")]
    public const float ShotgunDecalOffset = 7f;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: (FRand() - 0.5) * MinProjDistance * 0.194, and the side traces at +-0.194")]
    public const float ShotgunDecalSpread = 0.194f;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: SetDrawScale(MinProjDistance * 0.00176)")]
    public const float ShotgunDecalScale = 0.00176f;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: MaxTraceDistance = 40 + MaxProjDistance - MinProjDistance")]
    public const int ShotgunDecalTraceExtra = 40;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: AbandonProjector(10.0)")]
    public const float ShotgunDecalLifetime = 10f;

    [Provenance(ProvenanceKind.Decoded, "SShotGunProjector.PostBeginPlay: FRand() < 0.3")]
    public const float ShotgunDecalSoundChance = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "SShotGunProjector.PostBeginPlay: PlaySound(SW_Impact_Shot, , 0.8, , 100.0)")]
    public const float ShotgunDecalSoundVolume = 0.8f;

    [Provenance(ProvenanceKind.Decoded, "SShotGunProjector.PostBeginPlay: PlaySound(SW_Impact_Shot, , 0.8, , 100.0)")]
    public const float ShotgunDecalSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SpawnImpacts: bonerot.Roll = Rand(65536)")]
    public const int FullRoll = 65536;
}
