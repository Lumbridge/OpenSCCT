using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenScct.Content.Actors;
using OpenScct.Content.Json;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;
using UcAssetRef = OpenScct.Core.Uc.AssetRef;

namespace OpenScct.Content.Tests;

/// <summary>The map loader on SVMap 1.1 data: class tables, references of every kind, and the load-time base rules.</summary>
public sealed class MapLoaderClassTableTests
{
    /// <summary>Counts the events a load must not raise (stored Base and Owner) or must raise (AttachTag).</summary>
    private sealed class Probe : Actor
    {
        public int Attaches;
        public int BaseChanges;
        public int Gained;

        protected override void Attach(Actor Other) => Attaches++;

        protected override void BaseChange() => BaseChanges++;

        protected override void GainedChild(Actor Other) => Gained++;
    }

    private static string Doc(string actors) => """
        {
          "formatVersion": "1.1.0", "map": "Synth", "mapSettings": {}, "tagIndex": {}, "eventEdges": [], "missionTree": [],
          "levelInfo": {"id": "LevelInfo0", "class": "Engine.LevelInfo", "props": {"GEActivationMask": 0, "Level": {"$actor": "LevelInfo0"}}},
          "actors": [
        """ + actors + "]}";

    private static GameplayDoc Parse(string actors) => GameplayDoc.Parse(Encoding.UTF8.GetBytes(Doc(actors))).WithDerived();

    private static ClassTable Table(params UcClassInfo[] classes) => ClassTableBuilder.Build(classes);

    /// <summary>A table of <c>Core.Object</c>, <c>Engine.Actor</c> and the given classes (all direct Actor subclasses).</summary>
    private static ClassTable TableOf(params (string Path, PropBag Defaults)[] classes)
    {
        var sb = new StringBuilder("{\"format\": \"svm.classes/1\", \"classes\": {\"Core.Object\": {\"super\": null}, \"Engine.Actor\": {\"super\": \"Core.Object\"}");
        foreach ((string path, PropBag defaults) in classes)
        {
            sb.Append($", \"{path}\": {{\"super\": \"Engine.Actor\", \"defaults\": {defaults}}}");
        }

        return ClassTable.Parse(Encoding.UTF8.GetBytes(sb.Append("}}").ToString()));
    }

    /// <summary><paramref name="bag"/> without the <paramref name="remove"/> names and with the <paramref name="set"/> values.</summary>
    private static PropBag Edit(PropBag bag, string[] remove, params (string Name, PropValue Value)[] set)
    {
        var items = bag.Where(p => !remove.Contains(p.Key) && !set.Any(s => s.Name == p.Key)).ToList();
        items.AddRange(set.Select(s => new KeyValuePair<string, PropValue>(s.Name, s.Value)));
        return new PropBag(items);
    }

    private static PropBag PlayerStartDefaults => ClassTableBuilder.Defaults(PlayerStart.UcMeta.Class)!;

    [Fact]
    public void EveryGeneratedClassRoundTripsThroughAClassTable()
    {
        // A table written from the generated defaults must change nothing when the loader applies it: every member
        // type converts to prop JSON and back to the same value.
        UcClassInfo[] classes = [.. CorePackage.Info.Classes, .. EnginePackage.Info.Classes, .. SBasePackage.Info.Classes];
        UcClassInfo[] placeable = classes.Where(c => c is { IsActor: true, IsAbstract: false, Factory: not null } && c.Path != "Engine.LevelInfo").ToArray();
        ClassTable table = Table(classes.Where(c => c is { IsAbstract: false, Factory: not null }).ToArray());
        ClassTable parsed = ClassTable.Parse(CanonicalJson.ToFileBytes(table.ToJson()));
        Assert.Equal(CanonicalJson.ToCanonicalBytes(table.ToJson()), CanonicalJson.ToCanonicalBytes(parsed.ToJson()));

        var sb = new StringBuilder();
        for (int i = 0; i < placeable.Length; i++)
        {
            sb.Append(i == 0 ? "" : ", ").Append("{\"id\": \"A").Append(i).Append("\", \"class\": \"").Append(placeable[i].Path).Append("\", \"props\": {}}");
        }

        GameplayDoc doc = Parse(sb.ToString());
        var level = new World(1).Level;
        level.BeginLoad(); // caller-owned and never ended: no begin-play code runs
        MapLoadResult result = new MapLoader(AllMapsTests.GameRegistry()).Load(level, doc, parsed);
        Assert.Equal(placeable.Length, result.KnownCount);
        foreach (MapClassReport c in result.Classes)
        {
            Assert.True(c.DefaultsApplied.Count == 0, $"{c.ClassPath}: {string.Join(", ", c.DefaultsApplied)}");
            Assert.True(c.DefaultsUnapplied.Count == 0, $"{c.ClassPath}: {string.Join(", ", c.DefaultsUnapplied)}");
        }

        Assert.True(table.DefaultsOf("SBase.SAmmoStock")!.GetBool("bShouldBaseAtStartup"));
        Assert.Null(table.Find("Engine.Actor")!.Defaults);
        Assert.Equal("Core.Object", table.Find("Engine.Actor")!.Super);
    }

