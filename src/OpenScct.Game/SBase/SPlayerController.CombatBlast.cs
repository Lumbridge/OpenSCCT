using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Combat.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController blast states (spec/combat/blast-and-fall-damage.md rules 5 to 13): the flight after an
// explosion, the wall hit and its fall, the floor landing, the KO after a wall and the wake up. The blast start
// (ExplosionHit) and the damage handlers are in SPlayerController.Combat.cs.
public partial class SPlayerController
{
    private static readonly Name BNAME_s_BlastFallAfterObstacle = new("s_BlastFallAfterObstacle");
    private static readonly Name BNAME_s_BlastFloor = new("s_BlastFloor");
    private static readonly Name BNAME_s_BlastWakeUp = new("s_BlastWakeUp");
    private static readonly Name BNAME_s_KOAfterBlastFall = new("s_KOAfterBlastFall");
    private static readonly Name BNAME_s_SleepAfterKOBlastFall = new("s_SleepAfterKOBlastFall");

    /// <summary>The blast states' input: a spy's camera turns, no jump, no acceleration.</summary>
    private void BlastMove(float DeltaTime)
    {
        if (SBPawn is SPawnAttaque)
        {
            UpdateCamRotation(DeltaTime);
        }

        bPressedJump = false;
        if (Pawn is { } pawn)
        {
            pawn.Acceleration = FVector.Zero;
        }

        SendMove(DeltaTime, FVector.Zero);
    }

    /// <summary>A fall ends the blast: a dead pawn goes to s_Dead (not after the match).</summary>
    private void BlastDeadOrState(Name Alive)
    {
        if (Pawn is { Health: <= 0 })
        {
            if (!CombatInGameEnded)
            {
                eventChangeState(CNAME_s_Dead);
            }

            return;
        }

        eventChangeState(Alive);
    }

    /// <summary>s_Blast: the flight after an explosion (rules 5 to 8).</summary>
    [Uc("SBase.SPlayerController.s_Blast")]
    public class s_Blast : UcActorState, IPlayerMove, IMyNotifyLanded, IFire, IAltFire, IGadgetQuickChoice, IGadLaserSwitch,
        IGadTorcheSwitch, ISwitchHeadset, ISwitchROF, ITellClientServerWaitForFatalBlast
    {
        public static readonly s_Blast Instance = new(UcMeta.s_Blast, null);

        protected s_Blast(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The blast time, rumble and the end of an active shock on the owner.</summary>
        [Uc("SBase.SPlayerController.s_Blast.BeginState", Sha1 = "cc47459c3133a2357ca0e3eb9e34444b3627db4e")]
        [PortOf("uc:SBase.SPlayerController:s_Blast.BeginState", Spec = "spec/combat/blast-and-fall-damage.md#s_blast")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IsRemotePlayer)
            {
                return;
            }

            pc.fTimeOfBlast = pc.Level.TimeSeconds;
            CombatFx.Notify(pc, CombatFx.Vibration_Blast);
            pc.EndStickyShockNow();
        }

        /// <summary>A wall ahead (box probe 1.2 x the step, steep and facing the flight) ends in s_BlastFallAfterObstacle.</summary>
        [Uc("SBase.SPlayerController.s_Blast.PlayerMove", Sha1 = "e52dc33973e010e563959f24237a17a9f1bdb8a4")]
        [PortOf("uc:SBase.SPlayerController:s_Blast.PlayerMove", Spec = "spec/combat/blast-and-fall-damage.md#s_blast")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is { } pawn && TraceHitZones.CollisionOf(self) is { } world)
            {
                FVector end = pawn.Location + pawn.Velocity * (DeltaTime * K.BlastProbeScale);
                if (world.LineCheck(out FCheckResult hit, pawn, end, pawn.Location, K.BlastProbeExtent, ETraceFlags.TRACE_AllColliding))
                {
                    FVector n = hit.Normal;
                    FVector v = pawn.Velocity;
                    n.Z = 0f;
                    v.Z = 0f;
                    if (hit.Normal.Z < K.BlastObstacleNormalZ && (n | v) < K.BlastObstacleFacing && pawn is SBasePawn sp)
                    {
                        self.m_LastSpecialMoveTime = self.Level.TimeSeconds;
                        self.CombatPorts.ChangeEnumAction(sp, SBasePawn.SA_BLAST_OBSTACLE, false);
                        self.eventChangeState(BNAME_s_BlastFallAfterObstacle);
                        return;
                    }
                }
            }

            self.BlastMove(DeltaTime);
        }

