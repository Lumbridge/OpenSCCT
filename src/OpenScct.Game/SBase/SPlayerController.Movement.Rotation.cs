using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController view and pawn rotation: the view (controller) rotation from the turn and look inputs, the
// limited view of the traversal states, the pawn yaw that follows the stick for the spy (Mario movement) or the view
// for weapon movement, and the spy's air steering. Written from spec/movement/input-and-rotation.md.
public partial class SPlayerController
{
    /// <summary>
    /// The view rotation for this frame: the merc's mouse model or the spy's camera model, with the desired rotation
    /// and the shot marker kept in step.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdateRotation", Sha1 = "715ba8d8ef2503e2b5603c5e0962749ee439c367")]
    [PortOf("uc:SBase.SPlayerController:UpdateRotation", Spec = "spec/movement/input-and-rotation.md#view-rotation-updaterotation")]
    public virtual void UpdateRotation(float DeltaTime, float maxPitch)
    {
        MyDesiredRotation = LocalMyDesiredRotation;
        if (bLocalShootedPawn != bShootedPawn)
        {
            bLocalShootedPawn = bShootedPawn;
            fDateLastShootedPawn = Level.TimeSeconds;
        }

        if (SBPawn is SPAWNDEFENSE)
        {
            UpdateRotationDefenseur_Mouse(DeltaTime);
        }
        else
        {
            UpdateCamRotation(DeltaTime);
        }

        aTurnPrevious = aTurn;
        LocalMyDesiredRotation = MyDesiredRotation;
    }

    /// <summary>
    /// Mouse view: the desired rotation's yaw and pitch change by int(input x ratio x sensitivity x dt x 50) each
    /// (truncated separately), pitch clamped to -12000..13000; the view is set to it and it goes to the server.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdateRotationAttaquant_Mouse", Sha1 = "f964e120acc983da45a832c27c4bed2f5805f215")]
    [PortOf("uc:SBase.SPlayerController:UpdateRotationAttaquant_Mouse", Spec = "spec/movement/input-and-rotation.md#view-rotation-updaterotation")]
    public virtual void UpdateRotationAttaquant_Mouse(float dt)
    {
        // The input moves the desired rotation (the aim the rifle shoots along); the view is set to it.
        float r = bBinocularVision ? MovementK.BinocularRotationRatio : 1f;
        FRotator v = MyDesiredRotation;
        v.Yaw += MovementMath.Int(fTurnValue * r * fSensibiliteSouris * dt * MovementK.RotationRateScale);
        v.Pitch += MovementMath.Int(fLookUpValue * r * fSensibiliteSouris * dt * MovementK.RotationRateScale);
        v = v.Normalize();
        v.Pitch = SvmMath.Clamp(v.Pitch, MovementK.PitchMin, MovementK.PitchMax);
        MyDesiredRotation = v;
        Rotation = v;
        UpdateDesiredRotation(MyDesiredRotation);
    }

    /// <summary>The merc's mouse view: the same as the spy-side mouse model.</summary>
    [Uc("SBase.SPlayerController.UpdateRotationDefenseur_Mouse", Sha1 = "a703bd2ff05cb07f611079af7a2f7d6b22377aed")]
    [PortOf("uc:SBase.SPlayerController:UpdateRotationDefenseur_Mouse", Spec = "spec/movement/input-and-rotation.md#view-rotation-updaterotation")]
    public virtual void UpdateRotationDefenseur_Mouse(float dt) => UpdateRotationAttaquant_Mouse(dt);

    /// <summary>
    /// The spy's camera: armed, the pitch is repelled past 6000 (clamped -12000..15000) and the target snap is
    /// added; unarmed, the yaw can be inverted and the pitch is clamped -12000..13000.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdateCamRotation", Sha1 = "5eaf34d65f3f2ba02e166e58bf468e945e30d9ef")]
    [PortOf("uc:SBase.SPlayerController:UpdateCamRotation", Spec = "spec/movement/input-and-rotation.md#view-rotation-updaterotation")]
    public virtual void UpdateCamRotation(float DeltaTime)
    {
        float r = bBinocularVision ? MovementK.BinocularRotationRatio : 1f;
        float sens = fSensibiliteSouris;
        float k = MovementK.RotationRateScale;
        FRotator v = Rotation.Normalize();
        if (SBPawn is SPawnAttaque { WeaponLoaded: 2 })
        {
            float c = 1f;
            int absPitch = v.Pitch < 0 ? -v.Pitch : v.Pitch;
            if (absPitch > MovementK.ArmedPitchRepulsionStart)
            {
                c = 1f - (absPitch - MovementK.ArmedPitchRepulsionStart) / MovementK.ArmedPitchRepulsionSpan;
            }

            if ((v.Pitch > 0 && fLookUpValue < 0f) || (v.Pitch < 0 && fLookUpValue > 0f))
            {
                c = 1f;
            }

            v.Pitch += MovementMath.Int(r * c * fLookUpValue * DeltaTime * sens * k);
            v = v.Normalize();
            v.Pitch = SvmMath.Clamp(v.Pitch, MovementK.PitchMin, MovementK.ArmedPitchMax);
            if (bSnappedToTarget)
            {
                v.Yaw += MovementMath.Int(fSnapAngleX * k * DeltaTime);
                v.Pitch += MovementMath.Int(fSnapAngleY * k * DeltaTime);
            }

            v.Yaw += MovementMath.Int(r * fTurnValue * DeltaTime * sens * k);
        }
        else
        {
            float s = bInvertHorizontalAxisCamSpy ? -1f : 1f;
            v.Pitch += MovementMath.Int(r * sens * k * DeltaTime * fLookUpValue);
            v.Yaw += MovementMath.Int(r * sens * k * DeltaTime * s * fTurnValue);
            v = v.Normalize();
            v.Pitch = SvmMath.Clamp(v.Pitch, MovementK.PitchMin, MovementK.PitchMax);
        }

        Rotation = v;
    }

