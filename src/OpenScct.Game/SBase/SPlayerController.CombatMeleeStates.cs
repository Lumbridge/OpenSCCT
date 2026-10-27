using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController melee states (spec/combat/melee-elbow-charge-berserk.md): the elbow strike and its knock-back,
// the merc charge with its hit states, the charge victim's knock-back and the berserk spin. Their damage handlers are
// in SPlayerController.Combat.cs.
public partial class SPlayerController
{
    private static readonly Name MNAME_s_PlayerCharging_HitEnemy = new("s_PlayerCharging_HitEnemy");
    private static readonly Name MNAME_s_PlayerCharging_HitFriend = new("s_PlayerCharging_HitFriend");
    private static readonly Name MNAME_s_PlayerCharging_HitWall = new("s_PlayerCharging_HitWall");
    private static readonly Name MNAME_s_PlayerCharging_HitStickyShocked = new("s_PlayerCharging_HitStickyShocked");
    private static readonly Name MNAME_s_Charge_GoBackward = new("s_Charge_GoBackward");
    private static readonly Name MNAME_s_Charging_Bump = new("s_Charging_Bump");
    private static readonly Name MNAME_PlayerFallingKO = new("PlayerFallingKO");

    /// <summary>The knock-back blur (rule 12): strength from the hit direction, 0 to 1; length 2 + 5 c seconds.</summary>
    private void KnockBackBlur()
    {
        float c = SvmMath.Clamp(1f - (fElbowDamage + 1f) / 2f, 0f, 1f);
        CombatFx.Notify(this, CombatFx.CrossEyeBlur, c, 0f, K.ElbowBlurBase + K.ElbowBlurScale * c);
    }