    [Fact]
    public void TableValuesThatDifferApplyAndMissingOnesReset()
    {
        Assert.True(Table(PlayerStart.UcMeta.Class).DefaultsOf("Engine.PlayerStart")!.GetBool("bEnabled"));
        Assert.Equal(40f, PlayerStartDefaults.GetFloat("CollisionRadius"));
        ClassTable table = TableOf(("Engine.PlayerStart",
            Edit(PlayerStartDefaults, ["bEnabled"], ("CollisionRadius", PropValue.FromFloat(99f)), ("NoSuchProp", PropValue.FromInt(3)))));
        GameplayDoc doc = Parse("""
            {"id": "PlayerStart0", "class": "Engine.PlayerStart", "props": {"Tag": "PlayerStart", "TeamNumber": 1}},
            {"id": "PlayerStart1", "class": "Engine.PlayerStart", "props": {"Tag": "PlayerStart", "CollisionRadius": 12.0}}
            """);
        MapLoadResult result = new MapLoader().Load(new World(1).Level, doc, table);
        var a = (PlayerStart)result.FindActor("PlayerStart0")!;
        var b = (PlayerStart)result.FindActor("PlayerStart1")!;
        Assert.Equal((99f, 1, false, true), (a.CollisionRadius, (int)a.TeamNumber, a.bEnabled, a.bCoopStart));
        Assert.Equal(12f, b.CollisionRadius);
        MapClassReport report = Assert.Single(result.Classes, c => c.ClassPath == "Engine.PlayerStart");
        Assert.Equal(["CollisionRadius", "bEnabled"], report.DefaultsApplied.Order(StringComparer.Ordinal));
        Assert.Equal(["NoSuchProp"], report.DefaultsUnapplied);

        // Switched off, the generated defaults stand.
        MapLoadResult plain = new MapLoader { ApplyClassDefaults = false }.Load(new World(1).Level, doc, table);
        Assert.Equal((40f, true), (((PlayerStart)plain.FindActor("PlayerStart0")!).CollisionRadius, ((PlayerStart)plain.FindActor("PlayerStart0")!).bEnabled));
    }

    [Fact]
    public void MarkersTakeTheActorFieldsOfTheirClass()
    {
        // SBase.SFlag has no C# class in the default registry: its marker gets the Actor fields of the class table.
        ClassTable table = TableOf(("SBase.SFlag",
            Edit(PlayerStartDefaults, [], ("ResetTime", PropValue.FromInt(30)), ("bCollideActors", PropValue.FromBool(true)))));
        GameplayDoc doc = Parse("""{"id": "SFlag0", "class": "SBase.SFlag", "props": {"Tag": "SFlag", "ResetTime": 15, "CollisionHeight": 30.0}}""");
        MapLoadResult result = new MapLoader().Load(new World(1).Level, doc, table);
        var flag = (UnportedActor)result.FindActor("SFlag0")!;
        Assert.Equal((40f, 30f, true, true), (flag.CollisionRadius, flag.CollisionHeight, flag.bCollideActors, flag.bHidden));
        Assert.Same(table.DefaultsOf("SBase.SFlag"), flag.ClassDefaults);
        Assert.Equal(15, flag.GetEffective("ResetTime")!.Value.AsInt());
        Assert.Equal(40f, flag.GetEffective("CollisionRadius")!.Value.AsFloat());
        Assert.Equal(15, table.Effective(result.Gameplay.FindActor("SFlag0")!, "ResetTime")!.Value.AsInt());
        Assert.Equal(30, table.DefaultsOf("SBase.SFlag")!.GetInt("ResetTime"));
        MapClassReport report = Assert.Single(result.Classes, c => c.ClassPath == "SBase.SFlag");
        Assert.Equal(MapClassKind.Marker, report.Kind);
        Assert.Contains("ResetTime", report.DefaultsUnapplied);
        Assert.Contains("bEnabled", report.DefaultsUnapplied);
        Assert.Contains(result.Unapplied, p => p is { Prop: "ResetTime", Reason: UnappliedReason.NoMember });
    }

