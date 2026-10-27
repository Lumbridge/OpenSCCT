using System;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// The aim and look-at bone control a skeletal mesh instance keeps (spec/native/aim-ik.md): the request the script
/// natives AimAt and LookAt leave for the next animation update (a type, the view and original directions, the angle
/// limits), and the angle pairs the update steps toward it at the instance's IK speed. The pose build turns the pairs
/// into bone rotations (the upper body for AimAt AAFULL: yaw and pitch on bone 4, B Spine2). The fields keep the
/// instance's layout order; the offsets are in the member comments.
/// </summary>
public sealed class MeshAimIK
{
    /// <summary>The bone the AAFULL pair turns (the reference skeleton's bone 4).</summary>
    [Provenance(ProvenanceKind.Decoded, "BuildPose's per-bone IK call editor 0x111022f0: case 4 rotates by the pair at +0x334/+0x338")]
    public const int FullAimBone = 4;

    /// <summary>The bone name of <see cref="FullAimBone"/> in the SPerso skeletons (ATT_01, DEF_01).</summary>
    public const string FullAimBoneName = "B Spine2";

    /// <summary>Share of the aim angle the AAFULL pair takes (0x3f59999a).</summary>
    [Provenance(ProvenanceKind.Decoded, "IK update editor 0x110fbb90 case 6: FUN_110fb890(0.85, speed, +0x334, +0x338)")]
    public const float FullAimShare = 0.85f;

    /// <summary>Instance IK speed and return speed in radians per second (+0x384 and +0x388).</summary>
    [Provenance(ProvenanceKind.Decoded, "USkeletalMeshInstance constructor editor 0x1116cb80: +0x384 = +0x388 = 8.0")]
    public const float DefaultSpeed = 8f;

    /// <summary>The AimAt types (SBasePawn.eAimAtType) and the request types the instance stores for them.</summary>
    public enum AimType
    {
        AAFULL = 0,
        AAVERT = 1,
        AAHOH = 2,
        AABTW = 3,
    }

    /// <summary>+0x314, +0x318: the look-at pair (yaw, pitch), radians.</summary>
    public float LookYaw;

    public float LookPitch;

    /// <summary>+0x31c: the vertical aim (AAVERT).</summary>
    public float Vertical;

    /// <summary>+0x320.</summary>
    public float Single2;

    /// <summary>+0x324, +0x328: the AAHOH pair.</summary>
    public float HohYaw;

    public float HohPitch;

    /// <summary>+0x32c, +0x330: the AABTW pair.</summary>
    public float BtwYaw;

    public float BtwPitch;

    /// <summary>+0x334, +0x338: the AAFULL pair, radians; the pose turns <see cref="FullAimBone"/> by them.</summary>
    public float FullYaw;

    public float FullPitch;

    /// <summary>+0x340: the pending request (0 none; 6 AAFULL, 2 AAVERT, 4 AAHOH, 5 AABTW; 1 and 3 the look-ats).</summary>
    public int Request;

    /// <summary>+0x344 to +0x350: the limits of the request, radians (min and max horizontal, min and max vertical).</summary>
    public float MinHori;

    public float MaxHori;

    public float MinVert;

    public float MaxVert;

    /// <summary>+0x354: the view direction of the request.</summary>
    public FVector ViewDir;

    /// <summary>+0x360: the original direction (the pawn's facing).</summary>
    public FVector OriginalDir;

    /// <summary>+0x384, +0x388: the speeds.</summary>
    public float Speed = DefaultSpeed;

    public float ReturnSpeed = DefaultSpeed;

    /// <summary>+0x33c: the time of the last update.</summary>
    public float DeltaTime;

    /// <summary>
    /// SBasePawn.AimAt (native 1409): stores the request on the mesh instance (USkeletalMeshInstance 0x110f9160) with
    /// the type mapped AAFULL 6, AAVERT 2, AAHOH 4, AABTW 5 and the limits converted from degrees.
    /// </summary>
    [UcNative("ASBasePawn::execAimAt", Exe = 0x10ac26d0, Editor = 0x111ff680)]
    [PortOf("nat:SBase.SBasePawn.AimAt", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "execAimAt 0x111ff680 -> 0x111fac60 (jump table 0x111facd0: 6, 2, 4, 5) -> 0x110f9160 (fields +0x340 to +0x368, degrees x 0.017453)")]
    public void AimAt(AimType type, FVector viewDir, FVector originalDir, float minHori, float maxHori, float minVert, float maxVert)
    {
        int request = type switch
        {
            AimType.AAFULL => 6,
            AimType.AAVERT => 2,
            AimType.AAHOH => 4,
            AimType.AABTW => 5,
            _ => -1,
        };
        if (request < 0)
        {
            return;
        }

        Request = request;
        ViewDir = viewDir;
        OriginalDir = originalDir;
        MinHori = minHori * DegToRad;
        MaxHori = maxHori * DegToRad;
        MinVert = minVert * DegToRad;
        MaxVert = maxVert * DegToRad;
    }

