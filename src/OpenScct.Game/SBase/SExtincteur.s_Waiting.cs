using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SExtincteur
{
    /// <summary>Armed: any hit sprays a cloud at the hit point, back toward the shot.</summary>
    [Uc("SBase.SExtincteur.s_Waiting")]
    public class s_Waiting : UcActorState, ITakeDamage
    {
        public static readonly s_Waiting Instance = new(UcMeta.s_Waiting, null);

        protected s_Waiting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Spawns the cloud facing back along the shot, a noise for every player, the burst and a 10 s timer.</summary>
        [Uc("SBase.SExtincteur.s_Waiting.TakeDamage", Sha1 = "4713ff3c98274c9deff2f9f7a07ec24d9d40ae0d")]
        [PortOf("uc:SBase.SExtincteur:s_Waiting.TakeDamage", Spec = "spec/devices/extincteur.md#rules")]
        public void TakeDamage(Actor self, int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag)
        {
            var extincteur = (SExtincteur)self;
            extincteur.Emitter = extincteur.XLevel.Spawn<SExtincteurEmitter>(HitLocation, (-Momentum).Rotation(), null,
                extincteur.Instigator);
            extincteur.NotifyNoise(DeviceK.ExtincteurNoise);
            extincteur.PlaySound(extincteur.sndImpact, ESoundSlot.SLOT_Misc);
            extincteur.SetTimer(DeviceK.ExtincteurSprayTime, false);
            extincteur.GotoState(nameof(s_Used));
        }
    }
}
