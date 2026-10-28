using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using CK = OpenScct.Game.Combat.K;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController weapon and gadget input: the fire and alternate fire keys, the rate of fire, the pad-up
// torch and laser, the goggles and the scope keys, the gadget keys and the wheel, the muzzle clearance test, the
// temporary laser cut, the rumbles, and the gadget, energy and cloak parts of PlayerTick. Written from
// spec/equipment/merc-rifle.md, spy-shocker.md, grenade-throw.md, inventory-selection.md, merc-flashlight.md,
// merc-laser.md, vision-goggles.md, merc-scope.md and spy-energy.md.
//
// Each exec function asks the current state first (its interface), then the state's metadata (ignores, an empty
// override, or a shared body ported here), then runs the class version.
public partial class SPlayerController
{
    private static readonly Name CNAME_s_ElbowShocking = new("s_ElbowShocking");
    private static readonly Name CNAME_s_Reloading = new("s_Reloading");
    private static readonly Name CNAME_s_FullReloading = new("s_FullReloading");
    private static readonly Name CNAME_s_FireGrenade = new("s_FireGrenade");
    private static readonly Name CNAME_s_PlayerWalking = new("s_PlayerWalking");
    private static readonly ClassRef CREF_SGadTorche = new("SBase.SGadTorche");
    private static readonly ClassRef CREF_SGadTorcheLaser = new("SBase.SGadTorcheLaser");

    /// <summary>The shared body of the walking states' GadgetQuickChoice (spy quick launch, grenade-throw.md rule 5).</summary>
    private const string Sha1MarioQuickChoice = "f646de2e2ab9c9bb7d531ba40c00ba60a3960ba9";

    /// <summary>The shared body of the weapon states' SwitchLaser (spy-shocker.md rule 6).</summary>
    private const string Sha1WeaponSwitchLaser = "c7a4b9666591efeb2990524b26078f1157137c90";

    private bool _cloakButtonDown;

    public interface IFire
    {
        void Fire(SPlayerController self, float F);
    }

    public interface IAltFire
    {
        void AltFire(SPlayerController self, float F);
    }

    public interface IGadgetQuickChoice
    {
        void GadgetQuickChoice(SPlayerController self, int i);
    }

    public interface IGadLaserSwitch
    {
        void GadLaserSwitch(SPlayerController self);
    }

    public interface IGadTorcheSwitch
    {
        void GadTorcheSwitch(SPlayerController self);
    }

    public interface ISwitchBinocularVision
    {
        void SwitchBinocularVision(SPlayerController self);
    }

    public interface ISwitchHeadset
    {
        void SwitchHeadset(SPlayerController self, int i);
    }

    public interface ISwitchROF
    {
        void SwitchROF(SPlayerController self);
    }

    public interface ICanActivateCloak
    {
        bool CanActivateCloak(SPlayerController self);
    }

    public interface IZoom
    {
        void ZoomIn(SPlayerController self);

        void ZoomOut(SPlayerController self);
    }

    public interface IWheel
    {
        void WheelUp(SPlayerController self);

        void WheelDown(SPlayerController self);
    }

    public interface ISwitchLaser
    {
        void SwitchLaser(SPlayerController self);
    }

    public interface IWeaponNotifiedSnipingDrawEnded
    {
        void WeaponNotifiedSnipingDrawEnded(SPlayerController self);
    }

    public interface IClientStopBinoculars
    {
        void ClientStopBinoculars(SPlayerController self);
    }

    // ---- dispatch -------------------------------------------------------------------------------------------

    private bool StateRunsGlobal(string function, out string? sha1) =>
        CombatStateEvents.Lookup(this, function, out sha1) == CombatStateEvents.StateFunction.Global;

    /// <summary>The Fire key (exec).</summary>
    public void eventFire(float F)
    {
        if (StateNode is IFire s)
        {
            s.Fire(this, F);
        }
        else if (StateRunsGlobal("Fire", out _))
        {
            Fire(F);
        }
    }

    /// <summary>The alternate fire key (exec).</summary>
    public void eventAltFire(float F)
    {
        if (StateNode is IAltFire s)
        {
            s.AltFire(this, F);
        }
        else if (StateRunsGlobal("AltFire", out _))
        {
            AltFire(F);
        }
    }

    /// <summary>The quick gadget keys (exec GadgetQuickChoice 0 and 1).</summary>
    public void eventGadgetQuickChoice(int i)
    {
        if (StateNode is IGadgetQuickChoice s)
        {
            s.GadgetQuickChoice(this, i);
            return;
        }

        var kind = CombatStateEvents.Lookup(this, "GadgetQuickChoice", out string? sha1);
        if (kind == CombatStateEvents.StateFunction.Unported && sha1 == Sha1MarioQuickChoice)
        {
            MarioGadgetQuickChoice(i);
        }
    }

