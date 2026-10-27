using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The pawn functions of the weapons and pad-up gadgets: the merc's torch and laser (default gadgets, the pad-up
/// switch, the laser flag), and the body animation notifies that draw and holster each side's weapon. They are
/// SPAWNDEFENSE and SPawnAttaque units kept with the equipment port.
/// </summary>
public static class PawnWeapons
{
    private static readonly ClassRef TorcheClass = new("SBase.SGadTorche");
    private static readonly ClassRef TorcheLaserClass = new("SBase.SGadTorcheLaser");
    private static readonly Name NAME_s_InteractWithObject = new("s_InteractWithObject");
    private static readonly Name NAME_s_Reloading = new("s_Reloading");

    /// <summary>
    /// The merc's pad-up gadgets (merc-flashlight.md rule 1): a torch and a laser spawned at the pawn on the server
    /// and given to it.
    /// </summary>
    [Uc("SBase.SPAWNDEFENSE.AddDefaultGadgets", Sha1 = "742717eb1f6b196be08b366104a9101cc79bbc85")]
    [PortOf("uc:SBase.SPAWNDEFENSE:AddDefaultGadgets", Spec = "spec/equipment/merc-flashlight.md#rules")]
    public static void AddDefaultGadgets(SPAWNDEFENSE Merc)
    {
        if (DeviceClasses.Spawn(Merc, TorcheClass, Merc, default, Merc.Location, Merc.Rotation) is SGadTorche torch)
        {
            torch.GiveTo(Merc);
            Merc.Torche = torch;
        }

        if (DeviceClasses.Spawn(Merc, TorcheLaserClass, Merc, default, Merc.Location, Merc.Rotation) is SGadTorcheLaser laser)
        {
            laser.GiveTo(Merc);
            Merc.TorcheLaser = laser;
        }
    }

    /// <summary>
    /// The merc's pad-up switch (merc-flashlight.md rule 3, merc-laser.md rule 2): the torch is refused while
    /// interacting or in a vision mode (motion and EMF hand over to the torch), the laser while scoped or interacting
    /// (motion and EMF are reset first); the other pad-up gadget goes off.
    /// </summary>
    [Uc("SBase.SPAWNDEFENSE.PadUpGadgetSwitch", Sha1 = "5ae4dec7c9fa3850ffceac41d375a2d717746853")]
    [PortOf("uc:SBase.SPAWNDEFENSE:PadUpGadgetSwitch", Spec = "spec/equipment/merc-flashlight.md#rules")]
    [PortOf("uc:SBase.SPAWNDEFENSE:PadUpGadgetSwitch", Spec = "spec/equipment/merc-laser.md#rules")]
    public static void PadUpGadgetSwitch(SPAWNDEFENSE Merc, ClassRef GadgetClass)
    {
        var pc = Merc.Controller as SPlayerController;
        SGoggle? goggle = pc?.Goggle;
        int mode = goggle?.CurrentMode ?? SGoggle.REN_DynLight;
        bool interacting = pc?.IsInState(NAME_s_InteractWithObject) ?? false;
        bool motionOrEmf = mode is SGoggle.REN_MotionTrackingVision or SGoggle.REN_EMFVision;
        if (GadgetClass == TorcheClass)
        {
            if (interacting || mode != SGoggle.REN_DynLight)
            {
                if (motionOrEmf)
                {
                    Merc.Torche?.Use(0f);
                    if (goggle is not null)
                    {
                        goggle.bTorcheOn = false;
                        goggle.ResetVisionMode();
                    }
                }
                else
                {
                    Merc.Torche?.Unload();
                    Merc.TorcheLaser?.Unload();
                }

                return;
            }

            Merc.TorcheLaser?.Unload();
            Merc.Torche?.Load();
            Merc.Torche?.Use(0f);
            return;
        }

        if (GadgetClass != TorcheLaserClass)
        {
            return;
        }

        if ((pc?.bBinocularVision ?? false) || interacting)
        {
            if (!motionOrEmf)
            {
                Merc.Torche?.Unload();
                Merc.TorcheLaser?.Unload();
                return;
            }

            goggle?.ResetVisionMode();
        }

        if (Merc.MyWeapon is { } weapon && weapon.IsInState(NAME_s_Reloading))
        {
            return;
        }

        Merc.Torche?.Unload();
        Merc.TorcheLaser?.Load();
        Merc.TorcheLaser?.Use(0f);
    }

    /// <summary>The merc's laser flag on (it moves the server's shot origin to the gun).</summary>
    [Uc("SBase.SPAWNDEFENSE.LaserON", Sha1 = "daa2f8be30855955521e5723bd3488c05c673a5f")]
    [PortOf("uc:SBase.SPAWNDEFENSE:LaserON", Spec = "spec/equipment/merc-laser.md#rules")]
    public static void LaserON(SPAWNDEFENSE Merc) => Merc.bLaserOn = true;

    /// <summary>The merc's laser flag off.</summary>
    [Uc("SBase.SPAWNDEFENSE.LaserOFF", Sha1 = "d4c256fd2c54aa692c0df8a0068b1e6007341287")]
    [PortOf("uc:SBase.SPAWNDEFENSE:LaserOFF", Spec = "spec/equipment/merc-laser.md#rules")]
    public static void LaserOFF(SPAWNDEFENSE Merc) => Merc.bLaserOn = false;

