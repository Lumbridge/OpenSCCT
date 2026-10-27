using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController states s_ThrowGoTo, s_ThrowReady, s_Throw, s_ThrowLeave and ThrowObject: throwing the
// carried hand item (a pickable gameplay object; the Disk Hunt disk while it is still in the hand). Entered from the
// unarmed resting states when the fire mode is asked with a hand item (DrawRequest). Written from
// spec/objectives/disk-flag.md (Throwing a hand item). The release frame is the clip notify AnimNotifyThrow
// (SBasePawn.BtwFireNotifies.cs); the throw itself is SGamePlayObject.Throw, a client-to-server function.
public partial class SPlayerController
{
    private static readonly Name CNAME_Throw = new("Throw");
    private static readonly Name CNAME_s_ThrowReady = new("s_ThrowReady");
    private static readonly Name CNAME_s_Throw = new("s_Throw");
    private static readonly Name CNAME_s_ThrowLeave = new("s_ThrowLeave");

    /// <summary>
    /// The release (owner's machine): the hand item flies along the pawn's forward and up axes, faster with the
    /// charge (1 to 4 times 250 uu/s per axis); the charge gauge clears.
    /// </summary>
    [Uc("SBase.SPlayerController.ThrowObject", Sha1 = "d860041a609edd759abc9b4de9c5af09da361f92")]
    [PortOf("uc:SBase.SPlayerController:ThrowObject", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
    public void ThrowObject()
    {
        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.HandItem is { } item)
        {
            pawn.Rotation.GetAxes(out FVector x, out _, out FVector z);
            float f = fThrowForce + 1f;
            FVector v = (x + z) * (f * f * MovementK.ThrowObjectSpeed);
            if (!GadgetNet.ToServer(item, this, new CombatCall(CNAME_Throw) { Target = item, Vector1 = v }))
            {
                item.eventThrow(this, v);
            }
        }

        CombatFx.Notify(this, CombatFx.SetBallisticValue, 0f);
    }

    /// <summary>The throw clip ended: the unarmed rest for the stance.</summary>
    private void ThrowToRest(SBasePawn pawn)
    {
        if (pawn.bIsCrouched)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
            eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
        }
        else
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
            eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
        }
    }

    /// <summary>The shared parts of the four throw states: no fire, no alternate fire, no laser.</summary>
    public abstract class s_Throw_Shared : s_PlayerWalkingWeapon, IFire, IAltFire
    {
        protected s_Throw_Shared(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        public virtual void Fire(SPlayerController self, float F)
        {
        }

        public virtual void AltFire(SPlayerController self, float F)
        {
        }

        public override bool CanActivateLaser(SPlayerController self) => false;
    }

    /// <summary>Raising the item: the spy's close camera, the raise clip, then ready.</summary>
    [Uc("SBase.SPlayerController.s_ThrowGoTo")]
    public class s_ThrowGoTo : s_Throw_Shared
    {
        public static new readonly s_ThrowGoTo Instance = new(UcMeta.s_ThrowGoTo, s_PlayerWalkingWeapon.Instance);

        protected s_ThrowGoTo(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ThrowGoTo.Fire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowGoTo.Fire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void Fire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_ThrowGoTo.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowGoTo.AltFire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_ThrowGoTo.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowGoTo.CanActivateLaser", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ThrowGoTo.PlayerMove", Sha1 = "ab4f5b08da45c3e1e9b18306dae74d3bb3b39da2")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowGoTo.PlayerMove", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            base.PlayerMove(self, DeltaTime);

            // The pawn turns toward the view while the item is raised or thrown.
            _ = self.UpdatePawnRotationMario(DeltaTime, MovementK.ThrowTurnForward, 0);
        }

        [Uc("SBase.SPlayerController.s_ThrowGoTo.BeginState", Sha1 = "e11e31d4c4d6b5466ad5fff57a402e41545165f7")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowGoTo.BeginState", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.SPawnType != PawnType.SPAWNDEFENSE)
            {
                pc.MovementCamera(pawn.bIsCrouched ? ECamMode.ECM_SecondPersonCr : ECamMode.ECM_SecondPerson);
            }

            pawn.ChangeEnumAction(SBasePawn.SA_THROW_ENTER);
            base.BeginState(self);
        }

        [Uc("SBase.SPlayerController.s_ThrowGoTo.AnimEnd", Sha1 = "ada3955737254eaed1b7b6ca33c92fac323fbd38")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowGoTo.AnimEnd", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel == 0)
            {
                ((SPlayerController)self).eventChangeState(CNAME_s_ThrowReady);
            }
        }
    }

    /// <summary>
    /// Holding the item up: a Fire press starts the charge, its release throws (s_Throw) with the force the hold
    /// gave; the spy's fire-mode key puts the item down again (s_ThrowLeave). A merc holding an item can be grabbed.
    /// </summary>
    [Uc("SBase.SPlayerController.s_ThrowReady")]
    public class s_ThrowReady : s_Throw_Shared, ICanBeGrabbed
    {
        public static new readonly s_ThrowReady Instance = new(UcMeta.s_ThrowReady, s_PlayerWalkingWeapon.Instance);

        protected s_ThrowReady(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ThrowReady.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowReady.AltFire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void AltFire(SPlayerController self, float F)
        {
        }

        /// <summary>The press starts the charge timer (once) and resets the force.</summary>
        [Uc("SBase.SPlayerController.s_ThrowReady.Fire", Sha1 = "d6458c5a4f410037bcfaa06621025e4fe37d5296")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowReady.Fire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void Fire(SPlayerController self, float F)
        {
            if (self.bIsInInventoryMode)
            {
                return;
            }

            if (self.TimerUSe == 0f)
            {
                self.TimerUSe = self.Level.TimeSeconds;
            }

            self.fThrowForce = 0f;
        }

        [Uc("SBase.SPlayerController.s_ThrowReady.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowReady.CanActivateLaser", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ThrowReady.CanBeGrabbed", Sha1 = "9ca447bdebf24bc1fe879d251877cf611c4265e3")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowReady.CanBeGrabbed", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public bool CanBeGrabbed(SPlayerController self) => self.SBPawn?.SPawnType == PawnType.SPAWNDEFENSE;

        [Uc("SBase.SPlayerController.s_ThrowReady.BeginState", Sha1 = "3f96633ce69077209f530b4a9a342c795762c356")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowReady.BeginState", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.SPawnType != PawnType.SPAWNOFFENSE)
            {
                pawn.bCanBeGrabbed = true;
            }

            pc.TimerUSe = 0f;
            pawn.ChangeEnumAction(SBasePawn.SA_THROW_LOOP);
            base.BeginState(self);
        }

        [Uc("SBase.SPlayerController.s_ThrowReady.EndState", Sha1 = "a106714bff5b5e170192cea8915724ec44bcaf47")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowReady.EndState", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void EndState(Actor self)
        {
            if (((SPlayerController)self).SBPawn is { } pawn && pawn.SPawnType != PawnType.SPAWNOFFENSE)
            {
                pawn.bCanBeGrabbed = false;
            }

            base.EndState(self);
        }

        /// <summary>
        /// The charge on the input machine: a tap under MinimumThrowObjectDelay throws at full force, a longer hold
        /// grows from 0 to 1 over MaximumThrowObjectDelay with the gauge; the release throws. Then the global tick.
        /// </summary>
        [Uc("SBase.SPlayerController.s_ThrowReady.PlayerTick", Sha1 = "0b22ad3d7c2295e87b5c35eb555022630d07c5eb")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowReady.PlayerTick", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pc.IAmOnClient() && pc.TimerUSe > 0f)
            {
                float held = pc.Level.TimeSeconds - pc.TimerUSe;
                if (held < pc.MinimumThrowObjectDelay)
                {
                    pc.fThrowForce = 1f;
                }
                else
                {
                    pc.fThrowForce = SvmMath.Min((held - pc.MinimumThrowObjectDelay) / pc.MaximumThrowObjectDelay, 1f);
                    CombatFx.Notify(pc, CombatFx.SetBallisticValue, pc.fThrowForce);
                }

                if (pc.bFire == 0)
                {
                    pc.TimerUSe = 0f;
                    CombatFx.Notify(pc, CombatFx.SetBallisticValue, 0f);
                    pc.eventChangeState(CNAME_s_Throw);
                }
            }

            if (pawn.SPawnType == PawnType.SPAWNOFFENSE && pc.bFireMode == 1 && !pawn.bStickyShocking)
            {
                pc.eventChangeState(CNAME_s_ThrowLeave);
            }

            pc.PlayerTick(DeltaTime);
        }
    }

    /// <summary>The throw clip (its AnimNotifyThrow releases the item), then the unarmed rest.</summary>
    [Uc("SBase.SPlayerController.s_Throw")]
    public class s_Throw : s_Throw_Shared
    {
        public static new readonly s_Throw Instance = new(UcMeta.s_Throw, s_PlayerWalkingWeapon.Instance);

        protected s_Throw(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Throw.Fire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_Throw.Fire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void Fire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_Throw.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_Throw.AltFire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_Throw.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Throw.CanActivateLaser", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_Throw.PlayerMove", Sha1 = "6103cd6a14466664faa97cecb438b27edf2cd0cb")]
        [PortOf("uc:SBase.SPlayerController:s_Throw.PlayerMove", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            base.PlayerMove(self, DeltaTime);

            // The pawn turns toward the view while the item is raised or thrown.
            _ = self.UpdatePawnRotationMario(DeltaTime, MovementK.ThrowTurnForward, 0);
        }

        [Uc("SBase.SPlayerController.s_Throw.BeginState", Sha1 = "950000375ece7c00f20e7f40e6f9feea422d4a9a")]
        [PortOf("uc:SBase.SPlayerController:s_Throw.BeginState", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void BeginState(Actor self)
        {
            ((SPlayerController)self).SBPawn?.ChangeEnumAction(SBasePawn.SA_THROW);
            base.BeginState(self);
        }

        [Uc("SBase.SPlayerController.s_Throw.AnimEnd", Sha1 = "6864f6515aea80789bfed7e9aed4827eab0c2331")]
        [PortOf("uc:SBase.SPlayerController:s_Throw.AnimEnd", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.ThrowToRest(pawn);
            }
        }
    }

    /// <summary>Lowering the item without throwing: the walking camera, the raise clip backward, then the rest.</summary>
    [Uc("SBase.SPlayerController.s_ThrowLeave")]
    public class s_ThrowLeave : s_Throw_Shared
    {
        public static new readonly s_ThrowLeave Instance = new(UcMeta.s_ThrowLeave, s_PlayerWalkingWeapon.Instance);

        protected s_ThrowLeave(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ThrowLeave.Fire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowLeave.Fire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void Fire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_ThrowLeave.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowLeave.AltFire", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_ThrowLeave.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowLeave.CanActivateLaser", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ThrowLeave.PlayerMove", Sha1 = "adc65f3475ddb49f9aec0ab02c01e8191be6af67")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowLeave.PlayerMove", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            base.PlayerMove(self, DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_ThrowLeave.BeginState", Sha1 = "48fa1c5c366f0cce2b7d26f8998d631ac2e6d4d2")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowLeave.BeginState", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_Walking);
            pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_THROW_LEAVE);
            base.BeginState(self);
        }

        [Uc("SBase.SPlayerController.s_ThrowLeave.AnimEnd", Sha1 = "5c63659f708cac472d374532a4a8121fcd38d247")]
        [PortOf("uc:SBase.SPlayerController:s_ThrowLeave.AnimEnd", Spec = "spec/objectives/disk-flag.md#throwing-a-hand-item")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.SBPawn is { } pawn)
            {
                pc.ThrowToRest(pawn);
            }
        }
    }
}
