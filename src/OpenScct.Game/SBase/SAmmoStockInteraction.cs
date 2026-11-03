using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SAmmoStockInteraction: the ammo crate's use entry (spec/devices/infirmary-ammo.md).</summary>
public partial class SAmmoStockInteraction
{
    /// <summary>Reloads at the owning crate, then the usual interaction start.</summary>
    [Uc("SBase.SAmmoStockInteraction.InitInteract", Sha1 = "0a33bf706c9838e2d6d14acb97dbaa5ccd3d93a3")]
    [PortOf("uc:SBase.SAmmoStockInteraction:InitInteract", Spec = "spec/devices/infirmary-ammo.md#rules")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (Role == OpenScct.Core.Engine.ENetRole.ROLE_Authority && Owner is SAmmoStock &&
            Instigator.Pawn is SPAWNDEFENSE { Health: > 0 } && Instigator.PlayerReplicationInfo?.Stats is { } stats)
            stats.Merc_AmmoStocksUsed++;
        (Owner as SAmmoStock)?.Use(Instigator);
        base.InitInteract(Instigator, SubInteraction);
    }
}
