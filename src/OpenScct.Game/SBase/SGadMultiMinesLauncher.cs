using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadMultiMinesLauncher: the merc's mine launcher (spec/equipment/mine-launcher.md rules 1, 2, 6 to 10). The
/// merc finds a wall within reach, the placement clip runs, and its interaction frame places the chosen mine.
/// </summary>
public partial class SGadMultiMinesLauncher
{
    private static readonly Name CNAME_AskServerToRemoveOldestMine = new("AskServerToRemoveOldestMine");
    private static readonly Name CNAME_UpdateRemoveMineUsabilityOnClient = new("UpdateRemoveMineUsabilityOnClient");
    private static readonly Name CNAME_SpawnMine = new("SpawnMine");
    private static readonly AssetRef MenuValide = new("Interface.Menu.Menu_valide_1");
    private static readonly ClassRef CREF_SComputerObjectiveTrigger = new("SBase.SComputerObjectiveTrigger");
    private const string PlainLauncherPath = "SBase.SGadMultiMinesLauncher";
    private const string PresDetectorPath = "SBase.SGadMultiPresDetectorLauncher";

    /// <summary>The placement line (rule 2): single result; level, movers, other actors, world geometry.</summary>
    [Provenance(ProvenanceKind.Decoded, "UseMine 0x10a9fd80: flags 0x496")]
    public const ETraceFlags PlaceTraceFlags = ETraceFlags.TRACE_SingleResult | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Others |
        ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers;

    /// <summary>The opposite wall probe (rule 2): level, movers, world geometry.</summary>
    [Provenance(ProvenanceKind.Decoded, "UseMine / DropMineOnWall: flags 0x86")]
    public const ETraceFlags WallTraceFlags = ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers;

    /// <summary>The plain launcher (not the presence detector launcher, which shares this class).</summary>
    private bool IsPlainLauncher => string.Equals(GetUcClass().Path, PlainLauncherPath, System.StringComparison.OrdinalIgnoreCase);

    private bool IsPresDetector => PawnInventory.IsA(this, new ClassRef(PresDetectorPath));

    /// <summary>Type 0 at start; tutorial maps make the charge unlimited and an offline game raises it to 999.</summary>
    [Uc("SBase.SGadMultiMinesLauncher.PostNetBeginPlay", Sha1 = "b898d2654b8a5d69a73c4e82fc81b6ab75612812")]
    [PortOf("uc:SBase.SGadMultiMinesLauncher:PostNetBeginPlay", Spec = "spec/equipment/mine-launcher.md#placing")]
    protected override void PostNetBeginPlay()
    {
        iCurrentMineType = 0;
        if (GadgetWorld.CurLevelIsTutorial(this))
        {
            Charge = K.UnlimitedCharge;
        }
        else if (Charge > 0 && Charge < K.OfflineCharge && Level.NetMode == ENetMode.NM_Standalone)
        {
            Charge = K.OfflineCharge;
        }
    }

    /// <summary>True when the owner already has as many basic mines on the map as allowed.</summary>
    [Uc("SBase.SGadMultiMinesLauncher.ifReachedMaxMineInMap", Sha1 = "0f6802f818326a5df82786ff01b692d23b4d077d")]
    [PortOf("uc:SBase.SGadMultiMinesLauncher:ifReachedMaxMineInMap", Spec = "spec/equipment/mine-launcher.md#placing")]
    public bool ifReachedMaxMineInMap() => (OwnerPawn?.Controller as SPlayerController)?.NbBasicMineInMapIOwn >= iNbMineMax;

