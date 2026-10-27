using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenScct.Content.Json;
using OpenScct.Core;
using Xunit;

namespace OpenScct.Content.Tests;

/// <summary>SVMap 1.1 gameplay documents: animations, the 1.1 event-edge rules, mission triggers and their round trip.</summary>
public sealed class GameplayDoc11Tests
{
    /// <summary>A small 1.1 document laid out like the SteeE crane chain, with empty derived indexes.</summary>
    internal const string Synth = """
        {
          "formatVersion": "1.1.0",
          "map": "Synth",
          "levelInfo": {"id": "LevelInfo0", "class": "Engine.LevelInfo", "props": {"Tag": "LevelInfo", "GEActivationMask": 4}},
          "mapSettings": {"Story": {"TimeLimit": 10, "SpiesLives": 4, "SentriesLives": 3, "RespawnTime": 10}},
          "actors": [
            {"id": "Switch0", "class": "SBase.SInterrupteur", "props": {"Tag": "SInterrupteur", "Event": "go"}},
            {"id": "Crane0", "class": "SBase.SAnimatedMesh", "props": {"Tag": "go", "Mesh": {"$asset": "Anims.CraneMesh"}, "Animations": ["seq", "missing", 3]}},
            {"id": "Wall0", "class": "SBase.SGamePlayObject", "props": {
              "Tag": "boom", "DestroyOnEventFrom": [{"$actor": "Crane0"}, null, {"$asset": "Synth.Deco1"}],
              "DamagedMeshes": [{"Event": "dust", "Percent": 0}, {"Event": "None"}]}},
            {"id": "Dust0", "class": "Engine.Emitter", "props": {"Tag": "DUST"}},
            {"id": "Shaker0", "class": "SBase.SShaker", "props": {"Tag": "boom", "ShakeEvents": [{"EventSender": {"$actor": "Crane0"}}]}},
            {"id": "Magic0", "class": "SBase.SMagicEvent", "props": {"Tag": "Magic", "Groups": [
              {"EventGroup": [{"Event": "dust", "Delay": 1.0}, {"Event": "boom", "Delay": 2.0}]}]}},
            {"id": "Multi0", "class": "SBase.SThing", "props": {"Events": ["go", "", "boom"], "OnResetEvent": "dust", "event": "go",
              "Event2": "go", "TriggerEvent": {"$elements": {"3": "boom", "1": "go"}}, "EventFromActor": {"$actor": "Switch0"}}},
            {"id": "Mission0", "class": "SBase.SMission", "props": {"Tag": "SMission", "GameMode": 4, "Triggers": [{"$actor": "Dust0"}],
              "Objectives": [{"$actor": "Objective0"}]}},
            {"id": "Mission1", "class": "SBase.SMission", "props": {"Tag": "SMission", "Objectives": []}},
            {"id": "Objective0", "class": "SBase.SObjective", "props": {"Tag": "SObjective", "Triggers": [{"$actor": "Switch0"}]}}
          ],
          "animations": {"Anims.CraneMesh": {"animation": "Anims.CraneAnim", "sequences": {"seq": {
            "frames": 451, "rate": 40.0, "startFrame": 0, "groups": [], "notifies": [
              {"time": 0.97223943, "function": "None", "object": "Anims.Notify1", "class": "Engine.AnimNotify_Trigger", "props": {"EventName": "boom"}},
              {"time": 0.1, "function": "None", "object": "Anims.Sound1", "class": "Engine.AnimNotify_Sound", "props": {"Volume": 4.0}},
              {"time": 0.5, "props": {"EventName": "dust"}}]}}}},
          "tagIndex": {},
          "eventEdges": [],
          "missionTree": []
        }
        """;

    private static GameplayDoc Doc(string text = Synth) => GameplayDoc.Parse(Encoding.UTF8.GetBytes(text));

    private static (string From, string Via, string Event, string To, EventEdgeKind Kind) Key(EventEdge e) =>
        (e.From, e.Via, e.Event, string.Join(",", e.To), e.EdgeKind);

