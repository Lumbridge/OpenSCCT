using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SFlag
{
    /// <summary>
    /// The disk at home or at rest away from home (disk-flag.md rules 20 to 23): touches return or pick it up, and
    /// a disk resting away goes home after ResetTime.
    /// </summary>
    [Uc("SBase.SFlag.s_Initial")]
    public new class s_Initial : SObjectiveTrigger.s_Initial
    {
        public static new readonly s_Initial Instance = new(UcMeta.s_Initial, SObjectiveTrigger.s_Initial.Instance);

        protected s_Initial(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SFlag.s_Initial.BeginState", Sha1 = "d083d58b808179c9874aa798a481f7e80fe6cb7d")]
        [PortOf("uc:SBase.SFlag:s_Initial.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void BeginState(Actor self)
        {
            var flag = (SFlag)self;
            if (flag.Location != flag.InitialLocation)
            {
                flag.SetTimer(flag.ResetTime, false);
            }
        }

        /// <summary>A living merc player sends a resting disk home; a living spy player takes it at once.</summary>
        [Uc("SBase.SFlag.s_Initial.Touch", Sha1 = "c2f69fa306e664200f8e89f5243f4ba80303072b")]
        [PortOf("uc:SBase.SFlag:s_Initial.Touch", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void Touch(Actor self, Actor Other)
        {
            var flag = (SFlag)self;
            if (flag.Objective is { ObjectiveTaken: true } || flag.bPickable)
            {
                return;
            }

            if (flag.Location.X == flag.InitialLocation.X && flag.Location.Y == flag.InitialLocation.Y)
            {
                return;
            }

            if (Other is not SBasePawn { Health: > 0, Controller: SPlayerController pc } pawn)
            {
                return;
            }

            if (pawn.SPawnType == PawnType.SPAWNDEFENSE)
            {
                ObjectiveMessages.Broadcast(flag, K.MsgDataSecured, flag);
                if (pc.PlayerReplicationInfo?.Stats is { } stats)
                {
                    stats.Merc_SecureDisks++;
                }

                flag.eventReset();
            }
            else
            {
                DiskCarryRules.PickupFlag(pc, flag);
            }
        }

        /// <summary>ResetTime after coming to rest: the disk goes home, crediting the carrier's killer.</summary>
        [Uc("SBase.SFlag.s_Initial.Timer", Sha1 = "ac6d3c2a390a93be8c0ee3b35eab7cfdd78ef744")]
        [PortOf("uc:SBase.SFlag:s_Initial.Timer", Spec = "spec/objectives/disk-flag.md#rules")]
        public override void Timer(Actor self)
        {
            var flag = (SFlag)self;
            if (!flag.bActive)
            {
                return;
            }

            if (flag.CarrierKiller?.PlayerReplicationInfo?.Stats is { } stats)
            {
                stats.Merc_SecureDisks++;
            }

            flag.eventReset();
        }
    }
}
