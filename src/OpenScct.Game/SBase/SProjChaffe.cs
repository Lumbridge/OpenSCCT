using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjChaffe: the spy's chaff grenade (spec/equipment/chaff-grenade.md). Three seconds after the throw it
/// bursts: torches malfunction, gameplay objects lock, and for 10 s mercs in range and in sight get their HUD
/// perturbed and their goggles reset every tick.
/// </summary>
public partial class SProjChaffe
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_Brouillage.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly ClassRef CREF_SGadTorche = new("SBase.SGadTorche");

    /// <summary>The launch sound unless quick-launched; the launch trail is a view effect.</summary>
    [Uc("SBase.SProjChaffe.PostBeginPlay", Sha1 = "f5c65f9245b4d358dc29495c2fcd194b9cb8e87d")]
    [PortOf("uc:SBase.SProjChaffe:PostBeginPlay", Spec = "spec/equipment/chaff-grenade.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (QuickLaunch)
        {
            return;
        }

        PlaySound(SoundLaunch, ESoundSlot.SLOT_None, 1f, false, K.GrenadeSoundRadius);
    }

    /// <summary>The fuse: a client copy goes; the server stops, hides and bursts the grenade.</summary>
    [Uc("SBase.SProjChaffe.Timer", Sha1 = "a2f39a1a8853c707696e16b292853994ec2a4dfd")]
    [PortOf("uc:SBase.SProjChaffe:Timer", Spec = "spec/equipment/chaff-grenade.md#rules")]
    protected override void Timer()
    {
        if (Role != ENetRole.ROLE_Authority)
        {
            Destroy();
            return;
        }

        bBounce = false;
        SetPhysics(EPhysics.PHYS_None);
        bHidden = true;
        Explode(Location, K.ChaffExplodeNormal);
    }

    /// <summary>
    /// HitWall (native override shared with the flash-bang, decoded game 0x10ab8760): an aimed chaff grenade bounces;
    /// a quick-launched one bursts at its first hit, floors included.
    /// </summary>
    [UcNative("ASProjChaffe::HitWall", Exe = 0x10ab8760)]
    [PortOf("nat:SBase.SProjGrenade.HitWall", Spec = "spec/equipment/grenade-throw.md#rules")]
    protected override void HitWall(FVector HitNormal, Actor? Wall)
    {
        if (!QuickLaunchPop())
        {
            base.HitWall(HitNormal, Wall);
        }
    }

    /// <summary>The native burst's last step: the jamming field.</summary>
    [Uc("SBase.SProjChaffe.GotoBrouille", Sha1 = "cc01d6713d68a739da947475bb3589463b02ee0a")]
    [PortOf("uc:SBase.SProjChaffe:GotoBrouille", Spec = "spec/equipment/chaff-grenade.md#rules")]
    public void GotoBrouille() => GotoState(GadgetStates.s_Brouillage);

    /// <summary>
    /// Explode (native override, decoded 0x10abe640), once: hidden, the burst effects, a one-shot timer of the field
    /// length and the field.
    /// </summary>
    [UcNative("ASProjChaffe::Explode", Exe = 0x10abe640)]
    [PortOf("nat:Engine.Projectile.Explode", Spec = "spec/equipment/chaff-grenade.md#rules")]
    public override void Explode(FVector HitLocation, FVector HitNormal)
    {
        if (Exploded)
        {
            return;
        }

        Exploded = true;
        bHidden = true;
        ProjectileSpeed = 0f;
        ChaffExplodeEffect(HitNormal);
        SetTimer(TempsBrouillage, false);
        GotoBrouille();
    }

    /// <summary>
    /// ChaffExplodeEffect (decoded 0x10abe6d0): the noise, the burst sound for every player, the burst visual turned
    /// to the hit normal, and every torch within Range plus its radius malfunctions.
    /// </summary>
    [UcNative("ASProjChaffe::ChaffExplodeEffect", Exe = 0x10abe6d0)]
    [PortOf("nat:Engine.Projectile.Explode", Spec = "spec/equipment/chaff-grenade.md#rules")]
    public void ChaffExplodeEffect(FVector HitNormal)
    {
        SmokeLaunchEmitter = null;
        NotifyNoise(K.ChaffNoise);
        PlaySound(SoundExplode, ESoundSlot.SLOT_Misc, 1f, false, K.ChaffSoundRadius);
        XLevel.SpawnActor(new SChaffeEmitter(), Location, HitNormal.Rotation(), null, Instigator);
        GadgetFx.Notify(this, GadgetFx.ChaffBurst);
        foreach (SGadTorche torch in XLevel.AllActors<SGadTorche>())
        {
            if (torch.bHidden || torch.bDeleteMe)
            {
                continue;
            }

            float reach = Range + torch.CollisionRadius;
            if (FVector.DistSquared(torch.Location, Location) < reach * reach)
            {
                torch.Malfunction(GadTorcheDisableTime);
            }
        }
    }

    /// <summary>
    /// NativeStateBrouillageTick (decoded 0x10abe980): each merc in range, in sight and with a controller gets the
    /// HUD perturbation and the goggle reset.
    /// </summary>
    [UcNative("ASProjChaffe::execNativeStateBrouillageTick", Exe = 0x10abe980)]
    [PortOf("nat:SBase.SProjChaffe.NativeStateBrouillageTick", Spec = "spec/equipment/chaff-grenade.md#rules")]
    public void NativeStateBrouillageTick()
    {
        float range2 = (float)(Range * Range);
        foreach (SPAWNDEFENSE merc in XLevel.AllActors<SPAWNDEFENSE>())
        {
            if (merc.bHidden || merc.bDeleteMe || merc.Controller is not { } controller)
            {
                continue;
            }

            if (Range != 0 && !(FVector.DistSquared(merc.Location, Location) < range2))
            {
                continue;
            }

            if (!GadgetWorld.Sees(this, Location, merc))
            {
                continue;
            }

            if (controller is SPlayerController pc)
            {
                pc.SetHudPerturbation(TimeToDisipation);
                pc.eventClientChaffed();
            }

            CountJam(merc, controller);
        }
    }

    // OpenSCCT's own, for achievements: the mercs this grenade has jammed so far.
    private readonly HashSet<SPAWNDEFENSE> _jammed = [];

    /// <summary>Counts a chaff jam for the throwing spy, once per enemy merc per grenade (server only).</summary>
    private void CountJam(SPAWNDEFENSE merc, Controller victim)
    {
        if (Role != ENetRole.ROLE_Authority || Instigator is not SPawnAttaque { Controller: { } thrower } ||
            SGameInfo.TeamIndexOf(thrower) == SGameInfo.TeamIndexOf(victim) || !_jammed.Add(merc))
        {
            return;
        }

        thrower.PlayerReplicationInfo?.Stats?.AddFeat(FeatKeys.SpyChaffJams);
    }

    /// <summary>The jamming field (10 s): objects lock on entry, mercs are jammed every tick, then the grenade goes.</summary>
    [Uc("SBase.SProjChaffe.s_Brouillage")]
    public class s_Brouillage : UcActorState
    {
        public static readonly s_Brouillage Instance = new(UcMeta.s_Brouillage, null);

        protected s_Brouillage(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Every gameplay object colliding within Range locks for the field length (its class decides).</summary>
        [Uc("SBase.SProjChaffe.s_Brouillage.BeginState", Sha1 = "3447b5ea98774edfdc4b450a8613aa1cd3d7b2d5")]
        [PortOf("uc:SBase.SProjChaffe:s_Brouillage.BeginState", Spec = "spec/equipment/chaff-grenade.md#rules")]
        [Provenance(ProvenanceKind.Estimated, "CollidingActors: actors with collision whose location is within Range (UE2 radius test on the location)")]
        public override void BeginState(Actor self)
        {
            var chaff = (SProjChaffe)self;
            float range2 = (float)(chaff.Range * chaff.Range);
            foreach (SBasicGamePlayObject gpo in self.XLevel.AllActors<SBasicGamePlayObject>())
            {
                if (gpo.bCollideActors && !gpo.bDeleteMe && FVector.DistSquared(gpo.Location, self.Location) <= range2)
                {
                    gpo.eventSetLockedByChaff(chaff.TempsBrouillage);
                }
            }
        }

        [Uc("SBase.SProjChaffe.s_Brouillage.Tick", Sha1 = "4a0e798307741f76a5ea6efb56405d35d123013c")]
        [PortOf("uc:SBase.SProjChaffe:s_Brouillage.Tick", Spec = "spec/equipment/chaff-grenade.md#rules")]
        public override void Tick(Actor self, float DeltaTime) => ((SProjChaffe)self).NativeStateBrouillageTick();

        [Uc("SBase.SProjChaffe.s_Brouillage.Timer", Sha1 = "842d47d9443f6db763a74955d860559be935f9af")]
        [PortOf("uc:SBase.SProjChaffe:s_Brouillage.Timer", Spec = "spec/equipment/chaff-grenade.md#rules")]
        public override void Timer(Actor self) => self.Destroy();
    }
}