    [Fact]
    public void AnimationsAreRead()
    {
        GameplayDoc doc = Doc();
        Assert.Equal(1, doc.FormatMinor);
        Assert.True(doc.HasAnimations);
        AnimationSet set = Assert.Single(doc.Animations);
        Assert.Same(set, doc.FindAnimationSet("anims.cranemesh"));
        Assert.Equal("Anims.CraneAnim", set.Animation);
        AnimSequenceInfo seq = set.FindSequence("SEQ")!;
        Assert.Equal((451, 40f, 0), (seq.Frames, seq.Rate, seq.StartFrame));
        Assert.Equal(11.25f, seq.OneShotSeconds);
        Assert.Equal(3, seq.Notifies.Count);
        Assert.Equal("boom", seq.Notifies[0].Props.GetString("EventName"));
        Assert.Equal("Engine.AnimNotify_Trigger", seq.Notifies[0].Class);
        Assert.Null(seq.Notifies[2].Object);
        Assert.Null(doc.FindAnimationSet("Anims.Other"));
    }

    [Fact]
    public void EventEdgesFollowThe11Rules()
    {
        GameplayDoc doc = Doc();
        List<EventEdge> edges = doc.ComputeEventEdges();
        Assert.Equal(
            [
                ("Switch0", "Event", "go", "Crane0", EventEdgeKind.Fire),
                ("Crane0", "Animations[0]", "boom", "Wall0,Shaker0", EventEdgeKind.Notify),
                ("Crane0", "Animations[0]", "dust", "Dust0", EventEdgeKind.Notify),
                ("Wall0", "DamagedMeshes[0].Event", "dust", "Dust0", EventEdgeKind.Fire),
                ("Wall0", "DestroyOnEventFrom[0]", "boom", "Crane0", EventEdgeKind.Accept),
                ("Shaker0", "ShakeEvents[0].EventSender", "boom", "Crane0", EventEdgeKind.Accept),
                ("Magic0", "Groups[0].EventGroup[0].Event", "dust", "Dust0", EventEdgeKind.Fire),
                ("Magic0", "Groups[0].EventGroup[1].Event", "boom", "Wall0,Shaker0", EventEdgeKind.Fire),
                ("Multi0", "EventFromActor", "None", "Switch0", EventEdgeKind.Accept),
                ("Multi0", "Events[0]", "go", "Crane0", EventEdgeKind.Fire),
                ("Multi0", "Events[2]", "boom", "Wall0,Shaker0", EventEdgeKind.Fire),
                ("Multi0", "OnResetEvent", "dust", "Dust0", EventEdgeKind.Fire),
                ("Multi0", "TriggerEvent[1]", "go", "Crane0", EventEdgeKind.Fire),
                ("Multi0", "TriggerEvent[3]", "boom", "Wall0,Shaker0", EventEdgeKind.Fire),
            ],
            edges.Select(Key).ToList());

        EventEdge notify = edges[1];
        Assert.Equal("seq", notify.Seq);
        Assert.Equal("Anims.Notify1", notify.Notify);
        Assert.Equal(AnimNotifyInfo.NotifySeconds(0.97223943f, 451, 40f), notify.Time);
        Assert.Equal(10.962f, notify.Time!.Value, 3);
        Assert.Equal(string.Empty, edges[2].Notify);
        Assert.All(edges.Where(e => e.EdgeKind != EventEdgeKind.Notify), e => Assert.Null(e.Time));
    }

    [Fact]
    public void Documents10KeepThe10Rules()
    {
        GameplayDoc doc = Doc(Synth.Replace("\"1.1.0\"", "\"1.0.0\"", StringComparison.Ordinal));
        Assert.Equal(0, doc.FormatMinor);
        Assert.Equal([("Switch0", "Event", "go", "Crane0", EventEdgeKind.Fire)], doc.ComputeEventEdges().Select(Key).ToList());
        Assert.All(doc.ComputeMissionTree(), m => Assert.Null(m.Triggers));
    }

