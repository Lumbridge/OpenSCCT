using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController scope states s_Binoculars_Def and s_Binoculars_Def_Reloading (spec/equipment/merc-scope.md):
// the raise and lower, three zoom levels with the zoom animation, the slow aim with the view lag, the breath sway
// and hold, the fire kick, scoped walking, the scoped shot and the reload inside the scope.
public partial class SPlayerController
{
    private static readonly Name CNAME_s_Sniping = new("s_Sniping");
    private static readonly Name CNAME_s_Binoculars_Def_Reloading = new("s_Binoculars_Def_Reloading");

    // ---- shared pieces ------------------------------------------------------------------------------------

    /// <summary>SetFOV: the view angle now and as the target.</summary>
    [PortOf("uc:Engine.PlayerController:SetFOV", Spec = "spec/equipment/merc-scope.md#rules")]
    public void ScopeSetFOV(float NewFOV)
    {
        FovAngle = NewFOV;
        DesiredFOV = NewFOV;
    }

    /// <summary>ResetFOV: back to the default view angle.</summary>
    [PortOf("uc:Engine.PlayerController:ResetFOV", Spec = "spec/equipment/merc-scope.md#rules")]
    public void ScopeResetFOV() => ScopeSetFOV(DefaultFOV);

    /// <summary>The scope's aim speed for the current zoom level.</summary>
    private float ScopeAimSpeed => fBinocularVisionFOV == K.ScopeFov1 ? DES_fJumelleSpeed_Def_Zoom1 :
        fBinocularVisionFOV == K.ScopeFov2 ? DES_fJumelleSpeed_Def_Zoom2 : DES_fJumelleSpeed_Def_Zoom3;

    /// <summary>The breath amplitude for the current zoom level.</summary>
    private float ScopeBreathAmplitude => fBinocularVisionFOV == K.ScopeFov1 ? BinocularBreathAmplitudeZoom1 :
        fBinocularVisionFOV == K.ScopeFov2 ? BinocularBreathAmplitudeZoom2 : BinocularBreathAmplitudeZoom3;

    /// <summary>The exhausted-breath penalty K for the current zoom level.</summary>
    private float ScopeBreathPenalty => fBinocularVisionFOV == K.ScopeFov1 ? K.BreathPenalty1 :
        fBinocularVisionFOV == K.ScopeFov2 ? K.BreathPenalty2 : K.BreathPenalty3;

    /// <summary>A rotation component wrapped into the signed half turn (rule 9).</summary>
    private static int WrapAxis(int a)
    {
        a &= 65535;
        return a > K.ScopeWrapHalf ? a - 65536 : a;
    }

    private void ScopeSound(AssetRef Sound)
    {
        if (Pawn is { } pawn)
        {
            pawn.PlaySound(Sound, ESoundSlot.SLOT_None);
        }
    }

