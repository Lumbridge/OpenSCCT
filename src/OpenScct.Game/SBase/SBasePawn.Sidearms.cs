namespace OpenScct.Game.SBase;

public partial class SBasePawn
{
    public SWeapon? LastBulletWeapon;
    public SWeapon? PrimaryWeapon;
    public MercSidearm? SidearmWeapon;
    public int ActiveWeaponSlot;
    public float WeaponSwitchUntil;
    public bool WeaponFireLatch;
    public bool WeaponSwitching;
    public bool GearGrantSidearm;
    public int GearSidearmStartAmmo;
    public int GearSidearmStartClip;
}
