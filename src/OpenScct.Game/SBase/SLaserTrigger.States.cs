using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SLaserTrigger states (spec/devices/laser-trigger.md#states): s_Active (watching, the auto state), s_Inactive
// (switched off), s_LockedByChaff (broken by chaff or a sticky shot) and s_Absent (left out by SpawnPerCent).
public partial class SLaserTrigger
{
    /// <summary>Watching: the beam shows and CheckPresence runs every fCheckFrequency seconds, on every machine.</summary>
    [Uc("SBase.SLaserTrigger.s_Active")]
    public class s_Active : UcActorState
    {
        public static readonly s_Active Instance = new(UcMeta.s_Active, null);

        protected s_Active(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// A hidden tripwire goes absent (the rest still runs); the server shows the beam; the check timer starts at
        /// the last period CheckPresence chose; the tripwire is mended.
        /// </summary>
        [Uc("SBase.SLaserTrigger.s_Active.BeginState", Sha1 = "2df7652b5d6002f4b91d563dc2fccaea148d9afa")]
        [PortOf("uc:SBase.SLaserTrigger:s_Active.BeginState", Spec = "spec/devices/laser-trigger.md#states")]
        public override void BeginState(Actor self)
        {
            var laser = (SLaserTrigger)self;
            if (laser.bHidden)
            {
                laser.GotoState(NAME_s_Absent);
            }

            if (laser.Role == ENetRole.ROLE_Authority)
            {
                laser.HideLaser(false);
            }

            laser.SetTimer(laser.fCheckFrequency, true);
            laser.bIsBroken = false;
        }

        [Uc("SBase.SLaserTrigger.s_Active.Timer", Sha1 = "e96282ba2b500f0779588c330f8a8b88ebe5a20b")]
        [PortOf("uc:SBase.SLaserTrigger:s_Active.Timer", Spec = "spec/devices/laser-trigger.md#states")]
        public override void Timer(Actor self) => ((SLaserTrigger)self).CheckPresence();

        /// <summary>A toggle or untrigger-only tripwire is switched off by Trigger; a TriggerControl one is not.</summary>
        [Uc("SBase.SLaserTrigger.s_Active.Trigger", Sha1 = "3cbb981588acb026aa68cb917f7973223d942e7a")]
        [PortOf("uc:SBase.SLaserTrigger:s_Active.Trigger", Spec = "spec/devices/laser-trigger.md#states")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            if (((SLaserTrigger)self).TriggerMethode != TriggerType.TriggerControl)
            {
                self.GotoState(NAME_s_Inactive);
            }
        }

        [Uc("SBase.SLaserTrigger.s_Active.UnTrigger", Sha1 = "993d55c241053069baaecb7dcbaa09b92d88c41c")]
        [PortOf("uc:SBase.SLaserTrigger:s_Active.UnTrigger", Spec = "spec/devices/laser-trigger.md#states")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.GotoState(NAME_s_Inactive);

        [Uc("SBase.SLaserTrigger.s_Active.Tick", Sha1 = "24afc2bec8520f2c8f75fd4c5e48c860bc833a4b")]
        [PortOf("uc:SBase.SLaserTrigger:s_Active.Tick", Spec = "spec/devices/laser-trigger.md#states")]
        public override void Tick(Actor self, float DeltaTime) => ((SLaserTrigger)self).SyncLaserAndSparks();
    }

    /// <summary>Switched off: no beam, no checks (the looping timer runs the global Timer).</summary>
    [Uc("SBase.SLaserTrigger.s_Inactive")]
    public class s_Inactive : UcActorState
    {
        public static readonly s_Inactive Instance = new(UcMeta.s_Inactive, null);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SLaserTrigger.s_Inactive.BeginState", Sha1 = "7c1dd15e4d4683a4ad11d05af4e10512f40aa445")]
        [PortOf("uc:SBase.SLaserTrigger:s_Inactive.BeginState", Spec = "spec/devices/laser-trigger.md#states")]
        public override void BeginState(Actor self)
        {
            var laser = (SLaserTrigger)self;
            laser.HideLaser(true);
            laser.bIsBroken = false;
        }

        [Uc("SBase.SLaserTrigger.s_Inactive.Trigger", Sha1 = "1849bca198c86717b6230df735cb5c200b9fbd17")]
        [PortOf("uc:SBase.SLaserTrigger:s_Inactive.Trigger", Spec = "spec/devices/laser-trigger.md#states")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.GotoState(NAME_s_Active);

        /// <summary>A toggle or untrigger-only tripwire is switched back on by UnTrigger; a TriggerControl one is not.</summary>
        [Uc("SBase.SLaserTrigger.s_Inactive.UnTrigger", Sha1 = "0126472682141f707c26672a33494d1346bcda4e")]
        [PortOf("uc:SBase.SLaserTrigger:s_Inactive.UnTrigger", Spec = "spec/devices/laser-trigger.md#states")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            if (((SLaserTrigger)self).TriggerMethode != TriggerType.TriggerControl)
            {
                self.GotoState(NAME_s_Active);
            }
        }

        [Uc("SBase.SLaserTrigger.s_Inactive.Tick", Sha1 = "24afc2bec8520f2c8f75fd4c5e48c860bc833a4b")]
        [PortOf("uc:SBase.SLaserTrigger:s_Inactive.Tick", Spec = "spec/devices/laser-trigger.md#states")]
        public override void Tick(Actor self, float DeltaTime) => ((SLaserTrigger)self).SyncLaserAndSparks();
    }

    /// <summary>
    /// Broken by chaff or a sticky shot for StickyNeutralizationTime: no beam, sparks, a bang; Trigger and UnTrigger
    /// only change the state it will return to.
    /// </summary>
    [Uc("SBase.SLaserTrigger.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff, ISetLockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, SBasicGamePlayObject.s_LockedByChaff.Instance);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A new lock restarts the full StickyNeutralizationTime.</summary>
        [Uc("SBase.SLaserTrigger.s_LockedByChaff.SetLockedByChaff", Sha1 = "6a63fb16a8f5e9d4b3515aa74dad68b1da0cb263")]
        [PortOf("uc:SBase.SLaserTrigger:s_LockedByChaff.SetLockedByChaff", Spec = "spec/devices/laser-trigger.md#states")]
        public new void SetLockedByChaff(SBasicGamePlayObject self, float Length) => self.SetTimer(self.StickyNeutralizationTime, false);

        [Uc("SBase.SLaserTrigger.s_LockedByChaff.BeginState", Sha1 = "65fcd390ae306e53ba702942c6376f1aed0f2d4a")]
        [PortOf("uc:SBase.SLaserTrigger:s_LockedByChaff.BeginState", Spec = "spec/devices/laser-trigger.md#states")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var laser = (SLaserTrigger)self;
            laser.HideLaser(true);
            laser.bIsBroken = true;
            laser.PlaySound(SensorK.ChaffExplosionSound, ESoundSlot.SLOT_Misc, 1f, false, SensorK.ChaffExplosionRadius);
        }

        /// <summary>Trigger flips the remembered state (s_Active to s_Inactive only for a non-TriggerControl tripwire).</summary>
        [Uc("SBase.SLaserTrigger.s_LockedByChaff.Trigger", Sha1 = "b8cd0664b24e17772dfbef7145406fb465b72360")]
        [PortOf("uc:SBase.SLaserTrigger:s_LockedByChaff.Trigger", Spec = "spec/devices/laser-trigger.md#states")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var laser = (SLaserTrigger)self;
            if (laser.ChaffLockPreviousState == NAME_s_Inactive)
            {
                laser.ChaffLockPreviousState = NAME_s_Active;
            }
            else if (laser.ChaffLockPreviousState == NAME_s_Active && laser.TriggerMethode != TriggerType.TriggerControl)
            {
                laser.ChaffLockPreviousState = NAME_s_Inactive;
            }
        }

        [Uc("SBase.SLaserTrigger.s_LockedByChaff.UnTrigger", Sha1 = "774798611d72df569271b884f9b88db4b4685637")]
        [PortOf("uc:SBase.SLaserTrigger:s_LockedByChaff.UnTrigger", Spec = "spec/devices/laser-trigger.md#states")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            var laser = (SLaserTrigger)self;
            if (laser.ChaffLockPreviousState == NAME_s_Active)
            {
                laser.ChaffLockPreviousState = NAME_s_Inactive;
            }
        }

        [Uc("SBase.SLaserTrigger.s_LockedByChaff.EndState", Sha1 = "3988c04876cda88a72a3bb9c86f378aab9a717d0")]
        [PortOf("uc:SBase.SLaserTrigger:s_LockedByChaff.EndState", Spec = "spec/devices/laser-trigger.md#states")]
        public override void EndState(Actor self)
        {
            base.EndState(self);
            ((SLaserTrigger)self).HideLaser(false);
        }

        [Uc("SBase.SLaserTrigger.s_LockedByChaff.Tick", Sha1 = "24afc2bec8520f2c8f75fd4c5e48c860bc833a4b")]
        [PortOf("uc:SBase.SLaserTrigger:s_LockedByChaff.Tick", Spec = "spec/devices/laser-trigger.md#states")]
        public override void Tick(Actor self, float DeltaTime) => ((SLaserTrigger)self).SyncLaserAndSparks();
    }

    /// <summary>Left out of the round: hidden, deaf to events, timers, ticks and chaff.</summary>
    [Uc("SBase.SLaserTrigger.s_Absent")]
    public class s_Absent : UcActorState, ISetLockedByChaff
    {
        public static readonly s_Absent Instance = new(UcMeta.s_Absent, null);

        protected s_Absent(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SLaserTrigger.s_Absent.SetLockedByChaff", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SLaserTrigger:s_Absent.SetLockedByChaff", Spec = "spec/devices/laser-trigger.md#states")]
        public void SetLockedByChaff(SBasicGamePlayObject self, float Length)
        {
        }

        [Uc("SBase.SLaserTrigger.s_Absent.BeginState", Sha1 = "4a7d004098d7cebd1d6d0a2810acdc7f6567a399")]
        [PortOf("uc:SBase.SLaserTrigger:s_Absent.BeginState", Spec = "spec/devices/laser-trigger.md#states")]
        public override void BeginState(Actor self) => self.bHidden = true;

        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
        }

        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
        }

        public override void Timer(Actor self)
        {
        }

        public override void Tick(Actor self, float DeltaTime)
        {
        }
    }
}
