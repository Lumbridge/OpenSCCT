using System;
using System.Collections.Generic;
using System.Text;
using OpenScct.Content.Json;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Content.Tests;

public sealed class GameplayDocTests
{
    private static GameplayDoc TestBox() => GameplayDoc.Parse(TestPaths.TestBoxFile("sim/gameplay.json"));

    [Fact]
    public void TestBoxCounts()
    {
        GameplayDoc doc = TestBox();
        Assert.Equal("1.0.0", doc.FormatVersion);
        Assert.Equal("TestBox", doc.Map);
        Assert.Equal(20, doc.Actors.Count);
        Assert.Equal(21, doc.AllActors.Count);
        Assert.Same(doc.LevelInfo, doc.AllActors[0]);
        Assert.Equal(4, doc.GEActivationMask);
        Assert.Equal(12, doc.TagIndex.Count);
        Assert.Equal(2, doc.EventEdges.Count);
        Assert.Single(doc.MissionTree);
        Assert.Equal(["TestBox.Meshes.SlidingDoor"], doc.AssetPaths);
        for (int i = 0; i < doc.AllActors.Count; i++)
        {
            Assert.Equal(i, doc.AllActors[i].Index);
        }
    }

    [Fact]
    public void LevelInfoAndMapSettings()
    {
        GameplayDoc doc = TestBox();
        ActorRecord info = doc.LevelInfo;
        Assert.True(info.IsLevelInfo);
        Assert.Equal("LevelInfo0", info.Id);
        Assert.Equal("Engine.LevelInfo", info.Class);
        Assert.Equal("Engine", info.ClassPackage);
        Assert.Equal("LevelInfo", info.ClassName);
        Assert.Equal("TestBox", info.Props.GetString("Title"));
        Assert.Equal(4, info.Props.GetInt("RecommendedNumPlayers"));

        PropBag story = doc.GetModeSettings("story")!;
        Assert.NotNull(story);
        Assert.Equal(10, story.GetInt("TimeLimit"));
        Assert.Equal(4, story.GetInt("SpiesLives"));
        Assert.Equal(3, story.GetInt("SentriesLives"));
        Assert.Equal(10, story.GetInt("RespawnTime"));
        Assert.Null(doc.GetModeSettings("DiskHunt"));
    }

    [Fact]
    public void TypedPropertiesAndLazyReferences()
    {
        GameplayDoc doc = TestBox();
        ActorRecord mover = doc.FindActor("mover0")!;
        Assert.Equal("Mover0", mover.Id);
        Assert.Equal(new FVector(1280f, 512f, 0f), mover.Location);
        Assert.Equal(1f, mover.Props.GetFloat("MoveTime"));
        Assert.Equal(4f, mover.Props.GetFloat("STAYOPENTIME"));
        Assert.Equal(2, mover.Props.GetByte("NumKeys"));
        Assert.Equal(new Name("TriggerOpenTimed"), mover.Props.GetName("InitialState"));
        Assert.True(mover.Props.GetBool("bBlockPlayers"));
        Assert.False(mover.Props.GetBool("bHidden"));
        Assert.Equal("TestDoor", mover.Tag);

        AssetRef mesh = mover.Props.GetAsset("StaticMesh")!;
        Assert.Equal("TestBox.Meshes.SlidingDoor", mesh.Path);
        Assert.Equal("TestBox", mesh.Package);
        Assert.Equal("Meshes", mesh.Group);
        Assert.Equal("SlidingDoor", mesh.Name);
        Assert.True(mesh.IsInMap(doc.Map));

        PropStaticArray keys = mover.Props.GetStaticArray("KeyPos")!;
        Assert.Equal(2, keys.Count);
        Assert.Equal(1, keys.MaxIndex);
        Assert.Equal(new FVector(0f, 0f, 240f), keys.Get(1).AsVector());
        Assert.False(keys.TryGet(2, out _));

        ActorRecord flag = doc.FindActor("SFlag0")!;
        ActorRef drop = flag.Props.GetActorRef("DropZone")!;
        Assert.Equal("SFlagDropZone0", drop.Id);
        Assert.Same(doc.FindActor("SFlagDropZone0"), drop.Target);
        Assert.Same(drop.Target, flag.Props.GetActor("DropZone"));
        Assert.Equal(15, flag.Props.GetInt("ResetTime"));

        IReadOnlyList<PropValue> objectives = doc.FindActor("SMission0")!.Props.GetArray("Objectives");
        Assert.Equal(3, objectives.Count);
        Assert.Equal("SObjective2", objectives[2].AsActor().Target!.Id);

        var e = Assert.Throws<InvalidOperationException>(() => mover.Props.GetInt("MoveTime"));
        Assert.Contains("MoveTime", e.Message, StringComparison.Ordinal);
        Assert.Equal(7, mover.Props.GetInt("Missing", 7));
    }

