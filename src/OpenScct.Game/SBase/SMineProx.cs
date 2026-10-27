using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMineProx: the proximity mine (spec/equipment/mine-launcher.md rules 13, 14 and 21). Armed at once; every
/// 0.5 s a spy moving faster than 200 uu/s within 400 uu in sight triggers it, and it blows 0.5 s later.
/// </summary>
public partial class SMineProx
{
    public static new readonly StateTable ClassStates = new(SMines.ClassStates, s_Activated.Instance, s_LockedByChaff.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>A moving mover touching the mine blows it (server); any mover touching it is remembered as its support.</summary>
    [Uc("SBase.SMineProx.Touch", Sha1 = "0a21d25854f7e9710e235d6cdaf4e71895efe2de")]
    [PortOf("uc:SBase.SMineProx:Touch", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Touch(Actor Other)
    {
        if (Other is not Mover mover)
        {
            return;
        }

        MoverWhereIAm = mover;
        if (!mover.Velocity.IsZero() && Role == ENetRole.ROLE_Authority)
        {
            Explosion();
        }
    }

    /// <summary>The reference point 5 uu ahead and the light off; then the mine's start.</summary>
    [Uc("SBase.SMineProx.PostBeginPlay", Sha1 = "f3d5299a444d973d0b9c421bbc765b358154501b")]
    [PortOf("uc:SBase.SMineProx:PostBeginPlay", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void PostBeginPlay()
    {
        Rotation.GetAxes(out FVector x, out _, out _);
        refloc = Location + x * K.MineRefAhead;
        CurrentSkin = LightOff;
        base.PostBeginPlay();
    }

    /// <summary>Any damage blows the mine (once).</summary>
    [Uc("SBase.SMineProx.TakeDamage", Sha1 = "272559224230abe363f72647e6d7ae50ce62bf04")]
    [PortOf("uc:SBase.SMineProx:TakeDamage", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        if (!Exploded)
        {
            NoteDetonator(EventInstigator);
            Explosion();
        }
    }

    /// <summary>The interaction goes on the server, then the mine's own clean-up.</summary>
    [Uc("SBase.SMineProx.Destroyed", Sha1 = "1b0c45c9a9c7a4c64ed38833119f6096dcfb9325")]
    [PortOf("uc:SBase.SMineProx:Destroyed", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Destroyed()
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            InteractionObj?.Destroy();
        }

        base.Destroyed();
    }

    /// <summary>
    /// The mine's native tick (decoded 0x10ac1960): on the server, the area check on the tick its 0.5 s timer fires
    /// (while not triggered), or the blast once triggered for longer than TempsAvantExplosion; after the actor tick a
    /// moving support mover blows it. Clients show the replicated light; the bip runs while the light is on.
    /// </summary>
    [UcNative("ASMineProx::Tick", Exe = 0x10ac1960)]
    [PortOf("eng:ASMineProx::Tick", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void NativeTick(float DeltaSeconds)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            if (!Explode && !Exploded && TimerRate > 0f && !(DeltaSeconds + TimerCounter < TimerRate))
            {
                CheckArea();
            }
            else if (Explode && TempsAvantExplosion < Level.TimeSeconds - fTRef)
            {
                Explosion();
            }

            if (bDeleteMe)
            {
                return;
            }
        }

        base.NativeTick(DeltaSeconds);
        if (bDeleteMe)
        {
            return;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            if (MoverUnderMineMoves)
            {
                Explosion();
                return;
            }
        }
        else if (Skins.Count < 2 || Skins[1] != CurrentSkin)
        {
            SetLightSkin(CurrentSkin);
        }

        if (AmbientSound.IsNone && CurrentSkin == LightOn)
        {
            AmbientSound = BipSound;
        }
    }

    /// <summary>
    /// CheckArea (decoded 0x10ac0fa0): a spy within MyRange of refloc, faster than VitesseMin (3D when going up, else
    /// horizontal) and in sight triggers the mine: the trigger time, the warning bip for everyone, Explode.
    /// </summary>
    [UcNative("ASMineProx::CheckArea", Exe = 0x10ac0fa0)]
    [PortOf("eng:ASMineProx::Tick", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void CheckArea()
    {
        float range2 = (float)(MyRange * MyRange);
        float speed2 = (float)(VitesseMin * VitesseMin);
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (p is not SPawnAttaque || p.bDeleteMe || !(FVector.DistSquared(p.Location, refloc) < range2))
            {
                continue;
            }

            FVector v = p.Velocity;
            float s2 = v.Z > 0f ? v.SizeSquared() : v.SizeSquared2D();
            if (!(speed2 < s2) || !GadgetWorld.Sees(this, refloc, p))
            {
                continue;
            }

            fTRef = Level.TimeSeconds;
            PlaySound(BipBeforeExplode, ESoundSlot.SLOT_None, K.ProxBipVolume, false, K.ProxBipRadius);
            Explode = true;
        }
    }

    /// <summary>Explosion (native 1322, natives2-damage.md rule 5): marked exploded, then the shared mine blast.</summary>
    [UcNative("ASMineProx::execExplosion", Exe = 0x10ac1190)]
    [PortOf("nat:SBase.SMineProx.Explosion", Spec = "spec/native/natives2-damage.md#rules")]
    public void Explosion()
    {
        if (bDeleteMe || bExploding)
        {
            return;
        }

        Exploded = true;
        MineExplosion(ExplosionEffect, ExplodeSound, refloc, MyRange, true, MyDamage, fDistForBlast, PercentDamageRefillable, ShakeRange);
    }

    /// <summary>Armed: the light on and the 0.5 s scan timer.</summary>
    [Uc("SBase.SMineProx.s_Activated")]
    public new class s_Activated : SMines.s_Activated
    {
        public static new readonly s_Activated Instance = new(UcMeta.s_Activated, SMines.s_Activated.Instance);

        protected s_Activated(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMineProx.s_Activated.BeginState", Sha1 = "c44c34eb429d2b0e440662c5ec80ebd03c2db22a")]
        [PortOf("uc:SBase.SMineProx:s_Activated.BeginState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void BeginState(Actor self)
        {
            var mine = (SMineProx)self;
            mine.SetLightSkin(mine.LightOn);
            mine.CurrentSkin = mine.LightOn;
            mine.SetTimer(K.ProxScanPeriod, true);
        }
    }

    /// <summary>Chaffed: the light off while locked, on again after.</summary>
    [Uc("SBase.SMineProx.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, SBasicGamePlayObject.s_LockedByChaff.Instance);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMineProx.s_LockedByChaff.BeginState", Sha1 = "41a3cc522f7cd3979d80d5dccad218d13fe88050")]
        [PortOf("uc:SBase.SMineProx:s_LockedByChaff.BeginState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var mine = (SMineProx)self;
            mine.SetLightSkin(mine.LightOff);
            mine.CurrentSkin = mine.LightOff;
        }

        [Uc("SBase.SMineProx.s_LockedByChaff.EndState", Sha1 = "f0662021b12d65590970bce6dd30405746221413")]
        [PortOf("uc:SBase.SMineProx:s_LockedByChaff.EndState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void EndState(Actor self)
        {
            var mine = (SMineProx)self;
            mine.SetLightSkin(mine.LightOn);
            mine.CurrentSkin = mine.LightOn;
            base.EndState(self);
        }
    }
}
