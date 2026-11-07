using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using OpenScct.Physics.Collision;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ResponsiveSmokeGrowthTests(ITestOutputHelper output)
{
    [Fact]
    public void ClassifiedAirReleasesQuicklyAndEasesIntoItsOuterBloom()
    {
        FVector source = new(12.5f, 12.5f, 12.5f);
        var field = new ResponsiveSmokeField(default, 0, source);
        Array.Fill(field.Cells, (byte)1);
        field.RebuildFront(0, static (_, _) => true);
        int budget = int.MaxValue; field.AdvanceFront(0, ref budget);
        FVector near = source + new FVector(50, 0, 0), far = source + new FVector(200, 0, 0);
        Assert.Equal((ushort)1, field.ActivationTicks[field.CellIndex(near)]);
        Assert.Equal((ushort)6, field.ActivationTicks[field.CellIndex(far)]);
        Assert.InRange(field.SampleDensity(near, .15f), .999f, 1f);
        Assert.Equal(0f, field.SampleDensity(far, .25f));
        Assert.True(field.SampleDensity(far, .40f) > .5f);
    }

    [Fact]
    public void IncrementalClassificationKeepsSubTickPathDistance()
    {
        FVector source = new(-237.5f, 12.5f, 12.5f);
        var field = new ResponsiveSmokeField(default, 0, source);
        int seed = field.CellIndex(source); field.Cells[seed] = 1;
        field.RebuildFront(0, static (_, _) => true);
        int budget = int.MaxValue; field.AdvanceFront(0, ref budget);
        // Reveal air over multiple simulation ticks, as the bounded geometry classifier does.
        // The 400uu path takes ceil((400/1200 + .25*(400/300)^2)*20)=16 ticks.
        // Incremental discovery must not restart this whole curve at each newly classified cell.
        for (int cell = 1; cell <= 16; cell++)
        {
            field.Cells[seed + cell] = 1; field.CellOpened(seed + cell);
            field.RebuildFront(cell / 60f, static (_, _) => true);
            budget = int.MaxValue; field.AdvanceFront(cell / 60f, ref budget);
        }
        Assert.Equal((ushort)16, field.ActivationTicks[seed + 16]);
        Assert.True(field.SampleDensity(field.CellCenter(seed + 16), .90f) > .5f);
    }

    [Fact]
    public void IncrementalClassificationReopensDiagonalsBetweenKnownCells()
    {
        FVector source = new(-237.5f, -237.5f, 12.5f);
        var field = new ResponsiveSmokeField(default, 0, source);
        int seed = field.CellIndex(source);
        for (int x = 0; x <= 8; x++) field.Cells[seed + x] = 1;
        int target = seed + 8 + ResponsiveSmokeField.Dimension;
        field.Cells[target] = 1;
        field.RebuildFront(0, static (_, _) => true);
        int budget = int.MaxValue; field.AdvanceFront(0, ref budget);
        Assert.Equal((ushort)7, field.ActivationTicks[target]); // 175uu prefix + 50uu around the blocked corner.

        int corner = seed + 7 + ResponsiveSmokeField.Dimension;
        field.Cells[corner] = 1; field.CellOpened(corner);
        field.RebuildFront(1f / 60f, static (_, _) => true);
        budget = int.MaxValue; field.AdvanceFront(1f / 60f, ref budget);
        Assert.Equal((ushort)6, field.ActivationTicks[target]); // 175uu prefix + 35.35uu newly opened diagonal.
    }

    [Fact]
    public void ClosingDoorStillCancelsFutureGrowthAcrossItsBlockedCell()
    {
        FVector source = new(-237.5f, 12.5f, 12.5f);
        var field = new ResponsiveSmokeField(default, 0, source);
        int seed = field.CellIndex(source);
        for (int i = 0; i <= 16; i++) field.Cells[seed + i] = 1;
        field.RebuildFront(0, static (_, _) => true);
        int budget = int.MaxValue; field.AdvanceFront(0, ref budget);
        Assert.Equal((ushort)16, field.ActivationTicks[seed + 16]);
        field.Cells[seed + 8] = 0; field.InvalidateFront();
        field.RebuildFront(.1f, static (_, _) => true);
        budget = int.MaxValue; field.AdvanceFront(.1f, ref budget);
        Assert.Equal(ResponsiveSmokeField.Unreached, field.ActivationTicks[seed + 16]);
        Assert.Equal(0f, field.SampleDensity(field.CellCenter(seed + 16), .3f));
        field.Cells[seed + 8] = 1; field.InvalidateFront();
        field.RebuildFront(.15f, static (_, _) => true);
        budget = int.MaxValue; field.AdvanceFront(.15f, ref budget);
        Assert.NotEqual(ResponsiveSmokeField.Unreached, field.ActivationTicks[seed + 16]);
        Assert.True(field.SampleDensity(field.CellCenter(seed + 16), 1.5f) > .5f);
    }

    [Fact]
    public void GroundedGrenadeBuildsNearAndFarStandingExposurePromptly()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        scene.Level.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = true }, default, default);
        var builder = new CollisionDataBuilder();
        builder.AddBox(new FVector(-1000, -1000, -50), new FVector(1000, 1000, 0));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        var grenade = scene.Level.SpawnActor(new SProjSmoke { QuickLaunch = true }, new FVector(0, 0, 2), default)!;
        grenade.Explode(grenade.Location, new FVector(0, 0, 1));
        SSmokeEmitter cloud = Assert.IsType<SSmokeEmitter>(grenade.Smoke);
        Assert.True(ResponsiveSmokeWorld.For(scene.Level).TryGet(cloud, out var field));
        FVector near = new(0, 0, 180), far = new(220, 0, 180);
        float nearGas = float.PositiveInfinity, farGas = float.PositiveInfinity;
        float nearHalf = float.PositiveInfinity, farHalf = float.PositiveInfinity;
        for (int i = 0; i < Sim.Hz; i++)
        {
            scene.World.Step();
            float age = scene.Time - field.CreatedAt;
            float a = ResponsiveSmokeWorld.BreathingDensity(field, near, scene.Time);
            float b = ResponsiveSmokeWorld.BreathingDensity(field, far, scene.Time);
            if (i + 1 is 6 or 12 or 24 or 36)
            {
                int scheduled = 0, active = 0;
                foreach (ushort tick in field.ActivationTicks)
                {
                    if (tick != ResponsiveSmokeField.Unreached) scheduled++;
                    if (tick <= field.AgeTick(scene.Time)) active++;
                }
                output.WriteLine($"age={age:F3}s: near={a:F3}, far={b:F3}, classified={field.ClassifyCursor}, scheduled={scheduled}, active={active}.");
            }
            if (a >= ResponsiveSmokeWorld.ExposureThreshold) nearGas = Math.Min(nearGas, age);
            if (b >= ResponsiveSmokeWorld.ExposureThreshold) farGas = Math.Min(farGas, age);
            if (a >= .5f) nearHalf = Math.Min(nearHalf, age);
            if (b >= .5f) farHalf = Math.Min(farHalf, age);
        }
        output.WriteLine($"Grounded source {field.SourcePosition}, standing eye z=180: near gas {nearGas:F3}s / half density {nearHalf:F3}s; far x=220 gas {farGas:F3}s / half density {farHalf:F3}s.");
        Assert.InRange(nearGas, .1f, .35f); Assert.InRange(farGas, .35f, .65f);
        Assert.InRange(nearHalf, .1f, .45f); Assert.InRange(farHalf, .4f, .75f);
        Assert.True(farGas > nearGas + .15f);
    }

    [Fact]
    public void SixColdGroundedCloudsShareTheBudgetAndReachStandingHeight()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        scene.Level.SpawnActor(new GameReplicationInfo { ResponsiveSmokeEnabled = true }, default, default);
        var builder = new CollisionDataBuilder();
        builder.AddBox(new FVector(-500, -500, -50), new FVector(2000, 500, 0));
        scene.Level.SetService<ICollisionWorld>(CollisionWorld.Build(builder.Build()));
        var fields = new ResponsiveSmokeField[6]; var prior = new ushort[6][];
        var world = ResponsiveSmokeWorld.For(scene.Level);
        for (int i = 0; i < fields.Length; i++)
        {
            var cloud = scene.Level.SpawnActor(new SSmokeEmitter(), new FVector(i * 250, 0, 5), default)!;
            Assert.True(world.TryGet(cloud, out fields[i]));
            prior[i] = new ushort[ResponsiveSmokeField.CellCount]; Array.Fill(prior[i], ResponsiveSmokeField.Unreached);
        }
        float maximumLateness = 0; int fullyPublished = 0;
        for (int frame = 0; frame < 45; frame++)
        {
            scene.World.Step();
            for (int cloud = 0; cloud < fields.Length; cloud++)
            {
                ResponsiveSmokeField field = fields[cloud]; float age = scene.Time - field.CreatedAt;
                for (int cell = 0; cell < field.ActivationTicks.Length; cell++)
                {
                    ushort tick = field.ActivationTicks[cell];
                    if (prior[cloud][cell] == ResponsiveSmokeField.Unreached && tick != ResponsiveSmokeField.Unreached &&
                        ResponsiveSmokeField.Envelope(field.CellCenter(cell) - field.Center, age) > .5f)
                    {
                        float late = age - tick / ResponsiveSmokeField.TickRate;
                        maximumLateness = Math.Max(maximumLateness, late);
                        if (late >= ResponsiveSmokeField.GrowthFade) fullyPublished++;
                    }
                    prior[cloud][cell] = tick;
                }
            }
        }
        foreach (ResponsiveSmokeField field in fields)
            Assert.True(ResponsiveSmokeWorld.BreathingDensity(field, field.SourcePosition + new FVector(0, 0, 175), scene.Time) > .9f);
        output.WriteLine($"Six simultaneous cold grounded clouds: newly published body cells max arrival lateness={maximumLateness:F3}s, cells past complete fade={fullyPublished}, shared geometry queries={world.GeometryQueries}.");
        Assert.InRange(maximumLateness, 0, 1f / ResponsiveSmokeField.TickRate + .0001f);
        Assert.Equal(0, fullyPublished);
    }
}
