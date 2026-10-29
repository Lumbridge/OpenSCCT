using OpenScct.Physics.Collision;
using OpenScct.Provenance;

namespace OpenScct.Game.Camera;

/// <summary>The camera natives' constants (spec/native/camera.md, Constants).</summary>
[Provenance(ProvenanceKind.Decoded, "spec/native/camera.md constants: solver 0x111f56a0, UpdateView 0x111f4e50, calc views 0x111e7600 / 0x111f1400, FirstPersonCalcView 0x111f5230")]
public static class CameraK
{
    /// <summary>Number of camera modes (ECamMode, m_camParam[35]).</summary>
    public const int ModeCount = 35;

    /// <summary>Twig: (-p x twigX, 0, p x twigZ) / 16384 with p the controller pitch.</summary>
    public const float TwigScale = 1f / 16384f;

    /// <summary>The camera box when the mode does not use the cylinder (useCylColl off).</summary>
    public const float SmallBox = 2f;

    /// <summary>The final clearance sweep box.</summary>
    public const float ClearanceBox = 12f;

    /// <summary>Extra vertical catch-up speed of modes 4, 5 and 28 while the pawn moves vertically.</summary>
    public const float VerticalBoostSpeed = 190f;

    /// <summary>Mode 34 (SmoothTransitionCentered): the start point is a pawn bone plus this height.</summary>
    public const float CenteredStartLift = 30f;

    /// <summary>Mode 34 forces smoothing at this value.</summary>
    public const float CenteredSmoothValue = 20f;

    /// <summary>Mode 33 (NoTargetCol): the pitch below which the head room is tested, rotation units.</summary>
    public const int NoTargetColPitchLimit = 2000;

    /// <summary>Mode 33: the head room probe height above the camera.</summary>
    public const float NoTargetColProbe = 20f;

    /// <summary>Mode 33: pitch correction growth and decay, rotation units per second per second.</summary>
    public const float PitchCorrectionRate = 3200f;

    /// <summary>Mode 33: pitch correction cap.</summary>
    public const float PitchCorrectionMax = 6400f;

    /// <summary>Sticky camera view offset along the used object's forward axis.</summary>
    public const float StickyCamOffset = 3f;

    /// <summary>Extra offset of the camera network view.</summary>
    public const float CamNetworkExtraOffset = 5f;

    /// <summary>Smooth-to-sticky camera: the target is this far along the controller's forward axis from the object.</summary>
    public const float SmoothStickyLead = 30f;

    /// <summary>Smooth-to-sticky camera: approach speed.</summary>
    public const float SmoothStickySpeed = 2000f;

    /// <summary>Smooth-to-sticky camera: arrival distance from the used object.</summary>
    public const float SmoothStickyArrive = 80f;

    /// <summary>Reloaded fly-in slow start: the timer a new fly-in starts at.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x1001a8ba: mov [0x1036ab90], 0x42c80000 (100.0)")]
    public const float SmoothStickySlowTimer = 100f;

    /// <summary>Reloaded fly-in slow start: a new fly-in is a TimeSeconds gap over this many m_dT since the last call.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x1001a8ad: m_dT x [0x10346630] (4.0)")]
    public const float SmoothStickySlowGap = 4f;

    /// <summary>Reloaded fly-in slow start: timer drain per call, per millisecond of frame time.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x1001a90c: frame ms x [0x103464b8] (0.34)")]
    public const float SmoothStickySlowDrain = 0.34f;

    /// <summary>Reloaded fly-in slow start: the step scale while the timer runs.</summary>
    [Provenance(ProvenanceKind.Decoded, "Reloaded 0x1001a93a: step x [0x103464e0] (0.66)")]
    public const float SmoothStickySlowScale = 0.66f;

    /// <summary>The spy's first-person view on a bone is raised this far in world Z (game 0x10a98390).</summary>
    public const float SpyBoneLift = 15f;

    /// <summary>ResettingRotation: done when pitch and yaw are each less than this from the target (game 0x10a96750).</summary>
    public const int ResetTolerance = 5;

    /// <summary>The ideal and previous distance a new camera starts from (SPlayerCam.PostBeginPlay).</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerCam.PostBeginPlay: m_prevDistance = m_prevIdealDistance = 200")]
    public const float StartDistance = 200f;

    /// <summary>ActivateSmooth without a value: the spy's default smoothing speed.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerCam.ActivateSmooth: 150 for SPAWNOFFENSE")]
    public const float SpySmoothDefault = 150f;

    /// <summary>ActivateSmooth without a value: the merc's default smoothing speed.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerCam.ActivateSmooth: 15000 otherwise")]
    public const float MercSmoothDefault = 15000f;

    /// <summary>
    /// The target sweep (useCollTarget): the solver passes 0xdf and the sweep helper clears bit 0 (pawns), so 0xde:
    /// the camera target is not stopped by other pawns.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10a95427 loads 0xdf into EAX; the sweep helper 0x10a961c0 passes EAX & ~1")]
    public const ETraceFlags TargetTraceFlags = (ETraceFlags)0xde;

    /// <summary>The collision sweep from the target to the camera: 0xde.</summary>
    public const ETraceFlags CameraTraceFlags = (ETraceFlags)0xde;

    /// <summary>Added to <see cref="CameraTraceFlags"/> when the mode's collCBInterSpeed is above 0.</summary>
    public const ETraceFlags CameraBlockingFlag = ETraceFlags.TRACE_Camera;

    /// <summary>A pitch above this (in [-32768, 32767]) counts as looking down for the pitch curve.</summary>
    public const int HalfTurn = 32768;
}
