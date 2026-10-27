using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController spy binoculars (spec/equipment/spy-binoculars.md): the key's spy branch and the state
// s_Binoculars_Att. The voice target is kept on the controller; the server's voice routing belongs to the voice spec.
public partial class SPlayerController
{
    private static readonly Name CNAME_s_Binoculars_Att = new("s_Binoculars_Att");

    /// <summary>The binoculars' aim speed for the current zoom level.</summary>
    private float SpyBinocularsAimSpeed => fBinocularVisionFOV == K.SpyBinoFov1 ? DES_fJumelleSpeed_Att_Zoom1 :
        fBinocularVisionFOV == K.SpyBinoFov2 ? DES_fJumelleSpeed_Att_Zoom2 : DES_fJumelleSpeed_Att_Zoom3;

    /// <summary>
    /// The binoculars key for a holstered spy (rule 1): with the goggle clip idle, a state that allows binoculars, the
    /// shock lock over and live input, a spy not yet looking switches the goggles to the binocular view and enters
    /// s_Binoculars_Att; then the flag toggles (pressing again asks to leave).
    /// </summary>
    [Uc("SBase.SPlayerController.SwitchBinocularVision", Sha1 = "6b58b59f0fe370d208093723d05ac1c7cb5f9802")]
    [PortOf("uc:SBase.SPlayerController:SwitchBinocularVision", Spec = "spec/equipment/spy-binoculars.md#rules")]
    public void SpySwitchBinocularVision()
    {
        if (SBPawn is not { } pawn || CombatServices.Ports(this).IsAnimating(pawn, K.GoggleChannel) ||
            !CombatStateEvents.CanIUseBinocular(this) || Level.TimeSeconds <= iTimeToStopStickyShock || iDontUpdatePadInput != 0)
        {
            return;
        }

        if (!bBinocularVision)
        {
            Goggle?.SwitchMode(K.GoggleBinocularsKey);
            eventChangeState(CNAME_s_Binoculars_Att);
        }

        bBinocularVision = !bBinocularVision;
    }

    /// <summary>CanHackEnemyComs: listening to the other team is allowed unless voice chat is team only.</summary>
    [Uc("SBase.SPlayerController.CanHackEnemyComs", Sha1 = "0aa34f51e0ba63c74eff6af9626c21ff02480f8a")]
    [PortOf("uc:SBase.SPlayerController:CanHackEnemyComs", Spec = "spec/equipment/spy-binoculars.md#rules")]
    public bool CanHackEnemyComs() => GameReplicationInfo is not { } gri || gri.VoiceChatMode != EVoiceChatMode.VCM_Team;

    /// <summary>The binoculars' voice target cleared (rule 6).</summary>
    private void SpyBinocularsClearVoiceTarget()
    {
        if (bVoiceChatTargetSent)
        {
            GadgetFx.Notify(this, GadgetFx.BinocularsVoice);
            bVoiceChatTargetSent = false;
        }

        VoiceChatTarget = null;
    }

