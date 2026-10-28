using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Animation;

/// <summary>
/// The walk blender, ASBasePawn::UpdateMovementAnimation (spec/native/ANSWERS-anim.md A6), run in the pawn's post-tick
/// after physics: it keeps channels 1 to 15 in step with the movement. Moving on the ground it blends the walk set
/// (ForwardAnim on 5 and 6, RightAnim or LeftAnim on 7 and 8, RunAnim on 4) by the speed in the pawn's frame, keeps
/// the outgoing clips on 9 to 13 while a set changes, runs channel 5 at the speed's rate and holds every other walk
/// channel at channel 5's frame. Standing it fades them and turns in place on 2 and 3; it also runs the aim, shimmy,
/// grabbed and climb blends of the traversal and the blend of a channel into channel 0.
/// </summary>
public static class PawnMovementAnimation
{
    private const int RunChannel = 4;
    private const int ForwardChannel = 5;
    private const int BackwardChannel = 6;
    private const int SideChannel = 7;
    private const int OtherSideChannel = 8;

    /// <summary>The post-tick update of <paramref name="pawn"/> for a tick of <paramref name="dt"/> seconds.</summary>
    [UcNative("ASBasePawn::UpdateMovementAnimation", Editor = 0x112008a0)]
    [PortOf("eng:ASBasePawn::UpdateMovementAnimation", Spec = "spec/native/ANSWERS-anim.md#a6-walk-blender-asbasepawnupdatemovementanimation-0x112008a0-complete")]
    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0 and its blends 0x111ffa90, 0x111ffe80, 0x112004b0, 0x11200140")]
    [Provenance(ProvenanceKind.Measured, "goldens: walk, run and crouch starts, turn in place, pipe climb channels, roll blend (tests/OpenScct.DiffTest/Anim)")]
    public static void Update(SBasePawn pawn, float dt)
    {
        PawnAnimation a = pawn.PawnAnim;
        a.Owner ??= pawn;
        if (dt <= 0f)
        {
            return;
        }

        TurnHead(pawn, dt);
        FVector v = pawn.Velocity;
        if (pawn.Base is { } floor)
        {
            v -= floor.Velocity;
        }

        if (pawn.Health < 1 || pawn.bLyingOnGround)
        {
            for (int ch = 1; ch <= SBasePawn.LASTCHANNEL; ch++)
            {
                a.StepBlendAlpha(ch, 0f, dt * AnimationK.DeadFadePerSecond);
                a.SetChannelRateScale(ch, 0f);
            }

            pawn.fDistantTurnRate = 0f;
        }
        else if (pawn.fBlendTimeWithC0 > 0f)
        {
            float step = dt / pawn.fBlendTimeWithC0;
            a.StepBlendAlpha(pawn.ChannelForBlendWithC0, 1f, step);
            a.ZeroMovementAlpha(4, 14, step);
            pawn.fDistantTurnRate = 0f;
        }
        else if (Blend(pawn, a, v, dt))
        {
            pawn.fDistantTurnRate = 0f;
        }

        // The rest of the post-tick (editor 0x11205ef0): the yaw the next turn-in-place test starts from. The pose
        // build's tween cache follows here for a drawn pawn, else after the next advance (A4).
        pawn.OldRotYaw = pawn.Rotation.Yaw;
        bool drawn = IsDrawn(pawn);
        a.TweenCacheAfterAdvance = !drawn;
        if (drawn)
        {
            a.UpdateTweenCache();
        }
    }

    /// <summary>
    /// The simulation's part of the script event SBasePawn.TurnHead, which the walk blender runs first: the packed
    /// view and desired rotations the aim blends read (iYawPitchFromView, iDesiredYawPitch), the IK type history, the
    /// weapon aim (<see cref="AimWeapon"/>) and the arm cases (<see cref="ArmAim.Control"/>, stepped by the tick's
    /// <paramref name="dt"/>). The view draws the bone turns they leave on the mesh instance.
    /// </summary>
    [Uc("SBase.SBasePawn.TurnHead", Sha1 = "fa2992fa34ca759dc05b2b534fbda9b5c59bef60")]
    [PortOf("uc:SBase.SBasePawn:TurnHead", Spec = "spec/native/ANSWERS-anim.md#a6-walk-blender-asbasepawnupdatemovementanimation-0x112008a0-complete")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.TurnHead: PackW of the controller's rotation (spy not an autonomous proxy, or the merc), IK_Type_Previous on an IK change")]
    public static void TurnHead(SBasePawn pawn, float dt = 0f)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.Controller is { } c &&
            ((pawn.SPawnType != PawnType.SPAWNDEFENSE && c.RemoteRole != ENetRole.ROLE_AutonomousProxy) || pawn.SPawnType == PawnType.SPAWNDEFENSE))
        {
            pawn.iYawPitchFromView = PackW(c.Rotation.Yaw, c.Rotation.Pitch);
            if (c is SPlayerController pc)
            {
                pawn.iDesiredYawPitch = PackW(pc.MyDesiredRotation.Yaw, pc.MyDesiredRotation.Pitch);
            }
        }

        if (pawn.IK_Type_LastTick != pawn.IK_Type)
        {
            pawn.IK_Type_Previous = pawn.IK_Type_LastTick;
        }