    [Fact]
    public void MissionTreeHasTheMissionTriggers()
    {
        List<MissionNode> tree = Doc().ComputeMissionTree();
        Assert.Equal(2, tree.Count);
        Assert.Equal(("Mission0", 4, (int?)null), (tree[0].Mission, tree[0].GameMode, tree[0].MinimumObjectives));
        Assert.Equal(["Dust0"], tree[0].Triggers!);
        Assert.Equal(["Switch0"], Assert.Single(tree[0].Objectives).Triggers);
        Assert.Null(tree[1].Triggers);
        Assert.Empty(tree[1].Objectives);
    }

    [Fact]
    public void RebuiltIndexesRoundTripAndCheckClean()
    {
        GameplayDoc stale = Doc();
        Assert.Equal(3, stale.CheckDerived().Count); // the empty tag index, edges and missions
        GameplayDoc doc = stale.WithDerived();
        Assert.Empty(doc.CheckDerived());
        Assert.Equal(14, doc.EventEdges.Count);
        Assert.Equal(["Wall0", "Shaker0"], doc.ActorsWithTag("BOOM").Select(a => a.Id));

        byte[] canonical = CanonicalJson.ToCanonicalBytes(doc.ToJson());
        GameplayDoc again = GameplayDoc.Parse(CanonicalJson.ToFileBytes(doc.ToJson()));
        Assert.Equal(canonical, CanonicalJson.ToCanonicalBytes(again.ToJson()));
        Assert.Empty(again.CheckDerived());
        Assert.Equal(doc.EventEdges[1], again.EventEdges[1] with { To = doc.EventEdges[1].To });
        Assert.True(doc.EventEdges[1].ContentEquals(again.EventEdges[1]));
        Assert.True(again.MissionTree[0].ContentEquals(doc.MissionTree[0]));

        // A changed notify time is a derived difference.
        string text = Encoding.UTF8.GetString(CanonicalJson.ToFileBytes(doc.ToJson()));
        string moved = text.Replace("\"time\": 0.5", "\"time\": 0.25", StringComparison.Ordinal);
        Assert.NotEqual(text, moved);
        string problem = Assert.Single(GameplayDoc.Parse(Encoding.UTF8.GetBytes(moved)).CheckDerived());
        Assert.Contains("eventEdges[2]", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifySecondsIsFloat32()
    {
        Assert.Equal(10.27025f, AnimNotifyInfo.NotifySeconds(0.910887f, 451, 40f), 4);
        float t = 0.910887f;
        float expected = (t * 451f) / 40f;
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(AnimNotifyInfo.NotifySeconds(t, 451, 40f)));
        Assert.Equal(0f, AnimNotifyInfo.NotifySeconds(0.5f, 10, 0f));
        Assert.Equal(0f, AnimNotifyInfo.NotifySeconds(0.5f, 10, -1f));
    }

    [Theory]
    [InlineData("Event", true)]
    [InlineData("Events", true)]
    [InlineData("OpeningEvent", true)]
    [InlineData("XEvent", true)]
    [InlineData("event", false)]
    [InlineData("Event2", false)]
    [InlineData("SomeEvents", false)]
    public void EventKeysAreExact(string key, bool isEvent) => Assert.Equal(isEvent, GameplayDoc.IsEventKey(key));

    [Fact]
    public void MalformedAnimationsAreRejected()
    {
        var e = Assert.Throws<SvMapFormatException>(() => Doc(Synth.Replace("\"frames\": 451, ", "", StringComparison.Ordinal)));
        Assert.Contains("frames", e.Message, StringComparison.Ordinal);
        e = Assert.Throws<SvMapFormatException>(() => Doc(Synth.Replace("\"Anims.CraneMesh\": {\"animation\"", "\"CraneMesh\": {\"animation\"", StringComparison.Ordinal)));
        Assert.Contains("not an asset path", e.Message, StringComparison.Ordinal);
    }

    // ---- the pipeline's derivation on random documents -----------------------------------------------------

    private const string DeriveScript = """
        import json, sys
        from pathlib import Path
        sys.path.insert(0, sys.argv[1])
        from svmbuild import canonical, svmap

        req = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8"))
        out = [canonical.dumps(svmap.with_derived(canonical.loads(doc))) for doc in req]
        Path(sys.argv[3]).write_text(json.dumps(out), encoding="utf-8")
        """;

    [Fact]
    public void DerivedIndexesMatchThePipelineOnRandomDocuments()
    {
        string? python = PythonCrossCheckTests.FindPython();
        if (python is null)
        {
            if (Environment.GetEnvironmentVariable("SVM_REQUIRE_PYTHON") is { Length: > 0 })
            {
                Assert.Fail("SVM_REQUIRE_PYTHON is set but no Python with numpy was found.");
            }

            Assert.Skip("Python with numpy is not installed.");
        }

        var random = new SimRandom(20261023, stream: 2, purpose: 1);
        var docs = new List<string> { Synth };
        for (int i = 0; i < 400; i++)
        {
            docs.Add(RandomDocument(random, i % 7 == 0 ? "1.0.0" : "1.1.0"));
        }

        using var scratch = new ScratchDir();
        string request = scratch.File("request.json");
        string response = scratch.File("response.json");
        string scriptDir = Path.Combine(scratch.Path, "script");
        Directory.CreateDirectory(scriptDir);
        string script = Path.Combine(scriptDir, "derive.py");
        File.WriteAllBytes(request, CanonicalJson.ToCanonicalBytes(new SvJsonArray(docs.ConvertAll(SvJsonNode.String))));
        File.WriteAllText(script, DeriveScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        PythonCrossCheckTests.Run(python, [script, TestPaths.ContentPipeline, request, response], scratch.Path);
        var derived = (SvJsonArray)SvJson.Parse(File.ReadAllBytes(response));
        Assert.Equal(docs.Count, derived.Count);
        int edges = 0;
        for (int i = 0; i < docs.Count; i++)
        {
            GameplayDoc fromPython = GameplayDoc.Parse(Encoding.UTF8.GetBytes(((SvJsonString)derived[i]).Value));
            List<string> problems = fromPython.CheckDerived();
            Assert.True(problems.Count == 0, $"document {i}: {string.Join("; ", problems)}{Environment.NewLine}{docs[i]}");
            Assert.Equal(CanonicalJson.ToCanonicalBytes(fromPython.ToJson()), CanonicalJson.ToCanonicalBytes(Doc(docs[i]).ToJson(rebuildDerived: true)));
            edges += fromPython.EventEdges.Count;
        }

        Assert.True(edges > 1000, $"only {edges} edges in the random documents");
    }

    private static readonly string[] Tags = ["go", "GO", "boom", "Boom", "dust", "None", "", "Crane", "x_1"];
    private static readonly string[] Keys =
    [
        "Event", "Events", "OpenedEvent", "OnResetEvent", "event", "Event2", "SomeEvents", "Tag2", "Percent", "Delay",
        "DestroyOnEventFrom", "AlertOnEventFrom", "EventFromActor", "EventSender", "Group", "Mesh",
    ];

    private static string RandomDocument(SimRandom r, string version)
    {
        int n = 3 + r.Rand(10);
        var ids = new List<string> { "LevelInfo0" };
        for (int i = 0; i < n; i++)
        {
            ids.Add($"A{i}");
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{{\"formatVersion\": \"{version}\", \"map\": \"R\", \"mapSettings\": {{}}, \"tagIndex\": {{}}, \"eventEdges\": [], \"missionTree\": [], ");
        sb.Append("\"levelInfo\": {\"id\": \"LevelInfo0\", \"class\": \"Engine.LevelInfo\", \"props\": {\"GEActivationMask\": 0");
        if (r.Rand(2) == 0)
        {
            sb.Append(", \"Event\": \"").Append(Tags[r.Rand(Tags.Length)]).Append('"');
        }

        sb.Append("}}, \"actors\": [");
        string[] classes = ["SBase.SThing", "SBase.SMission", "SBase.SObjective", "SBase.SAnimatedMesh"];
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            string cls = classes[r.Rand(classes.Length)];
            sb.Append(CultureInfo.InvariantCulture, $"{{\"id\": \"A{i}\", \"class\": \"{cls}\", \"props\": {{");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var members = new List<string>();
            if (r.Rand(4) != 0)
            {
                used.Add("Tag");
                members.Add($"\"Tag\": \"{Tags[r.Rand(Tags.Length)]}\"");
            }

            if (cls == "SBase.SMission" || cls == "SBase.SObjective")
            {
                foreach (string k in new[] { "Objectives", "Triggers" })
                {
                    if (r.Rand(3) != 0)
                    {
                        used.Add(k);
                        members.Add($"\"{k}\": [{string.Join(", ", Enumerable.Range(0, r.Rand(4)).Select(_ => r.Rand(5) == 0 ? "null" : $"{{\"$actor\": \"{ids[r.Rand(ids.Count)]}\"}}"))}]");
                    }
                }

                if (r.Rand(2) == 0)
                {
                    used.Add("GameMode");
                    members.Add($"\"GameMode\": {r.Rand(8)}");
                }
            }

            if (cls == "SBase.SAnimatedMesh" && r.Rand(3) != 0)
            {
                used.Add("Mesh");
                used.Add("Animations");
                members.Add($"\"Mesh\": {{\"$asset\": \"{(r.Rand(4) == 0 ? "Anims.Other" : "Anims.Mesh")}\"}}");
                members.Add($"\"Animations\": [{string.Join(", ", Enumerable.Range(0, 1 + r.Rand(3)).Select(_ => r.Rand(5) == 0 ? "1" : $"\"{(r.Rand(3) == 0 ? "b" : "a")}\""))}]");
            }

            int extra = r.Rand(8);
            for (int k = 0; k < extra; k++)
            {
                string key = Keys[r.Rand(Keys.Length)];
                if (used.Add(key))
                {
                    members.Add($"\"{key}\": {RandomValue(r, ids, key, 0)}");
                }
            }

            sb.Append(string.Join(", ", members)).Append("}}");
        }

