using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadBackPack: the merc's backpack (spec/equipment/backpack.md). It carries a share of its owner's gadget
/// charges (Use refills them) and of two team mates' (they take theirs with the "Refill" entry on him); the server
/// picks the two mates and fills their slots on the owner's pawn, which every machine sees. A backpack mesh rides the
/// owner's spine.
/// </summary>
public partial class SGadBackPack
{
    /// <summary>
    /// The charges the backpack holds for a gadget: per gadget id for the known ones (less in a team of 3 or more),
    /// else two thirds of the class's charges (truncated).
    /// </summary>
    [Uc("SBase.SGadBackPack.GetBackPackCharge", Sha1 = "b0c0c4ad8559b6f9a2e8813d3c036d8c4e3ad284")]
    [PortOf("uc:SBase.SGadBackPack:GetBackPackCharge", Spec = "spec/equipment/backpack.md#rules")]
    public int GetBackPackCharge(SGadget gad)
    {
        SBasePawn? owner = Owner as SBasePawn;
        bool bBigTeam = (owner?.Controller?.PlayerReplicationInfo?.Team?.Size ?? 0) >= K.BackPackBigTeam;
        int id = GetUniqueIDByClass(new ClassRef(gad.GetUcClass().Path));
        if (owner?.SPawnType == PawnType.SPAWNDEFENSE)
        {
            switch (id)
            {
                case aSGadFragGrenade:
                case aSGadMultiMinesLauncher:
                case aSGadMultiPresDetectorLauncher:
                    return bBigTeam ? 1 : 2;
                case aSGadFlareLauncher:
                    return bBigTeam ? 3 : 6;
                case aSGadPhosphoGrenade:
                    return bBigTeam ? 2 : 3;
            }
        }
        else
        {
            switch (id)
            {
                case aSGadSmokeGrenade:
                case aSGadChaffeGrenade:
                case aSGadFlashBang:
                case aSGadNoiseEmitter:
                case aSGadStickyCam:
                case aSGadSpyBulletLauncher:
                    return 3;
            }
        }

        return SvmMath.Trunc(SBackPackInteraction.DefaultCharge(gad) * K.BackPackDefaultShare);
    }

    /// <summary>
    /// On the server: the first two other players of the owner's side become mates A and B (kept for the match, their
    /// current pawns followed), and each mate's slots on the owner's pawn are filled once from his gadgets. On every
    /// machine with the gadget: the owner's own share is filled once his whole gadget list has arrived.
    /// </summary>
    [Uc("SBase.SGadBackPack.Tick", Sha1 = "48bc4d8a782fafc3fb45963dfe935e3725c4cd0f")]
    [PortOf("uc:SBase.SGadBackPack:Tick", Spec = "spec/equipment/backpack.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SGadBackPack.Tick: mates from AllPlayerControllers (same SPawnType, not the owner), slots A0..3 / B0..3 while A0 / B0 is -1, own share while aNbChargesGad_Self[0] is -1, reset when fewer than iNBGadgets were counted")]
    [Provenance(ProvenanceKind.Estimated, "The slot count reads PawnInventory.TeamSlots (4 without a team) where a script read of None gives 0")]
    protected override void Tick(float DeltaTime)
    {
        if (Owner is SBasePawn owner)
        {
            int nbGad = PawnInventory.TeamSlots(owner);
            bool authority = Role == ENetRole.ROLE_Authority;
            if (authority)
            {
                PickMates(owner);
            }

            if (authority && owner.aBackPackOwner_A is { MyGadget: not null } mateA && owner.aNbChargesGad_Mate_A0 == -1)
            {
                FillMate(owner, mateA, true, nbGad);
            }

            int i = 0;
            if (authority && owner.aNbChargesGad_Mate_B0 == -1 && owner.aBackPackOwner_B is { MyGadget: not null } mateB)
            {
                i = FillMate(owner, mateB, false, nbGad);
            }

            // As in the script, the count is not reset after mate B: a first fill of B on the server leaves the own
            // share short, and it is filled again next tick.
            if (aNbChargesGad_Self[0] == -1 && owner.MyGadget is not null)
            {
                int initialized = 0;
                for (SGadget? gad = owner.MyGadget; gad is not null; gad = gad.MyGadget)
                {
                    if (!SBackPackInteraction.Refillable(gad))
                    {
                        continue;
                    }

                    if (i < nbGad && i < aNbChargesGad_Self.Length)
                    {
                        initialized++;
                        aNbChargesGad_Self[i] = GetBackPackCharge(gad);
                    }

                    i++;
                }

                if (initialized != nbGad)
                {
                    aNbChargesGad_Self[0] = -1;
                }
            }
        }

        base.Tick(DeltaTime);
    }

    /// <summary>The two mates (server): the first other players of the owner's side, then their current pawns.</summary>
    private void PickMates(SBasePawn owner)
    {
        for (Controller? c = Level.ControllerList; c is not null; c = c.nextController)
        {
            if (c is not SPlayerController { Pawn: SBasePawn pawn } pc || pawn.SPawnType != owner.SPawnType || ReferenceEquals(pawn, owner))
            {
                continue;
            }

            if (Controller_A is null)
            {
                owner.aBackPackOwner_A = pawn;
                Controller_A = pc;
                continue;
            }

            if (Controller_B is null && !ReferenceEquals(pc, Controller_A))
            {
                owner.aBackPackOwner_B = pawn;
                Controller_B = pc;
            }
        }

        if (Controller_A?.Pawn is SBasePawn a)
        {
            owner.aBackPackOwner_A = a;
        }

        if (Controller_B?.Pawn is SBasePawn b)
        {
            owner.aBackPackOwner_B = b;
        }
    }

