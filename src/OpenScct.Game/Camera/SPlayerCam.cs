using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Camera;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SPlayerCam: the player's camera actor. The script side (smoothing, fixed target, bone attach, view windows,
/// the reset states) and the natives (the mode table, SetMode, UpdateView, the following-camera solver and the
/// first-person view), written from spec/native/camera.md. The camera is local to the machine that draws: it never
/// changes the simulation, apart from the controller's Location, which the solver moves to the camera spot as the
/// original does.
/// </summary>
public partial class SPlayerCam
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates,
        s_Following.Instance, s_Reseting.Instance, s_Fixed.Instance, s_IntroCinematic.Instance);

    public override StateTable GetStateTable() => ClassStates;

    // Solver memory that the original keeps outside the script fields.
    private FVector _prevPawnLocation;
    private bool _hasPrevPawn;
    private SPlayerCam? _classDefaults;

    /// <summary>The class default object (reset speeds are scaled from it).</summary>
    internal SPlayerCam ClassDefaults => _classDefaults ??= GetUcClass().Create() as SPlayerCam ?? this;

    /// <summary>The mode parameters the camera solves with (m_camParam[m_camMode]).</summary>
    public FECamParam CurrentParam => m_camParam[(int)m_camMode < CameraK.ModeCount ? (int)m_camMode : 0];

    // ---- state dispatch -----------------------------------------------------------------------------------

    public interface IUpdateRotation
    {
        void UpdateRotation(SPlayerCam self, Actor? ViewActor);
    }

    public interface IResetRotation
    {
        void ResetRotation(SPlayerCam self, FRotator resetRot, float Rate);
    }

    /// <summary>The behind view a state provides (the s_Following view solves the following camera).</summary>
    public interface IBehindView
    {
        bool BehindView(SPlayerCam self, SPlayerController PC, SBasePawn Pawn, float DeltaTime, out FVector CameraLocation,
            out FRotator CameraRotation);
    }

    public void eventUpdateRotation(Actor? ViewActor)
    {
        if (StateNode is IUpdateRotation s)
        {
            s.UpdateRotation(this, ViewActor);
        }
        else
        {
            UpdateRotation(ViewActor);
        }
    }

    public void eventResetRotation(FRotator resetRot, float Rate)
    {
        if (StateNode is IResetRotation s)
        {
            s.ResetRotation(this, resetRot, Rate);
        }
        else
        {
            ResetRotation(resetRot, Rate);
        }
    }

    // ---- script functions ---------------------------------------------------------------------------------

    [Uc("SBase.SPlayerCam.PostBeginPlay", Sha1 = "dbadb7506349711799ad15959b689117a8318cb8")]
    [PortOf("uc:SBase.SPlayerCam:PostBeginPlay", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    protected override void PostBeginPlay()
    {
        InitCameraSettings();
        m_prevDistance = CameraK.StartDistance;
        m_prevIdealDistance = CameraK.StartDistance;
    }

    [Uc("SBase.SPlayerCam.UpdateRotation", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerCam:UpdateRotation", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public virtual void UpdateRotation(Actor? ViewActor)
    {
    }

    [Uc("SBase.SPlayerCam.ResetRotation", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerCam:ResetRotation", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public virtual void ResetRotation(FRotator resetRot, float Rate)
    {
    }

    /// <summary>The current mode's entry.</summary>
    [Uc("SBase.SPlayerCam.GetCurrentParam", Sha1 = "ea729c53d4e97f255bd9fd1c5838c1fd243b47f4")]
    [PortOf("uc:SBase.SPlayerCam:GetCurrentParam", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public FECamParam GetCurrentParam() => CurrentParam;

    /// <summary>Smoothing on; a value of 0 takes the side's default (150 spy, 15000 merc).</summary>
    [Uc("SBase.SPlayerCam.ActivateSmooth", Sha1 = "ef52fc2af3b875304f9163370684c451fc1f7d2c")]
    [PortOf("uc:SBase.SPlayerCam:ActivateSmooth", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void ActivateSmooth(float SmoothValue = 0f, bool SmoothIsAlwaysActive = false)
    {
        m_SmoothDisplacement = true;
        if (SmoothValue == 0f)
        {
            SmoothValue = m_EPC?.SBPawn?.SPawnType == PawnType.SPAWNOFFENSE ? CameraK.SpySmoothDefault : CameraK.MercSmoothDefault;
        }

        m_Smoothvalue = SmoothValue;
        m_SmoothIsAlwaysActive = SmoothIsAlwaysActive;
    }

    /// <summary>Lets the smoothing end once caught up; with <paramref name="TurnOffImmediatly"/> the script sets the flag instead.</summary>
    [Uc("SBase.SPlayerCam.DesactivateSmooth", Sha1 = "4ed24669b624cacd8467bfcf80bc4bb47c66f17f")]
    [PortOf("uc:SBase.SPlayerCam:DesactivateSmooth", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void DesactivateSmooth(bool TurnOffImmediatly = false)
    {
        if (TurnOffImmediatly)
        {
            m_SmoothDisplacement = true;
        }
        else
        {
            m_SmoothIsAlwaysActive = false;
        }
    }

    [Uc("SBase.SPlayerCam.SetFixeTarget", Sha1 = "a494ea4483bb007e118a7361cff40ace581005df")]
    [PortOf("uc:SBase.SPlayerCam:SetFixeTarget", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void SetFixeTarget(FVector targetPos, float speed, bool isCrouch = false)
    {
        m_fixedTargetSpeed = speed;
        m_fixedTargetPos = targetPos;
        m_fixedTarget = true;
    }

    [Uc("SBase.SPlayerCam.StopFixeTarget", Sha1 = "ebc15fc82b47650c70df89c2a233d74ba7de99d7")]
    [PortOf("uc:SBase.SPlayerCam:StopFixeTarget", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void StopFixeTarget() => m_fixedTarget = false;

    [Uc("SBase.SPlayerCam.GetCurrentDistance", Sha1 = "fde5e5033bae5df68dd9596940d713b0564d6f59")]
    [PortOf("uc:SBase.SPlayerCam:GetCurrentDistance", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public float GetCurrentDistance() => CurrentParam.Distance[(int)m_volSize & 3];

    [Uc("SBase.SPlayerCam.AttachCamToBone", Sha1 = "df362cba47193865989ca350f217b026884beca3")]
    [PortOf("uc:SBase.SPlayerCam:AttachCamToBone", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void AttachCamToBone(bool YesNo, Name BoneName = default, FVector offset = default)
    {
        m_bAttachToBone = YesNo;
        m_parentBone = BoneName;
        m_vRelativeLocation = offset;
        if (YesNo)
        {
            ResetEffects();
        }
    }

    [Uc("SBase.SPlayerCam.GetMinYaw", Sha1 = "fe489251e9e10f11d5cbff7ab2bb399e01802346")]
    [PortOf("uc:SBase.SPlayerCam:GetMinYaw", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public int GetMinYaw() => CurrentParam.minYaw;

    [Uc("SBase.SPlayerCam.GetMaxYaw", Sha1 = "5520785b2c0ce6174ad0c16b4bf41ee1422509e1")]
    [PortOf("uc:SBase.SPlayerCam:GetMaxYaw", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public int GetMaxYaw() => CurrentParam.maxYaw;

    [Uc("SBase.SPlayerCam.GetMinPitch", Sha1 = "3315c48a3d2656aa2ac93a9992e30217185c53f9")]
    [PortOf("uc:SBase.SPlayerCam:GetMinPitch", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public int GetMinPitch() => CurrentParam.minPitch;

    [Uc("SBase.SPlayerCam.GetMaxPitch", Sha1 = "f568a01521cf0df7a57ab457ecc0abb64879efa0")]
    [PortOf("uc:SBase.SPlayerCam:GetMaxPitch", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public int GetMaxPitch() => CurrentParam.maxPitch;

    /// <summary>Debug camera: 0 or 1 only; clears the controller's roll.</summary>
    [Uc("SBase.SPlayerCam.SetDebugCamMode", Sha1 = "230f290652034b03ce1db309b212102d33edea05")]
    [PortOf("uc:SBase.SPlayerCam:SetDebugCamMode", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
    public void SetDebugCamMode(int iMode)
    {
        if (iMode is < DCMMaxCamMode and >= 0)
        {
            DebugCamMode = iMode;
            if (m_EPC is { } pc)
            {
                pc.Rotation = new FRotator(pc.Rotation.Pitch, pc.Rotation.Yaw, 0);
            }
        }
    }

    // ---- natives ------------------------------------------------------------------------------------------

    /// <summary>Fills the 35 modes (InitCamParam).</summary>
    [UcNative("ASPlayerCam::execInitCameraSettings", Exe = 0x10a96920, Editor = 0x111f71d0)]
    [PortOf("nat:SBase.SPlayerCam.InitCameraSettings", Spec = "spec/native/camera.md#7-setmode-and-the-camera-state-natives")]
    public void InitCameraSettings() => CamParamTable.Fill(m_camParam);

    /// <summary>
    /// Changes the mode: leaving mode 34 turns smoothing on; Walking to Running and WalkingCr to RunningCr only
    /// switch; otherwise a collision mode left for one without collision keeps the pulled-in distance as the ideal,
    /// else the ideal distance jumps to the new mode's.
    /// </summary>
    [UcNative("ASPlayerCam::execSetMode", Exe = 0x10a967c0, Editor = 0x111f5170)]
    [PortOf("nat:SBase.SPlayerCam.SetMode", Spec = "spec/native/camera.md#7-setmode-and-the-camera-state-natives")]
    public void SetMode(ECamMode NewMode)
    {
        if (NewMode == m_camMode || (int)NewMode >= CameraK.ModeCount)
        {
            return;
        }

        ECamMode old = m_camMode;
        if (old == ECamMode.ECM_SmoothTransitionCentered)
        {
            m_SmoothDisplacement = true;
        }

        bool justRun = (old == ECamMode.ECM_Walking && NewMode == ECamMode.ECM_Running) ||
            (old == ECamMode.ECM_WalkingCr && NewMode == ECamMode.ECM_RunningCr);
        if (!justRun)
        {
            FECamParam from = m_camParam[(int)old];
            FECamParam to = m_camParam[(int)NewMode];
            m_prevIdealDistance = from.useColl != 0 && to.useColl == 0
                ? m_prevDistance
                : to.Distance[(int)m_volSize & 3];
        }

        m_camMode = NewMode;
    }

    /// <summary>
    /// ResettingRotation: true once the controller's yaw and pitch are each less than 5 units from
    /// m_resettingRotation (raw differences, no wrap); false while the game is paused. It only tests: nothing in the
    /// native turns the controller toward the target.
    /// </summary>
    [UcNative("ASPlayerCam::execResettingRotation", Exe = 0x10a96750, Editor = 0x111f5130)]
    [PortOf("nat:SBase.SPlayerCam.ResettingRotation", Spec = "spec/native/camera.md#7-setmode-and-the-camera-state-natives")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a96750: Level.Pauser gives false; |Yaw - target.Yaw| < 5 and |Pitch - target.Pitch| < 5 on the raw ints")]
    public bool ResettingRotation()
    {
        if (Level.Pauser is not null || m_EPC is not { } pc)
        {
            return false;
        }

        FRotator r = pc.Rotation;
        return System.Math.Abs(r.Yaw - m_resettingRotation.Yaw) < CameraK.ResetTolerance &&
            System.Math.Abs(r.Pitch - m_resettingRotation.Pitch) < CameraK.ResetTolerance;
    }

    /// <summary>
    /// FollowingRotation (native 1395), the s_Following rotation update: outside a pause, when the controller's pawn
    /// (SBPawn, else SpecTarget) is the view actor, it becomes m_sam and m_rotatedSam is set; otherwise m_rotatedSam is
    /// cleared. It turns nothing.
    /// </summary>
    [UcNative("ASPlayerCam::execFollowingRotation", Exe = 0x10a964f0, Editor = 0x111f5060)]
    [PortOf("nat:SBase.SPlayerCam.FollowingRotation", Spec = "spec/native/camera.md#7-setmode-and-the-camera-state-natives")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a964f0: pawn = SBPawn ?? SpecTarget; pawn == ViewActor with a mesh sets m_sam and m_rotatedSam (bit 0 of +0x17d8), else clears it")]
    public void FollowingRotation(Actor? ViewActor)
    {
        if (Level.Pauser is not null || m_EPC is not { } pc)
        {
            return;
        }

        SBasePawn? pawn = pc.SBPawn is { bDeleteMe: false } own ? own : pc.SpecTarget;
        if (pawn is not null && ReferenceEquals(pawn, ViewActor))
        {
            m_sam = pawn;
            m_rotatedSam = true;
            return;
        }

        m_rotatedSam = false;
    }

    /// <summary>The mode's name, as SGetCamModeStringFromEnum prints it.</summary>
    [UcNative("ASPlayerCam::execSGetCamModeStringFromEnum")]
    [PortOf("nat:SBase.SPlayerCam.SGetCamModeStringFromEnum", Spec = "spec/native/camera.md#7-setmode-and-the-camera-state-natives")]
    public static string SGetCamModeStringFromEnum(ECamMode Mode) => Mode.ToString();

    /// <summary>
    /// UpdateView: the view effects on a rotation. A live hit roll replaces the roll and fades toward 0 at
    /// m_hitFadeOut; a live shake replaces the roll (after the hit) and, once it has reached its target, the target
    /// moves toward 0 by m_shakeFadeOut and flips sign, while the shake follows it at m_shakeSpeed, so the roll swings
    /// from side to side as it dies down; a live tilt is added to the pitch and swings the same way. The values applied
    /// are the ones from before this frame's step. Every step is speed x Level.m_dT, truncated toward zero.
    /// </summary>
    [UcNative("ASPlayerCam::execUpdateView", Exe = 0x10a96220, Editor = 0x111f4e50)]
    [PortOf("nat:SBase.SPlayerCam.UpdateView", Spec = "spec/native/camera.md#6-shake-tilt-and-hit-roll-updateview-editor-0x111f4e50")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a96280: order, replace and add rules, target fade at the fade-out speed, Level.m_dT (+0x54c of the level)")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a96280 (REA): a reached shake or tilt target becomes -MoveTowardScalar(target, 0, fadeOut, dt) (FCHS after the call at 0x10a96340 and 0x10a963fc)")]
    [Provenance(ProvenanceKind.Estimated, "The original applies the result to the controller with a rotation-only MoveActor; the port returns it as the view rotation and leaves the controller's aim alone")]
    public FRotator UpdateView(FRotator Rot)
    {
        float dt = Level.m_dT;
        int pitch = Rot.Pitch;
        int roll = Rot.Roll;
        if (m_hitRoll != 0)
        {
            roll = m_hitRoll;
            m_hitRoll = CameraMath.ApproachInt(m_hitRoll, 0, m_hitFadeOut * dt);
        }

        if (m_shakeRoll != 0 || m_shakeTargetRoll != 0)
        {
            roll = m_shakeRoll;
            if (m_shakeRoll == m_shakeTargetRoll)
            {
                // The reached target comes back toward 0 and flips sign, so the shake swings to and fro as it dies down.
                m_shakeTargetRoll = -CameraMath.ApproachInt(m_shakeTargetRoll, 0, m_shakeFadeOut * dt);
            }

            m_shakeRoll = CameraMath.ApproachInt(m_shakeRoll, m_shakeTargetRoll, m_shakeSpeed * dt);
        }

        if (m_tiltPitch != 0 || m_tiltTargetPitch != 0)
        {
            pitch += m_tiltPitch;
            if (m_tiltPitch == m_tiltTargetPitch)
            {
                // The same swing as the shake: the reached target fades and flips sign.
                m_tiltTargetPitch = -CameraMath.ApproachInt(m_tiltTargetPitch, 0, m_tiltFadeOut * dt);
            }

            m_tiltPitch = CameraMath.ApproachInt(m_tiltPitch, m_tiltTargetPitch, m_tiltSpeed * dt);
        }

        return new FRotator(pitch, Rot.Yaw, roll);
    }

    // ---- the views --------------------------------------------------------------------------------------------

    /// <summary>The behind view of the current state, or false when the state has none (only s_Following solves).</summary>
    public bool BehindView(SPlayerController PC, SBasePawn Pawn, float DeltaTime, out FVector CameraLocation, out FRotator CameraRotation)
    {
        if (StateNode is IBehindView s)
        {
            return s.BehindView(this, PC, Pawn, DeltaTime, out CameraLocation, out CameraRotation);
        }

        CameraLocation = PC.Location;
        CameraRotation = PC.Rotation;
        return false;
    }

    /// <summary>
    /// The following camera (rule 3): start point, smoothing or fixed target, offset, twig, close-up lift, target
    /// sweep, target height, ideal distance, pitch curve, rate limit, collision and final clearance; the controller is
    /// moved to the camera spot and the view rotation gets the effects.
    /// </summary>
    [UcNative("ASPlayerCam::SolveFollowing", Exe = 0x10a94e00, Editor = 0x111f56a0)]
    [PortOf("nat:SBase.SPlayerCam.UpdateView", Spec = "spec/native/camera.md#3-following-camera-splayercam-s_following-editor-0x111f6af0-and-the-solver-0x111f56a0")]
    [Provenance(ProvenanceKind.Decoded, "camera.md rule 3 steps 1 to 15")]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111f56a0: the sweeps' source is the pawn (+0x17c8); the collision distance is rate limited from m_prevDistance (+0x17d0); both final clearance hits are held at m_absoluteMinDist (+0x18a4)")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a94e00: modes 4, 5, 28 use |Vz| + 190 while the pawn moves vertically; the close-up lift is dropped (and its memory cleared) when not close, and the raw lift is kept; the ideal distance grows by min(pawn move along the view, interSpeed dt); the collision speed is collCBInterSpeed only for a hit actor with bBlockCamera and neither bBlockActors nor bBlockPlayers; the collision sweep starts below the close-up lift; m_absoluteMinDist holds with or without useColl; the clearance sweeps use the collision flags")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a94e00 (REA): the collision sweep's end is lowered by the close-up lift as well as its start (0x10a9594d); with a fixed target a Time 0 hit is ignored (0x10a959ee); the mode 33 head room probe uses the collision flags and box (0x10a9606e); the corrected pitch is ftol(pitch + speed x dt)")]
    [Provenance(ProvenanceKind.Estimated, "The pawn's previous position (SBasePawn.m_prevPos, +0x4d4) is the pawn's location at the camera's last solve")]
    public void SolveFollowing(SPlayerController PC, SBasePawn Pawn, float DeltaTime, out FVector CameraLocation, out FRotator CameraRotation)
    {
        m_EPC ??= PC;
        m_sam = Pawn;
        m_volSize = EVolumeSize.EVS_Normal;
        float dt = DeltaTime;
        FECamParam p = CurrentParam;
        FVector box = p.useCylColl != 0
            ? new FVector(m_collisionRadius, m_collisionRadius, m_collisionHeight)
            : new FVector(CameraK.SmallBox, CameraK.SmallBox, CameraK.SmallBox);
        int pitch = FRotator.NormalizeAxis(PC.Rotation.Pitch);
        FVector forward = PC.Rotation.Vector();

        // 2. Start point, smoothing and fixed target.
        FVector start = Pawn.Location;
        start.Z = Pawn.Location.Z + Pawn.CollisionHeight - m_collisionHeight;
        if (m_camMode == ECamMode.ECM_SmoothTransitionCentered)
        {
            start = CameraBones.CenterOf(Pawn) + new FVector(0f, 0f, CameraK.CenteredStartLift);
            m_SmoothDisplacement = true;
            m_Smoothvalue = CameraK.CenteredSmoothValue;
        }

        if (!_hasPrevPawn)
        {
            m_prevStartPos = start;
            m_prevTargetZ = start.Z;
        }

        if (m_fixedTarget)
        {
            start = CameraMath.Approach(m_prevStartPos, m_fixedTargetPos, m_fixedTargetSpeed * dt);
        }
        else if (m_SmoothDisplacement && m_Smoothvalue > 0f)
        {
            float step = (Pawn.Velocity.Size() + m_Smoothvalue) * dt;
            if ((start - m_prevStartPos).Size() <= step)
            {
                if (!m_SmoothIsAlwaysActive)
                {
                    m_SmoothDisplacement = false;
                }
            }
            else
            {
                start = CameraMath.Approach(m_prevStartPos, start, step);
            }
        }

        m_prevStartPos = start;

        // 3. Offset in the pawn's pitch and yaw.
        FVector offset = CameraMath.Rotate(p.offset[0], new FRotator(Pawn.Rotation.Pitch, Pawn.Rotation.Yaw, 0));
        m_prevOffset = CameraMath.Approach(m_prevOffset, offset, p.offsetSpeed * dt);

        // 4. Twig.
        FVector twig = default;
        if (p.useTwig != 0)
        {
            float k = pitch * CameraK.TwigScale;
            twig = CameraMath.Rotate(new FVector(-k * p.twigX, 0f, k * p.twigZ), new FRotator(0, PC.Rotation.Yaw, 0));
        }

        // 5. Close-up lift.
        float closeup = 0f;
        if (m_closeupDist > 0f)
        {
            m_minCleanDist = m_absoluteMinDist + m_closeupDist;
            if (p.useCylColl != 0 && m_prevDistance < m_minCleanDist)
            {
                // The lift is smoothed with the last frame's raw lift, and the raw one is kept.
                float adjust = (m_minCleanDist - m_prevDistance) / m_closeupDist * m_closeupHeight;
                closeup = m_closeupDamping * adjust + (1f - m_closeupDamping) * m_prevCloseupAdjust;
                m_prevCloseupAdjust = adjust;
            }
            else
            {
                m_prevCloseupAdjust = 0f;
            }
        }

        // 6. Target, swept from the start.
        FVector target = start + m_prevOffset + twig + new FVector(0f, 0f, closeup);
        if (p.useCollTarget != 0 && !m_fixedTarget && Sweep(Pawn, start, target, box, CameraK.TargetTraceFlags, out FCheckResult targetHit))
        {
            target = targetHit.Location;
        }

        // 7. Target height.
        float vz = Pawn.Velocity.Z + (Pawn.Base is Mover mover ? mover.Velocity.Z : 0f);
        float vzAbs = SvmMath.Abs(vz);
        bool boost = (m_camMode is ECamMode.ECM_SecondPerson or ECamMode.ECM_SecondPersonCr or ECamMode.ECM_SplitJumpFP) && vzAbs > 0f;
        float zSpeed = vzAbs + (boost ? CameraK.VerticalBoostSpeed : p.targetZSpeed);
        m_prevTargetZ = CameraMath.Approach(m_prevTargetZ, target.Z, zSpeed * dt);
        m_prevTargetZ = SvmMath.Clamp(m_prevTargetZ, target.Z - m_targetZMaxDist, target.Z + m_targetZMaxDist);
        target.Z = m_prevTargetZ;

        // 8. Ideal distance.
        float dist0 = p.Distance[0];
        float pawnMove = _hasPrevPawn ? (Pawn.Location - _prevPawnLocation) | forward : 0f;
        float idealGap = dist0 - m_prevIdealDistance;
        if (SvmMath.Abs(idealGap) > p.interSpeed * dt)
        {
            // Shrinks at interSpeed; grows only as the pawn moves away along the view, at most interSpeed.
            m_prevIdealDistance = idealGap < 0f
                ? m_prevIdealDistance - p.interSpeed * dt
                : m_prevIdealDistance + SvmMath.Min(SvmMath.Max(pawnMove, 0f), p.interSpeed * dt);
        }
        else
        {
            m_prevIdealDistance = dist0;
        }

        // 9. Pitch curve.
        float distance = m_prevIdealDistance;
        if (p.usePitchCurve != 0 && pitch > 0 && m_maxPitch > 0)
        {
            float x = 1f - SvmMath.Clamp(pitch / (float)m_maxPitch, 0f, 1f);
            distance *= CameraMath.Bias(x, m_pitchCurveBias);
        }

        // 10. Rate limit from the last distance.
        distance = CameraMath.Approach(m_prevDistance, distance, p.interSpeed * dt);

        // 11 and 12. Camera spot and collision.
        FVector camera = target - forward * distance;
        ETraceFlags flags = CameraK.CameraTraceFlags | (p.collCBInterSpeed > 0f ? CameraK.CameraBlockingFlag : ETraceFlags.None);
        if (p.useColl != 0)
        {
            // The sweep runs from the target to the camera spot, both without the close-up lift.
            FVector lift = closeup > 0f ? new FVector(0f, 0f, closeup) : default;
            FVector sweepStart = target - lift;
            // With a fixed target, a hit that starts inside a blocker (Time 0) is ignored.
            if (Sweep(Pawn, sweepStart, camera - lift, box, flags, out FCheckResult hit) && (!m_fixedTarget || hit.Time != 0f))
            {
                // The hit distance, reached from the last frame's distance (m_prevDistance) at the collision speed. From the
                // step 10 distance instead, which already moved out by interSpeed dt, a camera at a wall never came in
                // past the final 12 uu clearance and stood 8 uu further out than the original's. collCBInterSpeed is for
                // camera-only blockers (bBlockCamera without bBlockActors or bBlockPlayers, the level's geometry among
                // them); anything else uses collInterSpeed.
                float hitDistance = (hit.Location - sweepStart).Size();
                bool cameraOnly = hit.Actor is { bBlockCamera: true, bBlockActors: false, bBlockPlayers: false };
                float speed = cameraOnly ? p.collCBInterSpeed : p.collInterSpeed;
                distance = CameraMath.Approach(m_prevDistance, hitDistance, speed * dt);
            }
        }

        distance = SvmMath.Max(distance, m_absoluteMinDist);
        camera = target - forward * distance;

        // 13. Final clearance.
        if (m_camMode is ECamMode.ECM_SecondPerson or ECamMode.ECM_SecondPersonCr)
        {
            var small = new FVector(CameraK.SmallBox, CameraK.SmallBox, CameraK.SmallBox);
            if (Sweep(Pawn, target, camera, small, flags, out FCheckResult h1))
            {
                camera = h1.Location;
            }

            var clear = new FVector(CameraK.ClearanceBox, CameraK.ClearanceBox, CameraK.ClearanceBox);
            if (Sweep(Pawn, target, camera, clear, flags, out FCheckResult h2))
            {
                camera = h2.Location;
            }

            distance = (camera - target).Size();
        }
        else
        {
            var clear = new FVector(CameraK.ClearanceBox, CameraK.ClearanceBox, CameraK.ClearanceBox);
            if (Sweep(Pawn, target, camera, clear, flags, out FCheckResult h1))
            {
                distance = SvmMath.Max((h1.Location - target).Size(), m_absoluteMinDist);
                camera = target - forward * distance;
                if (Sweep(Pawn, target, camera, clear, flags, out FCheckResult h2))
                {
                    // The second hit is held at m_absoluteMinDist too: with a wall right behind the pawn the camera stays
                    // 30 uu out, inside the pawn, as the original's does.
                    distance = SvmMath.Max((h2.Location - target).Size(), m_absoluteMinDist);
                    camera = target - forward * distance;
                }
            }
        }

        // 14. Keep the distance, move the controller to the camera, effects on the view.
        m_prevDistance = distance;
        _prevPawnLocation = Pawn.Location;
        _hasPrevPawn = true;
        PC.Location = camera;
        CameraLocation = camera;
        CameraRotation = UpdateView(PC.Rotation);

        // 15. Mode 33 head room: push the pitch up while the space above the camera is blocked.
        if (m_camMode == ECamMode.ECM_NoTargetCol)
        {
            bool blocked = pitch < CameraK.NoTargetColPitchLimit &&
                Sweep(Pawn, camera, camera + new FVector(0f, 0f, CameraK.NoTargetColProbe), box, flags, out _);
            fPitchCorrectionSpeed = blocked
                ? SvmMath.Min(CameraK.PitchCorrectionMax, fPitchCorrectionSpeed + CameraK.PitchCorrectionRate * dt)
                : SvmMath.Max(0f, fPitchCorrectionSpeed - CameraK.PitchCorrectionRate * dt);
            if (fPitchCorrectionSpeed > 0f)
            {
                FRotator r = PC.Rotation;
                PC.Rotation = new FRotator(PushPitch(r.Pitch, fPitchCorrectionSpeed, dt), r.Yaw, r.Roll);
            }
        }
    }

    /// <summary>
    /// The first-person view (rule 5). Without a bone: the controller's rotation (roll 0), the view target's location
    /// plus WalkBob and EyeHeight, then UpdateView. On a bone (when the level can say where it is): the bone's location,
    /// a view frame whose X, Y and Z axes are the bone's -Y, -Z and X; the spy's view 15 uu higher in world Z, the
    /// merc's controller turned to the view rotation; m_vRelativeLocation added along the view axes; no UpdateView.
    /// Then ShakeOffset; with smoothing the yaw follows from the last view's yaw at m_Smoothvalue per second, and the
    /// smoothing ends once caught up unless m_SmoothIsAlwaysActive. The view is kept in m_PrevRotation (and vFixedPos
    /// outside debug camera mode 1).
    /// </summary>
    [UcNative("ASPlayerCam::FirstPersonCalcView", Exe = 0x10a98390, Editor = 0x111f5230)]
    [PortOf("nat:SBase.SPlayerCam.UpdateView", Spec = "spec/native/camera.md#5-first-person-view-editor-0x111f5230")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a98390: bone frame (-Y, -Z, X) through OrthoRotation 0x10950770, spy +15 world Z, merc controller rotation set, offset along the view axes, no UpdateView on a bone, yaw smoothing 0x10a98720")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a98390 (REA): the view rotation's roll is set to 0 before UpdateView, which then applies the effects to the controller only, so the first-person view never carries a roll")]
    [Provenance(ProvenanceKind.Estimated, "The bone placement comes from the ICameraBones level service when the client installs one (its bone axes taken as the original mesh's)")]
    public void FirstPersonView(SPlayerController PC, Actor ViewTarget, float DeltaTime, out FVector CameraLocation, out FRotator CameraRotation)
    {
        m_EPC ??= PC;
        FRotator rotation = new(PC.Rotation.Pitch, PC.Rotation.Yaw, 0);
        FVector location = ViewTarget.Location;
        var pawn = ViewTarget as Pawn;
        if (m_bAttachToBone && pawn is not null && CameraBones.TryGet(pawn, m_parentBone, out FVector boneLocation, out FRotator boneRotation))
        {
            boneRotation.GetAxes(out FVector bx, out FVector by, out FVector bz);
            rotation = CameraMath.OrthoRotation(-by, -bz, bx);
            location = boneLocation;
            if (PC.SPawnType == PawnType.SPAWNOFFENSE)
            {
                location.Z += CameraK.SpyBoneLift;
            }
            else
            {
                PC.Rotation = rotation;
            }

            location += CameraMath.Rotate(m_vRelativeLocation, rotation);
        }
        else
        {
            if (pawn is not null)
            {
                location += pawn.WalkBob + new FVector(0f, 0f, pawn.EyeHeight);
            }

            // The original's UpdateView turns the controller, not this view, whose roll stays 0: in first person the
            // hit and shake rolls never show. The port keeps the tilt on the view pitch (open question 2).
            FRotator effects = UpdateView(rotation);
            rotation = new FRotator(effects.Pitch, effects.Yaw, 0);
        }

        location += PC.ShakeOffset;
        if (m_SmoothDisplacement && m_Smoothvalue > 0f)
        {
            if (!m_SmoothIsAlwaysActive && ((m_PrevRotation.Yaw ^ rotation.Yaw) & 0xffff) == 0)
            {
                m_SmoothDisplacement = false;
            }

            rotation = new FRotator(rotation.Pitch, CameraMath.ApproachYaw16(m_PrevRotation.Yaw, rotation.Yaw, m_Smoothvalue * DeltaTime), rotation.Roll);
        }

        m_PrevRotation = rotation;
        if (DebugCamMode != DCMFixed)
        {
            vFixedPos = location;
        }

        CameraLocation = location;
        CameraRotation = rotation;
    }

    /// <summary>A camera sweep: start, end, box extent and trace flags; true on a hit.</summary>
    internal delegate bool SweepFn(FVector start, FVector end, FVector extent, ETraceFlags flags, out FCheckResult hit);

    /// <summary>Test seam: when set, the solver's sweeps go here instead of the level's collision world.</summary>
    internal SweepFn? SweepOverride;

    /// <summary>The corrected mode 33 pitch: ftol(pitch + speed x dt), truncated after the add.</summary>
    internal static int PushPitch(int pitch, float speed, float dt) => SvmMath.Trunc(pitch + speed * dt);

    /// <summary>A box sweep through the level's collision world; false without one.</summary>
    private bool Sweep(Actor source, FVector start, FVector end, FVector extent, ETraceFlags flags, out FCheckResult hit)
    {
        if (SweepOverride is { } over)
        {
            return over(start, end, extent, flags, out hit);
        }

        if (XLevel.PhysicsDriver is PhysicsDriver driver && start != end)
        {
            return driver.Collision.LineCheck(out hit, source, end, start, extent, flags);
        }

        hit = FCheckResult.NoHit(end);
        return false;
    }

    // ---- states -----------------------------------------------------------------------------------------------

    /// <summary>The auto state: the following camera solves the behind view; ResetRotation can start a reset.</summary>
    [Uc("SBase.SPlayerCam.s_Following")]
    public class s_Following : UcActorState, IUpdateRotation, IResetRotation, IBehindView
    {
        public static readonly s_Following Instance = new(UcMeta.s_Following, null);

        protected s_Following(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerCam.s_Following.ResetRotation", Sha1 = "37989d1c19d865ae46259388949d643e901098fb")]
        [PortOf("uc:SBase.SPlayerCam:s_Following.ResetRotation", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
        [Provenance(ProvenanceKind.Decoded, "camera.md rule 8.7: dot product below 0.92 starts a reset")]
        public virtual void ResetRotation(SPlayerCam self, FRotator resetRot, float Rate)
        {
            if (self.m_EPC is not { } pc)
            {
                return;
            }

            FVector current = pc.Rotation.Vector();
            FVector wanted = resetRot.Vector();
            if (self.m_resetEnable && (current | wanted) < ResetDotLimit)
            {
                SPlayerCam defaults = self.ClassDefaults;
                self.m_resettingRotation = resetRot;
                self.m_resetPitchSpeed = defaults.m_resetPitchSpeed * Rate;
                self.m_resetYawSpeed = defaults.m_resetYawSpeed * Rate;
                self.GotoState(nameof(s_Reseting));
            }
        }

        [Uc("SBase.SPlayerCam.s_Following.UpdateRotation", Sha1 = "fdc1938b4c2dfe7cb28e9bd9896e2c9761215f77")]
        [PortOf("uc:SBase.SPlayerCam:s_Following.UpdateRotation", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
        public virtual void UpdateRotation(SPlayerCam self, Actor? ViewActor) => self.FollowingRotation(ViewActor);

        public bool BehindView(SPlayerCam self, SPlayerController PC, SBasePawn Pawn, float DeltaTime, out FVector CameraLocation,
            out FRotator CameraRotation)
        {
            self.SolveFollowing(PC, Pawn, DeltaTime, out CameraLocation, out CameraRotation);
            return true;
        }

        [Provenance(ProvenanceKind.Decoded, "s_Following.ResetRotation: 0.92")]
        private const float ResetDotLimit = 0.92f;
    }

    /// <summary>Turning back to a set rotation, then following again.</summary>
    [Uc("SBase.SPlayerCam.s_Reseting")]
    public class s_Reseting : s_Following
    {
        public static new readonly s_Reseting Instance = new(UcMeta.s_Reseting, s_Following.Instance);

        protected s_Reseting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerCam.s_Reseting.UpdateRotation", Sha1 = "56a169fe20fd378a4f921796d487a5f572bc4c57")]
        [PortOf("uc:SBase.SPlayerCam:s_Reseting.UpdateRotation", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
        public override void UpdateRotation(SPlayerCam self, Actor? ViewActor)
        {
            if (self.ResettingRotation())
            {
                self.GotoState(nameof(s_Following));
            }
        }
    }

    /// <summary>A fixed camera: no rotation update and no behind view.</summary>
    [Uc("SBase.SPlayerCam.s_Fixed")]
    public class s_Fixed : UcActorState, IUpdateRotation
    {
        public static readonly s_Fixed Instance = new(UcMeta.s_Fixed, null);

        protected s_Fixed(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerCam.s_Fixed.UpdateRotation", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerCam:s_Fixed.UpdateRotation", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
        public void UpdateRotation(SPlayerCam self, Actor? ViewActor)
        {
        }
    }

    /// <summary>The intro cinematic: no rotation update, no first-person view.</summary>
    [Uc("SBase.SPlayerCam.s_IntroCinematic")]
    public class s_IntroCinematic : UcActorState, IUpdateRotation
    {
        public static readonly s_IntroCinematic Instance = new(UcMeta.s_IntroCinematic, null);

        protected s_IntroCinematic(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerCam.s_IntroCinematic.UpdateRotation", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerCam:s_IntroCinematic.UpdateRotation", Spec = "spec/native/camera.md#8-script-side-splayercam-and-splayercontroller")]
        public void UpdateRotation(SPlayerCam self, Actor? ViewActor)
        {
        }
    }
}
