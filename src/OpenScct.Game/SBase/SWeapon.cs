using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeapon: the base of both sides' weapons (spec/equipment/merc-rifle.md rule 26). It owns the view-model
/// animation channels (their AnimEnd drives the weapon states) and the state dispatch of the weapon functions.
/// </summary>
public partial class SWeapon
{
    private static readonly Name CNAME_ServerSound = new("ServerSound");

    /// <summary>A weapon state's version of <see cref="Fire"/> (states that ignore it implement it empty).</summary>
    public interface IFire
    {
        void Fire(SWeapon self, float Value);
    }

    /// <summary>A weapon state's version of <see cref="AltFire"/>.</summary>
    public interface IAltFire
    {
        void AltFire(SWeapon self, float Value);
    }

    /// <summary>A weapon state's version of <see cref="Reload"/>.</summary>
    public interface IReload
    {
        bool Reload(SWeapon self);
    }

    /// <summary>A weapon state's version of <see cref="FullReload"/>.</summary>
    public interface IFullReload
    {
        bool FullReload(SWeapon self);
    }

    /// <summary>The view model's animation channels (first-person clips; their ends drive the weapon states).</summary>
    public ViewModelAnim ViewAnim { get; } = new();

    /// <summary>The owner's controller as an SPlayerController.</summary>
    public SPlayerController? OwnerPC => (Controller ?? Instigator?.Controller) as SPlayerController;

    // ---- shot numbers (fire prediction, spec/net/lag-compensation.md) -------------------------------------------

    /// <summary>
    /// The owner's client: the number of its last shot sent. The server: the number of the last shot received. Both
    /// start at 0 with the weapon, and shots travel reliably and in order, so the numbers agree.
    /// </summary>
    public int ShotCounter { get; private set; }

    /// <summary>The server, during one client shot: its number when it seeds the spread (0: the random stream).</summary>
    protected int ActiveShotId { get; private set; }

    /// <summary>The server, during one client shot: the player whose client already drew the shot's impacts, or null.</summary>
    protected PlayerController? PredictedBy { get; private set; }

    /// <summary>The owner's client: the number of a new shot.</summary>
    protected int NextShotId() => ++ShotCounter;

    /// <summary>
    /// The server, before a client's shot runs: a shot whose number follows the last one seeds the spread with it (a
    /// number out of order falls back to the random stream, so a client cannot pick its spread); a predicted shot's
    /// impacts are not sent back to its player.
    /// </summary>
    public void BeginClientShot(int ShotId, bool Predicted)
    {
        ActiveShotId = ShotId != 0 && ShotId == ShotCounter + 1 ? ShotId : 0;
        if (ShotId != 0)
        {
            ShotCounter = ShotId;
        }

        PredictedBy = Predicted ? OwnerPC : null;
    }

    /// <summary>The server, after the shot: back to the random stream.</summary>
    public void EndClientShot()
    {
        ActiveShotId = 0;
        PredictedBy = null;
    }

    /// <summary>
    /// Random call <paramref name="Call"/> of a trace: the shot's hashed number when <paramref name="ShotId"/> is set
    /// (the server's numbered shot or the client's prediction of it), else FRand.
    /// </summary>
    protected float SpreadRandom(int ShotId, int Aim, uint Side, int Call) =>
        ShotId != 0 ? ShotSpread.Unit(ShotId, Aim, Side, Call) : FRand();

    /// <summary>Fire through the current state.</summary>
    public void eventFire(float Value)
    {
        if (StateNode is IFire s)
        {
            s.Fire(this, Value);
        }
        else
        {
            Fire(Value);
        }
    }

    /// <summary>AltFire through the current state.</summary>
    public void eventAltFire(float Value)
    {
        if (StateNode is IAltFire s)
        {
            s.AltFire(this, Value);
        }
        else
        {
            AltFire(Value);
        }
    }

    /// <summary>Reload through the current state.</summary>
    public bool eventReload() => StateNode is IReload s ? s.Reload(this) : Reload();

    /// <summary>FullReload through the current state.</summary>
    public bool eventFullReload() => StateNode is IFullReload s ? s.FullReload(this) : FullReload();

    [Uc("SBase.SWeapon.Fire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeapon:Fire", Spec = "spec/equipment/merc-rifle.md#rules")]
    public virtual void Fire(float Value)
    {
    }

    [Uc("SBase.SWeapon.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeapon:AltFire", Spec = "spec/equipment/merc-rifle.md#rules")]
    public virtual void AltFire(float Value)
    {
    }

