using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeTests(ITestOutputHelper output)
{
    private static CombatScene Scene(bool enabled = true, ENetMode mode = ENetMode.NM_ListenServer)
    {
        var scene = new CombatScene(mode);
        scene.Level.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = enabled }, default, default);
        return scene;
    }
    private static SSmokeEmitter Cloud(CombatScene scene, FVector at = default) => scene.Level.SpawnActor(new SSmokeEmitter(), at, default)!;
    private static ResponsiveSmokeField Field(CombatScene scene, SSmokeEmitter cloud)
    { Assert.True(ResponsiveSmokeWorld.For(scene.Level).TryGet(cloud, out var field)); return field; }

    [Fact]
    public void DefaultOffRetainsSphericalSmokeAndAllocatesNoFields()
    {
        CombatScene scene = Scene(false);
        var (pc, _) = GadgetKit.Local(scene, false, new FVector(50, 0, 0)); pc.fLastTimeWakeUp = -10;
        Cloud(scene); scene.Run(.1f);
        Assert.True(pc.bInSmoke);
        Assert.Empty(ResponsiveSmokeWorld.For(scene.Level).Fields);
    }

    [Fact]
    public void ColdCloudFillsItsCenterPromptlyAndSettlesWithoutMoreGeometryQueries()
    {
        CombatScene scene = Scene(); SSmokeEmitter cloud = Cloud(scene);
        var watch = Stopwatch.StartNew(); scene.Run(1.5f); watch.Stop();
        ResponsiveSmokeField field = Field(scene, cloud);
        Assert.True(field.SampleDensity(default, scene.Time) > .95f);
        Assert.True(field.SampleDensity(new FVector(150, 0, 0), scene.Time) > .95f);
        Assert.Equal(0f, field.SampleDensity(new FVector(301, 0, 0), scene.Time));
        var volume = ResponsiveSmokeWorld.For(scene.Level); long queries = volume.GeometryQueries;
        scene.Run(.5f); Assert.Equal(queries, volume.GeometryQueries);
        Assert.InRange(queries, 1, ResponsiveSmokeField.CellCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ThinWallBlocksArrivalAndInterpolation()
    {
        CombatScene scene = Scene();
        GadgetKit.Walls(scene, (new FVector(49, -1000, -1000), new FVector(51, 1000, 1000), CollisionMaterialFlags.None));
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        ResponsiveSmokeField field = Field(scene, cloud);
        Assert.True(field.SampleDensity(new FVector(-50, 0, 0), scene.Time) > .9f);
        Assert.Equal(0f, field.SampleDensity(new FVector(50, 0, 0), scene.Time));
        Assert.Equal(0f, field.SampleDensity(new FVector(120, 0, 0), scene.Time));
    }

    [Fact]
    public void CloudFlowsAroundAnObstacleThroughConnectedAir()
    {
        CombatScene scene = Scene();
        GadgetKit.Walls(scene, (new FVector(49, -60, -1000), new FVector(51, 60, 1000), CollisionMaterialFlags.None));
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        Assert.True(Field(scene, cloud).SampleDensity(new FVector(125, 0, 0), scene.Time) > .9f);
    }

    [Fact]
    public void ZeroThicknessWallExactlyOnVoxelPlaneBlocksBothDirections()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        builder.AddQuad(new FVector(50, -1000, -1000), new FVector(50, 1000, -1000),
            new FVector(50, 1000, 1000), new FVector(50, -1000, 1000));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        SSmokeEmitter left = Cloud(scene), right = Cloud(scene, new FVector(150, 0, 0)); scene.Run(2);
        Assert.Equal(0f, Field(scene, left).SampleDensity(new FVector(125, 0, 0), scene.Time));
        Assert.Equal(0f, Field(scene, right).SampleDensity(new FVector(0, 0, 0), scene.Time));
        Assert.Equal(0f, Field(scene, left).SampleDensity(new FVector(50, 0, 0), scene.Time));
    }

    [Fact]
    public void NearWallSmokeSeedCannotStartOnTheOtherSide()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        builder.AddQuad(new FVector(1, -1000, -1000), new FVector(1, 1000, -1000),
            new FVector(1, 1000, 1000), new FVector(1, -1000, 1000));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        // The closest free cell (-12.5uu) is across the wall; the reachable cell at +37.5uu must seed the field.
        SSmokeEmitter cloud = Cloud(scene, new FVector(2, 0, 0)); scene.Run(2);
        Assert.True(Field(scene, cloud).SampleDensity(new FVector(100, 0, 0), scene.Time) > .9f);
        Assert.Equal(0f, Field(scene, cloud).SampleDensity(new FVector(-100, 0, 0), scene.Time));
    }

    [Fact]
    public void FullyObstructedSmokeSpawnCannotSeedThroughItsEnclosure()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        builder.AddBoxHullCollider(ColliderKind.Mover, null,
            ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockNonZeroExtent | ColliderFlags.BlockZeroExtent,
            new FVector(-5, -5, -5), new FVector(5, 5, 5));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        Assert.Equal(0f, Field(scene, cloud).SampleDensity(new FVector(100, 0, 0), scene.Time));
        Assert.All(Field(scene, cloud).ActivationTicks, tick => Assert.Equal(ResponsiveSmokeField.Unreached, tick));
    }

    [Fact]
    public void OpeningAndClosingDoorPreservesReleasedPocket()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        int door = builder.AddBoxHullCollider(ColliderKind.Mover, null,
            ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockNonZeroExtent | ColliderFlags.BlockZeroExtent,
            new FVector(49, -1000, -1000), new FVector(51, 1000, 1000));
        CollisionWorld collision = CollisionWorld.Build(builder.Build()); scene.Level.SetService<ICollisionWorld>(collision);
        SSmokeEmitter cloud = Cloud(scene); scene.Run(1.5f);
        ResponsiveSmokeField field = Field(scene, cloud); FVector pocket = new(125, 0, 0);
        Assert.Equal(0f, field.SampleDensity(pocket, scene.Time));
        collision.SetColliderEnabled(door, false); scene.Run(1.5f);
        Assert.True(field.SampleDensity(pocket, scene.Time) > .9f);
        collision.SetColliderEnabled(door, true); scene.Run(.5f);
        Assert.True(field.SampleDensity(pocket, scene.Time) > .9f);
        Assert.Equal(0f, field.SampleDensity(new FVector(50, 0, 0), scene.Time));
    }

    [Fact]
    public void BulletCarvesOnlyItsSegmentThenRecovers()
    {
        CombatScene scene = Scene(); SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        ResponsiveSmokeField field = Field(scene, cloud);
        ResponsiveSmokeWorld.Bullet(cloud, new FVector(-250, 0, 0), new FVector(0, 0, 0));
        Assert.Equal(0f, field.SampleDensity(new FVector(-100, 0, 0), scene.Time));
        Assert.True(field.SampleDensity(new FVector(100, 0, 0), scene.Time) > .9f);
        Assert.True(field.SampleDensity(new FVector(-100, 40, 0), scene.Time) > .9f);
        scene.Run(.25f); Assert.InRange(field.SampleDensity(new FVector(-100, 0, 0), scene.Time), .1f, .8f);
        scene.Run(.3f); Assert.True(field.SampleDensity(new FVector(-100, 0, 0), scene.Time) > .9f);
    }

    [Fact]
    public void OpticalDepthQueriesFollowTheSharedBulletCavity()
    {
        CombatScene scene = Scene(); SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        var world = ResponsiveSmokeWorld.For(scene.Level);
        FVector start = new(-500, 0, 0), end = new(500, 0, 0);
        Assert.True(world.OpticalDepth(start, end) > 4f); Assert.True(world.SmokeBetween(start, end));
        ResponsiveSmokeWorld.Bullet(cloud, start, end);
        Assert.Equal(0f, world.OpticalDepth(start, end)); Assert.False(world.SmokeBetween(start, end));
        Assert.Equal(0f, world.OpticalDepth(default, default));
    }

    [Fact]
    public void BlastCavityProvidesBreathingRoomAndWallStopsBlast()
    {
        CombatScene scene = Scene();
        GadgetKit.Walls(scene, (new FVector(49, -60, -1000), new FVector(51, 60, 1000), CollisionMaterialFlags.None));
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        ResponsiveSmokeField field = Field(scene, cloud);
        Assert.True(field.SampleDensity(new FVector(125, 0, 0), scene.Time) > .9f);
        ResponsiveSmokeWorld.Blast(cloud, default, 300);
        Assert.Equal(0f, ResponsiveSmokeWorld.BreathingDensity(field, new FVector(-100, 0, 0), scene.Time));
        Assert.True(field.SampleDensity(new FVector(125, 0, 0), scene.Time) > .9f);
        scene.Run(2.1f); Assert.True(field.SampleDensity(new FVector(-100, 0, 0), scene.Time) > .9f);
    }

    [Fact]
    public void FloorRestingFragClearsSmokeWithoutCrossingTheThinWall()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        builder.AddBox(new FVector(-1000, -1000, -25), new FVector(1000, 1000, 0));
        builder.AddQuad(new FVector(50, -1000, 0), new FVector(50, 1000, 0),
            new FVector(50, 1000, 1000), new FVector(50, -1000, 1000));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        SSmokeEmitter near = Cloud(scene, new FVector(0, 0, 100)), far = Cloud(scene, new FVector(125, 0, 100));
        scene.Run(2);
        Assert.True(Field(scene, near).SampleDensity(near.Location, scene.Time) > .9f);
        Assert.True(Field(scene, far).SampleDensity(far.Location, scene.Time) > .9f);
        SProjFrag frag = scene.Level.SpawnActor(new SProjFrag(), new FVector(0, 0, 2), default)!;
        Assert.Equal(frag.CollisionHeight, frag.Location.Z);
        frag.Explode(frag.Location, new FVector(0, 0, 1));
        Assert.Equal(0f, Field(scene, near).SampleDensity(near.Location, scene.Time));
        Assert.True(Field(scene, far).SampleDensity(far.Location, scene.Time) > .9f);
    }

    [Fact]
    public void WallMountedMineProjectsOnlyToItsOwnSideOfTheThinWall()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        builder.AddQuad(new FVector(0, -1000, -1000), new FVector(0, 1000, -1000),
            new FVector(0, 1000, 1000), new FVector(0, -1000, 1000));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        SSmokeEmitter near = Cloud(scene, new FVector(-100, 0, 100)), far = Cloud(scene, new FVector(100, 0, 100));
        scene.Run(2);
        SMineProx mine = scene.Level.SpawnActor(new SMineProx(), new FVector(-1, 0, 100), new FRotator(0, 32768, 0))!;
        Assert.InRange(mine.refloc.X, -7, -5);
        mine.Explosion();
        Assert.Equal(0f, Field(scene, near).SampleDensity(near.Location, scene.Time));
        Assert.True(Field(scene, far).SampleDensity(far.Location, scene.Time) > .9f);
    }

    [Fact]
    public void GasUsesBreathingVolumeAndRetainsMaskProtection()
    {
        CombatScene scene = Scene();
        var (pc, pawn) = GadgetKit.Local(scene, false, default); pc.fLastTimeWakeUp = -10;
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        Assert.True(pc.bInSmoke);
        void RunGas(float seconds)
        {
            for (int i = 0; i < (int)(seconds * Sim.Hz); i++) { pc.CombatPlayerTick(Sim.DeltaTime); scene.World.Step(); }
        }
        ResponsiveSmokeWorld.Blast(cloud, OpenScct.Game.AI.AiSight.Eye(pawn), 200); RunGas(.1f);
        Assert.False(pc.bInSmoke); Assert.True(pc.btoCought); // Existing cough lingers after breathing room opens.
        RunGas(2.2f); Assert.True(pc.bInSmoke);
        pawn.bGasMask = true; RunGas(.1f); Assert.False(pc.bInSmoke);
    }

    [Fact]
    public void OverlapUsesActualDensityAndStableContributingSource()
    {
        CombatScene scene = Scene(); var (_, pawn) = GadgetKit.Local(scene, false, default);
        SSmokeEmitter first = Cloud(scene), second = Cloud(scene, new FVector(20, 0, 0)); scene.Run(2);
        var world = ResponsiveSmokeWorld.For(scene.Level);
        Assert.Same(first, world.ExposureSource(pawn, world.CurrentTime, out float density)); Assert.Equal(1f, density);
        Field(scene, first).AddCarve(new(999, ResponsiveSmokeCarveKind.Bullet, new FVector(0, 0, -200), new FVector(0, 0, 200), 100, 6, scene.Time, .1f, .4f, null));
        Assert.Same(second, world.ExposureSource(pawn, world.CurrentTime, out _));
    }

    [Fact]
    public void OverlappingCloudsClampAtEachBreathingSampleBeforeAveraging()
    {
        CombatScene scene = Scene(); var (_, pawn) = GadgetKit.Local(scene, false, default);
        SSmokeEmitter a = Cloud(scene), b = Cloud(scene); scene.Run(2);
        FVector eye = OpenScct.Game.AI.AiSight.Eye(pawn);
        FVector[] clear = [eye, eye - new FVector(10, 0, 0), eye + new FVector(0, 10, 0), eye - new FVector(0, 10, 0),
            eye + new FVector(0, 0, 10), eye - new FVector(0, 0, 10)];
        uint sequence = 1;
        foreach (SSmokeEmitter cloud in new[] { a, b }) foreach (FVector p in clear)
            Field(scene, cloud).AddCarve(new(sequence++, ResponsiveSmokeCarveKind.Bullet, p, p, 1, 1, scene.Time, .1f, .4f, null));
        var world = ResponsiveSmokeWorld.For(scene.Level);
        Assert.Same(a, world.ExposureSource(pawn, world.CurrentTime, out float density));
        Assert.InRange(density, .14f, .15f);
        Assert.True(density < ResponsiveSmokeWorld.ExposureThreshold);
    }

    [Fact]
    public void InstallingAnAuthoritativeSnapshotCancelsOlderFrontWork()
    {
        CombatScene scene = Scene(); SSmokeEmitter cloud = Cloud(scene); scene.World.Step();
        var world = ResponsiveSmokeWorld.For(scene.Level); ResponsiveSmokeSnapshot snapshot = world.Capture(cloud);
        var cells = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(cells, (byte)1);
        var arrivals = new ushort[cells.Length]; Array.Fill(arrivals, (ushort)123);
        snapshot = snapshot with { Revision = snapshot.Revision + 1, Cells = cells, ActivationTicks = arrivals };
        world.ApplySnapshot(cloud, snapshot); scene.Run(.5f);
        Assert.Equal(arrivals, world.Capture(cloud).ActivationTicks);
    }

    [Fact]
    public void SnapshotAndSparseDeltaRoundTripWithoutAliasing()
    {
        CombatScene scene = Scene(); SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        var world = ResponsiveSmokeWorld.For(scene.Level); ResponsiveSmokeSnapshot before = world.Capture(cloud);
        ResponsiveSmokeSnapshot decoded = ResponsiveSmokeCodec.Decode(ResponsiveSmokeCodec.Encode(before));
        Assert.Equal(before.Cells, decoded.Cells); Assert.Equal(before.ActivationTicks, decoded.ActivationTicks);
        ResponsiveSmokeWorld.Bullet(cloud, new FVector(-100, 0, 0), new FVector(100, 0, 0));
        ResponsiveSmokeSnapshot after = world.Capture(cloud);
        ResponsiveSmokeSnapshot delta = ResponsiveSmokeCodec.DecodeDelta(before, ResponsiveSmokeCodec.EncodeDelta(before, after));
        Assert.Equal(after.Revision, delta.Revision); Assert.Single(delta.Carves); Assert.Empty(before.Carves);
        decoded.Cells[0] = (byte)(decoded.Cells[0] == 0 ? 1 : 0); Assert.NotEqual(decoded.Cells[0], before.Cells[0]);
        Assert.Throws<InvalidDataException>(() => ResponsiveSmokeCodec.DecodeDelta(after, ResponsiveSmokeCodec.EncodeDelta(before, after)));
        Assert.Throws<InvalidDataException>(() => ResponsiveSmokeCodec.Decode(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void FastCarvesSurviveAnOlderBaselineAndCanArriveBeforeTheRule()
    {
        CombatScene server = Scene(); SSmokeEmitter source = Cloud(server); server.Run(2);
        var authority = ResponsiveSmokeWorld.For(server.Level); ResponsiveSmokeSnapshot baseline = authority.Capture(source);
        ResponsiveSmokeWorld.Bullet(source, new FVector(-100, 0, 0), new FVector(100, 0, 0));
        ResponsiveSmokeSnapshot newer = authority.Capture(source);
        CombatScene client = Scene(false, ENetMode.NM_Client); SSmokeEmitter copy = Cloud(client);
        var replica = ResponsiveSmokeWorld.For(client.Level); replica.SynchronizeTime(server.Time);
        replica.ApplyCarves(copy, newer.Revision, newer.CapturedAt, newer.Carves);
        replica.ApplySnapshot(copy, baseline);
        ResponsiveSmokeField field = Field(client, copy);
        Assert.Single(field.Carves); Assert.Equal(newer.Revision, field.Revision);
        Assert.Equal(0f, field.SampleDensity(default, replica.CurrentTime));
        Assert.True(field.SampleDensity(new FVector(0, 75, 0), replica.CurrentTime) > .9f);
    }

    [Fact]
    public void ClientPredictedActorsCannotAuthorBulletOrBlastCarves()
    {
        CombatScene server = Scene(); SSmokeEmitter authorityCloud = Cloud(server); server.Run(2);
        var authority = ResponsiveSmokeWorld.For(server.Level);
        CombatScene client = Scene(true, ENetMode.NM_Client); SSmokeEmitter replicaCloud = Cloud(client);
        var replica = ResponsiveSmokeWorld.For(client.Level); replica.SynchronizeTime(server.Time);
        replica.ApplySnapshot(replicaCloud, authority.Capture(authorityCloud));
        // A locally created predicted source may still hold the default authority role.
        replicaCloud.Role = ENetRole.ROLE_Authority;
        ResponsiveSmokeWorld.Bullet(replicaCloud, new FVector(-500, 0, 0), new FVector(500, 0, 0));
        ResponsiveSmokeWorld.Blast(replicaCloud, default, 300);
        Assert.Empty(Field(client, replicaCloud).Carves);
        Assert.True(replica.SampleDensity(default) > .9f);
    }

    [Fact]
    public void DisplacementUsesWorldTimeDuringHistoricalWeaponCommands()
    {
        CombatScene scene = Scene(); SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        float worldTime = scene.Time;
        using (scene.Level.CommandTime(0, 0, Sim.DeltaTime)) ResponsiveSmokeWorld.Bullet(cloud, new FVector(-100, 0, 0), new FVector(100, 0, 0));
        Assert.Equal(worldTime, Assert.Single(Field(scene, cloud).Carves).CreatedAt);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    [InlineData(3, true, false)]
    public void ActualWeaponRaysClearHitsAndMissesIncludingScopeAndPellets(int kind, bool hit, bool scoped)
    {
        CombatScene scene = Scene(); var (pc, pawn) = GadgetKit.Local(scene, false, new FVector(-200, 0, 0));
        SWeaponDefense weapon = kind switch { 1 => new SWeaponDefense_Uzi(), 2 => new MercSidearm(), 3 => new SWeaponDefense_Shotgun(), _ => new SWeaponDefense() };
        weapon = scene.Give(weapon, pawn); weapon.GiveTo(pawn); pawn.WeaponLoaded = 2;
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        if (scoped) pc.GotoState(new Name("s_Binoculars_Def"));
        var traces = new SmokeShotTraces(scene.Level.Info, hit); scene.Level.SetService<IHitZones>(traces);
        weapon.TraceFire(0, 0, 0);
        ResponsiveSmokeField field = Field(scene, cloud);
        Assert.Equal(kind == 3 ? 9 : 1, field.Carves.Count);
        Assert.Equal(traces.Rays.Count, field.Carves.Count);
        for (int i = 0; i < traces.Rays.Count; i++)
        {
            Assert.Equal(traces.Rays[i].Start, field.Carves[i].Start);
            Assert.Equal(traces.Rays[i].End, field.Carves[i].End);
        }
    }

    [Fact]
    public void PenetratingBulletKeepsBothSegmentsButShockerDoesNotClear()
    {
        CombatScene scene = Scene(); var (_, pawn) = GadgetKit.Local(scene, false, new FVector(-200, 0, 0));
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), pawn); rifle.GiveTo(pawn);
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        var traces = new SmokeShotTraces(scene.Level.Info, false) { FakeCeiling = true }; scene.Level.SetService<IHitZones>(traces);
        rifle.TraceFire(0, 0, 0);
        Assert.Equal(2, Field(scene, cloud).Carves.Count);
        var (_, spy) = GadgetKit.Local(scene, true, new FVector(-200, 0, 0));
        SStickyShocker shocker = GadgetKit.Shocker(scene, spy);
        shocker.TraceFireWithPos(spy.Location, default, 0, 0, 0);
        Assert.Equal(2, Field(scene, cloud).Carves.Count);
    }

    private sealed class SmokeShotTraces(Actor world, bool hit) : IHitZones
    {
        public bool FakeCeiling;
        public List<(FVector Start, FVector End)> Rays { get; } = new();
        public BoneTraceResult TraceBone(Actor shooter, FVector end, FVector start, bool realFire)
        {
            bool fake = FakeCeiling && Rays.Count == 0;
            FVector ray = end - start;
            FVector at = hit || fake ? start + ray * (100f / Math.Max(.001f, ray.Size())) : end;
            Rays.Add((start, at));
            return new BoneTraceResult { Actor = world, bHit = hit || fake, HitLocation = at, HitNormal = new FVector(-1, 0, 0),
                SurfaceType = fake ? ESurfaceType.SURFACE_FakeCeiling : ESurfaceType.SURFACE_ConcreteHard };
        }
    }

    [Fact]
    public void SixMatureCloudsReuseGeometryAndCleanupTheFinalCloud()
    {
        CombatScene scene = Scene(); var clouds = new SSmokeEmitter[6];
        var lattice = Stopwatch.StartNew(); ResponsiveSmokeNoise.Warmup(); lattice.Stop();
        var preparation = Stopwatch.StartNew(); ResponsiveSmokeWorld.Warmup(scene.Level); preparation.Stop();
        Assert.Null(scene.Level.GetService<ResponsiveSmokeWorld>());
        GadgetKit.Walls(scene, (new FVector(-500, -500, -30), new FVector(2000, 500, 0), CollisionMaterialFlags.None),
            (new FVector(375, -80, 0), new FVector(385, 80, 400), CollisionMaterialFlags.None));
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < clouds.Length; i++)
        { clouds[i] = Cloud(scene, new FVector(i * 250, 0, 100)); GadgetKit.Local(scene, false, new FVector(i * 250, 50, 100)); }
        var frames = new double[120];
        for (int i = 0; i < frames.Length; i++)
        { long start = Stopwatch.GetTimestamp(); scene.World.Step(); frames[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
        watch.Stop(); double cold = watch.Elapsed.TotalMilliseconds; Array.Sort(frames);
        var world = ResponsiveSmokeWorld.For(scene.Level); long queries = world.GeometryQueries;
        var matureFrames = new double[120];
        watch.Restart();
        for (int i = 0; i < matureFrames.Length; i++)
        { long start = Stopwatch.GetTimestamp(); scene.World.Step(); matureFrames[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
        watch.Stop(); Array.Sort(matureFrames);
        Assert.Equal(queries, world.GeometryQueries);
        Assert.All(clouds, cloud => Assert.True(Field(scene, cloud).SampleDensity(cloud.Location, scene.Time) > .9f));
        output.WriteLine($"Six clouds and six mercs with floor/wall collision: macro lattice {lattice.Elapsed.TotalMilliseconds:F2} ms; remaining gated loading warmup {preparation.Elapsed.TotalMilliseconds:F2} ms; cold 120 ticks {cold:F2} ms, p95 {frames[113]:F3} ms, max {frames[^1]:F3} ms; mature 120 ticks {watch.Elapsed.TotalMilliseconds:F2} ms ({watch.Elapsed.TotalMilliseconds / 120:F3} ms/tick), p95 {matureFrames[113]:F3} ms, max {matureFrames[^1]:F3} ms; shared geometry queries {queries}.");
        foreach (SSmokeEmitter cloud in clouds) cloud.Destroy();
        Assert.Empty(world.Fields);
    }

    [Fact]
    public void DisabledWarmupDoesNotCreateAServiceOrAdvanceTheMatch()
    {
        CombatScene scene = Scene(false); int actors = scene.Level.ActorCount; long tick = scene.Level.TickCount;
        ResponsiveSmokeWorld.Warmup(scene.Level);
        Assert.Null(scene.Level.GetService<ResponsiveSmokeWorld>()); Assert.Equal(actors, scene.Level.ActorCount); Assert.Equal(tick, scene.Level.TickCount);
    }

    [Fact]
    public void RepeatedMovingDoorInvalidationIsBoundedAndDestroyedCloudsLeaveNoWork()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        int door = builder.AddBoxHullCollider(ColliderKind.Mover, null,
            ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockNonZeroExtent | ColliderFlags.BlockZeroExtent,
            new FVector(-1, -1000, -1000), new FVector(1, 1000, 1000), new FVector(50, 0, 0));
        CollisionWorld collision = CollisionWorld.Build(builder.Build()); scene.Level.SetService<ICollisionWorld>(collision);
        var clouds = new SSmokeEmitter[6]; for (int i = 0; i < clouds.Length; i++) clouds[i] = Cloud(scene, new FVector(-100 + i * 20, 0, 0));
        scene.Run(2); var world = ResponsiveSmokeWorld.For(scene.Level);
        for (int i = 0; i < 90; i++)
        {
            collision.SetColliderTransform(door, new FVector(50 + i % 3 * 10, 0, 0), default); scene.Run(.05f);
            if (i == 30) clouds[0].Destroy();
            Assert.InRange(world.PendingGeometryCells, 0, world.Fields.Count * ResponsiveSmokeField.CellCount);
        }
        foreach (SSmokeEmitter cloud in clouds) cloud.Destroy();
        Assert.Empty(world.Fields); Assert.Equal(0, world.PendingGeometryCells);
    }

    [Fact]
    public void ABlockerOutsideTheCloudInvalidatesCachedBlastSightWhenItMoves()
    {
        CombatScene scene = Scene(); var builder = new CollisionDataBuilder();
        int door = builder.AddBoxHullCollider(ColliderKind.Mover, null,
            ColliderFlags.CollideActors | ColliderFlags.BlockActors | ColliderFlags.BlockNonZeroExtent | ColliderFlags.BlockZeroExtent,
            new FVector(-1, -500, -500), new FVector(1, 500, 500), new FVector(-320, 0, 0));
        CollisionWorld collision = CollisionWorld.Build(builder.Build()); scene.Level.SetService<ICollisionWorld>(collision);
        SSmokeEmitter cloud = Cloud(scene); scene.Run(2); ResponsiveSmokeField field = Field(scene, cloud);
        FVector target = new(-100, 0, 0), origin = new(-350, 0, 0);
        ResponsiveSmokeWorld.Blast(cloud, origin, 300);
        Assert.True(field.SampleDensity(target, scene.Time) > .9f);
        collision.SetColliderTransform(door, new FVector(-320, 1200, 0), default); scene.Run(.1f);
        ResponsiveSmokeWorld.Blast(cloud, origin, 300);
        Assert.Equal(0f, field.SampleDensity(target, scene.Time));
    }

    [Fact]
    public void ReplacingCollisionWorldRebuildsPreviouslyFreeCells()
    {
        CombatScene scene = Scene(); SSmokeEmitter cloud = Cloud(scene); scene.Run(2);
        ResponsiveSmokeField field = Field(scene, cloud); Assert.True(field.SampleDensity(new FVector(125, 0, 0), scene.Time) > .9f);
        GadgetKit.Walls(scene, (new FVector(49, -1000, -1000), new FVector(51, 1000, 1000), CollisionMaterialFlags.None));
        scene.Run(2);
        Assert.Equal(0f, field.SampleDensity(new FVector(125, 0, 0), scene.Time));
        Assert.True(field.SampleDensity(new FVector(-50, 0, 0), scene.Time) > .9f);
    }
}
