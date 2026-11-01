using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The SPlayerController functions the map devices call: camera shake, the merc charge on gameplay objects, the
// ammo crate, the camera network limits and hand-over.
public partial class SPlayerController
{
    private static readonly ClassRef GadBombClass = new("SBase.SGadBomb");

    private static readonly AssetRef AmmoStockSound = new("Armes_impacts.Armes.Munitions_caisses1");

    /// <summary>
    /// Shakes this player's view (sent reliably from the server to the owning client): the camera's tilt and/or
    /// shake with the given strength, speed and fade-out. Pad rumble is not used by the versus maps.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientShake", Sha1 = "04fbbe39cf585e7c8c9f568e76951990322dab30")]
    [PortOf("uc:SBase.SPlayerController:ClientShake", Spec = "spec/devices/shaker.md#rules")]
    public virtual void ClientShake(int strenght, int speed, int FadeOut, bool bTilt, bool bShake, bool bRumble = false)
    {
        if (bTilt)
        {
            m_camera?.Tilt(strenght, speed, FadeOut);
        }

        if (bShake)
        {
            m_camera?.Shake(strenght, speed, FadeOut);
        }
    }

    /// <summary>The merc charge on a gameplay object (client to server): damage from this player's pawn.</summary>
    [Uc("SBase.SPlayerController.DestroyGPO", Sha1 = "9c9c0cf0dbb07238dca8783961150ecb34b3815e")]
    [PortOf("uc:SBase.SPlayerController:DestroyGPO", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public void DestroyGPO(SBasicGamePlayObject? gpo, int Damage, SDamageType Type) =>
        gpo?.eventTakeDamage(Damage, Pawn, FVector.Zero, new FVector(0f, 0f, 1f), Type, 0);

    /// <summary>
    /// The ammo crate (server to the owning client, where it runs): a merc, or anyone carrying a bomb gadget (which is
    /// refilled and recharged), gets the weapon refilled and the crate sound. A practice server's <c>!ammo</c> sends it too.
    /// </summary>
    [Uc("SBase.SPlayerController.ReloadAmmoStock", Sha1 = "49fdd95af9f7448491c021b759791241a7afd45d")]
    [PortOf("uc:SBase.SPlayerController:ReloadAmmoStock", Spec = "spec/devices/infirmary-ammo.md#rules")]
    public void ReloadAmmoStock()
    {
        if (IsRemotePlayer)
        {
            if (SBPawn is SPAWNDEFENSE carried)
            {
                carried.SidearmWeapon?.FullReload();
                if ((carried.PrimaryWeapon ?? carried.MyWeapon) is SWeaponDefense primary)
                {
                    primary.CancelCarriedReload();
                    primary.Ammo = primary.MaxAmmo; primary.ClipAmmo = primary.ClipMaxAmmo; primary.PublishCarriedAmmo();
                }
            }
            GameHost.ClientCall(this, new GameCall(CNAME_ReloadAmmoStock));
            return;
        }

        if (SBPawn is not { } pawn)
        {
            return;
        }

        IDeviceHooks? hooks = XLevel.GetService<IDeviceHooks>();
        bool reloaded = false;
        if (pawn is SPAWNDEFENSE)
        {
            if (myHUD is SHud hud)
            {
                hud.bReticuleFlash = false;
            }

            reloaded = true;
        }

        if (hooks?.FindGadgetType(pawn, GadBombClass) is SGadget gadget)
        {
            reloaded = true;
            hooks.FullReload(gadget);
            if (gadget.GetUcClass().Create() is SGadget defaults)
            {
                gadget.Charge = defaults.Charge;
            }
        }

        if (reloaded)
        {
            if (pawn.MyWeapon is { } weapon)
            {
                hooks?.FullReload(weapon);
                if (pawn.PrimaryWeapon is { } primary && !ReferenceEquals(primary, weapon)) hooks?.FullReload(primary);
                if (pawn.SidearmWeapon is { } sidearm && !ReferenceEquals(sidearm, weapon)) hooks?.FullReload(sidearm);
            }

            PlayOwnedSound(AmmoStockSound, ESoundSlot.SLOT_Misc, 1f, false, DeviceK.AmmoSoundRadius);
        }
    }

    private static readonly Name CNAME_ReloadAmmoStock = new("ReloadAmmoStock");

    /// <summary>
    /// Clamps a camera angle to within [Min, Max] of its limit centre, the short way round.
    /// </summary>
    [Uc("SBase.SPlayerController.ClampRotation", Sha1 = "f4194c0aba37c26c4d52df3eb7ffdad820112ffa")]
    [PortOf("uc:SBase.SPlayerController:ClampRotation", Spec = "spec/devices/cam-network.md#turning-and-limits")]
    public static int ClampRotation(int Value, int Target, int Min, int Max)
    {
        Value &= 65535;
        Target &= 65535;
        if (Target > Value)
        {
            return Target - Value > 32768
                ? (Value - System.Math.Abs(SvmMath.Clamp(Value + 65536 - Target, 0, -Min))) & 65535
                : (Value + System.Math.Abs(SvmMath.Clamp(Target - Value, 0, Max))) & 65535;
        }

        if (Value > Target)
        {
            return Value - Target > 32768
                ? (Value + System.Math.Abs(SvmMath.Clamp(Target + 65536 - Value, 0, Max))) & 65535
                : (Value - System.Math.Abs(SvmMath.Clamp(Value - Target, 0, -Min))) & 65535;
        }

        // The decoded script falls through to zero here. Keep an unchanged centre stable instead: entering a
        // placed camera with no mouse input must not turn it toward world yaw zero.
        return Value;
    }

    /// <summary>
    /// Leaves the network camera this player controls: control passes to another merc watching the same camera,
    /// or the camera's zoom and vision are reset and it becomes free.
    /// </summary>
    [Uc("SBase.SPlayerController.FreeCurrentCamera", Sha1 = "0c89ee5c417c2ff08e8fb4efebcd24e8cbe7ad16")]
    [PortOf("uc:SBase.SPlayerController:FreeCurrentCamera", Spec = "spec/devices/cam-network.md#ring-and-entry")]
    public void FreeCurrentCamera()
    {
        if (SBPawn?.UsedObject is not SCamNetwork cam || !ReferenceEquals(cam.aUsedByPawn, SBPawn))
        {
            return;
        }

        foreach (SBasePawn other in XLevel.AllActors<SBasePawn>())
        {
            if (other.Controller is SPlayerController pc && !ReferenceEquals(pc, this) && pc.SPawnType != PawnType.SPAWNOFFENSE &&
                ReferenceEquals(other.UsedObject, cam))
            {
                cam.aUsedByPawn = other;
                return;
            }
        }

        cam.fVisionFOV = DefaultFOV;
        cam.fOldVisionFOV = cam.fVisionFOV;

        // CamNetworkSwitchVisionModeOnServer(cam, -1) on the controlling pawn ends in the camera's SwitchToVision.
        cam.SwitchToVision(-1);
        cam.aUsedByPawn = null;
    }
}
