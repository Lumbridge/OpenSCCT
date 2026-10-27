using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the merc kit decoded from the natives behind the grenade throw, the torch and the laser sight
// (spec/equipment/grenade-throw.md, merc-flashlight.md, merc-laser.md).
public static partial class K
{
    // ---- grenade launch and bounce (grenade-throw.md rules 4, 10) -----------------------------------------------

    /// <summary>An aimed launch adds this to the force before it becomes the speed along the throw direction.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASProjGrenade::execLaunch game 0x10ab8b6a: ADD EDX,0xd2 on the Force operand")]
    public const int GrenadeLaunchBonus = 210;

    /// <summary>Below this speed after a bounce a grenade without a fuse stops (and explodes on the server).</summary>
    [Provenance(ProvenanceKind.Decoded, "grenade HitWall body game 0x10ab8cf0: 20.0 compared with the speed after the bounce")]
    public const float GrenadeRestSpeed = 20f;

    /// <summary>A bounce leaving faster than this upward is damped: Vz = (Vz + 400) / 2.</summary>
    [Provenance(ProvenanceKind.Decoded, "grenade HitWall body game 0x10ab8cf0: Velocity.Z <= 400 test, else (Vz + 400) * 0.5")]
    public const float GrenadeUpBounceCap = 400f;

    /// <summary>A grenade struck by a mover's face (|N . mover direction| above this) stops dead.</summary>
    [Provenance(ProvenanceKind.Decoded, "grenade HitWall body game 0x10ab8cf0: 0.9 against |HitNormal . Normal(Wall.Velocity)|")]
    public const float GrenadeMoverStopCos = 0.9f;

    /// <summary>The trace flags of the bounce's "is there geometry behind the hit" check (Movers, Level, LevelGeometry).</summary>
    [Provenance(ProvenanceKind.Decoded, "grenade HitWall body game 0x10ab8cf0: SingleLineCheck flags 0x86")]
    public const int GrenadeBounceTraceFlags = 0x86;

    /// <summary>A merc's aimed throw starts this far along his aim from the rifle's FX bone.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execUseGren game 0x10aa061c: 40.0 x vector(MyDesiredRotation)")]
    public const float MercThrowForward = 40f;

    /// <summary>A spy's aimed throw starts this far along the FX bone's direction.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadGrenLauncher::execUseGren game 0x10aa05d7: 10.0 x vector(FX bone rotation)")]
    public const float SpyThrowForward = 10f;

    // ---- torch placement (merc-flashlight.md rule 7) -------------------------------------------------------------

    /// <summary>
    /// The torch yaw leans this share of the way from the merc's desired yaw toward his body yaw:
    /// yaw = desired - int(-0.3 x (body - desired)).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadTorche::execNativeTickTorche game 0x10ab71ee: FMUL double -0.3 on the yaw difference")]
    public const double TorchBodyYawShare = -0.3;

    // ---- laser beam (merc-laser.md rule 9) -----------------------------------------------------------------------

    /// <summary>The laser's far point is 15000 uu from the merc's location raised by this when standing...</summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement game 0x10a98e00: 55.5 added to the end point's Z when not crouched")]
    public const float LaserEndRaiseStanding = 55.5f;

    /// <summary>...and by this when crouched.</summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement game 0x10a98e00: 48.29 added to the end point's Z when bIsCrouched")]
    public const float LaserEndRaiseCrouched = 48.29f;

    /// <summary>Inside a network camera the laser starts 20 uu ahead of the camera...</summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement game 0x10a98e00 and torch tick 0x10ab6eb0: camera X x 20")]
    public const float CamLaserForward = 20f;

    /// <summary>...11 uu to its side and 11 uu up (Y and Z axes of the camera rotation).</summary>
    [Provenance(ProvenanceKind.Decoded, "laser placement game 0x10a98e00 and torch tick 0x10ab6eb0: camera Y x -11, Z x 11")]
    public const float CamLaserSide = 11f;

    /// <summary>The laser beam hums while shown (SLaserBeam.LaserSound): volume 64, radius 24.</summary>
    [Provenance(ProvenanceKind.Decoded, "ASGadTorcheLaser::execNativeTickTorcheLaser game 0x10a99cc0: SoundVolume '@', SoundRadius 24.0")]
    public const float LaserSoundRadius = 24f;

    // ---- Reloaded recoil view step (merc-rifle.md rule 7a) ----------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: kick x 0.35")]
    public const float RecoilShare = 0.35f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: offsets clamped to +-2400")]
    public const float RecoilCap = 2400f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: a kick above 800 units (pitch unsigned, yaw absolute) starts nothing")]
    public const int RecoilKickLimit = 800;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: 0.033 s rise, then 0.033 s ease")]
    public const float RecoilRamp = 0.033f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: ease (1 - g)^2 x 0.9 + 0.1")]
    public const float RecoilEase = 0.9f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: the 0.1 share left applied at the end")]
    public const float RecoilRest = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: frames of 0.5 s or more stop it")]
    public const float RecoilMaxFrame = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: pitch kept above -12001 (-12000) and below 13001 (13000)")]
    public const int RecoilPitchMin = -12000;

    [Provenance(ProvenanceKind.Decoded, "Reloaded view step rva 0x117b0: pitch kept below 13001")]
    public const int RecoilPitchMax = 13000;
}
