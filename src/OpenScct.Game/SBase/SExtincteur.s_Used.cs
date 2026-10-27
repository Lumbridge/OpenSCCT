using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SExtincteur
{
    /// <summary>Spraying: further hits do nothing; the timer stops the cloud and re-arms.</summary>
    [Uc("SBase.SExtincteur.s_Used")]
    public class s_Used : UcActorState, ITakeDamage
    {
        public static readonly s_Used Instance = new(UcMeta.s_Used, null);

        protected s_Used(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SExtincteur.s_Used.TakeDamage", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SExtincteur:s_Used.TakeDamage", Spec = "spec/devices/extincteur.md#rules")]
        public void TakeDamage(Actor self, int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag)
        {
        }

        [Uc("SBase.SExtincteur.s_Used.Timer", Sha1 = "ac569379df65d47cbf247b74446d84640b765abb")]
        [PortOf("uc:SBase.SExtincteur:s_Used.Timer", Spec = "spec/devices/extincteur.md#rules")]
        public override void Timer(Actor self)
        {
            var extincteur = (SExtincteur)self;
            extincteur.KillEmitter();
            extincteur.GotoState(nameof(s_Waiting));
        }
    }
}
