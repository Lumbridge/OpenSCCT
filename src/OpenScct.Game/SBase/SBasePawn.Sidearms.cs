namespace OpenScct.Game.SBase;

public partial class SBasePawn
{
    public SWeapon? LastBulletWeapon;
    public SWeapon? PrimaryWeapon;
    public MercSidearm? SidearmWeapon;
    public int ActiveWeaponSlot;
    // A remote viewer may receive only the primary dummy, without its first-person actor.
    public int PrimaryAttachmentMask;
    public float WeaponSwitchUntil;
    public bool WeaponFireLatch;
    public bool WeaponSwitching;
    public bool GearGrantSidearm;
    public int GearSidearmStartAmmo;
    public int GearSidearmStartClip;
}
