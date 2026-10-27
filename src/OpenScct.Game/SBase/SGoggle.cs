using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGoggle: the vision goggles of a local player (spec/equipment/vision-goggles.md). The mode codes are the
/// renderer's: 5 normal view, 34 night, 36 thermal (spies), 37 motion tracking, 38 EMF (mercs), 50 the spy
/// binoculars. GoggleUp is the raised state (normal view), GoggleDown the lowered one; the pawn's goggle clip calls
/// Down and Up at the frame where the view changes.
/// </summary>
public partial class SGoggle
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, GoggleDown.Instance, GoggleUp.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name CNAME_GoggleUp = new("GoggleUp");
    private static readonly Name CNAME_GoggleDown = new("GoggleDown");
    private static readonly Name CNAME_s_Active = new("s_Active");
    private static readonly Name CNAME_s_Inactive = new("s_Inactive");
    private static readonly ClassRef MotionTrackingClass = new("SBase.SMotionTrackingVision");

    /// <summary>The goggle state's version of <see cref="SwitchGoggle"/>.</summary>
    public interface ISwitchGoggle
    {
        void SwitchGoggle(SGoggle self, int Mode);
    }

    /// <summary>The goggle state's version of <see cref="Down"/> and <see cref="Up"/>.</summary>
    public interface IDownUp
    {
        void Down(SGoggle self);

        void Up(SGoggle self);
    }

    public void eventSwitchGoggle(int Mode)
    {
        if (StateNode is ISwitchGoggle s)
        {
            s.SwitchGoggle(this, Mode);
        }
        else
        {
            SwitchGoggle(Mode);
        }
    }

    public void eventDown()
    {
        if (StateNode is IDownUp s)
        {
            s.Down(this);
        }
        else
        {
            Down();
        }
    }

    public void eventUp()
    {
        if (StateNode is IDownUp s)
        {
            s.Up(this);
        }
        else
        {
            Up();
        }
    }

    [Uc("SBase.SGoggle.SwitchGoggle", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGoggle:SwitchGoggle", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void SwitchGoggle(int Mode)
    {
    }

    [Uc("SBase.SGoggle.Down", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGoggle:Down", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void Down()
    {
    }

    [Uc("SBase.SGoggle.Up", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGoggle:Up", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void Up()
    {
    }

    /// <summary>The goggles' pawn (the controller's pawn).</summary>
    public SBasePawn? GogglePawn => SPC?.Pawn as SBasePawn;

    /// <summary>True when the owner is a merc.</summary>
    public bool IsMerc => GogglePawn is SPAWNDEFENSE;

    /// <summary>The controller, a base on the pawn, the normal view (rule 1).</summary>
    [Uc("SBase.SGoggle.PostBeginPlay", Sha1 = "45506ee3b1cb9b67b812c9b25e7d1909b624565f")]
    [PortOf("uc:SBase.SGoggle:PostBeginPlay", Spec = "spec/equipment/vision-goggles.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        SPC = Owner as SPlayerController;
        if (SPC?.Pawn is { } pawn)
        {
            SetBase(pawn);
        }

        CurrentMode = REN_DynLight;
    }

    /// <summary>
    /// A mode key (rule 3): 1 and 2 are the team's two modes, 3 cycles normal to first to second to normal, 10 is the
    /// spy binoculars' view.
    /// </summary>
    [Uc("SBase.SGoggle.SwitchMode", Sha1 = "191935d6042dc3ce95cac0c8c19504a16113090e")]
    [PortOf("uc:SBase.SGoggle:SwitchMode", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void SwitchMode(int i)
    {
        bool merc = IsMerc;
        switch (i)
        {
            case 1:
                SwitchVisionMode(merc ? REN_MotionTrackingVision : REN_NightVision);
                break;
            case 2:
                SwitchVisionMode(merc ? REN_EMFVision : REN_ThermalVision);
                break;
            case 3:
                if (CurrentMode == REN_DynLight)
                {
                    SwitchMode(1);
                }
                else if (CurrentMode is REN_NightVision or REN_MotionTrackingVision)
                {
                    SwitchMode(2);
                }
                else
                {
                    eventSwitchGoggle(REN_DynLight);
                }

                break;
            case 10:
                if (!merc && GogglePawn is { } spy)
                {
                    spy.bBinocularsOn = !spy.bBinocularsOn;
                    SwitchVisionMode(REN_Binoculars);
                }

                break;
        }
    }

    /// <summary>A mode on, or off when it is already on (rule 4); thermal can be withheld.</summary>
    [Uc("SBase.SGoggle.SwitchVisionMode", Sha1 = "845e0c7d525b23bb31a541a69e2eed77e64bb3f1")]
    [PortOf("uc:SBase.SGoggle:SwitchVisionMode", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void SwitchVisionMode(int m)
    {
        if (m == REN_ThermalVision && bNoThermalAvailable)
        {
            return;
        }

        eventSwitchGoggle(CurrentMode != m ? m : REN_DynLight);
    }

    /// <summary>
    /// Back to the normal view (rule 9): at once without the clip; with a goggle clip already playing, at once with
    /// the raise forced; otherwise through the raise clip.
    /// </summary>
    [Uc("SBase.SGoggle.ResetVisionMode", Sha1 = "2d107ba1b671680b284f39b0adf38d60b421f2ef")]
    [PortOf("uc:SBase.SGoggle:ResetVisionMode", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void ResetVisionMode(bool bNoAnim = false)
    {
        SBasePawn? pawn = GogglePawn;
        if (bNoAnim)
        {
            CurrentMode = REN_DynLight;
            Activate();
            PawnAction(pawn, SBasePawn.SA_GOG_DYNNOANIM, false);
            GotoState(CNAME_GoggleUp);
            return;
        }

        if (pawn is not null && CombatServices.Ports(this).IsAnimating(pawn, K.GoggleChannel))
        {
            CurrentMode = REN_DynLight;
            PawnAction(pawn, SBasePawn.SA_GOG_HEADMVT_OFF, true);
            Activate();
            return;
        }

        eventSwitchGoggle(REN_DynLight);
        eventUp();
    }

    /// <summary>
    /// The mode takes effect (rule 8): the tracker idles, the mode's tracker or message starts, and the renderer
    /// switches to the mode. Sounds are view effects.
    /// </summary>
    [Uc("SBase.SGoggle.Activate", Sha1 = "6b4e1e50d7efbbab4b143d01441153d8073d3c83")]
    [PortOf("uc:SBase.SGoggle:Activate", Spec = "spec/equipment/vision-goggles.md#rules")]
    [PortOf("uc:SBase.SGoggle:Activate", Spec = "spec/equipment/motion-tracking.md#rules")]
    public void Activate()
    {
        SetTrackerState(CNAME_s_Inactive);
        CombatFx.Notify(this, CombatFx.GoggleSound, CurrentMode);
        PlayVisionSounds();
        switch (CurrentMode)
        {
            case REN_MotionTrackingVision:
                if (MotionTrackingVision is null &&
                    DeviceClasses.Spawn(this, MotionTrackingClass, this, default, Location, Rotation) is SMotionTrackingVision mt)
                {
                    mt.PlayerOwner = SPC;
                    MotionTrackingVision = mt;
                }

                SetTrackerState(CNAME_s_Active);
                VisionMessage(1);
                break;
            case REN_EMFVision:
                VisionMessage(3);
                break;
            case REN_DynLight when IsMerc && SPC is { } pc:
                if (pc.RendMap == REN_MotionTrackingVision)
                {
                    VisionMessage(2);
                }
                else if (pc.RendMap == REN_EMFVision)
                {
                    VisionMessage(4);
                }

                break;
        }

        if (SPC is { } owner)
        {
            owner.RendMap = CurrentMode;
        }
    }

    /// <summary>The pawn's vision action for the current mode (the goggle lights of other players follow it).</summary>
    [Uc("SBase.SGoggle.TellPawnAboutCurrentVisionMode", Sha1 = "6edaa2d369962d47687a89c1c64300778119e4f0")]
    [PortOf("uc:SBase.SGoggle:TellPawnAboutCurrentVisionMode", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void TellPawnAboutCurrentVisionMode()
    {
        int action = VisionAction(CurrentMode);
        if (action != 0)
        {
            PawnAction(GogglePawn, action, false);
        }
    }

    /// <summary>The SA_VISION action of a mode (0 for the normal view and the binoculars).</summary>
    public static int VisionAction(int Mode) => Mode switch
    {
        REN_NightVision => SBasePawn.SA_VISION_NIGHT,
        REN_ThermalVision => SBasePawn.SA_VISION_THERMAL,
        REN_MotionTrackingVision => SBasePawn.SA_VISION_MOTION,
        REN_EMFVision => SBasePawn.SA_VISION_EMF,
        _ => 0,
    };

    /// <summary>
    /// Back in normal view the merc's torch comes back on if it was on when the mode started and neither light is
    /// on now (merc-flashlight.md rule 10).
    /// </summary>
    private void RestoreTorch()
    {
        if (GogglePawn is SPAWNDEFENSE merc && bTorcheOn && merc.Torche is { bInUse: false } torch &&
            merc.TorcheLaser is not { bInUse: true })
        {
            torch.Use(0f);
        }
    }

    /// <summary>Entering a mode the merc's torch goes off and is remembered.</summary>
    private void StoreTorch()
    {
        if (GogglePawn is SPAWNDEFENSE { Torche: { bInUse: true } torch })
        {
            bTorcheOn = true;
            torch.Unload();
        }
        else
        {
            bTorcheOn = false;
        }
    }

    private void PawnAction(SBasePawn? pawn, int action, bool forced)
    {
        if (pawn is not null)
        {
            CombatServices.Ports(this).ChangeEnumAction(pawn, action, forced);
        }
    }

    private void TestEmf()
    {
        if (GogglePawn is { } pawn)
        {
            PawnVision.TestAndSetEMFVisible(pawn);
        }
    }

    /// <summary>The mode message on the owner's HUD: 1 MTOn, 2 MTOff, 3 EMFOn, 4 EMFOff (a view effect).</summary>
    private void VisionMessage(int Which) =>
        CombatFx.Notify(SPC ?? (Actor)this, CombatFx.GoggleMessage, K.VisionMessageTime, Which);

    private void SetTrackerState(Name State)
    {
        if (MotionTrackingVision is { } mt && mt.GetStateTable().Find(State) is not null)
        {
            mt.GotoState(State);
        }
    }

    /// <summary>GoggleUp (auto): goggles raised, normal view.</summary>
    [Uc("SBase.SGoggle.GoggleUp")]
    public class GoggleUp : UcActorState, ISwitchGoggle, IDownUp
    {
        public static readonly GoggleUp Instance = new(UcMeta.GoggleUp, null);

        protected GoggleUp(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Rule 5: back to normal at once (the torch restored); otherwise the merc's torch is stored, the vision
        /// action is sent and the goggles come down (the mode starts at the clip's Down notify).
        /// </summary>
        [Uc("SBase.SGoggle.GoggleUp.SwitchGoggle", Sha1 = "73d527bec9e6d3ddd654c9d040d52cb9a4d0a2aa")]
        [PortOf("uc:SBase.SGoggle:GoggleUp.SwitchGoggle", Spec = "spec/equipment/vision-goggles.md#rules")]
        public void SwitchGoggle(SGoggle self, int Mode)
        {
            self.SaveMode = self.CurrentMode;
            if (Mode != REN_Binoculars)
            {
                self.CurrentMode = Mode;
            }

            SBasePawn? pawn = self.GogglePawn;
            if (self.CurrentMode == REN_DynLight && pawn is not { bBinocularsOn: true })
            {
                if (self.SaveMode != REN_DynLight)
                {
                    self.RestoreTorch();
                }

                self.bTorcheOn = false;
                self.ResetVisionMode(true);
                self.TestEmf();
                return;
            }

            if (self.IsMerc)
            {
                self.StoreTorch();
            }

            self.TellPawnAboutCurrentVisionMode();
            self.PawnAction(pawn, SBasePawn.SA_GOG_HEADMVT_ON, false);
            if (Mode != REN_Binoculars)
            {
                self.TestEmf();
            }
        }

        [Uc("SBase.SGoggle.GoggleUp.Down", Sha1 = "b9742d902c5a7fac16bad151005285765601a39e")]
        [PortOf("uc:SBase.SGoggle:GoggleUp.Down", Spec = "spec/equipment/vision-goggles.md#rules")]
        public void Down(SGoggle self) => Lower(self);

        [Uc("SBase.SGoggle.GoggleUp.Up", Sha1 = "b109106e4f7f706135077a5efe95d596ae44c9ed")]
        [PortOf("uc:SBase.SGoggle:GoggleUp.Up", Spec = "spec/equipment/vision-goggles.md#rules")]
        public void Up(SGoggle self) => Lower(self);

        private static void Lower(SGoggle self)
        {
            self.Activate();
            if (self.CurrentMode != REN_DynLight || self.GogglePawn is { bBinocularsOn: true })
            {
                self.GotoState(CNAME_GoggleDown);
            }
        }
    }

    /// <summary>GoggleDown: goggles on the eyes, a mode active.</summary>
    [Uc("SBase.SGoggle.GoggleDown")]
    public class GoggleDown : UcActorState, ISwitchGoggle, IDownUp
    {
        public static readonly GoggleDown Instance = new(UcMeta.GoggleDown, null);

        protected GoggleDown(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// Rule 7: the binoculars off, or normal view, raise the goggles (the torch restored for a merc); another mode
        /// switches at once without the clip.
        /// </summary>
        [Uc("SBase.SGoggle.GoggleDown.SwitchGoggle", Sha1 = "b26f8a5159f17bc0b9168b46e143f9a98340ac9d")]
        [PortOf("uc:SBase.SGoggle:GoggleDown.SwitchGoggle", Spec = "spec/equipment/vision-goggles.md#rules")]
        public void SwitchGoggle(SGoggle self, int Mode)
        {
            if (Mode != REN_Binoculars)
            {
                self.SaveMode = self.CurrentMode;
                self.CurrentMode = Mode;
            }

            SBasePawn? pawn = self.GogglePawn;
            bool binoculars = pawn is { bBinocularsOn: true };
            if (Mode == REN_Binoculars && !binoculars)
            {
                if (self.CurrentMode == REN_DynLight)
                {
                    self.PawnAction(pawn, SBasePawn.SA_GOG_HEADMVT_OFF, false);
                }
            }
            else if (self.CurrentMode == REN_DynLight && !binoculars)
            {
                if (self.IsMerc)
                {
                    self.RestoreTorch();
                }
                else
                {
                    self.bTorcheOn = false;
                }

                self.PawnAction(pawn, SBasePawn.SA_GOG_HEADMVT_OFF, false);
            }
            else
            {
                self.TellPawnAboutCurrentVisionMode();
                self.PawnAction(pawn, SBasePawn.SA_GOG_SWITCH_WITHOUTANIM, false);
                if (Mode == REN_Binoculars)
                {
                    if (self.SPC is { } pc)
                    {
                        pc.RendMap = self.CurrentMode;
                    }
                }
                else
                {
                    self.Activate();
                }
            }

            if (Mode != REN_Binoculars)
            {
                self.TestEmf();
            }
        }

        [Uc("SBase.SGoggle.GoggleDown.Down", Sha1 = "855db8c2fd9e059989c65a3dbccfe45b492168ad")]
        [PortOf("uc:SBase.SGoggle:GoggleDown.Down", Spec = "spec/equipment/vision-goggles.md#rules")]
        public void Down(SGoggle self) => Raise(self);

        [Uc("SBase.SGoggle.GoggleDown.Up", Sha1 = "1a148af798679a8b7c846e0d9a1e42b9240ea8a8")]
        [PortOf("uc:SBase.SGoggle:GoggleDown.Up", Spec = "spec/equipment/vision-goggles.md#rules")]
        public void Up(SGoggle self) => Raise(self);

        private static void Raise(SGoggle self)
        {
            self.Activate();
            if (self.CurrentMode == REN_DynLight && self.GogglePawn is not { bBinocularsOn: true })
            {
                self.GotoState(CNAME_GoggleUp);
            }
        }
    }
}
