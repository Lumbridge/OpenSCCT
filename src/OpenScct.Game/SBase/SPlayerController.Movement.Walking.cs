using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController walking rules the walking states share: the stick normalisation, the ground speed from
// the acceleration request, the ceiling probe and the roll start. Written from spec/movement/walking.md and
// spec/movement/roll.md.
public partial class SPlayerController
{
    private static readonly FVector CeilingProbeExtent =
        new(MovementK.CeilingProbeRadius, MovementK.CeilingProbeRadius, MovementK.CeilingProbeHalfHeight);

    /// <summary>The stick clamp of every walking PlayerMove: a longer stick is scaled back to 6000.</summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md stick normalisation rule 1")]
    public void ClampStick()
    {
        float sq = aForward * aForward + aStrafe * aStrafe;
        if (sq > MovementK.StickClampSquared)
        {
            float k = MovementK.StickFullScale / SvmMath.Sqrt(sq);
            aForward *= k;
            aStrafe *= k;
        }
    }

    /// <summary>The Mario stick scaling: adrenaline, then the crouched percentage or the standing boost.</summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md stick normalisation rule 2")]
    public void MarioStickScale(SBasePawn pawn)
    {
        if (fTimeWhenHit + AdrenalineTime >= Level.TimeSeconds)
        {
            aForward *= AdrenalineFactor;
            aStrafe *= AdrenalineFactor;
        }

        float k = pawn.bIsCrouched ? pawn.CrouchedPct : MovementK.MarioStandingBoost;
        aForward *= k;
        aStrafe *= k;
    }

    /// <summary>The weapon stick scaling: smoke, the input curve, then the spy or merc factors.</summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md stick normalisation rule 3")]
    public void WeaponStickScale(SBasePawn pawn)
    {
        bool crouched = pawn.bIsCrouched;
        bool merc = pawn is SPAWNDEFENSE;
        if (merc && btoCought)
        {
            if (crouched)
            {
                aForward *= aForward > 0f ? MovementK.SmokeCrouchForward : MovementK.SmokeCrouchBack;
                aStrafe *= MovementK.SmokeCrouchStrafe;
            }
            else
            {
                aForward *= aForward > 0f ? MovementK.ArmedForward : MovementK.ArmedBack;
                aStrafe *= MovementK.ArmedStrafe;
            }
        }

        float u = aForward / MovementK.StickFullScale;
        float curve = crouched
            ? (MovementK.WeaponCurveCrouchA * u + MovementK.WeaponCurveCrouchB) * u + MovementK.WeaponCurveCrouchC
            : (MovementK.WeaponCurveStandA * u + MovementK.WeaponCurveStandB) * u + MovementK.WeaponCurveStandC;
        aForward *= curve;
        aStrafe *= curve;
        if (!merc)
        {
            if (crouched)
            {
                if (CeilingAbove(pawn))
                {
                    bForceExitFireMode = true;
                }

                aForward *= MovementK.ArmedCrouched;
                aStrafe *= MovementK.ArmedCrouched;
            }
            else
            {
                aForward *= aForward > 0f ? MovementK.ArmedForward : MovementK.ArmedBack;
                aStrafe *= MovementK.ArmedStrafe;
            }
        }
        else if (!btoCought)
        {
            float s = Level.TimeSeconds - fLastChargingTime;
            float k = s < MovementK.MercChargeSlowTime ? MovementK.MercChargeSlow
                : SvmMath.Min(MovementK.MercChargeSlow + (s - MovementK.MercChargeSlowTime), 1f);
            aForward *= MovementK.MercSpeedFactor * k;
            aStrafe *= MovementK.MercSpeedFactor * k;
        }
        else
        {
            aForward *= MovementK.MercSpeedFactor;
            aStrafe *= MovementK.MercSpeedFactor;
        }
    }

    /// <summary>The acceleration request every walking state passes to its rotation helper: int(0.5 x stick).</summary>
    public int ForwardRequest => MovementMath.Int(MovementK.AccelRequestScale * aForward);

    /// <summary>The strafe request: int(0.5 x stick).</summary>
    public int StrafeRequest => MovementMath.Int(MovementK.AccelRequestScale * aStrafe);

    /// <summary>GroundSpeed = RunningSpeed x |newAccel| / 3000.</summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md ground speed formula")]
    public static float GroundSpeedFor(SBasePawn pawn, FVector newAccel) =>
        pawn.RunningSpeed * newAccel.Size() / MovementK.SpeedDivisor;