        sb.Append("], \"animations\": {\"Anims.Mesh\": {\"animation\": \"Anims.Anim\", \"sequences\": {");
        sb.Append(CultureInfo.InvariantCulture, $"\"a\": {{\"frames\": {1 + r.Rand(500)}, \"rate\": {(r.Rand(5) == 0 ? "0.0" : (1 + r.Rand(60)).ToString(CultureInfo.InvariantCulture) + ".5")}, \"notifies\": [");
        int notifies = r.Rand(4);
        for (int k = 0; k < notifies; k++)
        {
            if (k > 0)
            {
                sb.Append(", ");
            }

            string ev = r.Rand(4) == 0 ? "\"Volume\": 2.0" : $"\"EventName\": \"{Tags[r.Rand(Tags.Length)]}\"";
            sb.Append(CultureInfo.InvariantCulture, $"{{\"time\": {Json.Float32Text.Format(r.FRand())}, \"object\": \"Anims.N{k}\", \"props\": {{{ev}}}}}");
        }

        sb.Append("]}, \"b\": {\"frames\": 10, \"rate\": 30.0, \"notifies\": []}}}}}");
        return sb.ToString();
    }

    private static string RandomValue(SimRandom r, List<string> ids, string key, int depth)
    {
        int pick = r.Rand(depth > 2 ? 4 : 9);
        return pick switch
        {
            0 => $"\"{Tags[r.Rand(Tags.Length)]}\"",
            1 => $"{{\"$actor\": \"{ids[r.Rand(ids.Count)]}\"}}",
            2 => r.Rand(2) == 0 ? "null" : "{\"$asset\": \"R.Deco\"}",
            3 => r.Rand(2) == 0 ? "1.5" : "true",
            4 or 5 => "[" + string.Join(", ", Enumerable.Range(0, r.Rand(4)).Select(_ => RandomValue(r, ids, key, depth + 1))) + "]",
            6 => "{\"$elements\": {" + string.Join(", ", Enumerable.Range(0, 1 + r.Rand(3)).Select(i => $"\"{i * 2 + r.Rand(2)}\"").Distinct()
                .Select(i => $"{i}: {RandomValue(r, ids, key, depth + 1)}")) + "}}",
            _ => "{" + string.Join(", ", Enumerable.Range(0, 1 + r.Rand(3)).Select(_ => Keys[r.Rand(Keys.Length)]).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(k => $"\"{k}\": {RandomValue(r, ids, k, depth + 1)}")) + "}",
        };
    }
}