    public void eventGadLaserSwitch()
    {
        if (StateNode is IGadLaserSwitch s)
        {
            s.GadLaserSwitch(this);
        }
        else if (StateRunsGlobal("GadLaserSwitch", out _))
        {
            GadLaserSwitch();
        }
    }

    public void eventGadTorcheSwitch()
    {
        if (StateNode is IGadTorcheSwitch s)
        {
            s.GadTorcheSwitch(this);
        }
        else if (StateRunsGlobal("GadTorcheSwitch", out _))
        {
            GadTorcheSwitch();
        }
    }

    public void eventSwitchBinocularVision()
    {
        if (StateNode is ISwitchBinocularVision s)
        {
            s.SwitchBinocularVision(this);
        }
        else if (StateRunsGlobal("SwitchBinocularVision", out _))
        {
            SwitchBinocularVision();
        }
    }

    public void eventSwitchHeadset(int i)
    {
        if (StateNode is ISwitchHeadset s)
        {
            s.SwitchHeadset(this, i);
        }
        else if (StateRunsGlobal("SwitchHeadset", out _))
        {
            SwitchHeadset(i);
        }
    }

    public void eventSwitchROF()
    {
        if (StateNode is ISwitchROF s)
        {
            s.SwitchROF(this);
        }
        else if (StateRunsGlobal("SwitchROF", out _))
        {
            SwitchROF();
        }
    }

    public bool eventCanActivateCloak()
    {
        if (StateNode is ICanActivateCloak s)
        {
            return s.CanActivateCloak(this);
        }

        return CombatStateEvents.Lookup(this, "CanActivateCloak", out _) switch
        {
            CombatStateEvents.StateFunction.Ignored => false,
            _ => CanActivateCloak(),
        };
    }

    public void eventZoomIn()
    {
        if (StateNode is IZoom s)
        {
            s.ZoomIn(this);
        }
    }

    public void eventZoomOut()
    {
        if (StateNode is IZoom s)
        {
            s.ZoomOut(this);
        }
    }

    public void eventWheelUp()
    {
        if (StateNode is IWheel s)
        {
            s.WheelUp(this);
        }
        else if (StateRunsGlobal("WheelUp", out _))
        {
            WheelUp();
        }
    }

    public void eventWheelDown()
    {
        if (StateNode is IWheel s)
        {
            s.WheelDown(this);
        }
        else if (StateRunsGlobal("WheelDown", out _))
        {
            WheelDown();
        }
    }

    /// <summary>The laser key (exec SwitchLaser): only the weapon states toggle the spy laser.</summary>
    public void eventSwitchLaser()
    {
        if (StateNode is ISwitchLaser s)
        {
            s.SwitchLaser(this);
            return;
        }

        if (CombatStateEvents.Lookup(this, "SwitchLaser", out string? sha1) == CombatStateEvents.StateFunction.Unported &&
            sha1 == Sha1WeaponSwitchLaser)
        {
            WeaponSwitchLaser();
        }
    }

    public void eventWeaponNotifiedSnipingDrawEnded()
    {
        if (StateNode is IWeaponNotifiedSnipingDrawEnded s)
        {
            s.WeaponNotifiedSnipingDrawEnded(this);
        }
    }

    /// <summary>The weapon calls this at the end of the scope raise clip.</summary>
    public void WeaponNotifiedSnipingDrawEnded() => eventWeaponNotifiedSnipingDrawEnded();

    // ---- class versions ----------------------------------------------------------------------------------