    /// <summary>The merc's quick draw or holster notify (merc-rifle.md rule 25).</summary>
    [Uc("SBase.SPAWNDEFENSE.NotifyDrawUndrawWeapon", Sha1 = "05981c343bb76ec8e21fa554591af877a2ae28c0")]
    [PortOf("uc:SBase.SPAWNDEFENSE:NotifyDrawUndrawWeapon", Spec = "spec/equipment/merc-rifle.md#rules")]
    public static void MercNotifyDrawUndrawWeapon(SPAWNDEFENSE Merc)
    {
        var rifle = Merc.MyWeapon as SWeaponDefense;
        if (Merc.WeaponLoaded == 0)
        {
            Merc.WeaponLoaded = 2;
            rifle?.eventAnimateDraw();
        }
        else
        {
            Merc.WeaponLoaded = 0;
            rifle?.eventAnimateUnDraw();
        }
    }

    /// <summary>The merc's weapon lowered for a grab.</summary>
    [Uc("SBase.SPAWNDEFENSE.NotifyUnDrawWeapon_GRAB", Sha1 = "0230bbdd9e45ad9e399b3f00ef1223a3b4c231af")]
    [PortOf("uc:SBase.SPAWNDEFENSE:NotifyUnDrawWeapon_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
    public static void NotifyUnDrawWeapon_GRAB(SPAWNDEFENSE Merc)
    {
        Merc.WeaponLoaded = 0;
        (Merc.MyWeapon as SWeaponDefense)?.eventAnimateUnDraw_GRAB();
    }

    /// <summary>The merc's weapon raised after a grab.</summary>
    [Uc("SBase.SPAWNDEFENSE.NotifyDrawWeapon_GRAB", Sha1 = "e3b954470567cbc124dc454df9c578b4365e9237")]
    [PortOf("uc:SBase.SPAWNDEFENSE:NotifyDrawWeapon_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
    public static void NotifyDrawWeapon_GRAB(SPAWNDEFENSE Merc)
    {
        Merc.WeaponLoaded = 2;
        (Merc.MyWeapon as SWeaponDefense)?.eventAnimateDraw();
    }

    /// <summary>The spy's draw notify: the pistol's draw clip (spy-shocker.md rule 4).</summary>
    [Uc("SBase.SPawnAttaque.NotifyDrawWeapon", Sha1 = "0b14ae14054e430a7f85356f862c8e586cabf676")]
    [PortOf("uc:SBase.SPawnAttaque:NotifyDrawWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public static void NotifyDrawWeapon(SPawnAttaque Spy) => (Spy.MyWeapon as SWeaponAttaque)?.AnimateDrawWeapon();

    /// <summary>The spy's holster notify: the laser off on the server, the draw clip backward (rule 5).</summary>
    [Uc("SBase.SPawnAttaque.NotifyUndrawWeapon", Sha1 = "7cd0d0acd027ae3cfbeaa58e24021af514904f1f")]
    [PortOf("uc:SBase.SPawnAttaque:NotifyUndrawWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public static void NotifyUndrawWeapon(SPawnAttaque Spy)
    {
        if (Spy.MyWeapon is SWeaponAttaque pistol)
        {
            pistol.ServerSwitchLaser(false);
            pistol.AnimateUnDrawWeapon();
        }
    }

    /// <summary>The spy's quick draw or holster (traversal): 0 to 1 with the idle clip, 1, 2 or 7 to 0 with the laser off.</summary>
    [Uc("SBase.SPawnAttaque.NotifyDrawUndrawWeapon", Sha1 = "fcd12440aaa6f1549cb4aedfb621c8fda328e690")]
    [PortOf("uc:SBase.SPawnAttaque:NotifyDrawUndrawWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public static void SpyNotifyDrawUndrawWeapon(SPawnAttaque Spy)
    {
        if (Spy.MyWeapon is not SWeaponAttaque pistol)
        {
            return;
        }

        if (Spy.WeaponLoaded == 0)
        {
            pistol.AnimateQuickDrawWeapon();
        }
        else if (Spy.WeaponLoaded is 1 or 2 or 7)
        {
            pistol.ServerSwitchLaser(false);
            pistol.AnimateQuickUnDrawWeapon();
        }
    }

    /// <summary>The spy's other-hand notify: the pistol swaps between the aiming (2) and other hand (7) positions.</summary>
    [Uc("SBase.SPawnAttaque.NotifyChangeHandWeapon", Sha1 = "cfc857ddb53051ff5ac5a177a66a67060b06f12f")]
    [PortOf("uc:SBase.SPawnAttaque:NotifyChangeHandWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public static void NotifyChangeHandWeapon(SPawnAttaque Spy) => (Spy.MyWeapon as SWeaponAttaque)?.ChangeHandWeapon();

    /// <summary>The level's combat ports for a pawn.</summary>
    internal static ICombatPorts Ports(Actor a) => CombatServices.Ports(a);
}