    /// <summary>
    /// The Mario ground speed: the crouched low-ceiling cap at 700, the tiny-request cut, the formula, and the dead
    /// zone with its 10 uu/s hysteresis.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md ground speed, Mario movement rules 1 to 4")]
    public void MarioGroundSpeed(SBasePawn pawn, ref FVector newAccel)
    {
        if (pawn.bIsCrouched && newAccel.Size() > MovementK.LowCeilingAccel && CeilingAbove(pawn))
        {
            newAccel = newAccel.SafeNormal() * MovementK.LowCeilingAccel;
        }

        if (newAccel.Size() < 1f)
        {
            newAccel = default;
        }

        pawn.GroundSpeed = GroundSpeedFor(pawn, newAccel);
        float threshold = pawn.bIsCrouched ? MinSpeedInCrouch : MinSpeedStand;
        if (pawn.GroundSpeed < threshold - fHysteresisDiffSpeed)
        {
            pawn.GroundSpeed = 0f;
            fHysteresisDiffSpeed = 0f;
        }
        else
        {
            fHysteresisDiffSpeed = MovementK.SpeedHysteresis;
        }

        if (pawn.GroundSpeed == 0f)
        {
            newAccel = default;
        }
    }

    /// <summary>The weapon ground speed: recomputed only while walking, with the 75 / 50 dead zone.</summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md ground speed, weapon movement")]
    public static void WeaponGroundSpeed(SBasePawn pawn, ref FVector newAccel)
    {
        if (newAccel.Size() < 1f)
        {
            newAccel = default;
        }

        if (pawn.Physics != EPhysics.PHYS_Walking)
        {
            return;
        }

        pawn.GroundSpeed = GroundSpeedFor(pawn, newAccel);
        float deadZone = pawn.bIsCrouched ? MovementK.WeaponDeadZoneCrouched : MovementK.WeaponDeadZoneStanding;
        if (pawn.GroundSpeed < deadZone)
        {
            pawn.GroundSpeed = 0f;
        }

        if (pawn.GroundSpeed == 0f)
        {
            newAccel = default;
        }
    }

    /// <summary>The ceiling probe: a (35, 35, 50) box traced 20 up from the pawn hits something.</summary>
    public bool CeilingAbove(SBasePawn pawn) =>
        MovementNatives.Trace(pawn, out _, out _, pawn.Location + new FVector(0f, 0f, MovementK.CeilingProbeUp), pawn.Location, true,
            CeilingProbeExtent) is not null;

