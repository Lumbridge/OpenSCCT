using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController bomb states: planting the modem at a bomb spot and getting up. Written from
// spec/objectives/bomb-target.md (rules 4 to 7); the damage handlers are in Combat/CombatDispatch.cs.
public partial class SPlayerController
{
    /// <summary>The pawn's modem, or null.</summary>
    private SGadBomb? FindBombGadget() =>
        SBPawn is { } pawn ? PawnInventory.FindGadgetType(pawn, GadBombClass) as SGadBomb : null;

    /// <summary>True on the machine that runs this player's input and draws its view (the spy's own machine).</summary>
    private bool IsLocalInputMachine() => IAmOnClient() && ReferenceEquals(Level.GetLocalPlayerController(), this);

    /// <summary>The plant animation (rule 5); its end uses the modem on the spy's machine.</summary>
    [Uc("SBase.SPlayerController.s_PlaceBomb")]
    public class s_PlaceBomb : UcActorState, IPlayerMove
    {
        public static readonly s_PlaceBomb Instance = new(UcMeta.s_PlaceBomb, null);

        protected s_PlaceBomb(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The spy's own machine walks away when the modem cannot be used; otherwise the plant action.</summary>
        [Uc("SBase.SPlayerController.s_PlaceBomb.BeginState", Sha1 = "bfd13609da2fff5d0957e9a1a37401bceab1dec3")]
        [PortOf("uc:SBase.SPlayerController:s_PlaceBomb.BeginState", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is not { } pawn)
            {
                return;
            }

            // SGadBomb(FindGadgetType(...)).CanUse(): a missing modem reads false.
            if (pc.IsLocalInputMachine() && !(pc.FindBombGadget()?.CanUseGadget() ?? false))
            {
                pc.BackToUnarmedWalk();
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_BOMB);
        }

        [Uc("SBase.SPlayerController.s_PlaceBomb.AnimEnd", Sha1 = "8c1c1285728156fc8a05ddcd671ffb20e3db9519")]
        [PortOf("uc:SBase.SPlayerController:s_PlaceBomb.AnimEnd", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel != 0)
            {
                return;
            }

            var pc = (SPlayerController)self;
            if (pc.IsLocalInputMachine())
            {
                pc.FindBombGadget()?.Use(1f);
            }

            pc.eventChangeState(CNAME_s_PlaceBomb_End);
        }

        [Uc("SBase.SPlayerController.s_PlaceBomb.PlayerMove", Sha1 = "6c7b176e9b6c1e1e6bb32fe136a2003073bce881")]
        [PortOf("uc:SBase.SPlayerController:s_PlaceBomb.PlayerMove", Spec = "spec/objectives/bomb-target.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.PickupStillMove(DeltaTime, true);
    }

    /// <summary>Getting up after the plant (rule 6).</summary>
    [Uc("SBase.SPlayerController.s_PlaceBomb_End")]
    public class s_PlaceBomb_End : UcActorState, IPlayerMove
    {
        public static readonly s_PlaceBomb_End Instance = new(UcMeta.s_PlaceBomb_End, null);

        protected s_PlaceBomb_End(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlaceBomb_End.BeginState", Sha1 = "382475d7812279f5dca8d282c263757c57e4094c")]
        [PortOf("uc:SBase.SPlayerController:s_PlaceBomb_End.BeginState", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void BeginState(Actor self) => ((SPlayerController)self).SBPawn?.ChangeEnumAction(SBasePawn.SA_BOMB_END);

        /// <summary>A drawn weapon is put away for the stance, then back to the unarmed walk.</summary>
        [Uc("SBase.SPlayerController.s_PlaceBomb_End.AnimEnd", Sha1 = "62792c00595a2a9097a8b4187983f4c2ddc9f835")]
        [PortOf("uc:SBase.SPlayerController:s_PlaceBomb_End.AnimEnd", Spec = "spec/objectives/bomb-target.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.WeaponLoaded != 0)
            {
                pawn.ChangeEnumAction(pawn.bIsCrouched ? SBasePawn.SA_WALK_CR_2Hto0H : SBasePawn.SA_WALK_ST_2Hto0H);
            }

            pc.BackToUnarmedWalk();
        }

        [Uc("SBase.SPlayerController.s_PlaceBomb_End.PlayerMove", Sha1 = "80b82aa03e0e4257764c99a78c357c755cbf2813")]
        [PortOf("uc:SBase.SPlayerController:s_PlaceBomb_End.PlayerMove", Spec = "spec/objectives/bomb-target.md#rules")]
        public void PlayerMove(SPlayerController self, float DeltaTime) => self.PickupStillMove(DeltaTime, true);
    }
}