    /// <summary>
    /// The Fire key (merc-rifle.md rule 1): refused in inventory mode, with frozen input, shocked or in weapon transit;
    /// drops a carried pickup, then fires the drawn weapon.
    /// </summary>
    [Uc("SBase.SPlayerController.Fire", Sha1 = "09974fb120c6d63ba205fb3c312149e4ea7b7569")]
    [PortOf("uc:SBase.SPlayerController:Fire", Spec = "spec/equipment/merc-rifle.md#rules")]
    [PortOf("uc:SBase.SPlayerController:Fire", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void Fire(float F)
    {
        if (bIsInInventoryMode || iDontUpdatePadInput != 0 || SBPawn is not { } pawn || pawn.bStickyShocking || pawn.bWeaponTransit)
        {
            return;
        }

        MovementService.HooksOf(this).DiscardPickup(this);
        if (pawn.WeaponLoaded != 0)
        {
            pawn.MyWeapon?.eventFire(F);
        }
    }

    /// <summary>
    /// The alternate fire key (grenade-throw.md rule 1, inventory-selection.md rule 8): a grenade starts the charged
    /// throw (or clicks when empty), the taser fires from the idle rifle, any other gadget is used at full value when
    /// the muzzle is clear.
    /// </summary>
    [Uc("SBase.SPlayerController.AltFire", Sha1 = "7325c791da8d97a20de38bd1f48bf892bb155c64")]
    [PortOf("uc:SBase.SPlayerController:AltFire", Spec = "spec/equipment/grenade-throw.md#rules")]
    [PortOf("uc:SBase.SPlayerController:AltFire", Spec = "spec/equipment/inventory-selection.md#rules")]
    public void AltFire(float F)
    {
        if (SBPawn is not { } pawn || PawnInventory.TeamSlots(pawn) <= 0)
        {
            return;
        }

        if (bIsInInventoryMode || iDontUpdatePadInput != 0 || pawn.bWeaponTransit || pawn.bStickyShocking)
        {
            return;
        }

        switch (pawn.SelectedGadget)
        {
            case SGadGrenLauncher grenade:
                if (pawn.WeaponLoaded != 0 && grenade.CanUseGadget())
                {
                    if (!bUsingNow)
                    {
                        TimerUSe = Level.TimeSeconds;
                        bUsingNow = true;
                    }
                }
                else if (grenade.Charge <= 0)
                {
                    pawn.PlaySound(grenade.SND_EmptyCharge, ESoundSlot.SLOT_Misc, null, false, K.EmptyChargeRadius);
                }

                break;
            case SGadTazerLauncher taser:
                if (pawn.MyWeapon is { } weapon && weapon.IsInState(CNAME_s_Selected))
                {
                    taser.Use(1f);
                }

                break;
            case { } gadget:
                if (CheckIfCanFire())
                {
                    gadget.Use(1f);
                }

                break;
        }
    }

    /// <summary>
    /// A scripted turn (test pilots, tools): the view, and for a merc also the aim, since his mouse moves the aim and the
    /// view is set to it every frame (input-and-rotation.md, UpdateRotationAttaquant_Mouse).
    /// </summary>
    public void SetViewRotation(FRotator View)
    {
        Rotation = View;
        if (SBPawn is SPAWNDEFENSE)
        {
            MyDesiredRotation = View;
            LocalMyDesiredRotation = View;
        }
    }

    /// <summary>
    /// The reload and rate-of-fire buttons of weapon walking (merc-rifle.md rules 16 and 17), read every frame after
    /// the Use edge: a held reload button reloads unless the weapon is already reloading; the rate-of-fire button
    /// switches once per press (bChangeROF is the edge), and both are ignored in inventory mode.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.PlayerMove", Spec = "spec/equipment/merc-rifle.md#rules")]
    internal void WeaponWalkingButtons(SBasePawn pawn)
    {
        if (bReload > 0 && !bIsInInventoryMode && pawn.MyWeapon is { } weapon && !weapon.IsInState(WNAME_s_Reloading))
        {
            weapon.eventReload();
        }

        if (bROF > 0)
        {
            if (!bIsInInventoryMode && !bChangeROF)
            {
                bChangeROF = true;
                eventSwitchROF();
            }
        }
        else
        {
            bChangeROF = false;
        }
    }

    /// <summary>The rate-of-fire key (merc-rifle.md rule 16): not in inventory mode, with frozen input or shocked.</summary>
    [Uc("SBase.SPlayerController.SwitchROF", Sha1 = "335895574b98f28a93d81cae7982461e9131abf9")]
    [PortOf("uc:SBase.SPlayerController:SwitchROF", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void SwitchROF()
    {
        if (bIsInInventoryMode || iDontUpdatePadInput != 0 || SBPawn is not { bStickyShocking: false } pawn)
        {
            return;
        }

        (pawn.MyWeapon as SWeaponDefense)?.eventSwitchROF();
    }

    /// <summary>
    /// The muzzle clearance test (merc-rifle.md rule 3, spy-shocker.md rule 8): a merc fires when the 18 uu segment
    /// 56 to 38 uu behind the FX bone is clear; a spy when the segment from the FX bone back 40 uu (walking) or 0.9 of
    /// its distance to the pawn is clear.
    /// </summary>
    [Uc("SBase.SPlayerController.CheckIfCanFire", Sha1 = "9bcfec694220a5609b98de5bda9915e09aa2856d")]
    [PortOf("uc:SBase.SPlayerController:CheckIfCanFire", Spec = "spec/equipment/merc-rifle.md#rules")]
    [PortOf("uc:SBase.SPlayerController:CheckIfCanFire", Spec = "spec/equipment/spy-shocker.md#rules")]
    public bool CheckIfCanFire()
    {
        if (SBPawn is not { MyWeapon: { } weapon } pawn || TraceHitZones.CollisionOf(this) is not { } world)
        {
            return true;
        }

        FCoords fx = CombatServices.Ports(this).BoneCoords(weapon, SWeaponDefense.FxBone);
        FVector e = fx.Origin;
        FVector d = fx.XAxis.SafeNormal();
        FVector start;
        FVector end;
        if (pawn is SPAWNDEFENSE)
        {
            start = e - d * K.MuzzleClearanceFar;
            end = e - d * K.MuzzleClearanceNear;
        }
        else
        {
            float length = IsInState(CNAME_s_PlayerWalking) ? K.SpyMuzzleWalking : K.SpyMuzzleScale * FVector.Dist(e, pawn.Location);
            start = e - d * length;
            end = e;
        }

        return !world.LineCheck(out _, pawn, end, start, FVector.Zero, ETraceFlags.TRACE_AllColliding);
    }

    /// <summary>
    /// The laser key (merc-laser.md rule 1): the torch key's guards; the reticle flag from the spy pad-up gadget; then
    /// the pad-up switch to the laser.
    /// </summary>
    [Uc("SBase.SPlayerController.GadLaserSwitch", Sha1 = "d8711baa2defb0192fcc45c77e0abcc4796c75e8")]
    [PortOf("uc:SBase.SPlayerController:GadLaserSwitch", Spec = "spec/equipment/merc-laser.md#rules")]
    public void GadLaserSwitch()
    {
        if (!PadUpKeyAllowed(out SBasePawn? pawn))
        {
            return;
        }

        bNoReticule = pawn!.PadUpGadget is { } padUp && !PawnInventory.IsA(padUp, CREF_SGadTorcheLaser);
        PadUpSwitch(pawn, CREF_SGadTorcheLaser);
    }

    /// <summary>The torch key (merc-flashlight.md rule 2): the pad-up switch to the torch.</summary>
    [Uc("SBase.SPlayerController.GadTorcheSwitch", Sha1 = "09162a030a22800b553b8fdad65ec88a23b1bde6")]
    [PortOf("uc:SBase.SPlayerController:GadTorcheSwitch", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public void GadTorcheSwitch()
    {
        if (PadUpKeyAllowed(out SBasePawn? pawn))
        {
            PadUpSwitch(pawn!, CREF_SGadTorche);
        }
    }

    private bool PadUpKeyAllowed(out SBasePawn? pawn)
    {
        pawn = SBPawn;
        if (pawn is null || iDontUpdatePadInput != 0 || pawn.bStickyShocking)
        {
            return false;
        }

        return pawn.MyWeapon is not { } w || !(w.IsInState(CNAME_s_ElbowShocking) || w.IsInState(CNAME_s_Reloading) ||
            w.IsInState(CNAME_s_FullReloading) || w.IsInState(CNAME_s_FireGrenade));
    }

    private static void PadUpSwitch(SBasePawn pawn, ClassRef cls)
    {
        if (pawn is SPAWNDEFENSE merc)
        {
            PawnWeapons.PadUpGadgetSwitch(merc, cls);
        }
        else
        {
            PawnInventory.PadUpGadgetSwitch(pawn, cls);
        }
    }

    /// <summary>
    /// The goggle keys (vision-goggles.md rule 2): refused when paused, shocked, with frozen input or for a withheld
    /// mode; the cycle key's shortcuts when only one mode is available; refused with message 7 while the HUD is
    /// perturbed, snowed or jammed.
    /// </summary>
    [Uc("SBase.SPlayerController.SwitchHeadset", Sha1 = "e042d01279ac101fb5f0e320ab54b99ebbdb614a")]
    [PortOf("uc:SBase.SPlayerController:SwitchHeadset", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void SwitchHeadset(int i)
    {
        if (Level.Pauser is not null || Level.TimeSeconds < iTimeToStopStickyShock || iDontUpdatePadInput > 0 || Goggle is not { } goggle)
        {
            return;
        }

        bool merc = SBPawn is SPAWNDEFENSE;
        GameReplicationInfo? gri = GameReplicationInfo;
        bool first = gri is null || (merc ? gri.bAvlb_MERC_MotiontrackingVision : gri.bAvlb_SPY_NightVision);
        bool second = gri is null || (merc ? gri.bAvlb_MERC_EMFVision : gri.bAvlb_SPY_ThermalVision);
        if ((i == 1 && !first) || (i == 2 && !second))
        {
            return;
        }

        bool handled = false;
        if (i == 3)
        {
            if (!first && !second)
            {
                return;
            }

            int firstMode = merc ? SGoggle.REN_MotionTrackingVision : SGoggle.REN_NightVision;
            if (!first && goggle.CurrentMode == SGoggle.REN_DynLight)
            {
                goggle.SwitchMode(2);
                handled = true;
            }
            else if (!second && goggle.CurrentMode == firstMode)
            {
                goggle.eventSwitchGoggle(SGoggle.REN_DynLight);
                handled = true;
            }
        }

        if (IsHudPerturbed || myHUD is SHud { bNeigeDisp: true } || bJammered)
        {
            CombatFx.Notify(this, CombatFx.LocalMessage, K.VisionMessageTime, K.MsgVisionRefused);
            return;
        }

        if (!handled)
        {
            goggle.SwitchMode(i);
        }
    }

    /// <summary>
    /// The scope key (merc-scope.md rule 1): with the goggle clip idle, a state that allows it, not shocked and live
    /// input, a merc whose rifle is idle and can snipe enters s_Binoculars_Def; pressed again it asks to leave. A spy
    /// with the pistol out only asks to holster (spy-shocker.md rule 5); a holstered spy takes the binoculars (spy-binoculars.md).
    /// </summary>
    [Uc("SBase.SPlayerController.SwitchBinocularVision", Sha1 = "6b58b59f0fe370d208093723d05ac1c7cb5f9802")]
    [PortOf("uc:SBase.SPlayerController:SwitchBinocularVision", Spec = "spec/equipment/merc-scope.md#rules")]
    public void SwitchBinocularVision()
    {
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn is not SPAWNDEFENSE)
        {
            if (pawn.WeaponLoaded != 0)
            {
                bForceExitFireMode = true;
            }
            else
            {
                SpySwitchBinocularVision();
            }

            return;
        }

        if (CombatServices.Ports(this).IsAnimating(pawn, K.GoggleChannel) || !CombatStateEvents.CanIUseBinocular(this) ||
            Level.TimeSeconds <= iTimeToStopStickyShock || iDontUpdatePadInput != 0)
        {
            return;
        }

        if (bBinocularVision)
        {
            bBinocularVision = false;
            return;
        }

        if (bFireMode != 0 || pawn.MyWeapon is not SWeaponDefense { bCanSnipe: true } rifle || !rifle.IsInState(CNAME_s_Selected) ||
            pawn.bStickyShocking || IsHudPerturbed)
        {
            return;
        }

        RPreviousState = GetStateName();
        eventChangeState(CNAME_s_Binoculars_Def);
        CombatServices.Ports(this).ChangeEnumAction(pawn, pawn.bIsCrouched ? SBasePawn.SA_WALK_CR_2H : SBasePawn.SA_WALK_ST_2H, false);
        bBinocularVision = true;
    }

    [Uc("SBase.SPlayerController.GadgetQuickChoice", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:GadgetQuickChoice", Spec = "spec/equipment/grenade-throw.md#rules")]
    public void GadgetQuickChoice(int i)
    {
    }

    /// <summary>
    /// The unarmed walking states' quick gadget (grenade-throw.md rule 5): a grenade with charges asks the walking
    /// state to stand and holster before the quick throw; the cloak toggles.
    /// </summary>
    [Uc("SBase.SPlayerController.s_PlayerWalkingMario.GadgetQuickChoice", Sha1 = "f646de2e2ab9c9bb7d531ba40c00ba60a3960ba9")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingMario.GadgetQuickChoice", Spec = "spec/equipment/grenade-throw.md#rules")]
    public void MarioGadgetQuickChoice(int i)
    {
        if (SBPawn is not SPawnAttaque spy || i >= PawnInventory.TeamSlots(spy))
        {
            return;
        }

        if (i == 1 && spy.SelectedGadget is SGadCloak)
        {
            return;
        }

        float now = Level.TimeSeconds;
        if (fDateFireMode != 0f || now <= fDateLaunchedLastQuickGadget + K.QuickLaunchCooldown)
        {
            return;
        }

        SelectGadget(i);
        switch (spy.SelectedGadget)
        {
            case SGadGrenLauncher grenade when grenade.CanQuickLaunch && (grenade.Charge > 0 || grenade.Charge == K.UnlimitedCharge) &&
                (!spy.bIsCrouched || spy.CanUnCrouch()):
                bUnCrouchAndUnDraw = 1;
                bQuickGadgetQuery = true;
                break;
            case SGadCloak cloak:
                cloak.QuickUse();
                break;
        }
    }

    /// <summary>
    /// The notify of the quick throw clip (grenade-throw.md rule 6): a spy with a quick-launchable grenade uncloaks
    /// over 2 s and throws at the feet.
    /// </summary>
    [Uc("SBase.SPlayerController.RealGadgetQuickLaunch", Sha1 = "57b63a0a9df9113b27e88fc6956a3cb58e306532")]
    [PortOf("uc:SBase.SPlayerController:RealGadgetQuickLaunch", Spec = "spec/equipment/grenade-throw.md#rules")]
    public void RealGadgetQuickLaunch()
    {
        if (SBPawn is SPawnAttaque spy && spy.SelectedGadget is SGadGrenLauncher { CanQuickLaunch: true } grenade &&
            (grenade.Charge > 0 || grenade.Charge == K.UnlimitedCharge || GadgetWorld.CurLevelIsTutorial(spy)))
        {
            PawnVision.SetDesiredGlassValue(spy, 0, K.DrawUncloakTime);
            grenade.QuickUse();
        }
    }

    /// <summary>
    /// A gadget key (inventory-selection.md rule 4): slots 0 to 3, refused while knocked down; the selection goes to
    /// slot num and is loaded.
    /// </summary>
    [Uc("SBase.SPlayerController.SelectGadget", Sha1 = "746de0597985dad6f82289be3c967cbe2a281deb")]
    [PortOf("uc:SBase.SPlayerController:SelectGadget", Spec = "spec/equipment/inventory-selection.md#rules")]
    public void SelectGadget(int num)
    {
        if (num < 0 || num > K.GadgetSlots - 1 || SBPawn is not { } pawn || PawnInventory.SelectionRefused(pawn, false))
        {
            return;
        }

        PawnInventory.SelectSlot(pawn, num);
    }

    /// <summary>The wheel (inventory-selection.md rule 7): the next gadget with the default mouse setting, else run.</summary>
    [Uc("SBase.SPlayerController.WheelUp", Sha1 = "26c19afa3bf27e4f9ca49bf2b0cb9c657183ae27")]
    [PortOf("uc:SBase.SPlayerController:WheelUp", Spec = "spec/equipment/inventory-selection.md#rules")]
    public void WheelUp()
    {
        if (SBPawn is { } pawn && WheelSelectsGadgets(pawn))
        {
            PawnInventory.NextGadget(pawn);
            pawn.SelectedGadget?.Load();
        }
        else
        {
            bFastSpeed = true;
        }
    }

    /// <summary>The wheel down: the previous gadget with the default mouse setting, else walk.</summary>
    [Uc("SBase.SPlayerController.WheelDown", Sha1 = "6a38f6a97dd6d392cdaaa3a4bc942f19d37cf1d1")]
    [PortOf("uc:SBase.SPlayerController:WheelDown", Spec = "spec/equipment/inventory-selection.md#rules")]
    public void WheelDown()
    {
        if (SBPawn is { } pawn && WheelSelectsGadgets(pawn))
        {
            PawnInventory.PrevGadget(pawn);
        }
        else
        {
            bFastSpeed = false;
        }
    }

    private bool WheelSelectsGadgets(SBasePawn pawn) =>
        (XLevel.GetService<IDeviceHooks>()?.GetPlayerProfile()?.SelectedMouse ?? LocalProfileOptions()?.SelectedMouse ?? 0) == 0 &&
        PawnInventory.TeamSlots(pawn) > 0;

    /// <summary>The weapon states' laser key: the spy laser toggles on the server (spy-shocker.md rule 6).</summary>
    [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.SwitchLaser", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void WeaponSwitchLaser()
    {
        if (SBPawn?.MyWeapon is SWeaponAttaque pistol)
        {
            pistol.ServerSwitchLaser(!pistol.bLaserOn);
        }
    }

    /// <summary>The cloak is allowed in any state that does not refuse it (spy-energy.md rule 4).</summary>
    [Uc("SBase.SPlayerController.CanActivateCloak", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
    [PortOf("uc:SBase.SPlayerController:CanActivateCloak", Spec = "spec/equipment/spy-energy.md#rules")]
    public bool CanActivateCloak() => true;

    /// <summary>
    /// The spy's draw snap (spy-shocker.md rule 3): from the pelvis 40 uu up, the nearest visible awake merc beyond
    /// 200 uu whose aim rotation (to his pelvis 15 uu up, offset 12 uu sideways) is within 6000 units of the camera in
    /// yaw and pitch. Returns that rotation with the pitch scaled by 1.133 and sets bAttTargetEnemy.
    /// </summary>
    [Uc("SBase.SPlayerController.FindAttClosestTarget", Sha1 = "955dbd5c2e6889bd2d59a809c3a2bf0d156227e1")]
    [PortOf("uc:SBase.SPlayerController:FindAttClosestTarget", Spec = "spec/equipment/spy-shocker.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The pelvis is the pawn location (no skeletal pose); the found rotation is returned to the draw state")]
    public bool FindAttClosestTarget(out FRotator Aim)
    {
        Aim = Rotation;
        bAttTargetEnemy = false;
        if (SBPawn is not { } me)
        {
            return false;
        }

        ICollisionWorld? world = TraceHitZones.CollisionOf(this);
        FVector from = me.Location + new FVector(0f, 0f, K.SnapFromRaise);
        FRotator view = Rotation.Normalize();
        float best = float.MaxValue;
        foreach (Pawn p in XLevel.DynamicActors<Pawn>())
        {
            if (p is not SPAWNDEFENSE merc || ReferenceEquals(merc, me) || merc.bSleeping)
            {
                continue;
            }

            FVector to = merc.Location + new FVector(0f, 0f, K.SnapToRaise);
            FVector d = to - from;
            float dist = d.Size();
            if (!(dist > K.SnapMinRange) || !(dist < best) || (world is not null && !world.FastTrace(to, from)))
            {
                continue;
            }

            FVector flat = new FVector(d.X, d.Y, 0f).SafeNormal();
            FRotator aim = (d + (flat ^ new FVector(0f, 0f, 1f)) * K.SnapSideOffset).Rotation();
            aim.Roll = 0;
            aim = aim.Normalize();
            int dy = FRotator.NormalizeAxis(aim.Yaw - view.Yaw);
            int dp = FRotator.NormalizeAxis(aim.Pitch - view.Pitch);
            if ((dy < 0 ? -dy : dy) >= K.SnapCone || (dp < 0 ? -dp : dp) >= K.SnapCone)
            {
                continue;
            }

            best = dist;
            Aim = aim;
            bAttTargetEnemy = true;
        }

        if (bAttTargetEnemy)
        {
            Aim.Pitch = SvmMath.Trunc(Aim.Pitch * K.SnapPitchFactor);
        }

        return bAttTargetEnemy;
    }

    // ---- laser cut and rumble ---------------------------------------------------------------------------

    /// <summary>
    /// The temporary laser cut (merc-laser.md rule 5) for reloads, throws, shocks and interactions: on the merc's
    /// controlling machine, a lit laser is remembered and turned off without the message.
    /// </summary>
    [Uc("SBase.SPlayerController.EteintLaser", Sha1 = "277cc592faf7f402a95709b18d4bf0c89f449456")]
    [PortOf("uc:SBase.SPlayerController:EteintLaser", Spec = "spec/equipment/merc-laser.md#rules")]
    public void EteintLaser()
    {
        if (SBPawn is SPAWNDEFENSE { TorcheLaser: { bInUse: true } laser } && RemoteRole != ENetRole.ROLE_AutonomousProxy)
        {
            bLaserEtaitAllume = true;
            laser.Use(1f);
        }
    }

    /// <summary>The laser back on after a cut (merc-laser.md rule 5), with the reticle hidden.</summary>
    [Uc("SBase.SPlayerController.RallumeLaserEteint", Sha1 = "dad7271c1c9afeaa36430b4cd087592da31d2365")]
    [PortOf("uc:SBase.SPlayerController:RallumeLaserEteint", Spec = "spec/equipment/merc-laser.md#rules")]
    public void RallumeLaserEteint()
    {
        if (!bLaserEtaitAllume)
        {
            return;
        }

        bNoReticule = true;
        (SBPawn as SPAWNDEFENSE)?.TorcheLaser?.Use(1f);
        bLaserEtaitAllume = false;
    }

    /// <summary>The shot rumble (a view effect).</summary>
    [Uc("SBase.SPlayerController.Vibration_Tir", Sha1 = "b760a4a4979f9b1a8d662849f688ff46fdbacaf4")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Tir", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void Vibration_Tir() => CombatFx.Notify(this, CombatFx.Vibration_Tir);

    /// <summary>The gadget rumble for <paramref name="Length"/> seconds (a view effect).</summary>
    [Uc("SBase.SPlayerController.Vibration_Gadget", Sha1 = "3142faf77456ef465a49d3d1421fcfac7fa51824")]
    [PortOf("uc:SBase.SPlayerController:Vibration_Gadget", Spec = "spec/equipment/grenade-throw.md#rules")]
    public void Vibration_Gadget(float Length) => CombatFx.Notify(this, CombatFx.Vibration_Gadget, Length);

    // ---- PlayerTick parts --------------------------------------------------------------------------------

    /// <summary>
    /// The gadget, energy and cloak parts of the global PlayerTick on the local player's machine: the inventory mode
    /// cleared, the item cycling keys, the throw gauge and release, the spy's energy, the cloak button (the cloak ramp
    /// runs in the pawn tick).
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/inventory-selection.md#rules")]
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/grenade-throw.md#rules")]
    [PortOf("uc:SBase.SPlayerController:PlayerTick", Spec = "spec/equipment/spy-energy.md#rules")]
    public void EquipmentPlayerTick(float DeltaTime)
    {
        bIsInInventoryMode = false;
        if (SBPawn is not { } pawn)
        {
            return;
        }

        float now = Level.TimeSeconds;
        ItemCycling(pawn, now);
        ThrowCharge(pawn, now);
        if (pawn is SPawnAttaque spy)
        {
            SpyEnergy.ComputeEnergyLevel(spy, DeltaTime);
            CloakButton(spy, now);
        }

        GadgetPlayerTick(DeltaTime);
    }

    /// <summary>Inventory rule 7: the next and previous item keys repeat every 0.2 s while held.</summary>
    [Provenance(ProvenanceKind.Estimated, "The repeat time is kept in TimeChoiceGadget (the spec does not name the field)")]
    private void ItemCycling(SBasePawn pawn, float now)
    {
        if ((BT_NextItem == 0 && BT_PrevItem == 0) || now - TimeChoiceGadget < K.ItemCycleRepeat || PawnInventory.TeamSlots(pawn) <= 0)
        {
            return;
        }

        TimeChoiceGadget = now;
        if (BT_NextItem != 0)
        {
            PawnInventory.NextGadget(pawn);
            pawn.SelectedGadget?.Load();
        }
        else
        {
            PawnInventory.PrevGadget(pawn);
        }
    }

    /// <summary>
    /// Grenade-throw rule 2: the gauge while the alternate fire is held; on release the throw at the gauge value when
    /// the muzzle is clear. A selection changed to another gadget during the charge uses that gadget instead.
    /// </summary>
    private void ThrowCharge(SBasePawn pawn, float now)
    {
        if (!bUsingNow)
        {
            return;
        }

        if (pawn.SelectedGadget is not SGadGrenLauncher grenade)
        {
            if (bAltFire == 0)
            {
                bUsingNow = false;
                AltFire(0f);
            }

            return;
        }

        float e = now - TimerUSe;
        float v = WeaponRules.ThrowValue(e, pawn.WeaponLoaded != 0, K.ThrowTapWindow, K.ThrowRamp);
        if (e >= K.ThrowTapWindow)
        {
            CombatFx.Notify(this, CombatFx.SetBallisticValue, v);
        }

        if (bAltFire != 0)
        {
            return;
        }

        bUsingNow = false;
        if (CheckIfCanFire())
        {
            grenade.Use(v);
        }

        CombatFx.Notify(this, CombatFx.SetBallisticValue, 0f);
    }

    /// <summary>
    /// Spy-energy rule 6: with the cloak selected the hold-breath button toggles it: a long press at 0.2 s when it
    /// can turn on, a release after 0.2 s, or a short tap that turns it off (else the quick gadget of slot 1).
    /// </summary>
    private void CloakButton(SPawnAttaque spy, float now)
    {
        bool down = bHoldBreath != 0;
        if (spy.SelectedGadget is not SGadCloak cloak)
        {
            _cloakButtonDown = down;
            return;
        }

        if (down && !_cloakButtonDown)
        {
            fLastPressedUseButton = now;
        }
        else if (down && fLastPressedUseButton > 0f && now - fLastPressedUseButton > K.CloakButtonHold && cloak.CanTurnOn())
        {
            fLastPressedUseButton = 0f;
            cloak.QuickUse();
        }
        else if (!down && _cloakButtonDown && fLastPressedUseButton > 0f)
        {
            float held = now - fLastPressedUseButton;
            fLastPressedUseButton = 0f;
            if (held > K.CloakButtonHold || cloak.CanTurnOff())
            {
                cloak.QuickUse();
            }
            else
            {
                PawnInventory.SelectGagdet(spy, 1);
                if (spy.SelectedGadget is SGadCloak again)
                {
                    again.QuickUse();
                }
                else
                {
                    eventGadgetQuickChoice(1);
                }
            }
        }

        _cloakButtonDown = down;
    }

    /// <summary>
    /// The combat and equipment parts of PlayerTick (shock, HUD, gadgets, smoke) for the player controlled on this
    /// machine, run from the controller's tick after the movement layer's PlayerTick.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Runs after the movement PlayerTick in the same frame, not inside it")]
    protected override void Tick(float DeltaTime)
    {
        base.Tick(DeltaTime);
        if (MovementService.Of(this) is not null && Player is UViewport && SBPawn is not null)
        {
            EnsureGoggle();
            CombatPlayerTick(DeltaTime);
        }
    }

    /// <summary>
    /// The local player's goggles (vision-goggles.md rule 1): one SGoggle owned by the controller on every machine
    /// that draws (never on a dedicated server), spawned the first time the local player ticks with a pawn.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:PostBeginPlay", Spec = "spec/equipment/vision-goggles.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The goggles spawn at the first local tick with a pawn instead of in PostBeginPlay (not ported here)")]
    public SGoggle? EnsureGoggle()
    {
        if (Goggle is null && Level.NetMode != ENetMode.NM_DedicatedServer &&
            XLevel.SpawnActor(new SGoggle(), Location, Rotation, this) is { } goggle)
        {
            goggle.SPC = this;
            if (Pawn is { } pawn)
            {
                goggle.SetBase(pawn);
            }

            Goggle = goggle;
        }

        return Goggle;
    }
}
