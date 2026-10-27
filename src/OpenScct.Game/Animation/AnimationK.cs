using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>Constants of the pawn animation layer (spec/native/anim.md, spec/native/ANSWERS-anim.md).</summary>
public static class AnimationK
{
    [Provenance(ProvenanceKind.Decoded, "ClipRate 0x11105660: 30 frames per second when the channel's clip is missing or has rate 0")]
    public const float MissingClipRate = 30f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: dead or lying channels fade at 10 x dt")]
    public const float DeadFadePerSecond = 10f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the walk channels start as AWaitSt0 loops tweened over 0.1 s")]
    public const float InitTween = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the walk blender creates channels up to 24")]
    public const int LastWalkInitChannel = 24;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the no-run overlay steps at 10 per tick (step 10) or 10 x dt")]
    public const float NoRunStep = 10f;

    [Provenance(ProvenanceKind.Decoded, "ZeroMovementAlpha step 10000: at once")]
    public const float AtOnce = 10000f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: feet ledge channels 5 to 13 step 1e9")]
    public const float AtOnceFeetLedge = 1e9f;

    [Provenance(ProvenanceKind.Decoded, "climb step 0x11200140: alpha steps of 100000")]
    public const float AtOnceClimb = 100000f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the roll channel drops at once with step 100000")]
    public const float AtOnceRoll = 100000f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the feet ledge window of 2 s")]
    public const float FeetLedgeWindow = 2f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: fTimeOfFeetLedge = TimeSeconds - 3 once used")]
    public const float FeetLedgeReset = 3f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: feet ledge channel 5 frame 0.5 for the right foot")]
    public const float FeetLedgeRightFrame = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: a roll in the air fades channel 15 past frame 0.65")]
    public const float RollFadeFrame = 0.65f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: standing still below a squared horizontal speed of 2")]
    public const float StillSpeedSquared = 2f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: on a vertical plane still below |V.z| 1")]
    public const float StillVerticalSpeed = 1f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: turn in place steps at 6.6666665 dt")]
    public const float TurnStepPerSecond = 6.6666665f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: turn in place alpha 0.75 crouched")]
    public const float TurnAlphaCrouched = 0.75f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: turn in place alpha 0.5 standing")]
    public const float TurnAlphaStanding = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: turn rates above 5000 yaw units per second turn in place")]
    public const float TurnThreshold = 5000f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: turn clip rate scale |rate| x 4.1666666e-5")]
    public const float TurnRateScale = 4.1666666e-05f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: a simulated proxy smooths the turn rate over 0.3 s")]
    public const float TurnSmoothProxy = 0.3f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: 3.3333333 = 1 / 0.3")]
    public const float TurnSmoothProxyInv = 3.3333333f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: an authority pawn smooths the turn rate over 0.1 s")]
    public const float TurnSmoothAuthority = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: 10 = 1 / 0.1")]
    public const float TurnSmoothAuthorityInv = 10f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the strafe bias applies above a squared side weight of 0.1")]
    public const float StrafeBiasThreshold = 0.1f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: a side speed above 1 uu/s gives a strafe weight")]
    public const float StrafeMinSpeed = 1f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0, constant 0x11626c4c: slow walk below a rate of 1")]
    [Provenance(ProvenanceKind.Measured, "golden movement.spy-run-forward: rates 0.456 and 0.911 play at scale 1, 1.367 does not")]
    public const float SlowWalkRate = 1f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the charge set (fBlendCoeffWalkAnim 385) has no slow walk")]
    public const float ChargeCoeff = 385f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: no run fade with a side weight of 0.7 or more")]
    public const float RunCutSideWeight = 0.7f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: on a horizontal pipe a coefficient below 120 reverses the clip")]
    public const float PipeHReverseCoeff = 120f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: Vandamme aim yaw scale")]
    public const float AimYawScale = 8.333333e-05f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: Vandamme and pipe aim pitch scale")]
    public const float AimPitchScale = 0.00010970927f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: Vandamme pitch offset 0x951")]
    public const int AimPitchOffset = 0x951;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: PipH2 aim yaw scale around a half turn")]
    public const float AimYawScalePipH2 = 6.1349696e-05f;

    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: PipH2 aim pitch scale")]
    public const float AimPitchScalePipH2 = 0.0001f;