        pawn.IK_Type_LastTick = pawn.IK_Type;
        AimWeapon(pawn);
        ArmAim.Control(pawn, dt);
    }

    /// <summary>
    /// The IKT_WEAPONLOADED case of TurnHead: with a weapon loaded and no weapon transition, AimAt(AAFULL) toward the
    /// merc's desired rotation (iDesiredYawPitch) or the spy's view (iYawPitchFromView), keeping the pawn's roll, from
    /// the pawn's facing, within 90 degrees each way. The arm cases are <see cref="ArmAim"/>.
    /// </summary>
    [Uc("SBase.SBasePawn.TurnHead", Sha1 = "fa2992fa34ca759dc05b2b534fbda9b5c59bef60")]
    [PortOf("uc:SBase.SBasePawn:TurnHead", Spec = "spec/native/aim-ik.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.TurnHead case IKT_WEAPONLOADED: AimAt(AAFULL, vector(Orientation), vector(Rotation), -90, 90, -90, 90)")]
    public static void AimWeapon(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.IK_Type != IK_Type_.IKT_WEAPONLOADED || pawn.WeaponLoaded == 0 || pawn.bWeaponTransit)
        {
            return;
        }

        int packed = pawn.SPawnType == PawnType.SPAWNDEFENSE ? pawn.iDesiredYawPitch : pawn.iYawPitchFromView;
        var orientation = new FRotator(packed >> 16, packed & 0xffff, pawn.Rotation.Roll);
        pawn.PawnAnim.Aim.AimAt(MeshAimIK.AimType.AAFULL, orientation.Vector(), pawn.Rotation.Vector(), -90f, 90f, -90f, 90f);
    }

    /// <summary>
    /// Whether the pawn's mesh is drawn on this machine (its pose built after the tick): not on a dedicated server,
    /// and not the local merc seen from his eyes.
    /// </summary>
    [Provenance(ProvenanceKind.Measured, "goldens: the local merc's tween cache moves after the advance (merc-jump frame 69), the spy's after the tick (spy-jump-in-place frame 79)")]
    public static bool IsDrawn(SBasePawn pawn)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.Level.NetMode == ENetMode.NM_DedicatedServer)
        {
            return false;
        }

        return !(pawn is SPAWNDEFENSE && pawn.Controller is PlayerController { bBehindView: false } pc &&
                 ReferenceEquals(pawn.Level.GetLocalPlayerController(), pc));
    }

    /// <summary>Core.Object.PackW: the high word over the low word.</summary>
    private static int PackW(int low, int high) => (high << 16) | (low & 0xffff);

    /// <summary>The main blend; true when the branch taken ends with fDistantTurnRate cleared by the common exit.</summary>
    private static bool Blend(SBasePawn pawn, PawnAnimation a, FVector v, float dt)
    {
        float c5 = a.GetAnimFrame(ForwardChannel);
        if (a.GetSequence(0).IsNone)
        {
            a.PlayAnim(0, Clip(pawn, pawn.AWaitSt0), 1f, AnimationK.InitTween, loop: true);
        }

        if (!pawn.bInitializeAnimation)
        {
            pawn.bInitializeAnimation = true;
            InitializeChannels(pawn, a);
        }

        float step = dt / pawn.BlendChangeTime;
        if (pawn.Role == ENetRole.ROLE_SimulatedProxy && pawn.Physics != pawn.OldPhysics)
        {
            RelockProxy(pawn);
        }

        EPhysics phys = pawn.Physics;
        if (pawn.bNoRunAnimation && phys != EPhysics.PHYS_Walking && !v.IsZero())
        {
            a.StepBlendAlpha(1, 0f, step);
            if (pawn.Velocity.Z < 0f)
            {
                a.StepBlendAlpha(1, 0f, AnimationK.NoRunStep);
                pawn.bNoRunAnimation = false;
            }
        }

        if (phys == EPhysics.PHYS_Vandamme && pawn.IK_Type == IK_Type_.IKT_JCVD)
        {
            float yaw = Clamp1(Signed16(pawn.iYawPitchFromView - pawn.Rotation.Yaw) * AnimationK.AimYawScale);
            float pitch = Clamp1(Signed16((pawn.iYawPitchFromView >> 16) + AnimationK.AimPitchOffset) * AnimationK.AimPitchScale);
            AimBlend(pawn, a, dt, yaw, pitch);
            if (pawn.bWeaponTransit)
            {
                VandammeWeapon(pawn, a, dt);
            }

            return true;
        }

        if (pawn.PipHFire == PipHFire_.PipH2 && pawn.IK_Type == IK_Type_.IKT_PIPH)
        {
            float yaw = Clamp1(Signed16((pawn.iYawPitchFromView & 0xffff) - pawn.Rotation.Yaw) * AnimationK.AimYawScale);
            float pitch = Clamp1(Signed16(pawn.iYawPitchFromView >> 16) * AnimationK.AimPitchScale);
            AimBlend(pawn, a, dt, yaw, pitch);
            return true;
        }

        if (pawn.PipHFire == PipHFire_.PipH4 && pawn.IK_Type == IK_Type_.IKT_PIPH)
        {
            float yaw = Clamp1(Signed16((pawn.iYawPitchFromView & 0xffff) - pawn.Rotation.Yaw + 0x8000) * AnimationK.AimYawScalePipH2);
            float pitch = Clamp1(Signed16(pawn.iYawPitchFromView >> 16) * AnimationK.AimPitchScalePipH2);
            AimBlend(pawn, a, dt, yaw, pitch);
            return true;
        }

        if (pawn.BTW != BTW_.NOBTW || (pawn.bBeginSpecial12Anim && phys == EPhysics.PHYS_Ledge))
        {
            ShimmyBlend(pawn, a, dt);
            return true;
        }

        if (phys == EPhysics.PHYS_FollowParent)
        {
            FollowParentBlend(pawn, a, dt);
            return true;
        }

        if ((phys is EPhysics.PHYS_OnPipe or EPhysics.PHYS_OnLadder) && pawn.bBeginSpecial12Anim)
        {
            ClimbStep(pawn, a, dt);
            return true;
        }

        if (phys is EPhysics.PHYS_RootMotion or EPhysics.PHYS_Linear or EPhysics.PHYS_OnPipe or EPhysics.PHYS_OnLadder or
            EPhysics.PHYS_Vandamme)
        {
            a.ZeroMovementAlpha(2, 14, AnimationK.AtOnce);
            return true;
        }

        float now = pawn.Level.TimeSeconds;
        if (now - AnimationK.FeetLedgeWindow < pawn.fTimeOfFeetLedge)
        {
            a.StepBlendAlpha(RunChannel, 1f, 1f);
            for (int ch = 5; ch < 14; ch++)
            {
                a.StepBlendAlpha(ch, 0f, AnimationK.AtOnceFeetLedge);
            }

            c5 = pawn.bIsLeftPart ? 0f : AnimationK.FeetLedgeRightFrame;
            a.SetAnimFrame(ForwardChannel, c5, unitFlag: false);
            pawn.fTimeOfFeetLedge = pawn.Level.TimeSeconds - AnimationK.FeetLedgeReset;
        }

        pawn.bBeginSpecial12Anim = false;
        phys = pawn.Physics;
        if (phys == EPhysics.PHYS_Falling || now < pawn.MovementBlendStartTime)
        {
            a.ZeroMovementAlpha(2, 14, step);
            if (pawn.Physics == EPhysics.PHYS_Falling && pawn.bIsRolling)
            {
                float roll = a.GetAnimFrame(SBasePawn.DBLJUMPCHANNEL);
                if (roll > AnimationK.RollFadeFrame)
                {
                    a.ZeroMovementAlpha(SBasePawn.DBLJUMPCHANNEL, SBasePawn.DBLJUMPCHANNEL + 1, step);
                    if (a.GetBlendAlpha(SBasePawn.DBLJUMPCHANNEL) == 0f)
                    {
                        pawn.bIsRolling = false;
                    }

                    if (roll >= 1f)
                    {
                        a.ZeroMovementAlpha(SBasePawn.DBLJUMPCHANNEL, SBasePawn.DBLJUMPCHANNEL + 1, AnimationK.AtOnceRoll);
                        pawn.bIsRolling = false;
                    }
                }
            }

            pawn.OldRotYaw = pawn.Rotation.Yaw;
            pawn.fDistantTurnRate = 0f;
            return false;
        }

        if (((v.Y * v.Y) + (v.X * v.X) < AnimationK.StillSpeedSquared &&
             (phys != EPhysics.PHYS_OnVerticalPlane || SvmMath.Abs(v.Z) < AnimationK.StillVerticalSpeed)) ||
            phys == EPhysics.PHYS_Ledge)
        {
            Standing(pawn, a, dt, step);
            return false;
        }

        a.ZeroMovementAlpha(2, 4, step);
        pawn.OldRotYaw = pawn.Rotation.Yaw;
        pawn.fDistantTurnRate = 0f;
        Moving(pawn, a, dt, step, c5);
        return false;
    }

    /// <summary>
    /// The first call: every walk channel up to 24 exists, 2 to 13 hold AWaitSt0 loops tweened over 0.1 s (the
    /// backward and side ones reversed), notifies off on 2 to 13 but channel 5 and alpha 0 on all of them.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UpdateMovementAnimation 0x112008a0: the init order 2, 3b, 5, 10, 6b, 11b, 8, 13, 7, 12, 4, 9 (7 and 12 backward for the spy)")]
    private static void InitializeChannels(SBasePawn pawn, PawnAnimation a)
    {
        PawnClip wait = Clip(pawn, pawn.AWaitSt0);
        bool merc = pawn.SPawnType != PawnType.SPAWNOFFENSE;
        a.PlayAnim(2, wait, 1f, AnimationK.InitTween, loop: true);
        a.PlayAnim(3, wait, 1f, AnimationK.InitTween, loop: true, backward: true);
        a.PlayAnim(5, wait, 1f, AnimationK.InitTween, loop: true);
        a.PlayAnim(10, wait, 1f, AnimationK.InitTween, loop: true);
        a.PlayAnim(6, wait, 1f, AnimationK.InitTween, loop: true, backward: true);
        a.PlayAnim(11, wait, 1f, AnimationK.InitTween, loop: true, backward: true);
        a.PlayAnim(8, wait, 1f, AnimationK.InitTween, loop: true);
        a.PlayAnim(13, wait, 1f, AnimationK.InitTween, loop: true);
        a.PlayAnim(7, wait, 1f, AnimationK.InitTween, loop: true, backward: !merc);
        a.PlayAnim(12, wait, 1f, AnimationK.InitTween, loop: true, backward: !merc);
        a.PlayAnim(4, wait, 1f, AnimationK.InitTween, loop: true);
        a.PlayAnim(9, wait, 1f, AnimationK.InitTween, loop: true);
        for (int ch = 2; ch < 14; ch++)
        {
            a.EnableChannelNotify(ch, false);
            a.SetBlendAlpha(ch, 0f);
        }

        a.EnableChannelNotify(ForwardChannel, true);
    }

    /// <summary>A simulated proxy relocks its root when its replicated Physics enters or leaves root motion.</summary>
    private static void RelockProxy(SBasePawn pawn)
    {
        if (pawn is not OpenScct.Physics.Movement.ISBasePawnPhysics sb || sb.RootMotionMesh is not { } mesh)
        {
            return;
        }

        if (pawn.Physics == EPhysics.PHYS_RootMotion)
        {
            mesh.LockRootMotion(pawn.iLockRootMotionType);
        }
        else if (pawn.OldPhysics == EPhysics.PHYS_RootMotion)
        {
            mesh.LockRootMotion(0);
        }
    }

    /// <summary>Standing still: the walk channels fade, and on the ground the pawn turns in place on 2 and 3.</summary>
    private static void Standing(SBasePawn pawn, PawnAnimation a, float dt, float step)
    {
        if (pawn.bNoRunAnimation)
        {
            float overlay = a.GetBlendAlpha(1);
            a.StepBlendAlpha(RunChannel, overlay, AnimationK.NoRunStep);
            a.StepBlendAlpha(1, 0f, AnimationK.NoRunStep);
            pawn.bNoRunAnimation = false;
        }

        a.ZeroMovementAlpha(4, 14, step);
        if (pawn.Physics != EPhysics.PHYS_Walking)
        {
            a.ZeroMovementAlpha(2, 4, step);
            pawn.OldRotYaw = pawn.Rotation.Yaw;
            pawn.fDistantTurnRate = 0f;
            return;
        }

        float turnStep = dt * AnimationK.TurnStepPerSecond;
        float alpha = pawn.bIsCrouched ? AnimationK.TurnAlphaCrouched : AnimationK.TurnAlphaStanding;
        float rate = Signed16(pawn.Rotation.Yaw - (int)pawn.OldRotYaw) / dt;
        bool local = pawn.Controller is not null && ReferenceEquals(pawn.Level.GetLocalPlayerController(), pawn.Controller);
        if (pawn.fDistantTurnRate == 0f || (pawn.Role > ENetRole.ROLE_SimulatedProxy && dt >= AnimationK.TurnSmoothAuthority) ||
            dt >= AnimationK.TurnSmoothProxy || local)
        {
            pawn.fDistantTurnRate = rate;
        }
        else if (pawn.Role < ENetRole.ROLE_AutonomousProxy)
        {
            pawn.fDistantTurnRate = ((rate * dt) + ((AnimationK.TurnSmoothProxy - dt) * pawn.fDistantTurnRate)) * AnimationK.TurnSmoothProxyInv;
            rate = pawn.fDistantTurnRate;
        }
        else
        {
            pawn.fDistantTurnRate = ((rate * dt) + ((AnimationK.TurnSmoothAuthority - dt) * pawn.fDistantTurnRate)) * AnimationK.TurnSmoothAuthorityInv;
            rate = pawn.fDistantTurnRate;
        }

        float scale = SvmMath.Abs(rate) * AnimationK.TurnRateScale;
        if (-rate <= AnimationK.TurnThreshold)
        {
            a.ZeroMovementAlpha(2, 3, turnStep);
        }
        else
        {
            SetSequence(pawn, a, 2, pawn.TurnAnim);
            a.StepBlendAlpha(2, alpha, turnStep);
            a.SetChannelRateScale(2, scale);
        }

        if (!(rate > AnimationK.TurnThreshold))
        {
            a.ZeroMovementAlpha(3, 4, turnStep);
            pawn.OldRotYaw = pawn.Rotation.Yaw;
            return;
        }

        SetSequence(pawn, a, 3, pawn.TurnAnim);
        a.StepBlendAlpha(3, alpha, turnStep);
        a.SetChannelRateScale(3, scale);
        pawn.OldRotYaw = pawn.Rotation.Yaw;
    }

    /// <summary>Moving: the walk set blended by the velocity in the pawn's frame (A6 steps 12 to 14).</summary>
    private static void Moving(SBasePawn pawn, PawnAnimation a, float dt, float step, float c5)
    {
        EPhysics phys = pawn.Physics;
        FVector x = pawn.Rotation.Vector();
        if (phys is EPhysics.PHYS_Walking or EPhysics.PHYS_OnVerticalPlane or EPhysics.PHYS_FollowParent)
        {
            x.Z = 0f;
        }

        x = x.SafeNormal();
        FVector d = pawn.Velocity.SafeNormal();
        FVector y = x.Cross(new FVector(0f, 0f, 1f)).SafeNormal();
        float fu = (d.Y * x.Y) + (d.Z * x.Z) + (d.X * x.X);
        float su = (y.Y * d.Y) + (y.Z * d.Z) + (y.X * d.X);
        float fw = fu * fu;
        float sw = su * su;
        if (phys == EPhysics.PHYS_OnPipeH)
        {
            sw = 0f;
            su = 0f;
            fu = fu > 0f ? 1f : fu < 0f ? -1f : fu;
            if (pawn.fBlendCoeffWalkAnim < AnimationK.PipeHReverseCoeff)
            {
                fu = -fu;
            }

            fw = fu * fu;
        }

        if (fw > 0f)
        {
            fw = 1f;
            if (sw > AnimationK.StrafeBiasThreshold)
            {
                float bias = fu <= 0f ? pawn.BackwardStrafeBias : pawn.ForwardStrafeBias;
                sw = ((1f - sw) * bias) + sw;
            }
        }

        if (phys is not (EPhysics.PHYS_Walking or EPhysics.PHYS_FollowParent or EPhysics.PHYS_OnVerticalPlane or EPhysics.PHYS_Repositionnal))
        {
            MovingOther(pawn, a, step, c5, fu, su, fw, sw);
            return;
        }

        FVector vel = pawn.Velocity;
        float f = (x.X * vel.X) + (x.Y * vel.Y) + (x.Z * vel.Z);
        float s = (y.X * vel.X) + (y.Y * vel.Y) + (y.Z * vel.Z);
        if (phys == EPhysics.PHYS_OnVerticalPlane)
        {
            f = vel.Z;
        }

        if (pawn.bNoRunAnimation)
        {
            a.StepBlendAlpha(1, 1f, dt * AnimationK.NoRunStep);
        }

        float af = SvmMath.Abs(f);
        float aside = SvmMath.Abs(s);
        float r = (af + aside) / pawn.fBlendCoeffWalkAnim;
        float w = aside <= AnimationK.StrafeMinSpeed ? 0f : 1f / (SvmMath.Abs(f / s) + 1f);
        bool merc = pawn.SPawnType != PawnType.SPAWNOFFENSE;
        if (pawn.ForwardAnim != a.GetSequence(ForwardChannel))
        {
            a.CopyAnimation(ForwardChannel, 10);
            a.SetBlendAlpha(10, a.GetBlendAlpha(ForwardChannel));
            a.CopyAnimation(BackwardChannel, 11);
            a.SetBlendAlpha(11, a.GetBlendAlpha(BackwardChannel));
            SetSequence(pawn, a, ForwardChannel, pawn.ForwardAnim);
            SetSequence(pawn, a, BackwardChannel, pawn.ForwardAnim);
        }

        if (pawn.RightAnim != a.GetSequence(OtherSideChannel))
        {
            if (!merc)
            {
                a.CopyAnimation(SideChannel, 12);
                a.SetBlendAlpha(12, a.GetBlendAlpha(SideChannel));
                SetSequence(pawn, a, SideChannel, pawn.RightAnim);
            }

            a.CopyAnimation(OtherSideChannel, 13);
            a.SetBlendAlpha(13, a.GetBlendAlpha(OtherSideChannel));
            SetSequence(pawn, a, OtherSideChannel, pawn.RightAnim);
        }

        if (pawn.LeftAnim != a.GetSequence(SideChannel) && merc)
        {
            a.CopyAnimation(SideChannel, 12);
            a.SetBlendAlpha(12, a.GetBlendAlpha(SideChannel));
            SetSequence(pawn, a, SideChannel, pawn.LeftAnim);
        }

        if (pawn.RunAnim != a.GetSequence(RunChannel))
        {
            if (pawn.bNoRunAnimation)
            {
                a.SetBlendAlpha(RunChannel, a.GetBlendAlpha(1));
                a.SetBlendAlpha(1, 0f);
                pawn.bNoRunAnimation = false;
            }

            a.CopyAnimation(RunChannel, 9);
            a.SetBlendAlpha(9, a.GetBlendAlpha(RunChannel));
            SetSequence(pawn, a, RunChannel, pawn.RunAnim);
        }

        float scale = 1f;
        float range = pawn.fRunSpeedLimit - pawn.fWalkSpeedLimit;
        float wf = 1f;
        if (range > 0f && !pawn.bIsCrouched && r < AnimationK.SlowWalkRate && pawn.fBlendCoeffWalkAnim != AnimationK.ChargeCoeff)
        {
            scale = r / AnimationK.SlowWalkRate;
            r = AnimationK.SlowWalkRate;
            if (scale < 1f)
            {
                wf = 1f - w;
            }
        }

        int zeroChannel;
        if (f < 0f)
        {
            a.StepBlendAlpha(ForwardChannel, 0f, step);
            a.StepBlendAlpha(BackwardChannel, wf * scale, step);
            zeroChannel = RunChannel;
        }
        else if (scale >= 1f)
        {
            float q;
            if (SvmMath.Abs(w) >= AnimationK.RunCutSideWeight || pawn.RunAnim.IsNone)
            {
                q = 0f;
                a.StepBlendAlpha(RunChannel, 0f, step);
            }
            else
            {
                float span = pawn.fRunSpeedLimit - pawn.fWalkSpeedLimit;
                if (span >= 0f)
                {
                    q = (af - pawn.fWalkSpeedLimit) / span;
                    q = q >= 0f ? (q > 1f ? 1f : q) : 0f;
                }
                else
                {
                    q = (af - pawn.fRunSpeedLimit) / (pawn.fWalkSpeedLimit - pawn.fRunSpeedLimit);
                    q = q >= 0f ? 1f - (q > 1f ? 1f : q) : 1f;
                }

                wf = 1f - q;
                r = (af + aside) * ((wf / pawn.fBlendCoeffWalkAnim) + (q / pawn.fBlendCoeffRunAnim));
                if (q <= 0f)
                {
                    a.StepBlendAlpha(RunChannel, 0f, step);
                }
                else if (!pawn.bNoRunAnimation)
                {
                    a.StepBlendAlpha(RunChannel, 1f, step);
                }
                else
                {
                    a.StepBlendAlpha(RunChannel, 0f, AnimationK.NoRunStep);
                }
            }

            a.StepBlendAlpha(ForwardChannel, wf * scale, step);
            zeroChannel = BackwardChannel;
        }
        else
        {
            a.StepBlendAlpha(range >= 0f ? RunChannel : ForwardChannel, 0f, step);
            if (pawn.fRunSpeedLimit - pawn.fWalkSpeedLimit < 0f)
            {
                a.StepBlendAlpha(RunChannel, wf * scale, step);
            }
            else
            {
                a.StepBlendAlpha(ForwardChannel, wf * scale, step);
            }

            zeroChannel = BackwardChannel;
        }

        a.StepBlendAlpha(zeroChannel, 0f, step);
        if (s < 0f)
        {
            a.StepBlendAlpha(SideChannel, 0f, step);
            a.StepBlendAlpha(OtherSideChannel, scale * w, step);
        }
        else
        {
            a.StepBlendAlpha(SideChannel, scale * w, step);
            a.StepBlendAlpha(OtherSideChannel, 0f, step);
        }

        for (int ch = 9; ch < 14; ch++)
        {
            a.StepBlendAlpha(ch, 0f, step);
        }

        a.SetChannelRateScale(ForwardChannel, r);
        for (int ch = 6; ch < 14; ch++)
        {
            a.SetAnimFrame(ch, c5, unitFlag: false);
        }

        a.SetAnimFrame(RunChannel, c5, unitFlag: false);
        if (pawn.bNoRunAnimation)
        {
            a.SetAnimFrame(1, c5, unitFlag: false);
        }
    }

    /// <summary>Moving in another mode (the hand-over-hand pipe): unit direction weights (A6 step 13).</summary>
    private static void MovingOther(SBasePawn pawn, PawnAnimation a, float step, float c5, float fu, float su, float fw, float sw)
    {
        float fps = a.ClipRate(ForwardChannel);
        if (pawn.ForwardAnim != a.GetSequence(ForwardChannel))
        {
            if (a.GetBlendAlpha(10) < a.GetBlendAlpha(ForwardChannel))
            {
                a.CopyAnimation(ForwardChannel, 10);
            }

            SetSequence(pawn, a, ForwardChannel, pawn.ForwardAnim);
            if (a.GetBlendAlpha(11) < a.GetBlendAlpha(BackwardChannel))
            {
                a.CopyAnimation(BackwardChannel, 11);
            }

            SetSequence(pawn, a, BackwardChannel, pawn.ForwardAnim);
        }

        if (pawn.RightAnim != a.GetSequence(SideChannel))
        {
            // The engine copies channel 5 here, not 7.
            if (a.GetBlendAlpha(12) < a.GetBlendAlpha(SideChannel))
            {
                a.CopyAnimation(ForwardChannel, 12);
            }

            SetSequence(pawn, a, SideChannel, pawn.RightAnim);
            if (a.GetBlendAlpha(13) < a.GetBlendAlpha(OtherSideChannel))
            {
                a.CopyAnimation(OtherSideChannel, 13);
            }

            SetSequence(pawn, a, OtherSideChannel, pawn.RightAnim);
        }

        if (fu < 0f)
        {
            a.StepBlendAlpha(ForwardChannel, 0f, step);
            a.StepBlendAlpha(BackwardChannel, fw, step);
        }
        else
        {
            a.StepBlendAlpha(ForwardChannel, fw, step);
            a.StepBlendAlpha(BackwardChannel, 0f, step);
        }

        if (su < 0f)
        {
            a.StepBlendAlpha(SideChannel, 0f, step);
            a.StepBlendAlpha(OtherSideChannel, sw, step);
        }
        else
        {
            a.StepBlendAlpha(SideChannel, sw, step);
            a.StepBlendAlpha(OtherSideChannel, 0f, step);
        }

        for (int ch = 9; ch < 14; ch++)
        {
            a.StepBlendAlpha(ch, 0f, step);
        }

        a.StepBlendAlpha(RunChannel, 0f, step);
        for (int ch = 4; ch < 14; ch++)
        {
            a.SetChannelRateScale(ch, fps / a.ClipRate(ch));
        }

        for (int ch = 6; ch < 14; ch++)
        {
            a.SetAnimFrame(ch, c5, unitFlag: false);
        }
    }

    /// <summary>The aim blend of the split jump and the hand-over-hand fire (A6.5).</summary>
    [UcNative("ASBasePawn::AimBlend", Editor = 0x111ffa90)]
    [Provenance(ProvenanceKind.Decoded, "0x111ffa90: corner and side weights from the yaw and pitch errors")]
    private static void AimBlend(SBasePawn pawn, PawnAnimation a, float dt, float yaw, float pitch)
    {
        a.SetChannelRateScale(5, 1f);
        a.SetChannelRateScale(8, 1f);
        a.SetChannelRateScale(10, 1f);
        a.SetChannelRateScale(13, 1f);
        float maxStep = pawn.bWeaponTransit ? dt * AnimationK.AimStepTransit : dt * AnimationK.AimStep;
        ZeroSets(a);
        if (pawn.RunAnim != a.GetSequence(5))
        {
            SetSequence(pawn, a, 5, pawn.RunAnim);
            a.StepBlendAlpha(5, 0f, AnimationK.AimSetChangeStep);
        }

        if (pawn.RightAnim != a.GetSequence(8))
        {
            SetSequence(pawn, a, 8, pawn.RightAnim);
            a.StepBlendAlpha(8, 0f, AnimationK.AimSetChangeStep);
        }

        if (pawn.ForwardAnim != a.GetSequence(10))
        {
            SetSequence(pawn, a, 10, pawn.ForwardAnim);
            a.StepBlendAlpha(10, 0f, AnimationK.AimSetChangeStep);
        }

        if (pawn.LeftAnim != a.GetSequence(13))
        {
            SetSequence(pawn, a, 13, pawn.LeftAnim);
            a.StepBlendAlpha(13, 0f, AnimationK.AimSetChangeStep);
        }

        float c5 = a.GetAnimFrame(5);
        a.SetAnimFrame(8, c5, unitFlag: false);
        a.SetAnimFrame(10, c5, unitFlag: false);
        a.SetAnimFrame(13, c5, unitFlag: false);
        float side;
        float corner;
        int other;
        if (-yaw > pitch)
        {
            Weights(-pitch, -yaw, out side, out corner);
            a.StepBlendAlpha(13, corner, maxStep);
            other = 10;
        }
        else
        {
            Weights(yaw, pitch, out side, out corner);
            a.StepBlendAlpha(10, corner, maxStep);
            other = 13;
        }

        a.StepBlendAlpha(other, 0f, maxStep);
        a.StepBlendAlpha(5, 1f, maxStep);
        a.StepBlendAlpha(8, side, maxStep);
    }

    /// <summary>The corner and side weights of the aim blend for errors (p, q).</summary>
    private static void Weights(float p, float q, out float side, out float corner)
    {
        if (AnimationK.AimSaturation <= p + q)
        {
            corner = 1f;
            side = 0f;
            return;
        }

        float m = (2f - p) - q;
        float k = (q - p) / m;
        float pm = p - 1f;
        side = 1f - SvmMath.Abs(pm / m);
        float qm = q - 1f;
        float k0 = k - 1f;
        float k1 = k + 1f;
        corner = 1f - SvmMath.Sqrt(((pm * pm) + (qm * qm)) / ((k0 * k0) + (k1 * k1)));
    }

    /// <summary>The split jump's weapon channel while the weapon is in transit (A6 step 4.5).</summary>
    [Provenance(ProvenanceKind.Estimated, "The channel query at mesh instance slot 0xec is read as IsAnimBackward (ANSWERS-anim.md A6 step 4.5)")]
    private static void VandammeWeapon(SBasePawn pawn, PawnAnimation a, float dt)
    {
        bool loaded = pawn.WeaponLoaded != 0;
        if (a.IsAnimBackward(SBasePawn.DRAWCHANNEL))
        {
            a.StepBlendAlpha(SBasePawn.DRAWCHANNEL, loaded ? 1f : 0f, dt * AnimationK.VandammeHolster);
        }
        else if (!loaded)
        {
            a.StepBlendAlpha(SBasePawn.DRAWCHANNEL, 1f, dt * AnimationK.VandammeDrawIn);
        }
        else
        {
            a.StepBlendAlpha(SBasePawn.DRAWCHANNEL, 0f, dt * AnimationK.VandammeDrawOut);
        }
    }

    /// <summary>The back-to-wall and ledge shimmy blend (A6.6): RightAnim on 5, LeftAnim on 8 by the side speed.</summary>
    [UcNative("ASBasePawn::ShimmyBlend", Editor = 0x111ffe80)]
    [Provenance(ProvenanceKind.Decoded, "0x111ffe80: side speed from the facing axis, the corner step 4 dt at BlendChangeTime 20")]
    private static void ShimmyBlend(SBasePawn pawn, PawnAnimation a, float dt)
    {
        float step = dt / pawn.BlendChangeTime;
        if (pawn.BlendChangeTime == AnimationK.ShimmyCornerBlendTime)
        {
            step = dt * AnimationK.ShimmyCornerStep;
        }

        ZeroSets(a);
        a.SetChannelRateScale(5, 1f);
        a.SetChannelRateScale(8, 1f);
        a.StepBlendAlpha(10, 0f, step);
        a.StepBlendAlpha(13, 0f, step);
        float c5 = a.GetAnimFrame(5);
        if (pawn.RightAnim != a.GetSequence(5))
        {
            a.CopyAnimation(5, 10);
            SetSequence(pawn, a, 5, pawn.RightAnim);
        }

        if (pawn.LeftAnim != a.GetSequence(8))
        {
            a.CopyAnimation(8, 13);
            SetSequence(pawn, a, 8, pawn.LeftAnim);
        }

        FVector x = pawn.Rotation.Vector();
        float right = 0f;
        float left = 0f;
        float u = (x.Y * pawn.Velocity.X) - (x.X * pawn.Velocity.Y);
        if (u <= AnimationK.ShimmySideSpeed)
        {
            if (u < -AnimationK.ShimmySideSpeed)
            {
                right = 1f;
            }
        }
        else
        {
            left = 1f;
        }

        if (pawn.BlendChangeTime == AnimationK.ShimmyCornerBlendTime)
        {
            return;
        }

        a.StepBlendAlpha(5, right, step);
        a.StepBlendAlpha(8, left, step);
        if (left > 0f || right > 0f)
        {
            a.SetAnimFrame(5, c5, unitFlag: false);
            a.SetAnimFrame(8, c5, unitFlag: false);
            a.SetAnimFrame(10, c5, unitFlag: false);
            a.SetAnimFrame(13, c5, unitFlag: false);
        }

        a.SetChannelRateScale(5, pawn.Velocity.Size() / pawn.fBlendCoeffWalkAnim);
    }

    /// <summary>The grabbed merc following the spy (A6.7).</summary>
    [UcNative("ASBasePawn::FollowParentBlend", Editor = 0x112004b0)]
    [Provenance(ProvenanceKind.Decoded, "0x112004b0: forward or backward weight, side weight on 9 or 8, frames from channel 5")]
    private static void FollowParentBlend(SBasePawn pawn, PawnAnimation a, float dt)
    {
        float c5 = a.GetAnimFrame(5);
        if (pawn.ForwardAnim != a.GetSequence(5))
        {
            a.ZeroMovementAlpha(1, 15, AnimationK.AtOnce);
            SetSequence(pawn, a, 5, pawn.ForwardAnim);
            SetSequence(pawn, a, 6, pawn.ForwardAnim);
        }

        if (pawn.RightAnim != a.GetSequence(8))
        {
            SetSequence(pawn, a, 9, pawn.LeftAnim);
            SetSequence(pawn, a, 8, pawn.RightAnim);
        }

        float step = dt / pawn.BlendChangeTime;
        FVector vel = pawn.Velocity;
        if (!vel.IsZero() && a.GetSequence(0) != new Name(AnimationK.FollowParentCatchClip))
        {
            FVector x = pawn.Rotation.Vector();
            x.Z = 0f;
            x = x.SafeNormal();
            FVector d = vel.SafeNormal();
            FVector y = x.Cross(new FVector(0f, 0f, 1f)).SafeNormal();
            float side = (y.X * d.X) + (d.Y * y.Y) + (d.Z * y.Z);
            float f = (x.X * vel.X) + (x.Z * vel.Z) + (x.Y * vel.Y);
            float s = (y.X * vel.X) + (y.Y * vel.Y) + (y.Z * vel.Z);
            float w = SvmMath.Abs(s) <= 1f ? 0f : 1f / (SvmMath.Abs(f / s) + 1f);
            if (f < 0f)
            {
                a.StepBlendAlpha(5, 0f, step);
                a.StepBlendAlpha(6, 1f, step);
            }
            else
            {
                a.StepBlendAlpha(5, 1f, step);
                a.StepBlendAlpha(6, 0f, step);
            }

            int sideChannel;
            if (side < 0f)
            {
                a.StepBlendAlpha(2, 0f, step);
                a.StepBlendAlpha(8, w, step);
                sideChannel = 2;
            }
            else
            {
                a.StepBlendAlpha(9, w, step);
                a.StepBlendAlpha(8, 0f, step);
                sideChannel = 9;
            }

            a.SetChannelRateScale(sideChannel, 1f);
            a.SetChannelRateScale(8, 1f);
            a.SetAnimFrame(5, c5, unitFlag: false);
            a.SetAnimFrame(6, c5, unitFlag: false);
            a.SetAnimFrame(0, a.GetAnimFrame(0), unitFlag: false);
            return;
        }

        a.StepBlendAlpha(1, 0f, step);
        a.StepBlendAlpha(2, 0f, step);
        a.StepBlendAlpha(3, 0f, step);
        a.StepBlendAlpha(5, 0f, step);
        a.StepBlendAlpha(6, 0f, step);
        a.StepBlendAlpha(9, 0f, step);
        a.StepBlendAlpha(8, 0f, step);
    }

    /// <summary>
    /// The climb step of the vertical pipe and the ladder (A6.8): ForwardAnim on 5 (up) or 6 (down) at once, its
    /// frame marking the steps where a climb may stop, and channel 0's mirror following the leading hand.
    /// </summary>
    [UcNative("ASBasePawn::ClimbStep", Editor = 0x11200140)]
    [Provenance(ProvenanceKind.Decoded, "0x11200140: rest at Velocity.Z 0, restart at clip rate x dt / N (+ 0.5 left), bCanStop at 0.45 and past 0.95, bIsLeftPart and channel 0's mirror on the middle half")]
    [Provenance(ProvenanceKind.Measured, "golden traversal.spy-pipe-climb-down: bCanStop pulses at channel 5 frames 0.455 and 0.954, bIsLeftPart toggles at 0.264 and 0.763")]
    public static void ClimbStep(SBasePawn pawn, PawnAnimation a, float dt)
    {
        ArgumentGuard(pawn, a);
        a.ZeroMovementAlpha(2, 4, AnimationK.AtOnce);
        a.ZeroMovementAlpha(7, 14, AnimationK.AtOnce);
        a.ZeroMovementAlpha(4, 5, AnimationK.AtOnce);
        a.SetChannelRateScale(5, 1f);
        float frame = a.GetAnimFrame(5);
        if (pawn.ForwardAnim != a.GetSequence(5))
        {
            SetSequence(pawn, a, 5, pawn.ForwardAnim);
        }

        if (pawn.ForwardAnim != a.GetSequence(6))
        {
            SetSequence(pawn, a, 6, pawn.ForwardAnim);
        }

        if (pawn.Velocity.Z == 0f)
        {
            a.StepBlendAlpha(5, 0f, AnimationK.AtOnceClimb);
            a.StepBlendAlpha(6, 0f, AnimationK.AtOnceClimb);
            a.SetChannelRateScale(5, 0f);
            pawn.fOldFrame = -1f;
            pawn.bIsStopped = true;
            pawn.bCanStop = true;
            return;
        }

        if (pawn.bIsStopped)
        {
            float n = PawnActions.Clip(pawn, pawn.ForwardAnim).Frames;
            frame = a.ClipRate(5) * dt / n;
            if (pawn.bIsLeftPart)
            {
                frame += AnimationK.ClimbLeftOffset;
            }
        }

        pawn.bIsStopped = false;
        if (pawn.Velocity.Z <= 0f)
        {
            a.StepBlendAlpha(5, 0f, AnimationK.AtOnceClimb);
            a.StepBlendAlpha(6, 1f, AnimationK.AtOnceClimb);
        }
        else
        {
            a.StepBlendAlpha(5, 1f, AnimationK.AtOnceClimb);
            a.StepBlendAlpha(6, 0f, AnimationK.AtOnceClimb);
        }

        a.SetAnimFrame(5, frame, unitFlag: false);
        a.SetAnimFrame(6, frame, unitFlag: false);
        float old = pawn.fOldFrame;
        if (old < AnimationK.ClimbStopUp && AnimationK.ClimbStopUp < frame)
        {
            pawn.bCanStop = true;
        }

        if ((AnimationK.ClimbStopWrap < old && frame < AnimationK.ClimbStopWrap) || AnimationK.ClimbStopEnd < frame)
        {
            pawn.bCanStop = true;
        }

        if ((old < AnimationK.ClimbLeftFrom || AnimationK.ClimbLeftTo < old) &&
            AnimationK.ClimbLeftFrom < frame && frame < AnimationK.ClimbLeftTo)
        {
            a.SetMirror(0, false);
            pawn.bIsLeftPart = true;
        }

        if (AnimationK.ClimbLeftFrom < old && old < AnimationK.ClimbLeftTo &&
            (frame < AnimationK.ClimbLeftFrom || AnimationK.ClimbLeftTo < frame))
        {
            a.SetMirror(0, true);
            pawn.bIsLeftPart = false;
        }

        pawn.fOldFrame = frame;
    }

    /// <summary>The zeroing the aim and shimmy blends start with: 2-3, 6-7, 9, 11-12 and 4 at once.</summary>
    private static void ZeroSets(PawnAnimation a)
    {
        a.ZeroMovementAlpha(2, 4, AnimationK.AtOnce);
        a.ZeroMovementAlpha(6, 8, AnimationK.AtOnce);
        a.ZeroMovementAlpha(9, 10, AnimationK.AtOnce);
        a.ZeroMovementAlpha(11, 13, AnimationK.AtOnce);
        a.ZeroMovementAlpha(14, 14, AnimationK.AtOnce);
        a.ZeroMovementAlpha(4, 5, AnimationK.AtOnce);
    }

    /// <summary>SetAnimSequence by name (None empties the channel's sequence).</summary>
    private static void SetSequence(SBasePawn pawn, PawnAnimation a, int channel, Name sequence)
    {
        if (sequence.IsNone)
        {
            a.ClearSequence(channel);
            return;
        }

        a.SetAnimSequence(channel, PawnActions.Clip(pawn, sequence));
    }

    private static PawnClip Clip(SBasePawn pawn, Name sequence) => PawnActions.Clip(pawn, sequence);

    /// <summary>A 16-bit rotator difference as a signed value.</summary>
    private static int Signed16(int value)
    {
        int d = value & 0xffff;
        return d > 0x7fff ? d - 0x10000 : d;
    }

    private static float Clamp1(float value) => value < -1f ? -1f : value > 1f ? 1f : value;

    private static void ArgumentGuard(SBasePawn pawn, PawnAnimation a)
    {
        System.ArgumentNullException.ThrowIfNull(pawn);
        System.ArgumentNullException.ThrowIfNull(a);
    }
}
