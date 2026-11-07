using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

public sealed class ShotgunPelletTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 23)]
    [InlineData(3, 71)]
    [InlineData(9, 214)]
    public void PartialHitsAccumulateOnlyThePelletsThatHit(int pellets, int damage)
    {
        var (scene, gun, target) = Rig();
        var rays = new Rays(scene.Level.Info, i => i < pellets ? target : null);
        scene.Level.SetService<IHitZones>(rays);
        gun.TraceFire(0, 0, 0);
        Assert.Equal(9, rays.Traces.Count);
        Assert.Equal(1000 - damage, target.Health);
    }

    [Fact]
    public void EachPelletsHitZoneIsScaledBeforeSummingAndTheFinalMultiplierRunsOnce()
    {
        var (scene, gun, target) = Rig();
        var rays = new Rays(scene.Level.Info, i => i < 3 ? target : null) { Pills = [1, 2, 5] };
        scene.Level.SetService<IHitZones>(rays);
        gun.TraceFire(0, 0, 0);
        // Base 119: head 357 + torso 119 + limb 23; trunc(499 * .2) = 99.
        Assert.Equal(901, target.Health);
        Assert.Equal(99, ((SPlayerController)target.Controller!).LastDamages[0].Damage);
    }

    [Fact]
    public void DifferentVictimsReceiveOnlyTheirOwnPelletsAndTheNextShotHasFreshScratch()
    {
        var (scene, gun, first) = Rig();
        var (_, second) = scene.Player(true, new FVector(200, 0, 100));
        second.Health = 1000;
        var rays = new Rays(scene.Level.Info, i => i == 0 ? first : second);
        scene.Level.SetService<IHitZones>(rays);
        gun.TraceFire(0, 0, 0);
        Assert.Equal(977, first.Health);
        Assert.Equal(810, second.Health);
        rays.Traces.Clear();
        gun.TraceFire(0, 0, 0);
        Assert.Equal(954, first.Health);
        Assert.Equal(620, second.Health);
    }

    [Fact]
    public void WorldMarksSitOnTheActualNinePelletIntersections()
    {
        var (scene, gun, _) = Rig();
        var rays = new Rays(scene.Level.Info, _ => scene.Level.Info);
        scene.Level.SetService<IHitZones>(rays);
        gun.TraceFire(0, 0, 0);
        var marks = ImpactTable.Of(scene.Level).Slots.Where(m => m.Live).ToArray();
        Assert.Equal(9, marks.Length);
        for (int i = 0; i < marks.Length; i++) Assert.Equal(rays.Traces[i].Hit, marks[i].Location);
        gun.SpawnImpacts();
        ((SWeaponDefenseDummy_Shotgun)gun.Dummy!).SpawnImpacts(false);
        Assert.Equal(9, ImpactTable.Of(scene.Level).Added);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16000)]
    public void SpreadIsACentreAndEightEquallySpacedPelletsInTheShotPlane(int pitch)
    {
        var (scene, gun, _) = Rig();
        gun.AccurateRotation = new FRotator(pitch, 7000, 3000);
        var rays = new Rays(scene.Level.Info, _ => null);
        scene.Level.SetService<IHitZones>(rays);
        gun.TraceFire(0, 0, 0);
        FVector centre = rays.Traces[0].End;
        FVector direction = (centre - rays.Traces[0].Start).SafeNormal();
        float radius = gun.ShootingRange * .194f * gun.RadiusScale;
        for (int i = 1; i < 9; i++)
        {
            FVector offset = rays.Traces[i].End - centre;
            FVector next = rays.Traces[i == 8 ? 1 : i + 1].End - centre;
            Assert.InRange(MathF.Abs(offset.Size() - radius), 0, .002f);
            Assert.InRange(MathF.Abs(offset | direction), 0, .002f);
            Assert.InRange(MathF.Abs((offset.SafeNormal() | next.SafeNormal()) - MathF.Sqrt(.5f)), 0, .0001f);
        }
    }

    [Fact]
    public void NumberedOwnerPredictionAndAuthorityUseTheSameGeometryWithoutPredictingDamage()
    {
        var (owner, ownerGun, ownerVictim) = Rig(ENetMode.NM_Client);
        ownerGun.Role = ENetRole.ROLE_AutonomousProxy;
        owner.Level.SetService(new FirePrediction());
        var ownerRays = new Rays(owner.Level.Info, _ => ownerVictim);
        owner.Level.SetService<IHitZones>(ownerRays);
        ownerGun.ServerFireWeapon();
        Assert.Equal(1000, ownerVictim.Health);
        Assert.Equal(9, ownerRays.Traces.Count);

        var (server, serverGun, serverVictim) = Rig();
        var serverRays = new Rays(server.Level.Info, _ => serverVictim);
        server.Level.SetService<IHitZones>(serverRays);
        serverGun.BeginClientShot(ownerGun.ShotCounter, true);
        try { serverGun.TraceFire(serverGun.GetAccuracy(), 0, 0); }
        finally { serverGun.EndClientShot(); }
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(ownerRays.Traces[i].Start, serverRays.Traces[i].Start);
            Assert.Equal(ownerRays.Traces[i].End, serverRays.Traces[i].End);
        }
        Assert.Equal(786, serverVictim.Health);
    }

    private static (CombatScene Scene, SWeaponDefense_Shotgun Gun, SBasePawn Target) Rig(
        ENetMode mode = ENetMode.NM_Standalone)
    {
        var scene = new CombatScene(mode);
        var (_, merc) = scene.Player(false, new FVector(0, 0, 100));
        var (_, spy) = scene.Player(true, new FVector(100, 0, 100));
        var gun = scene.Give(new SWeaponDefense_Shotgun(), merc);
        gun.GiveTo(merc);
        scene.Run(1);
        spy.Health = 1000;
        return (scene, gun, spy);
    }

    private sealed class Rays(Actor world, Func<int, Actor?> target) : IHitZones
    {
        public List<(FVector Start, FVector End, FVector Hit)> Traces { get; } = [];
        public int[] Pills { get; set; } = [];

        public BoneTraceResult TraceBone(Actor shooter, FVector end, FVector start, bool realFire)
        {
            int index = Traces.Count;
            Actor? actor = target(index);
            FVector hit = actor is null ? end : start + (end - start).SafeNormal() * 100;
            Traces.Add((start, end, hit));
            return new BoneTraceResult { bHit = actor is not null, Actor = actor ?? world, HitLocation = hit,
                HitNormal = (start - end).SafeNormal(), PillTag = index < Pills.Length ? Pills[index] : 2,
                SurfaceType = ESurfaceType.SURFACE_ConcreteHard };
        }
    }
}