    [Fact]
    public void TagIndexMatchesIgnoringCaseInLevelOrder()
    {
        GameplayDoc doc = TestBox();
        IReadOnlyList<ActorRecord> starts = doc.ActorsWithTag("playerstart");
        Assert.Equal(["PlayerStart0", "PlayerStart1", "PlayerStart2", "PlayerStart3"], Ids(starts));
        Assert.Equal(["LevelInfo0"], Ids(doc.ActorsWithTag("LevelInfo")));
        Assert.Empty(doc.ActorsWithTag("NoSuchTag"));
    }

    [Fact]
    public void EventEdgesAndMissionTree()
    {
        GameplayDoc doc = TestBox();
        EventEdge door = doc.EventEdges[0];
        Assert.Equal("Trigger0", door.From);
        Assert.Equal("Event", door.Via);
        Assert.Equal("TestDoor", door.Event);
        Assert.Equal(["Mover0"], door.To);
        Assert.Equal("TestDoor", doc.FindActor("Trigger0")!.Event);

        MissionNode mission = doc.MissionTree[0];
        Assert.Equal("SMission0", mission.Mission);
        Assert.Equal(5, mission.GameMode);
        Assert.Equal(2, mission.MinimumObjectives);
        Assert.Equal(3, mission.Objectives.Count);
        Assert.Equal("SObjective2", mission.Objectives[2].Objective);
        Assert.Equal(["SFlag0"], mission.Objectives[2].Triggers);
    }

    [Fact]
    public void DerivedIndexesRebuildToTheStoredOnes()
    {
        GameplayDoc doc = TestBox();
        Assert.Empty(doc.CheckDerived());
        Assert.Empty(doc.CheckReferences());
    }

    [Fact]
    public void StaleDerivedIndexesAreReported()
    {
        string text = Encoding.UTF8.GetString(TestPaths.TestBoxFile("sim/gameplay.json"));
        string stale = text.Replace("\"Event\": \"TestDoor\"", "\"Event\": \"OtherDoor\"", StringComparison.Ordinal);
        GameplayDoc doc = GameplayDoc.Parse(Encoding.UTF8.GetBytes(stale));
        string problem = Assert.Single(doc.CheckDerived());
        Assert.StartsWith("eventEdges do not match the actors' event properties: first difference at eventEdges[0]", problem, StringComparison.Ordinal);
        Assert.Contains("stored Trigger0.Event -> TestDoor [Mover0]", problem, StringComparison.Ordinal);
        Assert.Contains("derived Trigger0.Event -> OtherDoor []", problem, StringComparison.Ordinal);
        Assert.Empty(doc.ComputeEventEdges()[0].To);
    }

    [Fact]
    public void ToJsonRoundTripsCanonically()
    {
        byte[] file = TestPaths.TestBoxFile("sim/gameplay.json");
        GameplayDoc doc = GameplayDoc.Parse(file);
        Assert.Equal(CanonicalJson.Canonicalize(file), CanonicalJson.ToCanonicalBytes(doc.ToJson()));
        Assert.Equal(file, CanonicalJson.ToFileBytes(doc.ToJson()));
    }