    /// <summary>
    /// The view inside a yaw and pitch window around a reference (ledges, pipes, zip lines, back to wall): turned by
    /// the inputs, clamped when it already lies inside, else brought back toward the window at 50000 x m_dT per
    /// frame.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdateLimitedRotation", Sha1 = "096190d9871cb52169926786d12088f6e3741dae")]
    [PortOf("uc:SBase.SPlayerController:UpdateLimitedRotation", Spec = "spec/movement/input-and-rotation.md#limited-view-rotation-updatelimitedrotation")]
    public virtual void UpdateLimitedRotation(float DeltaTime, int minYaw, int maxYaw, int minPitch, int maxPitch, int YawRef, int PitchRef)
    {
        float f = DeltaTime * MovementK.RotationRateScale * fSensibiliteSouris;
        FRotator v = Rotation;
        if (bInvertHorizontalAxisCamSpy && (SBPawn?.WeaponLoaded ?? 0) == 0)
        {
            v.Yaw -= MovementMath.Int(fTurnValue * f);
        }
        else
        {
            v.Yaw += MovementMath.Int(fTurnValue * f);
        }

        v.Pitch += MovementMath.Int(fLookUpValue * f * MovementK.LimitedPitchFactor);
        int recentre = MovementMath.Int(Level.m_dT * MovementK.LimitedRecentreSpeed);
        v.Yaw = LimitAxis(Rotation.Yaw, v.Yaw, YawRef, minYaw, maxYaw, recentre);
        v.Pitch = LimitAxis(Rotation.Pitch, v.Pitch, PitchRef, minPitch, maxPitch, recentre);
        Rotation = v;
    }

    private static int LimitAxis(int current, int wanted, int reference, int min, int max, int recentre)
    {
        if ((ClampRotation(reference, current, min, max) & 65535) == (current & 65535))
        {
            return ClampRotation(reference, wanted, min, max);
        }

        int t = (wanted - reference) & 65535;
        int clamped = t < 32768 ? SvmMath.Clamp(t, 0, max & 65535) : SvmMath.Clamp(t, min & 65535, 65535);
        if (clamped == t)
        {
            return wanted;
        }

        return ClampRotation(wanted, clamped + reference, -recentre, recentre);
    }

    /// <summary>
    /// Weapon movement: the acceleration is the stick in the pawn's flattened axes, and the pawn turns to the view
    /// yaw (pitch and roll kept). The pawn always faces the view.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdatePawnRotationWeapon", Sha1 = "b0b3bb3f74fd6105400d0a610fbaca7ebaaddaab")]
    [PortOf("uc:SBase.SPlayerController:UpdatePawnRotationWeapon", Spec = "spec/movement/input-and-rotation.md#pawn-rotation-for-weapon-movement-updatepawnrotationweapon")]
    public virtual FVector UpdatePawnRotationWeapon(float DeltaTime, int Forward, int Strafe)
    {
        if (Pawn is not { } pawn)
        {
            return default;
        }

        MovementMath.FlatAxes(pawn.Rotation, out FVector X, out FVector Y);
        FVector a = X * Forward / X.Size() + Y * Strafe / Y.Size();
        a.Z = 0f;
        FRotator r = pawn.Rotation;
        r.Yaw = Rotation.Yaw;
        pawn.Rotation = r;
        return a;
    }

