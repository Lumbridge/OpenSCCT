using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SVolumetricSensor states (spec/devices/volumetric-sensor.md#states): s_Active (watching, the auto state),
// s_SomethingDetected (confirming a detection), s_Inactive (switched off) and s_LockedByChaff (broken). These run on
// the server; clients only see bLightState.
public partial class SVolumetricSensor
{
    /// <summary>Watching: green, one volume checked every 0.5 s / Volumes.Length.</summary>
    [Uc("SBase.SVolumetricSensor.s_Active")]
    public class s_Active : UcActorState
    {
        public static readonly s_Active Instance = new(UcMeta.s_Active, null);

        private readonly int _beginPc;

        protected s_Active(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
            foreach ((string name, int pc) in info.LabelPcs)
            {
                if (name == "Begin")
                {
                    _beginPc = pc;
                }
            }
        }

        [Uc("SBase.SVolumetricSensor.s_Active.BeginState", Sha1 = "18982623295b80333aac52753dae6fe4ec57c0ca")]
        [PortOf("uc:SBase.SVolumetricSensor:s_Active.BeginState", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void BeginState(Actor self) => ((SVolumetricSensor)self).bLightState = SensorK.LightGreen;

        /// <summary>The 3 s of red after an immediate detection are over.</summary>
        [Uc("SBase.SVolumetricSensor.s_Active.Timer", Sha1 = "18982623295b80333aac52753dae6fe4ec57c0ca")]
        [PortOf("uc:SBase.SVolumetricSensor:s_Active.Timer", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void Timer(Actor self) => ((SVolumetricSensor)self).bLightState = SensorK.LightGreen;

        [Uc("SBase.SVolumetricSensor.s_Active.CheckPresence", Sha1 = "575da3fadb084770edfb70f6238fec87f7082be4")]
        [PortOf("uc:SBase.SVolumetricSensor:s_Active.CheckPresence", Spec = "spec/devices/volumetric-sensor.md#states")]
        public static void CheckPresence(SVolumetricSensor self) => self.CheckPresence();

        [Uc("SBase.SVolumetricSensor.s_Active.UnTrigger", Sha1 = "993d55c241053069baaecb7dcbaa09b92d88c41c")]
        [PortOf("uc:SBase.SVolumetricSensor:s_Active.UnTrigger", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.GotoState(NAME_s_Inactive);

        /// <summary>Begin: Sleep(0.5 / Volumes.Length); CheckPresence; goto Begin. With no volume it sleeps for ever.</summary>
        [Uc("SBase.SVolumetricSensor.s_Active@code", Sha1 = "2a7f3821ecce01f7ef031c0c6c6ebfc43df4b243")]
        [PortOf("uc:SBase.SVolumetricSensor:s_Active@code", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var sensor = (SVolumetricSensor)self;
            if (sensor.Volumes.Count == 0)
            {
                frame.Pc = LatentFrame.NoCode;
                return;
            }

            if (frame.Pc == _beginPc)
            {
                frame.Pc = _beginPc + 1;
                sensor.SleepStep();
                return;
            }

            CheckPresence(sensor);
            if (!ReferenceEquals(sensor.StateNode, this))
            {
                // CheckPresence went to s_SomethingDetected: that state has no code.
                return;
            }

            // goto Begin.
            frame.Pc = _beginPc + 1;
            sensor.SleepStep();
        }
    }

    /// <summary>
    /// Confirming: red with the alarm loop for DetectionDelay seconds, then the Event when DetectedActor is still in
    /// the volume and moving fast enough; back to watching either way.
    /// </summary>
    [Uc("SBase.SVolumetricSensor.s_SomethingDetected")]
    public class s_SomethingDetected : UcActorState
    {
        public static readonly s_SomethingDetected Instance = new(UcMeta.s_SomethingDetected, null);

        protected s_SomethingDetected(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SVolumetricSensor.s_SomethingDetected.BeginState", Sha1 = "6e9e9cd3d9bfab15f319e41bc2e419114941a51b")]
        [PortOf("uc:SBase.SVolumetricSensor:s_SomethingDetected.BeginState", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void BeginState(Actor self)
        {
            var sensor = (SVolumetricSensor)self;
            sensor.bLightState = SensorK.LightRed;
            sensor.SetTimer(sensor.DetectionDelay, false);
            sensor.AmbientSound = SensorK.SensorAlarmSound;
            sensor.SoundVolume = SensorK.SensorAlarmVolume;
            sensor.SoundRadius = SensorK.SensorAlarmRadius;
        }

        [Uc("SBase.SVolumetricSensor.s_SomethingDetected.EndState", Sha1 = "79046b160cd4e87666e3fd668e212522b701ea80")]
        [PortOf("uc:SBase.SVolumetricSensor:s_SomethingDetected.EndState", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void EndState(Actor self) => self.AmbientSound = AssetRef.None;

        [Uc("SBase.SVolumetricSensor.s_SomethingDetected.Timer", Sha1 = "1938719ebd4cdc15512bf561810e40a2d14fc8aa")]
        [PortOf("uc:SBase.SVolumetricSensor:s_SomethingDetected.Timer", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void Timer(Actor self)
        {
            var sensor = (SVolumetricSensor)self;
            if (sensor.CurrentIndex < sensor.Volumes.Count && sensor.Volumes[sensor.CurrentIndex] is { } volume)
            {
                foreach (Pawn p in TouchingPawns(volume))
                {
                    if (sensor.Detects(p) && ReferenceEquals(sensor.DetectedActor, p))
                    {
                        sensor.TriggerEvent(sensor.Event, sensor, p);
                    }
                }
            }

            sensor.GotoState(NAME_s_Active);
        }

        [Uc("SBase.SVolumetricSensor.s_SomethingDetected.UnTrigger", Sha1 = "993d55c241053069baaecb7dcbaa09b92d88c41c")]
        [PortOf("uc:SBase.SVolumetricSensor:s_SomethingDetected.UnTrigger", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.GotoState(NAME_s_Inactive);
    }

    /// <summary>Switched off: black LED until a Trigger.</summary>
    [Uc("SBase.SVolumetricSensor.s_Inactive")]
    public class s_Inactive : UcActorState
    {
        public static readonly s_Inactive Instance = new(UcMeta.s_Inactive, null);

        protected s_Inactive(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SVolumetricSensor.s_Inactive.BeginState", Sha1 = "2fa80025dcf24592a9b24398c2b92f6b21289365")]
        [PortOf("uc:SBase.SVolumetricSensor:s_Inactive.BeginState", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void BeginState(Actor self) => ((SVolumetricSensor)self).bLightState = SensorK.LightOff;

        [Uc("SBase.SVolumetricSensor.s_Inactive.Trigger", Sha1 = "1849bca198c86717b6230df735cb5c200b9fbd17")]
        [PortOf("uc:SBase.SVolumetricSensor:s_Inactive.Trigger", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.GotoState(NAME_s_Active);
    }

    /// <summary>Broken by chaff or a sticky shot: black LED, sparks and a bang; back to the remembered state later.</summary>
    [Uc("SBase.SVolumetricSensor.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff, ISetLockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, SBasicGamePlayObject.s_LockedByChaff.Instance);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SVolumetricSensor.s_LockedByChaff.SetLockedByChaff", Sha1 = "6a63fb16a8f5e9d4b3515aa74dad68b1da0cb263")]
        [PortOf("uc:SBase.SVolumetricSensor:s_LockedByChaff.SetLockedByChaff", Spec = "spec/devices/volumetric-sensor.md#states")]
        public new void SetLockedByChaff(SBasicGamePlayObject self, float Length) => self.SetTimer(self.StickyNeutralizationTime, false);

        [Uc("SBase.SVolumetricSensor.s_LockedByChaff.BeginState", Sha1 = "41b36fc8fa5d04961798a93a6ae0979f87783832")]
        [PortOf("uc:SBase.SVolumetricSensor:s_LockedByChaff.BeginState", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var sensor = (SVolumetricSensor)self;
            sensor.bLightState = SensorK.LightBroken;
            sensor.PlaySound(SensorK.ChaffExplosionSound, ESoundSlot.SLOT_Misc, 1f, false, SensorK.ChaffExplosionRadius);
        }

        /// <summary>The LED and sparks follow a changed bLightState (as LightStateChange does).</summary>
        [Uc("SBase.SVolumetricSensor.s_LockedByChaff.Tick", Sha1 = "cf2a6f76c707024dce49179f9dd5def3b2043511")]
        [PortOf("uc:SBase.SVolumetricSensor:s_LockedByChaff.Tick", Spec = "spec/devices/volumetric-sensor.md#states")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var sensor = (SVolumetricSensor)self;
            if (sensor.bLightState != sensor.bLocalLightState)
            {
                sensor.bLocalLightState = sensor.bLightState;
                sensor.ApplyLight();
            }
        }
    }
}