        /// <summary>Landing after 0.3 s: the floor clip, then s_BlastFloor or s_Dead.</summary>
        [Uc("SBase.SPlayerController.s_Blast.MyNotifyLanded", Sha1 = "d98ff359cde99f5e680873d87992eee89b78b6cc")]
        [PortOf("uc:SBase.SPlayerController:s_Blast.MyNotifyLanded", Spec = "spec/combat/blast-and-fall-damage.md#s_blast")]
        public bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            if (self.Level.TimeSeconds - self.fTimeOfBlast < K.BlastLandingGrace)
            {
                return false;
            }

            bool result = self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (!self.IsRemotePlayer && self.SBPawn is { } pawn)
            {
                self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BLAST_FLOOR, false);
                self.BlastDeadOrState(BNAME_s_BlastFloor);
            }

            return result;
        }

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

        public void TellClientServerWaitForFatalBlast(SPlayerController self)
        {
        }
    }

    /// <summary>s_BlastFallAfterObstacle: the fall after a wall hit (rules 11 and 12).</summary>
    [Uc("SBase.SPlayerController.s_BlastFallAfterObstacle")]
    public class s_BlastFallAfterObstacle : UcActorState, IPlayerMove, IMyNotifyLanded, IFire, IAltFire, IGadgetQuickChoice,
        IGadLaserSwitch, IGadTorcheSwitch, ISwitchHeadset, ISwitchROF, ITellClientServerWaitForFatalBlast
    {
        public static readonly s_BlastFallAfterObstacle Instance = new(UcMeta.s_BlastFallAfterObstacle, null);

        protected s_BlastFallAfterObstacle(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Falling more than 4 s kills (the pawn's own SFell).</summary>
        [Uc("SBase.SPlayerController.s_BlastFallAfterObstacle.PlayerMove", Sha1 = "85ca6b011b6b513fa3f6aca94a4705fee5b9fe1c")]
        [PortOf("uc:SBase.SPlayerController:s_BlastFallAfterObstacle.PlayerMove", Spec = "spec/combat/blast-and-fall-damage.md#blast-into-an-obstacle")]
        public void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is { } pawn && self.Level.TimeSeconds - self.m_LastSpecialMoveTime > K.BlastObstacleFallLimit)
            {
                self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_DIE_FALLING, false);
                self.DamagePawn_C2S(pawn, pawn.Health, pawn, pawn.Location, new FVector(0f, 0f, -1f), SDamageType.SFell);
                self.eventChangeState(CNAME_s_Dead);
                return;
            }

            self.BlastMove(DeltaTime);
        }

        /// <summary>The landing: dead stays dead; else the fall damage, then the KO after a wall.</summary>
        [Uc("SBase.SPlayerController.s_BlastFallAfterObstacle.MyNotifyLanded", Sha1 = "caa7069a0280d7693cf3a54a6a1251902d45cabc")]
        [PortOf("uc:SBase.SPlayerController:s_BlastFallAfterObstacle.MyNotifyLanded", Spec = "spec/combat/blast-and-fall-damage.md#blast-into-an-obstacle")]
        public bool MyNotifyLanded(SPlayerController self, FVector HitNormal, float fTimeLeft, Actor? LandedOn)
        {
            bool result = self.MyNotifyLanded(HitNormal, fTimeLeft, LandedOn);
            if (self.IsRemotePlayer || self.SBPawn is not { } pawn)
            {
                return result;
            }

            if (pawn.Health <= 0)
            {
                self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_LAND_AFTER_BLAST_WALL, false);
                self.eventChangeState(CNAME_s_Dead);
                return result;
            }

            float h = self.fZMaxFalling - pawn.Location.Z;
            if (h > pawn.Fell_MinHeightForDmg)
            {
                int dmg = SvmMath.Trunc(CombatRules.FallDamage(h, pawn.Fell_MinHeightForDmg, pawn.Fell_MaxHeightForDmg,
                    pawn.ClassDefaults.Health));
                if (dmg >= pawn.Health)
                {
                    self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_DIE_FALLING, false);
                    self.DamagePawn_C2S(pawn, pawn.ClassDefaults.Health, pawn, pawn.Location, new FVector(0f, 0f, 1f), SDamageType.SFell);
                    self.fZMaxFalling = pawn.Location.Z;
                    self.eventChangeState(CNAME_s_Dead);
                    return result;
                }

                self.DamagePawn_C2S(pawn, dmg, pawn, pawn.Location, new FVector(0f, 0f, 1f), SDamageType.SFell);
                self.fZMaxFalling = pawn.Location.Z;
            }

            self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_LAND_AFTER_BLAST_WALL, false);
            self.eventChangeState(BNAME_s_KOAfterBlastFall);
            return result;
        }

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

        public void TellClientServerWaitForFatalBlast(SPlayerController self)
        {
        }
    }

    /// <summary>s_BlastFloor: lying after the landing; the wake-up clip follows (rule 9).</summary>
    [Uc("SBase.SPlayerController.s_BlastFloor")]
    public class s_BlastFloor : UcActorState, IPlayerMove, IFire, IAltFire, IGadgetQuickChoice, IGadLaserSwitch, IGadTorcheSwitch,
        ISwitchHeadset, ISwitchROF, ITellClientServerWaitForFatalBlast
    {
        public static readonly s_BlastFloor Instance = new(UcMeta.s_BlastFloor, null);

        protected s_BlastFloor(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BlastFloor.AnimEnd", Sha1 = "f0495eb106cdadc02f938f1f92870d36fe47dc89")]
        [PortOf("uc:SBase.SPlayerController:s_BlastFloor.AnimEnd", Spec = "spec/combat/blast-and-fall-damage.md#s_blast")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.IsRemotePlayer || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_BLAST_WAKEUP, false);
            pc.eventChangeState(BNAME_s_BlastWakeUp);
        }

        [Uc("SBase.SPlayerController.s_BlastFloor.PlayerMove", Sha1 = "7fbf68afb9019caff632fecf61173c34076ea364")]
        [PortOf("uc:SBase.SPlayerController:s_BlastFloor.PlayerMove", Spec = "spec/combat/blast-and-fall-damage.md#s_blast")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.BlastMove(DeltaTime);

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

        public void TellClientServerWaitForFatalBlast(SPlayerController self)
        {
        }
    }

    /// <summary>s_BlastWakeUp (inactive): the wake-up clip, then the waiting state (rule 10).</summary>
    [Uc("SBase.SPlayerController.s_BlastWakeUp")]
    public class s_BlastWakeUp : s_Inactive
    {
        public static new readonly s_BlastWakeUp Instance = new(UcMeta.s_BlastWakeUp, null);

        protected s_BlastWakeUp(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BlastWakeUp.AnimEnd", Sha1 = "3b9a4b4ec5370318874bdc54379d2a02fe55a713")]
        [PortOf("uc:SBase.SPlayerController:s_BlastWakeUp.AnimEnd", Spec = "spec/combat/blast-and-fall-damage.md#s_blast")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && !pc.IsRemotePlayer)
            {
                pc.GotoWaitingStateCombat();
            }
        }

        [Uc("SBase.SPlayerController.s_BlastWakeUp.PlayerMove", Sha1 = "1666acd7e5844356276ed268866bba5ce2ef7e3a")]
        [PortOf("uc:SBase.SPlayerController:s_BlastWakeUp.PlayerMove", Spec = "spec/combat/blast-and-fall-damage.md#s_blast")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.InactiveMove(DeltaTime);
    }

    /// <summary>s_KOAfterBlastFall (inactive): the wall landing clip, then the sleep (rule 13).</summary>
    [Uc("SBase.SPlayerController.s_KOAfterBlastFall")]
    public class s_KOAfterBlastFall : s_Inactive
    {
        public static new readonly s_KOAfterBlastFall Instance = new(UcMeta.s_KOAfterBlastFall, null);

        protected s_KOAfterBlastFall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_KOAfterBlastFall.AnimEnd", Sha1 = "56b5b6b3e36655eee7100b9b2029b28d7f9f7886")]
        [PortOf("uc:SBase.SPlayerController:s_KOAfterBlastFall.AnimEnd", Spec = "spec/combat/blast-and-fall-damage.md#blast-into-an-obstacle")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.IsRemotePlayer)
            {
                return;
            }

            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
            pc.eventChangeState(BNAME_s_SleepAfterKOBlastFall);
        }
    }

    /// <summary>s_SleepAfterKOBlastFall (inactive): 3 s on the ground, then the wake up (rule 13).</summary>
    [Uc("SBase.SPlayerController.s_SleepAfterKOBlastFall")]
    public class s_SleepAfterKOBlastFall : s_Inactive
    {
        public static new readonly s_SleepAfterKOBlastFall Instance = new(UcMeta.s_SleepAfterKOBlastFall, null);

        protected s_SleepAfterKOBlastFall(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_SleepAfterKOBlastFall.PlayerMove", Sha1 = "cf866b91187771570a38a4e685b03915641babc7")]
        [PortOf("uc:SBase.SPlayerController:s_SleepAfterKOBlastFall.PlayerMove", Spec = "spec/combat/blast-and-fall-damage.md#blast-into-an-obstacle")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.InactiveMove(DeltaTime);
            if (self.IsRemotePlayer || self.SBPawn is not { } pawn ||
                !(self.Level.TimeSeconds - self.m_LastSpecialMoveTime > K.BlastObstacleSleep))
            {
                return;
            }

            self.CombatPorts.ChangeEnumAction(pawn, SBasePawn.SA_WAKE_AFTER_BLAST_WALL, false);
            self.eventChangeState(BNAME_s_BlastWakeUp);
        }
    }
}
