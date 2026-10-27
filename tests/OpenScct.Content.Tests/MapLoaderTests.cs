using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Content.Actors;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

// The stand-in ports below are filled by the map loader through reflection.
#pragma warning disable CS0649

namespace OpenScct.Content.Tests;

public sealed class MapLoaderTests
{
    /// <summary>Stand-in ports with UnrealScript field names, to exercise property application.</summary>
    private sealed class TestMover : Actor
    {
        public readonly FVector[] KeyPos = new FVector[24];
        public FVector BasePos;
        public float MoveTime;
        public float StayOpenTime;
        public byte NumKeys;
        public new byte DrawType;
        public new string StaticMesh = string.Empty;
        public int Triggered;

        protected override void Trigger(Actor? Other, Pawn? EventInstigator) => Triggered++;
    }

    private sealed class TestFlag : Actor
    {
        public Actor? DropZone;
        public int ResetTime;
        public float fInteractionRadius;
        public string TriggerName = string.Empty;
    }

    private sealed class TestObjective : Actor
    {
        public List<Actor> Triggers = new();
        public string ObjectiveName = string.Empty;
    }

    private sealed class TestMission : Actor
    {
        public Actor[] Objectives = [];
        public byte GameMode;
        public int MinimumObjectives;
        public bool bChained = true;
    }

    private static List<T> Collect<T>(ActorIterator<T> actors)
        where T : Actor
    {
        var list = new List<T>();
        foreach (T a in actors)
        {
            list.Add(a);
        }

        return list;
    }

    private static GameplayDoc Gameplay() => GameplayDoc.Parse(TestPaths.TestBoxFile("sim/gameplay.json"));

    [Fact]
    public void DefaultRegistryCreatesGeneratedClassesAndCountsTheRest()
    {
        var world = new World(1);
        MapLoadResult result = new MapLoader().Load(world.Level, Gameplay());

        Assert.Equal(21, result.Actors.Count);
        Assert.Equal(11, result.KnownCount);
        Assert.Equal(9, result.UnknownCount);
        Assert.Equal(0, result.FailedSpawns);
        Assert.Equal(
            [
                new("SBase.SComputerObjectiveTrigger", 2), new("SBase.SFlag", 1), new("SBase.SFlagDropZone", 1),
                new("SBase.SGETrigger", 1), new("SBase.SMission", 1), new("SBase.SObjective", 3),
            ],
            result.UnknownClasses.ToList());
        Assert.IsType<BlockingVolume>(result.FindActor("BlockingVolume0"));
        Assert.IsType<Mover>(result.FindActor("Mover0"));
        Assert.IsType<Trigger>(result.FindActor("Trigger0"));
        MapClassReport starts = Assert.Single(result.Classes, c => c.ClassPath == "Engine.PlayerStart");
        Assert.Equal((4, typeof(PlayerStart), MapClassKind.Generated, false), (starts.Count, starts.CSharpType, starts.Kind, starts.HasPortedCode));
        Assert.True(Assert.Single(result.Classes, c => c.ClassPath == "Engine.Mover").HasPortedCode);
        Assert.Equal(MapClassKind.LevelInfo, Assert.Single(result.Classes, c => c.ClassPath == "Engine.LevelInfo").Kind);
        Assert.Equal(21, world.Level.ActorCount);
        Assert.True(world.Level.Info.bBegunPlay);
        Assert.False(world.Level.IsLoading);
    }

