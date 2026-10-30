using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadget: an item of the pawn's gadget list (spec/equipment/inventory-selection.md). Gadgets beyond the
/// slice (sticky cam, heartbeat sensor, mines, camera network, gas mask...) use this base behaviour: they take a
/// slot, can be selected and keep their charges, and their Use does nothing until their own port exists.
/// </summary>
public partial class SGadget
{
    private static readonly Name CNAME_ServerDecCharge = new("ServerDecCharge");
    private static readonly Name CNAME_ServerAddCharge = new("ServerAddCharge");
    private static readonly Name CNAME_FullReload = new("FullReload");

    private SGadget? _classDefaults;

    /// <summary>The class default object (UnrealScript <c>default.X</c>).</summary>
    public SGadget ClassDefaults => _classDefaults ??= GetUcClass().Create() as SGadget ?? this;

    /// <summary>The owner pawn of the gadget list, or null.</summary>
    public SBasePawn? OwnerPawn => Owner as SBasePawn ?? Instigator as SBasePawn;

    /// <summary>Gives the gadget to a pawn: instigator and owner, then the pawn's AddGadget.</summary>
    [Uc("SBase.SGadget.GiveTo", Sha1 = "59af1126c36030dc5b22ebfc53b0ae8d8b645d8d")]
    [PortOf("uc:SBase.SGadget:GiveTo", Spec = "spec/equipment/inventory-selection.md#rules")]
    public virtual void GiveTo(Pawn Other)
    {
        Instigator = Other;
        SetOwner(Other);
        if (Other is SBasePawn pawn)
        {
            PawnInventory.AddGadget(pawn, this);
        }
    }