    /// <summary>
    /// The unarmed resting states' draw: a fire-mode request (not while sticky-shocking) switches the laser on when
    /// asked, then throws a carried object or plays the draw and enters the draw transition.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md s_PlayerWalkingSt0H / Cr0H PlayerTick")]
    public void DrawRequest(int drawAction)
    {
        if (!bForceFireMode || SBPawn is not { } pawn || pawn.bStickyShocking)
        {
            return;
        }

        bForceFireMode = false;
        if (bForceLaserFireMode)
        {
            MovementService.HooksOf(this).SwitchLaser(this, true);
        }

        if (pawn.HandItem is not null)
        {
            ChangeStateIfPorted(MovementStateNames.s_ThrowGoTo);
            return;
        }

        pawn.ChangeEnumAction(drawAction);
        eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_0HTo2H);
    }

    /// <summary>
    /// The armed resting states' holster (spy only): a fresh press of the fire mode (after a release) or a forced exit,
    /// with no input lock and no sticky shock, plays the holster and enters the holster transition.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "walking.md s_PlayerWalkingSt2H / Cr2H PlayerTick")]
    [Provenance(ProvenanceKind.Estimated, "Both requests are cleared when the holster starts")]
    public void HolsterRequest(int holsterAction)
    {
        if (SBPawn is not { } pawn || pawn is SPAWNDEFENSE || !(bForceExitFireMode || (bTestBoutonLache && bFireMode == 1)) ||
            iDontUpdatePadInput != 0 || pawn.bStickyShocking || IsInState(MovementStateNames.s_PlayerWalkingMario_T_2HToOH))
        {
            return;
        }

        bForceExitFireMode = false;
        bTestBoutonLache = false;
        pawn.ChangeEnumAction(holsterAction);
        eventChangeState(MovementStateNames.s_PlayerWalkingMario_T_2HToOH);
    }

    /// <summary>
    /// The crouch clip of a stand-to-crouch transition ended (base channel, controlling side): the crouched resting
    /// state for the hands.
    /// </summary>
    public void CrouchClipEnded(int Channel)
    {
        if (Channel != 0 || !IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.WeaponLoaded == 0)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
            eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
        }
        else
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_2H);
            eventChangeState(MovementStateNames.s_PlayerWalkingCr2H);
        }
    }

    /// <summary>Leaving an armed resting state: a merc can no longer be grabbed, a spy drops the fire-mode request.</summary>
    public void EndArmedRest()
    {
        if (SBPawn is SPAWNDEFENSE merc)
        {
            merc.bCanBeGrabbed = false;
        }
        else
        {
            bForceFireMode = false;
            fDateFireMode = 0f;
        }
    }

    /// <summary>The camera mode (camera port, through the movement hooks); the mode's view window is kept for the limited views.</summary>
    public void MovementCamera(ECamMode Mode)
    {
        MovementCamMode = Mode;
        MovementService.HooksOf(this).SetCameraMode(this, Mode);
    }

    /// <summary>The camera mode the movement last asked for.</summary>
    public ECamMode MovementCamMode { get; private set; }

    /// <summary>
    /// The view limited by the current camera mode's window around a reference (the script's
    /// UpdateLimitedRotation(dt, m_camera.GetMinYaw(), ...)).
    /// </summary>
    public void LimitedViewByCamera(float DeltaTime, int YawRef, int PitchRef)
    {
        CameraWindow w = CameraWindows.WindowOf(MovementCamMode);
        UpdateLimitedRotation(DeltaTime, w.MinYaw, w.MaxYaw, w.MinPitch, w.MaxPitch, YawRef, PitchRef);
    }

    /// <summary>The camera smoothing (camera port, through the movement hooks).</summary>
    public void MovementCameraSmooth(float Smooth) => MovementService.HooksOf(this).CameraSmooth(this, Smooth);

    /// <summary>The death check at the top of the walking moves: a dead pawn plays its death and goes to s_Dead.</summary>
    private bool DeadInWalking(SBasePawn pawn)
    {
        if (pawn.Health > 0 || IsInState(MovementStateNames.GameEnded))
        {
            return false;
        }

        MovementService.HooksOf(this).PlayAnimDeath(this);
        eventChangeState(MovementStateNames.s_Dead);
        return true;
    }

    /// <summary>
    /// The roll start inside Mario walking (roll.md): moving, pushing the stick and past the cooldown, a box trace
    /// along the facing (or the velocity when that is close) must be clear; a crouched spy rolls at 3000, a standing
    /// one dives at clamp(5.83 v + 2625, 3500, 4700). A pawn in the way sets <paramref name="bCantRollCuzColl"/>.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.PlayerMove", Spec = "spec/movement/roll.md#start-inside-s_playerwalkingmarioplayermove-after-the-stick-scaling-and-the-ceiling-check")]
    private void StartRoll(SBasePawn pawn, ref bool bCantRollCuzColl)
    {
        float now = Level.TimeSeconds;
        bool stick = !(aStrafe > -MovementK.StickDeadSquare && aStrafe < MovementK.StickDeadSquare) ||
                     !(aForward > -MovementK.StickDeadSquare && aForward < MovementK.StickDeadSquare);
        if (!(pawn.Velocity.Size() > MovementK.RollMinSpeed) || !stick || !(m_LastRollingTime < now - fTimeBetweenTwoRolls))
        {
            return;
        }

        pawn.Rotation.GetAxes(out FVector x, out _, out _);
        bool crouched = pawn.bIsCrouched;
        float length = crouched ? 0.5f * fDistanceToRoll : fDistanceToRoll;
        FVector start = crouched
            ? pawn.Location + new FVector(0f, 0f, MovementK.RollTraceCrouchedLift)
            : pawn.Location - new FVector(0f, 0f, MovementK.RollTraceStandingDrop);
        Actor? hit = MovementNatives.Trace(pawn, out _, out _, start + x * length, start, true, CeilingProbeExtent);
        if (hit is not null)
        {
            FVector y = pawn.Velocity.SafeNormal();
            if ((y | x) > MovementK.RollRetraceCone)
            {
                hit = MovementNatives.Trace(pawn, out _, out _, start + y * length, start, true, CeilingProbeExtent);
            }
        }

        if (hit is null)
        {
            bPressedJump = false;
            FVector dir = new FVector(aStrafe, aForward, 0f).SafeNormal();
            if (crouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ROLLING);
                aStrafeRoulade = MovementK.CrouchedRollMagnitude * dir.X;
                aForwardRoulade = MovementK.CrouchedRollMagnitude * dir.Y;
                eventChangeState(MovementStateNames.s_PlayerRolling);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                float g = SvmMath.Clamp(MovementK.DiveRollSlope * pawn.Velocity.Size() + MovementK.DiveRollOffset,
                    MovementK.DiveRollMin, MovementK.DiveRollMax);
                aStrafeRoulade = g * dir.X;
                aForwardRoulade = g * dir.Y;
                pawn.ChangeEnumAction(g > MovementK.DiveRollMin ? SBasePawn.SA_ROLLINGUP : SBasePawn.SA_ROLLING);
                eventChangeState(MovementStateNames.s_PlayerRollingUp);
            }
        }
        else if (hit is Pawn)
        {
            bCantRollCuzColl = true;
        }
    }
}