    /// <summary>Fills a mate's four slots on the owner's pawn from the mate's gadgets (torches left out); the count.</summary>
    private int FillMate(SBasePawn owner, SBasePawn mate, bool a, int nbGad)
    {
        int i = 0;
        for (SGadget? gad = mate.MyGadget; gad is not null; gad = gad.MyGadget)
        {
            if (!SBackPackInteraction.Refillable(gad))
            {
                continue;
            }

            if (i < nbGad)
            {
                int charge = GetBackPackCharge(gad);
                int id = GetUniqueIDByClass(new ClassRef(gad.GetUcClass().Path));
                switch ((a, i))
                {
                    case (true, 0): owner.aNbChargesGad_Mate_A0 = charge; owner.aIDGad_Mate_A0 = id; break;
                    case (true, 1): owner.aNbChargesGad_Mate_A1 = charge; owner.aIDGad_Mate_A1 = id; break;
                    case (true, 2): owner.aNbChargesGad_Mate_A2 = charge; owner.aIDGad_Mate_A2 = id; break;
                    case (true, 3): owner.aNbChargesGad_Mate_A3 = charge; owner.aIDGad_Mate_A3 = id; break;
                    case (false, 0): owner.aNbChargesGad_Mate_B0 = charge; owner.aIDGad_Mate_B0 = id; break;
                    case (false, 1): owner.aNbChargesGad_Mate_B1 = charge; owner.aIDGad_Mate_B1 = id; break;
                    case (false, 2): owner.aNbChargesGad_Mate_B2 = charge; owner.aIDGad_Mate_B2 = id; break;
                    case (false, 3): owner.aNbChargesGad_Mate_B3 = charge; owner.aIDGad_Mate_B3 = id; break;
                }
            }

            i++;
        }

        return i;
    }

    /// <summary>
    /// Use (on the owner's machine): each gadget below its class's charges takes what the own share holds for it, up
    /// to the missing charges ("Gadget refill done" for each); AddCharge also tells the server.
    /// </summary>
    [Uc("SBase.SGadBackPack.Use", Sha1 = "d422dd53f4d3943951b73ac19e4033459fd5d7b6")]
    [PortOf("uc:SBase.SGadBackPack:Use", Spec = "spec/equipment/backpack.md#rules")]
    public override void Use(float Value)
    {
        if (Owner is SBasePawn owner)
        {
            int i = 0;
            for (SGadget? gad = owner.MyGadget; gad is not null; gad = gad.MyGadget)
            {
                if (!SBackPackInteraction.Refillable(gad))
                {
                    continue;
                }

                int full = SBackPackInteraction.DefaultCharge(gad);
                if (gad.Charge < full && i < aNbChargesGad_Self.Length && aNbChargesGad_Self[i] > 0)
                {
                    int add = (byte)System.Math.Clamp(full - gad.Charge, 0, aNbChargesGad_Self[i]);
                    gad.AddCharge(add);
                    aNbChargesGad_Self[i] -= add;
                    GadgetHud.AddMessageToAuxiliaryStack(owner.Controller as SPlayerController, TextGadgetRefillDone, K.BackPackRefillMessageTime);
                }

                i++;
            }
        }

        base.Use(Value);
    }

    /// <summary>The backpack mesh goes with the gadget (the original does not run the gadget's own Destroyed).</summary>
    [Uc("SBase.SGadBackPack.Destroyed", Sha1 = "8b49531d18b7585a84658b53851da7efd6d5f165")]
    [PortOf("uc:SBase.SGadBackPack:Destroyed", Spec = "spec/equipment/backpack.md#rules")]
    protected override void Destroyed()
    {
        DummyBackPack?.Destroy();
    }

    /// <summary>Based on the owner; the server hangs the side's backpack mesh on his spine.</summary>
    [Uc("SBase.SGadBackPack.PostBeginPlay", Sha1 = "2b3afc4d1ad0d9f2b94acaa34ff0891bf17ccbf1")]
    [PortOf("uc:SBase.SGadBackPack:PostBeginPlay", Spec = "spec/equipment/backpack.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The mesh's Instigator is set to the owner: a UE2 pawn is its own Instigator, so the script's Spawn passes it on")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        SetBase(Owner);
        if (Role != ENetRole.ROLE_Authority || Owner is not SBasePawn owner)
        {
            return;
        }

        SBackPack? bag = owner.SPawnType == PawnType.SPAWNDEFENSE
            ? XLevel.SpawnActor(new SBackPack(), Location, Rotation, owner, owner)
            : XLevel.SpawnActor<SBackPack>(new SBackPack_Att(), Location, Rotation, owner, owner);
        DummyBackPack = bag;
        if (bag is not null)
        {
            bag.Instigator = owner;
            bag.SetOwner(owner);
            ObjectiveNatives.AttachToBone(owner, bag, K.BackPackBone);
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        DummyBackPack = Live(DummyBackPack);
        Controller_A = Live(Controller_A);
        Controller_B = Live(Controller_B);
    }
}