    /// <summary>
    /// UpdateRotationDefenseur (rules 6 to 9): the slow aim with the view chasing it, the breath sway and hold, the
    /// fire kick with its FOV pulse, then the view and the aim committed.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Binoculars_Def.UpdateRotationDefenseur", Sha1 = "aefdf42467d7fadc651ddb3420237d3b2261bf65")]
    [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.UpdateRotationDefenseur", Spec = "spec/equipment/merc-scope.md#rules")]
    public void ScopeUpdateRotation(float dt)
    {
        // Rule 6: aim.
        float m = dt * 100f;
        FRotator view = Rotation.Normalize();
        FRotator des = MyDesiredRotation.Normalize();
        FRotator dif = (des - view).Normalize();
        float s = ScopeAimSpeed;
        float vAcc = fLookUpValue * s * fSensibiliteSouris * dt;
        float k = 1f;
        int absPitch = des.Pitch < 0 ? -des.Pitch : des.Pitch;
        bool backToLevel = (des.Pitch > 0 && vAcc < 0f) || (des.Pitch < 0 && vAcc > 0f);
        if (absPitch > K.ScopeEdgeStart && !backToLevel)
        {
            k = 1f - (absPitch - K.ScopeEdgeStart) / K.ScopeEdgeSpan;
        }

        des.Pitch += SvmMath.Trunc(vAcc * k);
        view.Pitch += m > 0f ? SvmMath.Trunc(dif.Pitch / (K.ScopeLagDivisor / m)) : 0;
        des = des.Normalize();
        if (fBinocularVisionFOV == K.ScopeFov2)
        {
            des.Pitch = SvmMath.Clamp(des.Pitch, K.ScopePitchMin20, K.ScopePitchMax20);
        }
        else if (fBinocularVisionFOV == K.ScopeFov1)
        {
            des.Pitch = SvmMath.Clamp(des.Pitch, K.ScopePitchMin30, K.ScopePitchMax30);
        }

        float hAcc = fTurnValue * s * fSensibiliteSouris * dt * K.ScopeYawFactor;
        des.Yaw += SvmMath.Trunc(hAcc);
        view.Yaw += m > 0f ? SvmMath.Trunc(dif.Yaw / (K.ScopeLagDivisor / m)) : 0;

        // Rule 7: breath.
        float amp = ScopeBreathAmplitude;
        BinocularBreathFactorUp = 0f;
        BinocularBreathFactorRight = 0f;
        bool holding = bHoldBreath != 0 && BinocularCannotBlockTime == 0f && BinocularHoldBreathTime > 0f;
        if (holding)
        {
            if (!bJustWasHoldBreath)
            {
                bJustWasHoldBreath = true;
                BinocularBreathUp = true;
                BinocularBreath = K.InspireStartPhase;
                BreathFactors(amp);
                view.Pitch -= SvmMath.Trunc(BinocularBreathFactorUp);
                des.Pitch -= SvmMath.Trunc(BinocularBreathFactorUp);
                bBinocularWaitForEndInspire = true;
                ScopeSound(Snd_BinocularInspire);
            }
            else if (bBinocularWaitForEndInspire)
            {
                BinocularBreath += K.BreathPhaseSpeed * dt;
                if (BinocularBreath >= K.HalfPi)
                {
                    bBinocularWaitForEndInspire = false;
                }
                else
                {
                    BreathFactors(amp);
                }
            }
            else
            {
                BinocularHoldBreathTime -= dt;
                if (BinocularHoldBreathTime <= 0f)
                {
                    BinocularCannotBlockTime = K.BreathExhaustedLockout;
                }

                des.Pitch += SvmMath.Trunc(vAcc * k);
                des.Yaw += SvmMath.Trunc(hAcc);
            }
        }
        else if (bJustWasHoldBreath)
        {
            bJustWasHoldBreath = false;
            if (BinocularCannotBlockTime != K.BreathExhaustedLockout)
            {
                BinocularCannotBlockTime = K.BreathReleaseLockout;
            }

            int dy = SvmMath.Trunc(BinocularBreathFactorRight - K.HalfPi);
            int dp = SvmMath.Trunc(BinocularBreathFactorUp - K.HalfPi);
            view.Yaw += dy;
            des.Yaw += dy;
            view.Pitch += dp;
            des.Pitch += dp;
            BinocularBreath = K.HalfPi;
            BinocularBreathUp = false;
            ScopeSound(Snd_BinocularStrongBreath);
        }
        else if (BinocularHoldBreathTime < K.BreathResetDelay)
        {
            float gap = K.BreathResetDelay / SvmMath.Clamp(BinocularHoldBreathTime, K.BreathGapMin, K.BreathResetDelay) - 1f;
            if (BreathSweep(dt))
            {
                ScopeSound(Snd_BinocularStrongBreath);
            }

            BinocularBreathFactorUp = SvmMath.Sin(BinocularBreath) * (amp - ScopeBreathPenalty * gap);
            BinocularBreathFactorRight = SvmMath.Cos(BinocularBreath) * K.BreathRightAmplitude;
            BinocularHoldBreathTime = SvmMath.Min(BinocularHoldBreathTime + dt, K.BreathResetDelay);
            BinocularCannotBlockTime = SvmMath.Max(BinocularCannotBlockTime - dt, 0f);
        }
        else
        {
            BinocularCannotBlockTime = SvmMath.Max(BinocularCannotBlockTime - dt, 0f);
            if (SvmMath.Abs(fTurnValue) < K.BreathStillInput && SvmMath.Abs(fLookUpValue) < K.BreathStillInput)
            {
                bool wasUp = BinocularBreathUp;
                if (BreathSweep(dt))
                {
                    BinocularBreathRight = SvmMath.Trunc(FRand() * K.BreathRightRoll) > K.BreathRightThreshold;
                    if (!wasUp)
                    {
                        ScopeSound(Snd_BinocularBreath);
                    }
                }

                BreathFactors(amp);
            }
        }

        view.Yaw += SvmMath.Trunc(BinocularBreathFactorRight);
        view.Pitch += SvmMath.Trunc(BinocularBreathFactorUp);

        // Rule 8: kick.
        if (bBinocularKick)
        {
            bBinocularKick = false;
            fBinocularKickTime = BinocularTotalKickTime;
        }

        if (fBinocularKickTime > 0f)
        {
            fBinocularKickTime = SvmMath.Max(fBinocularKickTime - dt, 0f);
            // Reloaded's fix (rl:fix.sniper_kick): the same kick for every player, whatever his mouse sensitivity.
            float q = K.KickQ * K.KickSensitivity * dt;
            float time = fBinocularKickTime;
            float kick;
            if (time > K.KickSplit)
            {
                kick = (BinocularTotalKickTime - time) * BinocularKickAmplitude * q;
                if (fBinocularZoomEffectTime <= 0f)
                {
                    ScopeSetFOV(fBinocularVisionFOV + (BinocularTotalKickTime - time) * BinocularKickFOVAmplitude * K.KickRiseShare * q);
                }
            }
            else
            {
                kick = -time * BinocularKickAmplitude * K.KickFallShare * q;
                ScopeSetFOV(fBinocularVisionFOV + time * BinocularKickFOVAmplitude * K.KickFallShare * q);
            }

            view.Pitch += SvmMath.Trunc(kick);
            des.Pitch += SvmMath.Trunc(kick);
        }

        // Rule 9: commit.
        bool steady = bHoldBreath != 0 && BinocularHoldBreathTime > 0f && BinocularCannotBlockTime <= 0f && !bBinocularWaitForEndInspire;
        if (!steady)
        {
            view = new FRotator(WrapAxis(view.Pitch), WrapAxis(view.Yaw), view.Roll);
            des.Yaw += SvmMath.Trunc(BinocularBreathFactorRight);
            des.Pitch += SvmMath.Trunc(BinocularBreathFactorUp);
            des = des.Normalize();
            des = new FRotator(WrapAxis(des.Pitch), WrapAxis(des.Yaw), des.Roll);
            Rotation = view;
            UpdateDesiredRotation(des);
        }
        else
        {
            FRotator d = (des - view) * K.BreathHoldSnap;
            view = new FRotator(WrapAxis(view.Pitch + d.Pitch), WrapAxis(view.Yaw + d.Yaw), view.Roll);
            Rotation = view;
            UpdateDesiredRotation(view);
        }

        LocalMyDesiredRotation = des;
    }

    /// <summary>The breath factors from the phase: up = sin x amplitude, right = +-cos x 4.</summary>
    private void BreathFactors(float amp)
    {
        BinocularBreathFactorUp = SvmMath.Sin(BinocularBreath) * amp;
        float right = SvmMath.Cos(BinocularBreath) * K.BreathRightAmplitude;
        BinocularBreathFactorRight = BinocularBreathRight ? -right : right;
    }

    /// <summary>The phase sweeps between -pi/2 and pi/2 at 0.8 pi per second; true at each turn.</summary>
    private bool BreathSweep(float dt)
    {
        if (BinocularBreathUp)
        {
            BinocularBreath += K.BreathPhaseSpeed * dt;
            if (BinocularBreath >= K.HalfPi)
            {
                BinocularBreath = K.HalfPi;
                BinocularBreathUp = false;
                return true;
            }
        }
        else
        {
            BinocularBreath -= K.BreathPhaseSpeed * dt;
            if (BinocularBreath <= -K.HalfPi)
            {
                BinocularBreath = -K.HalfPi;
                BinocularBreathUp = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>The scope zoom in (rule 5): 30 to 20 to 10 degrees.</summary>
    private void ScopeZoomIn()
    {
        if (fBinocularVisionFOV == K.ScopeFov1)
        {
            ScopeZoomTo(K.ScopeFov2, K.AudioZoom2);
        }
        else if (fBinocularVisionFOV == K.ScopeFov2)
        {
            ScopeZoomTo(K.ScopeFov3, K.AudioZoom3);
        }
        else
        {
            return;
        }

        ScopeSound(Snd_ZoomIn);
    }

    /// <summary>The scope zoom out (rule 5): 10 to 20 to 30 degrees.</summary>
    private void ScopeZoomOut()
    {
        if (fBinocularVisionFOV == K.ScopeFov3)
        {
            ScopeZoomTo(K.ScopeFov2, K.AudioZoom2);
        }
        else if (fBinocularVisionFOV == K.ScopeFov2)
        {
            ScopeZoomTo(K.ScopeFov1, K.AudioZoom1);
        }
        else
        {
            return;
        }

        if (myHUD is SHud hud)
        {
            ScopeSound(hud.Snd_SwitchBinoculars);
        }
    }

    private void ScopeZoomTo(float fov, float audio)
    {
        fPreviousBinocularVisionFOV = fBinocularVisionFOV;
        fBinocularVisionFOV = fov;
        AudioZoomFactor = audio;
        BinocularNoiseDuration = K.ScopeNoiseTicks;
        bChangeZoom = true;
    }

    // ---- s_Binoculars_Def ------------------------------------------------------------------------------------

    /// <summary>s_Binoculars_Def: the merc's scope.</summary>
    [Uc("SBase.SPlayerController.s_Binoculars_Def")]
    public class s_Binoculars_Def : UcActorState, IPlayerTick, IPlayerMove, IUpdateRotation, IFire, IAltFire, ISwitchROF,
        IGadLaserSwitch, IGadTorcheSwitch, IZoom, IWheel, IClientStopBinoculars, IWeaponNotifiedSnipingDrawEnded, IMyFalling,
        ICanIUseBinocular, ICanBeGrabbed, ICanBeDropAttacked, ICanBeElbowShocked, ICanCrouch, IDropAttack
    {
        public static readonly s_Binoculars_Def Instance = new(UcMeta.s_Binoculars_Def, null);

        protected s_Binoculars_Def(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Rule 2: lights off (the laser remembered), the breath reserve restored, the rifle raised in single shot,
        /// the grenade charge cleared and the first zoom at 30 degrees.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.BeginState", Sha1 = "1ecae974c731e23a29a01d340319aa2c2ce2dd7b")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.BeginState", Spec = "spec/equipment/merc-scope.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            float now = pc.Level.TimeSeconds;
            pc.iDateCheckBinoculars = 0;
            SBasePawn? pawn = pc.SBPawn;
            if (!pc.IsRemotePlayer && pc.PreviousState == CNAME_s_Binoculars_Def_Reloading)
            {
                pc.bBinocularVision = true;
                if (pawn is not null)
                {
                    CombatServices.Ports(pc).ChangeEnumAction(pawn, SBasePawn.SA_BINOCULAR_USING, false);
                }
            }

            if (pawn is SPAWNDEFENSE merc)
            {
                merc.bCanBeGrabbed = true;
                if (merc.Torche is { bInUse: true } torch)
                {
                    torch.Unload();
                }

                if (merc.TorcheLaser is { bInUse: true } laser)
                {
                    pc.bLaserPreviously = true;
                    laser.Unload();
                }
            }

            pc.BinocularNoiseDuration = K.ScopeNoiseTicks;
            pc.AudioZoomFactor = K.AudioZoom1;
            pc.VoiceChatTarget = null;
            if (now - pc.BinocularBreathLastTimeSeconds > K.BreathResetDelay)
            {
                pc.BinocularHoldBreathTime = K.BreathResetDelay;
                pc.BinocularBreath = 0f;
                pc.BinocularBreathFactorUp = 0f;
                pc.BinocularBreathFactorRight = 0f;
                pc.BinocularBreathUp = true;
                pc.BinocularBreathRight = true;
            }
            else
            {
                pc.BinocularHoldBreathTime = SvmMath.Min(K.BreathResetDelay,
                    SvmMath.Trunc(pc.BinocularHoldBreathTime + now - pc.BinocularBreathLastTimeSeconds));
            }

            pc.bBinocularKick = false;
            if (pawn?.MyWeapon is SWeaponDefense rifle)
            {
                rifle.GotoState(CNAME_s_Sniping);
                rifle.ROF_Single(true);
            }

            pc.TimerUSe = 0f;
            pc.bUsingNow = false;
            pc.SetBallisticValue(0f);
            pc.bChangeZoom = true;
            pc.fBinocularZoomEffectTime = K.FirstZoomEffectTime;
            pc.fBinocularVisionFOV = K.ScopeFov1;
            pc.fPreviousBinocularVisionFOV = pc.DefaultFOV;
        }

        /// <summary>
        /// Rule 15: the breath time stamp, the rifle lowered and idle in its previous mode, the view back to the
        /// default angle, the laser back on if it was on.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.EndState", Sha1 = "7a17f4c79aedbf5a3a6e620ec17b15b7288f52d3")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.EndState", Spec = "spec/equipment/merc-scope.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.BinocularBreathLastTimeSeconds = pc.Level.TimeSeconds;
            SBasePawn? pawn = pc.SBPawn;
            if (!pc.IsRemotePlayer && pawn is not null)
            {
                CombatServices.Ports(pc).ChangeEnumAction(pawn, SBasePawn.SA_BINOCULAR_END_USING, false);
            }

            if (pawn?.MyWeapon is SWeaponDefense rifle)
            {
                rifle.AnimateUndrawSniping();
                rifle.GotoState(CNAME_s_Selected);
            }

            if (pawn is not null)
            {
                pawn.bCanBeGrabbed = false;
            }

            CombatFx.Notify(pc, CombatFx.HudState);
            GadgetHud.SetHudState(pc, Name.None);
            pc.ScopeResetFOV();
            pc.AudioZoomFactor = 0f;
            pc.ClearBinocularVoiceTarget();
            (pawn?.MyWeapon as SWeaponDefense)?.PreviousMode();
            if (pc.bLaserPreviously && pawn is SPAWNDEFENSE merc)
            {
                pc.bLaserPreviously = false;
                PawnWeapons.PadUpGadgetSwitch(merc, CREF_SGadTorcheLaser);
            }

            pc.bPressedJump = false;
            pc.bBinocularVision = false;
        }

        /// <summary>Rule 13: the reload or ROF key with room in the clip reloads inside the scope; then the frame.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.PlayerTick", Sha1 = "8832791201ae72b2fb1caa50c05f2088ba023dc2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.PlayerTick", Spec = "spec/equipment/merc-scope.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if ((pc.bReload != 0 || pc.bROF != 0) && pc.SBPawn?.MyWeapon is SWeaponDefense { Ammo: > 0 } rifle &&
                rifle.ClipAmmo < rifle.ClipMaxAmmo && !pc.IsHudPerturbed && pc.GetStateName() == CNAME_s_Binoculars_Def)
            {
                pc.ScopeResetFOV();
                pc.eventChangeState(CNAME_s_Binoculars_Def_Reloading);
            }

            pc.PlayerTick(DeltaTime);
        }

        /// <summary>
        /// Rule 4, 10, 11 and 15: leaving when the scope is switched off, the zoom animation, the shot timer, and
        /// slow walking without jumps.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.PlayerMove", Sha1 = "1449c02c6cfced184c310d817e7a6b9293ec6132")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.PlayerMove", Spec = "spec/equipment/merc-scope.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (!self.bBinocularVision)
            {
                if (!self.IsRemotePlayer)
                {
                    int action = pawn.bIsCrouched ? SBasePawn.SA_WALK_CR_2H :
                        pawn.WeaponLoaded == 2 ? SBasePawn.SA_WALK_ST_2H : SBasePawn.SA_WALK_ST_0H;
                    CombatServices.Ports(self).ChangeEnumAction(pawn, action, false);
                }

                self.eventChangeState(self.RPreviousState);
                return;
            }

            self.Rotation.GetAxes(out FVector x, out _, out _);
            self.BinocularVoiceTrace(pawn, x, true);
            if (self.bChangeZoom)
            {
                self.fBinocularZoomEffectTime = K.ZoomEffectTime + DeltaTime;
                self.bChangeZoom = false;
            }

            if (self.fBinocularZoomEffectTime > 0f)
            {
                self.fBinocularZoomEffectTime -= DeltaTime;
                float t = self.fBinocularZoomEffectTime;
                if (t < 0f)
                {
                    self.ScopeSetFOV(self.fBinocularVisionFOV);
                }
                else if (t < K.ZoomInterpolationStart)
                {
                    float target = self.fBinocularVisionFOV;
                    self.ScopeSetFOV(target + (self.fPreviousBinocularVisionFOV - target) * t * K.ZoomInterpolationRate);
                }
            }

            self.BinocularTimeBetweenFire = SvmMath.Max(self.BinocularTimeBetweenFire - DeltaTime, 0f);

            ProcessCrouch(self, pawn);
            float a = self.aForward / K.ScopeInputSpan;
            float curve = pawn.bIsCrouched
                ? (K.ScopeCrouchA * a + K.ScopeCrouchB) * a + K.ScopeCrouchC
                : (K.ScopeStandA * a + K.ScopeStandB) * a + K.ScopeStandC;
            self.aForward *= curve * self.BinocularWalkSpeed;
            self.aStrafe *= curve * self.BinocularWalkSpeed;
            self.GroundPitch = 0;
            self.eventUpdateRotation(DeltaTime, 1f);
            FVector newAccel = self.UpdatePawnRotationWeapon(DeltaTime, self.ForwardRequest, self.StrafeRequest);
            pawn.GroundSpeed = pawn.RunningSpeed * newAccel.Size() / K.ScopeAccelNorm;
            if (pawn.GroundSpeed < (pawn.bIsCrouched ? K.ScopeMinSpeedCrouched : K.ScopeMinSpeedStanding))
            {
                pawn.GroundSpeed = 0f;
            }

            self.bPressedJump = false;
            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>The scope's view: the global prologue and epilogue around the scoped aim.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.UpdateRotationDefenseur_Mouse", Sha1 = "bcc2e1de1c17355bff5074faf9753a6427e45e02")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.UpdateRotationDefenseur_Mouse", Spec = "spec/equipment/merc-scope.md#rules")]
        public void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch)
        {
            self.MyDesiredRotation = self.LocalMyDesiredRotation;
            if (self.bLocalShootedPawn != self.bShootedPawn)
            {
                self.bLocalShootedPawn = self.bShootedPawn;
                self.fDateLastShootedPawn = self.Level.TimeSeconds;
            }

            self.ScopeUpdateRotation(DeltaTime);
            self.aTurnPrevious = self.aTurn;
        }

        /// <summary>
        /// Rule 11: an empty clip with a reserve reloads inside the scope; otherwise one shot every 0.5 s once the
        /// raise clip has ended, with the kick.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.Fire", Sha1 = "80dd6a8c486e155a62c91e1afd17f2e7c3e9ace1")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.Fire", Spec = "spec/equipment/merc-scope.md#rules")]
        public void Fire(SPlayerController self, float F)
        {
            if (self.SBPawn?.MyWeapon is not SWeaponDefense rifle)
            {
                return;
            }

            if (rifle.ClipAmmo == 0 && rifle.Ammo > 0 && !self.IsHudPerturbed)
            {
                self.ScopeResetFOV();
                self.eventChangeState(CNAME_s_Binoculars_Def_Reloading);
                return;
            }

            if (self.BinocularTimeBetweenFire <= 0f && !rifle.ViewAnim.IsAnimating(SWeaponDefense.SNIPECHANNEL))
            {
                MovementService.HooksOf(self).DiscardPickup(self);
                if (rifle.Ammo > 0)
                {
                    self.bBinocularKick = true;
                    rifle.eventFire(F);
                    self.BinocularTimeBetweenFire = self.BinocularWaitFireTime;
                }
            }
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        /// <summary>Rule 14: the mode stays single shot.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.SwitchROF", Sha1 = "c69d79eb3354daac6603fa82051543a91f8bb09f")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.SwitchROF", Spec = "spec/equipment/merc-scope.md#rules")]
        public void SwitchROF(SPlayerController self) => (self.SBPawn?.MyWeapon as SWeaponDefense)?.ROF_Single(false);

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Def.ZoomIn", Sha1 = "ce8b0df6a12e57577f5ef1b4b39743be31655ff6")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.ZoomIn", Spec = "spec/equipment/merc-scope.md#rules")]
        public void ZoomIn(SPlayerController self) => self.ScopeZoomIn();