    /// <summary>The binoculars' view turn (rule 7): input x dt x sensitivity x zoom speed x 5, pitch clamped.</summary>
    [Uc("SBase.SPlayerController.s_Binoculars_Att.UpdateCamRotation", Sha1 = "023583ba28898ac685c7366be0ac7d308e43a128")]
    [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.UpdateCamRotation", Spec = "spec/equipment/spy-binoculars.md#rules")]
    public void SpyBinocularsCamRotation(float DeltaTime)
    {
        float speed = SpyBinocularsAimSpeed;
        FRotator view = Rotation.Normalize();
        view.Pitch += MovementMath.Int(fLookUpValue * DeltaTime * fSensibiliteSouris * speed * K.SpyBinoTurnScale);
        view.Yaw += MovementMath.Int(fTurnValue * DeltaTime * fSensibiliteSouris * speed * K.SpyBinoTurnScale);
        view = view.Normalize();
        view.Pitch = (int)SvmMath.Clamp(view.Pitch, K.SpyBinoPitchMin, K.SpyBinoPitchMax);
        Rotation = view;
        UpdateDesiredRotation(view);
    }

    /// <summary>A zoom step (rule 5): the previous angle kept, the sound, 15 noise frames, the audio zoom, the animation.</summary>
    private void SpyBinocularsZoom(float Fov, float Audio, AssetRef Sound)
    {
        fPreviousBinocularVisionFOV = fBinocularVisionFOV;
        fBinocularVisionFOV = Fov;
        if (!Sound.IsNone)
        {
            Pawn?.PlaySound(Sound, ESoundSlot.SLOT_None);
        }

        BinocularNoiseDuration = K.SpyBinoNoiseFrames;
        AudioZoomFactor = Audio;
        bChangeZoom = true;
    }

    /// <summary>Leaves to the stance's unarmed rest without the draw reminder (rule 4).</summary>
    private void SpyBinocularsLeave()
    {
        bRemindDrawWeapon = false;
        bForceFireMode = false;
        eventChangeState(Pawn is { bIsCrouched: true } ? CNAME_s_PlayerWalkingCr0H : CNAME_s_PlayerWalkingSt0H);
    }

    /// <summary>s_Binoculars_Att: the spy's binoculars.</summary>
    [Uc("SBase.SPlayerController.s_Binoculars_Att")]
    public class s_Binoculars_Att : UcActorState, IPlayerTick, IPlayerMove, IUpdateRotation, IFire, IAltFire, IZoom, IWheel,
        IClientStopBinoculars, ICanIUseBinocular, ICanBeElbowShocked, ICanBeDropAttacked, ICanSwitchGoggleManually, ICanCrouch,
        IDropAttack, IGadLaserSwitch, IGadTorcheSwitch, IPrevStickyCam
    {
        public static readonly s_Binoculars_Att Instance = new(UcMeta.s_Binoculars_Att, null);

        protected s_Binoculars_Att(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Rule 2: the first zoom at 30 degrees, the HUD page, EMF-visible, no pad-up gadget, a drawn pistol holstered
        /// at once, no draw reminder, the first audio zoom and no voice target.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Att.BeginState", Sha1 = "00ddcc06cf7c07d908a2218b61e2c43448c4e98b")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.BeginState", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.iDateCheckBinoculars = 0;
            pc.bChangeZoom = false;
            pc.bBehindView = false;
            pc.fPreviousBinocularVisionFOV = K.SpyBinoFov1;
            pc.fBinocularVisionFOV = K.SpyBinoFov1;
            pc.ScopeSetFOV(pc.fBinocularVisionFOV);
            pc.MyDesiredRotation = pc.Rotation;
            GadgetHud.SetHudState(pc, GadgetStates.s_H_Binoculars_Att);
            if (pc.SBPawn is { } pawn)
            {
                if (pc.IAmOnClient())
                {
                    PawnVision.TestAndSetEMFVisible(pawn);
                }

                PawnInventory.PadUpGadgetSwitch(pawn, default);
                if (pawn.WeaponLoaded != 0)
                {
                    PawnVision.SetWeaponLoaded(pawn, 0);
                    if (pawn.MyWeapon is SWeaponAttaque pistol)
                    {
                        pistol.ServerSwitchLaser(false);
                        pistol.AnimateQuickUnDrawWeapon();
                    }

                    if (pawn.bIsCrouched)
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_2Hto0H);
                        pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
                    }
                    else
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2Hto0H);
                        pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                    }
                }
            }

            pc.bRemindDrawWeapon = false;
            pc.bForceFireMode = false;
            pc.AudioZoomFactor = K.AudioZoom1;
            pc.VoiceChatTarget = null;
        }

        /// <summary>
        /// Rule 3: the behind view back, the default angle, the goggles out of the binocular view, the HUD page off, the
        /// voice target cleared, and on the input machine the spy stops being EMF-visible when nothing else shows him.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Att.EndState", Sha1 = "aad5c162135c918b9c4547036aac0a4cbc6d7b52")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.EndState", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bBehindView = true;
            pc.fBinocularVisionFOV = K.SpyBinoFov1;
            pc.ScopeResetFOV();
            pc.Goggle?.SwitchMode(K.GoggleBinocularsKey);
            pc.bPressedJump = false;
            GadgetHud.SetHudState(pc, Name.None);
            pc.AudioZoomFactor = 0f;
            pc.SpyBinocularsClearVoiceTarget();
            pc.bBinocularVision = false;
            if (pc.IAmOnClient() && pc.SBPawn is { WeaponLoaded: 0 } pawn)
            {
                int mode = pawn.GoggleBeam?.CurrentMode ?? pc.Goggle?.CurrentMode ?? 0;
                if ((mode == 0 || mode == SGoggle.REN_DynLight) && pawn.DesiredGlassEffect == 0 && !pawn.bSpyBulletOn)
                {
                    PawnVision.UnSetEMFToServer(pawn);
                }
            }
        }

        /// <summary>Rule 4: the flag off leaves to the unarmed rest; then the global tick.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Att.PlayerTick", Sha1 = "56a74ceae7c2d85c1f10a8fa3a146327db486cc9")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.PlayerTick", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (!pc.bBinocularVision)
            {
                pc.SpyBinocularsLeave();
            }

            pc.PlayerTick(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Att.ClientStopBinoculars", Sha1 = "8013c92659ef3a0c0cadd4f288e37cdbb5b36d3b")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.ClientStopBinoculars", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void ClientStopBinoculars(SPlayerController self)
        {
            self.bBinocularVision = false;
            self.SpyBinocularsLeave();
        }

        /// <summary>The view turn with the global prologue and epilogue (rule 7).</summary>
        public void UpdateRotation(SPlayerController self, float DeltaTime, float maxPitch)
        {
            self.MyDesiredRotation = self.LocalMyDesiredRotation;
            if (self.bLocalShootedPawn != self.bShootedPawn)
            {
                self.bLocalShootedPawn = self.bShootedPawn;
                self.fDateLastShootedPawn = self.Level.TimeSeconds;
            }

            self.SpyBinocularsCamRotation(DeltaTime);
            self.aTurnPrevious = self.aTurn;
            self.LocalMyDesiredRotation = self.MyDesiredRotation;
        }

        /// <summary>
        /// Rule 6: the view, the voice target once per second, the zoom animation, the noise frames, crouching, the slow
        /// walk, the HUD's sound timer, and a Use press at most once per second marks the objective.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Att.PlayerMove", Sha1 = "c6333cc9b040f07f5f3e6e3239c32d7f8e1220a9")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.PlayerMove", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.Rotation.GetAxes(out FVector x, out _, out _);
            self.eventUpdateRotation(DeltaTime, 2f);
            float now = self.Level.TimeSeconds;
            int second = SvmMath.Trunc(now);
            if (self.iDateCheckBinoculars != second)
            {
                self.iDateCheckBinoculars = second;
                FVector eye = pawn.Location + CombatServices.Ports(self).EyePosition(pawn);
                Actor? hit = MovementNatives.Trace(self, out _, out _, eye + x * (self.AudioZoomFactor * K.SpyBinoVoiceRange),
                    eye + x * K.SpyBinoVoiceStart, true);
                if (hit is null)
                {
                    if (self.VoiceChatTarget is not null)
                    {
                        self.SpyBinocularsClearVoiceTarget();
                    }
                }
                else if (!ReferenceEquals(hit, self.VoiceChatTarget))
                {
                    if (self.VoiceChatTarget is not null)
                    {
                        self.SpyBinocularsClearVoiceTarget();
                    }

                    if (hit is SPAWNDEFENSE && self.CanHackEnemyComs())
                    {
                        pawn.PlaySound(self.Snd_VoiceHacking, ESoundSlot.SLOT_None);
                        self.VoiceChatTarget = hit;
                        self.bVoiceChatTargetSent = true;
                        GadgetFx.Notify(self, GadgetFx.BinocularsVoice, default, hit.ActorId);
                    }
                }
            }

            if (self.bChangeZoom)
            {
                self.fBinocularZoomEffectTime = K.SpyBinoZoomTime + DeltaTime;
                self.bChangeZoom = false;
            }

            if (self.fBinocularZoomEffectTime > 0f)
            {
                self.fBinocularZoomEffectTime -= DeltaTime;
                if (self.fBinocularZoomEffectTime < 0f)
                {
                    self.fBinocularZoomEffectTime = 0f;
                    self.ScopeSetFOV(self.fBinocularVisionFOV);
                }
                else
                {
                    float target = self.fBinocularVisionFOV;
                    self.ScopeSetFOV(target + (self.fPreviousBinocularVisionFOV - target) * self.fBinocularZoomEffectTime * K.SpyBinoZoomRate);
                }
            }

            if (self.BinocularNoiseDuration > 0)
            {
                self.BinocularNoiseDuration--;
            }

            self.bPressedJump = false;
            ProcessCrouch(self, pawn);
            float a = self.aForward / K.ScopeInputSpan;
            float curve = pawn.bIsCrouched
                ? (K.ScopeCrouchA * a + K.ScopeCrouchB) * a + K.ScopeCrouchC
                : (K.ScopeStandA * a + K.ScopeStandB) * a + K.ScopeStandC;
            self.aForward *= curve;
            self.aStrafe *= curve;
            FVector newAccel = self.UpdatePawnRotationWeapon(DeltaTime, MovementMath.Int(self.BinocularWalkSpeed * self.aForward),
                MovementMath.Int(self.BinocularWalkSpeed * self.aStrafe));
            float ground = pawn.RunningSpeed * (newAccel.Size() / K.ScopeAccelNorm);
            if (ground < (pawn.bIsCrouched ? K.SpyBinoMinSpeedCrouched : K.SpyBinoMinSpeedStanding))
            {
                ground = 0f;
            }

            pawn.GroundSpeed = ground;
            if (ground == 0f)
            {
                newAccel = FVector.Zero;
            }

            self.SendMove(DeltaTime, newAccel);
            if (self.myHUD is SHud hud)
            {
                hud.BinocularSndRegular += DeltaTime;
                if (self.bUsing > 0 && now - self.TimeUsing >= 0f)
                {
                    self.TimeUsing = now + K.SpyBinoMarkRepeat;
                    SpyBinoculars.LockObjectiveBinocularsAtt(hud, self);
                }
            }
        }

        /// <summary>The binoculars' crouch (rule 6): the stance request without leaving the state, at most every 0.5 s.</summary>
        [Uc("SBase.SPlayerController.s_Binoculars_Att.ProcessCrouch", Sha1 = "ce1d52977dfe3f100f386e99e4473a4ae77b0700")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.ProcessCrouch", Spec = "spec/equipment/spy-binoculars.md#rules")]
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
                    pawn.ChangeEnumAction(pawn.WeaponLoaded == 0 ? SBasePawn.SA_WAIT_CR2ST_0H : SBasePawn.SA_WAIT_CR2ST_2H);
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
                pawn.ChangeEnumAction(pawn.WeaponLoaded == 0 ? SBasePawn.SA_WAIT_ST2CR_0H : SBasePawn.SA_WAIT_ST2CR_2H);
            }
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Att.ZoomIn", Sha1 = "ce8b0df6a12e57577f5ef1b4b39743be31655ff6")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.ZoomIn", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void ZoomIn(SPlayerController self)
        {
            if (self.fBinocularVisionFOV > K.SpyBinoFov2)
            {
                self.SpyBinocularsZoom(K.SpyBinoFov2, K.AudioZoom2, self.Snd_ZoomIn);
            }
            else if (self.fBinocularVisionFOV > K.SpyBinoFov3)
            {
                self.SpyBinocularsZoom(K.SpyBinoFov3, K.AudioZoom3, self.Snd_ZoomIn);
            }
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Att.ZoomOut", Sha1 = "243e37b0e6e4b8944cb8d2036264054424581491")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.ZoomOut", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void ZoomOut(SPlayerController self)
        {
            AssetRef sound = (self.myHUD as SHud)?.Snd_SwitchBinoculars ?? default;
            if (self.fBinocularVisionFOV < K.SpyBinoFov2)
            {
                self.SpyBinocularsZoom(K.SpyBinoFov2, K.AudioZoom2, sound);
            }
            else if (self.fBinocularVisionFOV < K.SpyBinoFov1)
            {
                self.SpyBinocularsZoom(K.SpyBinoFov1, K.AudioZoom1, sound);
            }
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Att.WheelUp", Sha1 = "074e55c1ee308e4587e9a932dd3d038012308e4c")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.WheelUp", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void WheelUp(SPlayerController self) => ZoomIn(self);

        [Uc("SBase.SPlayerController.s_Binoculars_Att.WheelDown", Sha1 = "15f8fc879fed4684fd6747bdf33ded5208de965a")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.WheelDown", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void WheelDown(SPlayerController self) => ZoomOut(self);

        [Uc("SBase.SPlayerController.s_Binoculars_Att.DropAttack", Sha1 = "1ecd88ecf104eb177db7eace702a9b201a9fa4f3")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.DropAttack", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public void DropAttack(SPlayerController self, SBasePawn PawnToAttack) =>
            MovementService.HooksOf(self).PerformDropAttack(self, PawnToAttack);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        public void PrevStickyCam(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_Binoculars_Att.CanIUseBinocular", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.CanIUseBinocular", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public bool CanIUseBinocular(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Binoculars_Att.CanBeElbowShocked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.CanBeElbowShocked", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public bool CanBeElbowShocked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Binoculars_Att.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.CanBeDropAttacked", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Binoculars_Att.CanSwitchGoggleManually", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.CanSwitchGoggleManually", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public bool CanSwitchGoggleManually(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Binoculars_Att.CanCrouch", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Binoculars_Att.CanCrouch", Spec = "spec/equipment/spy-binoculars.md#rules")]
        public bool CanCrouch(SPlayerController self) => true;
    }
}
