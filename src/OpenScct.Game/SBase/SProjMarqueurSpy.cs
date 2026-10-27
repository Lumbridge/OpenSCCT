using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SProjMarqueurSpy: the spy bullet's wall marker (spec/equipment/spy-bullet.md rules 6 to 10). For
/// TempsMarqueur seconds the server marks every living merc within fDetectionRadius for the spies' radar, and takes
/// the mark off again when he leaves or the marker goes.
/// </summary>
public partial class SProjMarqueurSpy
{
    /// <summary>Sticky cams and mines do not attach to the marker.</summary>
    [Uc("SBase.SProjMarqueurSpy.AcceptStickableGPO", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
    [PortOf("uc:SBase.SProjMarqueurSpy:AcceptStickableGPO", Spec = "spec/equipment/spy-bullet.md#rules")]
    public override bool AcceptStickableGPO() => false;

    /// <summary>In rain the marker shows half as warm.</summary>
    [Uc("SBase.SProjMarqueurSpy.PreBeginPlay", Sha1 = "7cf035cdf3cf022e65ff4fbb199f16863f9d4da9")]
    [PortOf("uc:SBase.SProjMarqueurSpy:PreBeginPlay", Spec = "spec/equipment/spy-bullet.md#rules")]
    protected override void PreBeginPlay()
    {
        if (SLaserTrigger.InRain(Location))
        {
            HeatIntensity *= 0.5f;
        }
    }

    /// <summary>The server's marker lives TempsMarqueur seconds (no interaction, unlike other gameplay objects).</summary>
    [Uc("SBase.SProjMarqueurSpy.PostBeginPlay", Sha1 = "065370946ed1708ad65b799c0e493511a4d19c1f")]
    [PortOf("uc:SBase.SProjMarqueurSpy:PostBeginPlay", Spec = "spec/equipment/spy-bullet.md#rules")]
    protected override void PostBeginPlay()
    {
        // Not simulated in the script: a client's copy never starts the timer (net-replication.md rule 19.2).
        if (Role < ENetRole.ROLE_Authority)
        {
            return;
        }

        SetTimer(TempsMarqueur, false);
    }

    [Uc("SBase.SProjMarqueurSpy.Timer", Sha1 = "842d47d9443f6db763a74955d860559be935f9af")]
    [PortOf("uc:SBase.SProjMarqueurSpy:Timer", Spec = "spec/equipment/spy-bullet.md#rules")]
    protected override void Timer() => Destroy();

    /// <summary>Takes entry <paramref name="i"/> off the list and its merc's mark off.</summary>
    [Uc("SBase.SProjMarqueurSpy.DropDetected", Sha1 = "1a3d27c41036f7bcf504a5229f30abbcce2f2f52")]
    [PortOf("uc:SBase.SProjMarqueurSpy:DropDetected", Spec = "spec/equipment/spy-bullet.md#rules")]
    public void DropDetected(int i)
    {
        SPAWNDEFENSE? p = DetectedActor[i];
        DetectedActor.RemoveAt(i);
        p?.DetectedByMarqueurSpy(K.SpyBulletTeam, false);
    }

    /// <summary>The server's marker takes every mark it made off as it goes.</summary>
    [Uc("SBase.SProjMarqueurSpy.Destroyed", Sha1 = "a3839c57db630c64a5c53fa81a86e1e3b8e9dcf9")]
    [PortOf("uc:SBase.SProjMarqueurSpy:Destroyed", Spec = "spec/equipment/spy-bullet.md#rules")]
    protected override void Destroyed()
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            while (DetectedActor.Count > 0)
            {
                DropDetected(0);
            }
        }

        base.Destroyed();
    }

    /// <summary>Forgets a merc without touching his mark (a direct spy bullet hit keeps it).</summary>
    [Uc("SBase.SProjMarqueurSpy.RemoveActorInList", Sha1 = "b86063a53b8ab7f5410395d46ffc9e14f286c519")]
    [PortOf("uc:SBase.SProjMarqueurSpy:RemoveActorInList", Spec = "spec/equipment/spy-bullet.md#rules")]
    public void RemoveActorInList(SPAWNDEFENSE ActorToRemove)
    {
        int i = 0;
        while (i < DetectedActor.Count)
        {
            if (ReferenceEquals(DetectedActor[i], ActorToRemove))
            {
                DetectedActor.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>
    /// Server: every living, unmarked merc within fDetectionRadius is marked and listed first; a listed merc beyond
    /// 1.1 x fDetectionRadius is dropped.
    /// </summary>
    [Uc("SBase.SProjMarqueurSpy.Tick", Sha1 = "03a6edce30d63c9586a3cce5fe726eb4d07f55ae")]
    [PortOf("uc:SBase.SProjMarqueurSpy:Tick", Spec = "spec/equipment/spy-bullet.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "RadiusPawns walks the level's pawns in actor order, distance below the radius")]
    protected override void Tick(float DeltaTime)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            foreach (SBasePawn pawn in GadgetWorld.Pawns(XLevel))
            {
                if (pawn is SPAWNDEFENSE { bDeleteMe: false, bSpyBulletOn: false, Health: > 0 } p &&
                    (p.Location - Location).Size() < fDetectionRadius)
                {
                    DetectedActor.Insert(0, p);
                    p.HitBySpyBullet(K.SpyBulletTeam);
                }
            }

            int i = 0;
            while (i < DetectedActor.Count)
            {
                // A destroyed merc reads as None, whose location is the origin in the script.
                FVector at = DetectedActor[i] is { bDeleteMe: false } listed ? listed.Location : FVector.Zero;
                if ((at - Location).Size() > K.SpyZoneDropFactor * fDetectionRadius)
                {
                    DropDetected(i);
                }
                else
                {
                    i++;
                }
            }
        }

        base.Tick(DeltaTime);
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        for (int i = 0; i < DetectedActor.Count; i++)
        {
            DetectedActor[i] = Live(DetectedActor[i]);
        }
    }
}