        [Uc("SBase.SPlayerController.s_Binoculars_Def.ZoomOut", Sha1 = "243e37b0e6e4b8944cb8d2036264054424581491")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.ZoomOut", Spec = "spec/equipment/merc-scope.md#rules")]
        public void ZoomOut(SPlayerController self) => self.ScopeZoomOut();

        [Uc("SBase.SPlayerController.s_Binoculars_Def.WheelUp", Sha1 = "074e55c1ee308e4587e9a932dd3d038012308e4c")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.WheelUp", Spec = "spec/equipment/merc-scope.md#rules")]
        public void WheelUp(SPlayerController self) => self.ScopeZoomIn();

        [Uc("SBase.SPlayerController.s_Binoculars_Def.WheelDown", Sha1 = "15f8fc879fed4684fd6747bdf33ded5208de965a")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.WheelDown", Spec = "spec/equipment/merc-scope.md#rules")]
        public void WheelDown(SPlayerController self) => self.ScopeZoomOut();

        /// <summary>The scope stop: the next move leaves the scope.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.ClientStopBinoculars", Sha1 = "908a2da953419ab9a219be8e1eb052b25f13da7c")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.ClientStopBinoculars", Spec = "spec/equipment/merc-scope.md#rules")]
        public void ClientStopBinoculars(SPlayerController self) => self.bBinocularVision = false;

        /// <summary>Rule 3: the raise clip ended: the HUD scope page and the zoomed view.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.WeaponNotifiedSnipingDrawEnded", Sha1 = "89a586344e8c4fee380c3f8ea4be4b87f378defa")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.WeaponNotifiedSnipingDrawEnded", Spec = "spec/equipment/merc-scope.md#rules")]
        public void WeaponNotifiedSnipingDrawEnded(SPlayerController self)
        {
            CombatFx.Notify(self, CombatFx.HudState, 1f);
            GadgetHud.SetHudState(self, GadgetStates.s_H_Binoculars_Def);
            self.ScopeSetFOV(self.fBinocularVisionFOV);
        }

        /// <summary>Falling closes the scope at once.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.MyFalling", Sha1 = "31c6f96447e1dc1bd46fb8d46ab58b39b2e46fce")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.MyFalling", Spec = "spec/equipment/merc-scope.md#rules")]
        public void MyFalling(SPlayerController self, float LeftTime)
        {
            self.bBinocularVision = false;
            self.MyFalling(LeftTime);
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Def.CanIUseBinocular", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.CanIUseBinocular", Spec = "spec/equipment/merc-scope.md#rules")]
        public bool CanIUseBinocular(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Binoculars_Def.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.CanBeGrabbed", Spec = "spec/equipment/merc-scope.md#rules")]
        public bool CanBeGrabbed(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Binoculars_Def.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.CanBeDropAttacked", Spec = "spec/equipment/merc-scope.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Binoculars_Def.CanBeElbowShocked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.CanBeElbowShocked", Spec = "spec/equipment/merc-scope.md#rules")]
        public bool CanBeElbowShocked(SPlayerController self) => true;

        /// <summary>
        /// The scope's crouch: the stance changes without leaving the scope, with the two-handed transition clip
        /// (SA_WAIT_CR2ST_2H, SA_WAIT_ST2CR_2H), at most every 0.5 s. With no room to stand the latch goes back on.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.ProcessCrouch", Sha1 = "644f20d3e5297ac471a59ab772488956639a7c53")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.ProcessCrouch", Spec = "spec/equipment/merc-scope.md#rules")]
        private static void ProcessCrouch(SPlayerController self, SBasePawn pawn)
        {
            if (!pawn.bCanCrouch)
            {
                return;
            }

            float now = self.Level.TimeSeconds;
            if (!self.bCrouchWithRoll)
            {
                if (!pawn.bIsCrouched)
                {
                    return;
                }

                if (pawn.CanUnCrouch() && self.m_LastSpecialMoveTime < now - K.SpyBinoCrouchGuard)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                    self.m_LastSpecialMoveTime = now;
                    pawn.ChangeEnumAction(SBasePawn.SA_WAIT_CR2ST_2H);
                }
                else
                {
                    self.bCrouchWithRoll = true;
                }
            }
            else if (!pawn.bIsCrouched && self.m_LastSpecialMoveTime < now - K.SpyBinoCrouchGuard)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                self.m_LastSpecialMoveTime = now;
                pawn.ChangeEnumAction(SBasePawn.SA_WAIT_ST2CR_2H);
            }
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Def.CanCrouch", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.CanCrouch", Spec = "spec/equipment/merc-scope.md#rules")]
        public bool CanCrouch(SPlayerController self) => true;

        /// <summary>A spy dropping on the scoped merc is resolved at once (rule 17).</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def.DropAttack", Sha1 = "c65dc302cbf5c258baa07c3ff70d95f5991f3a90")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.DropAttack", Spec = "spec/equipment/merc-scope.md#rules")]
        public void DropAttack(SPlayerController self, SBasePawn PawnToAttack) =>
            MovementService.HooksOf(self).PerformDropAttack(self, PawnToAttack);
    }