    [Fact]
    public void ReferencesOfEveryKindConvert()
    {
        GameplayDoc doc = Parse("""
            {"id": "Zone0", "class": "Engine.ZoneInfo", "props": {"Tag": "ZoneInfo"}},
            {"id": "Start0", "class": "Engine.PlayerStart", "props": {"Tag": "PlayerStart", "Level": {"$actor": "LevelInfo0"},
              "Region": {"Zone": {"$actor": "Zone0"}, "ZoneNumber": 2, "iLeaf": 5},
              "StaticMesh": {"$asset": "Synth.Meshes.Box"}, "Texture": null, "MessageClass": {"$class": "Engine.LocalMessage"},
              "IgnoredEchelonLights": [{"$asset": "Synth.Light3"}, {"$actor": "Zone0"}, null],
              "StaticMeshInstance": {"$asset": "Synth.SMI"}, "nextNavigationPoint": {"$actor": "Start1"}}},
            {"id": "Start1", "class": "Engine.PlayerStart", "props": {"Tag": "PlayerStart", "Level": {"$actor": "Zone0"},
              "nextNavigationPoint": {"$actor": "Zone0"}, "MessageClass": null}}
            """);
        MapLoadResult result = new MapLoader().Load(new World(1).Level, doc, null);
        var zone = (ZoneInfo)result.FindActor("Zone0")!;
        var start = (PlayerStart)result.FindActor("Start0")!;
        var start1 = (PlayerStart)result.FindActor("Start1")!;
        Assert.Same(zone, start.Region.Zone);
        Assert.Equal((2, 5), (start.Region.ZoneNumber, start.Region.iLeaf));
        Assert.Equal(new UcAssetRef("synth.meshes.box"), start.StaticMesh);
        Assert.True(start.Texture.IsNone);
        Assert.Equal(new ClassRef("Engine.LocalMessage"), start.MessageClass);
        Assert.True(start1.MessageClass.IsNone);
        Assert.Equal([null, zone, null], start.IgnoredEchelonLights);
        Assert.Same(start1, start.nextNavigationPoint);
        Assert.Null(start1.nextNavigationPoint);
        Assert.Null(start.StaticMeshInstance);

        UnappliedProp[] expected =
        [
            new("Engine.PlayerStart", "IgnoredEchelonLights", UnappliedReason.RenderOnlyActor, 1),
            new("Engine.PlayerStart", "Level", UnappliedReason.WrongActorClass, 1),
            new("Engine.PlayerStart", "StaticMeshInstance", UnappliedReason.ObjectReference, 1),
            new("Engine.PlayerStart", "nextNavigationPoint", UnappliedReason.WrongActorClass, 1),
        ];
        Assert.Equal(expected, result.Unapplied);
    }