    /// <summary>
    /// On each machine: the first activatable, non pad-up gadget becomes the selection when the owner has none;
    /// tutorial maps make the charge unlimited and an offline game raises a charge in 1..998 to 999.
    /// </summary>
    [Uc("SBase.SGadget.PostNetBeginPlay", Sha1 = "7ce8005a60b3fee4866cca0dd2d3ca5e634e4f3f")]
    [PortOf("uc:SBase.SGadget:PostNetBeginPlay", Spec = "spec/equipment/inventory-selection.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        if (OwnerPawn is { SelectedGadget: null } pawn && bActivatable && !IsAPadUpGadget)
        {
            pawn.SelectedGadget = this;
        }

        if (Level.NetMode == ENetMode.NM_Standalone && Charge > 0 && Charge < K.OfflineCharge)
        {
            Charge = K.OfflineCharge;
        }
    }

    /// <summary>Removes itself from the owner's gadget list.</summary>
    [Uc("SBase.SGadget.Destroyed", Sha1 = "264bfb82c4422b6537d1e8eb65370e58b4487b0e")]
    [PortOf("uc:SBase.SGadget:Destroyed", Spec = "spec/equipment/inventory-selection.md#rules")]
    protected override void Destroyed()
    {
        if (OwnerPawn is { } pawn)
        {
            PawnInventory.Unlink(pawn, this);
        }

        base.Destroyed();
    }

    /// <summary>Uses the gadget (empty for the base class).</summary>
    [Uc("SBase.SGadget.Use", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGadget:Use", Spec = "spec/equipment/inventory-selection.md#rules")]
    public virtual void Use(float Value)
    {
    }

    /// <summary>The quick use (the base class uses the gadget at full value).</summary>
    [Uc("SBase.SGadget.QuickUse", Sha1 = "b093d2e1219b750b8bc85f009aea655c852ac85b")]
    [PortOf("uc:SBase.SGadget:QuickUse", Spec = "spec/equipment/inventory-selection.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SGadget.uc QuickUse: Use(1.0)")]
    public virtual void QuickUse() => Use(1f);

    /// <summary>The gadget becomes the selection (empty for the base class).</summary>
    [Uc("SBase.SGadget.Load", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGadget:Load", Spec = "spec/equipment/inventory-selection.md#rules")]
    public virtual void Load()
    {
    }

    /// <summary>The gadget leaves the selection (empty for the base class).</summary>
    [Uc("SBase.SGadget.Unload", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SGadget:Unload", Spec = "spec/equipment/inventory-selection.md#rules")]
    public virtual void Unload()
    {
    }

    /// <summary>The charges back to the class default (server; a client sends it).</summary>
    [Uc("SBase.SGadget.FullReload", Sha1 = "a00bfcb130a80c0b65bf1f488ad97f9b73ff004a")]
    [PortOf("uc:SBase.SGadget:FullReload", Spec = "spec/equipment/inventory-selection.md#rules")]
    public virtual void FullReload()
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_FullReload) { Target = this });
            return;
        }

        Charge = ClassDefaults.Charge;
    }

    /// <summary>
    /// CanUse (native, decoded): false within RefireRate of LastUsed; then a charge left, or a tutorial map.
    /// </summary>
    [UcNative("ASGadget::execCanUse", Exe = 0x10a9f9c0)]
    [PortOf("nat:SBase.SGadget.CanUse", Spec = "spec/equipment/gadget-natives.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "gadget-natives.md rule 1: now - LastUsed < RefireRate refuses; Charge > 0 or CurLevelIsTutorial")]
    public virtual bool CanUseGadget() =>
        !(Level.TimeSeconds - LastUsed < RefireRate) && (Charge > 0 || GadgetWorld.CurLevelIsTutorial(this));

    /// <summary>
    /// DecCharge (native, decoded): one charge less on this machine; a machine without authority also asks the
    /// server (ServerDecCharge).
    /// </summary>
    [UcNative("ASGadget::execDecCharge", Exe = 0x10a9fa30)]
    [Uc("SBase.SGadget.DecCharge", Sha1 = "b7880daa2617deb98c573578e195329b33e66cc9")]
    [PortOf("nat:SBase.SGadget.DecCharge", Spec = "spec/equipment/gadget-natives.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "gadget-natives.md rule 2: Charge -= n here, ServerDecCharge(n) without authority, no floor, no LastUsed")]
    public void DecCharge(int NbCharge = 1)
    {
        // An OpenSCCT addition: a practice round's gadgets never run out (no charge used here or on the server).
        if (Modes.PracticeRules.IsOn(this))
        {
            return;
        }

        Charge -= NbCharge;
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ServerDecCharge) { Target = this, Int1 = NbCharge });
        }
    }

    /// <summary>The server side of DecCharge.</summary>
    [UcNative("ASGadget::execServerDecCharge", Exe = 0x10a9fab0)]
    [Uc("SBase.SGadget.ServerDecCharge", Sha1 = "524e4374619fcf931f609460ce00706ad787a3fd")]
    [PortOf("nat:SBase.SGadget.ServerDecCharge", Spec = "spec/equipment/gadget-natives.md#rules")]
    public void ServerDecCharge(int NbCharge = 1)
    {
        if (!Modes.PracticeRules.IsOn(this))
        {
            Charge -= NbCharge;
        }
    }

    /// <summary>AddCharge (native, decoded): more charges here; a machine without authority also asks the server.</summary>
    [UcNative("ASGadget::execAddCharge", Exe = 0x10a9faf0)]
    [Uc("SBase.SGadget.AddCharge", Sha1 = "53d992f174c134e04848926a8933edf74bacb4b7")]
    [PortOf("nat:SBase.SGadget.AddCharge", Spec = "spec/equipment/gadget-natives.md#rules")]
    public void AddCharge(int Amount = 1)
    {
        Charge += Amount;
        if (Role < ENetRole.ROLE_Authority && OwnerPawn?.Controller is PlayerController pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ServerAddCharge) { Target = this, Int1 = Amount });
        }
    }

    /// <summary>The server side of AddCharge.</summary>
    [UcNative("ASGadget::execServerAddCharge", Exe = 0x10a9fb70)]
    [Uc("SBase.SGadget.ServerAddCharge", Sha1 = "acb59280cd80c7b0557700395f79ddd276a69e2d")]
    [PortOf("nat:SBase.SGadget.ServerAddCharge", Spec = "spec/equipment/gadget-natives.md#rules")]
    public void ServerAddCharge(int Amount = 1) => Charge += Amount;

    /// <summary>The server mirrors a refill on the owning client.</summary>
    [Uc("SBase.SGadget.AddChargeOnClientFromServer", Sha1 = "4b7be7f297f16d99fc3552577d64bc8f62a246a8")]
    [PortOf("uc:SBase.SGadget:AddChargeOnClientFromServer", Spec = "spec/equipment/inventory-selection.md#rules")]
    public void AddChargeOnClientFromServer(int Amount) => Charge += Amount;

    /// <summary>SelectNext (native, decoded 0x10a9f970): this gadget or the next selectable one along the list.</summary>
    public SGadget? SelectNext() => PawnInventory.SelectNext(this);

    /// <summary>GetUniqueIDByClass (native): the gadget id of the class (the aSGad* constants), 0 for none.</summary>
    [UcNative("ASGadget::execGetUniqueIDByClass")]
    [PortOf("nat:SBase.SGadget.GetUniqueIDByClass", Spec = "spec/equipment/inventory-selection.md#data")]
    public static int GetUniqueIDByClass(ClassRef GadgetClass) => GadgetClass.Path switch
    {
        "SBase.SGadChaffeGrenade" => aSGadChaffeGrenade,
        "SBase.SGadFlareLauncher" => aSGadFlareLauncher,
        "SBase.SGadFlashBang" => aSGadFlashBang,
        "SBase.SGadFragGrenade" => aSGadFragGrenade,
        "SBase.SGadPhosphoGrenade" => aSGadPhosphoGrenade,
        "SBase.SGadPressureGrenade" => aSGadPressureGrenade,
        "SBase.SGadSmokeGrenade" => aSGadSmokeGrenade,
        "SBase.SGadJammer" => aSGadJammer,
        "SBase.SGadMinesLauncher" => aSGadMinesLauncher,
        "SBase.SGadMultiMinesLauncher" => aSGadMultiMinesLauncher,
        "SBase.SGadNoiseEmitter" => aSGadNoiseEmitter,
        "SBase.SGadPresenceDetector" => aSGadPresenceDetector,
        "SBase.SGadStickyCam" => aSGadStickyCam,
        "SBase.SSpyBulletLauncher" => aSGadSpyBulletLauncher,
        "SBase.SGadBackPack" => aSGadBackPack,
        "SBase.SGadGasMask" => aSGadGasMask,
        "SBase.SGadMultiPresDetectorLauncher" => aSGadMultiPresDetectorLauncher,
        "SBase.SGadCamNetwork" => aSGadCamNetwork,
        "SBase.SGadTazerLauncher" => aSGadTazerLauncher,
        _ => aNoGadget,
    };

    /// <summary>A HUD auxiliary message for the owner.</summary>
    [Uc("SBase.SGadget.LocalMessage", Sha1 = "d3a2c18198594d95e534aa24f32ae3611df19b6b")]
    [PortOf("uc:SBase.SGadget:LocalMessage", Spec = "spec/equipment/inventory-selection.md#rules")]
    public void LocalMessage(string Message, float Time) =>
        CombatFx.Notify(OwnerPawn?.Controller ?? (Actor)this, CombatFx.LocalMessage, Time);
}
