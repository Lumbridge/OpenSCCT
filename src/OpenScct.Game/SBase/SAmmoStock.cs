using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SAmmoStock: an ammo crate that refills a merc's weapon and bomb (spec/devices/infirmary-ammo.md).</summary>
public partial class SAmmoStock
{
    [Uc("SBase.SAmmoStock.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SAmmoStock:AcceptStickableGPO", Spec = "spec/devices/infirmary-ammo.md#rules")]
    public override bool AcceptStickableGPO() => false;

    /// <summary>Shown in thermal and EMF vision.</summary>
    [Uc("SBase.SAmmoStock.PostNetBeginPlay", Sha1 = "525a774bad50da0b13cc710c2d6452640e6ebd36")]
    [PortOf("uc:SBase.SAmmoStock:PostNetBeginPlay", Spec = "spec/devices/infirmary-ammo.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        bEMFIsOn = true;
        EMFIntensity = 0.6f;
        EMFOpacity = 1f;
        bSideFadeEffect = true;
        HeatIntensity = 0.6f;
        HeatOpacity = 1f;
    }

    /// <summary>The user's controller reloads.</summary>
    [Uc("SBase.SAmmoStock.Use", Sha1 = "f8980b0254b22ebf7017c61080c4a792ca565d9e")]
    [PortOf("uc:SBase.SAmmoStock:Use", Spec = "spec/devices/infirmary-ammo.md#rules")]
    public void Use(SPlayerController? Instigator) => Instigator?.ReloadAmmoStock();
}
