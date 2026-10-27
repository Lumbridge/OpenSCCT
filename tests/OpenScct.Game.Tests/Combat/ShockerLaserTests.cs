using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

/// <summary>
/// The spy laser's native side (spec/equipment/spy-shocker.md "Laser drawing (native)", decoded from
/// execTickSWeaponAttaque 0x10ab7870 and the placement 0x10ab7c80): the dot for every spy pistol and the beam for the
/// local player's own one while bLaserOn, both gone when it is off, nothing on a dedicated server; the dot 10 uu before
/// the first hit bullets do not pass, along the line, scaled by its distance from the view; the beam from its start to
/// the hit at (length + 1, 0.5, 0.5).
/// </summary>
public sealed class ShockerLaserTests
{
    /// <summary>A wall across +X at <see cref="X"/>, and a glass pane (bBulletGoTru) in front of it when given.</summary>
    private sealed class Wall : ICollisionWorld
    {
        public float X = 1000f;
        public Actor? Glass;
        public float GlassX = 500f;

        public bool LineCheck(out FCheckResult Hit, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags)
        {
            var hits = new List<FCheckResult>();
            MultiLineCheck(hits, SourceActor, End, Start, Extent, TraceFlags);
            Hit = hits.Count > 0 ? hits[0] : FCheckResult.NoHit(End);
            return hits.Count > 0;
        }

        public int MultiLineCheck(List<FCheckResult> Hits, Actor? SourceActor, FVector End, FVector Start, FVector Extent, ETraceFlags TraceFlags)
        {
            Hits.Clear();
            if (Glass is not null)
            {
                Add(Hits, Start, End, GlassX, Glass);
            }

            Add(Hits, Start, End, X, null);
            return Hits.Count;
        }

        private static void Add(List<FCheckResult> hits, FVector start, FVector end, float x, Actor? actor)
        {
            if (end.X == start.X)
            {
                return;
            }

            float t = (x - start.X) / (end.X - start.X);
            if (t <= 0f || t >= 1f)
            {
                return;
            }

            FCheckResult r = FCheckResult.NoHit(end);
            r.Time = t;
            r.Location = start + (end - start) * t;
            r.Normal = new FVector(-1f, 0f, 0f);
            r.Actor = actor;
            hits.Add(r);
            hits.Sort((p, q) => p.Time.CompareTo(q.Time));
        }

        public bool PointCheck(out FCheckResult Hit, Actor? SourceActor, FVector Location, FVector Extent, ETraceFlags TraceFlags)
        {
            Hit = FCheckResult.NoHit(Location);
            return false;
        }

        public bool FastTrace(FVector End, FVector Start) => true;

        public int GetZoneIndex(FVector Location) => 0;

        public ZoneInfo? GetZoneInfo(FVector Location) => null;
    }

    private static (CombatScene Scene, SPlayerController PC, SPawnAttaque Spy, SStickyShocker Pistol, Wall Wall) Spy(
        ENetMode mode = ENetMode.NM_Client, bool local = true)
    {
        var scene = new CombatScene(mode);
        var wall = new Wall();
        scene.Level.SetService<ICollisionWorld>(wall);
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);
        spy.WeaponDummy = pistol;
        spy.WeaponLoaded = 2;
        pc.Rotation = default;
        if (local)
        {
            scene.Level.Info.LocalPlayerController = pc;
        }