    /// <summary>
    /// The IK step of UpdateAnimation (editor 0x110fbb90, called first with the update's time): the pair of the
    /// request steps toward it, every other angle returns to 0, and the request is cleared. Types 6 (AAFULL) and 0
    /// (none) are the ones the SBase script uses; the others are ported for the pairs AAFULL leaves alone.
    /// </summary>
    [UcNative("USkeletalMeshInstance::UpdateAnimation (IK step)", Editor = 0x110fbb90)]
    [PortOf("eng:USkeletalMeshInstance::UpdateAnimation", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "0x110fbb90 cases 0, 4, 5, 6; the steps 0x110fb890, 0x110fb440, 0x110fb3c0")]
    [Provenance(ProvenanceKind.Estimated, "Cases 1 to 3 (look-at and AAVERT) step only the pairs they leave at rest here; their own steps are not ported (no SBase script call uses them)")]
    public void Update(float dt)
    {
        DeltaTime = dt;
        switch (Request)
        {
            case 4:
                ReturnPair(ref LookYaw, ref LookPitch);
                Return(ref Vertical);
                Return(ref Single2);
                Step(0.85f, Speed, ref HohYaw, ref HohPitch);
                ReturnPair(ref BtwYaw, ref BtwPitch);
                ReturnPair(ref FullYaw, ref FullPitch);
                break;
            case 5:
                ReturnPair(ref LookYaw, ref LookPitch);
                Return(ref Vertical);
                Return(ref Single2);
                ReturnPair(ref HohYaw, ref HohPitch);
                Step(0.85f, Speed, ref BtwYaw, ref BtwPitch);
                ReturnPair(ref FullYaw, ref FullPitch);
                break;
            case 6:
                ReturnPair(ref LookYaw, ref LookPitch);
                Return(ref Vertical);
                Return(ref Single2);
                ReturnPair(ref HohYaw, ref HohPitch);
                ReturnPair(ref BtwYaw, ref BtwPitch);
                Step(FullAimShare, Speed, ref FullYaw, ref FullPitch);
                break;
            default:
                ReturnPair(ref LookYaw, ref LookPitch);
                Return(ref Vertical);
                Return(ref Single2);
                ReturnPair(ref HohYaw, ref HohPitch);
                ReturnPair(ref BtwYaw, ref BtwPitch);
                ReturnPair(ref FullYaw, ref FullPitch);
                break;
        }

        Request = 0;
    }

    /// <summary>Back to rest at once (a new pawn, a reset mesh).</summary>
    public void Reset()
    {
        LookYaw = LookPitch = Vertical = Single2 = HohYaw = HohPitch = BtwYaw = BtwPitch = FullYaw = FullPitch = 0f;
        Request = 0;
    }

    private const float DegToRad = 0.017453f;

    /// <summary>
    /// 0x110fb890: the targets are share x asin(view z) (pitch) and share x the signed angle between the view and the
    /// original direction in the horizontal plane (yaw, negative when the view is to the right); both move at the
    /// speed, the smaller change slowed so both arrive together, each by at most speed x dt, within share x the limits.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "0x110fb890: asin of the clamped view Z, acos of the 2D normal dot, sign from (v.y a.x - a.y v.x) > 0, rate split by the ratio of the changes, clamps to share x +0x344..+0x350")]
    private void Step(float share, float speed, ref float yaw, ref float pitch)
    {
        float pitchTarget = SvmMath.Asin(Math.Clamp(ViewDir.Z, -1f, 1f)) * share;
        float dPitch = pitchTarget - pitch;
        FVector a = new FVector(OriginalDir.X, OriginalDir.Y, 0f).SafeNormal();
        FVector v = new FVector(ViewDir.X, ViewDir.Y, 0f).SafeNormal();
        float yawTarget = SvmMath.Acos(Math.Clamp((v | a), -1f, 1f));
        if ((v.Y * a.X) - (a.Y * v.X) > 0f)
        {
            yawTarget = -yawTarget;
        }

        float dYaw = (yawTarget * share) - yaw;
        float yawSpeed = speed;
        float pitchSpeed = speed;
        if (dPitch != 0f && dYaw != 0f)
        {
            float r = dYaw / dPitch;
            if (MathF.Abs(r) <= 1f)
            {
                yawSpeed = MathF.Abs(r * speed);
            }
            else
            {
                pitchSpeed = MathF.Abs(speed / r);
            }
        }

        float maxYaw = yawSpeed * DeltaTime;
        dYaw = Math.Clamp(dYaw, -maxYaw, maxYaw);
        yaw = Math.Clamp(yaw + dYaw, share * MinHori, share * MaxHori);
        float maxPitch = pitchSpeed * DeltaTime;
        dPitch = Math.Clamp(dPitch, -maxPitch, maxPitch);
        pitch = Math.Clamp(pitch + dPitch, share * MinVert, share * MaxVert);
    }

    /// <summary>0x110fb440: a pair returns to 0 at the return speed, the smaller one slowed so both arrive together.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x110fb440")]
    private void ReturnPair(ref float a, ref float b)
    {
        float rateA = ReturnSpeed;
        float rateB = ReturnSpeed;
        if (a != 0f && b != 0f)
        {
            float r = a / b;
            if (MathF.Abs(r) <= 1f)
            {
                rateA = MathF.Abs(r * ReturnSpeed);
            }
            else
            {
                rateB = MathF.Abs(ReturnSpeed / r);
            }
        }

        a = Toward0(a, rateA * DeltaTime);
        b = Toward0(b, rateB * DeltaTime);
    }

    /// <summary>0x110fb3c0: one angle returns to 0 at the return speed.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x110fb3c0")]
    private void Return(ref float a) => a = Toward0(a, ReturnSpeed * DeltaTime);

    private static float Toward0(float value, float step) =>
        value > 0f ? MathF.Max(value - step, 0f) : value < 0f ? MathF.Min(value + step, 0f) : 0f;
}