    /// <summary>A knocked-back player leaves back-to-wall with the stance action.</summary>
    private void LeaveBackToWall(SBasePawn pawn)
    {
        if (pawn.BTW != BTW_.NOBTW)
        {
            CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BTW_OUT, true);
        }
    }

    /// <summary>The pawn stays put with the view locked to its yaw (knock-back and spin clips move it by root motion).</summary>
    private void LockedViewMove(float DeltaTime, bool bCameraFree)
    {
        if (Pawn is not { } pawn)
        {
            return;
        }

        if (bCameraFree)
        {
            eventUpdateRotation(DeltaTime, 1f);
        }
        else
        {
            Rotation = new FRotator(Rotation.Pitch, pawn.Rotation.Yaw, 0);
        }

        bPressedJump = false;
        pawn.Acceleration = FVector.Zero;
        SendMove(DeltaTime, FVector.Zero);
    }

    /// <summary>Fire again when the button is still held after a melee clip.</summary>
    private void FireIfHeld()
    {
        if (bFire != 0)
        {
            eventFire(1f);
        }
    }

    // ---- elbow -----------------------------------------------------------------------------------------------

    /// <summary>s_ElbowShooting: the attacker's strike (rule 8); the labels choose the standing or ground clip.</summary>
    [Uc("SBase.SPlayerController.s_ElbowShooting")]
    public class s_ElbowShooting : UcActorState, IPlayerMove
    {
        public static readonly s_ElbowShooting Instance = new(UcMeta.s_ElbowShooting, null);

        private readonly int _crouchedPc;

        protected s_ElbowShooting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
            _crouchedPc = -1;
            foreach ((string name, int pc) in info.LabelPcs)
            {
                if (name == "EnemyIsCrouched")
                {
                    _crouchedPc = pc;
                }
            }
        }

        [Uc("SBase.SPlayerController.s_ElbowShooting.BeginState", Sha1 = "fe1958ae73572b3ee1d171d149acfb90527e59e2")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShooting.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.EteintLaser();
            if (!pc.IsRemotePlayer)
            {
                pc.iDontUpdatePadInput = 1;
            }

            if (pc.Role == ENetRole.ROLE_Authority && pc.Pawn is { } pawn)
            {
                pawn.PlaySound(pc.MeleeSound, ESoundSlot.SLOT_None, null, false, K.MeleeSoundRadius);
            }
        }

        [Uc("SBase.SPlayerController.s_ElbowShooting.EndState", Sha1 = "0d429e50f4a0ce0aaa18222d5066d7ed1e2bdc90")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShooting.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IsRemotePlayer)
            {
                pc.iDontUpdatePadInput = 0;
            }

            pc.RallumeLaserEteint();
        }

        /// <summary>The attack channel's end returns the owner to its waiting state.</summary>
        [Uc("SBase.SPlayerController.s_ElbowShooting.AnimEnd", Sha1 = "5f13774e85f42f979474536dafe4b16cf6b685e6")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShooting.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == K.AttackChannel && !pc.IsRemotePlayer)
            {
                pc.GotoWaitingStateCombat();
            }
        }

        /// <summary>A spy's camera turns; a merc's view turns at the strike rate.</summary>
        [Uc("SBase.SPlayerController.s_ElbowShooting.PlayerMove", Sha1 = "0264b3e9e239fabb64572df43132339879ac7468")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShooting.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is SPAWNDEFENSE)
            {
                self.eventUpdateRotation(DeltaTime, K.ElbowRotationPitch);
            }
            else
            {
                self.UpdateCamRotation(DeltaTime);
            }

            if (self.Pawn is { } pawn)
            {
                pawn.Acceleration = FVector.Zero;
            }

            self.SendMove(DeltaTime, FVector.Zero);
        }

        /// <summary>The labels (owner): standing plays the strike (and the merc's rifle butt), crouched the ground strike.</summary>
        [Uc("SBase.SPlayerController.s_ElbowShooting@code", Sha1 = "6e79e9fa40ad63397b33336842511b0af27df52b")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShooting@code", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var pc = (SPlayerController)self;
            bool crouched = frame.Pc == _crouchedPc;
            frame.Pc = LatentFrame.NoCode;
            if (pc.IsRemotePlayer || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (crouched)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_ELBOW_ATTACK_GROUND, false);
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_ELBOW_ATTACK, false);
            if (pawn is SPAWNDEFENSE)
            {
                pawn.MyWeapon?.GotoState(CNAME_s_ElbowShocking);
            }
        }
    }

    /// <summary>s_ElbowShot_GoBackward: the elbow victim's knock-back (rule 12).</summary>
    [Uc("SBase.SPlayerController.s_ElbowShot_GoBackward")]
    public class s_ElbowShot_GoBackward : UcActorState, IPlayerMove, IFire, IAltFire, IGadgetQuickChoice, IGadLaserSwitch,
        IGadTorcheSwitch, ISwitchHeadset, ISwitchROF
    {
        public static readonly s_ElbowShot_GoBackward Instance = new(UcMeta.s_ElbowShot_GoBackward, null);

        protected s_ElbowShot_GoBackward(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ElbowShot_GoBackward.BeginState", Sha1 = "e6d1302e978d95af39cc381adff32777646de0ac")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShot_GoBackward.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.EndStickyShockNow();
            pc.LeaveBackToWall(pawn);
            if (pc.ActorPassedByMirrorState is SBasePawn attacker)
            {
                pawn.m_AttackedBy = attacker;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_ELBOW_ATTACKED_GOBACK, false);
            (pawn.MyWeapon as SWeaponDefense)?.AnimateBlast();
            pc.KnockBackBlur();
            pc.bNoReticule = true;
            pc.EteintLaser();
            if (pawn is SPAWNDEFENSE)
            {
                pc.bElbowBackward = true;
            }
        }

        [Uc("SBase.SPlayerController.s_ElbowShot_GoBackward.EndState", Sha1 = "6823a9efe7f96eaee22c81c1b61e4db5fb4970c5")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShot_GoBackward.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bNoReticule = false;
            pc.RallumeLaserEteint();
            pc.bElbowBackward = false;
        }

        [Uc("SBase.SPlayerController.s_ElbowShot_GoBackward.AnimEnd", Sha1 = "c9b26faca83c64d7236fa1f970b9a1a566f140cd")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShot_GoBackward.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && !pc.IsRemotePlayer)
            {
                pc.GotoWaitingStateCombat();
                pc.FireIfHeld();
            }
        }

        [Uc("SBase.SPlayerController.s_ElbowShot_GoBackward.PlayerMove", Sha1 = "f263bde7336e9c35bb9b351e4ad156f730f389ad")]
        [PortOf("uc:SBase.SPlayerController:s_ElbowShot_GoBackward.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.LockedViewMove(DeltaTime, false);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void GadgetQuickChoice(SPlayerController self, int i)
        {
        }

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        public void SwitchROF(SPlayerController self)
        {
        }
    }

    // ---- charge ----------------------------------------------------------------------------------------------

    /// <summary>s_PlayerCharging: the merc's 1 s charge (rules 14 to 19).</summary>
    [Uc("SBase.SPlayerController.s_PlayerCharging")]
    public class s_PlayerCharging : UcActorState, IPlayerTick, IPlayerMove, INotifyBump, INotifyHitWall, IMyFalling, IGoInSmoke,
        ICanActivateLaser, ICanBeDropAttacked, IFire, IAltFire
    {
        public static readonly s_PlayerCharging Instance = new(UcMeta.s_PlayerCharging, null);

        protected s_PlayerCharging(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging.BeginState", Sha1 = "af402927e0571c331f6b4ff92320b23ab2887bfa")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            float now = pc.Level.TimeSeconds;
            pc.bIsCharging = true;
            pc.fGrabDate = now;
            pc.fRespawnDate = now + pc.FRand() * K.ChargePulseRandom + K.ChargePulseBase;
            pc.bNoReticule = true;
            pc.EteintLaser();
            pc.fChargeTempsRotationEnnemi = now + pc.fChargeDuration;
            if (!pc.IsRemotePlayer)
            {
                pc.bFinDeChargeEnCours = false;
                pc.SetTimer(K.ChargeFirstPhase, false);
            }

            new FRotator(0, pc.Rotation.Yaw, 0).GetAxes(out FVector x, out FVector y, out _);
            pc.vNormalFence = (x * pc.aForward + y * pc.aStrafe).SafeNormal();
            pc.TargetPawn = null;
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging.EndState", Sha1 = "057060d64aa6bc7b1287f6deb24f4ab6edbee001")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Estimated, "EndState body not specced: charge flag off, reticle and laser back, the charge time for the cooldowns")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bIsCharging = false;
            pc.bNoReticule = false;
            pc.RallumeLaserEteint();
            pc.fLastChargingTime = pc.Level.TimeSeconds;
            pc.SetTimer(0f, false);
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging.Timer", Sha1 = "1dfc10208feb1fc5e7a5baeb9e8172a4600b9206")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.Timer", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IsRemotePlayer)
            {
                pc.FinDeCharge();
            }
        }

        /// <summary>Rule 17: the global frame, the target lock, the camera tilt and rumble, the HUD pulse.</summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging.PlayerTick", Sha1 = "60b569d6ba399dbc91dd639865ed94302270eb30")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.PlayerTick", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            float before = pc.Level.TimeSeconds - DeltaTime;
            ComputeTargetDistance(pc);
            pc.PlayerTick(DeltaTime);
            float now = pc.Level.TimeSeconds;
            if (now - pc.fGrabDate > K.ChargeTiltPeriod)
            {
                pc.fGrabDate = now;
                CombatFx.Notify(pc, CombatFx.Vibration_Charge);
            }

            if (before < pc.fRespawnDate && now >= pc.fRespawnDate)
            {
                pc.SetHudPerturbationLength(K.ChargePulseLength);
            }
        }

        /// <summary>While charging the target distance is the charge target lock.</summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging.ComputeTargetDistance", Sha1 = "7d01aa90a426ebeca1aca0e173a920c354d0fd4a")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.ComputeTargetDistance", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public static void ComputeTargetDistance(SPlayerController self) => self.FindTargetToCharge();

        /// <summary>
        /// Rule 16: the heading blends toward the aim (or the target) with the weights 9 or 14 (t + 1) against
        /// 50 dt; pawn and view follow; the speed ramps from 400 to 770.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging.PlayerMove", Sha1 = "729a42abc141b9364a2933612de2568f69b1b2c3")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            float now = self.Level.TimeSeconds;
            float k = DeltaTime * K.ChargeSteerRate;
            FVector aim = self.MyDesiredRotation.Vector();
            FVector a;
            if (self.TargetPawn is { } target)
            {
                float c = K.ChargeTargetWeight * (self.fChargeDuration - (self.fChargeTempsRotationEnnemi - now) + 1f);
                a = (aim * c + (target.Location - pawn.Location).SafeNormal() * k) / (k + c);
            }
            else
            {
                float w = self.fChargeTempsRotationPersonne;
                a = (aim * w + self.vNormalFence * k) / (k + w);
            }

            a.Z = 0f;
            self.vNormalFence = a;
            FRotator heading = a.Rotation();
            self.MyDesiredRotation = heading;
            pawn.Rotation = new FRotator(0, heading.Yaw, 0);
            self.Rotation = new FRotator(self.Rotation.Pitch, heading.Yaw, self.Rotation.Roll);
            pawn.GroundSpeed = SvmMath.Clamp(pawn.GroundSpeed + DeltaTime * K.ChargeSpeedRamp, K.ChargeSpeedMin, K.ChargeSpeedMax);
            self.SendMove(DeltaTime, a * (pawn.GroundSpeed * K.ChargeAccelScale));
        }

        /// <summary>
        /// Rule 19 (owner): a spy in front is charged (or, shocking, shocks the merc); a merc in front shares the hit;
        /// anything else is a wall (a gameplay object takes 35).
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging.NotifyBump", Sha1 = "10d704c04106afd7b50f0d7ae95dcc4567251dc8")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.NotifyBump", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.IsRemotePlayer || Other is Mover || self.SBPawn is not { } me)
            {
                return false;
            }

            bool inFront = ((Other.Location - me.Location) | me.Rotation.Vector()) > 0f;
            if (Other is SBasePawn other && inFront)
            {
                self.TurnToward(me, other);
                if (other.bStickyShocking)
                {
                    self.eventChangeState(MNAME_s_PlayerCharging_HitStickyShocked);
                    self.DamagePawn_C2S(me, (int)self.iChargeDamage, other, me.Location, me.Location - other.Location,
                        SDamageType.SDTStickyShocker, 0, K.MeleeRefillable);
                }
                else if (other is SPawnAttaque)
                {
                    self.ChargeDamage(other, me, SDamageType.SDamage_Charged);
                    self.eventChangeState(MNAME_s_PlayerCharging_HitEnemy);
                }
                else
                {
                    self.eventChangeState(MNAME_s_PlayerCharging_HitFriend);
                    self.ChargeDamage(other, me, SDamageType.SDamage_Charged);
                    self.ChargeDamage(me, other, SDamageType.SDamage_Charged);
                }

                return false;
            }

            if (Other is SBasicGamePlayObject gpo && inFront)
            {
                self.DestroyGPO(gpo, (int)self.iChargeDamage, SDamageType.SDamage_Charged);
            }

            self.eventChangeState(MNAME_s_PlayerCharging_HitWall);
            return false;
        }

        /// <summary>A wall (owner): a gameplay object takes 35; rumble, a 2 s HUD perturbation, the wall hit.</summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging.NotifyHitWall", Sha1 = "915515f1a75e71cfad800923222872fd43388323")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.NotifyHitWall", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (self.IsRemotePlayer)
            {
                return false;
            }

            if (Wall is SBasicGamePlayObject gpo)
            {
                self.DestroyGPO(gpo, (int)self.iChargeDamage, SDamageType.SDamage_Charged);
            }

            CombatFx.Notify(self, CombatFx.Vibration_Charge);
            self.SetHudPerturbationLength(K.ChargeWallPerturbation);
            self.eventChangeState(MNAME_s_PlayerCharging_HitWall);
            return false;
        }

        /// <summary>Falling ends the weapon charge and starts a normal fall.</summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging.MyFalling", Sha1 = "ae0622ce4e8573d800b37cd3fed74c998f395e11")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.MyFalling", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void MyFalling(SPlayerController self, float LeftTime)
        {
            self.SBPawn?.MyWeapon?.GotoState(CNAME_s_Selected);
            self.MyFalling(LeftTime);
        }

        /// <summary>Smoke starts the ending of the charge.</summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging.GoInSmoke", Sha1 = "1f44cf18639e17266a0c9e0ed20cee781c56cbdf")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.GoInSmoke", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void GoInSmoke(SPlayerController self, float tCough, float tFaint, float tHud, float blur)
        {
            self.GoInSmoke(tCough, tFaint, tHud, blur);
            if (!self.bFinDeChargeEnCours && !self.IsRemotePlayer)
            {
                self.FinDeCharge();
            }
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.CanActivateLaser", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_PlayerCharging.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging.CanBeDropAttacked", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }

    /// <summary>s_PlayerCharging_Hit: the hit states' base (rule 20).</summary>
    [Uc("SBase.SPlayerController.s_PlayerCharging_Hit")]
    public class s_PlayerCharging_Hit : UcActorState, IPlayerMove, ICanBeGrabbed, ICanActivateLaser, ICheckshock, IFire, IAltFire
    {
        public static readonly s_PlayerCharging_Hit Instance = new(UcMeta.s_PlayerCharging_Hit, null);

        protected s_PlayerCharging_Hit(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_Hit.BeginState", Sha1 = "435bf5d70627dabefb08dd66971e3a60c931a9b6")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_Hit.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bNoReticule = true;
            if (pc.SBPawn is { } pawn)
            {
                pawn.bCanBeGrabbed = true;
            }

            pc.EteintLaser();
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_Hit.EndState", Sha1 = "4163112ad046e2b3a629ce76010ff698d07960d5")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_Hit.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Estimated, "EndState body not specced: the reticle and laser back")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bNoReticule = false;
            pc.RallumeLaserEteint();
        }

        /// <summary>The bump clip's end: armed walking, fire if held.</summary>
        [Uc("SBase.SPlayerController.s_PlayerCharging_Hit.AnimEnd", Sha1 = "8b1648018f1c2b2aff18d3f14d19d353f2786daf")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_Hit.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.IsRemotePlayer || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
            pc.eventChangeState(CNAME_s_PlayerWalkingSt2H);
            pc.FireIfHeld();
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_Hit.PlayerMove", Sha1 = "23b585620665ee3c57299a73a2d2eae7a601d407")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_Hit.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.LockedViewMove(DeltaTime, false);

        [Uc("SBase.SPlayerController.s_PlayerCharging_Hit.CanBeGrabbed", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_Hit.CanBeGrabbed", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public virtual bool CanBeGrabbed(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerCharging_Hit.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_Hit.CanActivateLaser", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public bool CanActivateLaser(SPlayerController self) => false;

        public void Checkshock(SPlayerController self, int PillTag, bool bPlayAnim) => self.ChargeHitCheckshock(PillTag);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        /// <summary>A hit's clip, weapon state and sound (HitEnemy, HitFriend, HitWall).</summary>
        protected static void HitBegin(SPlayerController pc, int Action, Name WeaponState, bool bElbowSoundWhenHolstered)
        {
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, Action, false);
            pawn.MyWeapon?.GotoState(WeaponState);
            if (pc.Role == ENetRole.ROLE_Authority)
            {
                pawn.PlaySound(bElbowSoundWhenHolstered ? pc.MeleeSound : MSND_RifleButt, ESoundSlot.SLOT_None, null, false, K.MeleeSoundRadius);
            }
        }
    }

    [Uc("SBase.SPlayerController.s_PlayerCharging_HitEnemy")]
    public class s_PlayerCharging_HitEnemy : s_PlayerCharging_Hit
    {
        public static new readonly s_PlayerCharging_HitEnemy Instance = new(UcMeta.s_PlayerCharging_HitEnemy, s_PlayerCharging_Hit.Instance);

        protected s_PlayerCharging_HitEnemy(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitEnemy.BeginState", Sha1 = "a744f97ba2b2c6ac4889e3689cd0c1cb7397f276")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitEnemy.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            HitBegin((SPlayerController)self, SBasePawn.SA_BUMP_ENEMY, CNAME_s_ElbowShocking, true);
        }
    }

    [Uc("SBase.SPlayerController.s_PlayerCharging_HitFriend")]
    public class s_PlayerCharging_HitFriend : s_PlayerCharging_Hit
    {
        public static new readonly s_PlayerCharging_HitFriend Instance = new(UcMeta.s_PlayerCharging_HitFriend, s_PlayerCharging_Hit.Instance);

        protected s_PlayerCharging_HitFriend(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitFriend.BeginState", Sha1 = "1e2cdb1812d39090a9b594195b7e69c25c484cbb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitFriend.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            HitBegin((SPlayerController)self, SBasePawn.SA_BUMP_WALL, MNAME_s_Charging_Bump, true);
        }
    }

    [Uc("SBase.SPlayerController.s_PlayerCharging_HitWall")]
    public class s_PlayerCharging_HitWall : s_PlayerCharging_Hit
    {
        public static new readonly s_PlayerCharging_HitWall Instance = new(UcMeta.s_PlayerCharging_HitWall, s_PlayerCharging_Hit.Instance);

        protected s_PlayerCharging_HitWall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitWall.BeginState", Sha1 = "36141ef013d14a051a3358c900dde69211cf9464")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitWall.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            HitBegin((SPlayerController)self, SBasePawn.SA_BUMP_WALL, MNAME_s_Charging_Bump, false);
        }
    }

    /// <summary>s_PlayerCharging_HitStickyShocked: the merc charged a shocked pawn and shocked himself (3.5 s).</summary>
    [Uc("SBase.SPlayerController.s_PlayerCharging_HitStickyShocked")]
    public class s_PlayerCharging_HitStickyShocked : s_PlayerCharging_Hit, IPlayAnimDmg
    {
        public static new readonly s_PlayerCharging_HitStickyShocked Instance =
            new(UcMeta.s_PlayerCharging_HitStickyShocked, s_PlayerCharging_Hit.Instance);

        protected s_PlayerCharging_HitStickyShocked(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitStickyShocked.BeginState", Sha1 = "dcd42a98c2ad47d641f7bf1e967993340ec565da")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var pc = (SPlayerController)self;
            pc.TimeShock = 0f;
            if (!pc.IsRemotePlayer)
            {
                pc.SetTimer(K.ChargeSelfShockRecovery, false);
            }

            pc.SBPawn?.MyWeapon?.GotoState(CNAME_s_Dmg_Electric);
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitStickyShocked.Timer", Sha1 = "d27076efd3cc51c5bfa8938df42c721132095409")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.Timer", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn)
            {
                pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
            }

            pc.eventChangeState(CNAME_s_PlayerWalkingSt2H);
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitStickyShocked.AnimEnd", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void AnimEnd(Actor self, int Channel)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitStickyShocked.PlayAnimDmg", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.PlayAnimDmg", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void PlayAnimDmg(SPlayerController self, int PillTag, FVector Momentum)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerCharging_HitStickyShocked.CanBeGrabbed", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerCharging_HitStickyShocked.CanBeGrabbed", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override bool CanBeGrabbed(SPlayerController self) => false;
    }

    /// <summary>s_Charge_GoBackward: the charge or berserk victim's knock-back, then a 3 s lie down (rule 22).</summary>
    [Uc("SBase.SPlayerController.s_Charge_GoBackward")]
    public class s_Charge_GoBackward : UcActorState, IPlayerMove, IMyFalling, IFire, IAltFire, IGadgetQuickChoice, IGadLaserSwitch,
        IGadTorcheSwitch, ISwitchHeadset, ISwitchROF, ICanActivateCloak
    {
        public static readonly s_Charge_GoBackward Instance = new(UcMeta.s_Charge_GoBackward, null);

        protected s_Charge_GoBackward(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Charge_GoBackward.BeginState", Sha1 = "4eaff62e7aaa7a863c353ec2798d96f590b9e46d")]
        [PortOf("uc:SBase.SPlayerController:s_Charge_GoBackward.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.Goggle?.ResetVisionMode(true);
            if (pawn is SPAWNDEFENSE && pawn.Health > 0 && !pc.IsRemotePlayer)
            {
                CombatFx.Notify(pc, CombatFx.HeadCamera, K.GrabbedCameraX, 0f, K.GrabbedCameraZ);
                pc.SetHudPerturbationLength(K.ChargeVictimPerturbation);
                CombatFx.Notify(pc, CombatFx.OverlayFade, 0f, 255f, K.ChargeVictimOverlay);
                CombatFx.Notify(pc, CombatFx.WeirdEffect, 1f);
                if (pawn.MyWeapon is SWeaponDefense hidden)
                {
                    hidden.bDrawWeapon = false;
                }
            }

            pc.EndStickyShockNow();
            pc.LeaveBackToWall(pawn);
            if (pc.ActorPassedByMirrorState is SBasePawn attacker)
            {
                pawn.m_AttackedBy = attacker;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_CHARGE_ATTACKED_GOBACK, false);
            (pawn.MyWeapon as SWeaponDefense)?.AnimateBlast();
            pc.bNoReticule = true;
            pc.KnockBackBlur();
            pc.EteintLaser();
        }

        [Uc("SBase.SPlayerController.s_Charge_GoBackward.EndState", Sha1 = "789179cefeb7caddba874da91e3ebd2dff612a40")]
        [PortOf("uc:SBase.SPlayerController:s_Charge_GoBackward.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Estimated, "EndState body not specced: the reticle and laser back")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bNoReticule = false;
            pc.RallumeLaserEteint();
        }

        [Uc("SBase.SPlayerController.s_Charge_GoBackward.MyFalling", Sha1 = "0f3a37adb2de0514b9765eb2881d3d98326bea0f")]
        [PortOf("uc:SBase.SPlayerController:s_Charge_GoBackward.MyFalling", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void MyFalling(SPlayerController self, float LeftTime) => self.eventChangeState(MNAME_PlayerFallingKO);

        [Uc("SBase.SPlayerController.s_Charge_GoBackward.AnimEnd", Sha1 = "cd373b8e94608c44c26fc1237a51652c90d19438")]
        [PortOf("uc:SBase.SPlayerController:s_Charge_GoBackward.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0)
            {
                return;
            }

            pc.fLyingOnGroundDuration = pc.fKOAfterBerserkOrCharge;
            pc.eventChangeState(CNAME_s_LyingOnGround);
        }

        [Uc("SBase.SPlayerController.s_Charge_GoBackward.PlayerMove", Sha1 = "c70d3f5c602fc3526fe5e34600fa353ba773ee9a")]
        [PortOf("uc:SBase.SPlayerController:s_Charge_GoBackward.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.LockedViewMove(DeltaTime, self.SBPawn is SPawnAttaque);

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }

        public void GadgetQuickChoice(SPlayerController self, int i)
        {
        }

        public void GadLaserSwitch(SPlayerController self)
        {
        }

        public void GadTorcheSwitch(SPlayerController self)
        {
        }

        public void SwitchHeadset(SPlayerController self, int i)
        {
        }

        public void SwitchROF(SPlayerController self)
        {
        }

        public bool CanActivateCloak(SPlayerController self) => false;
    }

    // ---- berserk ---------------------------------------------------------------------------------------------

    /// <summary>s_Berserk360: the merc's spin (rules 23 to 26); the server hits the pawns it bumps.</summary>
    [Uc("SBase.SPlayerController.s_Berserk360")]
    public class s_Berserk360 : UcActorState, IPlayerMove, INotifyBump, INotifyHitWall, IMyFalling, ICanBeDropAttacked,
        ICanActivateLaser, IFire, IAltFire
    {
        public static readonly s_Berserk360 Instance = new(UcMeta.s_Berserk360, null);

        protected s_Berserk360(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Berserk360.BeginState", Sha1 = "3c2e2c10ac598a81f5d4d5d3ea35767299678340")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bNoReticule = true;
            pc.EteintLaser();
            if (!pc.IsRemotePlayer)
            {
                CombatFx.Notify(pc, CombatFx.CrossEyeBlur, 1f, 0f, K.ChargeVictimOverlay);
            }

            // Rule 31, on every machine that enters the state: the camera on 'B Head' (13, 0, 9), the behind view
            // with the pawn drawn, the rifle's view model hidden (the same calls as the merc's ladder camera).
            pc.MercLadderCamera();
        }

        [Uc("SBase.SPlayerController.s_Berserk360.EndState", Sha1 = "a898d725f2899fbfaa43e0c78da824e7889c963d")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (!pc.IsRemotePlayer)
            {
                pc.ResetMercCam();
                if (pc.SBPawn?.MyWeapon is SWeaponDefense rifle)
                {
                    rifle.bDrawWeapon = true;
                }

                pc.FireIfHeld();
            }

            pc.RallumeLaserEteint();
            pc.bNoReticule = false;
        }

        /// <summary>
        /// Rule 24 (server): a pawn bumped early in the spin and not already knocked back takes 35 (approximate
        /// pill), counted for an enemy, with the rifle butt sound and the owner's rumble.
        /// </summary>
        [Uc("SBase.SPlayerController.s_Berserk360.NotifyBump", Sha1 = "8117ddb9b15a91f5bf41a93f84b269dff848e0bd")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.NotifyBump", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public bool NotifyBump(SPlayerController self, Actor Other)
        {
            if (self.Role != ENetRole.ROLE_Authority || self.SBPawn is not { } me || Other is not SBasePawn victim ||
                victim.Controller is not SPlayerController vpc || vpc.IsInState(MNAME_s_Charge_GoBackward) ||
                !(self.CombatPorts.AnimFrame(me, 0) < K.BerserkHitWindow))
            {
                return false;
            }

            vpc.PawnTakeDamage((int)self.iChargeDamage, me, victim.Location, victim.Location - me.Location, SDamageType.SDamage_Charged,
                K.MeleeRefillable);
            if (SGameInfo.TeamIndexOf(vpc) != SGameInfo.TeamIndexOf(self) || self.IsMercDeathMatch)
            {
                IncStat(self, static s => s.Merc_BerserkHits++);
            }

            me.PlaySound(MSND_RifleButt, ESoundSlot.SLOT_None, null, false, K.MeleeSoundRadius);
            self.Client_Vibration_Charge_Obstacle();
            return false;
        }

        [Uc("SBase.SPlayerController.s_Berserk360.NotifyHitWall", Sha1 = "5dc541107287ea37554fcb1aeef1bcb0d1ab37a5")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.NotifyHitWall", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (self.IsRemotePlayer)
            {
                return false;
            }

            if (Wall is SBasicGamePlayObject gpo)
            {
                self.DestroyGPO(gpo, (int)self.iChargeDamage, SDamageType.SDamage_Charged);
            }

            CombatFx.Notify(self, CombatFx.Vibration_Charge);
            self.SetHudPerturbationLength(K.ChargeWallPerturbation);
            return false;
        }

        [Uc("SBase.SPlayerController.s_Berserk360.AnimEnd", Sha1 = "30d282aa38b06f67b83a9f6d7203fa61f532eb56")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.IsRemotePlayer || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WALK_ST_2H, false);
            pc.eventChangeState(CNAME_s_PlayerWalkingSt2H);
        }

        /// <summary>The aim follows the spinning pawn's yaw with the camera pitch; no acceleration.</summary>
        [Uc("SBase.SPlayerController.s_Berserk360.PlayerMove", Sha1 = "e556035526584a416f3abacce255275c15117267")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is { } pawn)
            {
                self.MyDesiredRotation = new FRotator(self.Rotation.Pitch, pawn.Rotation.Yaw, 0);
            }

            self.LockedViewMove(DeltaTime, false);
        }

        [Uc("SBase.SPlayerController.s_Berserk360.MyFalling", Sha1 = "e0e26eafdfe308bc5f59f103d2afc2a1d59d182b")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.MyFalling", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        [Provenance(ProvenanceKind.Estimated, "MyFalling body not specced: a normal fall")]
        public void MyFalling(SPlayerController self, float LeftTime) => self.MyFalling(LeftTime);

        [Uc("SBase.SPlayerController.s_Berserk360.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.CanBeDropAttacked", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_Berserk360.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Berserk360.CanActivateLaser", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-berserk-spin")]
        public bool CanActivateLaser(SPlayerController self) => false;

        public void Fire(SPlayerController self, float F)
        {
        }

        public void AltFire(SPlayerController self, float F)
        {
        }
    }
}