    [Theory]
    [InlineData("\"id\": \"Mover0\"", "\"id\": \"zoneinfo0\"", "duplicate actor id")]
    [InlineData("\"GEActivationMask\": 4,", "", "GEActivationMask")]
    [InlineData("\"GEActivationMask\": 4", "\"GEActivationMask\": 256", "GEActivationMask")]
    [InlineData("\"NumKeys\": 2", "\"NumKeys\": 4294967296", "outside the int range")]
    [InlineData("{\"$actor\": \"SFlagDropZone0\"}", "{\"$actor\": \"SFlagDropZone0\", \"x\": 1}", "exactly one member")]
    [InlineData("{\"$asset\": \"TestBox.Meshes.SlidingDoor\"}", "{\"$asset\": \"SlidingDoor\"}", "not an asset path")]
    [InlineData("\"MoveTime\": 1.0", "\"MoveTime\": 1.0, \"movetime\": 2.0", "given twice")]
    [InlineData("\"formatVersion\": \"1.0.0\"", "\"formatVersion\": \"2.0.0\"", "not an SVMap 1.x version")]
    [InlineData("\"class\": \"Engine.Mover\"", "\"class\": \"Mover\"", "not Package.Class")]
    [InlineData("\"0\": {\"X\": 0.0", "\"00\": {\"X\": 0.0", "decimal element index")]
    public void MalformedDocumentsAreRejected(string from, string to, string reason)
    {
        string text = Encoding.UTF8.GetString(TestPaths.TestBoxFile("sim/gameplay.json"));
        Assert.Contains(from, text, StringComparison.Ordinal);
        byte[] broken = Encoding.UTF8.GetBytes(ReplaceFirst(text, from, to));
        var e = Assert.Throws<SvMapFormatException>(() => GameplayDoc.Parse(broken));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
        Assert.Equal("sim/gameplay.json", e.File);
    }

    [Fact]
    public void DanglingReferencesResolveToNullAndAreReported()
    {
        string text = Encoding.UTF8.GetString(TestPaths.TestBoxFile("sim/gameplay.json"));
        GameplayDoc doc = GameplayDoc.Parse(Encoding.UTF8.GetBytes(ReplaceFirst(text, "{\"$actor\": \"SFlagDropZone0\"}", "{\"$actor\": \"Nobody\"}")));
        ActorRef r = doc.FindActor("SFlag0")!.Props.GetActorRef("DropZone")!;
        Assert.True(r.IsDangling);
        Assert.Null(r.Target);
        Assert.Equal(["SFlag0 refers to unknown actor Nobody"], doc.CheckReferences());
    }

    [Fact]
    public void PropValueEqualityIsByContent()
    {
        GameplayDoc a = TestBox();
        GameplayDoc b = TestBox();
        for (int i = 0; i < a.AllActors.Count; i++)
        {
            Assert.True(a.AllActors[i].Props.ContentEquals(b.AllActors[i].Props));
            foreach (KeyValuePair<string, PropValue> p in a.AllActors[i].Props)
            {
                Assert.Equal(p.Value, b.AllActors[i].Props[p.Key]);
                Assert.Equal(p.Value.GetHashCode(), b.AllActors[i].Props[p.Key].GetHashCode());
            }
        }

        Assert.NotEqual(PropValue.FromFloat(0f), PropValue.FromFloat(-0f));
        Assert.NotEqual(PropValue.FromInt(1), PropValue.FromFloat(1f));
    }

    private static List<string> Ids(IReadOnlyList<ActorRecord> records)
    {
        var ids = new List<string>();
        foreach (ActorRecord r in records)
        {
            ids.Add(r.Id);
        }

        return ids;
    }

    private static string ReplaceFirst(string text, string from, string to)
    {
        int at = text.IndexOf(from, StringComparison.Ordinal);
        return text[..at] + to + text[(at + from.Length)..];
    }
}