        scene.Run(0.05f);
        return (scene, pc, spy, pistol, wall);
    }

    private static void LaserOn(CombatScene scene, SStickyShocker pistol, bool on)
    {
        pistol.bLaserOn = on;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        scene.Run(1f / Sim.Hz);
        pistol.bLaserOn = on;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
    }

    [Fact]
    public void TheLocalSpyGetsTheDotAndTheBeamWhileTheLaserIsOn()
    {
        (CombatScene scene, _, _, SStickyShocker pistol, _) = Spy();
        LaserOn(scene, pistol, true);
        Assert.IsType<SLaserProjector>(pistol.LaserProjector);
        Assert.NotNull(pistol.LaserBeam);

        LaserOn(scene, pistol, false);
        Assert.Null(pistol.LaserProjector);
        Assert.Null(pistol.LaserBeam);
    }

    [Fact]
    public void AnotherPlayersSpyShowsOnlyTheDot()
    {
        (CombatScene scene, _, _, SStickyShocker pistol, _) = Spy(local: false);
        LaserOn(scene, pistol, true);
        Assert.NotNull(pistol.LaserProjector);
        Assert.Null(pistol.LaserBeam);
    }

    [Fact]
    public void ADedicatedServerSpawnsNoLaserActors()
    {
        (CombatScene scene, _, _, SStickyShocker pistol, _) = Spy(ENetMode.NM_DedicatedServer, local: false);
        LaserOn(scene, pistol, true);
        Assert.Null(pistol.LaserProjector);
        Assert.Null(pistol.LaserBeam);
    }

    [Fact]
    public void TheDotSitsTenUnitsBeforeTheWallScaledByItsDistanceFromTheView()
    {
        (CombatScene scene, SPlayerController pc, _, SStickyShocker pistol, Wall wall) = Spy();
        LaserOn(scene, pistol, true);
        FVector view = new(-200f, 0f, 160f);
        pistol.PlaceLaser(view);

        Projector dot = pistol.LaserProjector!;
        FCoords hand = CombatServices.Ports(pistol).BoneCoords(pistol, SWeaponDefense.FxBone);
        FVector start = hand.Origin + hand.XAxis * 30f - hand.YAxis * 4f + hand.ZAxis * 8f;
        float t = (wall.X - start.X) / hand.XAxis.X;
        FVector hit = start + hand.XAxis * t;
        Assert.Equal(hit.X - 10f * hand.XAxis.X, dot.Location.X, 2);
        Assert.Equal(hit.Y, dot.Location.Y, 1);
        Assert.Equal(System.MathF.Max(FVector.Dist(hit, view) / 1500f, 0.05f), dot.DrawScale, 4);
        Assert.True(dot.bProjectOnBackfaces);
        Assert.False(dot.bProjectActor);

        SLaserBeam beam = pistol.LaserBeam!;
        Assert.Equal(FVector.Dist(hit, start) + 1f, beam.DrawScale3D.X, 2);
        Assert.Equal(0.5f, beam.DrawScale3D.Y);
        Assert.Equal(0.5f, beam.DrawScale3D.Z);
        Assert.Equal(start.X, beam.Location.X, 2);
        _ = pc;
    }

    [Fact]
    public void ANearDotKeepsTheSmallestScaleAndBulletPassableGlassIsLookedThrough()
    {
        (CombatScene scene, _, SPawnAttaque spy, SStickyShocker pistol, Wall wall) = Spy();
        Actor glass = scene.Level.SpawnActor(new SLaserBeam(), new FVector(500f, 0f, 160f), default)!;
        glass.bBulletGoTru = true;
        wall.Glass = glass;
        wall.X = 120f;
        LaserOn(scene, pistol, true);
        // A view 30 uu from the wall: under 75 uu the scale stays at its 0.05 floor.
        pistol.PlaceLaser(new FVector(90f, 0f, 160f));
        Projector dot = pistol.LaserProjector!;
        Assert.True(dot.Location.X < 120f);
        Assert.Equal(0.05f, dot.DrawScale, 4);

        // The wall moves behind the glass: the dot goes through the glass to the wall.
        wall.X = 900f;
        pistol.PlaceLaser(spy.Location);
        Assert.True(dot.Location.X > 880f, $"dot at {dot.Location}");
    }

    [Fact]
    public void ShotsCountOneMuzzleMeshPerTick()
    {
        (CombatScene scene, _, _, SStickyShocker pistol, _) = Spy();
        pistol.MuzzleCount = 3;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        Assert.Equal(1, pistol.MyMuzzleCount);
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        Assert.Equal(3, pistol.MyMuzzleCount);
        _ = scene;
    }

    [Fact]
    public void ThePistolHangsAtTheHolsterInTheHandOrInTheOtherHand()
    {
        (CombatScene scene, _, SPawnAttaque spy, SStickyShocker pistol, _) = Spy();
        spy.WeaponLoaded = 0;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        Assert.Equal(new Name("HOLSTER"), pistol.AttachmentBone);
        spy.WeaponLoaded = 7;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        Assert.Equal(new Name("B L Hand"), pistol.AttachmentBone);
        Assert.Equal(new FVector(2f, -8f, 2f), pistol.RelativeLocation);
        Assert.Equal(0x8000, pistol.RelativeRotation.Roll);
        spy.WeaponLoaded = 2;
        pistol.TickSWeaponAttaque(Sim.DeltaTime);
        Assert.Equal(new Name("B R Hand"), pistol.AttachmentBone);
        Assert.Equal(default, pistol.RelativeLocation);
        _ = scene;
    }
}