    /// <summary>
    /// Alt-fire (rule 1): when walking, find a wall (UseMine); a stickable wall starts the placement clip, anything else
    /// shows "Can't place mine here".
    /// </summary>
    [Uc("SBase.SGadMultiMinesLauncher.Use", Sha1 = "38bfe6e74380983abe567e896dad03d8d49e297f")]
    [PortOf("uc:SBase.SGadMultiMinesLauncher:Use", Spec = "spec/equipment/mine-launcher.md#placing")]
    public override void Use(float Value)
    {
        if (OwnerPawn is not { Physics: EPhysics.PHYS_Walking } pawn)
        {
            return;
        }

        UseMine();
        if (iCurrentMineType == NbIT - 1 || iCurrentMineType == NbIT - 2)
        {
            iCurrentMineType = 0;
        }

        bool stickable = HitActor switch
        {
            null => false,
            SBasicGamePlayObject gpo => gpo.AcceptStickableGPO(),
            _ => HitActor.bWorldGeometry || HitActor is LevelInfo,
        };
        var pc = pawn.Controller as SPlayerController;
        if (stickable)
        {
            pc?.eventChangeState(GadgetStates.s_PutMineOnWall_Grab);
        }
        else
        {
            // Older content packs retain the package's untranslated developer wording. Use the shipped
            // English localization for that exact legacy value; leave other translations and tracker text alone.
            string placementText = TextCanSpawnMine;
            if (string.Equals(placementText, "Cant spawn mine here", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(placementText, "Can't spawn mine here", System.StringComparison.OrdinalIgnoreCase))
            {
                placementText = "Can't place mine here";
            }
            GadgetHud.AddMessageToAuxiliaryStack(pc, placementText, K.MineMessageTime);
            PlaySound(MenuValide, ESoundSlot.SLOT_Misc, 1f, false, K.EmptyChargeRadius);
        }

        base.Use(Value);
    }

    /// <summary>
    /// UseMine (native 1306, decoded 0x10a9fc70): from the eye along the view (not pitched up past 0.4), a wall within
    /// 55 uu that is not a mover, a floor or a ceiling; a mine within 12.5 uu or a terminal within 45 uu replaces the
    /// hit actor; the normal faces back; each type needing an opposite wall checks it.
    /// </summary>
    [UcNative("ASGadMultiMinesLauncher::execUseMine", Exe = 0x10a9fc70)]
    [PortOf("nat:SBase.SGadMultiMinesLauncher.UseMine", Spec = "spec/equipment/mine-launcher.md#placing")]
    public void UseMine()
    {
        HitActor = null;
        if (OwnerPawn is not Pawn pawn)
        {
            return;
        }

        FRotator view = pawn.Controller?.Rotation ?? pawn.Rotation;
        view.GetAxes(out FVector x, out _, out _);
        if (x.Z > K.MineMaxViewUp)
        {
            return;
        }

        FVector eye = pawn.Location + pawn.WalkBob + new FVector(0f, 0f, pawn.EyeHeight);
        FVector end = eye + x * K.MineReach;
        bool hit = GadgetWorld.Line(pawn, end, eye, PlaceTraceFlags, out FCheckResult h);
        if ((hit && h.Actor is Mover) || SvmMath.Abs(h.Normal.Z) > K.MineMaxNormalZ)
        {
            HitLocation = FVector.Zero;
            HitNormal = FVector.Zero;
            HitActor = null;
            return;
        }

        Actor? found = hit ? GadgetWorld.HitActorOf(pawn, h) : null;
        if (hit)
        {
            foreach (Actor a in XLevel.AllActors<Actor>())
            {
                float d2 = FVector.DistSquared(a.Location, h.Location);
                if ((a is SMines && d2 < K.MineSpacingSquared) ||
                    (PawnInventory.IsA(a, CREF_SComputerObjectiveTrigger) && d2 < K.MineTerminalSpacingSquared))
                {
                    found = a;
                    break;
                }
            }
        }

        HitLocation = h.Location;
        HitNormal = SGadStickyCam.FacingBack(h.Normal, x);
        HitActor = found;
        for (int i = 0; i < NbIT - 2 && i < fMaxOppositeWallDistance.Length; i++)
        {
            CanUse[i] = 1;
            if (fMaxOppositeWallDistance[i] > 0f && !HasOppositeWall(pawn, fMaxOppositeWallDistance[i]))
            {
                CanUse[i] = 0;
            }
        }
    }

    /// <summary>A wall in front of the placement within <paramref name="Distance"/> (from 2 uu off it).</summary>
    private bool HasOppositeWall(Pawn pawn, float Distance) =>
        GadgetWorld.Line(pawn, HitLocation + HitNormal * Distance, HitLocation + HitNormal * K.MineWallProbeStart, WallTraceFlags, out _);

    /// <summary>
    /// DropMineOnWall (native 1307, decoded 0x10aa00c0): the mine limit, the opposite wall again for the types that
    /// need one, then the refire time, one charge, the mine on the server (5 uu off the wall) and its message.
    /// </summary>
    [UcNative("ASGadMultiMinesLauncher::execDropMineOnWall", Exe = 0x10aa00c0)]
    [PortOf("nat:SBase.SGadMultiMinesLauncher.DropMineOnWall", Spec = "spec/equipment/mine-launcher.md#placing")]
    public void DropMineOnWall()
    {
        if (OwnerPawn is not Pawn pawn)
        {
            return;
        }

        var pc = pawn.Controller as SPlayerController;
        if ((IsPlainLauncher && pc is not null && iNbMineMax <= pc.NbBasicMineInMapIOwn) ||
            (IsPresDetector && pc is not null && iNbMineMax <= pc.NbTrapMineInMapIOwn))
        {
            GadgetHud.AddMessageToAuxiliaryStack(pc, TextTooManyMineInMap, K.MineMessageTime);
            return;
        }

        int type = iCurrentMineType;
        if (type >= 0 && type < fMaxOppositeWallDistance.Length && fMaxOppositeWallDistance[type] > 0f &&
            !HasOppositeWall(pawn, fMaxOppositeWallDistance[type]))
        {
            if ((!IsPresDetector && type != 0) || type == 0)
            {
                GadgetHud.AddMessageToStack(pc, (pc?.myHUD as SHud)?.MineOppositeWall, (int)K.MineMessageTime + 1);
            }

            return;
        }

        if (type < 0 || type >= MineClass.Length)
        {
            return;
        }

        LastUsed = Level.TimeSeconds;
        DecCharge(1);
        SpawnMine(MineClass[type], pawn, HitLocation + HitNormal * K.MineWallOffset, HitNormal.Rotation());
        if (type < SpawnText.Length)
        {
            GadgetHud.AddMessageToAuxiliaryStack(pc, SpawnText[type], K.MineMessageTime);
        }
    }

    /// <summary>SelectNextMineType (native 1308): the next menu item, wrapping.</summary>
    [UcNative("ASGadMultiMinesLauncher::execSelectNextMineType", Exe = 0x10aa0390)]
    [PortOf("nat:SBase.SGadMultiMinesLauncher.SelectNextMineType", Spec = "spec/equipment/mine-launcher.md#the-menu")]
    public void SelectNextMineType()
    {
        iCurrentMineType++;
        if (iCurrentMineType >= NbIT)
        {
            iCurrentMineType = 0;
        }
    }

    /// <summary>SelectPreviousMineType (native 1309): the previous menu item, wrapping.</summary>
    [UcNative("ASGadMultiMinesLauncher::execSelectPreviousMineType", Exe = 0x10aa03c0)]
    [PortOf("nat:SBase.SGadMultiMinesLauncher.SelectPreviousMineType", Spec = "spec/equipment/mine-launcher.md#the-menu")]
    public void SelectPreviousMineType()
    {
        iCurrentMineType--;
        if (iCurrentMineType < 0)
        {
            iCurrentMineType = NbIT - 1;
        }
    }

    /// <summary>
    /// AskServerToRemoveOldestMine (native 1831, client to server, decoded 0x10aa2a60): the player's oldest mine of the
    /// type is destroyed unless testing; the client learns whether there was one.
    /// </summary>
    [UcNative("ASGadMultiMinesLauncher::execAskServerToRemoveOldestMine", Exe = 0x10aa2a60)]
    [PortOf("nat:SBase.SGadMultiMinesLauncher.AskServerToRemoveOldestMine", Spec = "spec/equipment/mine-launcher.md#the-menu")]
    public void AskServerToRemoveOldestMine(Actor? PC, byte MineType, bool bTest = false)
    {
        if (GadgetNet.ToServer(this, OwnerPawn?.Controller as PlayerController,
            new CombatCall(CNAME_AskServerToRemoveOldestMine) { Target = this, Actor1 = PC, Int1 = MineType, Bool1 = bTest }))
        {
            return;
        }

        if (PC is null)
        {
            return;
        }

        SMines? oldest = null;
        foreach (SMines mine in XLevel.AllActors<SMines>())
        {
            if (mine.bDeleteMe || !ReferenceEquals(mine.PcOwner, PC) || mine.iMineType != MineType)
            {
                continue;
            }

            oldest ??= mine;
            if (mine.fSpawnedTime < oldest.fSpawnedTime)
            {
                oldest = mine;
            }
        }

        if (!bTest)
        {
            oldest?.Destroy();
        }

        UpdateRemoveMineUsabilityOnClient(oldest is not null);
    }

    /// <summary>Whether "Delete oldest" can be used (server to owner).</summary>
    [Uc("SBase.SGadMultiMinesLauncher.UpdateRemoveMineUsabilityOnClient", Sha1 = "6df54d49aa16e9cc4dbe76be74fc4e90cae94a76")]
    [PortOf("uc:SBase.SGadMultiMinesLauncher:UpdateRemoveMineUsabilityOnClient", Spec = "spec/equipment/mine-launcher.md#the-menu")]
    public void UpdateRemoveMineUsabilityOnClient(bool val)
    {
        if (GadgetNet.ToOwner(OwnerPawn?.Controller as PlayerController,
            new CombatCall(CNAME_UpdateRemoveMineUsabilityOnClient) { Target = this, Bool1 = val }))
        {
            return;
        }

        CanUse[NbIT - 2] = val ? 1 : 0;
    }

    /// <summary>
    /// SpawnMine (client to server, rule 7): turned square to the wall when it almost faces away from it, spawned and
    /// counted for its owner, riding a gameplay object it was put on.
    /// </summary>
    [Uc("SBase.SGadMultiMinesLauncher.SpawnMine", Sha1 = "f86b1bd6f9a705d1d06a84bfe7baba08737ea7f1")]
    [PortOf("uc:SBase.SGadMultiMinesLauncher:SpawnMine", Spec = "spec/equipment/mine-launcher.md#placing")]
    public void SpawnMine(ClassRef MineClass, Pawn P, FVector Loc, FRotator Rot)
    {
        if (GadgetNet.ToServer(this, P.Controller as PlayerController, new CombatCall(CNAME_SpawnMine)
        {
            Target = this,
            Name1 = new Name(MineClass.Path ?? ""),
            Actor1 = P,
            Vector1 = Loc,
            Rotation = Rot,
        }))
        {
            return;
        }

        Rot.GetAxes(out FVector x, out _, out _);
        bool wall = GadgetWorld.Line(this, Loc - x * K.MineSnapTrace, Loc, ETraceFlags.TRACE_World, out FCheckResult h);
        HitActor = wall ? GadgetWorld.HitActorOf(this, h) : null;
        HitLocation = h.Location;
        HitNormal = h.Normal;
        if (wall)
        {
            FRotator n = h.Normal.Rotation();
            if (((n.Yaw - Rot.Yaw) & 65535) < K.MineSnapStep && ((n.Pitch - Rot.Pitch) & 65535) < K.MineSnapStep)
            {
                Rot = new FRotator((n.Pitch + K.MineSnapStep / 2) >> 8 << 8, (n.Yaw + K.MineSnapStep / 2) >> 8 << 8, Rot.Roll);
            }
        }

        if (DeviceClasses.Spawn(this, MineClass, P, Name.None, Loc, Rot) is not SMines mine)
        {
            return;
        }

        mine.iMineType = (byte)(IsPresDetector ? 1 : 0);
        mine.PcOwner = P.Controller as SPlayerController;
        mine.AddMineCounter(1, mine.PcOwner);
        GadgetFx.Notify(mine, GadgetFx.MinePlaced, default, mine.iMineType);
        if (mine.PcOwner?.PlayerReplicationInfo?.Stats is { } stats)
        {
            if (mine.iMineType == 1)
            {
                stats.Merc_SpyTrackersPlaced++;
            }
            else
            {
                stats.Merc_MinesPlaced++;
            }
        }

        if (HitActor is SBasicGamePlayObject gpo)
        {
            mine.SetBase(gpo);
            if (gpo.bWorldGeometry)
            {
                gpo.Attached.Insert(0, mine);
            }
        }
    }
}
