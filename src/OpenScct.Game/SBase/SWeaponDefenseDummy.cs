using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeaponDefenseDummy: the merc's third-person rifle (spec/equipment/merc-rifle.md rule 22). The server counts
/// the shots in the replicated MuzzleCount; every drawing machine plays the muzzle effects once per new count for
/// the mercs it does not control.
/// </summary>
public partial class SWeaponDefenseDummy
{
    private static readonly Name BoneRightHand = new("B R Hand");
    private static readonly Name BoneHolster = new("HOLSTER");

    /// <summary>The dummy's attachment: 1 in the right hand, 0 at the holster (iState).</summary>
    public bool InHand => iState == 1;

    /// <summary>One shot more for the clients' muzzle effects (server).</summary>
    [Uc("SBase.SWeaponDefenseDummy.AddMuzzleCount", Sha1 = "b85178f8ef6bb5706707d67ade14236c9958b026")]
    [PortOf("uc:SBase.SWeaponDefenseDummy:AddMuzzleCount", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AddMuzzleCount() => MuzzleCount += 1;

    /// <summary>The clients start in step with the server's count (no effects for shots fired before).</summary>
    [Uc("SBase.SWeaponDefenseDummy.PostNetBeginPlay", Sha1 = "19a7f368ae83ae60613d7a239b63a53b059f3779")]
    [PortOf("uc:SBase.SWeaponDefenseDummy:PostNetBeginPlay", Spec = "spec/equipment/merc-rifle.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "PostNetBeginPlay body not specced: the local count starts at the replicated one")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        MyMuzzleCount = MuzzleCount;
    }

    /// <summary>
    /// Rule 22: the attachment follows WeaponLoaded (not while lying down); not on a dedicated server, each new count
    /// plays the muzzle effects when the merc has no first-person weapon on this machine, or is a remote player on a
    /// listen server.
    /// </summary>
    [Uc("SBase.SWeaponDefenseDummy.Tick", Sha1 = "bd648732ede2db378dbb723f7be3b5f3352a17de")]
    [PortOf("uc:SBase.SWeaponDefenseDummy:Tick", Spec = "spec/equipment/merc-rifle.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        base.Tick(DeltaTime);
        if (Instigator is SBasePawn pawn && !pawn.bLyingOnGround)
        {
            int want = pawn.WeaponLoaded > 0 ? 1 : 0;
            if (want != iState)
            {
                iState = want;
                CombatFx.Notify(this, want == 1 ? BoneRightHand : BoneHolster);
            }
        }

        TickMuzzle();
    }

    /// <summary>The tick's muzzle step: each new count plays the muzzle effects where nobody sees the first-person gun.</summary>
    private protected virtual void TickMuzzle()
    {
        if (Level.NetMode == ENetMode.NM_DedicatedServer)
        {
            return;
        }

        while (MyMuzzleCount < MuzzleCount)
        {
            MyMuzzleCount += 1;
            bool remoteHere = Instigator?.Controller is SPlayerController { IsRemotePlayer: true };
            bool carriedRemote = Instigator is SBasePawn { SidearmWeapon: not null } carried &&
                !ReferenceEquals(carried, Level.GetLocalPlayerController()?.Pawn);
            if (Instigator is SBasePawn { MyWeapon: null } || remoteHere || carriedRemote)
            {
                ClientFireMuzzle();
            }
        }
    }

    /// <summary>The third-person muzzle flash, smoke and casing at the dummy's FX bone (a view effect).</summary>
    [Uc("SBase.SWeaponDefenseDummy.ClientFireMuzzle", Sha1 = "0ca7f3c45460827e62f40fd966a88fda6ed01d23")]
    [PortOf("uc:SBase.SWeaponDefenseDummy:ClientFireMuzzle", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void ClientFireMuzzle() => CombatFx.Notify(this, CombatFx.ThirdPersonMuzzle);
}