    [Fact]
    public void StoredBaseAndOwnerLoadWithoutEvents()
    {
        ActorClassRegistry registry = ActorClassRegistry.CreateDefault();
        registry.Register<Probe>("SBase.SProbe");
        GameplayDoc doc = Parse("""
            {"id": "Lift0", "class": "SBase.SProbe", "props": {"Tag": "Lift"}},
            {"id": "Rider0", "class": "SBase.SProbe", "props": {"Tag": "Rider", "Base": {"$actor": "Lift0"}, "Owner": {"$actor": "Lift0"}}},
            {"id": "Loop0", "class": "SBase.SProbe", "props": {"Tag": "Loop", "Base": {"$actor": "Loop0"}}},
            {"id": "Deco0", "class": "SBase.SProbe", "props": {"Tag": "Deco", "Base": {"$asset": "Synth.StaticMeshActor9"}}}
            """);
        MapLoadResult result = new MapLoader(registry).Load(new World(1).Level, doc, null);
        var lift = (Probe)result.FindActor("Lift0")!;
        var rider = (Probe)result.FindActor("Rider0")!;
        Assert.Same(lift, rider.Base);
        Assert.Same(lift, rider.Owner);
        Assert.Equal([rider], lift.Attached);
        Assert.Equal((0, 0, 0), (lift.Attaches, lift.Gained, rider.BaseChanges));
        Assert.Null(result.FindActor("Loop0")!.Base);
        Assert.Null(result.FindActor("Deco0")!.Base);
        Assert.Contains(result.Unapplied, p => p is { Prop: "Base", Reason: UnappliedReason.TypeMismatch, Count: 1 });
        Assert.Contains(result.Unapplied, p => p is { Prop: "Base", Reason: UnappliedReason.RenderOnlyActor, Count: 1 });
        Assert.DoesNotContain(result.Unapplied, p => p.Prop == "Owner");
    }

    [Fact]
    public void AttachTagBasesFollowTheLoadTimeRule()
    {
        ActorClassRegistry registry = ActorClassRegistry.CreateDefault();
        registry.Register<Probe>("SBase.SProbe");
        GameplayDoc doc = Parse("""
            {"id": "Glass0", "class": "SBase.SProbe", "props": {"Tag": "Glass", "AttachTag": "liftdoor"}},
            {"id": "Mover541", "class": "SBase.SProbe", "props": {"Tag": "LiftDoor"}},
            {"id": "Light0", "class": "SBase.SProbe", "props": {"Tag": "Light", "AttachTag": "MOVER541", "bHardAttach": true}},
            {"id": "Self0", "class": "SBase.SProbe", "props": {"Tag": "Self", "AttachTag": "Self"}},
            {"id": "Lost0", "class": "SBase.SProbe", "props": {"Tag": "Lost", "AttachTag": "Nothing"}},
            {"id": "Second0", "class": "SBase.SProbe", "props": {"Tag": "LiftDoor"}}
            """);
        MapLoadResult result = new MapLoader(registry).Load(new World(1).Level, doc, null);
        var mover = (Probe)result.FindActor("Mover541")!;
        Assert.Same(mover, result.FindActor("Glass0")!.Base);
        Assert.Same(mover, result.FindActor("Light0")!.Base);
        Assert.Null(result.FindActor("Self0")!.Base);
        Assert.Null(result.FindActor("Lost0")!.Base);
        Assert.Equal(2, mover.Attaches); // Attach reaches the base, no BaseChange for the based actor
        Assert.Equal(0, ((Probe)result.FindActor("Glass0")!).BaseChanges);
        Assert.Equal(["Glass0", "Light0", "Self0", "Lost0"], result.AttachTagBases.Select(b => result.RecordOf(b.Actor)!.Id));
        Assert.Equal([true, true, true, false], result.AttachTagBases.Select(b => b.Base is not null));

        // Caller-owned loads attach once the caller asks, after its EndLoad.
        var level = new World(1).Level;
        level.BeginLoad();
        MapLoadResult owned = new MapLoader(registry).Load(level, doc, null);
        Assert.Null(owned.FindActor("Glass0")!.Base);
        level.EndLoad();
        owned.AttachStartupBases();
        owned.AttachStartupBases();
        Assert.Same(owned.FindActor("Mover541"), owned.FindActor("Glass0")!.Base);
        Assert.Equal(2, ((Probe)owned.FindActor("Mover541")!).Attaches);
    }

