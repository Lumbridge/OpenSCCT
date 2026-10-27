using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SWeaponDefense states (spec/equipment/merc-rifle.md rules 4-5, 14-21 and merc-scope.md rule 3): the idle
// state with the auto-reload, the firing timer, the reloads and the grenade throw, the holster and grab idles, the
// electric shock and the scope raise. The weapon states run on the owner's machine.
public partial class SWeaponDefense
{
    public static new readonly StateTable ClassStates = new(SWeapon.ClassStates,
        s_Selected.Instance, s_Firing.Instance, s_Reloading.Instance, s_FullReloading.Instance, s_FireGrenade.Instance,
        s_IdleInHolster.Instance, s_IdleGrabbed.Instance, s_Dmg_Electric.Instance, s_Dmg_Electric_End.Instance,
        s_Sniping.Instance, s_ElbowShocking.Instance, s_Charging.Instance, s_Charging_End.Instance, s_Charging_Bump.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name CNAME_s_Dmg_Electric_End = new("s_Dmg_Electric_End");

    /// <summary>One more shot of the trigger pull when the muzzle is clear (rules 4 and 5), with the rumble.</summary>
    private bool TryShot()
    {
        if (!(OwnerPC?.CheckIfCanFire() ?? true))
        {
            return false;
        }

        BulletInARow += 1;
        return true;
    }

    /// <summary>
    /// The start of a reload: the sound, the reload clip, the third-person action (the shotgun passes its shell
    /// count's, or -1 for none), no reticle or laser, the message. <paramref name="ClearFlash"/> also stops the
    /// low-ammo flash.
    /// </summary>
    private protected void ReloadBegin(float MessageTime, int Action = SBasePawn.SA_RELOAD, bool ClearFlash = true)
    {
        ServerSound(ReloadSound, ESoundSlot.SLOT_None, (int)K.WeaponSoundRadius);
        ViewAnim.SetAlpha(ACTIONCHANNEL, 1f);
        ViewAnim.Play(ACTIONCHANNEL, AFirstReloSt, ViewModelClips.DataOf(AFirstReloSt));
        if (Action >= 0 && Instigator is SBasePawn pawn)
        {
            CombatServices.Ports(this).ChangeEnumAction(pawn, Action, false);
        }

        if (OwnerPC is { } pc)
        {
            pc.bNoReticule = true;
            pc.EteintLaser();
            if (ClearFlash && pc.myHUD is SHud hud)
            {
                hud.bReticuleFlash = false;
            }
        }

        LocalMessage(TextReloading, MessageTime, K.ReloadMessagePriority);
    }

    private void ReloadEnd(int Channel)
    {
        if (Channel != ACTIONCHANNEL)
        {
            return;
        }

        ViewAnim.SetAlpha(ACTIONCHANNEL, 0f);
        GotoState(FireHeld ? CNAME_s_Firing : CNAME_s_Selected);
    }

    /// <summary>s_Selected (auto): the idle weapon with the auto-reload and the holster checks.</summary>
    [Uc("SBase.SWeaponDefense.s_Selected")]
    public class s_Selected : UcActorState, IFire, ISwitchROF
    {
        public static readonly s_Selected Instance = new(UcMeta.s_Selected, null);

        protected s_Selected(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>An empty clip with fire held clicks at once (rule 15; the tick then reloads).</summary>
        [Uc("SBase.SWeaponDefense.s_Selected.BeginState", Sha1 = "16de6504615c3beb48aeaa7f8ae300bee53f3db4")]
        [PortOf("uc:SBase.SWeaponDefense:s_Selected.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            if (w.ClipAmmo == 0 && w.ROFMode != SRateOfFireMode.ROF_Single && w.FireHeld)
            {
                w.Fire(1f);
            }
        }

        /// <summary>The side-step channels blend out (a view effect).</summary>
        [Uc("SBase.SWeaponDefense.s_Selected.EndState", Sha1 = "4246f526bd2adac0b25bad1a015cd5c9b1129e52")]
        [PortOf("uc:SBase.SWeaponDefense:s_Selected.EndState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void EndState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.ViewAnim.SetAlpha(MVTCHANNELMOVE_LT, 0f);
            w.ViewAnim.SetAlpha(MVTCHANNELMOVE_RT, 0f);
        }

        /// <summary>
        /// Rule 14: the native tick; a holstered weapon goes to the holster or grab idle; then the auto-reload when a
        /// burst or auto pull ran the clip dry.
        /// </summary>
        [Uc("SBase.SWeaponDefense.s_Selected.Tick", Sha1 = "dcb1bf28dee743cd5343cc5b5f8c86399dfe6f23")]
        [PortOf("uc:SBase.SWeaponDefense:s_Selected.Tick", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var w = (SWeaponDefense)self;
            w.NativeTickSWeaponDefense(DeltaTime);
            if (w.Instigator is SBasePawn { WeaponLoaded: 0 })
            {
                w.GotoState(w.ViewAnim.Sequence(MVTCHANNEL) == ViewModelClips.FirstGrbdStBg2 ? CNAME_s_IdleGrabbed : CNAME_s_IdleInHolster);
                return;
            }

            // The idle or walk loop of the arms follows the pawn's speed every tick, unless a draw, landing, jump, fall,
            // grab or charge clip plays on channel 0. The side-step blend on channels 1 and 2 is not ported yet.
            Name seq = w.ViewAnim.Sequence(MVTCHANNEL);
            if (seq != ViewModelClips.FirstDrawStEd2 && seq != ViewModelClips.FirstLandStFd2 && seq != ViewModelClips.FirstLandStHi2 &&
                seq != ViewModelClips.FirstJumpStFd2 && seq != ViewModelClips.FirstFallStFd2 && seq != ViewModelClips.FirstGrbdStBg2 &&
                seq != ViewModelClips.FirstAtakChit2)
            {
                w.AnimateWaiting();
            }

            if (w.BulletsToFire == 0 && w.ClipAmmo == 0 && w.Ammo > 0 && w.ROFMode != SRateOfFireMode.ROF_Single)
            {
                w.eventReload();
            }
        }

        /// <summary>The grab clip's end hides the view model; other ends as global.</summary>
        [Uc("SBase.SWeaponDefense.s_Selected.AnimEnd", Sha1 = "c5c4d30698c658e4a6c80a600e3042de1d3733af")]
        [PortOf("uc:SBase.SWeaponDefense:s_Selected.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == MVTCHANNEL && w.ViewAnim.Sequence(MVTCHANNEL) == ViewModelClips.FirstGrbdStBg2)
            {
                w.bDrawWeapon = false;
                return;
            }

            w.AnimEnd(Channel);
        }

        public void Fire(SWeapon self, float Value) => self.Fire(Value);

        /// <summary>The mode switch is refused while fire is held (rule 16).</summary>
        [Uc("SBase.SWeaponDefense.s_Selected.SwitchROF", Sha1 = "0db3fdd04ed345ed08c8f7411254d994f462d40b")]
        [PortOf("uc:SBase.SWeaponDefense:s_Selected.SwitchROF", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void SwitchROF(SWeaponDefense self)
        {
            if (!self.FireHeld)
            {
                self.SwitchROF();
            }
        }
    }

    /// <summary>s_Firing: the first shot at once, then one per RateOfFire timer while the pull lasts.</summary>
    [Uc("SBase.SWeaponDefense.s_Firing")]
    public class s_Firing : UcActorState, IFire, IAltFire, IReload, IFullReload, ISwitchROF, IAnimateDraw, IAnimateUnDraw,
        IAnimateUnDraw_GRAB
    {
        public static readonly s_Firing Instance = new(UcMeta.s_Firing, null);

        protected s_Firing(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Rule 4: with a clear muzzle the first shot of the pull; the looping timer always.</summary>
        [Uc("SBase.SWeaponDefense.s_Firing.BeginState", Sha1 = "36fbabb27635d48a1402461ab4ad7a7022d6e488")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            if (w.TryShot())
            {
                w.BulletsToFire = w.GetNbBulletsFromROF();
                w.FireWeapon();
                w.OwnerPC?.Vibration_Tir();
            }

            w.SetTimer(w.RateOfFire, true);
        }

        /// <summary>The shot clip's channel blends out.</summary>
        [Uc("SBase.SWeaponDefense.s_Firing.EndState", Sha1 = "81f1f65f4400bde61ae660b99651b00062d4e3f4")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.EndState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void EndState(Actor self) => ((SWeaponDefense)self).ViewAnim.SetAlpha(ACTIONCHANNEL, 0f);

        /// <summary>
        /// Rule 5: the pull ends when its shots are spent, an auto pull is released or the elbow is pulled back
        /// (barrel smoke unless single); otherwise one more shot when the muzzle is clear.
        /// </summary>
        [Uc("SBase.SWeaponDefense.s_Firing.Timer", Sha1 = "e584567ded206c0adb785044594c0b788f9a57c5")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.Timer", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void Timer(Actor self)
        {
            var w = (SWeaponDefense)self;
            bool elbowBack = w.OwnerPC?.bElbowBackward ?? false;
            if (w.BulletsToFire <= 0 || (w.ROFMode == SRateOfFireMode.ROF_Auto && !w.FireHeld) || elbowBack)
            {
                if (w.ROFMode != SRateOfFireMode.ROF_Single)
                {
                    w.CreateSmoke2();
                }

                w.ViewAnim.SetAlpha(ACTIONCHANNEL, 0f);
                w.GotoState(CNAME_s_Selected);
                return;
            }

            if (w.TryShot())
            {
                w.FireWeapon();
                w.OwnerPC?.Vibration_Tir();
            }
        }

        /// <summary>The shot clip's end zeroes the action channel.</summary>
        [Uc("SBase.SWeaponDefense.s_Firing.AnimEnd", Sha1 = "6e94411de4d65ff603be190741541a4b9edaca6e")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == ACTIONCHANNEL)
            {
                w.ViewAnim.SetAlpha(ACTIONCHANNEL, 0f);
                return;
            }

            w.AnimEnd(Channel);
        }

        public void Fire(SWeapon self, float Value)
        {
        }

        public void AltFire(SWeapon self, float Value)
        {
        }

        public bool Reload(SWeapon self) => false;

        public bool FullReload(SWeapon self) => false;

        [Uc("SBase.SWeaponDefense.s_Firing.SwitchROF", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.SwitchROF", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void SwitchROF(SWeaponDefense self)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Firing.AnimateDraw", Sha1 = "b8af27d1abb5bc4151f3f2849e3d278ea935aa4e")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.AnimateDraw", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateDraw(SWeaponDefense self)
        {
            self.GotoState(CNAME_s_Selected);
            self.AnimateDraw();
        }

        [Uc("SBase.SWeaponDefense.s_Firing.AnimateUnDraw", Sha1 = "182e3c8f9bacf0b102a82175d09be342b2817dba")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.AnimateUnDraw", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw(SWeaponDefense self)
        {
            self.GotoState(CNAME_s_Selected);
            self.AnimateUnDraw();
        }

        [Uc("SBase.SWeaponDefense.s_Firing.AnimateUnDraw_GRAB", Sha1 = "412f288683fe1fdf7d497e262fd771b6540e7d9c")]
        [PortOf("uc:SBase.SWeaponDefense:s_Firing.AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw_GRAB(SWeaponDefense self)
        {
            self.GotoState(CNAME_s_Selected);
            self.AnimateUnDraw_GRAB();
        }
    }

    /// <summary>s_Reloading: the reload clip; at its end the clip is filled from the reserve (rule 17).</summary>
    [Uc("SBase.SWeaponDefense.s_Reloading")]
    public class s_Reloading : UcActorState, IFire, IAltFire, IReload, IFullReload
    {
        public static readonly s_Reloading Instance = new(UcMeta.s_Reloading, null);

        protected s_Reloading(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Reloading.BeginState", Sha1 = "1f55c1d2e56e414a4f60eb890e34c24f982bfdf8")]
        [PortOf("uc:SBase.SWeaponDefense:s_Reloading.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            if (w.Ammo == 0 || w.ClipAmmo == w.ClipMaxAmmo)
            {
                w.GotoState(CNAME_s_Selected);
                return;
            }

            w.ReloadBegin(K.ReloadMessageTime);
        }

        [Uc("SBase.SWeaponDefense.s_Reloading.AnimEnd", Sha1 = "7a50911c04e321ff474422d1d26bce277057e87a")]
        [PortOf("uc:SBase.SWeaponDefense:s_Reloading.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == ACTIONCHANNEL && w.MaxAmmo > 0)
            {
                w.ClipAmmo = SvmMath.Min(w.Ammo, w.ClipMaxAmmo);
            }

            w.ReloadEnd(Channel);
        }

        [Uc("SBase.SWeaponDefense.s_Reloading.EndState", Sha1 = "ab15a16d2adf4a3812e3f88447f2ba2d0c019fca")]
        [PortOf("uc:SBase.SWeaponDefense:s_Reloading.EndState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void EndState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.ViewAnim.SetAlpha(ACTIONCHANNEL, 0f);
            if (w.OwnerPC is { } pc)
            {
                pc.RallumeLaserEteint();
                pc.bNoReticule = false;
            }
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

    /// <summary>s_FullReloading: the ammo crate's reload; at its end reserve and clip are full (rule 18).</summary>
    [Uc("SBase.SWeaponDefense.s_FullReloading")]
    public class s_FullReloading : UcActorState, IFire, IAltFire, IReload, IFullReload
    {
        public static readonly s_FullReloading Instance = new(UcMeta.s_FullReloading, null);

        protected s_FullReloading(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_FullReloading.BeginState", Sha1 = "4f337b55ab7c86825c7c87fef79a20167912dc65")]
        [PortOf("uc:SBase.SWeaponDefense:s_FullReloading.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        [PortOf("uc:SBase.SWeaponDefense:s_FullReloading.BeginState", Spec = "spec/equipment/ammo-stock.md#rules")]
        public override void BeginState(Actor self) => ((SWeaponDefense)self).ReloadBegin(K.FullReloadMessageTime);

        [Uc("SBase.SWeaponDefense.s_FullReloading.AnimEnd", Sha1 = "3ca8d59006b2d6c29cdfe8b8d3069e124713abf6")]
        [PortOf("uc:SBase.SWeaponDefense:s_FullReloading.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        [PortOf("uc:SBase.SWeaponDefense:s_FullReloading.AnimEnd", Spec = "spec/equipment/ammo-stock.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == ACTIONCHANNEL)
            {
                w.Ammo = w.MaxAmmo;
                w.ClipAmmo = w.ClipMaxAmmo;
            }

            w.ReloadEnd(Channel);
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

    /// <summary>s_FireGrenade (extends s_Reloading): the merc's grenade throw clip (rule 19).</summary>
    [Uc("SBase.SWeaponDefense.s_FireGrenade")]
    public class s_FireGrenade : s_Reloading
    {
        public static new readonly s_FireGrenade Instance = new(UcMeta.s_FireGrenade, s_Reloading.Instance);

        protected s_FireGrenade(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_FireGrenade.BeginState", Sha1 = "3d83484a5a87ff96d0cc755ab76eb201331e6d5f")]
        [PortOf("uc:SBase.SWeaponDefense:s_FireGrenade.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        [PortOf("uc:SBase.SWeaponDefense:s_FireGrenade.BeginState", Spec = "spec/equipment/grenade-throw.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.ServerSound(w.ReloadSound, ESoundSlot.SLOT_None, (int)K.WeaponSoundRadius);
            w.ViewAnim.SetAlpha(ACTIONCHANNEL, 1f);
            w.ViewAnim.Play(ACTIONCHANNEL, w.AFirstReloGrenSt, ViewModelClips.DataOf(w.AFirstReloGrenSt));
            if (w.Instigator is SBasePawn pawn)
            {
                CombatServices.Ports(w).ChangeEnumAction(pawn, SBasePawn.SA_FIRE_GRENADE, false);
            }

            if (w.OwnerPC is { } pc)
            {
                pc.bNoReticule = true;
                pc.EteintLaser();
            }
        }

        [Uc("SBase.SWeaponDefense.s_FireGrenade.AnimEnd", Sha1 = "e4ca4d32b24be6c51fbf6eb9e6e767a5bf3774f0")]
        [PortOf("uc:SBase.SWeaponDefense:s_FireGrenade.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == ACTIONCHANNEL)
            {
                w.GotoState(CNAME_s_Selected);
                return;
            }

            w.AnimEnd(Channel);
        }
    }

    /// <summary>s_IdleInHolster: the holster clip, the redraw and the hand-over to the grab idle (rule 20).</summary>
    [Uc("SBase.SWeaponDefense.s_IdleInHolster")]
    public class s_IdleInHolster : UcActorState
    {
        public static readonly s_IdleInHolster Instance = new(UcMeta.s_IdleInHolster, null);

        protected s_IdleInHolster(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_IdleInHolster.BeginState", Sha1 = "502394da07f68fed98481934474cc9d6899c7e41")]
        [PortOf("uc:SBase.SWeaponDefense:s_IdleInHolster.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.bDrawReticule = false;
            w.ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstDrawStEd2, K.FirstDrawStEd2, K.HolsterClipRate);
        }

        [Uc("SBase.SWeaponDefense.s_IdleInHolster.EndState", Sha1 = "961fb449d88f6106490afff6bf3d98040f58b92d")]
        [PortOf("uc:SBase.SWeaponDefense:s_IdleInHolster.EndState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void EndState(Actor self) => ((SWeaponDefense)self).bDrawReticule = true;

        [Uc("SBase.SWeaponDefense.s_IdleInHolster.Tick", Sha1 = "4df777ce9e2fb2d3aa77a830af4b6c6154ad96a0")]
        [PortOf("uc:SBase.SWeaponDefense:s_IdleInHolster.Tick", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var w = (SWeaponDefense)self;
            if (w.Instigator is SBasePawn { WeaponLoaded: not 0 } && !w.bDrawWeapon)
            {
                w.eventAnimateDraw();
            }

            if (w.ViewAnim.IsAnimating(MVTCHANNEL) && w.ViewAnim.Sequence(MVTCHANNEL) == ViewModelClips.FirstGrbdStBg2)
            {
                w.GotoState(CNAME_s_IdleGrabbed);
            }
        }

        /// <summary>The forward holster clip hides the view model; the backward draw clip returns to s_Selected.</summary>
        [Uc("SBase.SWeaponDefense.s_IdleInHolster.AnimEnd", Sha1 = "9fa562caa77a2e2bffdc55f108d036e6c78a2243")]
        [PortOf("uc:SBase.SWeaponDefense:s_IdleInHolster.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == MVTCHANNEL && w.ViewAnim.Sequence(MVTCHANNEL) == ViewModelClips.FirstDrawStEd2)
            {
                if (w.ViewAnim.IsBackward(MVTCHANNEL))
                {
                    w.GotoState(CNAME_s_Selected);
                }
                else
                {
                    w.bDrawWeapon = false;
                }

                return;
            }

            w.AnimEnd(Channel);
        }
    }

    /// <summary>s_IdleGrabbed (extends s_IdleInHolster): hidden view model, the spread decay in script (rule 12).</summary>
    [Uc("SBase.SWeaponDefense.s_IdleGrabbed")]
    public class s_IdleGrabbed : s_IdleInHolster
    {
        public static new readonly s_IdleGrabbed Instance = new(UcMeta.s_IdleGrabbed, s_IdleInHolster.Instance);

        protected s_IdleGrabbed(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_IdleGrabbed.BeginState", Sha1 = "5d1b7f6d01d558a0a62cbf35176f26522fc38ee7")]
        [PortOf("uc:SBase.SWeaponDefense:s_IdleGrabbed.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self) => ((SWeaponDefense)self).bDrawWeapon = false;

        [Uc("SBase.SWeaponDefense.s_IdleGrabbed.Tick", Sha1 = "ea9f5b8573ae9bf372d04efedb740f90f8ee35c6")]
        [PortOf("uc:SBase.SWeaponDefense:s_IdleGrabbed.Tick", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void Tick(Actor self, float DeltaTime) => ((SWeaponDefense)self).NativeTickSWeaponDefense(DeltaTime);

        /// <summary>The draw clip after the grab returns to s_Selected.</summary>
        [Uc("SBase.SWeaponDefense.s_IdleGrabbed.AnimEnd", Sha1 = "224d3030d2304e31cf528033160927f22aff34cf")]
        [PortOf("uc:SBase.SWeaponDefense:s_IdleGrabbed.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        [Provenance(ProvenanceKind.Estimated, "s_IdleGrabbed.AnimEnd body not specced: the draw clip's end leaves the grab idle")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == MVTCHANNEL && w.ViewAnim.Sequence(MVTCHANNEL) == ViewModelClips.FirstDrawStEd2 &&
                w.ViewAnim.IsBackward(MVTCHANNEL))
            {
                w.GotoState(CNAME_s_Selected);
                return;
            }

            if (Channel != MVTCHANNEL)
            {
                w.AnimEnd(Channel);
            }
        }
    }

    /// <summary>s_Dmg_Electric: the shock loop on the pain channel, reticle and laser off (rule 21).</summary>
    [Uc("SBase.SWeaponDefense.s_Dmg_Electric")]
    public class s_Dmg_Electric : UcActorState
    {
        public static readonly s_Dmg_Electric Instance = new(UcMeta.s_Dmg_Electric, null);

        protected s_Dmg_Electric(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Dmg_Electric.BeginState", Sha1 = "f4f089499c7f905719dd47a533ebbd836393047d")]
        [PortOf("uc:SBase.SWeaponDefense:s_Dmg_Electric.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        [PortOf("uc:SBase.SWeaponDefense:s_Dmg_Electric.BeginState", Spec = "spec/equipment/sticky-shock-effect.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.ViewAnim.SetAlpha(PAINCHANNEL, 1f);
            w.ViewAnim.Loop(PAINCHANNEL, ViewModelClips.FirstElecStFd2);
            if (w.OwnerPC is { } pc)
            {
                pc.bNoReticule = true;
                pc.EteintLaser();
            }
        }

        [Uc("SBase.SWeaponDefense.s_Dmg_Electric.EndState", Sha1 = "dc141f42d87435e2b1c954f4202fec27b1cfa7be")]
        [PortOf("uc:SBase.SWeaponDefense:s_Dmg_Electric.EndState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void EndState(Actor self)
        {
            if (((SWeaponDefense)self).OwnerPC is { } pc)
            {
                if (!pc.bLaserEtaitAllume)
                {
                    pc.bNoReticule = false;
                }

                pc.RallumeLaserEteint();
            }
        }
    }

    /// <summary>s_Dmg_Electric_End: the shock loop blends out and the weapon is idle again.</summary>
    [Uc("SBase.SWeaponDefense.s_Dmg_Electric_End")]
    public class s_Dmg_Electric_End : UcActorState
    {
        public static readonly s_Dmg_Electric_End Instance = new(UcMeta.s_Dmg_Electric_End, null);

        protected s_Dmg_Electric_End(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Dmg_Electric_End.BeginState", Sha1 = "006db0db76732111b7c3a80ddacbdbf9a13fd155")]
        [PortOf("uc:SBase.SWeaponDefense:s_Dmg_Electric_End.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.ViewAnim.SetAlpha(PAINCHANNEL, 0f);
            w.ViewAnim.Stop(PAINCHANNEL);
            w.GotoState(CNAME_s_Selected);
        }
    }

    /// <summary>s_Sniping: the scope raise clip; at its end the controller zooms and the view model hides.</summary>
    [Uc("SBase.SWeaponDefense.s_Sniping")]
    public class s_Sniping : UcActorState
    {
        public static readonly s_Sniping Instance = new(UcMeta.s_Sniping, null);

        protected s_Sniping(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Sniping.BeginState", Sha1 = "25f2139b2290fb610a9bb802793a70af4ad69410")]
        [PortOf("uc:SBase.SWeaponDefense:s_Sniping.BeginState", Spec = "spec/equipment/merc-scope.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.ViewAnim.SetAlpha(SNIPECHANNEL, 1f);
            w.ViewAnim.Play(SNIPECHANNEL, ViewModelClips.FirstSnipStBg2, K.FirstSnipStBg2, K.SnipeClipRate);
        }

        [Uc("SBase.SWeaponDefense.s_Sniping.AnimEnd", Sha1 = "9eda158db1f4b5ced23869e2db5a262a2f8691da")]
        [PortOf("uc:SBase.SWeaponDefense:s_Sniping.AnimEnd", Spec = "spec/equipment/merc-scope.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == SNIPECHANNEL)
            {
                w.OwnerPC?.WeaponNotifiedSnipingDrawEnded();
                w.bDrawWeapon = false;
                return;
            }

            w.AnimEnd(Channel);
        }
    }
}