    [Provenance(ProvenanceKind.Decoded, "aim blend 0x111ffa90: steps of 1.4285715 dt")]
    public const float AimStep = 1.4285715f;

    [Provenance(ProvenanceKind.Decoded, "aim blend 0x111ffa90: steps of 5 dt with bWeaponTransit")]
    public const float AimStepTransit = 5f;

    [Provenance(ProvenanceKind.Decoded, "aim blend 0x111ffa90: saturation at a' + b' >= 1.9999")]
    public const float AimSaturation = 1.9999f;

    [Provenance(ProvenanceKind.Decoded, "aim blend 0x111ffa90: a set change drops the channel with step 10")]
    public const float AimSetChangeStep = 10f;

    [Provenance(ProvenanceKind.Decoded, "Vandamme weapon blend 0x112008a0: 10 dt, 5 dt and 3.3333333 dt")]
    public const float VandammeDrawIn = 10f;

    [Provenance(ProvenanceKind.Decoded, "Vandamme weapon blend 0x112008a0")]
    public const float VandammeHolster = 5f;

    [Provenance(ProvenanceKind.Decoded, "Vandamme weapon blend 0x112008a0")]
    public const float VandammeDrawOut = 3.3333333f;

    [Provenance(ProvenanceKind.Decoded, "shimmy blend 0x111ffe80: BlendChangeTime 20 marks the corner, stepping at 4 dt")]
    public const float ShimmyCornerBlendTime = 20f;

    [Provenance(ProvenanceKind.Decoded, "shimmy blend 0x111ffe80: corner step 4 dt")]
    public const float ShimmyCornerStep = 4f;

    [Provenance(ProvenanceKind.Decoded, "shimmy blend 0x111ffe80: a side speed above 1 picks a shimmy side")]
    public const float ShimmySideSpeed = 1f;

    [Provenance(ProvenanceKind.Decoded, "climb step 0x11200140: bCanStop on crossing 0.45")]
    public const float ClimbStopUp = 0.45f;

    [Provenance(ProvenanceKind.Decoded, "climb step 0x11200140: bCanStop on a wrap below 0.5")]
    public const float ClimbStopWrap = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "climb step 0x11200140: bCanStop past 0.95")]
    public const float ClimbStopEnd = 0.95f;

    [Provenance(ProvenanceKind.Decoded, "climb step 0x11200140: the left part is the middle half (0.25, 0.75)")]
    public const float ClimbLeftFrom = 0.25f;

    [Provenance(ProvenanceKind.Decoded, "climb step 0x11200140")]
    public const float ClimbLeftTo = 0.75f;

    [Provenance(ProvenanceKind.Decoded, "climb step 0x11200140: the left hand restarts half a cycle in")]
    public const float ClimbLeftOffset = 0.5f;

    [Provenance(ProvenanceKind.Decoded, "PlayJumpStart 0x111f97a0: BlendChangeTime 0.05 (0x3d4ccccd)")]
    public const float JumpStartBlendChangeTime = 0.05f;

    [Provenance(ProvenanceKind.Decoded, "PlayJumpStart 0x111f97a0: a horizontal speed above 10 uu/s jumps forward")]
    public const float JumpForwardSpeed = 10f;

    [Provenance(ProvenanceKind.Decoded, "follow-parent blend 0x112004b0: channel 0 plays the grabbed catch clip (name 0xb2d1, GrbdStAlBg0 in the editor's name table)")]
    public const string FollowParentCatchClip = "GrbdStAlBg0";
}
