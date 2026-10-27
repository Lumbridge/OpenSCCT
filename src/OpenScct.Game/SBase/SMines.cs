using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMines: a mine placed by a merc (spec/equipment/mine-launcher.md rules 12 and 20). It arms after
/// TempsActivation (s_Activated), counts towards its owner's limit, and blasts with the shared mine explosion.
/// </summary>
public partial class SMines
{
    public static new readonly StateTable ClassStates = new(SBasicGamePlayObject.ClassStates, s_Activated.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>No auto state: the script is only marked initialised (the mine arms by its timer).</summary>
    [Uc("SBase.SMines.SetInitialState", Sha1 = "d418c51545c3def908ca506e80d1caa470c69164")]
    [PortOf("uc:SBase.SMines:SetInitialState", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void SetInitialState() => bScriptInitialized = true;

    /// <summary>Nothing sticks to a mine.</summary>
    [Uc("SBase.SMines.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SMines:AcceptStickableGPO", Spec = "spec/equipment/mine-launcher.md#mines")]
    public override bool AcceptStickableGPO() => false;

    /// <summary>In rain the mine's heat is halved (the gameplay object's description copy does not run).</summary>
    [Uc("SBase.SMines.PreBeginPlay", Sha1 = "7cf035cdf3cf022e65ff4fbb199f16863f9d4da9")]
    [PortOf("uc:SBase.SMines:PreBeginPlay", Spec = "spec/equipment/mine-launcher.md#mines")]
    [Provenance(ProvenanceKind.Estimated, "GetRainVolume is not ported: rain volumes are a map feature (no rain in SteeE)")]
    protected override void PreBeginPlay()
    {
    }

    /// <summary>On the server the interaction goes and the owner's mine count drops.</summary>
    [Uc("SBase.SMines.Destroyed", Sha1 = "7ef90b6527e433344065016eb6e5a6375ff7fda8")]
    [PortOf("uc:SBase.SMines:Destroyed", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Destroyed()
    {
        if (Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        InteractionObj?.Destroy();
        AddMineCounter(-1, PcOwner);
    }

    /// <summary>The server counts the mine for its owner (basic mines and trap mines apart).</summary>
    [Uc("SBase.SMines.AddMineCounter", Sha1 = "f6332cdd5f71db339d75916f16f953d988a74c46")]
    [PortOf("uc:SBase.SMines:AddMineCounter", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void AddMineCounter(int val, SPlayerController? PcThatSpawnedMine)
    {
        if (Role != ENetRole.ROLE_Authority || PcThatSpawnedMine is null)
        {
            return;
        }

        foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
        {
            if (!ReferenceEquals(pc, PcThatSpawnedMine))
            {
                continue;
            }

            if (iMineType == 0)
            {
                pc.NbBasicMineInMapIOwn += val;
            }
            else
            {
                pc.NbTrapMineInMapIOwn += val;
            }

            break;
        }
    }

    /// <summary>The placement sound; on the server the arming timer and the gameplay object's start; the placement time.</summary>
    [Uc("SBase.SMines.PostBeginPlay", Sha1 = "3de15c4c02e2817df65f9c8bfe5633e57f7cfff0")]
    [PortOf("uc:SBase.SMines:PostBeginPlay", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void PostBeginPlay()
    {
        PlaySound(SND_PoseMine, ESoundSlot.SLOT_None, 1f, false, K.MinePoseSoundRadius);
        if (Role == ENetRole.ROLE_Authority)
        {
            if (TempsActivation > 0)
            {
                SetTimer(TempsActivation, false);
            }
            else
            {
                Timer();
            }

            base.PostBeginPlay();
        }

        fSpawnedTime = Level.TimeSeconds;
    }

    /// <summary>The arming timer: the mine is active.</summary>
    [Uc("SBase.SMines.Timer", Sha1 = "a6e40b2ca727d929aef6c9651ae749d62f0fe8e8")]
    [PortOf("uc:SBase.SMines:Timer", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Timer() => GotoState(GadgetStates.s_Activated);

    /// <summary>Skin slot 1 (the mine's light), growing the list when needed.</summary>
    protected void SetLightSkin(AssetRef Skin)
    {
        while (Skins.Count < 2)
        {
            Skins.Add(AssetRef.None);
        }

        Skins[1] = Skin;
    }

    /// <summary>
    /// The mine explosion (natives2-damage.md rule 5): the explosion visual, the noise, the sound for everyone
    /// (radius 1000), the blast from <paramref name="RefLoc"/> with the falloff from the mine, and the mine is gone.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "natives2-damage.md rule 5: shared shape of SMineProx.Explosion and SMineLaser.Explosion")]
    protected void MineExplosion(ClassRef Effect, AssetRef Sound, FVector RefLoc, float Range, bool bIntRange, int Damage,
        float DistForBlast, int PercentRefill, int Shake)
    {
        if (!Effect.IsNone)
        {
            DeviceClasses.Spawn(this, Effect, null, Name.None, Location, Rotation);
        }

        NotifyNoise(K.ChaffNoise);
        PlaySound(Sound, ESoundSlot.SLOT_None, 1f, false, K.MineExplosionSoundRadius);
        SPlayerController? owner = PcOwner ?? (Owner as Pawn)?.Controller as SPlayerController;
        bExploding = true;
        GadgetBlast.Run(new GadgetBlast.Blast(this, RefLoc, Location, Range, bIntRange, Damage, MyDamageRange, DistForBlast,
            MyDamageType, SDamageType.SDTBase, MyDamageType, PercentRefill, Shake, owner?.Pawn, true));
        Destroy();
    }

    /// <summary>A moving mover under the mine (MoverWhereIAm).</summary>
    protected bool MoverUnderMineMoves => MoverWhereIAm is { } mover && !mover.Velocity.IsZero();

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        PcOwner = Live(PcOwner);
        MoverWhereIAm = Live(MoverWhereIAm);
    }

    /// <summary>s_Activated: the armed mine (the subclasses add the light and the checks).</summary>
    [Uc("SBase.SMines.s_Activated")]
    public class s_Activated : UcActorState
    {
        public static readonly s_Activated Instance = new(UcMeta.s_Activated, null);

        protected s_Activated(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }
    }
}