    /// <summary>
    /// Mario movement: the pawn turns toward the stick direction (controller axes) at a rate set by its horizontal
    /// speed, and accelerates only along directions in front of it.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdatePawnRotationMario", Sha1 = "47412c1b9119b7d39d1b2bdd4566d81800effb30")]
    [PortOf("uc:SBase.SPlayerController:UpdatePawnRotationMario", Spec = "spec/movement/input-and-rotation.md#pawn-rotation-for-spy-movement-updatepawnrotationmario")]
    public virtual FVector UpdatePawnRotationMario(float DeltaTime, int Forward, int Strafe, FVector ForcedAcceleration = default)
    {
        if (Pawn is not { } pawn)
        {
            return default;
        }

        float v = pawn.Velocity.Size2D();
        float w = v < MovementK.MarioSlowTurnSpeed ? (v * (0.0125f * v - 2f) + 2f) * SpeedRot : SpeedRot;
        float maxDeltaTime = MovementK.MarioMaxStep / w;
        FVector a;
        if (!ForcedAcceleration.IsZero())
        {
            a = ForcedAcceleration;
        }
        else
        {
            MovementMath.FlatAxes(Rotation, out FVector X, out FVector Y);
            a = X.SafeNormal() * Forward + Y.SafeNormal() * Strafe;
        }

        a.Z = 0f;
        float dt = DeltaTime > maxDeltaTime ? maxDeltaTime : DeltaTime;
        float step = dt * w;
        bool zero = false;
        if (!a.IsZero())
        {
            FRotator r = pawn.Rotation;
            float d = MovementMath.Int((float)(MovementMath.Yaw(a) - r.Yaw)) & 65535;
            if (d < 32768f && d > step)
            {
                d = step;
                zero = pawn.Velocity.Size() < MovementK.MarioStopTurnSpeed;
            }
            else if (d >= 32768f && d < 65536f - step)
            {
                d = 65536f - step;
                zero = pawn.Velocity.Size() < MovementK.MarioStopTurnSpeed;
            }

            r.Yaw += MovementMath.Int(d);
            pawn.Rotation = r;
        }

        pawn.Rotation.GetAxes(out FVector forward, out _, out _);
        float p = a | forward;
        if (zero || !(p > 0f))
        {
            return default;
        }

        return a.SafeNormal() * p;
    }

    /// <summary>
    /// The spy's air steering: the stick only accelerates along the pawn's facing, fading to nothing as the
    /// horizontal speed reaches RunningSpeed. The pawn rotation is not changed.
    /// </summary>
    [Uc("SBase.SPlayerController.UpdatePawnRotationMarioJump", Sha1 = "2526e5a2706a8902e0e3a11da2b81acb1284cdfb")]
    [PortOf("uc:SBase.SPlayerController:UpdatePawnRotationMarioJump", Spec = "spec/movement/input-and-rotation.md#pawn-rotation-for-spy-jumps-updatepawnrotationmariojump")]
    public virtual FVector UpdatePawnRotationMarioJump(float DeltaTime, int Forward, int Strafe, FVector ForcedAcceleration = default)
    {
        if (Pawn is not { } pawn)
        {
            return default;
        }

        FVector a;
        if (!ForcedAcceleration.IsZero())
        {
            a = ForcedAcceleration.SafeNormal();
        }
        else
        {
            MovementMath.FlatAxes(Rotation, out FVector X, out FVector Y);
            a = X * Forward / X.Size() + Y * Strafe / Y.Size();
        }

        a.Z = 0f;
        float n = a.Size();
        if (n > 0f)
        {
            float h = pawn.Velocity.Size2D();
            pawn.Rotation.GetAxes(out FVector forward, out _, out _);
            if (h > MovementK.MarioJumpSteerSpeed)
            {
                float t = (pawn.Velocity | forward) / h;
                if ((a | forward) * t < 0f)
                {
                    t = -t;
                }

                a = forward * n * t;
                float running = (pawn as SBasePawn)?.RunningSpeed ?? 385f;
                a *= SvmMath.Max(0f, 1f - SvmMath.Abs(h) / running);
            }
            else
            {
                a = forward * (a | forward);
            }
        }

        return a;
    }

    /// <summary>The stick's component along the way the pawn faces (pipes and ladders).</summary>
    [Uc("SBase.SPlayerController.fGetForwardForPipe", Sha1 = "868161e4586d1940847e5a503326a1960cc5f93b")]
    [PortOf("uc:SBase.SPlayerController:fGetForwardForPipe", Spec = "spec/movement/input-and-rotation.md#pipe-stick-projection-fgetforwardforpipe")]
    public virtual float fGetForwardForPipe(float Forward, float Strafe)
    {
        if (Pawn is not { } pawn)
        {
            return 0f;
        }

        MovementMath.FlatAxes(Rotation, out FVector X, out FVector Y);
        FVector a = X * Forward / X.Size() + Y * Strafe / Y.Size();
        return a | pawn.Rotation.Vector();
    }

    /// <summary>Client to server (unreliable): the view rotation the server keeps for this player.</summary>
    [Uc("SBase.SPlayerController.UpdateDesiredRotation", Sha1 = "f8a03f1ec14ea7eb718561af0574c8b2b78de07f")]
    [PortOf("uc:SBase.SPlayerController:UpdateDesiredRotation", Spec = "spec/movement/input-and-rotation.md#view-rotation-updaterotation")]
    public virtual void UpdateDesiredRotation(FRotator Rot)
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            MovementNet.Of(this).UpdateDesiredRotation(this, Rot);
            return;
        }

        MyDesiredRotation = Rot;
    }
}
