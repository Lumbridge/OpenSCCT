using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.Devices;

/// <summary>Constants of the SBase map devices (spec/devices), each with where it came from.</summary>
public static class DeviceK
{
    // ---- gameplay objects ---------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBase.SBasicGamePlayObject.Timer description retry")]
    public const float DescriptionRetry = 3f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SBasicGamePlayObject.AddOnHud priority threshold")]
    public const int HudPriority = 5;

    [Provenance(ProvenanceKind.Decoded, "SBase.SBasicGamePlayObject.TakeDamage and SGamePlayObject.TakeDamage sound radius")]
    public const float DamageSoundRadius = 200f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SBasicGamePlayObject.Malfunction team index (mercs)")]
    public const int MercTeam = 1;

    [Provenance(ProvenanceKind.Decoded, "SBase.SBasicGamePlayObject.Malfunction message switch")]
    public const int MalfunctionMessage = 21;

    [Provenance(ProvenanceKind.Decoded, "SBase.SBasicGamePlayObject.Malfunction message class")]
    public static readonly ClassRef GameMessages = new("SBase.SGameMessages");

    [Provenance(ProvenanceKind.Decoded, "SBase.SBasicGamePlayObject.TakeDamage sticky spark class")]
    public static readonly ClassRef SparkShockerClass = new("SBase.SSparkShocker");

    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 1.3.3: ProcessDamage raises NotifyNoise(0.5) at destruction when the object has a DestroySound")]
    public const float DestroyNoise = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.Trigger destroy-on-event momentum (0, 0, 200)")]
    public const float DestroyOnEventMomentumZ = 200f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.TakeDamage momentum length")]
    public const float MomentumLength = 300f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.TakeDamage momentum jitter on X and Y (50 * FRand - 25)")]
    public const float MomentumJitter = 50f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.TakeDamage momentum Z floor")]
    public const int MomentumMinZ = 300;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.TakeDamage push impulse Momentum x 5 / Mass")]
    public const float PushImpulse = 5f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.Touch pawn mass threshold")]
    public const float TouchPushMass = 40f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.Touch upward kick")]
    public const float TouchPushZ = 50f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.s_Flying.BeginState spin floor")]
    public const int FlyingSpinFloor = 10000;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.s_Flying.Bump: pawns ignored below this speed")]
    public const float BumpMinSpeed = 20f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.s_Flying.Bump damage = |V| x Mass / 500")]
    public const float BumpDamageDivisor = 500f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.s_Flying.Bump: damage applied above this")]
    public const int BumpDamageMin = 8;

    [Provenance(ProvenanceKind.Decoded, "SBase.SGamePlayObject.s_Flying.Bump non-pawn jitter on X and Y")]
    public const float BumpJitter = 50f;

    // ---- extinguisher spray -------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBase.SExtincteur.s_Waiting.TakeDamage spray time")]
    public const float ExtincteurSprayTime = 10f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SExtincteur.s_Waiting.TakeDamage noise loudness")]
    public const float ExtincteurNoise = 1f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SExtincteurEmitter particle lifetime")]
    public const float ExtincteurPuffLife = 1.6f;

    // ---- magic event --------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBase.SMagicEvent.BeginPlay update timer")]
    public const float MagicEventTimer = 0.05f;

    // ---- infirmary ----------------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBase.SInfirmary.Use heal sound radius")]
    public const float HealSoundRadius = 100f;

    [Provenance(ProvenanceKind.Decoded, "SBase.SPlayerController.ReloadAmmoStock sound radius")]
    public const float AmmoSoundRadius = 100f;

    // ---- camera network -----------------------------------------------------------------------------------

    [Provenance(ProvenanceKind.Decoded, "SBase.SOrientableObject.PostNetBeginPlay activation sound radius")]
    public const float CameraActivationRadius = 100f;
}