    /// <summary>s_Binoculars_Def_Reloading: the reload inside the scope (rule 13).</summary>
    [Uc("SBase.SPlayerController.s_Binoculars_Def_Reloading")]
    public class s_Binoculars_Def_Reloading : UcActorState, IPlayerTick, IFire, ISwitchROF, IZoom, IWheel, ICanIUseBinocular
    {
        public static readonly s_Binoculars_Def_Reloading Instance = new(UcMeta.s_Binoculars_Def_Reloading, null);

        protected s_Binoculars_Def_Reloading(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The scope stays on, the HUD in its default page, the rifle reloads on the owner.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def_Reloading.BeginState", Sha1 = "a951b9e0b8a1ac0c5558fd33e88cc2b00965da0d")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def_Reloading.BeginState", Spec = "spec/equipment/merc-scope.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bBinocularVision = true;
            CombatFx.Notify(pc, CombatFx.HudState);
            GadgetHud.SetHudState(pc, Name.None);
            if (!pc.IsRemotePlayer)
            {
                pc.SBPawn?.MyWeapon?.eventReload();
            }
        }

        /// <summary>
        /// Each tick the view angle is reset; once the rifle has left s_Reloading and the merc is not shocked, back
        /// to the scope (its BeginState runs again).
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Def_Reloading.PlayerTick", Sha1 = "9d8ae751c0b8b3c2d4ce66e38b2e6d558427a985")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def_Reloading.PlayerTick", Spec = "spec/equipment/merc-scope.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.ScopeResetFOV();
            if (pc.SBPawn is { } pawn && !(pawn.MyWeapon?.IsInState(CNAME_s_Reloading) ?? false) && !pawn.bStickyShocking)
            {
                pc.PreviousState = CNAME_s_Binoculars_Def_Reloading;
                pc.eventChangeState(CNAME_s_Binoculars_Def);
                return;
            }

            pc.PlayerTick(DeltaTime);
        }

        public void Fire(SPlayerController self, float F)
        {
        }

        public void SwitchROF(SPlayerController self)
        {
        }

        public void ZoomIn(SPlayerController self)
        {
        }

        public void ZoomOut(SPlayerController self)
        {
        }

        public void WheelUp(SPlayerController self)
        {
        }

        public void WheelDown(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Def_Reloading.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def_Reloading.CanIUseBinocular", Spec = "spec/equipment/merc-scope.md#rules")]
        public bool CanIUseBinocular(SPlayerController self) => false;
    }
}