    [Fact]
    public void StartupBaseCandidatesNeedTheUe2Flags()
    {
        GameplayDoc doc = Parse("""
            {"id": "Floor0", "class": "Engine.PlayerStart", "props": {"Tag": "Floor"}},
            {"id": "Ammo0", "class": "Engine.PlayerStart", "props": {"Tag": "Ammo", "bShouldBaseAtStartup": true, "bCollideWorld": true}},
            {"id": "Fall0", "class": "Engine.PlayerStart", "props": {"Tag": "Fall", "bShouldBaseAtStartup": true, "bCollideWorld": true, "Physics": 2}},
            {"id": "Spin0", "class": "Engine.PlayerStart", "props": {"Tag": "Spin", "bShouldBaseAtStartup": true, "bCollideWorld": true, "Physics": 3}},
            {"id": "NoWorld0", "class": "Engine.PlayerStart", "props": {"Tag": "NoWorld", "bShouldBaseAtStartup": true}}
            """);
        MapLoadResult result = new MapLoader().Load(new World(1).Level, doc, null);
        Assert.Equal(["Ammo0", "Spin0"], result.StartupBaseCandidates().Select(a => result.RecordOf(a)!.Id));
        Actor floor = result.FindActor("Floor0")!;
        Assert.Equal(2, result.FindStartupBases(_ => floor));
        Assert.Same(floor, result.FindActor("Ammo0")!.Base);
        Assert.Null(result.FindActor("Fall0")!.Base);
    }

    [Fact]
    public void ClassTablesAreReadAndChecked()
    {
        ClassTable table = ClassTable.Parse("""
            {"format": "svm.classes/1", "classes": {
              "Core.Object": {"super": null},
              "Engine.Actor": {"super": "Core.Object"},
              "SBase.SThing": {"super": "Engine.Actor", "defaults": {"DrawScale3D": {"X": 1.0, "Y": 1.0, "Z": 1.0}, "KeyPos": {"$elements": {"0": {"X": 1.0}, "2": {"X": 2.0}}}, "Names": ["a"]}},
              "SBase.SOrphan": {"super": "SBase.SMissing", "defaults": {}}
            }}
            """u8);
        Assert.Equal(["SBase.SThing", "Engine.Actor", "Core.Object"], table.Chain("SBase.SThing").Select(c => c.Path));
        Assert.True(table.IsA("SBase.SThing", "engine.actor"));
        Assert.False(table.IsA("SBase.SThing", "SBase.SOrphan"));

        PropValue merged = ClassTable.Overlay(table.DefaultsOf("SBase.SThing")!["DrawScale3D"], PropValue.FromStruct(new PropBag([new("Y", PropValue.FromFloat(4f))])));
        Assert.Equal(new FVector(1f, 4f, 1f), merged.AsVector());
        PropValue elements = ClassTable.Overlay(table.DefaultsOf("SBase.SThing")!["KeyPos"],
            PropValue.FromStaticArray(new PropStaticArray([new(2, PropValue.FromStruct(new PropBag([new("Y", PropValue.FromFloat(5f))]))), new(3, PropValue.FromInt(1))])));
        Assert.Equal([0, 2, 3], elements.AsStaticArray().Select(e => e.Key));
        Assert.Equal(new FVector(0f, 5f, 0f), elements.AsStaticArray().Get(2).AsVector()); // an element is replaced whole
        Assert.Empty(ClassTable.Overlay(table.DefaultsOf("SBase.SThing")!["Names"], PropValue.FromArray([])).AsArray());

        GameplayDoc doc = Parse("""
            {"id": "Thing0", "class": "SBase.SThing", "props": {"Tag": "Thing", "DrawScale3D": {"Z": 2.0}}},
            {"id": "Other0", "class": "SBase.SOther", "props": {"Tag": "Other"}}
            """);
        Assert.Equal(new FVector(1f, 1f, 2f), table.EffectiveProps(doc.FindActor("Thing0")!).GetVector("DrawScale3D"));
        Assert.Equal(
            [
                "sim/classes.json: no defaults for Engine.LevelInfo (LevelInfo0)",
                "sim/classes.json: no defaults for SBase.SOther (Other0)",
                "sim/classes.json: SBase.SOrphan names super class SBase.SMissing, which is missing",
            ],
            table.Check(doc));

        var e = Assert.Throws<SvMapFormatException>(() => ClassTable.Parse("""{"format": "svm.classes/2", "classes": {}}"""u8));
        Assert.Contains("svm.classes/1", e.Message, StringComparison.Ordinal);
        e = Assert.Throws<SvMapFormatException>(() => ClassTable.Parse("""{"format": "svm.classes/1", "classes": {"A.B": {"super": null}, "a.b": {"super": null}}}"""u8));
        Assert.Contains("listed twice", e.Message, StringComparison.Ordinal);
    }
}
