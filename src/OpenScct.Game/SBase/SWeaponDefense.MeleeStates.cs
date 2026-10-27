using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SWeaponDefense view-model states of the melee moves: the rifle butt strike (s_ElbowShocking, which also blocks
// the trigger and the reloads) and the charge clips, all on view model channel 0
// (spec/combat/melee-elbow-charge-berserk.md rule 30).
public partial class SWeaponDefense
{
    private static readonly Name NAME_s_Berserk360 = new("s_Berserk360");

    /// <summary>s_ElbowShocking: the rifle butt clip; the trigger and the reloads are ignored until it ends.</summary>
    [Uc("SBase.SWeaponDefense.s_ElbowShocking")]
    public class s_ElbowShocking : UcActorState, IFire, IAltFire, IReload, IFullReload, IAnimateUnDraw_GRAB
    {
        public static readonly s_ElbowShocking Instance = new(UcMeta.s_ElbowShocking, null);

        protected s_ElbowShocking(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_ElbowShocking.BeginState", Sha1 = "a844377dce6352fa3acd1d5a9c601737bc831099")]
        [PortOf("uc:SBase.SWeaponDefense:s_ElbowShocking.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        [Provenance(ProvenanceKind.Decoded, "s_ElbowShocking.BeginState: bNoReticule, PlayAnim('FirstAtakCrFd2') on channel 0, EteintLaser (melee rule 30)")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            if (w.OwnerPC is { } pc)
            {
                pc.bNoReticule = true;
            }

            w.ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstAtakCrFd2, ViewModelClips.DataOf(ViewModelClips.FirstAtakCrFd2));
            w.OwnerPC?.EteintLaser();
        }

        [Uc("SBase.SWeaponDefense.s_ElbowShocking.EndState", Sha1 = "485a143cb6adf46bc5ff7bc1695688930d21c97e")]
        [PortOf("uc:SBase.SWeaponDefense:s_ElbowShocking.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        [Provenance(ProvenanceKind.Decoded, "s_ElbowShocking.EndState: the reticle back unless the laser was on (melee rule 30)")]
        public override void EndState(Actor self)
        {
            var w = (SWeaponDefense)self;
            if (w.OwnerPC is { bLaserEtaitAllume: false } pc)
            {
                pc.bNoReticule = false;
            }
        }

        [Uc("SBase.SWeaponDefense.s_ElbowShocking.AnimEnd", Sha1 = "bd598fbc01cb1f1be1f52fa245602744e677edc8")]
        [PortOf("uc:SBase.SWeaponDefense:s_ElbowShocking.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#elbow-strike")]
        [Provenance(ProvenanceKind.Decoded, "s_ElbowShocking.AnimEnd: channel 0, laser back outside s_Berserk360, AnimateWaiting, PreviousState (melee rule 30)")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == MVTCHANNEL)
            {
                if (w.OwnerPC is { } pc && !pc.IsInState(NAME_s_Berserk360))
                {
                    pc.RallumeLaserEteint();
                }

                w.AnimateWaiting();
                if (w.PreviousState.IsNone)
                {
                    w.PreviousState = CNAME_s_Selected;
                }

                w.GotoState(w.PreviousState);
            }

            w.AnimEnd(Channel);
        }

        [Uc("SBase.SWeaponDefense.s_ElbowShocking.AnimateUnDraw_GRAB", Sha1 = "412f288683fe1fdf7d497e262fd771b6540e7d9c")]
        [PortOf("uc:SBase.SWeaponDefense:s_ElbowShocking.AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw_GRAB(SWeaponDefense self)
        {
            self.GotoState(CNAME_s_Selected);
            self.PreviousState = CNAME_s_Selected;
            self.AnimateUnDraw_GRAB();
        }

        public void Fire(SWeapon self, float Value)
        {
        }

        public void AltFire(SWeapon self, float Value)
        {
        }

        public bool Reload(SWeapon self) => false;

        public bool FullReload(SWeapon self) => false;
    }

    /// <summary>s_Charging: the charge clip on the view model; the laser is off during the charge.</summary>
    [Uc("SBase.SWeaponDefense.s_Charging")]
    public class s_Charging : UcActorState
    {
        public static readonly s_Charging Instance = new(UcMeta.s_Charging, null);

        protected s_Charging(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Charging.BeginState", Sha1 = "21273be1bcde49680722a4c5ef5aa623d1ca4b16")]
        [PortOf("uc:SBase.SWeaponDefense:s_Charging.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Decoded, "s_Charging.BeginState: LoopAnim('FirstAtakChFd2', , 0.3) on channel 0 (melee rule 30)")]
        [Provenance(ProvenanceKind.Estimated, "The 0.3 s tween is not modelled: the view model channels switch clips at once")]
        public override void BeginState(Actor self) => ((SWeaponDefense)self).ViewAnim.Loop(MVTCHANNEL, REF_FirstAtakChFd2);

        [Uc("SBase.SWeaponDefense.s_Charging.EndState", Sha1 = "e202d73835536ea7602ecee2b1a492f6df79ee3c")]
        [PortOf("uc:SBase.SWeaponDefense:s_Charging.EndState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Decoded, "s_Charging.EndState: LoopAnim('FirstWalkStFd2', , 0.3, 0) (melee rule 30)")]
        public override void EndState(Actor self) => ((SWeaponDefense)self).ViewAnim.Loop(MVTCHANNEL, ViewModelClips.FirstWalkStFd2);
    }

    /// <summary>s_Charging_End: the charge run-out clip, then s_Selected.</summary>
    [Uc("SBase.SWeaponDefense.s_Charging_End")]
    public class s_Charging_End : UcActorState
    {
        public static readonly s_Charging_End Instance = new(UcMeta.s_Charging_End, null);

        protected s_Charging_End(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Charging_End.BeginState", Sha1 = "0d583fb96a296eef5e331469fab37d9d5855ee27")]
        [PortOf("uc:SBase.SWeaponDefense:s_Charging_End.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Decoded, "s_Charging_End.BeginState: PlayAnim('FirstAtakChEd2') on channel 0 (melee rule 30)")]
        public override void BeginState(Actor self) =>
            ((SWeaponDefense)self).ViewAnim.Play(MVTCHANNEL, REF_FirstAtakChEd2, ViewModelClips.DataOf(REF_FirstAtakChEd2));

        [Uc("SBase.SWeaponDefense.s_Charging_End.AnimEnd", Sha1 = "84cb88f6829bc2dcf438e0a961544232828c9020")]
        [PortOf("uc:SBase.SWeaponDefense:s_Charging_End.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Decoded, "s_Charging_End.AnimEnd: channel 0 returns to s_Selected (melee rule 30)")]
        public override void AnimEnd(Actor self, int Channel) => ChargeClipEnd((SWeaponDefense)self, Channel, false);
    }

    /// <summary>s_Charging_Bump: the charge impact clip, then s_Selected.</summary>
    [Uc("SBase.SWeaponDefense.s_Charging_Bump")]
    public class s_Charging_Bump : UcActorState
    {
        public static readonly s_Charging_Bump Instance = new(UcMeta.s_Charging_Bump, null);

        protected s_Charging_Bump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Charging_Bump.BeginState", Sha1 = "7f80df2d2097350cf03b02f613a691ebe3137511")]
        [PortOf("uc:SBase.SWeaponDefense:s_Charging_Bump.BeginState", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Decoded, "s_Charging_Bump.BeginState: PlayAnim('FirstAtakChit2') on channel 0 (melee rule 30)")]
        public override void BeginState(Actor self) =>
            ((SWeaponDefense)self).ViewAnim.Play(MVTCHANNEL, REF_FirstAtakChit2, ViewModelClips.DataOf(REF_FirstAtakChit2));

        [Uc("SBase.SWeaponDefense.s_Charging_Bump.AnimEnd", Sha1 = "3dab626bde30c499884544a40d21fa4cb96e0f0e")]
        [PortOf("uc:SBase.SWeaponDefense:s_Charging_Bump.AnimEnd", Spec = "spec/combat/melee-elbow-charge-berserk.md#merc-charge")]
        [Provenance(ProvenanceKind.Decoded, "s_Charging_Bump.AnimEnd: channel 0, AnimateWaiting, then s_Selected (melee rule 30)")]
        public override void AnimEnd(Actor self, int Channel) => ChargeClipEnd((SWeaponDefense)self, Channel, true);
    }

    /// <summary>The end of a charge clip on channel 0: s_Selected (after the idle or walk loop), then the global AnimEnd.</summary>
    private static void ChargeClipEnd(SWeaponDefense w, int Channel, bool AnimateWaitingFirst)
    {
        if (Channel == MVTCHANNEL)
        {
            if (AnimateWaitingFirst)
            {
                w.AnimateWaiting();
            }

            w.GotoState(CNAME_s_Selected);
            w.PreviousState = CNAME_s_Selected;
        }

        w.AnimEnd(Channel);
    }
}