    [Fact]
    public void LevelInfoZonesAndPlayerStartsTakeTheirProperties()
    {
        var world = new World(1);
        MapLoadResult result = new MapLoader().Load(world.Level, Gameplay());

        LevelInfo info = world.Level.Info;
        Assert.Same(info, result.Actors[0]);
        Assert.Equal("TestBox", info.Title);
        Assert.Equal("SVM", info.Author);
        Assert.Equal(4, info.GEActivationMask);
        Assert.Same(result.Gameplay.LevelInfo, result.RecordOf(info));

        List<ZoneInfo> zones = Collect(world.Level.AllActors<ZoneInfo>());
        Assert.Equal(4, zones.Count); // LevelInfo is a ZoneInfo too
        var zone1 = (ZoneInfo)result.FindActor("ZoneInfo1")!;
        Assert.Equal("Room B", zone1.LocationName);
        Assert.Equal(new FVector(2048f, 512f, 256f), zone1.Location);

        List<PlayerStart> starts = Collect(world.Level.AllActors<PlayerStart>(new Name("PlayerStart")));
        Assert.Equal(4, starts.Count);
        Assert.Equal([0, 0, 1, 1], starts.Select(s => (int)s.TeamNumber));
        Assert.Equal(new FVector(1792f, 256f, 100f), starts[2].Location);
        Assert.Equal(new FRotator(0, 32768, 0), starts[2].Rotation);
        Assert.Equal("PlayerStart2", result.RecordOf(starts[2])!.Id);

        // Spawn order is level order, so actor ids follow the records.
        for (int i = 1; i < result.Actors.Count; i++)
        {
            Assert.True(result.Actors[i]!.ActorId > result.Actors[i - 1]!.ActorId);
        }
    }

    [Fact]
    public void UnportedActorsKeepTheirPlaceTagAndRecord()
    {
        var world = new World(1);
        MapLoadResult result = new MapLoader().Load(world.Level, Gameplay());

        var flag = (UnportedActor)result.FindActor("SFlag0")!;
        Assert.Equal("SBase.SFlag", flag.ClassPath);
        Assert.Equal("SFlag0", flag.Record!.Id);
        Assert.Equal(new Name("SFlag"), flag.Tag);
        Assert.True(flag.bCollideActors);
        Assert.Equal(50f, flag.CollisionRadius);
        Assert.Equal(30f, flag.CollisionHeight);
        Assert.Equal(new FVector(128f, -1408f, 32f), flag.Location);
        Assert.Null(flag.ClassDefaults); // TestBox is SVMap 1.0, without a class table
        Assert.Equal(15, flag.GetEffective("ResetTime")!.Value.AsInt());
        Assert.Null(flag.GetEffective("bNoSuchProp"));

        var trigger = (Trigger)result.FindActor("Trigger0")!;
        Assert.Equal(new Name("Trigger"), trigger.Tag);
        Assert.Equal(new Name("TestDoor"), trigger.Event);
        Assert.Equal(160f, trigger.CollisionRadius);

        Assert.Single(Collect(world.Level.AllActors<Actor>(new Name("TestDoor"))));
        Assert.Contains(result.Unapplied, p => p is { ClassPath: "SBase.SFlag", Prop: "DropZone", Reason: UnappliedReason.NoMember, Count: 1 });
        Assert.Contains(result.UnappliedProps, kv => kv.Key == "SBase.SObjective.Triggers" && kv.Value == 3);
        Assert.DoesNotContain(result.UnappliedProps, kv => kv.Key.StartsWith("Engine.Mover.", StringComparison.Ordinal));
        Assert.DoesNotContain(result.UnappliedProps, kv => kv.Key.EndsWith(".Location", StringComparison.Ordinal));
        Assert.DoesNotContain(result.UnappliedProps, kv => kv.Key.EndsWith(".Tag", StringComparison.Ordinal));
    }