    [Uc("SBase.SWeapon.Reload", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeapon:Reload", Spec = "spec/equipment/merc-rifle.md#rules")]
    public virtual bool Reload() => false;

    [Uc("SBase.SWeapon.FullReload", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeapon:FullReload", Spec = "spec/equipment/merc-rifle.md#rules")]
    public virtual bool FullReload() => false;

    [Uc("SBase.SWeapon.ServerFireWeapon", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeapon:ServerFireWeapon", Spec = "spec/equipment/merc-rifle.md#rules")]
    [PortOf("rep:SBase.SWeapon.ServerFireWeapon", Spec = "spec/equipment/merc-rifle.md#network")]
    public virtual void ServerFireWeapon(int iBulletInARow = 0)
    {
    }

    /// <summary>
    /// A sound the owner plays immediately and relays reliably for other listeners.
    /// Authority-originated calls still reach the owner, who has not predicted those calls.
    /// </summary>
    [Uc("SBase.SWeapon.ServerSound", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeapon:ServerSound", Spec = "spec/equipment/merc-rifle.md#network")]
    public virtual void ServerSound(AssetRef MySound, ESoundSlot Slot = ESoundSlot.SLOT_None, int Radius = 0)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } pc)
        {
            (Instigator ?? (Actor)this).PlayOwnedSound(MySound, Slot, null, false, Radius == 0 ? (float?)null : Radius);
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ServerSound)
            {
                Bool1 = true, // Already played by the owner; relay only to other listeners.
                Target = this,
                Name1 = new Name(MySound.Path),
                Int1 = (int)Slot,
                Int2 = Radius,
            });
            return;
        }

        (Instigator ?? (Actor)this).PlaySound(MySound, Slot, null, false, Radius == 0 ? (float?)null : Radius);
    }

    /// <summary>Gives the weapon to a pawn: instigator, owner and the pawn's MyWeapon.</summary>
    [Uc("SBase.SWeapon.GiveTo", Sha1 = "e33436553072875953688dc7b456fec2379760b4")]
    [PortOf("uc:SBase.SWeapon:GiveTo", Spec = "spec/equipment/inventory-selection.md#rules")]
    public virtual void GiveTo(Pawn Other)
    {
        Instigator = Other;
        SetOwner(Other);
        if (Other is SBasePawn pawn)
        {
            pawn.MyWeapon = this;
            if (pawn is SPAWNDEFENSE && this is not MercSidearm) pawn.PrimaryWeapon = this;
            if (pawn is SPAWNDEFENSE && pawn.SidearmWeapon is not null)
                OpenScct.Game.Equipment.SidearmEquipment.FollowPawnRelevance(pawn, this);
            SBPawn = pawn;
        }

        Controller ??= Other.Controller;
    }

    /// <summary>The pawn reference on every machine and the EMF opacity of an undrawn weapon.</summary>
    [Uc("SBase.SWeapon.PostNetBeginPlay", Sha1 = "4e54525478f6903e86ca1791b79481d3cf6a38b9")]
    [PortOf("uc:SBase.SWeapon:PostNetBeginPlay", Spec = "spec/equipment/merc-rifle.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        SBPawn = Instigator as SBasePawn;
        UnSetEMFWeapon();
    }

    /// <summary>Drawn in EMF vision at full opacity.</summary>
    [Uc("SBase.SWeapon.SetEMFWeapon", Sha1 = "dba9e6d595c3c362c5c086449cc08f777ca5f27d")]
    [PortOf("uc:SBase.SWeapon:SetEMFWeapon", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void SetEMFWeapon()
    {
        bEMFIsOn = true;
        EMFOpacity = 1f;
    }

    /// <summary>Not drawn in EMF vision (0.3 opacity).</summary>
    [Uc("SBase.SWeapon.UnSetEMFWeapon", Sha1 = "8f593dadee3bdbf54798b5cf5f88c8b301710693")]
    [PortOf("uc:SBase.SWeapon:UnSetEMFWeapon", Spec = "spec/equipment/vision-goggles.md#rules")]
    public void UnSetEMFWeapon()
    {
        bEMFIsOn = false;
        EMFOpacity = EmfUndrawnOpacity;
    }

    [Provenance(ProvenanceKind.Decoded, "SWeapon.UnSetEMFWeapon EMFOpacity 0.3")]
    private const float EmfUndrawnOpacity = 0.3f;

    /// <summary>The eye point of the owner (the base GetFireStart).</summary>
    [Uc("SBase.SWeapon.GetFireStart", Sha1 = "f919ccce476fc2a7cc695b3021a92cb51adc8b3b")]
    [PortOf("uc:SBase.SWeapon:GetFireStart", Spec = "spec/equipment/merc-rifle.md#rules")]
    public virtual FVector GetFireStart() =>
        Instigator is { } pawn ? pawn.Location + CombatServices.Ports(this).EyePosition(pawn) : Location;

    /// <summary>The view-model channels advance every tick; ends raise AnimEnd through the weapon's state.</summary>
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        OpenScct.Game.Equipment.SidearmEquipment.Tick(this);
        if (this is SWeaponDefense carried) carried.TickCarriedAmmo();
        ViewAnim.Advance(DeltaSeconds, this, static (a, c) => a.eventAnimEnd(c));
    }
}