    [Fact]
    public void RegisteredPortsTakeValuesStaticArraysAndActorReferences()
    {
        ActorClassRegistry registry = ActorClassRegistry.CreateDefault();
        registry.Register<TestMover>("Engine.Mover");
        registry.Register<TestFlag>("sbase.sflag");
        registry.Register<TestObjective>("SBase.SObjective");
        registry.Register<TestMission>("SBase.SMission");
        var world = new World(1);
        MapLoadResult result = new MapLoader(registry).Load(world.Level, Gameplay());
        Assert.Equal(16, result.KnownCount);
        Assert.Equal(4, result.UnknownCount);
        Assert.Equal(MapClassKind.Registered, Assert.Single(result.Classes, c => c.ClassPath == "Engine.Mover").Kind);

        var mover = (TestMover)result.FindActor("Mover0")!;
        Assert.Equal(new FVector(0f, 0f, 240f), mover.KeyPos[1]);
        Assert.Equal(default, mover.KeyPos[2]);
        Assert.Equal(new FVector(1280f, 512f, 0f), mover.BasePos);
        Assert.Equal(1f, mover.MoveTime);
        Assert.Equal(4f, mover.StayOpenTime);
        Assert.Equal(2, mover.NumKeys);
        Assert.Equal(8, mover.DrawType);
        Assert.Equal("TestBox.Meshes.SlidingDoor", mover.StaticMesh);
        Assert.Equal(new Name("TriggerOpenTimed"), mover.InitialState);
        Assert.True(mover.bBlockActors);

        var flag = (TestFlag)result.FindActor("SFlag0")!;
        Assert.Same(result.FindActor("SFlagDropZone0"), flag.DropZone);
        Assert.IsType<UnportedActor>(flag.DropZone);
        Assert.Equal(15, flag.ResetTime);
        Assert.Equal(50f, flag.fInteractionRadius);
        Assert.Equal("Test Files", flag.TriggerName);

        var mission = (TestMission)result.FindActor("SMission0")!;
        Assert.Equal(3, mission.Objectives.Length);
        Assert.All(mission.Objectives, o => Assert.IsType<TestObjective>(o));
        Assert.Equal(5, mission.GameMode);
        Assert.Equal(2, mission.MinimumObjectives);
        Assert.False(mission.bChained);
        var disk = (TestObjective)mission.Objectives[2];
        Assert.Equal("Test Files", disk.ObjectiveName);
        Assert.Same(flag, Assert.Single(disk.Triggers));
        Assert.Same(flag, result.Resolve(result.Gameplay.FindActor("SObjective2")!.Props.GetArray("Triggers")[0].AsActor()));

        Assert.DoesNotContain(result.UnappliedProps, kv => kv.Key.StartsWith("Engine.Mover.KeyPos", StringComparison.Ordinal));
        Assert.DoesNotContain(result.UnappliedProps, kv => kv.Key == "SBase.SFlag.DropZone");

        // Map events reach ported receivers through the level's tag index.
        world.Level.TriggerEvent(new Name("testdoor"), null, null);
        Assert.Equal(1, mover.Triggered);
    }

    [Fact]
    public void ReferencesThatDoNotFitTheFieldAreReported()
    {
        ActorClassRegistry registry = ActorClassRegistry.CreateDefault();
        registry.Register<TestFlag>("SBase.SFlag");
        registry.Register<ZoneInfo>("SBase.SFlagDropZone");
        registry.Register<TestObjective>("SBase.SObjective");
        MapLoadResult result = new MapLoader(registry).Load(new World(1).Level, Gameplay());
        Assert.IsType<ZoneInfo>(((TestFlag)result.FindActor("SFlag0")!).DropZone);

        var typed = new ActorClassRegistry();
        typed.Register<StrictFlag>("SBase.SFlag");
        MapLoadResult strict = new MapLoader(typed).Load(new World(1).Level, Gameplay());
        Assert.Null(((StrictFlag)strict.FindActor("SFlag0")!).DropZone);
        Assert.Contains(strict.UnappliedProps, kv => kv.Key == "SBase.SFlag.DropZone");
    }

    private sealed class StrictFlag : Actor
    {
        public PlayerStart? DropZone;
    }

    [Fact]
    public void CallerOwnedLoadsStayOpenUntilTheCallerEndsThem()
    {
        var world = new World(1);
        world.Level.BeginLoad();
        MapLoadResult result = new MapLoader().Load(world.Level, Gameplay());
        Assert.True(world.Level.IsLoading);
        Assert.False(world.Level.Info.bBegunPlay);
        world.Level.EndLoad();
        Assert.True(world.Level.Info.bBegunPlay);
        Assert.Equal(21, result.Actors.Count(a => a is not null));
    }

    [Fact]
    public void LoadedLevelsTick()
    {
        var world = new World(1);
        new MapLoader().Load(world, SvMap.Load(TestPaths.TestBox));
        world.Step(120);
        Assert.Equal(120, world.TickCount);
        Assert.Equal(21, world.Level.ActorCount);
        Assert.Throws<InvalidOperationException>(() => new MapLoader().Load(world.Level, Gameplay()));
    }
}
